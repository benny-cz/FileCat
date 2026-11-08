using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.Core.Archives;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<ArchiveTargetReview?> ReviewArchiveTargetAsync(string path, ArchiveWriteProfile profile, ArchiveJobPassword? password, string? expectedSourceSha256 = null)
        => await RunArchivePreparationAsync(path, "Check archive for update",
            "Reading the complete archive and checking whether its contents and metadata can be preserved.",
            ct => Services.Jobs.ReviewArchiveUpdateAsync(path, profile, password, ct, preserveEncryption: true, expectedSourceSha256: expectedSourceSha256).GetAwaiter().GetResult());

    private async Task<T?> RunArchivePreparationAsync<T>(string path, string title, string message, Func<CancellationToken, T> work) where T : class
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Services.Io.StoppingToken);
        var closer = new DialogCloser();
        var task = Services.Io.RunOwned(Core.FileSystem.PathUtil.GetDeviceKey(path), IoPriority.Normal,
            work, stop.Token);
        Task<object?>? dialog = null;
        try
        {
            if (!task.IsCompleted)
            {
                var body = new StackPanel { Spacing = 10, Children = {
                    new TextBlock { Text = "Checking " + System.IO.Path.GetFileName(path), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
                    new ProgressBar { IsIndeterminate = true, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch },
                } };
                dialog = Dialogs.ShowCustomAsync(title, body, [new DialogButton("Cancel", "cancel", IsCancel: true, IsDefault: true)], closer: closer);
                if (await Task.WhenAny(task, dialog) == dialog) stop.Cancel();
            }
            var result = await task; // RunOwned retains this owner through worker cancellation, exit and native close.
            if (stop.IsCancellationRequested || Services.Io.IsStopped) return null;
            return result;
        }
        catch (OperationCanceledException) { return null; }
        finally
        {
            // Dialog construction can fail too. The native call still owns its borrowed secret and lease.
            if (!task.IsCompleted)
            {
                stop.Cancel();
                try { await task; } catch (Exception) { /* Preserve the original presentation error after draining. */ }
            }
            closer.Close(null);
            if (dialog is not null) await dialog;
        }
    }
}
