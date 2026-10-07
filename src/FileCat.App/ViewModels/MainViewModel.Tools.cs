using Avalonia.Controls;
using Avalonia.Layout;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

/// <summary>P3 tools: find files, flat view, compare-and-mark, archives, quick view, attributes.</summary>
public sealed partial class MainViewModel
{
    private async Task<bool> ExecuteToolCommandAsync(string id)
    {
        switch (id)
        {
            case CommandIds.FindFiles: await FindFilesAsync(); return true;
            case CommandIds.RegistryExport: await ExportRegistryAsync(); return true;
            case CommandIds.RegistryImport: await ImportRegistryAsync(); return true;
            case CommandIds.RegistryWritable: await OpenWritableRegistryAsync(); return true;
            case CommandIds.RegistryView: await SwitchRegistryViewAsync(); return true;
            case CommandIds.RegistrySaveData: await SaveRegistryDataAsync(); return true;
            case CommandIds.RegistryLoadData: await LoadRegistryDataAsync(); return true;
            case CommandIds.HexEdit: EditHex(); return true;
            case CommandIds.HexRecovery: await RecoverHexAsync(); return true;
            case CommandIds.FlatView: FlatView(); return true;
            case CommandIds.CompareDirectories: await CompareDirectoriesAsync(); return true;
            case CommandIds.Unpack: await UnpackAsync(); return true;
            case CommandIds.QuickView: ToggleQuickView(); return true;
            case CommandIds.Attributes: await ChangeAttributesAsync(); return true;
            case CommandIds.Pack: await PackAsync(); return true;
            case CommandIds.BulkRename: await BulkRenameAsync(); return true;
            case CommandIds.CreateLink: await CreateLinkAsync(); return true;
            case CommandIds.VerifyChecksums: await VerifyChecksumsAsync(); return true;
            case CommandIds.ApplyCommand: await ApplyCommandAsync(); return true;
            case CommandIds.CompareFiles: await CompareFilesAsync(); return true;
            case CommandIds.FindDeleted: await FindDeletedAsync(); return true;
            case CommandIds.TestArchive: TestArchives(); return true;
            case CommandIds.EditSessions: await ShowEditSessionsAsync(); return true;
        }
        return false;
    }

    // ---- Find files and result sets ----------------------------------------------------------------------

