using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

internal sealed record StorageObjectCallResult(StorageMutationResult Result, bool Canceled = false,
    bool PersistenceFault = false, bool LimitReached = false, StorageProblem? PersistenceProblem = null)
{
    internal bool MustStop => Canceled || PersistenceFault || LimitReached || Result.Effect == StorageEffect.Uncertain;
}

/// <summary>One held object's preparation, durable intent, atomic admission and durable postcondition.
/// The caller retains the traversal/object and all native capacity until this method actually returns.</summary>
internal static class StorageObjectCallRunner
{
    internal static StorageObjectCallResult Run(Job job, StorageTransformJournalWriter writer, int sourceIndex, IStorageObjectLease held)
    {
        var request = job.Request.StorageTransform ?? throw new InvalidOperationException("Missing storage request.");
        if (request.Action is not (StorageAction.Compress or StorageAction.RemoveCompression) ||
            writer.Header.JobId != job.Id || writer.Header.Action != request.Action || writer.Header.Scope != request.Scope ||
            writer.Header.ReviewRevision != request.ReviewRevision || sourceIndex < 0 || sourceIndex >= writer.Header.Roots.Length)
            throw new InvalidOperationException("The held call does not belong to this storage approval.");
        StorageObjectIdentity identity = held.Identity;
        while (true)
        {
            job.Checkpoint();
            var before = held.Observe(job.Token);
            if (before.Identity != identity) return Refused(before, "held-identity-changed", "The held object identity changed; this observation is not mutated.");
            if (NoCall(before, request.Action) is { } noCall) return new(noCall);
            try { before = StorageExecutionRetry.Run(job, held.Path, () => held.PrepareMutation(request.Action, job.Token)); }
            catch (StorageInspectionException error)
            { return new(new(before, null, error.Problem.Code == "sharing-skipped" ? StorageEffect.Skipped : StorageEffect.Failed, error.Problem)); }
            catch (IOException error)
            { return new(new(before, null, StorageEffect.Failed, Problem("preparation-failed", "The held mutation handle could not be prepared: " + error.Message))); }
            if (before.Identity != identity) return Refused(before, "prepared-identity-changed", "Preparation returned a different physical object; no call was attempted.");
            if (NoCall(before, request.Action) is { } preparedNoCall) return new(preparedNoCall);

            StorageJournalCall call;
            try { call = writer.FlushIntent(sourceIndex, held.Path, before); }
            catch (StorageJournalLimitException)
            {
                return new(new(before, before, StorageEffect.NotProcessed, Problem("record-limit", "The storage recovery-record limit was reached; select smaller scopes.")), LimitReached: true);
            }
            catch (IOException error)
            { return Persistence(new(before, before, StorageEffect.NotProcessed, null), error); }

            var admitted = job.TryAdmitStorageCall(call);
            if (admitted != StorageCallAdmission.Admitted)
            {
                try { writer.FlushNotAttempted(call, admitted == StorageCallAdmission.Canceled ? StorageNoCallReason.Canceled :
                    admitted == StorageCallAdmission.Paused ? StorageNoCallReason.Paused : StorageNoCallReason.StateChanged); }
                catch (IOException error)
                { return Persistence(new(before, before, StorageEffect.NotProcessed, null), error); }
                if (admitted == StorageCallAdmission.Paused) continue; // checkpoint waits; both observations and intent are fresh after Resume
                return new(new(before, before, StorageEffect.NotProcessed,
                    Problem(admitted == StorageCallAdmission.Canceled ? "stopped-before-call" : "call-not-admitted",
                        admitted == StorageCallAdmission.Canceled ? "Stopped before the native call; this item was not changed." : "This native call was not admitted.")),
                    Canceled: admitted == StorageCallAdmission.Canceled);
            }

            StorageMutationResult result;
            // The token no longer controls this admitted call. Stop cannot bypass the held postcondition or its flush.
            try { result = held.ApplyAdmitted(request.Action); }
            catch (Exception error)
            { result = new(before, null, StorageEffect.Uncertain, Problem("admitted-call-threw", "An admitted call did not return a trustworthy postcondition: " + error.Message)) { NativeCallAttempted = true }; }
            if (result is null || result.Before is null || result.Before.Identity != identity || result.After is { } after && after.Identity != identity)
                result = new(before, null, StorageEffect.Uncertain, Problem("admitted-identity-invalid", "The admitted call returned an invalid object identity; its effect remains uncertain.")) { NativeCallAttempted = true };
            try { writer.FlushResult(call, result); }
            catch (IOException error) { return Persistence(result, error); }
            return new(result, Canceled: job.IsCancellationRequested);
        }
    }

    private static StorageObjectCallResult Refused(StorageObservation before, string code, string message) =>
        new(new(before, null, StorageEffect.NotProcessed, new(StorageReasonKind.IdentityChanged, code, message, true)));
    private static StorageObjectCallResult Persistence(StorageMutationResult result, IOException error) =>
        new(result, PersistenceFault: true, PersistenceProblem: Problem("storage-evidence-failed", "The storage result could not be saved durably: " + error.Message));
    private static StorageProblem Problem(string code, string message) =>
        new(StorageReasonKind.TransientFailure, code, message.Length <= 2000 ? message : message[..2000], true);
    private static StorageMutationResult? NoCall(StorageObservation before, StorageAction action)
    {
        if (before.Ineligible is { } problem) return new(before, before, StorageEffect.Skipped, problem);
        if (before.Compression is not (StorageObservationState.Enabled or StorageObservationState.Disabled))
            return new(before, before, StorageEffect.NotProcessed, Problem("compression-unknown", "The held compression state could not be established."));
        var desired = action == StorageAction.Compress ? StorageObservationState.Enabled : StorageObservationState.Disabled;
        return before.Compression == desired ? new(before, before, StorageEffect.AlreadySatisfied, null) : null;
    }
}
