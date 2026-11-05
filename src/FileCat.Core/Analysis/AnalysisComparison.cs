using System.Collections.Immutable;
using System.Globalization;

namespace FileCat.Core.Analysis;

public enum AnalysisChange { Added, Removed, Growing, Shrinking, Moved, Attribution, Replaced, MetadataChanged, NoObservedMetadataChange, PreviouslyUnknown, Unknown }
public enum AnalysisComparisonEvidence { PathBased, CompatibleIdentityAndVersion, Structural }

/// <summary>Exact signed UInt128 difference, including values outside the signed Int128 range.</summary>
public readonly record struct AnalysisDelta(bool Negative, UInt128 Magnitude)
{
    public static AnalysisDelta Between(UInt128 previous, UInt128 current) => current >= previous ? new(false, current - previous) : new(true, previous - current);
    public override string ToString() => (Magnitude == 0 ? "" : Negative ? "−" : "+") + Magnitude.ToString(CultureInfo.InvariantCulture);
    public double? RelativePercent(UInt128 previous) => previous == 0 ? null : (Negative ? -1d : 1d) * (double)Magnitude / (double)previous * 100d;
}

public sealed record AnalysisComparisonCompatibility(bool SamePolicy, bool SameBasis, bool SameScope, bool VerifiedIdentityContext,
    bool PreviousComplete, bool CurrentComplete, ImmutableArray<string> Explanations)
{
    public bool ComparableValues => SamePolicy && SameBasis && SameScope;
    public bool ComparableTotals => ComparableValues && PreviousComplete && CurrentComplete;
}

/// <summary>One metric accounting entity. Shared object aliases are one row for Unique file size and On disk.</summary>
public sealed record AnalysisComparisonEntity(ImmutableArray<ulong> NodeIds, ImmutableArray<string> Paths, AnalysisMeasurement Value,
    string? ObservedIdentity, string? VersionEvidence, DateTimeOffset? ModifiedUtc, bool Fresh)
{
    internal ImmutableArray<string> Keys { get; init; } = [];
    internal string AttributionKey { get; init; } = "";
    internal string MetadataFingerprint { get; init; } = "";
    internal bool Mapped { get; init; }
}

public sealed record AnalysisComparisonRow(AnalysisComparisonEntity? Previous, AnalysisComparisonEntity? Current,
    AnalysisChange Change, AnalysisComparisonEvidence Evidence, AnalysisDelta? Delta, string Explanation)
{
    public double? RelativePercent => Delta is { } delta && Previous?.Value is { IsKnown: true } old ? delta.RelativePercent(old.Value!.Value) : null;
}

/// <summary>Inert derived observations and their bounded work/result memory lease. No live source is acquired.</summary>
public sealed class AnalysisComparisonResult : IDisposable
{
    private IDisposable? _memory;
    private ImmutableArray<AnalysisComparisonRow> _rows;
    internal AnalysisComparisonResult(AnalysisMetric metric, AnalysisComparisonCompatibility compatibility, AnalysisTotals previous,
        AnalysisTotals current, ImmutableArray<AnalysisComparisonRow> rows, AnalysisTaint taint, IDisposable memory)
    {
        Metric = metric; Compatibility = compatibility; PreviousTotals = previous; CurrentTotals = current; _rows = rows; Taint = taint; _memory = memory;
        NetChange = compatibility.ComparableTotals && previous.CompatibleBasis && current.CompatibleBasis && previous.Missing.Count == 0 && current.Missing.Count == 0 &&
            previous.IdentityUnverified == 0 && current.IdentityUnverified == 0 ? AnalysisDelta.Between(previous.Known, current.Known) : null;
    }
    public AnalysisMetric Metric { get; }
    public AnalysisComparisonCompatibility Compatibility { get; }
    public AnalysisTotals PreviousTotals { get; }
    public AnalysisTotals CurrentTotals { get; }
    public ImmutableArray<AnalysisComparisonRow> Rows => Volatile.Read(ref _memory) is not null ? _rows : throw new ObjectDisposedException(nameof(AnalysisComparisonResult));
    public AnalysisTaint Taint { get; }
    public AnalysisDelta? NetChange { get; }
    public void Dispose()
    {
        var memory = Interlocked.Exchange(ref _memory, null); if (memory is null) return;
        _rows = []; memory.Dispose();
    }
}

