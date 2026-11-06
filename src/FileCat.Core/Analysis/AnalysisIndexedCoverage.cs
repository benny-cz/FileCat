using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Bounded full-graph counters collected during validation, independent of the UI preview.</summary>
internal sealed class AnalysisIndexedCoverage(AnalysisIndexedGraphHeader header)
{
    private readonly Dictionary<MeasurementState, ulong> _measurements = [];
    private readonly HashSet<ulong> _nodes = [];
    private readonly HashSet<string> _roots = (header.StaleEvidence?.RootIds ?? []).ToHashSet(StringComparer.Ordinal);
    private bool _complete = true, _unlocated = header.StaleEvidence?.Unlocated ?? false;
    private ulong _sampledStale;
    private readonly AnalysisIndexedImportedCoverage? _imported = header.ImportedCoverage is { } supplied ? new(header, supplied) : null;
    internal ulong Directories;
    internal ulong EligibleDirectories, VerifiedPaths, UnverifiedPaths;
    internal bool Changed = header.StaleEvidence is { Events: > 0 };

    internal void Path(AnalysisNode node)
    {
        if (node.Kind == AnalysisKind.Directory) Directories++;
        if (node.Kind == AnalysisKind.Directory && node.IncludedByPolicy && node.MetadataState == MeasurementState.Known && node.Cloud != true) EligibleDirectories++;
        if (node.MetadataState == MeasurementState.Changed) Stale(node.Id, node.RootId);
    }
    internal void Object(AnalysisObject value, bool hasAliases)
    {
        if (value.LogicalLength.State == MeasurementState.Changed || value.LinkCount.State == MeasurementState.Changed)
        { Changed = true; if (!hasAliases) { _unlocated = true; _imported?.Unlocated(); } }
    }
    internal void Alias(AnalysisNode node, AnalysisObject value, bool changedFacts)
    {
        if (node.Kind == AnalysisKind.File && node.IncludedByPolicy)
        { if (value.VerifiedIdentity is null) UnverifiedPaths++; else VerifiedPaths++; }
        if (changedFacts || value.LogicalLength.State == MeasurementState.Changed || value.LinkCount.State == MeasurementState.Changed) Stale(node.Id, node.RootId);
    }
    internal void Fact(StorageFact fact, AnalysisNode? directory, bool hasAliases)
    {
        var state = fact.Measurement.State; _measurements[state] = checked(_measurements.GetValueOrDefault(state) + 1);
        if (state == MeasurementState.Changed)
        {
            Changed = true;
            if (directory is not null) Stale(directory.Id, directory.RootId);
            else if (!hasAliases) { _unlocated = true; _imported?.Unlocated(); }
        }
    }
    internal void Issue(AnalysisIssue issue, string? resolvedRoot = null)
    {
        _imported?.Issue(issue, resolvedRoot);
        if (!AnalysisCoverage.IsStaleIssue(issue.Code, issue.State)) return;
        _sampledStale++;
        if (issue.NodeId is { } node) AddNode(node);
        if ((issue.RootId ?? resolvedRoot) is { } root) _roots.Add(root);
        if (issue.NodeId is null && issue.RootId is null) _unlocated = true;
        Changed = true;
    }
    internal void Stale(ulong node, string root) { Changed = true; _imported?.Node(node, root); AddNode(node); _roots.Add(root); }
    private void AddNode(ulong node)
    {
        if (_nodes.Contains(node)) return;
        if (_nodes.Count < AnalysisLimits.Issues) _nodes.Add(node); else _complete = false;
    }
    internal AnalysisDatasetCoverage Complete(ImmutableDictionary<MeasurementState, ulong> issues, ImmutableDictionary<AnalysisBoundaryKind, ulong> boundaries,
        ulong sampledIssues, AnalysisFreshness freshness)
    {
        var enumeration = header.Enumeration; enumeration?.Validate();
        if (enumeration is not null && (enumeration.Requested > EligibleDirectories || header.Coverage == NamespaceCoverage.CompleteUnderPolicy && header.Phase == AnalysisPhase.Finished &&
            (enumeration.Requested != EligibleDirectories || enumeration.Completed != enumeration.Requested || enumeration.Failed != 0)))
            throw new InvalidDataException("Enumeration coverage contradicts the captured directory graph.");
        ulong totalIssues = issues.Values.Aggregate(0UL, (sum, value) => checked(sum + value));
        if (header.StaleEvidence is { } stale)
        {
            if (stale.Events < _sampledStale) throw new InvalidDataException("Stale-event coverage understates its samples.");
            if (stale.Events > _sampledStale) _complete = false;
        }
        else if (totalIssues > sampledIssues) { _complete = false; _unlocated = true; }
        if (freshness != AnalysisFreshness.Observed && !Changed && _roots.Count == 0 && _nodes.Count == 0) _unlocated = true;
        var record = new AnalysisCoverageRecord(enumeration?.Requested ?? 0, enumeration?.Completed ?? 0, enumeration?.Failed ?? 0,
            enumeration is not null && header.Phase == AnalysisPhase.Finished && header.Coverage == NamespaceCoverage.CompleteUnderPolicy,
            boundaries.Values.Aggregate(0UL, (sum, value) => checked(sum + value)), issues.GetValueOrDefault(MeasurementState.Excluded),
            checked(issues.GetValueOrDefault(MeasurementState.Failed) + issues.GetValueOrDefault(MeasurementState.Changed)), issues.GetValueOrDefault(MeasurementState.Pending),
            _measurements.ToImmutableDictionary(), VerifiedPaths, UnverifiedPaths, _nodes.Order().ToImmutableArray())
            { BoundaryTotals = boundaries, StaleRootIds = _roots.Order(StringComparer.Ordinal).ToImmutableArray() };
        return _imported is null ? new(record, enumeration is not null, EligibleDirectories, _complete && !_unlocated, _unlocated)
            : _imported.Complete(record, Directories, EligibleDirectories, _complete && !_unlocated, _unlocated);
    }
}