    private async Task FindFilesAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is { Scheme: Schemes.Registry } registryRoot)
        {
            if (registryRoot.Path.Length == 0)
            {
                var roots = new[] { "HKCU", "HKLM", "HKCR", "HKU", "HKCC" };
                var picked = await Dialogs.ChooseAsync(new ChoiceOptions("Search Registry root",
                    roots.Select(r => new ChoiceItem(r)).ToArray()));
                if (picked.Index < 0) return;
                registryRoot = registryRoot.WithPath(roots[picked.Index]);
            }
            var registryResult = await RegistrySearchDialog.ShowAsync(this, registryRoot);
            if (registryResult.Outcome == SearchDialogOutcome.GoTo && registryResult.Item is { } found)
                tab.Navigate(found.Parent, found.Name);
            else if (registryResult.Outcome == SearchDialogOutcome.ShowInPanel && registryResult.Set is { } set)
                OpenResultSet(set, registryResult.Running);
            View.FocusActivePanel();
            return;
        }
        var root = tab?.Location is { IsFileSystem: true } l ? l.Path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // In a result set, Find searches within its items and the matches form a narrower set (plan §11).
        var within = tab?.Location is { Scheme: Schemes.ResultSet } rl ? Services.ResultSets.Get(rl) : null;
        // A window of its own, so the panels stay free while it searches (plan §11, Salamander's Find).
        FindWindow.Open(this, root, within);
    }

    internal void OpenResultSet(ResultSet set, CancellationTokenSource? running)
    {
        var panel = Workspace.ActivePanel;
        if (panel is null) return;
        var tab = panel.OpenTab(ResultSetProvider.LocationOf(set));
        // While the search still runs, the tab follows new results at a bounded rate.
        var last = DateTime.MinValue;
        bool pending = false;
        bool open = true;
        Action changed = () => Services.Ui.Post(() =>
        {
            if (!open || pending || tab.Location?.Session != set.Id) return;
            var wait = TimeSpan.FromMilliseconds(600) - (DateTime.UtcNow - last);
            pending = true;
            _ = Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero).ContinueWith(_ => Services.Ui.Post(() =>
            {
                pending = false;
                last = DateTime.UtcNow;
                if (open && tab.Location?.Session == set.Id) tab.Refresh();
            }));
        });
        set.Changed += changed;
        tab.Closed += () =>
        {
            open = false;
            set.Changed -= changed;
        };
        tab.Banner = set.Provenance + (running is null ? "" : " — still searching (Esc in the Find window stops it)");
        if (running is not null) _ = WatchSearchCompletionAsync(set, tab, running, () => open);
    }

    private async Task WatchSearchCompletionAsync(ResultSet set, TabViewModel tab, CancellationTokenSource running, Func<bool> isOpen)
    {
        while (isOpen() && !running.IsCancellationRequested && !set.IsComplete && set.Issues.Count == 0) await Task.Delay(500);
        if (!isOpen()) return;
        tab.Banner = set.Provenance + (set.IsComplete ? " — complete" : " — stopped before completion") + (set.Issues.Count > 0 ? $"; {set.Issues.Count} location(s) could not be searched" : "");
    }

    /// <summary>Flat view (Ctrl+B): an explicit, cancellable recursive result set of this folder (plan §11).</summary>
    private void FlatView()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { IsFileSystem: true } loc)
        {
            Notify("Flat view works for file-system folders.");
            return;
        }
        var folderName = Path.GetFileName(loc.Path.TrimEnd('\\', '/'));
        var set = Services.ResultSets.Create($"Flat: {(folderName.Length > 0 ? folderName : loc.Path)}", $"All files below {loc.Path}");
        var session = new SearchSession(new SearchQuery { Roots = [loc.Path], IncludeDirectories = false, IncludeHidden = Services.Settings.ShowHidden }, set);
        var cts = new CancellationTokenSource();
        _flatViews[set.Id] = cts;
        _ = Task.Run(() =>
        {
            try { session.Run(cts.Token); }
            finally
            {
                Services.Ui.Post(() =>
                {
                    _flatViews.Remove(set.Id);
                    cts.Dispose();
                });
            }
        });
        OpenResultSet(set, cts);
    }

    private readonly Dictionary<string, CancellationTokenSource> _flatViews = new();

    // ---- Compare files ----------------------------------------------------------------------------------------

    /// <summary>
    /// Compare files (plan §16.2, Ctrl+I). Marked files decide: two in the active panel, or one there and one in the
    /// other panel. With none marked, the file under the cursor is compared with the file of the same name in the other
    /// panel's folder, or, when that folder has none, with the file under the cursor there. Contents open off the UI
    /// thread (a server may ask for a password); the window owns them from then on.
    /// </summary>
    private async Task CompareFilesAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is null) return;
        var marked = MarkedFiles(tab);
        if (marked.Count > 2)
        {
            Notify("More than two files are marked: mark the two to compare, or one here and one in the other panel.", true);
            return;
        }
        if (marked.Count == 2)
        {
            await OpenFileComparisonAsync(marked[0], marked[1]);
            return;
        }
        var mine = marked.Count == 1 ? marked[0] : FocusedFile(tab);
        if (mine is null)
        {
            Notify(tab.Listing.TryGetFocused(out var focused) && focused.Kind != EntryKind.Parent && focused.IsContainer
                ? "Compare files compares files: focus a file, or mark two. Compare directories (Ctrl+F10) compares folders."
                : "Focus a file to compare it with the file of the same name in the other panel, or mark two files.", true);
            return;
        }
        var other = Workspace.ActiveTarget?.ActiveTab;
        if (other?.Location is null || ReferenceEquals(other, tab))
        {
            Notify("Mark two files to compare them, or show the file to compare with in a second panel.", true);
            return;
        }
        var theirs = MarkedFiles(other);
        if (theirs.Count > 1)
        {
            Notify($"The other panel has several marked files: mark one there, or none to compare \"{mine.Name}\" with its namesake.", true);
            return;
        }
        var match = theirs.Count == 1 ? theirs[0] : Namesake(other, mine.Name);
        if (match is null || match.Equals(mine)) match = FocusedFile(other);
        if (match is null || match.Equals(mine))
        {
            Notify(other.Listing.State == ListingState.Loading
                ? "The other panel is still reading its folder: try again when it is complete."
                : other.Location.Equals(tab.Location)
                    ? "Both panels show the same folder: mark the two files to compare."
                    : $"There is no \"{mine.Name}\" in {other.DisplayPath}. Focus the file to compare it with there, or mark one file in each panel.", true);
            return;
        }
        await OpenFileComparisonAsync(mine, match);
    }

    /// <summary>The marked files of a tab (folders aside), at most three: a comparison takes two.</summary>
    private static List<ItemRef> MarkedFiles(TabViewModel tab)
    {
        if (!tab.Listing.HasMarks) return [];
        var selection = tab.Listing.GetSelection(includeHiddenMarks: false);
        try { return selection.Where(i => !i.IsContainer).Take(3).ToList(); }
        finally { ItemSources.Release(selection); }
    }

    private static ItemRef? FocusedFile(TabViewModel tab) =>
        tab.Listing.TryGetFocused(out var f) && !f.IsContainer ? tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex) : null;

    /// <summary>The file named <paramref name="name"/> in a tab's folder: exactly, or else the one name that differs only in letter case.</summary>
    private static ItemRef? Namesake(TabViewModel tab, string name)
    {
        var listing = tab.Listing;
        int index = listing.FindStoreIndex(name);
        if (index < 0) index = listing.FindStoreIndexIgnoringCase(name);
        return index >= 0 && !listing.Store[index].IsContainer ? listing.GetItemRef(index) : null;
    }

    /// <summary>Opens both contents on their device workers; F5 uses the same admission and ownership boundaries.</summary>
    private async Task OpenFileComparisonAsync(ItemRef a, ItemRef b)
    {
        string Display(ItemRef i) => i.FileSystemPath ?? Services.Providers.Display(i.Parent).TrimEnd('/', '\\') + "/" + i.Name;
        async Task<IContentSource> OpenItemAsync(ItemRef item)
        {
            var provider = Services.Providers.For(item.Parent);
            var source = await Services.Io.Run(provider.GetDeviceKey(item.Parent), Core.Threading.IoPriority.Interactive,
                _ => provider.OpenContent(item));
            if (Services.Io.IsStopped)
            {
                source?.Dispose();
                throw new OperationCanceledException("Comparison admission stopped.");
            }
            return source ?? throw new InvalidDataException($"\"{item.Name}\" has no content to compare.");
        }
        async Task<(IContentSource Left, IContentSource Right)> OpenBothAsync()
        {
            var left = await OpenItemAsync(a);
            try { return (left, await OpenItemAsync(b)); }
            catch
            {
                left.Dispose();
                throw;
            }
        }
        try
        {
            var (left, right) = await OpenBothAsync();
            if (Services.Io.IsStopped)
            {
                left.Dispose();
                right.Dispose();
                throw new OperationCanceledException("Comparison admission stopped.");
            }
            Views.CompareWindow.Open(Display(a), left, Display(b), right, reopenAsync: OpenBothAsync);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException or NotSupportedException)
        {
            Notify("Cannot compare: " + (ex is OperationCanceledException ? "canceled." : ex.Message), true);
        }
    }

    // ---- Compare and mark ----------------------------------------------------------------------------------

    private async Task CompareDirectoriesAsync()
    {
        var left = Workspace.ActivePanel?.ActiveTab;
        var right = Workspace.ActiveTarget?.ActiveTab;
        if (left?.Location is null || right?.Location is null)
        {
            Notify("Compare needs a target panel with a folder.", true);
            return;
        }
        if (left.Listing.State != ListingState.Complete || right.Listing.State != ListingState.Complete ||
            left.Listing.IsRefreshing || right.Listing.IsRefreshing || left.Listing.Issues.Count != 0 || right.Listing.Issues.Count != 0)
        {
            Notify("Compare needs two complete listings without read errors. Reread both folders first.");
            return;
        }
        int leftGeneration = left.Listing.Generation, rightGeneration = right.Listing.Generation;
        using var stop = new CancellationTokenSource();
        bool Current() => !left.Listing.IsDisposed && !right.Listing.IsDisposed &&
            left.Listing.Generation == leftGeneration && right.Listing.Generation == rightGeneration && !Services.Io.IsStopped;
        void Changed(object? sender, ListingChange change) { if (!Current()) stop.Cancel(); }
        void Closed() => stop.Cancel();
        left.Listing.Changed += Changed; right.Listing.Changed += Changed;
        left.Closed += Closed; right.Closed += Closed;
        try
        {
            var size = new CheckBox { Content = "Size", IsChecked = true };
            var time = new CheckBox { Content = "Modification time", IsChecked = true };
            var content = new CheckBox { Content = "Content (reads both files; slower)" };
            var recursive = new CheckBox { Content = "Include subfolders: a preview of every difference below, marking nothing" };
            var body = new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = $"Compare {left.DisplayPath}\nwith {right.DisplayPath}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { size, time, content } },
                recursive,
                new TextBlock { Text = "Names always match; folders compare by presence. Without subfolders, items that differ or exist on one side only are marked in both panels.", Classes = { "muted", "small" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            } };
            var go = await Dialogs.ShowCustomAsync("Compare directories", body, [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Compare", "ok", IsDefault: true)]);
            if (go as string != "ok" || !Current() || stop.IsCancellationRequested) return;
            var criteria = (size.IsChecked == true ? CompareCriteria.Size : 0) | (time.IsChecked == true ? CompareCriteria.Time : 0) | (content.IsChecked == true ? CompareCriteria.Content : 0);
            var fs = Services.Platform.FileOperations;
            var leftEntries = Snapshot(left.Listing);
            var rightEntries = Snapshot(right.Listing);
            var lp = Services.Providers.For(left.Location);
            var rp = Services.Providers.For(right.Location);
            var lloc = left.Location;
            var rloc = right.Location;
            // Timestamp tolerance at the coarser file system's known precision (FAT/SMB 2 s).
            var tol = TimeSpan.FromSeconds(2);
            if (lloc.IsFileSystem && rloc.IsFileSystem)
            {
                var a = await Services.Io.Run(lp.GetDeviceKey(lloc), Core.Threading.IoPriority.Normal, _ =>
                {
                    stop.Token.ThrowIfCancellationRequested();
                    return fs.GetVolumeInfo(lloc.Path).TimestampPrecision;
                });
                if (!Current() || stop.IsCancellationRequested) return;
                var b = await Services.Io.Run(rp.GetDeviceKey(rloc), Core.Threading.IoPriority.Normal, _ =>
                {
                    stop.Token.ThrowIfCancellationRequested();
                    return fs.GetVolumeInfo(rloc.Path).TimestampPrecision;
                });
                if (!Current() || stop.IsCancellationRequested) return;
                tol = a > b ? a : b;
                if (tol < TimeSpan.FromSeconds(1)) tol = TimeSpan.FromSeconds(1);
            }
            if (recursive.IsChecked == true)
            {
                CompareTrees(left, right, criteria, tol);
                return;
            }
            if ((criteria & CompareCriteria.Content) != 0) Notify("Comparing contents…");
            var io = new ComparisonIo(Services.Io, Services.Providers);
            var result = await Task.Run(() => DirectoryCompare.CompareAsync(leftEntries, rightEntries, criteria, tol, async (ln, rn) =>
            {
                try
                {
                    return await io.ContentEqualAsync(lp.GetItemRef(lloc, leftEntries.First(e => e.Name == ln)),
                        rp.GetItemRef(rloc, rightEntries.First(e => e.Name == rn)), stop.Token);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException) { return null; }
            }, stop.Token, caseInsensitiveNames: OperatingSystem.IsWindows() && lloc.IsFileSystem && rloc.IsFileSystem));
            if (!Current() || stop.IsCancellationRequested) return;
            left.Listing.UnmarkEverything();
            right.Listing.UnmarkEverything();
            left.Listing.MarkNames(result.LeftMarks, true);
            right.Listing.MarkNames(result.RightMarks, true);
            var label = result.Describe(criteria, (criteria & CompareCriteria.Time) != 0 ? tol : TimeSpan.Zero);
            left.ComparisonLabel = label;
            right.ComparisonLabel = label;
            Notify(result.LeftMarks.Count + result.RightMarks.Count == 0 ? "The folders match." : label);
        }
        catch (OperationCanceledException) { }
        finally
        {
            left.Listing.Changed -= Changed; right.Listing.Changed -= Changed;
            left.Closed -= Closed; right.Closed -= Closed;
        }
    }

    /// <summary>
    /// Recursive comparison (plan §16.2): a preview window that changes nothing; each side's differences open as a result
    /// set in that side's panel, for the usual commands.
    /// </summary>
    private void CompareTrees(TabViewModel left, TabViewModel right, CompareCriteria criteria, TimeSpan tolerance)
    {
        var lloc = left.Location!;
        var rloc = right.Location!;
        string leftName = left.DisplayPath, rightName = right.DisplayPath;
        var leftPanel = left.Panel;
        var rightPanel = right.Panel;
        var how = new List<string> { "name" };
        if ((criteria & CompareCriteria.Size) != 0) how.Add("size");
        if ((criteria & CompareCriteria.Time) != 0) how.Add($"time ±{tolerance.TotalSeconds:0.#} s");
        if ((criteria & CompareCriteria.Content) != 0) how.Add("content");
        bool caseInsensitive = OperatingSystem.IsWindows() && lloc.IsFileSystem && rloc.IsFileSystem;
        var lcaps = Services.Providers.For(lloc).GetCapabilities(lloc);
        var rcaps = Services.Providers.For(rloc).GetCapabilities(rloc);
        static bool Writable(Location l, LocationCapabilities c) => l.IsFileSystem && (c & LocationCapabilities.TransferTarget) != 0;
        // Folders inside each other (also through a link) are compared, but not offered for synchronizing (I94).
        string? overlap = lloc.IsFileSystem && rloc.IsFileSystem ? SyncPlanner.Overlap(lloc.Path, rloc.Path, Services.Platform.FileOperations.GetFinalPath) : null;
        var sync = overlap is not null ? null : new Views.SyncContext(leftName, rightName, Writable(lloc, lcaps), Writable(rloc, rcaps),
            (lcaps & LocationCapabilities.Recycle) != 0, (rcaps & LocationCapabilities.Recycle) != 0, OperatingSystem.IsWindows(),
            (items, sourceIsLeft, permanent) => StartSync(items, sourceIsLeft, permanent, lloc, rloc));
        Views.DirectoryDiffWindow.StartAsync(leftName, rightName,
            $"Compared by {string.Join(", ", how)}; folders on one side are listed once; links to folders are not followed." +
            (overlap is null ? "" : $" Synchronize is not offered. {overlap}"),
            (progress, ct) => Task.Run(() => TreeCompare.CompareAsync(Services.Providers, lloc, rloc, criteria, tolerance, caseInsensitive, ct, progress,
                new ComparisonIo(Services.Io, Services.Providers))),
            (entries, leftSide) =>
            {
                var set = Services.ResultSets.Create($"{(leftSide ? "Left" : "Right")} differences: {Path.GetFileName((leftSide ? leftName : rightName).TrimEnd('\\', '/'))}",
                    $"Items that differ between {leftName} and {rightName}");
                foreach (var e in entries)
                {
                    var data = leftSide ? e.Left : e.Right;
                    var folder = leftSide ? e.LeftFolder : e.RightFolder;
                    if (data is not { } d || folder is null) continue;
                    int slash = e.RelativePath.LastIndexOf('/');
                    set.Add(Services.Providers.For(folder).GetItemRef(folder, d), slash < 0 ? "" : e.RelativePath[..slash]);
                }
                set.IsComplete = true;
                var panel = leftSide ? leftPanel : rightPanel;
                panel.OpenTab(ResultSetProvider.LocationOf(set));
                Workspace.Activate(panel);
            }, sync,
            entry =>
            {
                if (entry is not { Left: { } l, Right: { } r, LeftFolder: { } lf, RightFolder: { } rf }) return;
                _ = OpenFileComparisonAsync(Services.Providers.For(lf).GetItemRef(lf, l), Services.Providers.For(rf).GetItemRef(rf, r));
            });
    }

    /// <summary>Starts a previewed one-way synchronization as ordinary jobs, run one after another.</summary>
    private bool StartSync(IReadOnlyList<SyncItem> items, bool sourceIsLeft, bool deletePermanently, Location left, Location right)
    {
        var target = sourceIsLeft ? right : left;
        var requests = SyncPlanner.BuildRequests(items, sourceIsLeft, target, Services.Providers, deletePermanently);
        if (requests.Count == 0)
        {
            Notify("Nothing to synchronize.");
            return false;
        }
        foreach (var request in requests) Services.Jobs.Submit(request);
        Notify($"Synchronizing {Services.Providers.Display(sourceIsLeft ? left : right)} → {Services.Providers.Display(target)}: {SyncPlanner.Describe(items)}. " +
               "Ctrl+J shows the operations.");
        return true;
    }

    private static List<EntryData> Snapshot(ListingModel listing)
    {
        var list = new List<EntryData>(listing.TotalCount);
        for (int i = 0; i < listing.Store.Count; i++) list.Add(listing.Store[i]);
        return list;
    }

    // ---- Archives ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Ctrl+PgDn on a file that is not a known archive: open it by its signature, as ZIP (e.g. .docx, .apk) or as one of
    /// the read-only formats (a TAR, 7z, RAR, compressed file, or disc image under another name).
    /// </summary>
    internal bool TryOpenAsArchive(TabViewModel tab, in EntryData e)
    {
        if (e.IsContainer || tab.Location is null) return false;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        if (item.FileSystemPath is not { } path) return false;
        FileCat.Archives.ArchiveKind? other;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> sig = stackalloc byte[4];
            if (fs.Read(sig) >= 4 && sig[0] == 'P' && sig[1] == 'K')
            {
                tab.Navigate(ZipProvider.ForFile(path));
                return true;
            }
            other = FileCat.Archives.ArchiveFormats.BySignature(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        if (other is null) return false;
        tab.Navigate(FileCat.Archives.ArchiveProvider.ForFile(path, other));
        return true;
    }

    /// <summary>Unpack (Alt+F6/Alt+F9): extracts whole archives into the target panel's folder.</summary>
    private async Task UnpackAsync()
    {
        var tab = ActiveTab;
        var sel = tab?.Listing.GetSelection();
        if (tab is null || sel is null || sel.Count == 0) return;
        var archives = sel.Where(s => s.FileSystemPath is { } p && !s.IsContainer).Select(s => s.FileSystemPath!).ToList();
        if (archives.Count == 0)
        {
            Notify("Focus or mark archives to unpack.");
            return;
        }
        var target = Workspace.ActiveTarget?.ActiveTab?.Location is { IsFileSystem: true } t ? t.Path : Path.GetDirectoryName(archives[0])!;
        var r = await Dialogs.PromptAsync(new PromptOptions("Unpack", $"Unpack {Formatters.Plural(archives.Count, "archive", "archives")} to:")
        {
            Text = target,
            CheckboxText = "Separate folder for each archive (named after it)",
            CheckboxValue = archives.Count > 1,
            ConfirmText = "Unpack",
        });
        if (r is null) return;
        foreach (var archive in archives)
        {
            bool other = Services.Archives.IsContainer(Path.GetFileName(archive));
            var root = other ? FileCat.Archives.ArchiveProvider.ForFile(archive) : ZipProvider.ForFile(archive);
            FileCat.Core.Resources.ResourceProvider provider = other ? Services.Archives : Services.Zip;
            List<ItemRef> members;
            try
            {
                members = await Task.Run(() =>
                {
                    var list = new List<EntryData>();
                    provider.EnumerateAsync(root, new CollectSink(list), CancellationToken.None).GetAwaiter().GetResult();
                    return list.Select(e => provider.GetItemRef(root, e)).ToList();
                });
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Notify($"\"{Path.GetFileName(archive)}\" cannot be read as {(other ? "an archive" : "a ZIP archive")}: {ex.Message}", true);
                continue;
            }
            var dest = r.Checked ? Path.Combine(r.Text, Path.GetFileNameWithoutExtension(archive)) : r.Text;
            var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.Extract, Sources = members, Destination = Location.FileSystem(dest), Description = $"Unpack \"{Path.GetFileName(archive)}\" to {dest}" });
            Track(job, tab);
        }
    }

    private sealed class CollectSink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }

    // ---- Quick view --------------------------------------------------------------------------------------------

    private void ToggleQuickView()
    {
        var source = Workspace.ActivePanel;
        var target = Workspace.ActiveTarget;
        if (source is null || target is null)
        {
            Notify("Quick view shows the focused item in the target panel; there is no target panel.", true);
            return;
        }
        target.QuickViewSource = target.QuickViewSource is null ? source : null;
    }

    // ---- Attributes ------------------------------------------------------------------------------------------

    private async Task ChangeAttributesAsync()
    {
        var tab = ActiveTab;
        var sel = tab?.Listing.GetSelection();
        if (tab is null || sel is null || sel.Count == 0) return;
        if (sel.Any(s => s.FileSystemPath is null))
        {
            Notify("Attributes can be changed for file-system items.", true);
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            await ChangeUnixAttributesAsync(tab, sel);
            return;
        }
        var infos = sel.Select(s => Services.Platform.FileOperations.TryGetInfo(s.FileSystemPath!)).Where(i => i is not null).Cast<FileSystemItemInfo>().ToList();
        bool? State(FileAttributes a) => infos.All(i => (i.Attributes & a) != 0) ? true : infos.All(i => (i.Attributes & a) == 0) ? false : null;
        CheckBox Box(string label, FileAttributes a) => new() { Content = label, IsThreeState = true, IsChecked = State(a), Tag = a };
        var boxes = new[] { Box("Read-only", FileAttributes.ReadOnly), Box("Hidden", FileAttributes.Hidden), Box("System", FileAttributes.System), Box("Archive", FileAttributes.Archive) };
        var modified = new TextBox { Text = infos.Count == 1 ? infos[0].ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty, PlaceholderText = "unchanged (yyyy-MM-dd HH:mm:ss)" };
        var created = new TextBox { Text = infos.Count == 1 ? infos[0].CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty, PlaceholderText = "unchanged" };
        Avalonia.Automation.AutomationProperties.SetName(modified, "Modified");
        Avalonia.Automation.AutomationProperties.SetName(created, "Created");
        var recursive = new CheckBox { Content = "Also apply to everything inside the marked folders (links are not followed)", IsVisible = sel.Any(s => s.IsContainer) };
        var body = new StackPanel { Spacing = 6, MinWidth = 480 };
        body.Children.Add(new TextBlock { Text = sel.Count == 1 ? sel[0].Name : Formatters.Plural(sel.Count, "item", "items"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { boxes[0], boxes[1], boxes[2], boxes[3] } });
        body.Children.Add(new TextBlock { Text = "A filled box sets, an empty box clears, a mixed box leaves the attribute unchanged.", Classes = { "muted", "small" } });
        body.Children.Add(new TextBlock { Text = "Modified:" });
        body.Children.Add(modified);
        body.Children.Add(new TextBlock { Text = "Created:" });
        body.Children.Add(created);
        body.Children.Add(recursive);
        var r = await Dialogs.ShowCustomAsync("Attributes and times", body, [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Apply", "ok", IsDefault: true)]);
        if (r as string != "ok") return;
        FileAttributes set = 0, clear = 0;
        foreach (var b in boxes)
        {
            var a = (FileAttributes)b.Tag!;
            if (b.IsChecked == true && State(a) != true) set |= a;
            else if (b.IsChecked == false && State(a) != false) clear |= a;
        }
        DateTime? Parse(TextBox t, DateTime? original) =>
            DateTime.TryParse(t.Text, out var d) && (original is null || Math.Abs((d.ToUniversalTime() - original.Value).TotalSeconds) >= 1) ? d.ToUniversalTime() : null;
        var mod = Parse(modified, infos.Count == 1 ? infos[0].ModifiedUtc : null);
        var cre = Parse(created, infos.Count == 1 ? infos[0].CreatedUtc : null);
        if (set == 0 && clear == 0 && mod is null && cre is null)
        {
            Notify("Nothing to change.");
            return;
        }
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Attributes,
            Sources = sel,
            Attributes = new AttributeChangeSet(set, clear, mod, cre, recursive.IsChecked == true),
        });
        Track(job, tab);
    }
}
