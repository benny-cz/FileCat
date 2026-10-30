using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private FileClipboard _fileClipboard = new();
    private readonly Dictionary<Job, FileClipboard.Publication> _clipboardMoves = [];
    private readonly Dictionary<Job, (uint Sequence, string[] Paths)> _externalClipboardCopies = [];
    private readonly Dictionary<Job, ClipboardVirtualStaging.Prepared> _virtualClipboardCopies = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TabViewModel, PendingPaste> _pendingPastes = new();

    private sealed class PendingPaste { public long Revision; }

    private async Task PutFilesOnClipboardAsync(bool cut)
    {
        var tab = ActiveTab;
        var top = View.TopLevel;
        var clipboard = View.Clipboard;
        if (tab is null || top is null || clipboard is null) return;
        var selection = tab.Listing.GetSelection();
        if (selection.Count == 0) { ItemSources.Release(selection); return; }
        long revision = _fileClipboard.BeginWrite();
        try
        {
            // The publication must account for every captured item. A refused export leaves the old clipboard intact.
            var paths = new List<string>(selection.Count);
            foreach (var item in selection)
            {
                if (item.FileSystemPath is not { } path)
                {
                    Notify("The selection includes items outside the file system. Nothing was placed on the clipboard; copy or extract the complete selection to a folder first.", true);
                    return;
                }
                paths.Add(path);
            }
            var storageItems = new List<IStorageItem>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                IStorageItem? storage = selection[i].IsContainer
                    ? await top.StorageProvider.TryGetFolderFromPathAsync(paths[i])
                    : await top.StorageProvider.TryGetFileFromPathAsync(paths[i]);
                if (!_fileClipboard.IsLatestWrite(revision)) return;
                if (storage is null)
                {
                    Notify($"“{Formatters.SafeName(paths[i])}” could not be placed on the clipboard. Nothing was copied or cut; the previous clipboard was kept.", true);
                    return;
                }
                storageItems.Add(storage);
            }
            var canMove = await _fileClipboard.PublishAsync(clipboard, revision, storageItems, paths, cut);
            if (canMove is null) return;
            if (cut && canMove == false)
                Notify($"Copied {Formatters.Plural(paths.Count, "item", "items")}. This clipboard cannot preserve Cut safely; use Move (F6) to move files.");
            else
                Notify(cut
                    ? $"Cut {Formatters.Plural(paths.Count, "item", "items")}. Paste into FileCat to move them; moving through another application's clipboard is not supported."
                    : $"Copied {Formatters.Plural(paths.Count, "item", "items")} to the clipboard.");
        }
        finally
        {
            _fileClipboard.EndWrite(revision);
            ItemSources.Release(selection);
        }
    }

    private async Task PasteAsync(TabViewModel? contextTab = null)
    {
        var clipboard = View.Clipboard;
        var tab = contextTab ?? ActiveTab;
        if (clipboard is null || tab?.Location is not { } destination) return;

        // Location is immutable. A tab object is not: navigation, panel changes and delayed clipboard owners
        // must never redirect a transfer to the tab's later location (FCUX-070).
        long navigation = tab.NavigationGeneration;
        var pending = _pendingPastes.GetOrCreateValue(tab);
        long invocation = ++pending.Revision;
        bool IsAlive() => !tab.Listing.IsDisposed && pending.Revision == invocation;
        FileClipboard.Snapshot snapshot;
        try { snapshot = await _fileClipboard.ReadAsync(clipboard, Services.Paths.TempDirectory); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            if (IsAlive()) Notify(ex.Message, true);
            return;
        }
        using var staging = snapshot.Staging;
        if (!IsAlive())
        {
            if (tab.Listing.IsDisposed) Notify("Paste was canceled because its destination tab was closed.");
            return;
        }

        if (snapshot.Files.Count > 0)
        {
            if (!destination.IsFileSystem)
            {
                Notify("Items can be pasted only into a file-system folder.", true);
                return;
            }
            var provider = Services.Providers.For(destination);
            if ((provider.GetCapabilities(destination) & LocationCapabilities.TransferTarget) == 0)
            {
                Notify($"Paste could not start in its original destination: {provider.ExplainUnavailable(destination, LocationCapabilities.TransferTarget)}", true);
                return;
            }
            bool exists = await Services.Io.Run(provider.GetDeviceKey(destination), IoPriority.Interactive, _ => Directory.Exists(destination.Path));
            if (!IsAlive())
            {
                if (tab.Listing.IsDisposed) Notify("Paste was canceled because its destination tab was closed.");
                return;
            }
            if (!exists)
            {
                Notify($"Paste could not start: the original destination “{Formatters.SafeName(Services.Providers.Display(destination))}” is no longer available.", true);
                return;
            }
            if (!await _fileClipboard.IsCurrentAsync(clipboard, snapshot))
            {
                if (IsAlive()) Notify(new FileClipboard.ChangedException().Message, true);
                return;
            }
            if (!IsAlive()) return;
            var move = snapshot.Publication is { Cut: true } publication ? publication : null;
            if (move is not null && !_fileClipboard.TryBeginMove(move, out var reason))
            {
                Notify(reason!);
                return;
            }
            try
            {
                // Ensure completion is observed before submission; even an immediately finished job belongs
                // to the captured destination and can update only its own clipboard publication.
                _ = Operations;
                var job = Services.Jobs.Submit(new JobRequest
                {
                    Kind = move is null ? JobKind.Copy : JobKind.Move,
                    Sources = snapshot.Files,
                    Destination = destination,
                });
                _jobOrigins[job] = (tab, destination);
                if (move is not null) _clipboardMoves.Add(job, move);
                if (staging is not null)
                {
                    _virtualClipboardCopies.Add(job, staging);
                    staging.HandOff();
                }
                else if (snapshot.Publication is null && snapshot.WindowsSequence is { } sequence
                         && OperatingSystem.IsWindows()
                         && snapshot.Files.All(file => file.FileSystemPath is { } path
                             && Path.IsPathFullyQualified(path)))
                    _externalClipboardCopies.Add(job, (sequence,
                        snapshot.Files.Select(file => file.FileSystemPath!).ToArray()));
            }
            catch
            {
                if (move is not null) _fileClipboard.SubmissionFailed(move);
                throw;
            }
            return;
        }

        // A path paste is navigation intent, unlike a captured file transfer. Newer navigation (including
        // still-pending provider preparation), another Paste or tab closure owns the tab thereafter.
        if (tab.NavigationGeneration != navigation || tab.Location != destination) return;
        var text = snapshot.Text;
        if (!string.IsNullOrWhiteSpace(text) && text.Length < 4096 && !text.Contains('\n') && !text.Contains('\r'))
        {
            if (Services.Providers.TryParse(text.Trim(), destination, out var location) && location is not null)
            {
                string? focus = null;
                if (location.IsFileSystem)
                {
                    var provider = Services.Providers.For(location);
                    bool isFile = await Services.Io.Run(provider.GetDeviceKey(location), IoPriority.Interactive, _ => File.Exists(location.Path));
                    if (!IsAlive() || tab.NavigationGeneration != navigation || tab.Location != destination) return;
                    if (isFile)
                    {
                        focus = Path.GetFileName(location.Path);
                        location = Location.FileSystem(Path.GetDirectoryName(location.Path)!);
                    }
                }
                if (!IsAlive() || tab.NavigationGeneration != navigation || tab.Location != destination) return;
                tab.Navigate(location, focus);
                return;
            }
        }
        Notify("The clipboard holds no files or location.");
    }

    private void OnClipboardMoveFinished(Job job)
    {
        if (!_clipboardMoves.Remove(job, out var publication)) return;
        _fileClipboard.CompleteMove(publication, job);
        if (publication.State == FileClipboard.MoveState.ReviewRequired)
            Notify($"Move fully completed {publication.CompletedRoots} of {publication.Paths.Count} cut items. Open Operations (Ctrl+J) to review or retry; select the remaining originals and Cut again to choose a new destination.", true);
    }

    private void OnExternalClipboardCopyFinished(Job job)
    {
        if (!_externalClipboardCopies.Remove(job, out var source)) return;
        if (job.State != JobState.Completed
            || job.CompletedRootIndices.Count != source.Paths.Length) return;
        // An external Cut never grants FileCat authority to remove originals. Tell the shell's
        // exact still-current data object that the completed result was Copy, without claiming
        // an unoptimized Move or invoking delete-on-paste.
        _ = WindowsClipboardFileDrop.TryReportCompletedCopyAsync(source.Sequence, source.Paths);
    }

    private void OnVirtualClipboardCopyFinished(Job job)
    {
        if (_virtualClipboardCopies.Remove(job, out var staging)) staging.Complete();
    }
}
