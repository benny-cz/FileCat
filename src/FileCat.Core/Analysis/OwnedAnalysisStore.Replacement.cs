namespace FileCat.Core.Analysis;

public sealed partial class OwnedAnalysisStore
{
    private async Task<OwnedAnalysisReplacementOutcome> ExecuteReplacementAsync(Contents data, OwnedAnalysisDeleteReview review,
        Guid incomingId, Guid operation, AnalysisSavedView view, AnalysisInventoryFormat.ContainerInspection incomingDetails,
        AnalysisPrivateInventoryStore.DatasetWrite prepared, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        var work = new ReplacementWork { Prepared = prepared };
        string originalMetadata = AnalysisPrivateNames.CatalogEntry(review.CatalogId, review.Entry.InventoryId);
        string pending = AnalysisPrivateNames.PendingReplacement(review.CatalogId, review.Entry.InventoryId, incomingId, operation);
        string incomingName = AnalysisPrivateNames.ReplacementIncoming(incomingId, operation);
        string previousName = AnalysisPrivateNames.ReplacementPrevious(review.Entry.InventoryId, operation);
        Exception? failure = null;
        try
        {
            await AnalysisIo.Run(_io, devices, token =>
            {
                work.Writer = data.Store!.OpenCatalogLock(token);
                var selected = Select(data, review.Entry.InventoryId, token);
                if (selected.Catalog != review.CatalogId || selected.Entry != review.Entry || selected.Pending is not null)
                    throw new InvalidDataException("The reviewed saved analysis changed. Prepare a new replacement review.");
                new AnalysisSavedCatalog(data.Store).ValidateReplacementBudget(review.Entry, incomingId, incomingDetails.Bytes,
                    incomingDetails.Coverage, incomingDetails.Taint, view, token);
                using var directory = data.Store.BorrowDirectory();
                work.Metadata = directory.OpenMaintenance(originalMetadata);
                if (MetadataIdentity(work.Metadata) != (review.MetadataIdentity, review.MetadataSha256))
                    throw new InvalidDataException("The reviewed metadata object or bytes changed.");
                work.Old = directory.OpenReplacementFile(review.Entry.Name);
                if (work.Old.Identity != review.PayloadIdentity || work.Old.Stream.Length != review.Entry.Bytes)
                    throw new InvalidDataException("The reviewed saved inventory object changed.");
                return true;
            }, cancellation).ConfigureAwait(false);
            var oldDetails = await AnalysisInventoryFormat.InspectContainerAsync(work.Old!.Stream, work.Old.Verify, _io, devices, cancellation).ConfigureAwait(false);
            ValidateContainer(review.Entry, oldDetails);
            if (oldDetails.Sha256 != review.PayloadSha256) throw new InvalidDataException("The reviewed saved bytes changed.");
            await AnalysisIo.Run(_io, devices, token =>
            {
                RequireCatalog(data, review.CatalogId, token); work.Writer!.Verify(); work.Old.Verify();
                token.ThrowIfCancellationRequested(); work.MutationAttempted = true; work.Metadata!.MoveOwnedNew(pending);
                work.Prepared!.PublishPrepared(incomingId, token); work.IncomingPublished = true;
                work.IncomingIdentity = work.Prepared.PublishedIdentity;
                work.ClosePrepared();
                using var directory = data.Store!.BorrowDirectory(); work.Incoming = directory.OpenReplacementFile(incomingName);
                if (work.Incoming.Identity != work.IncomingIdentity) throw new IOException("The incoming private name was substituted after publication.");
                return true;
            }, cancellation).ConfigureAwait(false);
            var confirmedIncoming = await AnalysisInventoryFormat.InspectContainerAsync(work.Incoming!.Stream, work.Incoming.Verify, _io, devices, cancellation).ConfigureAwait(false);
            if (confirmedIncoming != incomingDetails) throw new InvalidDataException("The published incoming inventory no longer matches the prepared bytes.");

            // Preserve a complete verified previous version above the usual free-space reserve.
            // No original-name switch occurs until both actual publication and close finish.
            work.Backup = await AnalysisIo.Run(_io, devices, token =>
            {
                work.Old.Verify(); work.Old.Stream.Position = 0;
                return AnalysisPrivateInventoryStore.BeginReplacementWrite(data.Store!.BorrowDirectory(), previousName, token);
            }, cancellation).ConfigureAwait(false);
            byte[] buffer = new byte[64 * 1024]; long copied = 0;
            while (copied < review.Entry.Bytes)
            {
                int count = await AnalysisIo.Run(_io, devices, token =>
                {
                    token.ThrowIfCancellationRequested(); work.Old.Verify();
                    int read = work.Old.Stream.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, review.Entry.Bytes - copied)));
                    if (read == 0) throw new InvalidDataException("The previous inventory ended during backup.");
                    work.Backup.Stream.Write(buffer.AsSpan(0, read)); work.Backup.FlushBatch(); return read;
                }, cancellation).ConfigureAwait(false);
                copied += count;
            }
            await AnalysisIo.Run(_io, devices, token => { token.ThrowIfCancellationRequested(); work.Backup.Prepare(); return true; }, cancellation).ConfigureAwait(false);
            var backupDetails = await AnalysisInventoryFormat.InspectContainerAsync(work.Backup.PreparedStream, work.Backup.Verify, _io, devices, cancellation).ConfigureAwait(false);
            ValidateContainer(review.Entry, backupDetails);
            if (backupDetails.Sha256 != review.PayloadSha256) throw new InvalidDataException("The complete previous-version backup could not be verified.");
            await AnalysisIo.Run(_io, devices, token =>
            {
                work.Backup.PublishPrepared(review.Entry.InventoryId, token); work.PreviousPublished = true;
                work.PreviousIdentity = work.Backup.PublishedIdentity; work.CloseBackup();
                using var directory = data.Store!.BorrowDirectory(); work.Previous = directory.OpenReplacementFile(previousName);
                if (work.Previous.Identity != work.PreviousIdentity) throw new IOException("The previous private name was substituted after publication.");
                RequireCatalog(data, review.CatalogId, token); work.Writer!.Verify(); work.Metadata!.Verify(); work.Old.Verify(); work.Incoming!.Verify();
                token.ThrowIfCancellationRequested(); work.SwitchAttempted = true;
                work.Incoming.ReplaceOwned(work.Old); work.Switched = true;
                work.CloseOld(); work.CloseIncoming(); work.NewDataClosed = true;
                token.ThrowIfCancellationRequested(); RequireCatalog(data, review.CatalogId, token);
                work.CatalogAttempted = true;
                new AnalysisSavedCatalog(data.Store!).CompleteReplacement(review.CatalogId, new(incomingId, review.Entry.Name,
                    incomingDetails.Bytes, DateTimeOffset.UtcNow, incomingDetails.Coverage, incomingDetails.Taint, view), token);
                work.Committed = true;
                // Cancellation after the logical commit cannot turn a confirmed save into a failure.
                work.Previous.RemoveOwned(); work.ClosePrevious();
                work.Metadata.RemoveOwned(); work.CloseMetadata(); work.Cleaned = true;
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;
            // Native state is updated before post-rename durability checks can throw.
            work.Switched |= work.Old?.RemovalRequested == true;
            if (!work.MutationAttempted) work.Restored = true;
            else if (!work.Committed && !work.CatalogAttempted && !AnalysisCloseUnconfirmedException.IsPresent(error) && !ContainsPublicationUncertainty(error))
            {
                try { await RollbackReplacementAsync(data, work, review, pending, originalMetadata, devices).ConfigureAwait(false); }
                catch (Exception rollback) { failure = new AggregateException(failure, rollback); }
            }
        }
        finally
        {
            try { await AnalysisIo.ReleaseScratchAsync(_io, devices, work).ConfigureAwait(false); }
            catch (Exception close) { failure = failure is null ? close : new AggregateException(failure, close); }
        }
        var receipt = work.Committed ? new AnalysisInventoryReceipt(incomingId, review.Entry.Name, incomingDetails.Bytes) : null;
        if (failure is null) return Outcome(OwnedAnalysisReplacementState.Replaced, null, receipt, "The named analysis was replaced. The previous private copy was removed.");
        bool uncertain = AnalysisCloseUnconfirmedException.IsPresent(failure) || ContainsPublicationUncertainty(failure) ||
            work.SwitchAttempted && !work.Switched && !work.Restored;
        if (work.Committed && !uncertain)
            return Outcome(OwnedAnalysisReplacementState.ReplacedPreviousKept, operation, receipt,
                "The new named analysis was saved. Previous-version cleanup is incomplete; review its pending replacement before further maintenance.");
        bool kept = !uncertain && work.Restored;
        if (kept && failure is OperationCanceledException) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        var state = uncertain ? OwnedAnalysisReplacementState.Uncertain : kept ? OwnedAnalysisReplacementState.Kept : OwnedAnalysisReplacementState.RecoveryRequired;
        string detail = state switch
        {
            OwnedAnalysisReplacementState.Kept => "The existing saved analysis was kept. Replacement was not completed. " + failure.Message,
            OwnedAnalysisReplacementState.Uncertain => work.Committed
                ? "The new analysis was saved, but private cleanup could not be confirmed. Keep remaining files and inspect pending maintenance."
                : "The replacement outcome could not be confirmed. Keep remaining files and inspect pending maintenance before retrying.",
            _ => "The replacement is incomplete. Remaining versions were preserved; inspect its pending replacement before retrying."
        };
        throw new OwnedAnalysisReplacementException(Outcome(state, kept ? null : operation, receipt, detail), failure);

        OwnedAnalysisReplacementOutcome Outcome(OwnedAnalysisReplacementState state, Guid? pendingOperation, AnalysisInventoryReceipt? saved, string detail) =>
            new(state, review.Entry.InventoryId, incomingId, review.Entry.Name, pendingOperation, saved, detail);
    }

    private Task<bool> RollbackReplacementAsync(Contents data, ReplacementWork work, OwnedAnalysisDeleteReview review,
        string pending, string originalMetadata, AnalysisIoDevices devices) => AnalysisIo.Run(_io, devices, _ =>
    {
        RequireCatalog(data, review.CatalogId, CancellationToken.None); work.Writer?.Verify();
        using var directory = data.Store!.BorrowDirectory();
        if (work.Switched)
        {
            if (work.Previous is null || work.Previous.Identity != work.PreviousIdentity) throw new IOException("The verified previous copy is unavailable for rollback.");
            work.Incoming ??= directory.OpenReplacementFile(review.Entry.Name);
            if (work.Incoming.Identity != work.IncomingIdentity) throw new IOException("The current name changed. Its new occupant was preserved.");
            work.Previous.ReplaceOwned(work.Incoming); work.CloseIncoming(); work.CloseOld();
            work.ClosePrevious(); work.PreviousPublished = false; work.IncomingPublished = false;
        }
        else
        {
            if (work.Old is not null) work.Old.Verify();
            // Only exact runtime-owned leases can be retired. Unknown publication/close outcomes
            // retain the marker rather than opening a name and assuming it belongs to this work.
            if (work.IncomingPublished)
            {
                if (work.Incoming is null || work.Incoming.Identity != work.IncomingIdentity) throw new IOException("The incoming private publication needs fresh recovery inspection.");
                work.Incoming.RemoveOwned(); work.CloseIncoming(); work.IncomingPublished = false;
            }
            if (work.PreviousPublished)
            {
                if (work.Previous is null || work.Previous.Identity != work.PreviousIdentity) throw new IOException("The previous private publication needs fresh recovery inspection.");
                work.Previous.RemoveOwned(); work.ClosePrevious(); work.PreviousPublished = false;
            }
        }
        if (work.Metadata?.MaintenanceName == pending) work.Metadata.MoveOwnedNew(originalMetadata);
        else if (work.Metadata is not null && work.Metadata.MaintenanceName != originalMetadata) throw new IOException("The previous catalog record could not be restored.");
        work.Restored = true; return true;
    }, CancellationToken.None);

    private static bool ContainsPublicationUncertainty(Exception error) => error is AnalysisPublicationUncertainException ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsPublicationUncertainty) ||
        error.InnerException is { } inner && ContainsPublicationUncertainty(inner);

    private sealed class ReplacementWork : IAnalysisScratchOwned
    {
        internal AnalysisPrivateFile? Writer, Metadata, Old, Incoming, Previous;
        internal AnalysisPrivateInventoryStore.DatasetWrite? Prepared, Backup;
        internal string? IncomingIdentity, PreviousIdentity;
        internal bool MutationAttempted, IncomingPublished, PreviousPublished, SwitchAttempted, Switched, NewDataClosed, CatalogAttempted, Committed, Cleaned, Restored;
        private static void Close<T>(ref T? source) where T : class, IDisposable
        { var owned = Interlocked.Exchange(ref source, null); if (owned is not null) AnalysisIo.CloseOnWorker(owned); }
        internal void ClosePrepared() => Close(ref Prepared);
        internal void CloseBackup() => Close(ref Backup);
        internal void CloseOld() => Close(ref Old);
        internal void CloseIncoming() => Close(ref Incoming);
        internal void ClosePrevious() => Close(ref Previous);
        internal void CloseMetadata() => Close(ref Metadata);
        public void Dispose()
        {
            var failures = new List<Exception>();
            void Attempt(Action close) { try { close(); } catch (Exception error) { failures.Add(error); } }
            Attempt(ClosePrepared); Attempt(CloseBackup); Attempt(CloseOld); Attempt(CloseIncoming); Attempt(ClosePrevious); Attempt(CloseMetadata);
            Attempt(() => Close(ref Writer));
            if (failures.Count != 0) throw new AggregateException(failures);
        }
    }
}
