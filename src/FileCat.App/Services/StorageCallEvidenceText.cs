using System.Text;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

internal static class StorageCallEvidenceText
{
    internal static string Format(StorageCallEvidencePage page, bool activeAtCapture)
    {
        var text = new StringBuilder();
        void Line(string value = "") => text.AppendLine(value);
        static string Q(string? value) => JsonSerializer.Serialize(value);
        void Observation(string label, StorageObservation observation)
        {
            Line(label + $": compression {observation.Compression}; EFS {observation.Efs}; " +
                $"kind {(observation.IsDirectory ? "directory" : "file")}; volume {observation.Identity.Volume:X16}; " +
                $"binding {Q(observation.Identity.VolumeBinding)}; object {Q(observation.Identity.ObjectId)}");
            if (observation.Ineligible is { } problem) Problem(label + " eligibility", problem);
        }
        void Problem(string label, StorageProblem problem) =>
            Line(label + $": {problem.Kind}; {(problem.IsError ? "error" : "warning")}; {Q(problem.Code)}; {Q(problem.Message)}");
        Line("FileCat recorded storage calls");
        Line("Historical evidence only; paths and IDs do not authorize a new operation.");
        Line("Quoted strings use JSON escaping; decoding restores their exact value.");
        Line("Action: " + StorageOperationText.Action(page.Action)); Line("Scope: " + StorageOperationText.Scope(page.Scope));
        Line("Review revision: " + page.ReviewRevision.ToString("D"));
        EfsConsent(page.EfsConsent, Line);
        Line("Journal: " + page.Prefix.JobId.ToString("D"));
        Line("Recorded through sequence: " + page.Prefix.Sequence);
        Line("Durable byte endpoint: " + page.Prefix.ByteLength); Line("Durable snapshot SHA-256: " + page.Prefix.Sha256);
        Line("Pages stay at this exact prefix. Reopen the operation report to capture newer evidence.");
        Line("Call details");
        foreach (var call in page.Calls)
        {
            Line(); Line($"Call {call.Call}; selected root {call.SourceIndex + 1}"); Line("Path: " + Q(call.Path));
            Line($"Held identity: volume {call.Identity.Volume:X16}; binding {Q(call.Identity.VolumeBinding)}; object {Q(call.Identity.ObjectId)}");
            Observation("Before", call.Before); Line((page.Action is StorageAction.EncryptEfs or StorageAction.DecryptEfs ? "Desired EFS state: " : "Desired compression state: ") + call.Desired);
            if (call.EfsConsentId is { } consentId) Line("EFS scope consent: " + consentId.ToString("D"));
            if (call.Disposition is null)
                Line(activeAtCapture ? "Pending at this active snapshot: an intent is durable; no result is recorded. This does not establish a completed uncertain effect."
                    : "No result is recorded at this prefix. The effect is not established by this call record.");
            else
            {
                Line("Disposition: " + call.Disposition);
                if (call.NoCallReason is { } reason) Line("No-call reason: " + reason);
                if (call.Result is { } result)
                {
                    Line($"Native call attempted: {result.NativeCallAttempted}; observed effect: {result.Effect}");
                    Observation("Result before", result.Before);
                    if (result.After is { } after) Observation("Result after", after); else Line("Result after: unavailable");
                    if (result.Problem is { } problem) Problem("Result reason", problem);
                }
                else Line("No mutation result accompanies this no-call record.");
            }
        }
        Line();
        Line(page.Calls.Length == 0 ? "No further calls occur in this captured prefix."
            : $"Displayed calls {page.Calls[0].Call}–{page.Calls[^1].Call}. " +
              (page.More ? "More exact records remain; choose Next recorded call page." : "End of the captured call records."));
        return text.ToString();
    }

    internal static void EfsConsent(StorageEfsConsent? consent, Action<string> line)
    {
        if (consent is null) return;
        line("Recorded EFS scope consent: " + consent.ConsentId.ToString("D"));
        line("Concurrent in-place link-change limitation accepted: " + consent.Acknowledgments.AcceptConcurrentLinkChange);
        line(consent.Action == StorageAction.EncryptEfs
            ? "Removal of compression from all main/named streams, including streams added before completion, accepted: " + consent.Acknowledgments.AcceptAllStreamDecompression
            : "All-stream decompression consent: not applicable to EFS decryption.");
        line("These acknowledgments do not establish key recovery, output approval or prevention of concurrent link redirection.");
    }
}
