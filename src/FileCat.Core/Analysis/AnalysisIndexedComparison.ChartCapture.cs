namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    internal sealed partial class Chart
    {
        internal Task<ChartCapture> CaptureScopeAsync(AnalysisIndexedComparisonChartProjection projection, CancellationToken cancellation = default) =>
            _owned.UseAsync(async _ =>
            {
                if (!projection.BelongsTo(_projectionIdentity)) throw new InvalidOperationException("Capture the chart of this exact comparison side.");
                using var admission = AnalysisExplorerAdmission.Acquire(); ReadLease? reader = _source.Retain();
                try
                {
                    string key = projection.Scope; int skipped = 0;
                    for (int depth = 0; AnalysisTree.Other(key, out int count, out string parent); depth++)
                    {
                        if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("Display aggregation nesting limit.");
                        skipped = checked(skipped + count); key = parent;
                    }
                    ulong folder = PathId(key); AnalysisTotals totals; AnalysisLogicalTotals logical; bool unknown, logicalUnknown; ulong rows;
                    var path = folder == 0 ? null : await _hierarchy.ReadAsync(folder, cancellation).ConfigureAwait(false);
                    string scope = folder == 0 ? "All captured locations" : await new AnalysisReportLineage(Descriptor,
                        (id, token) => _source.ReadPathAsync(Previous, id, token)).ResolveAsync(path!.Path, cancellation).ConfigureAwait(false);
                    if (skipped != 0)
                    {
                        await using var subset = await _hierarchy.CreateSubsetAsync(folder, skipped, cancellation).ConfigureAwait(false);
                        totals = subset.Totals; logical = subset.LogicalTotals; unknown = subset.HasUnmeasuredEvidence;
                        logicalUnknown = subset.HasUnmeasuredLogicalEvidence; rows = subset.GeometryCount; scope += " / Other small items";
                    }
                    else
                    {
                        if (folder == 0 && !CombinedAreaAvailable) throw new InvalidOperationException("Choose one recorded location with compatible allocation components.");
                        totals = path?.Totals ?? _hierarchy.Totals; logical = path?.Logical ?? _hierarchy.LogicalTotals;
                        unknown = path?.HasUnmeasuredEvidence ?? _hierarchy.HasUnmeasuredEvidence;
                        logicalUnknown = path?.HasUnmeasuredLogicalEvidence ?? _hierarchy.HasUnmeasuredLogicalEvidence;
                        rows = (await _hierarchy.ReadGeometryAsync(folder, maximum: 1, cancellation: cancellation).ConfigureAwait(false)).Total;
                    }
                    cancellation.ThrowIfCancellationRequested(); var context = _owner.ChartContext with { PreviousSide = Previous };
                    var descriptor = Descriptor;
                    var header = new AnalysisReportHeader(Metric: _hierarchy.Metric, Scope: AnalysisReportScope.FilteredView,
                        StartedUtc: descriptor.Identity.StartedUtc, ObservedUtc: descriptor.Identity.ObservedUtc, Coverage: descriptor.Coverage,
                        Freshness: descriptor.Freshness, Taint: context.Taint, Totals: totals, Logical: logical,
                        ScopeLabel: (Previous ? "Previous" : "Current") + " recorded amounts · " + AnalysisNames.Display(scope, 4096) + "; full side hierarchy; table filters excluded",
                        Rows: Math.Min(rows, (ulong)AnalysisLimits.ChartLeaves), DetailLimited: projection.DetailLimited || rows > AnalysisLimits.ChartLeaves)
                    { HasUnmeasuredEvidence = unknown, HasUnmeasuredLogicalEvidence = logicalUnknown, NamespaceRowsOnly = true };
                    var result = new ChartCapture(reader, header, context); reader = null; return result;
                }
                finally { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
            });
    }
    internal sealed class ChartCapture(ReadLease source, AnalysisReportHeader header, AnalysisComparisonChartContext context) : IAsyncDisposable
    {
        private readonly object _gate = new(); private Task? _closed;
        internal AnalysisReportHeader Header => header;
        internal AnalysisComparisonChartContext Context => context;
        public ValueTask DisposeAsync() { lock (_gate) return new(_closed ??= source.DisposeAsync().AsTask()); }
    }
}
