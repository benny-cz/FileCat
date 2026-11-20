using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private async Task MaintainSavedAsync(Guid inventory, bool restore)
        {
            try
            {
                await using var captured = await WithInventoryProgress("Checking saved analysis", token => Model.PrepareSavedMaintenanceAsync(inventory, token));
                if (_disposed || !await SavedAnalysisMaintenanceDialog.ConfirmAsync(_window._dialogs, captured.Review, Model.SavedDirectory ?? "", restore))
                { Model.SetStatus("The saved analysis was kept."); return; }
                if (_disposed || !_window.IsVisible) return;
                var outcome = await WithInventoryProgress(restore ? "Restoring named analysis" : "Deleting named analysis", token => captured.ChangeAsync(restore, token));
                Model.SetStatus(outcome.Detail);
            }
            catch (OwnedAnalysisDeletionException error) { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus(error.Outcome.Detail); }
            catch (OperationCanceledException) { Model.SetStatus("Saved-analysis maintenance canceled. Inspect any pending entry before retrying."); }
            catch (Exception error) when (InventoryError(error)) { _actualWork.RecordUnconfirmedClose(error); Model.SetStatus("Saved analysis was not changed: " + AnalysisNames.Display(error.Message, 400)); }
        }
    }
    private sealed partial class IndexedSessionView
    {
        private async Task MaintainSavedAsync(Guid inventory, bool restore, CancellationTokenSource cancel)
        {
            try
            {
                await using var captured = await InventoryProgressAsync("Checking saved analysis", token => AnalysisOwnedStoreAccess.PrepareAsync(_services!, inventory, _actualWork, token), cancel);
                if (_disposed || !await SavedAnalysisMaintenanceDialog.ConfirmAsync(_window._dialogs, captured.Review, _services!.Paths.AnalysisSnapshotsDirectory, restore)) return;
                if (_disposed || !_window.IsVisible) return;
                var outcome = await InventoryProgressAsync(restore ? "Restoring named analysis" : "Deleting named analysis", token => captured.ChangeAsync(restore, token), cancel);
                if (!_disposed) _status.Text = outcome.Detail;
            }
            catch (OwnedAnalysisDeletionException error) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = error.Outcome.Detail; }
            catch (OperationCanceledException) { if (!_disposed) _status.Text = "Saved-analysis maintenance canceled. Inspect any pending entry before retrying."; }
            catch (Exception error) when (Expected(error)) { RememberViewCloseFailure(error); if (!_disposed) _status.Text = "Saved analysis was not changed: " + AnalysisNames.Display(error.Message, 400); }
        }
    }
}
