using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.Core.Archives;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <param name="expectedSourceSha256">The revision the change must apply to: a SHA-256, or a listing's revision token, which
    /// is resolved here (the listing's archive is read then, through its own handle) for the listing <paramref name="listing"/>.</param>
    private async Task<ArchiveTargetReview?> ReviewArchiveTargetAsync(string path, ArchiveWriteProfile profile, ArchiveJobPassword? password,
        string? expectedSourceSha256 = null, string title = "Check archive for update",
        string message = "Reading the complete archive and checking whether its contents and metadata can be preserved.", Location? listing = null)
        => await RunArchivePreparationAsync(path, title, message, ct =>
        {
            string? expected = ResolveListingRevision(listing, expectedSourceSha256, ct);
            return Services.Jobs.ReviewArchiveUpdateAsync(path, profile, password, ct, preserveEncryption: true, expectedSourceSha256: expected).GetAwaiter().GetResult();
        });

    /// <summary>
    /// A listing row's revision for a native review: a revision token is resolved by the listing's provider to the SHA-256
    /// of the archive that listing showed (review finding of 12 November 2026); any other value already is that SHA-256.
    /// </summary>
    private string? ResolveListingRevision(Location? listing, string? revision, CancellationToken ct) =>
        !ArchiveListingRevision.IsToken(revision) ? revision
        : listing?.Scheme == Core.Resources.Schemes.Zip ? Services.Zip.ResolveListingRevision(revision!, ct)
        : listing?.Scheme == Core.Resources.Schemes.Archive ? Services.Archives.ResolveListingRevision(revision!, ct)
        : throw new IOException(ArchiveListingRevision.Closed);

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
                string checking = "Checking " + System.IO.Path.GetFileName(path);
                // Plan §11.8: the Enter that asked for this check repeats while held, and with Cancel as the default it
                // cancelled the check at once. No button is the default; focus starts on the named progress, which is what
                // a screen reader then reads, and Escape or Cancel (Tab) ends the check.
                var progress = new ProgressBar { IsIndeterminate = true, MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Stretch, Focusable = true };
                Avalonia.Automation.AutomationProperties.SetName(progress, checking);
                var body = new StackPanel { Spacing = 10, Children = {
                    new TextBlock { Text = checking, TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
                    progress,
                    new TextBlock { Text = "Operations stay available meanwhile (Ctrl+J).", TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Classes = { "muted" } },
                } };
                // Plan §11.5: the operation center stays usable while this format scan is pending.
                dialog = Dialogs.ShowCustomAsync(title, body, [new DialogButton("Cancel", "cancel", IsCancel: true)], initialFocus: progress, closer: closer,
                    behavior: new DialogBehavior { KeepOperationsUsable = true });
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
