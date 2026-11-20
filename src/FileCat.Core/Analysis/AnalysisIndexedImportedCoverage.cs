using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Bounded supplied-coverage checks over streaming graph validation events.</summary>
internal sealed class AnalysisIndexedImportedCoverage
{
    private readonly AnalysisIndexedGraphHeader _header;
    private readonly AnalysisCoverageRecord _declared;
    private readonly HashSet<ulong> _nodes, _remaining;
    private readonly HashSet<string> _roots;
    private readonly List<ulong> _legacyNodes = [];
    private readonly List<string> _legacyRoots = [];
    private readonly HashSet<ulong> _legacySeen = [];
    private readonly HashSet<string> _legacyRootsSeen = new(StringComparer.Ordinal);
    private bool _knownStale, _unlocated;
    internal AnalysisIndexedImportedCoverage(AnalysisIndexedGraphHeader header, AnalysisCoverageRecord declared)
    {
        _header = header; _declared = declared;
        if (declared.StaleScopeNodes.IsDefault || declared.StaleRootIds.IsDefault || declared.BoundaryTotals is null || declared.MeasurementStates is null ||
            declared.StaleScopeNodes.Length > AnalysisLimits.Issues || declared.StaleRootIds.Length > header.Roots.Length ||
            declared.EnumerationCompleted > declared.EnumerationRequested || declared.EnumerationFailed > declared.EnumerationRequested - declared.EnumerationCompleted)
            throw Invalid();
        _nodes = declared.StaleScopeNodes.ToHashSet(); _remaining = _nodes.ToHashSet();
        _roots = declared.StaleRootIds.ToHashSet(StringComparer.Ordinal);
        if (_nodes.Count != declared.StaleScopeNodes.Length || _roots.Count != declared.StaleRootIds.Length ||
            _roots.Any(id => !header.Roots.Any(root => root.Id == id))) throw Invalid();
        if (declared.Qualification is { } q && (q.StaleScopesComplete && q.HasUnlocatedStaleScopes ||
            !declared.StaleScopeNodes.SequenceEqual(declared.StaleScopeNodes.Order()) ||
            !declared.StaleRootIds.SequenceEqual(declared.StaleRootIds.Order(StringComparer.Ordinal)))) throw Invalid();
    }
    internal void Node(ulong id, string root)
    {
        _knownStale = true;
        if (_declared.Qualification is not { } q) return;
        if (!_roots.Contains(root) || q.StaleScopesComplete && !_nodes.Contains(id)) throw Invalid();
        _remaining.Remove(id);
    }
    internal void Unlocated() { _knownStale = true; _unlocated = true; }
    internal void Issue(AnalysisIssue issue, string? resolvedRoot)
    {
        bool legacy = issue.State == MeasurementState.Changed || issue.Code is "source-changed-or-boundary" or "changed-during-observation";
        if (legacy)
        {
            if (issue.NodeId is { } node && _legacySeen.Add(node)) _legacyNodes.Add(node);
            if (issue.RootId is { } root && _legacyRootsSeen.Add(root)) _legacyRoots.Add(root);
        }
        if (!AnalysisCoverage.IsStaleIssue(issue.Code, issue.State)) return;
        _knownStale = true;
        if (issue.NodeId is { } id) Node(id, resolvedRoot ?? issue.RootId ?? throw Invalid());
        if (_declared.Qualification is not null && issue.RootId is { } declaredRoot && !_roots.Contains(declaredRoot)) throw Invalid();
        if (issue.NodeId is null && issue.RootId is null) Unlocated();
    }
    internal AnalysisDatasetCoverage Complete(AnalysisCoverageRecord computed, ulong directories, ulong eligible, bool complete, bool unlocated)
    {
        var d = _declared;
        if (d.EnumerationRequested > directories || d.Boundaries != computed.Boundaries || d.ExcludedIssues != computed.ExcludedIssues ||
            d.FailedIssues != computed.FailedIssues || d.PendingIssues != computed.PendingIssues || d.IdentityVerifiedPaths != computed.IdentityVerifiedPaths ||
            d.IdentityUnverifiedPaths != computed.IdentityUnverifiedPaths || !Equal(d.BoundaryTotals, computed.BoundaryTotals) || !Equal(d.MeasurementStates, computed.MeasurementStates)) throw Invalid();
        bool final = _header.Phase == AnalysisPhase.Finished && _header.Coverage == NamespaceCoverage.CompleteUnderPolicy;
        if (d.Qualification is { } q)
        {
            if (q.EligibleDirectories != eligible || d.EnumerationRequested > eligible ||
                !q.EnumerationKnown && (d.EnumerationRequested != 0 || d.EnumerationCompleted != 0 || d.EnumerationFailed != 0) ||
                q.EnumerationKnown && final && (d.EnumerationRequested != eligible || d.EnumerationCompleted != eligible || d.EnumerationFailed != 0) ||
                d.DenominatorFinal != (q.EnumerationKnown && final) || _remaining.Count != 0 ||
                _knownStale && _header.Freshness == AnalysisFreshness.Observed || _unlocated && !q.HasUnlocatedStaleScopes ||
                _header.Freshness != AnalysisFreshness.Observed && !_knownStale && d.StaleScopeNodes.IsEmpty && d.StaleRootIds.IsEmpty && !q.HasUnlocatedStaleScopes) throw Invalid();
            return new(d, q.EnumerationKnown, eligible, q.StaleScopesComplete, q.HasUnlocatedStaleScopes);
        }
        // Legacy zero counts did not encode whether enumeration was known. Preserve that
        // uncertainty explicitly. Nonzero counts must support the indexed coverage contract.
        if (d.DenominatorFinal != final || !d.StaleScopeNodes.SequenceEqual(_legacyNodes) || !d.StaleRootIds.SequenceEqual(_legacyRoots) ||
            d.EnumerationRequested > eligible || final && d.EnumerationRequested != 0 &&
                (d.EnumerationRequested != eligible || d.EnumerationCompleted != eligible || d.EnumerationFailed != 0)) throw Invalid();
        bool known = d.EnumerationRequested != 0 || eligible == 0;
        var normalized = computed with
        {
            EnumerationRequested = d.EnumerationRequested, EnumerationCompleted = d.EnumerationCompleted, EnumerationFailed = d.EnumerationFailed,
            DenominatorFinal = known && final, Qualification = new(known, eligible, complete, unlocated)
        };
        return new(normalized, known, eligible, complete, unlocated);
    }
    private static bool Equal<T>(ImmutableDictionary<T, ulong> a, ImmutableDictionary<T, ulong> b) where T : notnull =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    private static InvalidDataException Invalid() => new("Imported inventory coverage contradicts its graph.");
}
