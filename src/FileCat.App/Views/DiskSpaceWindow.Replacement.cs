using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private async Task ReplaceSavedAsync(Guid previous, AnalysisSnapshotLease revision)
        {
            try
            {
                await using var captured = await WithInventoryProgress("Preparing replacement inventory", token => Model.PrepareSavedReplacementAsync(previous, revision, token));
                if (_disposed || !await SavedAnalysisReplacementDialog.ConfirmAsync(_window._dialogs, captured.Review, Model.SavedDirectory ?? ""))
                { Model.SetStatus("The existing saved analysis was kept."); return; }
                if (_disposed || !_window.IsVisible) return;
                var result = await WithInventoryProgress("Replacing named analysis", token => captured.ReplaceAsync(token));
                if (result.Receipt is { } receipt) Model.RememberReplacement(revision, receipt);
                Model.SetStatus(result.Detail);
            }
            catch (OwnedAnalysisReplacementException error)
            {
                _actualWork.RecordUnconfirmedClose(error);
                if (error.Outcome.Receipt is { } receipt) Model.RememberReplacement(revision, receipt);
                Model.SetStatus(error.Outcome.Detail);
            }
            catch (OperationCanceledException) { Model.SetStatus("Named replacement canceled. Inspect any pending entry before retrying."); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus("Private storage close could not be confirmed. Keep these observations and inspect the saved name before retrying."); }
            catch (Exception error) when (InventoryError(error))
            { Model.SetStatus("Replacement was not completed. Keep the remaining files and choose a new name if needed. " + AnalysisNames.Display(error.Message, 400)); }
        }
        private async Task RecoverSavedAsync(Guid operation)
        {
            try
            {
                await using var captured = await WithInventoryProgress("Inspecting pending replacement", token => Model.PrepareSavedRecoveryAsync(operation, token));
                if (_disposed) return;
                var decision = await SavedAnalysisRecoveryDialog.ReviewAsync(_window._dialogs, captured.Review, Model.SavedDirectory ?? "");
                if (_disposed || decision is not { } chosen) { Model.SetStatus("The replacement was left pending. Remaining files were kept."); return; }
                var result = await WithInventoryProgress("Recovering named analysis", token => captured.ApplyAsync(chosen.Action, chosen.Name, token));
                Model.SetStatus(result.Detail);
            }
            catch (OwnedAnalysisRecoveryException error) { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus(error.Outcome.Detail); }
            catch (OperationCanceledException) { Model.SetStatus("Recovery canceled. Inspect remaining versions before retrying."); }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus("Private storage close could not be confirmed. Keep remaining files and inspect the pending replacement again."); }
            catch (Exception error) when (InventoryError(error)) { Model.SetStatus("Recovery unavailable: " + AnalysisNames.Display(error.Message, 400)); }
        }
    }
    private sealed partial class IndexedSessionView
    {
        private async Task ReplaceInventoryAsync(Guid previous, AnalysisDatasetLease source, AnalysisSavedView view, CancellationTokenSource cancel)
        {
            try
            {
                await using var captured = await InventoryProgressAsync("Preparing replacement inventory", token =>
                    AnalysisOwnedStoreAccess.PrepareReplacementAsync(_services!, previous, source, view, _actualWork, null, token), cancel);
                var closer = new ViewModels.DialogCloser(); _inventoryCloser = closer;
                if (_disposed || !await SavedAnalysisReplacementDialog.ConfirmAsync(_window._dialogs, captured.Review, _services!.Paths.AnalysisSnapshotsDirectory, closer))
                { if (!_disposed) PersistenceStatus = "The existing saved analysis was kept."; return; }
                if (_disposed || !_window.IsVisible) return;
                var result = await InventoryProgressAsync("Replacing named analysis", token => captured.ReplaceAsync(token), cancel);
                if (result.Receipt is { } receipt) { _revisionSaved = true; _savedInventoryId = receipt.InventoryId; _savedInventoryName = receipt.Name; }
                if (!_disposed) PersistenceStatus = result.Detail;
            }
            catch (OwnedAnalysisReplacementException error)
            {
                RememberViewCloseFailure(error);
                if (error.Outcome.Receipt is { } receipt) { _revisionSaved = true; _savedInventoryId = receipt.InventoryId; _savedInventoryName = receipt.Name; }
                if (!_disposed) PersistenceStatus = error.Outcome.Detail;
            }
            catch (OperationCanceledException) { if (!_disposed) PersistenceStatus = "Named replacement canceled. Inspect any pending entry before retrying."; }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            { RememberViewCloseFailure(error); if (!_disposed) PersistenceStatus = "Private storage close could not be confirmed. Keep these observations and inspect the saved name before retrying."; }
            catch (Exception error) when (Expected(error))
            { if (!_disposed) PersistenceStatus = "Replacement was not completed. Keep remaining files and choose a new name if needed. " + AnalysisNames.Display(error.Message, 400); }
        }
        private async Task RecoverSavedAsync(Guid operation, CancellationTokenSource cancel)
        {
            try
            {
                await using var captured = await InventoryProgressAsync("Inspecting pending replacement", token => AnalysisOwnedStoreAccess.PrepareRecoveryAsync(_services!, operation, _actualWork, token), cancel);
                if (_disposed) return;
                var decision = await SavedAnalysisRecoveryDialog.ReviewAsync(_window._dialogs, captured.Review, _services!.Paths.AnalysisSnapshotsDirectory,
                    () => _inventoryCloser = new ViewModels.DialogCloser());
                if (_disposed || decision is not { } chosen) { if (!_disposed) PersistenceStatus = "The replacement was left pending. Remaining files were kept."; return; }
                var result = await InventoryProgressAsync("Recovering named analysis", token => captured.ApplyAsync(chosen.Action, chosen.Name, token), cancel);
                if (!_disposed) PersistenceStatus = result.Detail;
            }
            catch (OwnedAnalysisRecoveryException error) { RememberViewCloseFailure(error); if (!_disposed) PersistenceStatus = error.Outcome.Detail; }
            catch (OperationCanceledException) { if (!_disposed) PersistenceStatus = "Recovery canceled. Inspect remaining versions before retrying."; }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            { RememberViewCloseFailure(error); if (!_disposed) PersistenceStatus = "Private storage close could not be confirmed. Keep remaining files and inspect the pending replacement again."; }
            catch (Exception error) when (Expected(error)) { if (!_disposed) PersistenceStatus = "Recovery unavailable: " + AnalysisNames.Display(error.Message, 400); }
        }
    }
}
