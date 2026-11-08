using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal static partial class AnalysisOwnedStoreAccess
{
    internal static async Task<AnalysisSavedEntry?> FindReplacementTargetAsync(AppServices services, string name, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateName(name);
        await using var owned = await OpenAsync(services, cancellation); using var catalog = await owned.ReadCatalogAsync(cancellation);
        if (catalog.PendingReplacements.Any(p => p.Previous.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
            catalog.PendingDeletions.Any(p => p.Entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("This name has pending maintenance. Open saved analyses and review the pending entry, or choose a new name.");
        if (catalog.UnindexedNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("An unindexed inventory already uses this name. Keep it and choose a new name.");
        return catalog.Entries.SingleOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    internal static Task<AnalysisOwnedReplacementCapture> PrepareReplacementAsync(AppServices services, Guid previous, AnalysisSnapshot snapshot,
        AnalysisSavedView view, AnalysisViewWork work, Action? closed, CancellationToken cancellation) =>
        PrepareReplacementCoreAsync(services, store => store.PrepareReplacementAsync(previous, snapshot, view, cancellation), work, closed, cancellation);
    internal static Task<AnalysisOwnedReplacementCapture> PrepareReplacementAsync(AppServices services, Guid previous, AnalysisDatasetLease source,
        AnalysisSavedView view, AnalysisViewWork work, Action? closed, CancellationToken cancellation) =>
        PrepareReplacementCoreAsync(services, store => store.PrepareReplacementAsync(previous, source, view, cancellation), work, closed, cancellation);
    private static async Task<AnalysisOwnedReplacementCapture> PrepareReplacementCoreAsync(AppServices services,
        Func<OwnedAnalysisStore, Task<OwnedAnalysisReplacementReview>> prepare, AnalysisViewWork work, Action? closed, CancellationToken cancellation)
    {
        var actual = work.Enter(); OwnedAnalysisStore? store = null;
        try
        {
            store = await OpenAsync(services, cancellation); var review = await prepare(store);
            return new(store, review, actual, work.RecordUnconfirmedClose, closed,
                (id, name) => services.AnalysisSavedReferences.RemovedAsync(id, name, comparison: false));
        }
        catch (Exception error)
        {
            work.RecordUnconfirmedClose(error);
            try { if (store is not null) await store.DisposeAsync(); }
            catch (Exception close) { work.RecordUnconfirmedClose(close); throw new AggregateException(error, close); }
            finally { actual.Dispose(); }
            throw;
        }
    }
    internal static async Task<AnalysisOwnedRecoveryCapture> PrepareRecoveryAsync(AppServices services, Guid operation, AnalysisViewWork work, CancellationToken cancellation)
    {
        var actual = work.Enter(); OwnedAnalysisStore? store = null;
        try
        {
            store = await OpenAsync(services, cancellation); var review = await store.PrepareReplacementRecoveryAsync(operation, cancellation);
            return new(store, review, actual, work.RecordUnconfirmedClose,
                (id, name) => services.AnalysisSavedReferences.RemovedAsync(id, name, comparison: false));
        }
        catch (Exception error)
        {
            work.RecordUnconfirmedClose(error);
            try { if (store is not null) await store.DisposeAsync(); }
            catch (Exception close) { work.RecordUnconfirmedClose(close); throw new AggregateException(error, close); }
            finally { actual.Dispose(); }
            throw;
        }
    }
}

internal sealed class AnalysisOwnedReplacementCapture(OwnedAnalysisStore store, OwnedAnalysisReplacementReview review, IDisposable actual,
    Action<Exception> recordCloseFailure, Action? closed, Func<Guid, string, Task> removed) : IAsyncDisposable
{
    private Exception? _operationFailure;
    internal OwnedAnalysisReplacementReview Review => review;
    internal async Task<OwnedAnalysisReplacementOutcome> ReplaceAsync(CancellationToken cancellation)
    {
        try
        {
            var result = await store.ReplaceAsync(review, cancellation);
            if (result.State != OwnedAnalysisReplacementState.Kept) await removed(result.PreviousId, result.Name);
            return result;
        }
        catch (Exception error)
        {
            _operationFailure = error; recordCloseFailure(error);
            if (error is OwnedAnalysisReplacementException { Outcome.State: not OwnedAnalysisReplacementState.Kept } failure)
                await removed(failure.Outcome.PreviousId, failure.Outcome.Name);
            else if (AnalysisCloseUnconfirmedException.IsPresent(error)) await removed(review.Previous.Entry.InventoryId, review.Previous.Entry.Name);
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        try { await review.DisposeAsync(); }
        catch (Exception error)
        {
            recordCloseFailure(error);
            // Completion is cached. An already observed execution error is not a second cleanup
            // failure and must not overwrite its structured outcome in the dialog's finally.
            if (!ReferenceEquals(error, _operationFailure)) failure = error;
        }
        try { await store.DisposeAsync(); }
        catch (Exception error) { recordCloseFailure(error); failure = AnalysisViewWork.PreserveCloseFailure(failure, error); }
        finally { try { closed?.Invoke(); } finally { actual.Dispose(); } }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

internal sealed class AnalysisOwnedRecoveryCapture(OwnedAnalysisStore store, OwnedAnalysisReplacementRecoveryReview review, IDisposable actual,
    Action<Exception> recordCloseFailure, Func<Guid, string, Task> removed) : IAsyncDisposable
{
    internal OwnedAnalysisReplacementRecoveryReview Review => review;
    internal async Task<OwnedAnalysisRecoveryOutcome> ApplyAsync(OwnedAnalysisRecoveryAction action, string? name, CancellationToken cancellation)
    {
        try
        {
            var result = await store.RecoverReplacementAsync(review, action, name, cancellation);
            if (result.State == OwnedAnalysisRecoveryState.Restored) await removed(result.IncomingId, review.Pending.Previous.Name);
            else if (result.State is OwnedAnalysisRecoveryState.FinishedIncoming or OwnedAnalysisRecoveryState.IncomingSavedCleanupPending)
                await removed(result.PreviousId, review.Pending.Previous.Name);
            return result;
        }
        catch (Exception error)
        {
            recordCloseFailure(error);
            if (action != OwnedAnalysisRecoveryAction.CopyPrevious &&
                (error is OwnedAnalysisRecoveryException { Outcome.State: OwnedAnalysisRecoveryState.RecoveryRequired or OwnedAnalysisRecoveryState.Uncertain } || AnalysisCloseUnconfirmedException.IsPresent(error)))
            {
                await removed(review.Pending.Previous.InventoryId, review.Pending.Previous.Name);
                if (error is not OwnedAnalysisRecoveryException { Outcome.Receipt.InventoryId: var confirmed } || confirmed != review.Pending.IncomingId)
                    await removed(review.Pending.IncomingId, review.Pending.Previous.Name);
            }
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        try { await store.DisposeAsync(); }
        catch (Exception error) { recordCloseFailure(error); throw; }
        finally { actual.Dispose(); }
    }
}
