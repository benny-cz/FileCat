using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisEnumerationProgress(ulong Requested, ulong Completed, ulong Failed)
{
    public ulong Pending => checked(Requested - Completed - Failed);
    internal void Validate()
    {
        if (Requested > AnalysisLimits.Observations || Completed > Requested || Failed > Requested - Completed)
            throw new InvalidDataException("Invalid enumeration coverage.");
    }
}

/// <summary>Full-revision coverage. Unknown enumeration and incomplete stale samples remain explicit.</summary>
public sealed record AnalysisDatasetCoverage(AnalysisCoverageRecord Observations, bool EnumerationKnown,
    ulong EligibleDirectories, bool StaleScopesComplete, bool HasUnlocatedStaleScopes)
{
    public ulong? UnscheduledDirectories => EnumerationKnown ? checked(EligibleDirectories - Observations.EnumerationRequested) : null;
}

internal sealed record AnalysisStaleEvidence(ulong Events, ImmutableArray<string> RootIds, bool Unlocated);

public static class AnalysisCoverage
{
    internal static bool IsStaleIssue(string code, MeasurementState state) => state == MeasurementState.Changed || code is "source-changed-or-boundary" or "changed-during-observation" or "device-not-responding";
    public static AnalysisBoundaryKind? BoundaryKind(string code) => code switch
    {
        "source-mount-boundary" or "source-volume-boundary" => AnalysisBoundaryKind.Mount,
        "navigation-link-not-followed" => AnalysisBoundaryKind.NavigationLink,
        "cloud-directory-not-enumerated" => AnalysisBoundaryKind.CloudDirectory,
        "special-entry-not-traversed" => AnalysisBoundaryKind.SpecialEntry,
        "root-overlap-unverified" => AnalysisBoundaryKind.UnverifiedRootOverlap,
        _ => null
    };
    public static ImmutableDictionary<AnalysisBoundaryKind, ulong> SampledBoundaries(AnalysisSnapshot snapshot) => snapshot.Issues
        .Select(i => BoundaryKind(i.Code)).Where(k => k.HasValue).GroupBy(k => k!.Value).ToImmutableDictionary(g => g.Key, g => (ulong)g.Count());
    public static AnalysisCoverageRecord Create(AnalysisSnapshot snapshot, ulong requested = 0, ulong completed = 0, ulong failed = 0)
    {
        if (completed > requested || failed > requested - completed) throw new InvalidDataException("Invalid enumeration coverage.");
        var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        ulong verified = 0, unverified = 0;
        foreach (var node in snapshot.Nodes.Where(n => n.Kind == AnalysisKind.File && n.IncludedByPolicy))
        {
            if (node.ObjectId is { } id && objects.GetValueOrDefault(id)?.VerifiedIdentity is not null) verified++;
            else unverified++;
        }
        var totals = snapshot.IssueTotals.Count > 0 ? snapshot.IssueTotals : snapshot.Issues.GroupBy(i => i.State).ToImmutableDictionary(g => g.Key, g => (ulong)g.Count());
        var boundaries = snapshot.BoundaryTotals.Count > 0 ? snapshot.BoundaryTotals : SampledBoundaries(snapshot);
        bool Stale(AnalysisIssue issue) => issue.State == MeasurementState.Changed || issue.Code is "source-changed-or-boundary" or "changed-during-observation";
        return new(requested, completed, failed, snapshot.Phase == AnalysisPhase.Finished && snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy,
            boundaries.Values.Aggregate(0ul, (sum, value) => checked(sum + value)), totals.GetValueOrDefault(MeasurementState.Excluded),
            checked(totals.GetValueOrDefault(MeasurementState.Failed) + totals.GetValueOrDefault(MeasurementState.Changed)), totals.GetValueOrDefault(MeasurementState.Pending),
            snapshot.Facts.GroupBy(f => f.Measurement.State).ToImmutableDictionary(g => g.Key, g => (ulong)g.Count()), verified, unverified,
            snapshot.Issues.Where(i => i.NodeId.HasValue && Stale(i)).Select(i => i.NodeId!.Value).Distinct().ToImmutableArray())
            { BoundaryTotals = boundaries, StaleRootIds = snapshot.Issues.Where(i => i.RootId is not null && Stale(i)).Select(i => i.RootId!).Distinct(StringComparer.Ordinal).ToImmutableArray() };
    }
}
