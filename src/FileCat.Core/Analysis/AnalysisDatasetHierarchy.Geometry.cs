using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetHierarchy
{
    /// <summary>Geometry-ranked real rows and accounting buckets, independent of table sorting.</summary>
    public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadGeometryAsync(ulong parent, ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => GeometryPage(state, parent, offset, maximum, token), cancellation);

    private static AnalysisDatasetPage<AnalysisTreeItem> GeometryPage(State state, ulong parent, ulong offset, int maximum, CancellationToken token)
    {
        state.Graph.RequireParent(parent, token); var index = state.Indexes[8]; var range = index.PrefixRange(Key(parent), token);
        if (offset > range.Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
        var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>(); long bytes = 0;
        while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
        {
            token.ThrowIfCancellationRequested(); ulong source = index.Read(range.First + offset + (ulong)rows.Count, token).Value;
            var item = state.GeometryItem(source, token); long cost = 1024L + item.Name.Length * 2L;
            if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break;
            rows.Add(item); bytes += cost;
        }
        return new(offset, range.Count, rows.ToImmutable());
    }

    private sealed partial class State
    {
        internal AnalysisTreeItem GeometryItem(ulong source, CancellationToken token) => (source & AnalysisGeometryOrder.BucketToken) != 0
            ? AnalysisGeometryOrder.Bucket(ReadBucket(source & ~AnalysisGeometryOrder.BucketToken, token), Metric)
            : AnalysisIndexedChart.Row(Read(source, token), Metric);

        internal State Retain()
        {
            var graph = Graph.Retain(); State? clone = null;
            try
            {
                clone = new(graph, null!, null!, null!, Metric, Compatible)
                    { _rows = _rows!.Retain(), Total = Total, TotalLogical = TotalLogical, FilterSummary = FilterSummary, Finished = true };
                clone._buckets = _buckets!.Retain();
                foreach (var index in Indexes) clone.Indexes.Add(index.Retain());
                clone.Placements = Placements?.Retain(); return clone;
            }
            catch { if (clone is not null) clone.Dispose(); else graph.Dispose(); throw; }
        }
    }
}
