using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>One bounded geometry input for grouped charts; all amounts come from accounting rows.</summary>
public sealed partial class AnalysisGroupedChart : IAnalysisChartData
{
    public AnalysisMetric Metric { get; }
    public DateTimeOffset ObservedUtc { get; }
    public string Scope { get; }
    public ImmutableDictionary<string, AnalysisTreeItem> Items { get; }
    public string? RemainderKey { get; }
    public ulong RemainderOffset { get; }
    private readonly ImmutableDictionary<string, ImmutableArray<AnalysisTreeItem>>? _geometryChildren;
    public ImmutableDictionary<string, AnalysisGroupChartRemainder> Remainders { get; } = ImmutableDictionary<string, AnalysisGroupChartRemainder>.Empty;
    public bool GeometryProjection => _geometryChildren is not null;
    public bool DetailLimited { get; }
    private AnalysisGroupedChart(AnalysisMetric metric, DateTimeOffset observed, string scope, IEnumerable<AnalysisTreeItem> items, string? remainder, ulong offset)
    { Metric = metric; ObservedUtc = observed; Scope = scope; Items = items.ToImmutableDictionary(i => i.Key); RemainderKey = remainder; RemainderOffset = offset; DetailLimited = remainder is not null; }
    public string ParentOf(string key) => AnalysisTree.Other(key, out _, out string parent) ? parent : Items.GetValueOrDefault(key)?.ParentKey ?? AnalysisTree.RootKey;
    public string NameOf(string key) => AnalysisTree.Other(key, out _, out _) ? "Other small items" : Items.GetValueOrDefault(key)?.Name ?? "All groups";
    public UInt128 ScopeValue(string key) => Items.GetValueOrDefault(key)?.Value ?? ChildrenOf(key).Aggregate((UInt128)0, (sum, item) => checked(sum + item.Value));
    public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key)
    {
        if (_geometryChildren is null) return Items.TryGetValue(key, out var item) ? item.Children.Select(k => Items[k]).OrderByDescending(i => i.Value).ThenBy(i => i.Key, StringComparer.Ordinal).ToImmutableArray() : [];
        int skip = 0;
        for (int depth = 0; ; depth++)
        {
            if (_geometryChildren.TryGetValue(key, out var exact)) return exact.Skip(skip).ToImmutableArray();
            if (Items.TryGetValue(key, out var leaf) && leaf.Family == "other") return [];
            if (!AnalysisTree.Other(key, out int offset, out string parent)) return [];
            if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("Grouped display aggregation nesting limit.");
            skip = checked(skip + offset); key = parent;
        }
    }
    public static UInt128 Area(AnalysisTotals totals, AnalysisMetric metric) => !totals.CompatibleBasis ? 0 : metric == AnalysisMetric.OnDisk ? checked(totals.Known + totals.IdentityUnverified) : totals.Known;
    public static AnalysisGroupedChart Overview(SessionIdentity identity, AnalysisGroupingDimension dimension, AnalysisMetric metric, AnalysisTotals totals,
        AnalysisLogicalTotals logical, AnalysisDatasetPage<AnalysisDatasetGroup> page)
    {
        if (page.Offset != 0 || page.Rows.Length > AnalysisLimits.ChartLeaves - 2) throw new ArgumentException("Charts need a bounded first accounting page.", nameof(page));
        var children = page.Rows.Select(group => GroupItem(group, dimension, metric, AnalysisTree.RootKey)).ToList();
        return Create(identity, metric, AnalysisTree.RootKey, "All groups — " + AnalysisGroupingRules.Title(dimension), totals, logical, children, page.HasMore, page.NextOffset);
    }
    public static AnalysisGroupedChart Members(SessionIdentity identity, AnalysisGroupingDimension dimension, AnalysisMetric metric, AnalysisDatasetGroup group,
        AnalysisDatasetPage<AnalysisDatasetGroupRow> page)
    {
        if (page.Offset != 0 || page.Rows.Length > AnalysisLimits.ChartLeaves - 3) throw new ArgumentException("Charts need a bounded first member page.", nameof(page));
        var children = page.Rows.Select(row => MemberItem(row, group.Definition.Key, metric)).ToList();
        if (group.HasSharedAccounting)
            children.Add(new("group-shared:" + group.Definition.Key, group.Definition.Key, "Shared files — charged once", null, AnalysisKind.Special, true, true,
                Area(group.Shared, metric), 0, 0, group.Shared.Missing.Count != 0 || metric == AnalysisMetric.UniqueFileSize && group.Shared.IdentityUnverified != 0,
                false, "shared", [], []) { IdentityUnverified = group.Shared.IdentityUnverified, LogicalUnknown = true });
        return Create(identity, metric, group.Definition.Key, group.Definition.Label, group.Totals, group.Logical, children, page.HasMore, page.NextOffset);
    }
    public static AnalysisTreeItem GroupItem(AnalysisDatasetGroup group, AnalysisGroupingDimension dimension, AnalysisMetric metric, string parent) =>
        new(group.Definition.Key, parent, AnalysisNames.Display(group.Definition.Label, 256), null, AnalysisKind.Directory, true, true, Area(group.Totals, metric), group.Logical.KnownBytes,
            group.Totals.MatchingPaths, !group.Totals.CompatibleBasis || group.Totals.Missing.Count != 0 || metric == AnalysisMetric.UniqueFileSize && group.Totals.IdentityUnverified != 0,
            false, group.Definition.Family, [], []) { GroupDefinition = group.Definition, GroupingDimension = dimension, IdentityUnverified = group.Totals.IdentityUnverified, LogicalUnknown = group.Logical.UnknownFiles != 0,
                ReferenceLogical = group.Kind == AnalysisGroupKind.SharedAcrossGroups ? group.ReferenceLogical : null };
    public static AnalysisTreeItem MemberItem(AnalysisDatasetGroupRow row, string parent, AnalysisMetric metric)
    {
        var path = row.Member.Path;
        return new(AnalysisTree.Key(path.Id), parent, AnalysisNames.DisplayName(path, 256), path.Id, path.Kind, false, true, Area(row.Assigned, metric),
            row.Member.LogicalLength.Value ?? 0, path.Kind == AnalysisKind.File ? 1UL : 0UL,
            !row.Assigned.CompatibleBasis || row.Assigned.Missing.Count != 0 || metric == AnalysisMetric.UniqueFileSize && row.Assigned.IdentityUnverified != 0,
            row.SharedElsewhere, path.Kind == AnalysisKind.Directory ? "folder" : AnalysisClassification.Family(path.Name), [], [])
        { CapturedDepth = row.Member.CapturedDepth, ModifiedUtc = row.Member.ModifiedUtc, ModifiedPrecisionTicks = row.Member.ModifiedPrecisionTicks,
            IdentityUnverified = row.Assigned.IdentityUnverified, LogicalUnknown = !row.Member.LogicalLength.IsKnown, MetadataState = path.MetadataState,
            IncludedByPolicy = path.IncludedByPolicy, ChargeLocation = row.SharedElsewhere ? "Shared charge shown in its accounting group" : null };
    }
    private static AnalysisGroupedChart Create(SessionIdentity identity, AnalysisMetric metric, string scope, string name, AnalysisTotals totals, AnalysisLogicalTotals logical,
        List<AnalysisTreeItem> children, bool more, ulong offset)
    {
        UInt128 total = Area(totals, metric), shown = 0; foreach (var child in children) shown = checked(shown + child.Value);
        if (shown > total) throw new InvalidDataException("Grouped chart children exceed their accounting scope.");
        string? remainder = null;
        if (shown < total)
        {
            if (!more) throw new InvalidDataException("Grouped chart area is missing from a complete accounting page.");
            remainder = "group-other:" + scope;
            children.Add(new(remainder, scope, "Other groups / members", null, AnalysisKind.Special, true, true, total - shown, 0, 0, true, false, "other", [], []) { LogicalUnknown = true });
        }
        var root = new AnalysisTreeItem(scope, AnalysisTree.RootKey, name, null, AnalysisKind.Directory, true, true, total, logical.KnownBytes, logical.Files,
            !totals.CompatibleBasis || totals.Missing.Count != 0, false, "folder", children.Select(i => i.Key).ToImmutableArray(), []) { IdentityUnverified = totals.IdentityUnverified, LogicalUnknown = logical.UnknownFiles != 0 };
        return new(metric, identity.ObservedUtc, scope, children.Append(root), remainder, offset);
    }
}
