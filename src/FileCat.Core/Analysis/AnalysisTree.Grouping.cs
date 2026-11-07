using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisTree
{
    /// <summary>Grouped display parentage never replaces the captured namespace or source IDs.</summary>
    public static AnalysisTree BuildGrouped(AnalysisSnapshot snapshot, AnalysisProjection projection, AnalysisGroupingDimension dimension,
        bool filterApplied, CancellationToken cancellation = default, Func<AnalysisNode, bool>? namespaceVisible = null)
    {
        var grouped = AnalysisGrouping.Project(snapshot, projection, dimension, cancellation);
        var nodes = snapshot.Nodes.ToDictionary(n => n.Id); var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var groups = grouped.Groups.ToImmutableDictionary(g => g.Definition.Key, StringComparer.Ordinal);
        var definitions = new Dictionary<ulong, AnalysisGroupDefinition>(); var depth = new Dictionary<ulong, int>();
        var ledger = new Dictionary<(string Group, ulong Owner), GroupAmount>(); var shared = new HashSet<ulong>();
        var references = new Dictionary<string, HashSet<ulong>>(StringComparer.Ordinal);
        var incomplete = snapshot.Issues.Where(i => i.NodeId.HasValue && i.State != MeasurementState.Excluded).Select(i => i.NodeId!.Value).ToHashSet();
        foreach (var node in snapshot.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            depth.Add(node.Id, node.ParentId == 0 ? 1 : checked(depth[node.ParentId] + 1));
            if (depth[node.Id] > AnalysisLimits.Depth + 1) throw new InvalidDataException("Invalid captured grouping depth.");
            if (node.Kind == AnalysisKind.File && node.IncludedByPolicy)
                definitions.Add(node.Id, AnalysisGroupingRules.Classify(dimension, node, Object(node), snapshot.Identity, snapshot.Roots));
        }
        foreach (var group in grouped.Groups)
        {
            foreach (ulong id in group.Members)
            {
                cancellation.ThrowIfCancellationRequested();
                var amount = new GroupAmount(); ledger.Add((group.Definition.Key, id), amount);
                if (group.Kind == AnalysisGroupKind.Entries && projection.Metric == AnalysisMetric.UniqueFileSize && Object(nodes[id]) is { VerifiedIdentity: null, LogicalLength.IsKnown: true } obj)
                    amount.Unverified = checked(amount.Unverified + obj.LogicalLength.Value!.Value);
            }
            foreach (var placement in group.Charges)
            {
                cancellation.ThrowIfCancellationRequested(); var charge = placement.Charge;
                ulong owner = charge.DirectorySelf ? charge.ParentId : charge.Shared || charge.Aliases.Length > 1 ? 0 : charge.Aliases.Single();
                var amount = Amount(group.Definition.Key, owner);
                amount.Charges = checked(amount.Charges + 1);
                if (charge.IdentityUnverified) amount.Unverified = checked(amount.Unverified + charge.Value); else amount.Known = checked(amount.Known + charge.Value);
                if (owner == 0) SharedReferences(group.Definition.Key, charge.Aliases);
            }
        }
        foreach (var charge in projection.MissingCharges)
        {
            cancellation.ThrowIfCancellationRequested();
            string group = charge.DirectoryId is not null ? AnalysisGroupingRules.Directory.Key : ChargeGroup(charge.Aliases);
            ulong owner = charge.DirectoryId ?? (charge.Aliases.Length == 1 ? charge.Aliases[0] : 0);
            var amount = Amount(group, owner); amount.Missing[charge.State] = checked(amount.Missing.GetValueOrDefault(charge.State) + 1);
            amount.Charges = checked(amount.Charges + 1);
            if (owner == 0) SharedReferences(group, charge.Aliases);
        }
        var items = new Dictionary<string, AnalysisTreeItem>(StringComparer.Ordinal);
        // Canonical real IDs remain available for basket names/hidden marks and source metadata.
        // Only explicit group child lists determine geometry and navigation.
        foreach (var node in snapshot.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            var member = Member(node, false); var none = new GroupAmount();
            items.Add(Key(node.Id), AnalysisGroupedChart.MemberItem(new(member, none.Export(node.Kind == AnalysisKind.File ? 1UL : 0, projection.Totals.CompatibleBasis), false), RootKey, projection.Metric)
                with { Visible = false, NameEvidence = AnalysisNames.NameEvidence(node) });
        }
        foreach (var group in grouped.Groups)
        {
            cancellation.ThrowIfCancellationRequested();
            var children = ImmutableArray.CreateBuilder<string>(); UInt128 assigned = 0; var total = new GroupAmount();
            foreach (ulong id in group.Members)
            {
                cancellation.ThrowIfCancellationRequested();
                var node = nodes[id]; var amount = Amount(group.Definition.Key, id); total.Add(amount);
                var row = new AnalysisDatasetGroupRow(Member(node, group.Kind != AnalysisGroupKind.Entries), amount.Export(node.Kind == AnalysisKind.File ? 1UL : 0, group.Totals.CompatibleBasis), shared.Contains(id));
                var item = AnalysisGroupedChart.MemberItem(row, group.Definition.Key, projection.Metric);
                bool reference = group.Kind == AnalysisGroupKind.SharedAcrossGroups;
                string key = reference ? "group-reference:" + group.Definition.Key + ":" + Key(id) : Key(id);
                item = item with { Key = key, NameEvidence = AnalysisNames.NameEvidence(node), Unknown = item.Unknown || node.MetadataState != MeasurementState.Known || incomplete.Contains(id) };
                items[key] = item; children.Add(key); assigned = checked(assigned + item.Value);
            }
            var bucket = Amount(group.Definition.Key, 0); total.Add(bucket);
            if (bucket.Charges != 0)
            {
                string key = "group-shared:" + group.Definition.Key;
                var totals = bucket.Export(0, group.Totals.CompatibleBasis); UInt128 value = AnalysisGroupedChart.Area(totals, projection.Metric);
                items.Add(key, new(key, group.Definition.Key, "Shared files — charged once", null, AnalysisKind.Special, true, true,
                    value, 0, 0, bucket.Missing.Count != 0 || projection.Metric == AnalysisMetric.UniqueFileSize && bucket.Unverified != 0, false, "shared", [], references[group.Definition.Key].Order().ToImmutableArray())
                    { IdentityUnverified = bucket.Unverified, LogicalUnknown = true });
                children.Add(key); assigned = checked(assigned + value);
            }
            if (total.Known != group.Totals.Known || total.Unverified != group.Totals.IdentityUnverified ||
                total.Missing.Count != group.Totals.Missing.Count || total.Missing.Any(p => group.Totals.Missing.GetValueOrDefault(p.Key) != p.Value) || assigned != group.ChartArea(projection.Metric))
                throw new InvalidDataException("Grouped member display does not match its accounting scope.");
            var summary = new AnalysisDatasetGroup(group.Definition, group.Kind, group.Totals, group.Logical, (ulong)group.Members.Length, total.Charges)
                { Shared = bucket.Export(0, group.Totals.CompatibleBasis), SharedCharges = bucket.Charges };
            if (group.Kind == AnalysisGroupKind.SharedAcrossGroups)
            {
                UInt128 referenceBytes = 0; ulong referenceFiles = 0; var referenceMissing = new Dictionary<MeasurementState, ulong>();
                foreach (ulong id in group.Members)
                {
                    cancellation.ThrowIfCancellationRequested(); var node = nodes[id]; if (node.Kind != AnalysisKind.File || !node.IncludedByPolicy) continue;
                    var length = Object(node)?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported); referenceFiles = checked(referenceFiles + 1);
                    if (length.IsKnown) referenceBytes = checked(referenceBytes + length.Value!.Value); else referenceMissing[length.State] = checked(referenceMissing.GetValueOrDefault(length.State) + 1);
                }
                summary = summary with { ReferenceLogical = new(referenceBytes, referenceFiles, referenceMissing.ToImmutableDictionary()) };
            }
            items.Add(group.Definition.Key, AnalysisGroupedChart.GroupItem(summary, dimension, projection.Metric, RootKey) with { Children = children.ToImmutable(), Members = group.Members });
        }
        var rootMembers = grouped.Groups.SelectMany(g => g.Members).Distinct().Order().ToImmutableArray();
        UInt128 logical = 0; ulong files = 0, unknown = 0;
        foreach (var group in grouped.Groups)
        { cancellation.ThrowIfCancellationRequested(); logical = checked(logical + group.Logical.KnownBytes); files = checked(files + group.Logical.Files); unknown = checked(unknown + group.Logical.UnknownFiles); }
        items.Add(RootKey, new(RootKey, "", "All groups — " + AnalysisGroupingRules.Title(dimension), null, AnalysisKind.Directory, true, true,
            AnalysisGroupedChart.Area(projection.Totals, projection.Metric), logical, files,
            !projection.Totals.CompatibleBasis || projection.Totals.Missing.Count != 0 || snapshot.Coverage != NamespaceCoverage.CompleteUnderPolicy || projection.Metric == AnalysisMetric.UniqueFileSize && projection.Totals.IdentityUnverified != 0,
            false, "folder", grouped.Groups.Select(g => g.Definition.Key).ToImmutableArray(), rootMembers)
            { IdentityUnverified = projection.Totals.IdentityUnverified, LogicalUnknown = unknown != 0 });
        return new(snapshot, projection, items.ToImmutableDictionary(StringComparer.Ordinal), filterApplied)
        { GroupingDimension = dimension, Groups = groups, FilterVisible = filterApplied ? CaptureFilterVisibility(snapshot, projection, namespaceVisible, cancellation) : null };

        AnalysisObject? Object(AnalysisNode node) => node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
        AnalysisDatasetGroupMember Member(AnalysisNode node, bool reference)
        {
            var obj = Object(node);
            return new(AnalysisPathMetadata.From(node), obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported), reference,
                node.Times is null ? obj?.ModifiedUtc : node.Times.ModifiedUtc, node.Times is null ? obj?.ModifiedPrecisionTicks ?? 0 : node.Times.ModifiedPrecisionTicks) { CapturedDepth = depth[node.Id] };
        }
        GroupAmount Amount(string group, ulong owner)
        {
            if (ledger.TryGetValue((group, owner), out var amount)) return amount;
            if (!groups.ContainsKey(group) || owner != 0) throw new InvalidDataException("The grouped owner is absent from its captured members.");
            ledger.Add((group, owner), amount = new()); return amount;
        }
        void SharedReferences(string group, ImmutableArray<ulong> aliases)
        {
            if (!references.TryGetValue(group, out var members)) references.Add(group, members = []);
            foreach (ulong id in aliases) if (projection.MatchingNodes.Contains(id)) { shared.Add(id); members.Add(id); }
        }
        string ChargeGroup(ImmutableArray<ulong> aliases)
        {
            if (aliases.IsEmpty) throw new InvalidDataException("A grouped charge has no captured aliases.");
            var first = definitions[aliases[0]]; bool different = false;
            foreach (ulong id in aliases) { cancellation.ThrowIfCancellationRequested(); different |= definitions[id].Key != first.Key; }
            return different ? AnalysisGroupingRules.Shared.Key : first.Key;
        }
    }
    private sealed class GroupAmount
    {
        internal UInt128 Known, Unverified;
        internal ulong Charges;
        internal readonly Dictionary<MeasurementState, ulong> Missing = [];
        internal AnalysisTotals Export(ulong files, bool compatible) => new(Known, Unverified, files, Missing.ToImmutableDictionary(), compatible);
        internal void Add(GroupAmount other)
        {
            Known = checked(Known + other.Known); Unverified = checked(Unverified + other.Unverified);
            Charges = checked(Charges + other.Charges);
            foreach (var pair in other.Missing) Missing[pair.Key] = checked(Missing.GetValueOrDefault(pair.Key) + pair.Value);
        }
    }
}
