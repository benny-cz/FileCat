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
    private AnalysisCapturedMemoryReport(AnalysisSnapshotLease source, AnalysisTree tree, Dictionary<ulong, AnalysisNode> nodes, AnalysisOrderedRows? order,
        ImmutableArray<ulong> selected, long reservation, DeviceIoScheduler io, AnalysisReportHeader header, AnalysisReportFormat format, AnalysisReportScope scope)
        : base(io, null, header, format)
    { _source = source; _tree = tree; _nodes = nodes; _order = order; _selected = selected; _reservation = reservation; _scope = scope; }

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
        if (scope == AnalysisReportScope.FilteredView && (published.GroupingDimension != AnalysisGroupingDimension.FolderHierarchy ||
            AnalysisTree.Other(folder, out _, out _) || !published.Items.TryGetValue(folder, out var shown) || shown.Kind != AnalysisKind.Directory || !shown.Visible))
            throw new InvalidOperationException("Choose a visible captured folder for a filtered-folder report.");
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
                var tree = AnalysisTree.Build(snapshot, projection, scope == AnalysisReportScope.FilteredView && published.FilterApplied, cancellation,
                    scope == AnalysisReportScope.FilteredView ? node => published.Items.GetValueOrDefault(AnalysisTree.Key(node.Id))?.Visible == true : null,
                    retainReportStatistics: true);
                var nodes = snapshot.Nodes.ToDictionary(node => node.Id);
                foreach (ulong id in selected) { cancellation.ThrowIfCancellationRequested(); if (!nodes.ContainsKey(id)) throw new InvalidDataException("A marked report path is absent from this captured revision."); }
                string capturedFolder = scope == AnalysisReportScope.FilteredView ? folder : AnalysisTree.RootKey;
                var parent = tree.Items[capturedFolder]; ulong rows; bool limited = false;
                if (scope == AnalysisReportScope.SelectedRows) rows = (ulong)selected.Length;
                else if (scope == AnalysisReportScope.EntireResult && format == AnalysisReportFormat.Csv) rows = (ulong)snapshot.Nodes.Length;
                else
                {
                    order = AnalysisTableOrder.Apply(tree.ChildrenOf(capturedFolder).Where(item => !item.Synthetic).ToImmutableArray(), sort, descending, cancellation);
                    rows = format == AnalysisReportFormat.Csv ? (ulong)order.Rows.Length : Math.Min((ulong)order.Rows.Length, (ulong)AnalysisLimits.ChartLeaves);
                    limited = rows < (ulong)order.Rows.Length || scope == AnalysisReportScope.EntireResult;
                }
                var header = new AnalysisReportHeader(tree.Metric, scope, snapshot.Identity.StartedUtc, snapshot.Identity.ObservedUtc, snapshot.Coverage,
                    snapshot.Freshness, snapshot.Taint, parent.ReportTotals!, parent.ReportLogical!,
                    scope == AnalysisReportScope.FilteredView ? tree.NameOf(capturedFolder) : "All captured locations", rows, limited)
                { SummaryDescribesSourceScope = scope == AnalysisReportScope.SelectedRows, NamespaceRowsOnly = true,
                    HasUnmeasuredEvidence = parent.Unknown, HasUnmeasuredLogicalEvidence = parent.LogicalUnknown,
                    MeasurementBases = projection.MeasurementBases, DetailedAllocation = snapshot.Policy.ComponentPreset == "detailed",
                    FilterContext = scope == AnalysisReportScope.FilteredView && published.FilterApplied
                        ? new(context?.Text ?? projection.FilterText, context?.CaseSensitive, projection.IncludesUnknownFilterResults, context is null ? null : context.RootId is not null) : null };
                var report = new AnalysisCapturedMemoryReport(retained, tree, nodes, order, selected, reservation, io, header, format, scope);
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
            var path = AnalysisPathMetadata.From(_nodes![item.NodeId!.Value]);
            yield return new(path, locations ? DisplayLocation(path, snapshot) : null, item.ReportTotals!, item.ReportLogical!, item.Unknown, item.SharedElsewhere, item.IsFilterContext)
                { HasUnmeasuredEvidence = item.Unknown, HasUnmeasuredLogicalEvidence = item.LogicalUnknown };
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
