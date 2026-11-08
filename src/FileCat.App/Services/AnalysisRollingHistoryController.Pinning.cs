using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal sealed record AnalysisHistoryPinResult(AnalysisInventoryReceipt? Receipt, string Detail);
internal sealed class AnalysisHistoryPinException(AnalysisInventoryReceipt receipt, Exception inner)
    : IOException("The named copy was published, but its final cleanup could not be confirmed. Keep it and inspect the named store before retrying.", inner)
{ internal AnalysisInventoryReceipt Receipt { get; } = receipt; }

internal sealed partial class AnalysisRollingHistoryController
{
    internal async Task<AnalysisHistoryPinResult> PinHistoryAsync(AnalysisHistoryRow row, string name, IDialogService dialogs, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateName(name); ArgumentNullException.ThrowIfNull(dialogs);
        AnalysisHistoryOpenSource? source = null; AnalysisDatasetLease? retained = null;
        AnalysisInventoryReceipt? receipt = null; Exception? failure = null; bool kept = false;
        try
        {
            source = await AnalysisHistoryBrowser.ProgressAsync("Preparing complete history copy", dialogs, token => OpenHistoryAsync(row, token), cancellation).ConfigureAwait(true);
            retained = await source.AcquireAsync(cancellation).ConfigureAwait(true);
            var existing = await AnalysisHistoryBrowser.ProgressAsync("Checking named destination", dialogs, token => AnalysisOwnedStoreAccess.FindReplacementTargetAsync(services, name, token), cancellation).ConfigureAwait(true);
            if (existing is not null)
            {
                await using var owner = await AnalysisHistoryBrowser.ProgressAsync("Opening named store", dialogs, token => AnalysisOwnedStoreAccess.OpenAsync(services, token), cancellation).ConfigureAwait(true);
                await using var review = await AnalysisHistoryBrowser.ProgressAsync("Preparing named replacement", dialogs, token => owner.PrepareReplacementAsync(existing.InventoryId, retained, source.Entry.View, token), cancellation).ConfigureAwait(true);
                if (await SavedAnalysisReplacementDialog.ConfirmAsync(dialogs, review, services.Paths.AnalysisSnapshotsDirectory).ConfigureAwait(true))
                {
                    var outcome = await AnalysisHistoryBrowser.ProgressAsync("Saving named replacement", dialogs, token => owner.ReplaceAsync(review, token), cancellation).ConfigureAwait(true);
                    receipt = outcome.Receipt;
                    if (receipt is null) throw new IOException(outcome.Detail);
                    await services.AnalysisSavedReferences.RemovedAsync(existing.InventoryId, existing.Name, false).ConfigureAwait(true);
                }
                else { await owner.KeepExistingAsync(review).ConfigureAwait(true); kept = true; }
            }
            else
            {
                await using var owner = await AnalysisHistoryBrowser.ProgressAsync("Opening named store", dialogs, token => AnalysisOwnedStoreAccess.OpenAsync(services, token), cancellation).ConfigureAwait(true);
                receipt = await AnalysisHistoryBrowser.ProgressAsync("Saving independent named copy", dialogs, token => owner.SaveCopyNewAsync(retained, name, source.Entry.View, token), cancellation).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            failure = error;
            if (error is AnalysisCatalogUpdateException catalog) receipt = catalog.Receipt;
            else if (error is OwnedAnalysisReplacementException replacement)
            {
                receipt = replacement.Outcome.Receipt;
                if (replacement.Outcome.State != OwnedAnalysisReplacementState.Kept)
                    await services.AnalysisSavedReferences.RemovedAsync(replacement.Outcome.PreviousId, replacement.Outcome.Name, false).ConfigureAwait(true);
            }
        }
        finally
        {
            try { if (retained is not null) await retained.DisposeAsync().ConfigureAwait(true); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            try { if (source is not null) await source.DisposeAsync().ConfigureAwait(true); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        }
        if (failure is not null)
        {
            RecordHistoryFailure(failure);
            if (receipt is not null) throw new AnalysisHistoryPinException(receipt, failure);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return new(receipt, kept ? "The existing named analysis was kept. No named copy was replaced and history was unchanged."
            : "An independent named copy was saved. Rolling retention cannot prune that named copy; the original history entry remains subject to its policy.");
    }
}
