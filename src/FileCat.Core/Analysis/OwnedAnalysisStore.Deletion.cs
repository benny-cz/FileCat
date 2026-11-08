namespace FileCat.Core.Analysis;

public sealed partial class OwnedAnalysisStore
{
    public Task<OwnedAnalysisDeletionOutcome> DeleteAsync(OwnedAnalysisDeleteReview review, CancellationToken cancellation = default) =>
        ChangeNamedAsync(review, restore: false, cancellation);
    public Task<OwnedAnalysisDeletionOutcome> RestoreNamedAsync(OwnedAnalysisDeleteReview review, CancellationToken cancellation = default) =>
        ChangeNamedAsync(review, restore: true, cancellation);
    private Task<OwnedAnalysisDeletionOutcome> DeleteUnderHistoryLockAsync(OwnedAnalysisDeleteReview review, AnalysisPrivateFile held, CancellationToken cancellation) =>
        ChangeNamedAsync(review, restore: false, cancellation, held);

    private Task<OwnedAnalysisDeletionOutcome> ChangeNamedAsync(OwnedAnalysisDeleteReview review, bool restore, CancellationToken cancellation, AnalysisPrivateFile? inheritedWriter = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (restore && (!review.IsRecovery || !review.PayloadPresent)) throw new InvalidOperationException("Only a pending analysis whose saved file is present can be restored.");
        if (restore && review.OpaqueBytes is not null) throw new InvalidOperationException("Restore requires a fresh valid-inventory review.");
        review.Consume(this);
        return FinishDeletionAsync(review.Entry.InventoryId, review.Entry.Name, () => _owned.UseAsync<OwnedAnalysisDeletionOutcome>(async data =>
        {
            var work = new DeletionWork(); Exception? failure = null;
            Guid operation = review.IsRecovery ? review.ExistingOperation : Guid.NewGuid();
            string original = AnalysisPrivateNames.CatalogEntry(review.CatalogId, review.Entry.InventoryId);
            string pending = AnalysisPrivateNames.PendingDeletion(review.CatalogId, review.Entry.InventoryId, operation);
            try
            {
                await AnalysisIo.Run(_io, _device, token =>
                {
                    if (review.OpaqueBytes is not null && !data.Rolling) throw new InvalidOperationException("Opaque removal requires an isolated history owner.");
                    if (review.OpaqueBytes is not null && review.Entry.Name != RollingName(review.Entry.InventoryId)) throw new InvalidDataException("Opaque removal is limited to generated history names.");
                    work.Writer = inheritedWriter ?? data.Store!.OpenCatalogLock(token); work.InheritedWriter = inheritedWriter is not null;
                    work.Writer.Verify();
                    var selected = Select(data, review.Entry.InventoryId, token);
                    if (selected.Catalog != review.CatalogId || selected.Entry != review.Entry || selected.PayloadPresent != review.PayloadPresent ||
                        (selected.Pending?.OperationId ?? Guid.Empty) != review.ExistingOperation)
                        throw new InvalidDataException("The reviewed saved analysis changed. Obtain a fresh review.");
                    using var directory = data.Store!.BorrowDirectory();
                    work.Metadata = directory.OpenMaintenance(selected.MetadataName);
                    var metadata = MetadataIdentity(work.Metadata);
                    if (metadata.Identity != review.MetadataIdentity || metadata.Sha256 != review.MetadataSha256)
                        throw new InvalidDataException("The reviewed metadata object or bytes changed. Obtain a fresh review.");
                    if (review.PayloadPresent)
                    {
                        work.Payload = directory.OpenMaintenance(review.Entry.Name);
                        if (work.Payload.Identity != review.PayloadIdentity || work.Payload.Stream.Length != (review.OpaqueBytes ?? review.Entry.Bytes))
                            throw new InvalidDataException("The reviewed inventory object changed. Obtain a fresh review.");
                    }
                    else RequireAbsent(directory, review.Entry.Name, token);
                    return true;
                }, cancellation).ConfigureAwait(false);
                if (work.Payload is not null)
                {
                    if (review.OpaqueBytes is { } opaqueBytes)
                    {
                        var raw = await InspectOpaqueBytesAsync(work.Payload.Stream, work.Payload.Verify, cancellation).ConfigureAwait(false);
                        if (raw.Bytes != opaqueBytes || raw.Sha256 != review.PayloadSha256) throw new InvalidDataException("The reviewed private history bytes changed. Review them again.");
                    }
                    else
                    {
                        var inspected = await AnalysisInventoryFormat.InspectContainerAsync(work.Payload.Stream, work.Payload.Verify,
                            _io, _device, cancellation).ConfigureAwait(false);
                        ValidateContainer(review.Entry, inspected);
                        if (inspected.Sha256 != review.PayloadSha256) throw new InvalidDataException("The reviewed saved bytes changed. Obtain a fresh review.");
                    }
                }
                await AnalysisIo.Run(_io, _device, token =>
                {
                    RequireCatalog(data, review.CatalogId, token); work.Writer!.Verify(); work.Metadata!.Verify(); work.Payload?.Verify();
                    token.ThrowIfCancellationRequested();
                    if (restore)
                    {
                        work.MutationAttempted = true;
                        work.Metadata.MoveOwnedNew(original); work.Restored = true;
                        work.CloseMetadata(); return true;
                    }
                    if (!review.IsRecovery)
                    {
                        work.MutationAttempted = true;
                        work.Metadata.MoveOwnedNew(pending); work.NewPending = true;
                    }
                    token.ThrowIfCancellationRequested();
                    if (work.Payload is not null)
                    {
                        work.DataRemovalAttempted = true;
                        work.Payload.RemoveOwned(); work.ClosePayload(); work.PayloadAbsent = true;
                    }
                    else
                    {
                        using var directory = data.Store!.BorrowDirectory(); RequireAbsent(directory, review.Entry.Name, token); work.PayloadAbsent = true;
                    }
                    // Once data is removed, complete only the reviewed private metadata cleanup.
                    work.MutationAttempted = true;
                    work.Metadata.RemoveOwned(); work.MetadataRemovalRequested = true;
                    work.CloseMetadata(); work.MetadataAbsent = true;
                    return true;
                }, cancellation).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
                if (work.Payload is { RemovalRequested: false }) work.DataRemovalAttempted = false;
                work.MetadataRemovalRequested |= work.Metadata?.RemovalRequested == true;
                if (!review.IsRecovery && work.Metadata is { RemovalRequested: false } moved && moved.MaintenanceName == pending)
                    work.NewPending = true;
                // No payload loss: put the exact held catalog record back, create-new only.
                // A failed rollback leaves an explicit pending entry, never a replay instruction.
                if (!restore && !review.IsRecovery && !work.DataRemovalAttempted && work.Metadata is { RemovalRequested: false } metadata && metadata.MaintenanceName == pending)
                {
                    try
                    {
                        await AnalysisIo.Run(_io, _device, _ =>
                        {
                            RequireCatalog(data, review.CatalogId, CancellationToken.None); metadata.MoveOwnedNew(original);
                            work.NewPending = false; work.Restored = true; return true;
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollback)
                    {
                        work.RollbackUnconfirmed = metadata.MaintenanceName != pending;
                        failure = new AggregateException(failure, rollback);
                    }
                }
            }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(_io, _device, work).ConfigureAwait(false); }
                catch (Exception close) { failure = failure is null ? close : new AggregateException(failure, close); }
            }
            if (failure is null)
                return restore
                    ? new(OwnedAnalysisDeletionState.Restored, review.Entry.InventoryId, review.Entry.Name, false, null, "The named analysis was restored. Its saved file was kept.")
                    : new(OwnedAnalysisDeletionState.Deleted, review.Entry.InventoryId, review.Entry.Name, true, null, "The named analysis and its private catalog record were deleted. Source files were not changed.");

            bool uncertain = AnalysisCloseUnconfirmedException.IsPresent(failure) || work.RollbackUnconfirmed ||
                work.DataRemovalAttempted && !work.PayloadAbsent || work.MetadataRemovalRequested && !work.MetadataAbsent ||
                restore && work.MutationAttempted && !work.Restored;
            bool needsRecovery = review.IsRecovery && !work.Restored && !work.MetadataAbsent || work.NewPending || work.DataRemovalAttempted && !work.MetadataAbsent;
            var state = uncertain ? OwnedAnalysisDeletionState.Uncertain : needsRecovery ? OwnedAnalysisDeletionState.RecoveryRequired : OwnedAnalysisDeletionState.Kept;
            string detail = state switch
            {
                OwnedAnalysisDeletionState.Uncertain => "The private-store outcome could not be confirmed. Keep remaining files and inspect pending maintenance before retrying.",
                OwnedAnalysisDeletionState.RecoveryRequired => work.PayloadAbsent
                    ? "The saved inventory was removed, but its private catalog cleanup is incomplete. Select its pending entry to finish deletion."
                    : "The saved inventory was kept. Its catalog record needs attention; select the pending entry to restore it or review deletion again.",
                _ => "The saved analysis was kept; deletion was not completed. " + failure.Message
            };
            if (failure is OperationCanceledException && state == OwnedAnalysisDeletionState.Kept)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
            throw new OwnedAnalysisDeletionException(new(state, review.Entry.InventoryId, review.Entry.Name, work.PayloadAbsent,
                needsRecovery || uncertain ? operation : null, detail), failure);
        }));
    }
    private static void RequireAbsent(AnalysisPrivateDirectory directory, string name, CancellationToken cancellation)
    {
        if (directory.Entries(cancellation).Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A saved file appeared after review. Obtain a fresh review; no replacement is permitted.");
    }
    private sealed class DeletionWork : IAnalysisScratchOwned
    {
        internal AnalysisPrivateFile? Writer, Metadata, Payload;
        internal bool InheritedWriter, MutationAttempted, NewPending, Restored, DataRemovalAttempted, PayloadAbsent, MetadataAbsent,
            MetadataRemovalRequested, RollbackUnconfirmed;
        internal void ClosePayload() => Close(ref Payload);
        internal void CloseMetadata() => Close(ref Metadata);
        private static void Close(ref AnalysisPrivateFile? file)
        {
            var owned = Interlocked.Exchange(ref file, null);
            if (owned is not null) AnalysisIo.CloseOnWorker(owned);
        }
        public void Dispose()
        {
            var failures = new List<Exception>();
            try { Close(ref Payload); } catch (Exception error) { failures.Add(error); }
            try { Close(ref Metadata); } catch (Exception error) { failures.Add(error); }
            try { if (InheritedWriter) Writer = null; else Close(ref Writer); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
}
