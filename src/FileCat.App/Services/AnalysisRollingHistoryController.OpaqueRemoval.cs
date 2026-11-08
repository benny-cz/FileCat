using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal sealed partial class AnalysisRollingHistoryController
{
    internal Task<AnalysisHistoryOpaqueRemovalCapture> PrepareOpaqueHistoryRemovalAsync(AnalysisHistoryRow row, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        OwnedAnalysisStore? store = null; IDisposable? protection = null;
        try
        {
            if (row.State == AnalysisHistoryRowState.PendingAbsent) throw new InvalidOperationException("Finish the pending metadata removal instead.");
            store = await AnalysisRollingHistoryAccess.OpenReviewAsync(services, cancellation).ConfigureAwait(false);
            using (var catalog = await store.ReadCatalogAsync(cancellation).ConfigureAwait(false))
            {
                if (row.State == AnalysisHistoryRowState.Unindexed)
                {
                    if (row.CatalogId != catalog.CatalogId || !catalog.UnindexedNames.Contains(row.Name, StringComparer.Ordinal))
                        throw new InvalidDataException("History changed. Refresh before reviewing removal.");
                }
                else _ = SelectRow(catalog, row);
            }
            protection = Protect(row.InventoryId);
            var review = await store.PrepareOpaqueHistoryDeleteAsync(row.Name, cancellation).ConfigureAwait(false);
            var result = new AnalysisHistoryOpaqueRemovalCapture(this, store, review, protection); store = null; protection = null; return result;
        }
        finally
        {
            try { if (store is not null) await store.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { RecordHistoryFailure(error); throw; }
            finally { if (UnconfirmedClose is null) protection?.Dispose(); }
        }
    }, cancellation);

    internal Task<OwnedAnalysisDeletionOutcome> RemoveOpaqueHistoryAsync(AnalysisHistoryOpaqueRemovalCapture capture, CancellationToken cancellation) => HistoryWorkAsync(async () =>
    {
        if (!ReferenceEquals(capture.Controller, this)) throw new InvalidOperationException("The removal review belongs to another profile.");
        capture.Choose();
        lock (_protectionGate)
            if (_protectedHistory.GetValueOrDefault(capture.Review.InventoryId) != 1)
                throw new InvalidOperationException("This copy is used by an open view or another review. Close that work and obtain a fresh removal review.");
        return await capture.Store.DeleteOpaqueHistoryAsync(capture.Review, cancellation).ConfigureAwait(false);
    }, cancellation);
}

internal sealed class AnalysisHistoryOpaqueRemovalCapture(AnalysisRollingHistoryController controller, OwnedAnalysisStore store,
    AnalysisRollingOpaqueDeleteReview review, IDisposable protection) : IAsyncDisposable
{
    internal AnalysisRollingHistoryController Controller => controller;
    internal OwnedAnalysisStore Store => store;
    internal AnalysisRollingOpaqueDeleteReview Review => review;
    private int _chosen;
    internal void Choose() { if (Interlocked.Exchange(ref _chosen, 1) != 0) throw new InvalidOperationException("Obtain a fresh removal review."); }
    private Task? _close;
    public ValueTask DisposeAsync() { lock (this) return new(_close ??= CloseAsync()); }
    private async Task CloseAsync()
    {
        try { await store.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { controller.RecordHistoryFailure(error); throw; }
        if (controller.UnconfirmedClose is null) protection.Dispose();
    }
}
