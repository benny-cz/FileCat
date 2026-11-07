using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Globalization;

namespace FileCat.Core.Analysis;

public sealed record AnalysisTreeItem(string Key, string ParentKey, string Name, ulong? NodeId, AnalysisKind Kind,
    bool Synthetic, bool Visible, UInt128 Value, UInt128 LogicalSize, ulong Files, bool Unknown,
    bool SharedElsewhere, string Family, ImmutableArray<string> Children, ImmutableArray<ulong> Members)
{
    public UInt128 IdentityUnverified { get; init; }
    public MeasurementState MetadataState { get; init; } = MeasurementState.Known;
    public bool IncludedByPolicy { get; init; } = true;
    public bool LogicalUnknown { get; init; }
    public string? NameEvidence { get; init; }
    public DateTimeOffset? ModifiedUtc { get; init; }
    public ulong ModifiedPrecisionTicks { get; init; }
    public int? CapturedDepth { get; init; }
    public AnalysisGroupDefinition? GroupDefinition { get; init; }
    public AnalysisGroupingDimension GroupingDimension { get; init; }
    public string? ChargeLocation { get; init; }
    public bool IsFilterContext { get; init; }
    // Known namespace metadata for these entries is not a measured zero for their targets.
    public bool IsNonFileLeaf => !Synthetic && (Kind is AnalysisKind.Link or AnalysisKind.Special);
}

