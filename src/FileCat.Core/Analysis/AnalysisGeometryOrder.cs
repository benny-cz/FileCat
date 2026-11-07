using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Analysis;

// Same immutable order as AnalysisTree.ChildrenOf, including accounting rows and ordinal ID ties.
// Table order/direction never participates in geometry membership.
internal static class AnalysisGeometryOrder
{
    internal const ulong BucketToken = 1UL << 63;
    internal static byte[] Key(ulong parent, AnalysisTreeItem item)
    {
        byte[] tie = Encoding.UTF8.GetBytes(item.Key);
        if (tie.Length > 1024) throw new InvalidDataException("Unsupported captured geometry key.");
        byte[] key = new byte[25 + tie.Length]; BinaryPrimitives.WriteUInt64BigEndian(key, parent);
        key[8] = item.Unknown && item.Value == 0 ? (byte)1 : (byte)0;
        UInt128 rank = UInt128.MaxValue - item.Value;
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(9), (ulong)(rank >> 64)); BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(17), (ulong)rank);
        tie.CopyTo(key, 25); return key;
    }
    internal static string BucketKey(ulong parent, AnalysisHierarchyBucketKind kind) =>
        (kind == AnalysisHierarchyBucketKind.SharedFiles ? "shared:" : "directory:") + (parent == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(parent));
    internal static AnalysisTreeItem Bucket(AnalysisHierarchyBucket bucket, AnalysisMetric metric)
    {
        UInt128 value = AnalysisGroupedChart.Area(bucket.Totals, metric);
        return new(BucketKey(bucket.ParentId, bucket.Kind), bucket.ParentId == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(bucket.ParentId),
            bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "Shared hard-linked files" : "Directory allocation", null, AnalysisKind.Special, true, true,
            value, 0, 0, bucket.Totals.Missing.Count != 0, false,
            bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "shared" : "directory-allocation", [], []) { IdentityUnverified = bucket.Totals.IdentityUnverified };
    }
}
