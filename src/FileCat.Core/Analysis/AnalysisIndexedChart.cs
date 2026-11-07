using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisChartRemainder(ulong Parent, ulong Offset);

/// <summary>A bounded display projection; its synthetic rows never stand in for a complete inventory.</summary>
public sealed class AnalysisIndexedChart : IAnalysisChartData
{
    private readonly ImmutableDictionary<string, ImmutableArray<AnalysisTreeItem>> _children;
    public AnalysisMetric Metric { get; }
    public DateTimeOffset ObservedUtc { get; }
    public string Scope { get; }
    public ImmutableDictionary<string, AnalysisTreeItem> Items { get; }
    public ImmutableDictionary<string, AnalysisChartRemainder> Remainders { get; }
    public bool DetailLimited { get; }
    private AnalysisIndexedChart(AnalysisMetric metric, DateTimeOffset observedUtc, string scope, Dictionary<string, AnalysisTreeItem> items,
        Dictionary<string, ImmutableArray<AnalysisTreeItem>> children, Dictionary<string, AnalysisChartRemainder> remainders, bool limited)
    { Metric = metric; ObservedUtc = observedUtc; Scope = scope; Items = items.ToImmutableDictionary(); _children = children.ToImmutableDictionary(); Remainders = remainders.ToImmutableDictionary(); DetailLimited = limited; }
    public string ParentOf(string key) => AnalysisTree.Other(key, out _, out string parent) ? parent : Items.GetValueOrDefault(key)?.ParentKey ?? AnalysisTree.RootKey;
    public string NameOf(string key) => AnalysisTree.Other(key, out _, out _) ? "Other small items" : Items.GetValueOrDefault(key)?.Name ?? "All locations";
    public UInt128 ScopeValue(string key) => Items.GetValueOrDefault(key)?.Value ?? ChildrenOf(key).Aggregate((UInt128)0, (sum, item) => checked(sum + item.Value));
    public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key)
    {
        int skip = 0;
        for (int depth = 0; AnalysisTree.Other(key, out int offset, out string parent); depth++)
        { if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("Display aggregation nesting limit."); skip = checked(skip + offset); key = parent; }
        return _children.GetValueOrDefault(key, []).Skip(skip).ToImmutableArray();
    }
    internal static async Task<AnalysisIndexedChart> BuildAsync(AnalysisDatasetHierarchy hierarchy, ulong scope, CancellationToken cancellation)
    {
        const int childrenPerLevel = 128;
        const long byteLimit = AnalysisLimits.PendingBytes / 4;
        var items = new Dictionary<string, AnalysisTreeItem>(StringComparer.Ordinal);
        var children = new Dictionary<string, ImmutableArray<AnalysisTreeItem>>(StringComparer.Ordinal);
        var remainders = new Dictionary<string, AnalysisChartRemainder>(StringComparer.Ordinal);
        var queue = new Queue<(ulong Id, int Depth)>(); bool limited = false; long bytes = 0;
        string scopeKey = scope == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(scope);
        AnalysisHierarchyRow? root = scope == 0 ? null : await hierarchy.ReadAsync(scope, cancellation).ConfigureAwait(false);
        if (root is not null && (root.Path.Kind != AnalysisKind.Directory || !root.IsVisible)) throw new InvalidOperationException("Choose a visible directory for the chart.");
        var totals = root?.Totals ?? hierarchy.Totals;
        var logical = root?.Logical ?? hierarchy.LogicalTotals;
        Add(root is null ? new(scopeKey, "", "All locations", null, AnalysisKind.Directory, true, true, hierarchy.ChartArea(totals), logical.KnownBytes,
            totals.MatchingPaths, totals.Missing.Count != 0, false, "folder", [], []) { LogicalUnknown = logical.UnknownFiles != 0, IdentityUnverified = totals.IdentityUnverified } : Row(root, hierarchy.Metric));
        // Captured ancestors supply names and navigation only. They never introduce extra chart area.
        ulong ancestor = root?.Path.ParentId ?? 0;
        for (int depth = 0; ancestor != 0 && depth < AnalysisLimits.Depth; depth++)
        {
            var row = await hierarchy.ReadAsync(ancestor, cancellation).ConfigureAwait(false);
            if (!Add(Row(row, hierarchy.Metric))) { limited = true; break; }
            ancestor = row.Path.ParentId;
        }
        queue.Enqueue((scope, 0));
        while (queue.TryDequeue(out var parent))
        {
            cancellation.ThrowIfCancellationRequested();
            string key = parent.Id == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(parent.Id);
            if (items.Count + childrenPerLevel + 3 > AnalysisLimits.ChartLeaves || bytes + 512 * childrenPerLevel > byteLimit)
            { limited = true; continue; }
            var parentItem = items[key]; if (parentItem.Value == 0) continue;
            var page = await hierarchy.ReadChildrenAsync(parent.Id, maximum: childrenPerLevel, cancellation: cancellation).ConfigureAwait(false);
            var buckets = await hierarchy.ReadBucketsAsync(parent.Id, cancellation).ConfigureAwait(false);
            var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>();
            UInt128 area = 0, logicalBytes = 0, unverified = 0; ulong files = 0; ulong used = 0;
            foreach (var row in page.Rows)
            {
                var item = Row(row, hierarchy.Metric);
                if (item.Value == 0) break;
                if (!Add(item)) { limited = true; break; }
                rows.Add(item); area = checked(area + item.Value); logicalBytes = checked(logicalBytes + item.LogicalSize);
                unverified = checked(unverified + item.IdentityUnverified); files = checked(files + item.Files); used++;
                if (parent.Depth < 3 && row.Path.Kind == AnalysisKind.Directory) queue.Enqueue((row.Path.Id, parent.Depth + 1));
            }
            foreach (var bucket in buckets)
            {
                UInt128 value = hierarchy.ChartArea(bucket.Totals); if (value == 0) continue;
                string bucketKey = "bucket:" + parent.Id + ":" + bucket.Kind;
                var item = new AnalysisTreeItem(bucketKey, key, bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "Shared hard-linked files" : "Directory allocation",
                    null, AnalysisKind.Special, true, true, value, 0, 0, bucket.Totals.Missing.Count != 0, false,
                    bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles ? "shared" : "directory-allocation", [], []) { IdentityUnverified = bucket.Totals.IdentityUnverified };
                if (!Add(item)) { limited = true; continue; }
                rows.Add(item); area = checked(area + value); unverified = checked(unverified + item.IdentityUnverified);
            }
            if (area > parentItem.Value || logicalBytes > parentItem.LogicalSize || files > parentItem.Files || unverified > parentItem.IdentityUnverified)
                throw new InvalidDataException("Chart children exceed their captured parent.");
            if (area < parentItem.Value)
            {
                string otherKey = "indexed-other:" + parent.Id + ":" + used;
                var item = new AnalysisTreeItem(otherKey, key, "Other small items", null, AnalysisKind.Special, true, true, parentItem.Value - area,
                    parentItem.LogicalSize - logicalBytes, parentItem.Files - files, parentItem.Unknown, false, "other", [], [])
                    { IdentityUnverified = parentItem.IdentityUnverified - unverified, LogicalUnknown = parentItem.LogicalUnknown };
                if (!Add(item)) throw new IOException("The bounded chart remainder could not fit. The previous view remains displayed.");
                rows.Add(item); remainders.Add(otherKey, new(parent.Id, used)); limited = true;
            }
            children.Add(key, rows.OrderByDescending(row => row.Value).ThenBy(row => row.Key, StringComparer.Ordinal).ToImmutableArray());
        }
        return new(hierarchy.Metric, hierarchy.Descriptor.Identity.ObservedUtc, scopeKey, items, children, remainders, limited);

        bool Add(AnalysisTreeItem item)
        {
            long cost = 640L + item.Key.Length * 2L + item.ParentKey.Length * 2L + item.Name.Length * 2L;
            if (items.Count >= AnalysisLimits.ChartLeaves || cost > byteLimit - bytes) return false;
            if (items.TryAdd(item.Key, item)) bytes += cost;
            return true;
        }
    }
    public static AnalysisTreeItem Row(AnalysisHierarchyRow row, AnalysisMetric metric)
    {
        var path = row.Path;
        UInt128 value = !row.Totals.CompatibleBasis ? 0 : metric == AnalysisMetric.OnDisk ? checked(row.Totals.Known + row.Totals.IdentityUnverified) : row.Totals.Known;
        return new(AnalysisTree.Key(path.Id), path.ParentId == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(path.ParentId), AnalysisNames.DisplayName(path, 256), path.Id,
            path.Kind, false, row.IsVisible, value, row.Logical.KnownBytes, row.Logical.Files, row.NamespaceIncomplete || row.Totals.Missing.Count != 0 ||
            metric == AnalysisMetric.UniqueFileSize && row.Totals.IdentityUnverified != 0, row.HasChargesElsewhere, path.Kind == AnalysisKind.Directory ? "folder" : AnalysisClassification.Family(path.Name), [], [])
        { IdentityUnverified = row.Totals.IdentityUnverified, LogicalUnknown = row.Logical.UnknownFiles != 0, MetadataState = path.MetadataState, IncludedByPolicy = path.IncludedByPolicy,
            ModifiedUtc = row.ModifiedUtc, ModifiedPrecisionTicks = row.ModifiedPrecisionTicks,
            IsFilterContext = row.IsFilterContext };
    }
}
