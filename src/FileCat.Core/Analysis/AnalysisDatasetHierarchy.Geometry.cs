using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetHierarchy
{
    internal Task<AnalysisDatasetHierarchy> RetainAsync(CancellationToken cancellation) =>
        _owned.RunAsync((state, _) => RetainedHierarchy(state, Descriptor, _io, _device), cancellation);
    private static AnalysisDatasetHierarchy RetainedHierarchy(State source, AnalysisDatasetDescriptor descriptor,
        FileCat.Core.Threading.DeviceIoScheduler io, AnalysisIoDevices device)
    {
        var retained = source.Retain();
        try { return new(retained, descriptor, io, device); }
        catch { AnalysisIo.CloseOnWorker(retained); throw; }
    }
    private static AnalysisHierarchyRow ReadPath(State state, ulong path, CancellationToken token)
    {
        _ = state.Graph.PathRow(path, token); var range = state.Indexes[0].FindRange(Key(path), token);
        if (range.Count != 1) throw Invalid(); return state.Read(state.Indexes[0].Read(range.First, token).Value, token);
    }
    /// <summary>Geometry-ranked real rows and accounting buckets, independent of table sorting.</summary>
    public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadGeometryAsync(ulong parent, ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => GeometryPage(state, parent, offset, maximum, token), cancellation);

    private static AnalysisDatasetPage<AnalysisTreeItem> GeometryPage(State state, ulong parent, ulong offset, int maximum, CancellationToken token)
    {
        if (parent == 0 && !state.Compatible) throw new InvalidOperationException("These locations report different allocation components. Choose one location to explore On disk.");
        state.Graph.RequireParent(parent, token); var index = state.Indexes[8]; var range = index.PrefixRange(Key(parent), token);
        if (offset > range.Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
        var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>(); long bytes = 0;
        while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
        {
            token.ThrowIfCancellationRequested(); ulong source = index.Read(range.First + offset + (ulong)rows.Count, token).Value;
            var item = state.GeometryItem(source, token, out long cost);
            if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break;
            rows.Add(item); bytes += cost;
        }
        return new(offset, range.Count, rows.ToImmutable());
    }

    private sealed partial class State
    {
        internal AnalysisTreeItem GeometryItem(ulong source, CancellationToken token) => GeometryItem(source, token, out _);
        internal AnalysisTreeItem GeometryItem(ulong source, CancellationToken token, out long bytes)
        {
            if ((source & AnalysisGeometryOrder.BucketToken) != 0)
            { bytes = 2048; return AnalysisGeometryOrder.Bucket(ReadBucket(source & ~AnalysisGeometryOrder.BucketToken, token), Metric); }
            var row = Read(source, token, out bytes); var item = AnalysisIndexedChart.Row(row, Metric);
            // Account for the captured record decoded to create this small projection,
            // including bounded object metadata, not only its shortened display label.
            bytes += 512L + item.Name.Length * 2L + (item.NameEvidence?.Length ?? 0) * 2L;
            return item;
        }

        internal State Retain()
        {
            var graph = Graph.Retain(); State? clone = null;
            try
            {
                clone = new(graph, null!, null!, null!, Metric, Compatible)
                    { _rows = _rows!.Retain(), Total = Total, TotalLogical = TotalLogical, TotalUnknown = TotalUnknown,
                        TotalLogicalUnknown = TotalLogicalUnknown, FilterSummary = FilterSummary, MeasurementBases = MeasurementBases, Finished = true };
                clone._buckets = _buckets!.Retain();
                foreach (var index in Indexes) clone.Indexes.Add(index.Retain());
                clone.Placements = Placements?.Retain(); return clone;
            }
            catch { if (clone is not null) clone.Dispose(); else graph.Dispose(); throw; }
        }
    }
}
