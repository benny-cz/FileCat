using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>A retained immutable dataset/hierarchy and declared report scope. No recorded location is opened.</summary>
public sealed class AnalysisCapturedDatasetReport : AnalysisCapturedReport
{
    private readonly AnalysisDatasetLease _source;
    private readonly AnalysisDatasetHierarchy _hierarchy;
    private readonly AnalysisReportScope _scope;
    private readonly AnalysisReportFormat _format;
    private readonly ulong _folder;
    private readonly ImmutableArray<ulong> _selected;
    private readonly AnalysisHierarchySort _sort;
    private readonly bool _descending;
    private AnalysisCapturedDatasetReport(AnalysisDatasetLease source, AnalysisDatasetHierarchy hierarchy, DeviceIoScheduler io, AnalysisIoDevices devices,
        AnalysisReportHeader header, AnalysisReportScope scope, AnalysisReportFormat format, ulong folder, ImmutableArray<ulong> selected, AnalysisHierarchySort sort, bool descending)
        : base(io, devices, header, format)
    { _source = source; _hierarchy = hierarchy; _scope = scope; _format = format; _folder = folder; _selected = selected; _sort = sort; _descending = descending; }

    internal static async Task<AnalysisCapturedDatasetReport> CreateAsync(AnalysisDatasetLease source, DeviceIoScheduler io, AnalysisIoDevices devices,
        AnalysisMetric metric, AnalysisReportFormat format, AnalysisReportScope scope, AnalysisFilterRequest filter, ulong folder,
        ImmutableArray<ulong> selected, string scopeLabel, AnalysisHierarchySort sort, bool descending, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(filter); ArgumentNullException.ThrowIfNull(scopeLabel);
        if (!Enum.IsDefined(metric) || !Enum.IsDefined(format) || !Enum.IsDefined(scope) || !Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (scopeLabel.Length > AnalysisLimits.LocatorBytes) throw new InvalidDataException("The report scope label exceeds its bound.");
        if (selected.IsDefault) selected = [];
        if (selected.Length > AnalysisLimits.ReviewPaths || selected.Any(id => id == 0) || selected.Distinct().Count() != selected.Length)
            throw new InvalidDataException("A report selection must contain at most 100,000 distinct captured real paths.");
        if (scope == AnalysisReportScope.SelectedRows && format != AnalysisReportFormat.Csv && selected.Length > AnalysisLimits.ChartLeaves)
            throw new InvalidOperationException("Use CSV to export every marked row, or select at most 5,000 rows for a human report.");
        AnalysisDatasetLease? retained = await source.RetainAsync(cancellation).ConfigureAwait(false); AnalysisDatasetHierarchy? hierarchy = null;
        Exception? failure = null;
        try
        {
            hierarchy = scope == AnalysisReportScope.FilteredView
                ? await retained.BuildHierarchyAsync(metric, filter, cancellation).ConfigureAwait(false)
                : await retained.BuildHierarchyAsync(metric, cancellation).ConfigureAwait(false);
            if (scope != AnalysisReportScope.FilteredView)
            { folder = 0; scopeLabel = scope == AnalysisReportScope.EntireResult ? "All captured locations" : "All captured locations; selected real paths"; }
            var parent = folder == 0 ? null : await hierarchy.ReadAsync(folder, cancellation).ConfigureAwait(false);
            if (parent is not null && (parent.Path.Kind != AnalysisKind.Directory || !parent.IsVisible)) throw new InvalidOperationException("Choose a visible captured folder for this report.");
            ulong rows; bool limited = false;
            if (scope == AnalysisReportScope.SelectedRows) rows = (ulong)selected.Length;
            else if (scope == AnalysisReportScope.EntireResult && format == AnalysisReportFormat.Csv) rows = retained.Descriptor.Paths;
            else
            {
                var first = await hierarchy.ReadChildrenAsync(folder, sort, 0, 1, cancellation, descending).ConfigureAwait(false);
                rows = format == AnalysisReportFormat.Csv ? first.Total : Math.Min(first.Total, (ulong)AnalysisLimits.ChartLeaves);
                limited = first.Total > rows || scope == AnalysisReportScope.EntireResult;
            }
            var descriptor = retained.Descriptor;
            var header = new AnalysisReportHeader(metric, scope, descriptor.Identity.StartedUtc, descriptor.Identity.ObservedUtc, descriptor.Coverage,
                descriptor.Freshness, descriptor.Taint, parent?.Totals ?? hierarchy.Totals, parent?.Logical ?? hierarchy.LogicalTotals, scopeLabel, rows, limited)
                { SummaryDescribesSourceScope = scope == AnalysisReportScope.SelectedRows, NamespaceRowsOnly = true,
                    HasUnmeasuredEvidence = parent?.HasUnmeasuredEvidence ?? hierarchy.HasUnmeasuredEvidence,
                    HasUnmeasuredLogicalEvidence = parent?.HasUnmeasuredLogicalEvidence ?? hierarchy.HasUnmeasuredLogicalEvidence,
                    MeasurementBases = hierarchy.MeasurementBases, DetailedAllocation = descriptor.Policy.ComponentPreset == "detailed",
                    FilterContext = scope == AnalysisReportScope.FilteredView && (!string.IsNullOrWhiteSpace(filter.Text) || filter.RootId is not null || filter.UnknownOnly)
                        ? new(filter.Text, filter.CaseSensitive, filter.UnknownOnly, filter.RootId is not null) : null };
            var result = new AnalysisCapturedDatasetReport(retained, hierarchy, io, devices, header, scope, format, folder, selected, sort, descending);
            retained = null; hierarchy = null; return result;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            Exception? closeFailure = null;
            try { if (hierarchy is not null) await hierarchy.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { closeFailure = error; }
            try { if (retained is not null) await retained.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { closeFailure = closeFailure is null ? error : new AggregateException(closeFailure, error); }
            if (closeFailure is not null) throw new AnalysisCloseUnconfirmedException(failure is null ? closeFailure : new AggregateException(failure, closeFailure));
        }
    }

    protected override async IAsyncEnumerable<AnalysisReportRow> RowsAsync(bool locations, [EnumeratorCancellation] CancellationToken cancellation)
    {
        ulong written = 0;
        if (_scope == AnalysisReportScope.SelectedRows)
        {
            foreach (ulong id in _selected)
            { cancellation.ThrowIfCancellationRequested(); yield return await RowAsync(await _hierarchy.ReadAsync(id, cancellation).ConfigureAwait(false), locations, cancellation).ConfigureAwait(false); }
        }
        else if (_scope == AnalysisReportScope.EntireResult && _format == AnalysisReportFormat.Csv)
        {
            ulong offset = 0;
            while (offset < _source.Descriptor.Paths)
            {
                var page = await _source.ReadPathsAsync(offset, cancellation: cancellation).ConfigureAwait(false);
                if (page.Rows.IsEmpty) throw new InvalidDataException("The captured report path page made no progress.");
                foreach (var path in page.Rows)
                    yield return await RowAsync(await _hierarchy.ReadAsync(path.Id, cancellation).ConfigureAwait(false), locations, cancellation).ConfigureAwait(false);
                offset = page.NextOffset;
            }
        }
        else
        {
            ulong offset = 0;
            while (written < Header.Rows)
            {
                var page = await _hierarchy.ReadChildrenAsync(_folder, _sort, offset, cancellation: cancellation, descending: _descending).ConfigureAwait(false);
                if (page.Rows.IsEmpty) throw new InvalidDataException("The captured folder report made no progress.");
                foreach (var row in page.Rows)
                {
                    if (written >= Header.Rows) yield break; written++;
                    yield return await RowAsync(row, locations, cancellation).ConfigureAwait(false);
                }
                offset = page.NextOffset;
            }
        }
    }
    private async Task<AnalysisReportRow> RowAsync(AnalysisHierarchyRow row, bool locations, CancellationToken cancellation) =>
        new(row.Path, locations ? await DisplayLocationAsync(row.Path, cancellation).ConfigureAwait(false) : null,
            row.Totals, row.Logical, row.NamespaceIncomplete, row.HasChargesElsewhere, row.IsFilterContext)
            { HasUnmeasuredEvidence = row.HasUnmeasuredEvidence, HasUnmeasuredLogicalEvidence = row.HasUnmeasuredLogicalEvidence };
    private async Task<string> DisplayLocationAsync(AnalysisPathMetadata path, CancellationToken cancellation)
    {
        var names = new List<string>(); var current = path; int chars = 0;
        for (int depth = 0; current.ParentId != 0; depth++)
        {
            if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("The captured report lineage exceeds its depth bound.");
            string name = AnalysisNames.DisplayName(current); chars = checked(chars + name.Length + 1);
            if (chars > AnalysisLimits.LocatorBytes) return "[Displayed location exceeds the report bound; use the full inventory for raw names.]";
            names.Add(name); current = await _source.ReadPathAsync(current.ParentId, cancellation).ConfigureAwait(false);
            if (current.RootId != path.RootId) throw new InvalidDataException("The captured report lineage changed roots.");
        }
        var root = _source.Descriptor.Roots.Single(value => value.Id == path.RootId); names.Reverse();
        return AnalysisReportLocations.Append(root, names);
    }
    protected override async ValueTask ReleaseAsync()
    {
        Exception? failure = null;
        try { await _hierarchy.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
        try { await _source.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) throw AnalysisCloseUnconfirmedException.From(failure);
    }

}

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Retains this exact revision and canonical accounting for a declared data-only report scope.</summary>
    public Task<AnalysisCapturedDatasetReport> CaptureReportAsync(AnalysisMetric metric, AnalysisReportFormat format, AnalysisReportScope scope,
        AnalysisFilterRequest filter, ulong folder = 0, ImmutableArray<ulong> selected = default, string scopeLabel = "Captured locations",
        AnalysisHierarchySort sort = AnalysisHierarchySort.Metric, bool descending = true, CancellationToken cancellation = default) =>
        AnalysisCapturedDatasetReport.CreateAsync(this, _io, _device, metric, format, scope, filter, folder, selected, scopeLabel, sort, descending, cancellation);
}
