using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.Core.Analysis;

public enum AnalysisGroupingDimension { FolderHierarchy, FileType, Extension, ModifiedAge, CapturedLocation }
public enum AnalysisGroupKind { Entries, SharedAcrossGroups, DirectoryAllocation }
public sealed record AnalysisGroupDefinition(string Key, string Value, string Label, string Family);
public sealed record AnalysisGroupedCharge(AnalysisCharge Charge, string GroupKey);
public sealed record AnalysisGroupSummary(AnalysisGroupDefinition Definition, AnalysisGroupKind Kind, AnalysisTotals Totals,
    AnalysisLogicalTotals Logical, ImmutableArray<ulong> Members, ImmutableArray<AnalysisGroupedCharge> Charges)
{
    public UInt128 ChartArea(AnalysisMetric metric) => !Totals.CompatibleBasis ? 0 : metric == AnalysisMetric.OnDisk ?
        checked(Totals.Known + Totals.IdentityUnverified) : Totals.Known;
}
public sealed record AnalysisGroupingProjection(SessionIdentity Identity, AnalysisGroupingDimension Dimension, AnalysisMetric Metric,
    AnalysisTotals Totals, ImmutableArray<AnalysisGroupSummary> Groups, int RuleVersion = AnalysisGroupingRules.RuleVersion)
{
    public AnalysisTaint Taint { get; init; } = AnalysisTaint.OrdinarySensitive;
}

/// <summary>Versioned classification of captured entries. These keys confer no source or action authority.</summary>
public static class AnalysisGroupingRules
{
    public const int RuleVersion = 1;
    public static string Title(AnalysisGroupingDimension dimension) => dimension switch
    {
        AnalysisGroupingDimension.FolderHierarchy => "Folder hierarchy",
        AnalysisGroupingDimension.FileType => "File-type family",
        AnalysisGroupingDimension.Extension => "Filename extension",
        AnalysisGroupingDimension.ModifiedAge => "Modification age at capture",
        AnalysisGroupingDimension.CapturedLocation => "Captured location",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };
    internal static readonly AnalysisGroupDefinition Shared = Create("shared", "Shared across groups", "shared");
    internal static readonly AnalysisGroupDefinition Directory = Create("directory", "Directory allocation", "directory-allocation");
    public static AnalysisGroupDefinition Classify(AnalysisGroupingDimension dimension, AnalysisNode node, AnalysisObject? obj,
        SessionIdentity identity, IReadOnlyList<RootBinding> roots)
    {
        if (node.Kind != AnalysisKind.File) throw new ArgumentException("Grouping classification requires a real captured file entry.", nameof(node));
        switch (dimension)
        {
            case AnalysisGroupingDimension.FileType:
                string family = AnalysisRawNames.HasLosslessText(node) ? AnalysisClassification.Family(node.Name) : "unknown";
                return Create("type:" + family, family == "other" ? "Other / unclassified extension" :
                    AnalysisChartColors.Legend(AnalysisColorDimension.FileType).First(c => c.Key == family).Label, family);
            case AnalysisGroupingDimension.Extension:
                if (!AnalysisRawNames.HasLosslessText(node)) return Create("extension:unknown", "Unknown extension (raw name)", "unknown");
                string extension = AnalysisNames.Extension(node.Name).ToLowerInvariant();
                string label = extension.Length == 0 ? "No extension" : "." + AnalysisNames.Display(extension.Replace("\\", "\\\\", StringComparison.Ordinal), 200, escapeTrailing: true);
                var definition = Create("extension:" + (extension.Length == 0 ? "none" : "value:" + extension), label, AnalysisClassification.Family(node.Name));
                return label.EndsWith('…') ? definition with { Label = label + " [" + definition.Key[^12..] + "]" } : definition;
            case AnalysisGroupingDimension.ModifiedAge:
                string age = node.MetadataState != MeasurementState.Known || !node.IncludedByPolicy ? "unknown" : AnalysisChartColors.Age(
                    node.Times is null ? obj?.ModifiedUtc : node.Times.ModifiedUtc,
                    node.Times is null ? obj?.ModifiedPrecisionTicks ?? 0 : node.Times.ModifiedPrecisionTicks, identity.ObservedUtc);
                return Create("age:" + age, AnalysisChartColors.Legend(AnalysisColorDimension.ModifiedAge).First(c => c.Key == age).Label, "folder");
            case AnalysisGroupingDimension.CapturedLocation:
                var root = roots.FirstOrDefault(r => r.Id == node.RootId) ?? throw new InvalidDataException("The captured location is absent.");
                return Create("location:" + root.Id, AnalysisNames.Display(root.Label, 160) + " [" + AnalysisNames.Display(root.Id, 64) + "]", "folder");
            case AnalysisGroupingDimension.FolderHierarchy:
                throw new ArgumentException("Folder hierarchy uses the captured namespace projection.", nameof(dimension));
            default: throw new ArgumentOutOfRangeException(nameof(dimension));
        }
    }
    internal static AnalysisGroupDefinition Create(string value, string label, string family) => new(
        "group:" + RuleVersion.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))), value, label, family);
}

