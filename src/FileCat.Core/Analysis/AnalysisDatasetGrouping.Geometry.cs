using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    /// <summary>Canonical geometry children, including shared accounting buckets, independent of every table order.</summary>
    public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadGeometryAsync(string? groupKey = null, ulong offset = 0, int maximum = 128,
        CancellationToken cancellation = default) => ReadAsync((state, token) => state.ReadGeometry(groupKey, offset, maximum, token), cancellation);

    /// <summary>Retains sealed captured indexes without rebuilding them or acquiring any live source.</summary>
    public Task<AnalysisDatasetGrouping> RetainAsync(CancellationToken cancellation = default) => ReadAsync((state, _) =>
    {
        var retained = state.Retain();
        try { return new AnalysisDatasetGrouping(retained, Descriptor, _io, _device, Filter); }
        catch { AnalysisIo.CloseOnWorker(retained); throw; }
    }, cancellation);

    private const ulong GeometryBucket = 1UL << 63, GeometryReference = 1UL << 62;
    private static bool HasSharedBucket(AnalysisDatasetGroup group) => group.HasSharedAccounting;
    private static AnalysisTreeItem SharedGeometryItem(AnalysisDatasetGroup group, AnalysisMetric metric) =>
        new("group-shared:" + group.Definition.Key, group.Definition.Key, "Shared files — charged once", null, AnalysisKind.Special, true, true,
            AnalysisGroupedChart.Area(group.Shared, metric), 0, 0, group.Shared.Missing.Count != 0 || metric == AnalysisMetric.UniqueFileSize && group.Shared.IdentityUnverified != 0,
            false, "shared", [], []) { IdentityUnverified = group.Shared.IdentityUnverified, LogicalUnknown = true };
    private static byte[] GeometryKey(AnalysisTreeItem item, bool overview)
    {
        byte[] rank = AnalysisGeometryOrder.Key(0, item).AsSpan(8).ToArray();
        // Preserve Unknown even when area is positive. The terminal byte is below every
        // character used by canonical keys, preserving ordinal prefix ordering as well.
        byte[] flags = [item.Unknown ? (byte)1 : (byte)0];
        return overview ? Join(rank, flags) : Join(Hash(item.ParentKey), rank, flags);
    }

    private sealed partial class State
    {
        internal AnalysisSortedIndexReader? GroupGeometry, MemberGeometry;
        internal AnalysisDatasetPage<AnalysisTreeItem> ReadGeometry(string? key, ulong offset, int maximum, CancellationToken token)
        {
            bool overview = key is null || key == AnalysisTree.RootKey; if (!overview) _ = Group(key!, token);
            var index = overview ? GroupGeometry! : MemberGeometry!;
            var range = overview ? new AnalysisIndexRange(1, index.Count) : index.PrefixRange(Hash(key!), token);
            ValidatePage(offset, maximum, range.Count); var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var entry = index.Read(range.First + offset + (ulong)rows.Count, token);
                var item = GeometryItem(overview, entry, token);
                if (!Fits(1024L + item.Name.Length * 2L + item.Key.Length * 2L, ref bytes)) break; rows.Add(item);
            }
            return new(offset, range.Count, rows.ToImmutable());
        }
        internal IEnumerable<AnalysisSortInput> GeometryEntries(bool members, CancellationToken token)
        {
            for (ulong row = 1; row <= Summaries!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); byte[] data = Summaries.Read(row, token); var group = DecodeSummary(data);
                AnalysisTreeItem? item = !members ? AnalysisGroupedChart.GroupItem(group, Dimension, Metric, AnalysisTree.RootKey) : HasSharedBucket(group) ? SharedGeometryItem(group, Metric) : null;
                yield return new(item is null ? null : new(GeometryKey(item, !members), members ? row | GeometryBucket : row), data.Length * 3 + 1024);
            }
            if (!members) yield break;
            foreach (bool reference in new[] { false, true })
            {
                var placements = reference ? ReferencePlacements! : MemberPlacements!;
                for (ulong row = 1; row <= placements.Count; row++)
                {
                    token.ThrowIfCancellationRequested(); ulong source = reference ? row | GeometryReference : row;
                    var item = MemberGeometryItem(source, token, out long bytes);
                    // Issue evidence is bounded and staged through the external-sort producer;
                    // even one path with many issues cannot monopolize a worker turn.
                    var issues = Graph.Find(AnalysisIndexedGraphRevision.IssueOwners, AnalysisGraphKeys.IssueOwner(item.NodeId!.Value, null, Graph.Header.Roots), cancellation: token);
                    for (ulong at = 0; at < issues.Count; at++)
                    {
                        token.ThrowIfCancellationRequested(); var issue = Graph.ReadIssue(Graph.ReadIndex(AnalysisIndexedGraphRevision.IssueOwners, issues.First + at, token).Value, token);
                        yield return new(null, 1024 + issue.Code.Length * 2);
                        if (issue.State != MeasurementState.Excluded) { item = item with { Unknown = true }; break; }
                    }
                    yield return new(new(GeometryKey(item, false), source), checked((int)bytes));
                }
            }
        }
        internal AnalysisTreeItem GeometryItem(bool overview, AnalysisSortEntry entry, CancellationToken token)
        {
            if (entry.Key.Length == 0 || entry.Key[^1] > 1) throw Invalid();
            AnalysisTreeItem item;
            if (overview)
            {
                if ((entry.Value & (GeometryBucket | GeometryReference)) != 0) throw Invalid();
                item = AnalysisGroupedChart.GroupItem(DecodeSummary(Summaries!.Read(entry.Value, token)), Dimension, Metric, AnalysisTree.RootKey);
            }
            else if ((entry.Value & GeometryBucket) != 0)
            {
                if ((entry.Value & GeometryReference) != 0) throw Invalid();
                var group = DecodeSummary(Summaries!.Read(entry.Value & ~GeometryBucket, token)); if (!HasSharedBucket(group)) throw Invalid();
                item = SharedGeometryItem(group, Metric);
            }
            else item = MemberGeometryItem(entry.Value, token, out _);
            item = item with { Unknown = entry.Key[^1] != 0 };
            if (!entry.Key.AsSpan().SequenceEqual(GeometryKey(item, overview))) throw Invalid(); return item;
        }
        private AnalysisTreeItem MemberGeometryItem(ulong source, CancellationToken token, out long bytes)
        {
            var row = ReadMemberRow(source, token, out var definition, out var kind, out bytes);
            var result = AnalysisGroupedChart.MemberItem(row, definition.Key, Metric);
            return result with { Key = kind == AnalysisGroupKind.SharedAcrossGroups ? "group-reference:" + definition.Key + ":" + result.Key : result.Key,
                Unknown = result.Unknown || row.Member.Path.MetadataState != MeasurementState.Known };
        }
        internal AnalysisDatasetGroupRow ReadMemberRow(ulong source, CancellationToken token, out AnalysisGroupDefinition definition, out AnalysisGroupKind kind, out long bytes)
        {
            bool reference = (source & GeometryReference) != 0; source &= ~GeometryReference;
            var placement = DecodePlacement((reference ? ReferencePlacements! : MemberPlacements!).Read(source, token));
            var item = DecodeEvent((reference ? References! : Members!).Read(placement.Event, token));
            if (placement.Reference != reference || placement.Path != item.Path || !placement.GroupHash.AsSpan().SequenceEqual(Hash(item.Definition.Key))) throw Invalid();
            var captured = Graph.ReadPathRecord(placement.Path, token); var path = captured.Node; var obj = Object(path.ObjectId, token);
            var member = new AnalysisDatasetGroupMember(AnalysisPathMetadata.From(path), obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported), reference,
                path.Times is null ? obj?.ModifiedUtc : path.Times.ModifiedUtc, path.Times is null ? obj?.ModifiedPrecisionTicks ?? 0 : path.Times.ModifiedPrecisionTicks)
                { CapturedDepth = captured.Depth + 1 };
            var row = new AnalysisDatasetGroupRow(member, new(placement.Known, placement.Unverified, path.Kind == AnalysisKind.File ? 1UL : 0UL, placement.Missing, Totals.CompatibleBasis), placement.SharedElsewhere);
            bytes = 2048L + AnalysisIndexedFilter.Cost(path) + (obj?.Id.Length ?? 0) * 2L;
            definition = item.Definition; kind = item.Kind; return row;
        }
        internal State Retain()
        {
            var graph = Graph.Retain(); State? result = null;
            try
            {
                result = new(graph, null!, Metric, Totals, Dimension)
                    { _stage = BuildStage.Done, Folded = true, VisibilityFinished = true, _allKnown = _allKnown, _allUnverified = _allUnverified, _allLogical = _allLogical, _allFiles = _allFiles };
                foreach (var pair in _allMissing) result._allMissing.Add(pair.Key, pair.Value);
                foreach (var pair in _allLogicalMissing) result._allLogicalMissing.Add(pair.Key, pair.Value);
                result.Members = Members!.Retain(); result.Charges = Charges!.Retain(); result.References = References!.Retain(); result.Summaries = Summaries!.Retain();
                result.MemberPlacements = MemberPlacements!.Retain(); result.ReferencePlacements = ReferencePlacements!.Retain(); result.Visibility = Visibility!.Retain();
                result.MemberIndex = MemberIndex!.Retain(); result.ChargeIndex = ChargeIndex!.Retain(); result.ReferenceIndex = ReferenceIndex!.Retain(); result.Lookup = Lookup!.Retain();
                result.Order = Order!.Retain(); result.MemberOrder = MemberOrder!.Retain(); result.ReferenceOrder = ReferenceOrder!.Retain(); result.VisibilityIndex = VisibilityIndex!.Retain();
                result.GroupGeometry = GroupGeometry!.Retain(); result.MemberGeometry = MemberGeometry!.Retain(); return result;
            }
            catch { if (result is not null) result.Dispose(); else graph.Dispose(); throw; }
        }
    }
}
