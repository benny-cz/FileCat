namespace FileCat.Core.Analysis;

public enum OwnedAnalysisRecoveryAction { RestorePrevious, FinishIncoming, CopyPrevious }
public enum OwnedAnalysisRecoveryState { Kept, Restored, FinishedIncoming, IncomingSavedCleanupPending, CopiedPrevious, RecoveryRequired, Uncertain }
public sealed record OwnedAnalysisRecoveryOutcome(OwnedAnalysisRecoveryState State, Guid PreviousId, Guid IncomingId, string Name,
    Guid? PendingOperationId, AnalysisInventoryReceipt? Receipt, string Detail);
public sealed class OwnedAnalysisRecoveryException(OwnedAnalysisRecoveryOutcome outcome, Exception inner) : IOException(outcome.Detail, inner)
{
    public OwnedAnalysisRecoveryOutcome Outcome { get; } = outcome;
}

public sealed partial class OwnedAnalysisStore
{
    public Task<OwnedAnalysisRecoveryOutcome> RecoverReplacementAsync(OwnedAnalysisReplacementRecoveryReview review, OwnedAnalysisRecoveryAction action,
        string? recoveryName = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (action == OwnedAnalysisRecoveryAction.RestorePrevious && !review.CanRestorePrevious ||
            action == OwnedAnalysisRecoveryAction.FinishIncoming && !review.CanFinishIncoming ||
            action == OwnedAnalysisRecoveryAction.CopyPrevious && review.CopySource is null)
            throw new InvalidOperationException(review.BlockedReason ?? "The requested recovery needs a complete verified version. Leave the remaining files pending.");
        if (action == OwnedAnalysisRecoveryAction.CopyPrevious) AnalysisPrivateInventoryStore.ValidateName(recoveryName!);
        else if (recoveryName is not null) throw new ArgumentException("A new filename is used only when copying a previous inventory.", nameof(recoveryName));
        review.Consume(this);
        return _owned.UseAsync(data => ExecuteRecoveryAsync(data, review, action, recoveryName, cancellation));
    }

