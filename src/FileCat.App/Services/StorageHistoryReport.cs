using System.Text;
using System.Text.Json;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

internal static class StorageHistoryReport
{
    internal static string Format(StorageReviewStatus status)
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
}
