using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    internal async Task SaveStorageCallsAsync(string leaf, StorageJournalPrefix prefix, CancellationToken cancellationToken)
    {
        if (!EditSessionsActive || View.TopLevel is not { } top || !Services.Jobs.SupportsStorageHistory) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Services.Io.StoppingToken);
        try
        {
            using var picked = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save all recorded calls from this snapshot — choose a new file",
                SuggestedFileName = $"storage-{prefix.JobId:N}-sequence-{prefix.Sequence}.fcj", DefaultExtension = "fcj",
                ShowOverwritePrompt = false, FileTypeChoices = [new FilePickerFileType("FileCat storage evidence") { Patterns = ["*.fcj"] }],
            });
            stop.Token.ThrowIfCancellationRequested();
            if (picked is null) return;
            if (picked.TryGetLocalPath() is not { } destination) { Notify("Choose a local file for the storage evidence export."); return; }
            var result = await ExportStorageCallsAsync(leaf, prefix, destination, stop.Token);
            if (EditSessionsActive) Notify("Saved all recorded calls through sequence " + prefix.Sequence + " to " + Formatters.SafeName(result.Destination) + "." +
                (result.Warning is null ? "" : " " + Formatters.SafeName(result.Warning)));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        { if (EditSessionsActive) Notify("Storage call export did not complete: " + Formatters.SafeName(error.Message), true); }
    }

    internal async Task<StorageExportResult> ExportStorageCallsAsync(string leaf, StorageJournalPrefix prefix, string destination, CancellationToken ct)
    {
        if (!EditSessionsActive || Services.Io.IsStopped) throw new IOException("This FileCat session has closed.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, Services.Io.StoppingToken);
        using var interactive = await StorageAccounting.EnterInteractiveAsync(stop.Token);
        return await Services.Io.RunOwned(PathUtil.GetDeviceKey(Services.Paths.JournalDirectory), IoPriority.Interactive,
            token => Services.Jobs.ExportStorageCalls(leaf, prefix, destination, token), stop.Token);
    }
}
