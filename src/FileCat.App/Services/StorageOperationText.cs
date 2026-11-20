using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

/// <summary>Pure projection of complete typed storage facts; sampled JobIssue rows never determine the outcome.</summary>
internal static class StorageOperationText
{
    internal static string Action(StorageAction action) => action switch
    {
        StorageAction.Compress => "Compress",
        StorageAction.RemoveCompression => "Remove compression",
        StorageAction.EncryptEfs => "Encrypt with EFS",
        StorageAction.DecryptEfs => "Decrypt EFS",
        _ => "Unknown storage action",
    };

    internal static string Scope(StorageScope scope) => scope switch
    {
        StorageScope.SelectedItems => "Selected items only",
        StorageScope.ExistingDescendants => "Selected items and existing descendants",
        _ => "Unknown scope",
    };

    internal static string Counts(StorageResultSnapshot snapshot)
    {
        var c = snapshot.Counts;
        return $"{c.Changed:N0} changed, {c.AlreadySatisfied:N0} already satisfied, {c.Skipped:N0} skipped, " +
            $"{c.Failed:N0} failed, {c.Uncertain:N0} uncertain, {c.NotProcessed:N0} not processed";
    }

    internal static string Completeness(StorageCompleteness completeness) => completeness switch
    {
        StorageCompleteness.Complete => "The requested scope was fully checked.",
        StorageCompleteness.Incomplete => "The requested scope was not fully completed.",
        _ => "Additional items may not have been discovered; counts cover observed items only.",
    };

    internal static string Details(StorageResultSnapshot snapshot)
    {
        var parts = new List<string> { Counts(snapshot) + ".", Completeness(snapshot.Discovery) };
        if (snapshot.PersistenceFault) parts.Add("Result not confirmed: storage evidence could not be saved or finalized reliably.");
        else if (snapshot.Counts.Uncertain > 0) parts.Add("Result not confirmed: the effect of a native call remains uncertain.");
        else if (snapshot.ReviewPending) parts.Add("Result not confirmed: storage evidence or ownership needs review.");
        if (snapshot.Canceled) parts.Add("Stopped; completed changes remain.");
        if (snapshot.ExecutionRefused) parts.Add("Execution was refused; review the recorded reason.");
        if (snapshot.ObservationCounts) parts.Add("Counts describe observations; some repeated identities could not be reconciled.");
        return string.Join(" ", parts);
    }

    internal static string Severity(StorageResultSnapshot? result, JobState state)
    {
        if (state == JobState.Failed || result is { PersistenceFault: true } or { ReviewPending: true } ||
            result is not null && (result.Counts.Failed > 0 || result.Counts.Uncertain > 0 || result.Reasons.Any(r => r.Count > 0 && r.Problem.IsError))) return "error";
        if (result is not null && (result.HasIssues || result.ExecutionRefused || state.IsFinished() && result.Discovery != StorageCompleteness.Complete)) return "warning";
        return state == JobState.Completed ? "ok" : "info";
    }
}
