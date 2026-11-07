using System.Buffers.Binary;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    private static byte[] OrderKey(byte[] prefix, byte[] tie, string name, ulong id, AnalysisTableSort field, UInt128 value, bool missing, MeasurementState state)
    {
        if (field == AnalysisTableSort.Name)
        {
            var named = AnalysisHierarchyNameKey.Create(0, id, name);
            return Join(prefix, [0], named.AsSpan(8).ToArray(), tie);
        }
        UInt128 rank = UInt128.MaxValue - (field == AnalysisTableSort.MeasurementState ? (uint)state : value);
        byte[] amount = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(amount, (ulong)(rank >> 64)); BinaryPrimitives.WriteUInt64BigEndian(amount.AsSpan(8), (ulong)rank);
        return Join(prefix, [missing ? (byte)1 : (byte)0], amount, tie);
    }
    private static ulong OrderedPosition(AnalysisSortedIndexReader index, byte[] prefix, AnalysisIndexRange range, ulong position,
        AnalysisTableSort field, bool descending, CancellationToken token)
    {
        if (descending) return range.First + position;
        var known = field == AnalysisTableSort.Name ? range : index.PrefixRange(Join(prefix, [0]), token);
        if (position < known.Count) return known.First + known.Count - 1 - position;
        return field == AnalysisTableSort.MeasurementState ? range.First + range.Count - 1 - (position - known.Count) : range.First + position;
    }
    private sealed partial class State
    {
        internal IEnumerable<AnalysisSortInput> PlacementEntries(bool references, CancellationToken token)
        {
            var records = references ? ReferencePlacements! : MemberPlacements!; var events = references ? References! : Members!;
            for (ulong id = 1; id <= records.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var placement = DecodePlacement(records.Read(id, token)); var item = DecodeEvent(events.Read(placement.Event, token));
                var path = Graph.ReadPath(placement.Path, token);
                foreach (var field in Enum.GetValues<AnalysisTableSort>())
                {
                    UInt128 area = !Totals.CompatibleBasis ? 0 : Metric == AnalysisMetric.OnDisk ? checked(placement.Known + placement.Unverified) : placement.Known;
                    UInt128 value = field == AnalysisTableSort.FileSize ? item.Logical.Value ?? 0 : field == AnalysisTableSort.FilePaths ? path.Kind == AnalysisKind.File ? 1U : 0U : area;
                    bool missing = field switch
                    {
                        AnalysisTableSort.ActiveMetric => !Totals.CompatibleBasis || placement.SharedElsewhere || placement.Missing.Count != 0 && area == 0,
                        AnalysisTableSort.FileSize => path.Kind != AnalysisKind.File || !item.Logical.IsKnown,
                        AnalysisTableSort.FilePaths => path.Kind != AnalysisKind.File,
                        AnalysisTableSort.MeasurementState => path.MetadataState != MeasurementState.Known || placement.Missing.Count != 0 || placement.SharedElsewhere,
                        _ => false
                    };
                    var key = OrderKey(Join(placement.GroupHash, [(byte)field]), Number(placement.Path), path.Name, placement.Path, field, value, missing, path.MetadataState);
                    yield return new(new(key, id), checked((int)(AnalysisIndexedFilter.Cost(path) + 512)));
                }
            }
        }
    }
}