/// <summary>Memory-backed grouping with attribution across ALL captured aliases before filtering.</summary>
public static class AnalysisGrouping
{
    public static AnalysisGroupingProjection Project(AnalysisSnapshot snapshot, AnalysisProjection projection,
        AnalysisGroupingDimension dimension, CancellationToken cancellation = default)
    {
        if (dimension == AnalysisGroupingDimension.FolderHierarchy || !Enum.IsDefined(dimension)) throw new ArgumentOutOfRangeException(nameof(dimension));
        if (snapshot.Identity.SessionId != projection.SessionId || snapshot.Identity.Epoch != projection.Epoch || snapshot.Identity.Revision != projection.Revision)
            throw new InvalidOperationException("The grouping belongs to another observation revision.");
        var nodes = snapshot.Nodes.ToDictionary(n => n.Id); var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var definitions = new Dictionary<ulong, AnalysisGroupDefinition>();
        foreach (var node in snapshot.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (node.Kind != AnalysisKind.File || !node.IncludedByPolicy) continue;
            var obj = node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
            var definition = AnalysisGroupingRules.Classify(dimension, node, obj, snapshot.Identity, snapshot.Roots);
            definitions.Add(node.Id, definition);
            if (!projection.MatchingNodes.Contains(node.Id)) continue;
            var group = Get(definition, AnalysisGroupKind.Entries); group.Members.Add(node.Id); group.Files = checked(group.Files + 1);
            if (obj?.LogicalLength.IsKnown == true) group.Logical = checked(group.Logical + obj.LogicalLength.Value!.Value);
            else
            {
                var state = obj?.LogicalLength.State ?? MeasurementState.Unsupported;
                if (state == MeasurementState.Known) state = MeasurementState.Unsupported;
                group.LogicalMissing[state] = checked(group.LogicalMissing.GetValueOrDefault(state) + 1);
            }
            // Unique file size retains unverified path evidence separately from asserted chart area.
            if (projection.Metric == AnalysisMetric.UniqueFileSize && obj is { VerifiedIdentity: null, LogicalLength.IsKnown: true })
                group.Unverified = checked(group.Unverified + obj.LogicalLength.Value!.Value);
        }
        foreach (var charge in projection.Charges)
        {
            cancellation.ThrowIfCancellationRequested();
            var group = charge.DirectorySelf ? Get(AnalysisGroupingRules.Directory, AnalysisGroupKind.DirectoryAllocation) : ChargeGroup(charge.Aliases);
            if (charge.DirectorySelf) group.Members.Add(charge.ParentId);
            if (charge.IdentityUnverified) group.Unverified = checked(group.Unverified + charge.Value); else group.Known = checked(group.Known + charge.Value);
            group.Charges.Add(new(charge, group.Definition.Key));
            if (group.Kind == AnalysisGroupKind.SharedAcrossGroups)
                foreach (ulong alias in charge.Aliases) if (projection.MatchingNodes.Contains(alias)) group.Members.Add(alias);
        }
        foreach (var charge in projection.MissingCharges)
        {
            cancellation.ThrowIfCancellationRequested();
            var group = charge.DirectoryId is { } directory ? Get(AnalysisGroupingRules.Directory, AnalysisGroupKind.DirectoryAllocation) : ChargeGroup(charge.Aliases);
            if (charge.DirectoryId is { } owner) group.Members.Add(owner);
            group.Missing[charge.State] = checked(group.Missing.GetValueOrDefault(charge.State) + 1);
            if (group.Kind == AnalysisGroupKind.SharedAcrossGroups)
                foreach (ulong alias in charge.Aliases) if (projection.MatchingNodes.Contains(alias)) group.Members.Add(alias);
        }
        var result = groups.Values.Select(g => new AnalysisGroupSummary(g.Definition, g.Kind,
            new(g.Known, g.Unverified, g.Files, g.Missing.ToImmutableDictionary(), projection.Totals.CompatibleBasis),
            new(g.Logical, g.Files, g.LogicalMissing.ToImmutableDictionary()), g.Members.Order().ToImmutableArray(), g.Charges.ToImmutableArray()))
            .OrderByDescending(g => g.ChartArea(projection.Metric)).ThenBy(g => g.Definition.Key, StringComparer.Ordinal).ToImmutableArray();
        UInt128 known = 0, uncertain = 0; ulong files = 0; var missing = new Dictionary<MeasurementState, ulong>();
        foreach (var group in result)
        {
            cancellation.ThrowIfCancellationRequested();
            known = checked(known + group.Totals.Known); uncertain = checked(uncertain + group.Totals.IdentityUnverified); files = checked(files + group.Totals.MatchingPaths);
            foreach (var entry in group.Totals.Missing) missing[entry.Key] = checked(missing.GetValueOrDefault(entry.Key) + entry.Value);
        }
        if (known != projection.Totals.Known || uncertain != projection.Totals.IdentityUnverified || files != projection.Totals.MatchingPaths ||
            missing.Count != projection.Totals.Missing.Count || missing.Any(p => projection.Totals.Missing.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidDataException("Grouped accounting does not match the captured hierarchy projection.");
        return new(snapshot.Identity, dimension, projection.Metric, projection.Totals, result) { Taint = AnalysisPrivacy.Join(snapshot.Taint, projection.Taint) };

        Group Get(AnalysisGroupDefinition definition, AnalysisGroupKind kind)
        {
            if (groups.TryGetValue(definition.Key, out var found))
            { if (found.Definition.Value != definition.Value || found.Kind != kind) throw new InvalidDataException("Conflicting group keys."); return found; }
            var created = new Group(definition, kind); groups.Add(definition.Key, created); return created;
        }
        Group ChargeGroup(ImmutableArray<ulong> aliases)
        {
            if (aliases.IsEmpty) throw new InvalidDataException("A file charge has no captured aliases.");
            AnalysisGroupDefinition? first = null; bool shared = false;
            foreach (ulong alias in aliases)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!nodes.ContainsKey(alias) || !definitions.TryGetValue(alias, out var definition)) throw new InvalidDataException("The charge alias is absent or excluded.");
                if (first is null) first = definition;
                else if (first.Value != definition.Value) shared = true;
            }
            return shared ? Get(AnalysisGroupingRules.Shared, AnalysisGroupKind.SharedAcrossGroups) : Get(first!, AnalysisGroupKind.Entries);
        }
    }
    private sealed class Group(AnalysisGroupDefinition definition, AnalysisGroupKind kind)
    {
        internal readonly AnalysisGroupDefinition Definition = definition;
        internal readonly AnalysisGroupKind Kind = kind;
        internal UInt128 Known, Unverified, Logical;
        internal ulong Files;
        internal readonly HashSet<ulong> Members = [];
        internal readonly List<AnalysisGroupedCharge> Charges = [];
        internal readonly Dictionary<MeasurementState, ulong> Missing = [], LogicalMissing = [];
    }
}
