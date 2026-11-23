using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

internal static class StorageHistoryReport
{
    internal static string Format(StorageReviewStatus status, IReadOnlyList<StorageCurrentObservation>? current = null, DateTime? observedUtc = null)
    {
        var reviewed = status.Current; var h = reviewed.History;
        var text = new StringBuilder();
        void Line(string value = "") => text.AppendLine(value);
        static string Q(string value) => JsonSerializer.Serialize(value);
        Line("FileCat storage history review");
        Line("Summary");
        Line(status.ReviewPending ? "Result not confirmed; explicit review is pending."
            : h.EffectUncertain || h.Damaged || h.LimitReached ? "Reviewed; effect remains uncertain." : "This history no longer requires acknowledgment.");
        Line("History: " + Q(reviewed.JournalLeaf));
        Line("Exact evidence identity: " + Q(reviewed.Evidence.Identity));
        Line($"Exact evidence: {reviewed.Evidence.ByteLength:N0} bytes; SHA-256 {reviewed.Evidence.Sha256}");
        Line("Requested action: " + (h.Action is { } action ? StorageOperationText.Action(action) : "unavailable"));
        Line("Scope: " + (h.Scope is { } scope ? StorageOperationText.Scope(scope) : "unavailable"));
        StorageCallEvidenceText.EfsConsent(h.EfsConsent, value => Line(value));
        Line("Recorded through sequence: " + h.Prefix.Sequence);
        if (h.Damaged) Line("Evidence is damaged or has an unknown schema. Missing facts remain unavailable; acknowledgment does not validate them.");
        if (h.LimitReached) Line("Evidence inspection reached a limit. Additional facts remain unavailable.");
        if (h.Result is { } result)
        {
            Line(StorageOperationText.Details(result));
            if (!h.Ended) Line("Counters are a durable lower bound. Later no-call observations and undiscovered scope may be absent.");
            foreach (var reason in result.Reasons) Line($"Reason total: {reason.Count:N0} · {Q(reason.Problem.Code)} · {Q(reason.Problem.Message)}");
        }
        else Line("Summary counters are unavailable. Scope completeness and missing outcomes are unknown.");
        foreach (var root in h.Roots)
        {
            Line($"Selected root [{root.SourceIndex + 1}]: " + Q(root.Path));
            Line($"Identity: volume {root.Observation.Identity.Volume:X16}; binding {Q(root.Observation.Identity.VolumeBinding ?? "unavailable")}; object {Q(root.Observation.Identity.ObjectId)}");
            var outcome = h.Result?.Roots.FirstOrDefault(r => r.SourceIndex == root.SourceIndex);
            Line("Status: " + (outcome is null ? "unavailable; completion is not established" : outcome.Completeness.ToString()));
        }
        if (h.Roots.Length == 0) Line("Selected-root identities are unavailable; no sample substitutes for the missing scope.");
        Line("Historical paths are evidence. They do not authorize mutation, cleanup or replay, and do not establish present identity continuity.");
        if (current is not null) CurrentObservations(h, current, observedUtc, Line);
        Line(); Line("Call details");
        if (h.LastCall is { } call)
        {
            Line("Last retained call fact (other calls remain in the exact evidence):");
            Line(JsonSerializer.Serialize(call));
            if (call.Disposition is null) Line("This intent has no matching durable result. Its historical effect is uncertain.");
        }
        else Line("No native-call detail was readable; this does not establish that no call occurred.");
        Line("Save call details exports the exact retained evidence as UTF-8 JSON records, including every retained intent and result. This summary is not a complete call log.");
        Line(); Line("Examples");
        Line("Bounded no-call examples; not a complete per-item log.");
        if (h.Result is { } examples)
        {
            foreach (var example in examples.Examples) Line(JsonSerializer.Serialize(example));
            Line($"Additional examples omitted: {examples.OmittedExamples:N0}");
        }
        foreach (var issue in status.Issues) Line("Review issue: " + Q(issue));
        Line(); Line("The reviewed summary will remain. Detailed call records may later be removed under the retention policy.");
        return text.ToString();
    }

    /// <summary>The objects as they are now, kept apart from the recorded intent, results and uncertainty.</summary>
    private static void CurrentObservations(StorageHistorySnapshot h, IReadOnlyList<StorageCurrentObservation> current, DateTime? observedUtc,
        Action<string> line)
    {
        static string Q(string value) => JsonSerializer.Serialize(value);
        line(""); line("Current observation");
        line("Read now without changing anything" + (observedUtc is { } at ? " (UTC " + at.ToString("O") + ")" : "") +
            ". It describes the items as they are now. It is not evidence of what a recorded call did, and a matching identity " +
            "after the original pins closed does not establish that it is the same object.");
        foreach (var observed in current)
        {
            var recorded = h.Roots.FirstOrDefault(r => r.Path == observed.Path)?.Observation ?? (h.LastCall?.Path == observed.Path ? h.LastCall.Before : null);
            string role = h.Roots.FirstOrDefault(r => r.Path == observed.Path) is { } root ? $"Selected root [{root.SourceIndex + 1}]" : "Last call object";
            if (observed.Observation is not { } now)
            {
                line($"{role}: {Q(observed.Path)}: unavailable now - {(observed.Problem is { } p ? Q(p.Code) + " · " + Q(p.Message) : "no observation")}");
                continue;
            }
            string compression = now.Compression switch
            {
                StorageObservationState.Enabled => now.IsDirectory ? "folder default compresses new items" : "main data compressed",
                StorageObservationState.Disabled => now.IsDirectory ? "folder default does not compress" : "main data not compressed",
                StorageObservationState.Unsupported => "compression unsupported here",
                _ => "compression state unknown",
            };
            string efs = now.Efs == StorageObservationState.Enabled ? "EFS encrypted" : now.Efs == StorageObservationState.Disabled ? "not EFS encrypted" : "EFS state unknown";
            string identity = recorded is null ? "no recorded identity to compare"
                : recorded.Identity == now.Identity ? "same volume and object ID as recorded (not proof of continuity)"
                : "a different object or volume from the recorded one";
            line($"{role}: {Q(observed.Path)}: {compression}; {efs}; {identity}" +
                (observed.Problem is { } now2 ? "; current exclusion " + Q(now2.Code) : ""));
        }
    }
}
