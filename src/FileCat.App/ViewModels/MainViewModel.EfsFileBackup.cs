using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _reviewingEfsFileBackup;

    private async Task BackUpSelectedEfsKeysAsync()
    {
        if (GetAvailability(CommandIds.EfsFileBackup) is { Enabled: false } unavailable)
        { Notify(unavailable.Reason ?? "EFS key backup is unavailable here."); return; }
        if (ActiveTab is not { Location: not null } tab) return;

        _reviewingEfsFileBackup = true;
        try
        {
            using var initial = new PreparationScope(tab, Services.Io);
            var captured = CaptureStorageItems(tab.Listing, efs: true);
            if (await StorageRejectedSelectionDialog.ShowIfRejectedAsync(Dialogs, captured.Items, captured.Hidden, efs: true)) return;
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(initial.Stop.Token, Services.Io.StoppingToken);
            string device = Services.Providers.For(captured.Items[0].Parent).GetDeviceKey(captured.Items[0].Parent);
            var review = await RunEfsBackupWorkAsync(device, token => Services.Jobs.PrepareEfsFileBackup(captured.Items, token), reading.Token);
            initial.Check();
            initial.Detach();

            var results = new Dictionary<string, StorageEfsFileBackupResult>(StringComparer.Ordinal);
            string? lastKey = null;
            while (!Services.Io.StoppingToken.IsCancellationRequested)
            {
                nint owner = View.TopLevel?.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } hwnd ? hwnd.Handle : 0;
                string? hash = await EfsFileBackupDialog.ChooseKeyAsync(Dialogs, review, captured.Hidden, results, lastKey, owner != 0);
                if (hash is null) return;
                results[hash] = await RunEfsBackupWorkAsync(device, token => Services.Jobs.BackUpEfsFileKey(review, hash, owner, token), Services.Io.StoppingToken);
                lastKey = hash;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { Notify("Could not review EFS key backup. " + error.Message, true); }
        finally { _reviewingEfsFileBackup = false; }
    }

    private async Task<T> RunEfsBackupWorkAsync<T>(string device, Func<CancellationToken, T> work, CancellationToken ct)
    {
        using var reservation = await StorageReviewResources.ReserveNativeAsync(ct);
        return await Services.Io.RunOwned(device, IoPriority.Interactive, token =>
        {
            using var native = reservation.Enter();
            return work(token);
        }, ct);
    }
}
