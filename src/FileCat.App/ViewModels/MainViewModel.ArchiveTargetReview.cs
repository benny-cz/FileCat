using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.Core.Archives;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<ArchiveTargetReview?> ReviewArchiveTargetAsync(string path, ArchiveWriteProfile profile, ArchiveJobPassword? password)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Services.Io.StoppingToken);
        var closer = new DialogCloser();
        var task = Services.Io.RunOwned(Core.FileSystem.PathUtil.GetDeviceKey(path), IoPriority.Normal,
            _ => Services.Jobs.ReviewArchiveUpdateAsync(path, profile, password, stop.Token, preserveEncryption: true).GetAwaiter().GetResult(), stop.Token);
        Task<object?>? dialog = null;
        try
        {
            if (!task.IsCompleted)
            {
                var body = new StackPanel { Spacing = 10, Children = {
                    new TextBlock { Text = "Checking " + System.IO.Path.GetFileName(path), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
                    new TextBlock { Text = "Reading the complete archive and checking whether its contents and metadata can be preserved.", TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
                    new ProgressBar { IsIndeterminate = true, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch },
                } };
                dialog = Dialogs.ShowCustomAsync("Check archive for update", body, [new DialogButton("Cancel", "cancel", IsCancel: true, IsDefault: true)], closer: closer);
                if (await Task.WhenAny(task, dialog) == dialog) stop.Cancel();
            }
            var result = await task; // RunOwned retains this owner through worker cancellation, exit and native close.
            if (stop.IsCancellationRequested || Services.Io.IsStopped) return null;
            return result;
        }
        catch (OperationCanceledException) { return null; }
        finally
        {
            closer.Close(null);
            if (dialog is not null) await dialog;
        }
    }
}
