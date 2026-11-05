using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public static class AnalysisCoverage
{
    public static AnalysisCoverageRecord Create(AnalysisSnapshot snapshot, ulong requested = 0, ulong completed = 0, ulong failed = 0)
    {
        if (completed > requested || failed > requested - completed) throw new InvalidDataException("Invalid enumeration coverage.");
        var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        ulong verified = 0, unverified = 0;
        foreach (var node in snapshot.Nodes.Where(n => n.Kind == AnalysisKind.File))
        {
            if (node.ObjectId is { } id && objects.GetValueOrDefault(id)?.VerifiedIdentity is not null) verified++;
            else unverified++;
        }
        var totals = snapshot.IssueTotals.Count > 0 ? snapshot.IssueTotals : snapshot.Issues.GroupBy(i => i.State).ToImmutableDictionary(g => g.Key, g => (ulong)g.Count());
        bool Stale(AnalysisIssue issue) => issue.State == MeasurementState.Changed || issue.Code is "source-changed-or-boundary" or "changed-during-observation";
        return new(requested, completed, failed, snapshot.Phase == AnalysisPhase.Finished && snapshot.Coverage == NamespaceCoverage.CompleteUnderPolicy,
            totals.GetValueOrDefault(MeasurementState.Excluded), totals.GetValueOrDefault(MeasurementState.Excluded),
            checked(totals.GetValueOrDefault(MeasurementState.Failed) + totals.GetValueOrDefault(MeasurementState.Changed)), totals.GetValueOrDefault(MeasurementState.Pending),
            snapshot.Facts.GroupBy(f => f.Measurement.State).ToImmutableDictionary(g => g.Key, g => (ulong)g.Count()), verified, unverified,
            snapshot.Issues.Where(i => i.NodeId.HasValue && Stale(i)).Select(i => i.NodeId!.Value).Distinct().ToImmutableArray())
            { StaleRootIds = snapshot.Issues.Where(i => i.RootId is not null && Stale(i)).Select(i => i.RootId!).Distinct(StringComparer.Ordinal).ToImmutableArray() };
    }
}
