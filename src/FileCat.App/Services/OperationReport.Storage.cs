using FileCat.Core.Jobs;

namespace FileCat.App.Services;

internal static partial class OperationReport
{
    private static void WriteStorage(Snapshot snapshot, Action<string> line)
    {
        line("FileCat storage operation report");
        line("Title: " + Quoted(snapshot.Job.Title));
        line("Captured (UTC): " + snapshot.CapturedUtc.ToString("O"));
        line("State: " + snapshot.State.Describe());
        line("Quoted paths and reasons use JSON escaping; decoding the quoted value recovers the exact text.");
        line(""); line("Summary");
        var result = snapshot.Storage;
        var request = snapshot.Job.Request.StorageTransform;
        if (request is not null)
        {
            line("Requested action: " + StorageOperationText.Action(request.Action));
            line("Scope: " + StorageOperationText.Scope(request.Scope));
            line("Reviewed revision: " + request.ReviewRevision.ToString("D"));
        }
        if (result is null) line("Storage observations are not yet available. Coverage and effects are not established.");
        else
        {
            line("Live observations at (UTC): " + (result.ObservedUtc ?? snapshot.CapturedUtc).ToString("O"));
            line(StorageOperationText.Details(result));
            line("Counter basis: " + (result.ObservationCounts ? "observations; repeated identities may be counted more than once" : "disjoint observed-object outcomes"));
            line("Ambiguous repeated identities: " + Count(result.AmbiguousDuplicates));
        }
        for (int i = 0; i < snapshot.StorageRootPaths.Length; i++)
        {
            line($"Selected root [{Count(i + 1)}]: " + Quoted(snapshot.StorageRootPaths[i]));
            var root = result?.Roots.FirstOrDefault(r => r.SourceIndex == i);
            var identity = root?.Identity ?? (request is not null && i < request.Roots.Length ? request.Roots[i].Observation.Identity : null);
            line("  Identity: " + (identity is null ? "unavailable" :
                $"volume {identity.Volume:X16}; binding {Quoted(identity.VolumeBinding ?? "unavailable")}; object {Quoted(identity.ObjectId)}"));
            line("  Status: " + (root is null ? "not yet established" : root.Completeness.ToString()) +
                (root?.Changed == true ? "; contains an observed change" : ""));
        }
        line("Selected roots may overlap; their statuses must not be added to global counts.");
        if (result is not null)
            foreach (var reason in result.Reasons)
                line($"Reason total: {Count(reason.Count)} · {reason.Problem.Kind} · {(reason.Problem.IsError ? "error" : "warning")} · " +
                    Quoted(reason.Problem.Code) + " · " + Quoted(reason.Problem.Message));

        line(""); line("Call details");
        if (result?.DurablePrefix is { } prefix)
        {
            line("Recorded through sequence: " + Count(prefix.Sequence));
            line("Durable byte endpoint: " + Count(prefix.ByteLength));
            line("Durable snapshot SHA-256: " + prefix.Sha256);
            line("Exact native intent/result records remain in the retained storage evidence. This summary does not contain those records.");
        }
        else line("No durable call-record snapshot is available in this summary.");
        if (!snapshot.State.IsFinished()) line("An in-flight intent is pending; it does not establish an uncertain completed effect.");

        line(""); line("Examples");
        line("Bounded no-call examples; these are not a complete per-item log. Reason totals above cover all recorded observations.");
        if (result is not null)
        {
            foreach (var example in result.Examples)
            {
                line(example.Effect + ": " + Quoted(example.Path));
                if (example.Problem is { } problem) line("  Reason: " + Quoted(problem.Code) + " · " + Quoted(problem.Message));
            }
            line("Additional examples omitted: " + Count(result.OmittedExamples));
        }
    }
}