/// <summary>One exact hierarchy shared by charts, the ranked table and navigation. Geometry never computes accounting.</summary>
public sealed partial class AnalysisTree : IAnalysisChartData
{
    public const string RootKey = "scope";
    public AnalysisSnapshot Snapshot { get; }
    public DateTimeOffset ObservedUtc => Snapshot.Identity.ObservedUtc;
    public AnalysisProjection Projection { get; }
    public AnalysisMetric Metric => Projection.Metric;
    public bool FilterApplied { get; }
    public AnalysisGroupingDimension GroupingDimension { get; private init; }
    public ImmutableDictionary<string, AnalysisGroupSummary> Groups { get; private init; } = ImmutableDictionary<string, AnalysisGroupSummary>.Empty;
    public string ComponentCoverageDescription { get; }
    public ulong BoundaryCount { get; }
    public ImmutableDictionary<string, AnalysisTreeItem> Items { get; }
    private ImmutableHashSet<ulong>? FilterVisible { get; init; }
    private readonly ConcurrentDictionary<string, ImmutableArray<AnalysisTreeItem>> _children = new(StringComparer.Ordinal);
    private AnalysisTree(AnalysisSnapshot snapshot, AnalysisProjection projection, ImmutableDictionary<string, AnalysisTreeItem> items, bool filterApplied)
    { Snapshot = snapshot; Projection = projection; Items = items; FilterApplied = filterApplied;
        ComponentCoverageDescription = AnalysisComponents.Describe(snapshot);
        BoundaryCount = snapshot.CoverageDetails?.Boundaries ?? AnalysisCoverage.Create(snapshot).Boundaries; }
    public static string Key(ulong id) => "n:" + id.ToString(CultureInfo.InvariantCulture);
    public static string OtherKey(string parent, int skipped) => skipped > 0 ? "other:" + skipped.ToString(CultureInfo.InvariantCulture) + ":" + parent : throw new ArgumentOutOfRangeException(nameof(skipped));
    internal static bool Other(string key, out int skipped, out string parent)
    {
        skipped = 0; parent = "";
        if (!key.StartsWith("other:", StringComparison.Ordinal)) return false;
        int end = key.IndexOf(':', 6);
        if (end < 0 || !int.TryParse(key.AsSpan(6, end - 6), NumberStyles.None, CultureInfo.InvariantCulture, out skipped) || skipped <= 0)
            throw new InvalidDataException("Invalid display aggregation key.");
        parent = key[(end + 1)..]; return true;
    }
    public string ParentOf(string key) => Other(key, out _, out string parent) ? parent : Items.GetValueOrDefault(key)?.ParentKey ?? RootKey;
    public string NameOf(string key) => key.StartsWith("other:", StringComparison.Ordinal) ? "Other small items" : Items.GetValueOrDefault(key)?.Name ?? "Analysis";
    public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key)
    {
        if (_children.TryGetValue(key, out var cached)) return cached;
        string originalKey = key; int skip = 0;
        while (Other(key, out int skipped, out string parentKey)) { skip = checked(skip + skipped); key = parentKey; }
        if (skip > 0) return ChildrenOf(key).Skip(skip).ToImmutableArray();
        if (!Items.TryGetValue(key, out var parent)) return [];
        var result = GroupingDimension == AnalysisGroupingDimension.FolderHierarchy && parent.Members.Length > 0 ? parent.Members.Select(id => Items[Key(id)]).Where(n => n.Visible).ToImmutableArray() : parent.Children.Select(k => Items[k]).Where(n => n.Visible)
            .OrderBy(n => n.Unknown && n.Value == 0).ThenByDescending(n => n.Value).ThenBy(n => n.Key, StringComparer.Ordinal).ToImmutableArray();
        if (_children.Count < 512) _children.TryAdd(originalKey, result);
        return result;
    }
    public UInt128 ScopeValue(string key)
    {
        if (Items.TryGetValue(key, out var item)) return item.Value;
        UInt128 total = 0; foreach (var child in ChildrenOf(key)) total = checked(total + child.Value); return total;
    }
    public bool CanExplore(string key)
    {
        int depth = 0;
        while (Other(key, out _, out string parent))
        { if (++depth > AnalysisLimits.Depth + 1) throw new InvalidDataException("Invalid grouped navigation depth."); key = parent; }
        if (key == RootKey) return true;
        return Items.TryGetValue(key, out var item) && (GroupingDimension == AnalysisGroupingDimension.FolderHierarchy
            ? item.Kind == AnalysisKind.Directory || item.Members.Length > 0 : item.GroupDefinition is not null);
    }
    public ImmutableArray<ulong> CaptureMembers(string key, bool matchingOnly = true)
    {
        var result = ImmutableArray.CreateBuilder<ulong>(); var unique = new HashSet<ulong>(); var pending = new Stack<string>(); pending.Push(key);
        while (pending.TryPop(out string? current))
        {
            if (current.StartsWith("other:", StringComparison.Ordinal)) { foreach (var child in ChildrenOf(current)) pending.Push(child.Key); continue; }
            if (!Items.TryGetValue(current, out var item)) continue;
            if (GroupingDimension != AnalysisGroupingDimension.FolderHierarchy && item.Members.Length > AnalysisLimits.ReviewPaths)
                throw new InvalidOperationException("Refine this selection to at most 100,000 real paths.");
            if (item.NodeId is { } id && (!matchingOnly || item.Visible &&
                (GroupingDimension != AnalysisGroupingDimension.FolderHierarchy || (FilterApplied ? Projection.MatchingNodes.Contains(id) : item.Kind != AnalysisKind.File || Projection.MatchingNodes.Contains(id)))))
            { Capture(id); }
            foreach (ulong member in item.Members)
                if (!matchingOnly || (GroupingDimension != AnalysisGroupingDimension.FolderHierarchy ? Items.GetValueOrDefault(Key(member))?.Visible == true : Projection.MatchingNodes.Contains(member))) Capture(member);
            foreach (string child in item.Children) if (!matchingOnly || Items[child].Visible) pending.Push(child);
            if (result.Count > AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine this selection to at most 100,000 real paths.");
        }
        return result.ToImmutable();
        void Capture(ulong id)
        {
            if (!unique.Add(id)) return;
            if (result.Count == AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine this selection to at most 100,000 real paths.");
            result.Add(id);
        }
    }
    public static AnalysisTree Build(AnalysisSnapshot snapshot, AnalysisProjection projection, bool filterApplied, CancellationToken cancellation = default,
        Func<AnalysisNode, bool>? namespaceVisible = null)
    {
        if (snapshot.Identity.SessionId != projection.SessionId || snapshot.Identity.Epoch != projection.Epoch || snapshot.Identity.Revision != projection.Revision)
            throw new InvalidOperationException("The projection belongs to another observation revision.");
        var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var facts = snapshot.Facts.Where(f => f.ObjectId is not null).ToLookup(f => f.ObjectId!, StringComparer.Ordinal);
        var incomplete = snapshot.Issues.Where(i => i.NodeId.HasValue && i.State != MeasurementState.Excluded).Select(i => i.NodeId!.Value).ToHashSet();
        var map = new Dictionary<string, Builder>(StringComparer.Ordinal) { [RootKey] = new(RootKey, "", "All locations", null, AnalysisKind.Directory, true, true, "folder") };
        var order = new List<string> { RootKey };
        int count = 0;
        foreach (var node in snapshot.Nodes)
        {
            if ((count++ & 4095) == 0) cancellation.ThrowIfCancellationRequested();
            string key = Key(node.Id), parent = node.ParentId == 0 ? RootKey : Key(node.ParentId);
            if (!map.TryGetValue(parent, out var parentItem)) throw new InvalidDataException("Invalid analysis hierarchy.");
            bool matching = projection.MatchingNodes.Contains(node.Id);
            var item = new Builder(key, parent, AnalysisNames.DisplayName(node), node.Id, node.Kind, false,
                !filterApplied || matching || projection.AliasContextNodes.Contains(node.Id) || node.Kind == AnalysisKind.Directory && node.IncludedByPolicy && namespaceVisible?.Invoke(node) == true,
                node.Kind == AnalysisKind.Directory ? "folder" : AnalysisClassification.Family(node.Name));
            item.MetadataState = node.MetadataState; item.IncludedByPolicy = node.IncludedByPolicy;
            item.FilterContext = projection.AliasContextNodes.Contains(node.Id) && !matching;
            item.SharedElsewhere = item.FilterContext;
            item.ModifiedUtc = node.Times?.ModifiedUtc; item.ModifiedPrecisionTicks = node.Times?.ModifiedPrecisionTicks ?? 0;
            if (node.Times is null && node.ObjectId is { } objectKey && objects.TryGetValue(objectKey, out var observedObject))
            { item.ModifiedUtc = observedObject.ModifiedUtc; item.ModifiedPrecisionTicks = observedObject.ModifiedPrecisionTicks; }
            item.NameEvidence = AnalysisNames.NameEvidence(node);
            bool namespaceUnknown = incomplete.Contains(node.Id) || node.IncludedByPolicy && node.MetadataState != MeasurementState.Known;
            item.Unknown = namespaceUnknown || projection.UnmeasuredStates.ContainsKey(node.Id);
            item.LogicalUnknown = node.IncludedByPolicy && (node.Kind == AnalysisKind.Unknown || node.Kind == AnalysisKind.Directory && namespaceUnknown);
            if (node.Kind == AnalysisKind.File && matching)
            {
                item.Files = 1;
                var obj = node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
                if (obj?.LogicalLength.IsKnown == true) item.Logical = obj.LogicalLength.Value!.Value;
                else item.LogicalUnknown = true;
                item.Unknown |= !projection.Totals.CompatibleBasis || projection.UnmeasuredStates.ContainsKey(node.Id) || projection.Metric switch
                {
                    AnalysisMetric.Files => false,
                    AnalysisMetric.OnDisk => obj is null || !facts[obj.Id].Any() || facts[obj.Id].Any(f => !f.Measurement.IsKnown),
                    AnalysisMetric.UniqueFileSize => obj?.VerifiedIdentity is null || !obj.LogicalLength.IsKnown,
                    _ => obj?.LogicalLength.IsKnown != true
                };
            }
            map.Add(key, item); parentItem.Children.Add(key); order.Add(key);
        }
        foreach (var charge in projection.Charges)
        {
            cancellation.ThrowIfCancellationRequested();
            string parent = charge.ParentId == 0 ? RootKey : Key(charge.ParentId);
            if (!map.ContainsKey(parent)) throw new InvalidDataException("Invalid charge parent.");
            if (!charge.Shared && !charge.DirectorySelf && charge.Aliases.Length == 1)
            {
                var item = map[Key(charge.Aliases[0])]; item.Value = checked(item.Value + charge.Value);
                if (charge.IdentityUnverified) item.Unverified = checked(item.Unverified + charge.Value);
                item.Visible = true; continue;
            }
            string key = (charge.DirectorySelf ? "directory:" : "shared:") + parent;
            if (!map.TryGetValue(key, out var bucket))
            {
                bucket = new(key, parent, charge.Label, null, AnalysisKind.Special, true, true, charge.DirectorySelf ? "directory-allocation" : "shared");
                bucket.PathMemberStatistics = !charge.DirectorySelf;
                map.Add(key, bucket); map[parent].Children.Add(key); order.Add(key);
            }
            bucket.Value = checked(bucket.Value + charge.Value);
            if (charge.IdentityUnverified) bucket.Unverified = checked(bucket.Unverified + charge.Value);
            foreach (ulong alias in charge.Aliases)
            {
                bucket.Members.Add(alias);
                if (map.TryGetValue(Key(alias), out var aliasItem))
                {
                    aliasItem.SharedElsewhere = true;
                    if (!charge.DirectorySelf && !projection.MatchingNodes.Contains(alias))
                    { aliasItem.Visible = true; aliasItem.FilterContext = true; }
                }
            }
        }
        foreach (var missing in projection.MissingCharges)
        {
            cancellation.ThrowIfCancellationRequested();
            if (missing.DirectoryId is not null || missing.Aliases.Length < 2) continue;
            foreach (ulong alias in missing.Aliases)
                if (map.TryGetValue(Key(alias), out var item))
                {
                    item.SharedElsewhere = true;
                    if (!projection.MatchingNodes.Contains(alias)) { item.Visible = true; item.FilterContext = true; }
                }
        }
        // Charge buckets were appended after real children. Folding in reverse parent-before-child order preserves additivity.
        foreach (string key in order.AsEnumerable().Reverse())
        {
            var item = map[key];
            if (item.Parent.Length == 0) continue;
            var parent = map[item.Parent];
            parent.Value = checked(parent.Value + item.Value); parent.Logical = checked(parent.Logical + item.Logical); parent.Files = checked(parent.Files + item.Files);
            parent.Unverified = checked(parent.Unverified + item.Unverified);
            parent.Unknown |= item.Unknown; parent.SharedElsewhere |= item.SharedElsewhere; parent.Visible |= item.Visible;
            parent.LogicalUnknown |= item.LogicalUnknown;
        }
        // Member statistics describe the matching paths inside a synthetic bucket. Populate them after the
        // additive fold: their real namespace rows already contributed logical bytes and file counts to ancestors.
        foreach (var bucket in map.Values.Where(i => i.PathMemberStatistics && i.Members.Count > 0))
            foreach (ulong member in bucket.Members)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!projection.MatchingNodes.Contains(member) || !map.TryGetValue(Key(member), out var item) || item.Files == 0) continue;
                bucket.Logical = checked(bucket.Logical + item.Logical); bucket.Files = checked(bucket.Files + item.Files);
                bucket.LogicalUnknown |= item.LogicalUnknown;
            }
        return new(snapshot, projection, map.ToImmutableDictionary(p => p.Key, p => p.Value.Freeze(), StringComparer.Ordinal), filterApplied);
    }
    private sealed class Builder(string key, string parent, string name, ulong? node, AnalysisKind kind, bool synthetic, bool visible, string family)
    {
        public readonly string Parent = parent;
        public UInt128 Value, Logical, Unverified;
        public ulong Files;
        public bool Unknown, SharedElsewhere, FilterContext, Visible = visible;
        public MeasurementState MetadataState = MeasurementState.Known;
        public bool IncludedByPolicy = true;
        public bool LogicalUnknown;
        public bool PathMemberStatistics;
        public string? NameEvidence;
        public DateTimeOffset? ModifiedUtc;
        public ulong ModifiedPrecisionTicks;
        public List<string> Children = [];
        public HashSet<ulong> Members = [];
        public AnalysisTreeItem Freeze() => new(key, Parent, name, node, kind, synthetic, Visible, Value, Logical, Files, Unknown,
            SharedElsewhere, family, Children.ToImmutableArray(), Members.Order().ToImmutableArray())
            { IdentityUnverified = Unverified, MetadataState = MetadataState, IncludedByPolicy = IncludedByPolicy, IsFilterContext = FilterContext,
                LogicalUnknown = LogicalUnknown, NameEvidence = NameEvidence, ModifiedUtc = ModifiedUtc, ModifiedPrecisionTicks = ModifiedPrecisionTicks };
    }
}
