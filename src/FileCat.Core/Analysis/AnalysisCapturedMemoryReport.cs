using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Bounded memory report using the canonical projection/tree fold and an independently retained snapshot.</summary>
public sealed class AnalysisCapturedMemoryReport : AnalysisCapturedReport
{
    private const long Budget = AnalysisLimits.MemoryBytes / 4;
    private static long s_reserved;
    private long _reservation;
    private AnalysisSnapshotLease? _source;
    private AnalysisTree? _tree;
    private Dictionary<ulong, AnalysisNode>? _nodes;
    private AnalysisOrderedRows? _order;
    private ImmutableArray<ulong> _selected;
    private readonly AnalysisReportScope _scope;
    private readonly bool _referenceScope;
    private AnalysisCapturedMemoryReport(AnalysisSnapshotLease source, AnalysisTree tree, Dictionary<ulong, AnalysisNode> nodes, AnalysisOrderedRows? order,
        ImmutableArray<ulong> selected, long reservation, DeviceIoScheduler io, AnalysisReportHeader header, AnalysisReportFormat format, AnalysisReportScope scope, bool referenceScope)
        : base(io, null, header, format)
    { _source = source; _tree = tree; _nodes = nodes; _order = order; _selected = selected; _reservation = reservation; _scope = scope; _referenceScope = referenceScope; }