    private async Task<OwnedAnalysisRecoveryOutcome> ExecuteRecoveryAsync(Contents data, OwnedAnalysisReplacementRecoveryReview review,
        OwnedAnalysisRecoveryAction action, string? recoveryName, CancellationToken cancellation)
    {
        var pending = review.Pending; var work = new RecoveryWork(); Exception? failure = null;
        string marker = RecoveryMarker(review.CatalogId, pending);
        var proofs = new[] { review.OriginalProof, review.IncomingProof, review.PreviousProof };
        try
        {
            await AnalysisIo.Run(_io, _device, token =>
            {
                work.Writer = data.Store!.OpenCatalogLock(token);
                if (SelectRecovery(data, pending.OperationId, token) != (review.CatalogId, pending)) throw new InvalidDataException("The pending replacement changed. Obtain a fresh review.");
                if (action == OwnedAnalysisRecoveryAction.FinishIncoming)
                {
                    var selected = OwnedAnalysisReplacementRecoveryReview.IsIncoming(review.OriginalProof, pending) ? review.OriginalProof : review.IncomingProof;
                    var value = selected.Inspection!;
                    new AnalysisSavedCatalog(data.Store).ValidateRecoveryBudget(review.CatalogId, pending, value.Bytes, value.Coverage, value.Taint, token);
                }
                using var directory = data.Store.BorrowDirectory();
                if (action == OwnedAnalysisRecoveryAction.CopyPrevious) RequireAbsent(directory, recoveryName!, token);
                work.Marker = directory.OpenMaintenance(marker);
                if (MetadataIdentity(work.Marker) != review.Marker) throw new InvalidDataException("The pending metadata object or bytes changed.");
                if (review.CurrentMetadata is { } expected)
                {
                    if (action == OwnedAnalysisRecoveryAction.CopyPrevious)
                    {
                        if (ReadMetadataIdentity(directory, AnalysisPrivateNames.CatalogEntry(review.CatalogId, pending.IncomingId)) != expected)
                            throw new InvalidDataException("The current catalog record changed.");
                    }
                    else
                    {
                        work.CurrentMetadata = directory.OpenMaintenance(AnalysisPrivateNames.CatalogEntry(review.CatalogId, pending.IncomingId));
                        if (MetadataIdentity(work.CurrentMetadata) != expected) throw new InvalidDataException("The current catalog record changed.");
                    }
                }
                if (action == OwnedAnalysisRecoveryAction.CopyPrevious)
                {
                    var source = proofs.Single(p => p.Display.Name == review.CopySource!.Name);
                    work.CopySource = directory.OpenRead(source.Display.Name); MatchRecoveryObject(work.CopySource, source);
                }
                else
                {
                    for (int index = 0; index < proofs.Length; index++)
                    {
                        var proof = proofs[index];
                        if (!proof.Display.Present) { RequireAbsent(directory, proof.Display.Name, token); continue; }
                        var file = directory.OpenReplacementFile(proof.Display.Name); work.Files[index] = file; MatchRecoveryObject(file, proof);
                    }
                }
                return true;
            }, cancellation).ConfigureAwait(false);
            if (action == OwnedAnalysisRecoveryAction.CopyPrevious)
            {
                var source = proofs.Single(p => p.Display.Name == review.CopySource!.Name);
                await VerifyRecoveryBytesAsync(work.CopySource!, source, cancellation).ConfigureAwait(false);
                work.MutationAttempted = true;
                work.Copied = await CopyRecoveryPayloadAsync(data, work.CopySource!, source.Inspection!, recoveryName!, cancellation).ConfigureAwait(false);
                await AnalysisIo.Run(_io, _device, _ => { work.CloseCopied(); return true; }, cancellation).ConfigureAwait(false);
                work.CopyConfirmed = true;
            }
            else
            {
                for (int index = 0; index < proofs.Length; index++)
                    if (work.Files[index] is { } file) await VerifyRecoveryBytesAsync(file, proofs[index], cancellation).ConfigureAwait(false);
                await AnalysisIo.Run(_io, _device, token => { RequireCatalog(data, review.CatalogId, token); work.Writer!.Verify(); work.Marker!.Verify(); return true; }, cancellation).ConfigureAwait(false);
                if (action == OwnedAnalysisRecoveryAction.RestorePrevious)
                {
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        token.ThrowIfCancellationRequested(); work.MutationAttempted = true;
                        if (work.CurrentMetadata is not null) { work.CurrentMetadata.RemoveOwned(); work.CloseCurrentMetadata(); }
                        token.ThrowIfCancellationRequested();
                        if (OwnedAnalysisReplacementRecoveryReview.IsIncoming(review.OriginalProof, pending))
                        { work.Files[2]!.ReplaceOwned(work.Files[0]!); work.CloseFile(0); work.CloseFile(2); work.MainConfirmed = true; }
                        else if (OwnedAnalysisReplacementRecoveryReview.IsOld(review.OriginalProof, pending))
                        { work.CloseFile(0); work.MainConfirmed = true; }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                    if (!review.Original.Present)
                    {
                        work.MutationAttempted = true;
                        work.Copied = await CopyRecoveryPayloadAsync(data, work.Files[2]!, review.PreviousProof.Inspection!, pending.Previous.Name, cancellation).ConfigureAwait(false);
                        await AnalysisIo.Run(_io, _device, _ => { work.CloseCopied(); work.MainConfirmed = true; return true; }, cancellation).ConfigureAwait(false);
                    }
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        if (!work.MainConfirmed) throw new IOException("The complete previous version is not confirmed at the original name.");
                        token.ThrowIfCancellationRequested(); RequireCatalog(data, review.CatalogId, token);
                        RetireRecoveryPayload(work, 1, review.Incoming.Name); RetireRecoveryPayload(work, 2, review.Previous.Name);
                        work.FinalMetadataAttempted = true;
                        work.Marker!.MoveOwnedNew(AnalysisPrivateNames.CatalogEntry(review.CatalogId, pending.Previous.InventoryId));
                        work.CloseMarker(); work.Resolved = true; return true;
                    }, cancellation).ConfigureAwait(false);
                }
                else
                {
                    var selected = OwnedAnalysisReplacementRecoveryReview.IsIncoming(review.OriginalProof, pending) ? review.OriginalProof : review.IncomingProof;
                    if (OwnedAnalysisReplacementRecoveryReview.IsOld(review.OriginalProof, pending) && work.Files[2] is null)
                    {
                        work.MutationAttempted = true;
                        work.Files[2] = await CopyRecoveryPayloadAsync(data, work.Files[0]!, review.OriginalProof.Inspection!, review.Previous.Name, cancellation).ConfigureAwait(false);
                    }
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        token.ThrowIfCancellationRequested(); work.MutationAttempted = true;
                        if (OwnedAnalysisReplacementRecoveryReview.IsOld(review.OriginalProof, pending))
                        { work.Files[1]!.ReplaceOwned(work.Files[0]!); work.CloseFile(0); work.CloseFile(1); work.MainConfirmed = true; }
                        else if (OwnedAnalysisReplacementRecoveryReview.IsIncoming(review.OriginalProof, pending))
                        { work.CloseFile(0); work.MainConfirmed = true; }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                    if (!review.Original.Present)
                    {
                        work.Copied = await CopyRecoveryPayloadAsync(data, work.Files[1]!, review.IncomingProof.Inspection!, pending.Previous.Name, cancellation).ConfigureAwait(false);
                        await AnalysisIo.Run(_io, _device, _ => { work.CloseCopied(); work.MainConfirmed = true; return true; }, cancellation).ConfigureAwait(false);
                    }
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        if (!work.MainConfirmed) throw new IOException("The complete incoming version is not confirmed at the original name.");
                        token.ThrowIfCancellationRequested(); RequireCatalog(data, review.CatalogId, token);
                        if (work.CurrentMetadata is not null) work.CloseCurrentMetadata();
                        else new AnalysisSavedCatalog(data.Store!).CompleteReplacement(review.CatalogId, new(pending.IncomingId, pending.Previous.Name,
                            selected.Inspection!.Bytes, DateTimeOffset.UtcNow, selected.Inspection.Coverage, selected.Inspection.Taint, AnalysisSavedView.Default), token);
                        work.IncomingCatalogConfirmed = true;
                        // This is now a confirmed saved incoming version. Cleanup cannot undo that result.
                        RetireRecoveryPayload(work, 1, review.Incoming.Name); RetireRecoveryPayload(work, 2, review.Previous.Name);
                        work.FinalMetadataAttempted = true; work.Marker!.RemoveOwned(); work.CloseMarker(); work.Resolved = true; return true;
                    }, cancellation).ConfigureAwait(false);
                }
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
            catch (Exception close) { failure = failure is null ? close : new AggregateException(failure, close); }
        }
        string name = recoveryName ?? pending.Previous.Name;
        AnalysisInventoryReceipt? receipt = work.CopyConfirmed ? new(pending.Previous.InventoryId, name, review.CopySource!.Inventory!.Bytes)
            : work.Resolved && action == OwnedAnalysisRecoveryAction.RestorePrevious ? new(pending.Previous.InventoryId, name, pending.Previous.Bytes)
            : work.IncomingCatalogConfirmed ? new(pending.IncomingId, name, (review.Original.Inventory?.InventoryId == pending.IncomingId ? review.Original : review.Incoming).Inventory!.Bytes) : null;
        if (failure is null)
        {
            var state = action == OwnedAnalysisRecoveryAction.CopyPrevious ? OwnedAnalysisRecoveryState.CopiedPrevious
                : action == OwnedAnalysisRecoveryAction.RestorePrevious ? OwnedAnalysisRecoveryState.Restored : OwnedAnalysisRecoveryState.FinishedIncoming;
            string detail = state switch
            {
                OwnedAnalysisRecoveryState.CopiedPrevious => "The previous inventory was copied to a new filename. Open it by filename using the default view. The pending replacement and its remaining files were kept.",
                OwnedAnalysisRecoveryState.Restored => "The previous named analysis was restored. The explicitly reviewed incoming version and replacement copies were discarded.",
                _ => "The incoming named analysis was confirmed. Previous-version cleanup finished." +
                    (pending.Current is null ? " Reconstructed display settings use the default view." : " Its saved display settings were kept.")
            };
            return Outcome(state, action == OwnedAnalysisRecoveryAction.CopyPrevious ? pending.OperationId : null, detail);
        }
        bool uncertain = AnalysisCloseUnconfirmedException.IsPresent(failure) || ContainsPublicationUncertainty(failure) || work.FinalMetadataAttempted;
        if (work.IncomingCatalogConfirmed && !uncertain)
            return Outcome(OwnedAnalysisRecoveryState.IncomingSavedCleanupPending, pending.OperationId,
                "The incoming named analysis was saved. Remaining previous-version cleanup is pending; inspect it again before maintenance.");
        var failed = uncertain ? OwnedAnalysisRecoveryState.Uncertain : work.MutationAttempted ? OwnedAnalysisRecoveryState.RecoveryRequired : OwnedAnalysisRecoveryState.Kept;
        if (failed == OwnedAnalysisRecoveryState.Kept && failure is OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        string problem = failed switch
        {
            OwnedAnalysisRecoveryState.Kept => "Recovery was not applied. Remaining files were kept. ",
            OwnedAnalysisRecoveryState.Uncertain => work.IncomingCatalogConfirmed
                ? "The incoming named analysis was saved, but cleanup could not be confirmed. Keep remaining files and inspect them again. "
                : "The recovery outcome could not be confirmed. Keep remaining files and inspect them again. ",
            _ => "Recovery is incomplete. Remaining versions and recovery metadata need fresh inspection. "
        };
        throw new OwnedAnalysisRecoveryException(Outcome(failed, pending.OperationId, problem + failure.Message), failure);

        OwnedAnalysisRecoveryOutcome Outcome(OwnedAnalysisRecoveryState state, Guid? operation, string detail) =>
            new(state, pending.Previous.InventoryId, pending.IncomingId, name, operation, receipt, detail);
    }

    private static void MatchRecoveryObject(AnalysisPrivateFile file, OwnedAnalysisReplacementRecoveryReview.FileProof proof)
    {
        file.Verify();
        if (proof.Inspection is null || file.Identity != proof.NativeIdentity || file.Stream.Length != proof.Inspection.Bytes)
            throw new InvalidDataException("A reviewed recovery file was substituted or changed. Its current occupant was preserved.");
    }
    private async Task VerifyRecoveryBytesAsync(AnalysisPrivateFile file, OwnedAnalysisReplacementRecoveryReview.FileProof proof, CancellationToken cancellation)
    {
        var current = await AnalysisInventoryFormat.InspectContainerAsync(file.Stream, file.Verify, _io, _device, cancellation).ConfigureAwait(false);
        if (current != proof.Inspection) throw new InvalidDataException("The complete reviewed recovery bytes changed. Obtain a fresh review.");
    }
    private static void RetireRecoveryPayload(RecoveryWork work, int index, string name)
    {
        if (work.Files[index] is not { } file) return;
        if (file.MaintenanceName != name) throw new InvalidOperationException("The recovery payload no longer has its reviewed generated name.");
        file.RemoveOwned(); work.CloseFile(index);
    }
    private sealed class RecoveryWork : IAnalysisScratchOwned
    {
        internal AnalysisPrivateFile? Writer, Marker, CurrentMetadata, Copied, CopySource;
        internal readonly AnalysisPrivateFile?[] Files = new AnalysisPrivateFile?[3];
        internal bool MutationAttempted, MainConfirmed, IncomingCatalogConfirmed, CopyConfirmed, FinalMetadataAttempted, Resolved;
        private static void Close(ref AnalysisPrivateFile? file)
        { var owned = Interlocked.Exchange(ref file, null); if (owned is not null) AnalysisIo.CloseOnWorker(owned); }
        internal void CloseFile(int index) => Close(ref Files[index]);
        internal void CloseCopied() => Close(ref Copied);
        internal void CloseMarker() => Close(ref Marker);
        internal void CloseCurrentMetadata() => Close(ref CurrentMetadata);
        public void Dispose()
        {
            var failures = new List<Exception>();
            void Attempt(Action close) { try { close(); } catch (Exception error) { failures.Add(error); } }
            for (int n = 0; n < Files.Length; n++) { int index = n; Attempt(() => CloseFile(index)); }
            Attempt(CloseCopied); Attempt(() => Close(ref CopySource)); Attempt(CloseCurrentMetadata); Attempt(CloseMarker); Attempt(() => Close(ref Writer));
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
}
