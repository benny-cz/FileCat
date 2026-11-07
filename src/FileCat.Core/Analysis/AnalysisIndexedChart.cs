using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisChartRemainder(ulong Parent, ulong Offset)
{
    public bool GeometryScope { get; init; }
}

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
        for (int depth = 0; ; depth++)
        {
            if (_children.TryGetValue(key, out var exact)) return exact.Skip(skip).ToImmutableArray();
            // A backend remainder is a bounded leaf until its exact scope is opened.
            if (Items.TryGetValue(key, out var item) && item.Family == "other") return [];
            if (!AnalysisTree.Other(key, out int offset, out string parent)) return [];
            if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("Display aggregation nesting limit.");
            skip = checked(skip + offset); key = parent;
        }
    }
    internal static async Task<AnalysisIndexedChart> BuildAsync(AnalysisDatasetHierarchy hierarchy, ulong scope, CancellationToken cancellation)
    {
        var root = scope == 0 ? null : await hierarchy.ReadAsync(scope, cancellation).ConfigureAwait(false);
        if (root is not null && (root.Path.Kind != AnalysisKind.Directory || !root.IsVisible)) throw new InvalidOperationException("Choose a visible directory for the chart.");
        string key = scope == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(scope);
        var totals = root?.Totals ?? hierarchy.Totals; var logical = root?.Logical ?? hierarchy.LogicalTotals;
        var item = root is not null ? Row(root, hierarchy.Metric) : ScopeItem(key, "", "All locations", totals, logical, hierarchy.Metric,
            hierarchy.HasUnmeasuredEvidence, hierarchy.HasUnmeasuredLogicalEvidence);
        return await BuildGeometryAsync(hierarchy.Metric, hierarchy.Descriptor.Identity.ObservedUtc, scope, 0, item, root?.Path.ParentId ?? 0,
            (id, token) => hierarchy.ReadAsync(id, token),
            (id, first, token) => hierarchy.ReadGeometryAsync(id, maximum: 128, cancellation: token), cancellation).ConfigureAwait(false);
    }
    internal static Task<AnalysisIndexedChart> BuildAsync(AnalysisDatasetHierarchy.Subset subset, CancellationToken cancellation)
    {
        string parent = subset.ParentId == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(subset.ParentId);
        var item = ScopeItem(subset.ScopeKey, parent, "Other small items", subset.Totals, subset.LogicalTotals, subset.Metric,
            subset.HasUnmeasuredEvidence, subset.HasUnmeasuredLogicalEvidence);
        return BuildGeometryAsync(subset.Metric, subset.Descriptor.Identity.ObservedUtc, subset.ParentId, subset.Skipped, item, subset.ParentId,
            (id, token) => subset.ReadSnapshotRowAsync(id, token),
            (id, first, token) => first ? subset.ReadGeometryAsync(cancellation: token) : subset.ReadSnapshotGeometryAsync(id, 128, token), cancellation);
    }
    private static AnalysisTreeItem ScopeItem(string key, string parent, string name, AnalysisTotals totals, AnalysisLogicalTotals logical, AnalysisMetric metric,
        bool unknown = false, bool logicalUnknown = false) =>
        new(key, parent, name, null, AnalysisKind.Directory, true, true, AnalysisGroupedChart.Area(totals, metric), logical.KnownBytes,
            logical.Files, unknown || totals.Missing.Count != 0 || !totals.CompatibleBasis, false, "folder", [], [])
            { LogicalUnknown = logicalUnknown || logical.UnknownFiles != 0, IdentityUnverified = totals.IdentityUnverified };

    private static async Task<AnalysisIndexedChart> BuildGeometryAsync(AnalysisMetric metric, DateTimeOffset observed, ulong scope, int skipped,
        AnalysisTreeItem root, ulong ancestor, Func<ulong, CancellationToken, Task<AnalysisHierarchyRow>> readRow,
        Func<ulong, bool, CancellationToken, Task<AnalysisDatasetPage<AnalysisTreeItem>>> readGeometry, CancellationToken cancellation)
    {
        const int childrenPerLevel = 128;
        const long byteLimit = AnalysisLimits.PendingBytes / 4;
        var items = new Dictionary<string, AnalysisTreeItem>(StringComparer.Ordinal);
        var children = new Dictionary<string, ImmutableArray<AnalysisTreeItem>>(StringComparer.Ordinal);
        var remainders = new Dictionary<string, AnalysisChartRemainder>(StringComparer.Ordinal);
        var queue = new Queue<(ulong Id, string Key, int Depth, int Skipped)>(); bool limited = false; long bytes = 0;
        if (!Add(root)) throw new IOException("The bounded chart root could not fit.");
        for (int depth = 0; ancestor != 0 && depth < AnalysisLimits.Depth; depth++)
        {
            var row = await readRow(ancestor, cancellation).ConfigureAwait(false);
            if (!Add(Row(row, metric))) { limited = true; break; } ancestor = row.Path.ParentId;
        }
        queue.Enqueue((scope, root.Key, 0, skipped));
        while (queue.TryDequeue(out var parent))
        {
            cancellation.ThrowIfCancellationRequested();
            if (items.Count + childrenPerLevel + 3 > AnalysisLimits.ChartLeaves || bytes + 512 * childrenPerLevel > byteLimit)
            { limited = true; continue; }
            var parentItem = items[parent.Key]; if (parentItem.Value == 0) continue;
            var page = await readGeometry(parent.Id, parent.Depth == 0, cancellation).ConfigureAwait(false);
            var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>();
            UInt128 area = 0, logicalBytes = 0, unverified = 0; ulong files = 0; int used = 0;
            foreach (var captured in page.Rows)
            {
                var item = captured with { ParentKey = parent.Key };
                if (item.Value == 0) break;
                if (!Add(item)) { limited = true; break; }
                rows.Add(item); area = checked(area + item.Value); logicalBytes = checked(logicalBytes + item.LogicalSize);
                unverified = checked(unverified + item.IdentityUnverified); files = checked(files + item.Files); used++;
                if (parent.Depth < 3 && item is { Kind: AnalysisKind.Directory, NodeId: { } id }) queue.Enqueue((id, item.Key, parent.Depth + 1, 0));
            }
            if (area > parentItem.Value || logicalBytes > parentItem.LogicalSize || files > parentItem.Files || unverified > parentItem.IdentityUnverified)
                throw new InvalidDataException("Chart children exceed their captured parent.");
            if (area < parentItem.Value)
            {
                if (used == 0) throw new IOException("The bounded geometry page made no progress. The previous view remains displayed.");
                string key = AnalysisTree.OtherKey(parent.Key, used);
                var item = new AnalysisTreeItem(key, parent.Key, "Other small items", null, AnalysisKind.Special, true, true, parentItem.Value - area,
                    parentItem.LogicalSize - logicalBytes, parentItem.Files - files, parentItem.Unknown, false, "other", [], [])
                    { IdentityUnverified = parentItem.IdentityUnverified - unverified, LogicalUnknown = parentItem.LogicalUnknown };
                if (!Add(item)) throw new IOException("The bounded chart remainder could not fit. The previous view remains displayed.");
                rows.Add(item); remainders.Add(key, new(parent.Id, checked((ulong)(parent.Skipped + used))) { GeometryScope = true }); limited = true;
            }
            // Keep the canonical prefix followed by its remainder. Re-sorting that remainder
            // by its combined value would make renderer-generated suffixes noncontiguous.
            children.Add(parent.Key, rows.ToImmutable());
        }
        return new(metric, observed, root.Key, items, children, remainders, limited);

        bool Add(AnalysisTreeItem item)
        {
            long cost = 640L + item.Key.Length * 2L + item.ParentKey.Length * 2L + item.Name.Length * 2L;
            if (items.Count >= AnalysisLimits.ChartLeaves || cost > byteLimit - bytes) return false;
            if (items.TryAdd(item.Key, item)) bytes += cost; return true;
        }
    }
    public static AnalysisTreeItem Row(AnalysisHierarchyRow row, AnalysisMetric metric)
    {
        var path = row.Path;
        UInt128 value = !row.Totals.CompatibleBasis ? 0 : metric == AnalysisMetric.OnDisk ? checked(row.Totals.Known + row.Totals.IdentityUnverified) : row.Totals.Known;
        return new(AnalysisTree.Key(path.Id), path.ParentId == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(path.ParentId), AnalysisNames.DisplayName(path, 256), path.Id,
            path.Kind, false, row.IsVisible, value, row.Logical.KnownBytes, row.Logical.Files, row.HasUnmeasuredEvidence || row.NamespaceIncomplete || row.Totals.Missing.Count != 0 ||
            metric == AnalysisMetric.UniqueFileSize && row.Totals.IdentityUnverified != 0, row.HasChargesElsewhere, path.Kind == AnalysisKind.Directory ? "folder" : AnalysisClassification.Family(path.Name), [], [])
        { IdentityUnverified = row.Totals.IdentityUnverified, LogicalUnknown = row.HasUnmeasuredLogicalEvidence || row.Logical.UnknownFiles != 0, MetadataState = path.MetadataState, IncludedByPolicy = path.IncludedByPolicy,
            ModifiedUtc = row.ModifiedUtc, ModifiedPrecisionTicks = row.ModifiedPrecisionTicks,
            IsFilterContext = row.IsFilterContext };
    }
}