    public static async Task<AnalysisCapturedMemoryReport> CaptureAsync(AnalysisSnapshotLease source, AnalysisTree published, DeviceIoScheduler io,
        AnalysisReportFormat format, AnalysisReportScope scope, string folder = AnalysisTree.RootKey, ImmutableArray<ulong> selected = default,
        AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true, CancellationToken cancellation = default, AnalysisFilterRequest? context = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(published); ArgumentNullException.ThrowIfNull(io);
        if (!Enum.IsDefined(format) || !Enum.IsDefined(scope) || !Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!ReferenceEquals(source.Snapshot, published.Snapshot)) throw new InvalidOperationException("The displayed tree and report owner must share the exact captured snapshot.");
        if (selected.IsDefault) selected = [];
        if (selected.Length > AnalysisLimits.ReviewPaths || selected.Any(id => id == 0) || selected.Distinct().Count() != selected.Length)
            throw new InvalidDataException("Choose at most 100,000 distinct captured real paths.");
        if (scope == AnalysisReportScope.SelectedRows && format != AnalysisReportFormat.Csv && selected.Length > AnalysisLimits.ChartLeaves)
            throw new InvalidOperationException("Choose CSV for all marked paths or at most 5,000 human-report rows.");
        if (scope == AnalysisReportScope.FilteredView && !published.CanExplore(folder)) throw new InvalidOperationException("Choose a captured view that can be explored.");
        var navigation = AnalysisSavedCatalog.SplitAggregationScope(scope == AnalysisReportScope.FilteredView ? folder : AnalysisTree.RootKey);
        if (scope == AnalysisReportScope.FilteredView && (!published.Items.TryGetValue(navigation.Scope, out var shown) || !shown.Visible ||
            navigation.Skipped != 0 && navigation.Skipped >= published.ChildrenOf(navigation.Scope).Length))
            throw new InvalidOperationException("That captured display scope is absent from this revision.");
        AnalysisSnapshotLease? retained = source.Retain(); long reservation = 0; AnalysisOrderedRows? order = null;
        Exception? failure = null;
        try
        {
            var snapshot = retained.Snapshot;
            long cost = checked(65536L + snapshot.Objects.Length * 128L + snapshot.Facts.Length * 128L + selected.Length * 16L); int processed = 0;
            foreach (var node in snapshot.Nodes)
            {
                if ((processed++ & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                cost = checked(cost + 2048L + node.Name.Length * 2L + node.RawName.Length);
            }
            while (true)
            {
                long current = Volatile.Read(ref s_reserved);
                if (cost > Budget - current) throw new InvalidOperationException("The shared memory-report budget is in use or this result exceeds it. The analysis remains available; export the entire inventory instead.");
                if (Interlocked.CompareExchange(ref s_reserved, current + cost, current) == current) { reservation = cost; break; }
            }
            return await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var projection = scope == AnalysisReportScope.FilteredView ? published.Projection : AnalysisAggregate.Project(snapshot, published.Metric, cancellation: cancellation);
                Func<AnalysisNode, bool>? visibility = scope == AnalysisReportScope.FilteredView ? node => published.Items.GetValueOrDefault(AnalysisTree.Key(node.Id))?.Visible == true : null;
                bool grouped = scope == AnalysisReportScope.FilteredView && published.GroupingDimension != AnalysisGroupingDimension.FolderHierarchy;
                var tree = grouped ? AnalysisTree.BuildGrouped(snapshot, projection, published.GroupingDimension, published.FilterApplied, cancellation, visibility, retainReportStatistics: true)
                    : AnalysisTree.Build(snapshot, projection, scope == AnalysisReportScope.FilteredView && published.FilterApplied, cancellation,
                    scope == AnalysisReportScope.FilteredView ? node => published.Items.GetValueOrDefault(AnalysisTree.Key(node.Id))?.Visible == true : null,
                    retainReportStatistics: true);
                var nodes = snapshot.Nodes.ToDictionary(node => node.Id);
                foreach (ulong id in selected) { cancellation.ThrowIfCancellationRequested(); if (!nodes.ContainsKey(id)) throw new InvalidDataException("A marked report path is absent from this captured revision."); }
                string capturedFolder = scope == AnalysisReportScope.FilteredView ? folder : AnalysisTree.RootKey;
                var parent = tree.Items[navigation.Scope]; bool other = navigation.Skipped != 0;
                var children = tree.ChildrenOf(capturedFolder);
                bool referenceScope = parent.ReferenceLogical is not null;
                var totals = other ? AnalysisReportStatistics.Active(children) : parent.ReportTotals!;
                var logical = other ? AnalysisReportStatistics.Logical(children.Select(item => item.ReportLogical!)) : parent.ReferenceLogical ?? parent.ReportLogical!;
                if (other && grouped)
                {
                    // The exact Other group scope owns the union of its real
                    // members, including references whose entry group is outside
                    // the suffix. Fold each matching namespace path once.
                    var members = tree.CaptureMatchingFiles(capturedFolder, cancellation);
                    var objects = snapshot.Objects.ToDictionary(value => value.Id, StringComparer.Ordinal); var paths = new AnalysisPathStatistics();
                    foreach (ulong id in members.NodeIds)
                    {
                        cancellation.ThrowIfCancellationRequested(); var node = nodes[id];
                        paths.Add(node.ObjectId is { } objectId && objects.TryGetValue(objectId, out var value) ? value.LogicalLength : AnalysisMeasurement.Missing(MeasurementState.Unsupported));
                    }
                    logical = paths.Export(); totals = totals with { MatchingPaths = logical.Files };
                }
                ulong rows; bool limited = false;
                if (scope == AnalysisReportScope.EntireResult && format != AnalysisReportFormat.Csv) { sort = AnalysisTableSort.ActiveMetric; descending = true; }
                if (scope == AnalysisReportScope.SelectedRows) rows = (ulong)selected.Length;
                else if (scope == AnalysisReportScope.EntireResult && format == AnalysisReportFormat.Csv) rows = (ulong)snapshot.Nodes.Length;
                else
                {
                    order = AnalysisTableOrder.Apply(children.Where(item => item.NodeId is not null || item.GroupDefinition is not null).ToImmutableArray(), sort, descending, cancellation);
                    rows = format == AnalysisReportFormat.Csv ? (ulong)order.Rows.Length : Math.Min((ulong)order.Rows.Length, (ulong)AnalysisLimits.ChartLeaves);
                    limited = rows < (ulong)order.Rows.Length || scope == AnalysisReportScope.EntireResult;
                }
                var header = new AnalysisReportHeader(tree.Metric, scope, snapshot.Identity.StartedUtc, snapshot.Identity.ObservedUtc, snapshot.Coverage,
                    snapshot.Freshness, snapshot.Taint, totals, logical,
                    scope == AnalysisReportScope.FilteredView ? (other ? "Other small items in " : "") + tree.NameOf(navigation.Scope) : "All captured locations", rows, limited)
                { SummaryDescribesSourceScope = scope == AnalysisReportScope.SelectedRows, NamespaceRowsOnly = !grouped || navigation.Scope != AnalysisTree.RootKey,
                    IncludesAccountingReferences = grouped || referenceScope,
                    ViewKind = grouped ? navigation.Scope == AnalysisTree.RootKey ? other ? AnalysisReportViewKind.SmallGroups : AnalysisReportViewKind.GroupOverview
                        : other ? AnalysisReportViewKind.SmallGroupMembers : AnalysisReportViewKind.GroupMembers : other ? AnalysisReportViewKind.SmallItems : AnalysisReportViewKind.NamespaceRows,
                    TableOrder = scope == AnalysisReportScope.SelectedRows || scope == AnalysisReportScope.EntireResult && format == AnalysisReportFormat.Csv ? null : new(sort, descending),
                    FilterTimeZone = snapshot.Identity.TimeZoneId,
                    HasUnmeasuredEvidence = other ? children.Any(item => item.Unknown) : parent.Unknown, HasUnmeasuredLogicalEvidence = logical.UnknownFiles != 0 || (other ? children.Any(item => item.LogicalUnknown) : parent.LogicalUnknown),
                    MeasurementBases = projection.MeasurementBases, DetailedAllocation = snapshot.Policy.ComponentPreset == "detailed",
                    FilterContext = scope == AnalysisReportScope.FilteredView && published.FilterApplied
                        ? new(context?.Text ?? projection.FilterText, context?.CaseSensitive, projection.IncludesUnknownFilterResults, context is null ? null : context.RootId is not null)
                        { UsesPanelFilter = context?.PanelFilter is not null } : null };
                var report = new AnalysisCapturedMemoryReport(retained, tree, nodes, order, selected, reservation, io, header, format, scope, referenceScope);
                retained = null; order = null; reservation = 0; return report;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            Exception? closeFailure = null;
            try { order?.Dispose(); } catch (Exception error) { closeFailure = error; }
            try { retained?.Dispose(); } catch (Exception error) { closeFailure = closeFailure is null ? error : new AggregateException(closeFailure, error); }
            finally { if (reservation != 0) Interlocked.Add(ref s_reserved, -reservation); }
            if (closeFailure is not null) throw AnalysisCloseUnconfirmedException.From(failure is null ? closeFailure : new AggregateException(failure, closeFailure));
        }
    }
    protected override async IAsyncEnumerable<AnalysisReportRow> RowsAsync(bool locations, [EnumeratorCancellation] CancellationToken cancellation)
    {
        await Task.CompletedTask;
        var tree = _tree!; var snapshot = _source!.Snapshot; ulong count = 0;
        IEnumerable<AnalysisTreeItem> rows = _scope == AnalysisReportScope.SelectedRows ? _selected.Select(id => tree.Items[AnalysisTree.Key(id)])
            : _scope == AnalysisReportScope.EntireResult && Format == AnalysisReportFormat.Csv ? snapshot.Nodes.Select(node => tree.Items[AnalysisTree.Key(node.Id)])
            : _order!.Rows;
        foreach (var item in rows)
        {
            cancellation.ThrowIfCancellationRequested(); if (count++ == Header.Rows) yield break;
            if (item.GroupDefinition is not null)
            {
                var group = tree.Groups[item.Key];
                yield return AnalysisReportGroupRows.Group(new(group.Definition, group.Kind, item.ReportTotals!, item.ReportLogical!, (ulong)group.Members.Length, (ulong)group.Charges.Length)
                    { ReferenceLogical = item.ReferenceLogical ?? new(0, 0, ImmutableDictionary<MeasurementState, ulong>.Empty) });
                continue;
            }
            var path = AnalysisPathMetadata.From(_nodes![item.NodeId!.Value]);
            yield return new(path, locations ? DisplayLocation(path, snapshot) : null, item.ReportTotals!, item.ReportLogical!, item.ReportNamespaceIncomplete, item.SharedElsewhere, item.IsFilterContext)
                { HasUnmeasuredEvidence = item.Unknown, HasUnmeasuredLogicalEvidence = item.LogicalUnknown, IsAccountingReference = _referenceScope || item.ReportAccountingReference,
                    LogicalNotApplicable = tree.GroupingDimension != AnalysisGroupingDimension.FolderHierarchy && item.Kind != AnalysisKind.File };
        }
    }
    private string DisplayLocation(AnalysisPathMetadata path, AnalysisSnapshot snapshot)
    {
        var names = new List<string>(); var current = _nodes![path.Id]; int length = 0;
        for (int depth = 0; current.ParentId != 0; depth++)
        {
            if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("The captured report lineage exceeds its bound.");
            string name = AnalysisNames.DisplayName(current); length = checked(length + name.Length + 1);
            if (length > AnalysisLimits.LocatorBytes) return "[Displayed location exceeds the report bound; use the full inventory for raw names.]";
            names.Add(name); current = _nodes[current.ParentId];
            if (current.RootId != path.RootId) throw new InvalidDataException("The captured report lineage changed roots.");
        }
        names.Reverse(); var root = snapshot.Roots.Single(value => value.Id == path.RootId);
        return AnalysisReportLocations.Append(root, names);
    }
    protected override ValueTask ReleaseAsync()
    {
        var source = _source; _source = null; _tree = null; _nodes = null; _selected = [];
        try { _order?.Dispose(); } finally
        {
            _order = null;
            try { source?.Dispose(); } finally { long bytes = Interlocked.Exchange(ref _reservation, 0); if (bytes != 0) Interlocked.Add(ref s_reserved, -bytes); }
        }
        return ValueTask.CompletedTask;
    }
}
