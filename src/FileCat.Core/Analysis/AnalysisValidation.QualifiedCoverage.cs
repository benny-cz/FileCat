namespace FileCat.Core.Analysis;

public static partial class AnalysisValidation
{
    private static void ValidateQualifiedCoverage(AnalysisSnapshot s, AnalysisCoverageRecord coverage, AnalysisCoverageQualification q,
        Dictionary<ulong, AnalysisNode> nodes, Dictionary<string, AnalysisObject> objects, Dictionary<string, List<ulong>> aliases,
        Dictionary<string, List<StorageFact>> facts, CancellationToken cancellation)
    {
        ulong eligible = (ulong)s.Nodes.Count(n => n.Kind == AnalysisKind.Directory && n.IncludedByPolicy && n.MetadataState == MeasurementState.Known && n.Cloud != true);
        if (q.EligibleDirectories != eligible || coverage.EnumerationRequested > eligible ||
            !q.EnumerationKnown && (coverage.EnumerationRequested != 0 || coverage.EnumerationCompleted != 0 || coverage.EnumerationFailed != 0) ||
            q.EnumerationKnown && s.Phase == AnalysisPhase.Finished && s.Coverage == NamespaceCoverage.CompleteUnderPolicy &&
                (coverage.EnumerationRequested != eligible || coverage.EnumerationCompleted != eligible || coverage.EnumerationFailed != 0) ||
            q.StaleScopesComplete && q.HasUnlocatedStaleScopes || coverage.StaleScopeNodes.Length > AnalysisLimits.Issues ||
            !coverage.StaleScopeNodes.SequenceEqual(coverage.StaleScopeNodes.Distinct().Order()) ||
            !coverage.StaleRootIds.SequenceEqual(coverage.StaleRootIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) ||
            coverage.StaleRootIds.Any(id => !s.Roots.Any(r => r.Id == id))) throw Invalid();
        var declared = coverage.StaleScopeNodes.ToHashSet(); var remaining = declared.ToHashSet();
        bool knownStale = false, unlocated = false;
        void Root(string id) { if (!coverage.StaleRootIds.Contains(id)) throw Invalid(); }
        void Node(ulong id)
        {
            if (!nodes.TryGetValue(id, out var node)) throw Invalid();
            knownStale = true; Root(node.RootId); remaining.Remove(id);
            if (q.StaleScopesComplete && !declared.Contains(id)) throw Invalid();
        }
        foreach (var node in s.Nodes)
        { cancellation.ThrowIfCancellationRequested(); if (node.MetadataState == MeasurementState.Changed) Node(node.Id); }
        foreach (var obj in objects.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            bool changed = obj.LogicalLength.State == MeasurementState.Changed || obj.LinkCount.State == MeasurementState.Changed ||
                facts.TryGetValue("o:" + obj.Id, out var components) && components.Any(f => f.Measurement.State == MeasurementState.Changed);
            if (!changed) continue;
            knownStale = true;
            if (aliases.TryGetValue(obj.Id, out var members)) foreach (ulong id in members) Node(id); else unlocated = true;
        }
        foreach (var fact in s.Facts)
        { cancellation.ThrowIfCancellationRequested(); if (fact.DirectoryId is { } id && fact.Measurement.State == MeasurementState.Changed) Node(id); }
        foreach (var issue in s.Issues)
        {
            if (!AnalysisCoverage.IsStaleIssue(issue.Code, issue.State)) continue;
            knownStale = true;
            if (issue.NodeId is { } id) Node(id);
            if (issue.RootId is { } root) Root(root);
            if (issue.NodeId is null && issue.RootId is null) unlocated = true;
        }
        if (remaining.Count != 0 || knownStale && s.Freshness == AnalysisFreshness.Observed ||
            unlocated && !q.HasUnlocatedStaleScopes || s.Freshness != AnalysisFreshness.Observed && !knownStale &&
                coverage.StaleScopeNodes.IsEmpty && coverage.StaleRootIds.IsEmpty && !q.HasUnlocatedStaleScopes) throw Invalid();
    }
}