/// <summary>
/// Bounded memory comparison fallback. Paths are matched by exact tagged raw components, never host normalization.
/// An explicit mapping permits structural comparison; neither a mapping nor saved identity is source/action authority.
/// Large indexed comparisons and presentation qualification remain separate work.
/// </summary>
public static class AnalysisComparison
{
    // One work/result lease admits the 32-MiB fallback; inputs and path keys have lower producer caps.
    private const long InputBudget = 12L * 1024 * 1024, PathBudget = 8L * 1024 * 1024;
    private static int s_active;
    private sealed class MemoryLease : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Exchange(ref s_active, 0); }
    }

    /// <summary>Data-only suggestions for captured roots. These strings never bind or open a source.</summary>
    public static ImmutableDictionary<string, string> SuggestRootMapping(AnalysisSnapshot previous, AnalysisSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        return MapRoots(previous, current, null);
    }

    public static AnalysisComparisonResult Build(AnalysisSnapshot previous, AnalysisSnapshot current, AnalysisMetric metric,
        IReadOnlyDictionary<string, string>? structuralRoots = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        cancellation.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref s_active, 1, 0) != 0) throw new InvalidOperationException("The bounded comparison memory budget is in use. Close the other comparison first.");
        IDisposable? memory = new MemoryLease();
        try
        {
            Preflight(previous, current, cancellation);
            AnalysisValidation.Validate(previous, cancellation); AnalysisValidation.Validate(current, cancellation);
            var mapping = MapRoots(previous, current, structuralRoots);
            var oldProjection = AnalysisAggregate.Project(previous, metric, cancellation: cancellation);
            var newProjection = AnalysisAggregate.Project(current, metric, cancellation: cancellation);
            var compatibility = Compatibility(previous, current, metric, mapping, structuralRoots is not null, oldProjection, newProjection);
            long pathBytes = 0;
            var oldEntities = Entities(previous, metric, oldProjection, mapping, old: true, ref pathBytes, cancellation);
            var newEntities = Entities(current, metric, newProjection, mapping, old: false, ref pathBytes, cancellation);
            var rows = Match(oldEntities, newEntities, compatibility, metric, structuralRoots is not null, cancellation);
            var result = new AnalysisComparisonResult(metric, compatibility, oldProjection.Totals, newProjection.Totals, rows,
                AnalysisPrivacy.Join(previous.Taint, current.Taint), memory); memory = null; return result;
        }
        finally { memory?.Dispose(); }
    }

    private static void Preflight(AnalysisSnapshot previous, AnalysisSnapshot current, CancellationToken cancellation)
    {
        long cost = 0;
        void TextCost(params string?[] fields)
        {
            foreach (string? field in fields) { cost = checked(cost + (field?.Length ?? 0) * 8L); if (cost > InputBudget) throw Limit(); }
        }
        foreach (var s in new[] { previous, current })
        {
            if (s.Roots.IsDefault || s.Nodes.IsDefault || s.Objects.IsDefault || s.Facts.IsDefault || s.Issues.IsDefault) throw new InvalidDataException("Invalid comparison observation arrays.");
            if (s.Roots.Length is < 1 or > AnalysisLimits.Roots) throw new InvalidDataException("Invalid comparison root count.");
            cost = checked(cost + ((long)s.Nodes.Length + s.Objects.Length + s.Facts.Length + s.Issues.Length) * 1024);
            if (cost > InputBudget) throw Limit();
            foreach (var root in s.Roots)
            {
                if (root is null) throw new InvalidDataException("Invalid comparison root.");
                TextCost(root.Id, root.DisplayLocator, root.CanonicalLocator, root.SourceIdentity, root.Label, root.ConnectionIdentityReference);
            }
            foreach (var n in s.Nodes)
            {
                if (n is null || n.Name is null || n.RawName.IsDefault) throw new InvalidDataException("Invalid comparison observation.");
                if (n.IssueCodes.IsDefault || n.IssueCodes.Length > AnalysisLimits.Issues) throw new InvalidDataException("Invalid comparison issue references.");
                cancellation.ThrowIfCancellationRequested(); cost = checked(cost + n.Name.Length * 16L + n.RawName.Length * 8L + n.IssueCodes.Length * 16L);
                if (cost > InputBudget) throw Limit();
                TextCost(n.RootId, n.ObservedIdentity, n.NameEncoding); foreach (string code in n.IssueCodes) TextCost(code);
            }
            foreach (var obj in s.Objects)
            {
                cancellation.ThrowIfCancellationRequested(); if (obj is null || obj.ObservedAliases.IsDefault) throw new InvalidDataException("Invalid comparison object.");
                cost = checked(cost + obj.ObservedAliases.Length * 16L); TextCost(obj.Id, obj.VerifiedIdentity, obj.VersionEvidence);
            }
            foreach (var fact in s.Facts)
            {
                cancellation.ThrowIfCancellationRequested(); if (fact is null || fact.Includes.IsDefault) throw new InvalidDataException("Invalid comparison storage fact.");
                if (fact.Includes.Length > AnalysisLimits.Components) throw new InvalidDataException("Invalid comparison component references.");
                cost = checked(cost + fact.Includes.Length * 16L);
                TextCost(fact.Id, fact.ObjectId, fact.ComponentId, fact.BasisId, fact.Method);
                foreach (string included in fact.Includes) TextCost(included);
            }
            foreach (var issue in s.Issues)
            { if (issue is null) throw new InvalidDataException("Invalid comparison issue."); TextCost(issue.Code, issue.RootId); }
        }
    }

    private static ImmutableDictionary<string, string> MapRoots(AnalysisSnapshot old, AnalysisSnapshot current, IReadOnlyDictionary<string, string>? supplied)
    {
        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal); var used = new HashSet<string>(StringComparer.Ordinal);
        if (supplied is not null)
        {
            if (supplied.Count is < 1 or > AnalysisLimits.Roots) throw new ArgumentException("Choose explicit previous/current roots for structural comparison.", nameof(supplied));
            foreach (var pair in supplied)
            {
                var before = old.Roots.SingleOrDefault(r => r.Id == pair.Key); var after = current.Roots.SingleOrDefault(r => r.Id == pair.Value);
                if (before is null || after is null || before.Kind != after.Kind || before.NameRules != after.NameRules ||
                    before.Disposition != AnalysisRootDisposition.Active || after.Disposition != AnalysisRootDisposition.Active || !used.Add(after.Id))
                    throw new ArgumentException("Structural root mappings must be unique compatible active roots.", nameof(supplied));
                map.Add(before.Id, after.Id);
            }
        }
        else
        {
            foreach (var before in old.Roots.Where(r => r.Disposition == AnalysisRootDisposition.Active))
            {
                var matches = current.Roots.Where(r => r.Disposition == AnalysisRootDisposition.Active && r.Scheme == before.Scheme && r.Kind == before.Kind &&
                    r.NameRules == before.NameRules && r.CanonicalLocator == before.CanonicalLocator && r.SourceIdentity == before.SourceIdentity).Take(2).ToArray();
                if (matches.Length == 1 && used.Add(matches[0].Id)) map.Add(before.Id, matches[0].Id);
            }
        }
        if (map.Count == 0) throw new InvalidOperationException("These inventories have no matching captured roots. Choose an inventory of the same captured scope, or provide an explicit structural root mapping. No identity continuity can be inferred.");
        return map.ToImmutable();
    }

    private static AnalysisComparisonCompatibility Compatibility(AnalysisSnapshot old, AnalysisSnapshot current, AnalysisMetric metric,
        ImmutableDictionary<string, string> map, bool structural, AnalysisProjection oldProjection, AnalysisProjection newProjection)
    {
        bool policy = old.Policy.Fingerprint == current.Policy.Fingerprint;
        bool rootsMatch = map.Count == old.Roots.Count(r => r.Disposition == AnalysisRootDisposition.Active) && map.Count == current.Roots.Count(r => r.Disposition == AnalysisRootDisposition.Active);
        var oldEncodings = old.Nodes.ToLookup(n => n.RootId, n => n.NameEncoding, StringComparer.Ordinal);
        var newEncodings = current.Nodes.ToLookup(n => n.RootId, n => n.NameEncoding, StringComparer.Ordinal);
        bool namesMatch = map.All(pair => oldEncodings[pair.Key].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).SequenceEqual(newEncodings[pair.Value].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal));
        bool scope = rootsMatch && namesMatch;
        bool identity = !structural && old.Identity.HostProfile == current.Identity.HostProfile && map.All(pair =>
        {
            var a = old.Roots.Single(r => r.Id == pair.Key); var b = current.Roots.Single(r => r.Id == pair.Value);
            return a.Assurance == IdentityAssurance.Verified && b.Assurance == IdentityAssurance.Verified && a.SourceIdentity is not null && a.SourceIdentity == b.SourceIdentity;
        });
        string[] Bases(AnalysisSnapshot s, bool directories) => s.Facts.Where(f => f.DirectoryId.HasValue == directories).Select(f => f.ComponentId + "\0" + f.BasisId + "\0" + string.Join('\0', f.Includes.Order(StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        bool EmptyFileBasis(AnalysisSnapshot empty, AnalysisSnapshot other) => empty.Policy.BasisIds.Length > 0 &&
            !empty.Nodes.Any(n => n.IncludedByPolicy && n.Kind is AnalysisKind.File or AnalysisKind.Unknown) &&
            other.Facts.Where(f => f.ObjectId is not null).All(f => empty.Policy.BasisIds.Contains(f.BasisId, StringComparer.Ordinal));
        var oldFiles = Bases(old, false); var newFiles = Bases(current, false);
        bool fileBasis = oldFiles.SequenceEqual(newFiles, StringComparer.Ordinal) || oldFiles.Length == 0 && EmptyFileBasis(old, current) || newFiles.Length == 0 && EmptyFileBasis(current, old);
        bool basis = metric != AnalysisMetric.OnDisk || oldProjection.Totals.CompatibleBasis && newProjection.Totals.CompatibleBasis && fileBasis && Bases(old, true).SequenceEqual(Bases(current, true), StringComparer.Ordinal);
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (!policy) reasons.Add("Scan policies differ. Values are shown side by side; affected differences are unknown.");
        if (!basis) reasons.Add("Allocation components or bases differ. No combined allocation difference is inferred.");
        if (!rootsMatch) reasons.Add("Only some captured roots match. Unmapped observations and the total difference are unknown.");
        if (!namesMatch) reasons.Add("Raw name representations differ between these roots. Membership changes and differences are unknown until an explicit compatible representation is available.");
        if (structural) reasons.Add("Explicit structural comparison. Matching paths do not establish object identity or continuity.");
        else if (!identity) reasons.Add("Path-based comparison. Host/profile or verified source identity continuity is unavailable.");
        bool Complete(AnalysisSnapshot s) => s.Phase == AnalysisPhase.Finished && s.Coverage == NamespaceCoverage.CompleteUnderPolicy && s.Freshness == AnalysisFreshness.Observed &&
            !s.Nodes.Any(n => n.IncludedByPolicy && (n.Kind == AnalysisKind.Unknown || n.MetadataState is MeasurementState.Failed or MeasurementState.Changed or MeasurementState.Pending)) &&
            !s.Issues.Any(i => i.State is MeasurementState.Failed or MeasurementState.Changed or MeasurementState.Pending) &&
            (s.CoverageDetails is not { } c || c.DenominatorFinal && c.EnumerationFailed == 0 && c.EnumerationPending == 0 && c.FailedIssues == 0 && c.PendingIssues == 0 && c.StaleScopeNodes.IsEmpty && c.StaleRootIds.IsEmpty);
        bool aComplete = Complete(old), bComplete = Complete(current);
        if (!aComplete) reasons.Add("Earlier coverage is incomplete or stale. Missing earlier entries are previously unknown, not additions.");
        if (!bComplete) reasons.Add("Current coverage is incomplete or stale. Missing current entries are unknown, not removals.");
        if (old.Identity.ObservedUtc > current.Identity.ObservedUtc) reasons.Add("The chosen previous inventory was observed later. Differences follow the selected Previous/Current order, not chronological growth.");
        reasons.Add("All values are metadata observations over an interval. Unchanged metadata does not prove unchanged content.");
        return new(policy, basis, scope, identity, aComplete, bComplete, reasons.ToImmutable());
    }

    private static ImmutableArray<AnalysisComparisonEntity> Entities(AnalysisSnapshot s, AnalysisMetric metric, AnalysisProjection projection,
        ImmutableDictionary<string, string> map, bool old, ref long pathBytes, CancellationToken cancellation)
    {
        var objects = s.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal); var roots = s.Roots.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var keys = new Dictionary<ulong, string>(); var paths = new Dictionary<ulong, string>(); var nodeIndex = new Dictionary<ulong, AnalysisNode>(); var fresh = new Dictionary<ulong, bool>();
        var staleNodes = (s.CoverageDetails?.StaleScopeNodes ?? []).Concat(s.Issues.Where(i => i.State == MeasurementState.Changed && i.NodeId.HasValue).Select(i => i.NodeId!.Value)).ToHashSet();
        var staleRoots = (s.CoverageDetails?.StaleRootIds ?? []).Concat(s.Issues.Where(i => i.State == MeasurementState.Changed && i.RootId is not null).Select(i => i.RootId!)).ToHashSet(StringComparer.Ordinal);
        var mapped = old ? map.Keys.ToHashSet(StringComparer.Ordinal) : map.Values.ToHashSet(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<AnalysisNode>>(StringComparer.Ordinal); var nodeGroups = new Dictionary<ulong, string>();
        foreach (var node in s.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            nodeIndex.Add(node.Id, node);
            fresh.Add(node.Id, s.Freshness == AnalysisFreshness.Observed && node.MetadataState == MeasurementState.Known && !staleNodes.Contains(node.Id) &&
                !staleRoots.Contains(node.RootId) && (node.ParentId == 0 || fresh[node.ParentId]));
            static string RootToken(string id) => Convert.ToHexString(AnalysisRawNames.Encode("utf16le", id));
            string prefix = node.ParentId == 0 ? mapped.Contains(node.RootId) ? "root:" + RootToken(old ? map[node.RootId] : node.RootId) : (old ? "unmapped-old:" : "unmapped-current:") + RootToken(node.RootId) : keys[node.ParentId];
            int raw = node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name).Length : node.RawName.Length;
            long keyLength = node.ParentId == 0 ? prefix.Length : checked(prefix.Length + node.NameEncoding.Length + raw * 2L + 2);
            string parentPath = node.ParentId == 0 ? "" : paths[node.ParentId];
            long displayMaximum = node.ParentId == 0 ? roots[node.RootId].CanonicalLocator.Length * 8L : parentPath.Length + node.Name.Length * 16L + raw * 2L + 32;
            if ((pathBytes = checked(pathBytes + (keyLength + displayMaximum) * 2L + 128)) > PathBudget) throw Limit();
            keys.Add(node.Id, node.ParentId == 0 ? prefix : prefix + "/" + node.NameEncoding + ":" + Convert.ToHexString(node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name) : node.RawName.AsSpan()));
            string separator = roots[node.RootId].NameRules == AnalysisNameRules.Windows ? "\\" : "/";
            paths.Add(node.Id, node.ParentId == 0 ? AnalysisNames.Display(roots[node.RootId].CanonicalLocator, AnalysisLimits.LocatorBytes) :
                parentPath + (parentPath.EndsWith(separator, StringComparison.Ordinal) ? "" : separator) + AnalysisNames.DisplayName(node, AnalysisLimits.RawNameBytes));
            if (!node.IncludedByPolicy) continue;
            if (node.Kind is AnalysisKind.File or AnalysisKind.Unknown)
            {
                var obj = node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
                string group = metric is AnalysisMetric.FileSize or AnalysisMetric.Files || obj?.VerifiedIdentity is null ? "path:" + node.Id : "object:" + obj.VerifiedIdentity;
                if (!groups.TryGetValue(group, out var members)) groups.Add(group, members = []);
                members.Add(node); nodeGroups.Add(node.Id, group);
            }
        }
        if (metric == AnalysisMetric.OnDisk)
            foreach (var id in s.Facts.Where(f => f.DirectoryId.HasValue).Select(f => f.DirectoryId!.Value).Distinct())
            { var node = nodeIndex[id]; if (node.IncludedByPolicy) groups.Add("directory:" + id, [node]); }
        var amounts = new Dictionary<string, UInt128>(StringComparer.Ordinal); var attribution = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var charge in projection.Charges)
        {
            cancellation.ThrowIfCancellationRequested();
            string group = charge.DirectorySelf ? "directory:" + charge.ParentId : nodeGroups[charge.Aliases[0]];
            amounts[group] = checked(amounts.GetValueOrDefault(group) + charge.Value);
            attribution[group] = charge.ParentId == 0 ? "scope" : keys[charge.ParentId];
        }
        var result = ImmutableArray.CreateBuilder<AnalysisComparisonEntity>();
        foreach (var pair in groups)
        {
            cancellation.ThrowIfCancellationRequested(); var members = pair.Value;
            var states = members.Select(n => projection.UnmeasuredStates.GetValueOrDefault(n.Id, n.Kind == AnalysisKind.Unknown ? MeasurementState.Failed : MeasurementState.Known)).ToArray();
            var unavailable = states.FirstOrDefault(state => state != MeasurementState.Known);
            bool known = unavailable == MeasurementState.Known && amounts.ContainsKey(pair.Key);
            var objectValues = members.Where(n => n.ObjectId is not null).Select(n => objects[n.ObjectId!]).DistinctBy(o => o.Id).ToArray();
            string? Single(Func<AnalysisObject, string?> select) { var values = objectValues.Select(select).Distinct(StringComparer.Ordinal).ToArray(); return values.Length == 1 ? values[0] : null; }
            string tag = pair.Key.StartsWith("directory:", StringComparison.Ordinal) ? "directory:" : "file:";
            var entity = new AnalysisComparisonEntity(members.Select(n => n.Id).ToImmutableArray(), members.Select(n => paths[n.Id]).Order(StringComparer.Ordinal).ToImmutableArray(),
                known ? AnalysisMeasurement.Known(amounts[pair.Key]) : AnalysisMeasurement.Missing(unavailable == MeasurementState.Known ? MeasurementState.Unsupported : unavailable),
                Single(o => o.VerifiedIdentity), Single(o => o.VersionEvidence), objectValues.Length == 0 ? members[0].Times?.ModifiedUtc : objectValues[0].ModifiedUtc,
                members.All(n => fresh[n.Id]))
            { Keys = members.Select(n => tag + keys[n.Id]).Order(StringComparer.Ordinal).ToImmutableArray(), AttributionKey = attribution.GetValueOrDefault(pair.Key, ""),
                MetadataFingerprint = string.Join(';', objectValues.Select(o => $"{o.LogicalLength.State}:{o.LogicalLength.Value?.ToString(CultureInfo.InvariantCulture)}:{o.LinkCount.State}:{o.LinkCount.Value?.ToString(CultureInfo.InvariantCulture)}:{o.ModifiedUtc:O}:{o.ModifiedPrecisionTicks}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + "|" +
                    string.Join(';', members.Select(n => $"{n.Kind}:{n.Sparse}:{n.Compressed}:{n.Cloud}:{n.Times?.ModifiedUtc:O}:{n.Times?.ModifiedPrecisionTicks}:{n.Times?.CreatedUtc:O}:{n.Times?.CreatedPrecisionTicks}:{n.Times?.ChangedUtc:O}:{n.Times?.ChangedPrecisionTicks}").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                Mapped = members.All(n => mapped.Contains(n.RootId)) };
            result.Add(entity);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<AnalysisComparisonRow> Match(ImmutableArray<AnalysisComparisonEntity> old, ImmutableArray<AnalysisComparisonEntity> current,
        AnalysisComparisonCompatibility compatibility, AnalysisMetric metric, bool structural, CancellationToken cancellation)
    {
        var oldPaths = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < old.Length; i++) foreach (string key in old[i].Keys) oldPaths.Add(key, i);
        var identities = old.Select((e, i) => (e, i)).Where(p => p.e.ObservedIdentity is not null && p.e.VersionEvidence is not null).ToLookup(p => (p.e.ObservedIdentity!, p.e.VersionEvidence!));
        var currentIdentities = current.Where(e => e.ObservedIdentity is not null && e.VersionEvidence is not null).ToLookup(e => (e.ObservedIdentity!, e.VersionEvidence!));
        var links = new HashSet<int>[current.Length]; var incoming = new int[old.Length];
        for (int i = 0; i < current.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var e = current[i]; var candidates = e.Keys.Where(oldPaths.ContainsKey).Select(k => oldPaths[k]).ToHashSet();
            if (candidates.Count == 0 && compatibility.VerifiedIdentityContext && e.Mapped && e.ObservedIdentity is { } identity && e.VersionEvidence is { } version &&
                currentIdentities[(identity, version)].Count() == 1)
            {
                var matches = identities[(identity, version)].Where(p => p.e.Mapped && p.e.Fresh && e.Fresh && p.e.MetadataFingerprint == e.MetadataFingerprint).Take(2).ToArray();
                if (matches.Length == 1) candidates.Add(matches[0].i);
            }
            links[i] = candidates; foreach (int candidate in candidates) incoming[candidate]++;
        }
        var rows = ImmutableArray.CreateBuilder<AnalysisComparisonRow>(); var used = new bool[old.Length];
        for (int i = 0; i < current.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var b = current[i]; var link = links[i];
            if (link.Count > 1 || link.Count == 1 && incoming[link.Single()] != 1)
            { rows.Add(new(null, b, AnalysisChange.Unknown, Evidence(null, b), null, "Alias groups split, merge or match ambiguously. No zero baseline or identity continuity was inferred.")); continue; }
            if (link.Count == 0)
            { rows.Add(Absent(null, b, compatibility.PreviousComplete)); continue; }
            int at = link.Single(); used[at] = true; var a = old[at];
            var evidence = Evidence(a, b);
            if (!compatibility.ComparableValues || !a.Mapped || !b.Mapped || !a.Fresh || !b.Fresh || !a.Value.IsKnown || !b.Value.IsKnown)
            { rows.Add(new(a, b, AnalysisChange.Unknown, evidence, null, "The measurements, scope, coverage freshness or policy do not support this difference.")); continue; }
            var delta = AnalysisDelta.Between(a.Value.Value!.Value, b.Value.Value!.Value);
            bool samePaths = a.Keys.SequenceEqual(b.Keys, StringComparer.Ordinal), sameIdentity = a.ObservedIdentity is not null && a.ObservedIdentity == b.ObservedIdentity;
            AnalysisChange change; string explanation;
            if (a.ObservedIdentity is not null && b.ObservedIdentity is not null && a.ObservedIdentity != b.ObservedIdentity)
            { change = AnalysisChange.Replaced; explanation = "A different observed object occupies this path. This is a path-value difference, not continuity of the old file."; }
            else if (delta.Magnitude != 0)
            { change = delta.Negative ? AnalysisChange.Shrinking : AnalysisChange.Growing; explanation = "Observed metric difference; reported allocation is not a promise of consumed or reclaimable physical space."; }
            else if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk && sameIdentity && (!samePaths || a.AttributionKey != b.AttributionKey))
            { change = AnalysisChange.Attribution; explanation = "Aliases or their common-parent accounting changed with the same observed amount. No storage growth or unchanged-content proof is inferred."; }
            else if (!samePaths && evidence == AnalysisComparisonEvidence.CompatibleIdentityAndVersion)
            { change = AnalysisChange.Moved; explanation = "Observed path movement with compatible source identity and version evidence; still no content or current action authority."; }
            else if (!samePaths || a.VersionEvidence != b.VersionEvidence || a.ModifiedUtc != b.ModifiedUtc || a.MetadataFingerprint != b.MetadataFingerprint)
            { change = AnalysisChange.MetadataChanged; explanation = "Observed paths or metadata changed. Identity continuity is not established by matching size and timestamps."; }
            else { change = AnalysisChange.NoObservedMetadataChange; explanation = "No observed metadata change. File contents were not compared."; }
            rows.Add(new(a, b, change, evidence, delta, explanation));
        }
        for (int i = 0; i < old.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); if (used[i]) continue;
            rows.Add(incoming[i] > 0 ? new(old[i], null, AnalysisChange.Unknown, Evidence(old[i], null), null, "The earlier alias group has an ambiguous current relationship.") : Absent(old[i], null, compatibility.CurrentComplete));
        }
        // Absolute change ranks before percentages, so a tiny denominator cannot dominate the default growth order.
        return rows.OrderByDescending(r => r.Delta?.Magnitude ?? 0).ThenBy(r => r.Current?.Paths[0] ?? r.Previous!.Paths[0], StringComparer.Ordinal).ToImmutableArray();

        AnalysisComparisonEvidence Evidence(AnalysisComparisonEntity? a, AnalysisComparisonEntity? b) => structural ? AnalysisComparisonEvidence.Structural :
            compatibility.VerifiedIdentityContext && a?.ObservedIdentity is { } id && id == b?.ObservedIdentity && a.VersionEvidence is { } version && version == b.VersionEvidence &&
            a.Fresh && b.Fresh && a.MetadataFingerprint == b.MetadataFingerprint
                ? AnalysisComparisonEvidence.CompatibleIdentityAndVersion : AnalysisComparisonEvidence.PathBased;
        AnalysisComparisonRow Absent(AnalysisComparisonEntity? a, AnalysisComparisonEntity? b, bool complete)
        {
            var e = b ?? a!;
            bool known = complete && compatibility.ComparableValues && e.Mapped && e.Fresh && e.Value.IsKnown;
            return new(a, b, known ? b is null ? AnalysisChange.Removed : AnalysisChange.Added : b is null ? AnalysisChange.Unknown : AnalysisChange.PreviouslyUnknown,
                Evidence(a, b), known ? AnalysisDelta.Between(a?.Value.Value ?? 0, b?.Value.Value ?? 0) : null,
                known ? b is null ? "Absent from the current complete captured policy scope. This historical row has no live target." : "Absent from the earlier complete captured policy scope. New observation, not a creation-date assertion." :
                "The other inventory did not establish complete comparable coverage here. Missing is unknown, not zero.");
        }
    }

    private static InvalidOperationException Limit() => new("This comparison exceeds the available memory budget. Choose inventories captured from a smaller scope. No partial comparison or zero baseline was published.");
}
