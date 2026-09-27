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
            case CommandIds.HexEdit: EditHexAsync(); return true;
            case CommandIds.HexRecovery: await RecoverHexAsync(); return true;
            case CommandIds.FlatView: FlatView(); return true;
            case CommandIds.CompareDirectories: await CompareDirectoriesAsync(); return true;
            case CommandIds.Unpack: await UnpackAsync(); return true;
            case CommandIds.QuickView: ToggleQuickView(); return true;
            case CommandIds.Attributes: await ChangeAttributesAsync(); return true;
            case CommandIds.Pack: Notify("Creating and updating archives arrives with the archive-editing slice (P5); extracting and browsing ZIP works now."); return true;
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
        var r = await SearchDialog.ShowAsync(this, root, within);
        switch (r.Outcome)
        {
            case SearchDialogOutcome.GoTo when r.Item is { } item:
                tab?.Navigate(item.Parent, item.Name);
                break;
            case SearchDialogOutcome.ShowInPanel when r.Set is { } set:
                OpenResultSet(set, r.Running);
                break;
        }
        View.FocusActivePanel();
    }

    private void OpenResultSet(ResultSet set, CancellationTokenSource? running)
    {
        var panel = Workspace.ActivePanel;
        if (panel is null) return;
        var tab = panel.OpenTab(ResultSetProvider.LocationOf(set));
        // While the search still runs, the tab follows new results at a bounded rate.
        var last = DateTime.MinValue;
        bool pending = false;
        set.Changed += () => Services.Ui.Post(() =>
        {
            if (pending || tab.Location?.Session != set.Id) return;
            var wait = TimeSpan.FromMilliseconds(600) - (DateTime.UtcNow - last);
            pending = true;
            _ = Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero).ContinueWith(_ => Services.Ui.Post(() =>
            {
                pending = false;
                last = DateTime.UtcNow;
                if (tab.Location?.Session == set.Id) tab.Refresh();
            }));
        });
        tab.Banner = set.Provenance + (running is null ? "" : " — still searching (Esc in the Find dialog stops it)");
        if (running is not null) _ = WatchSearchCompletionAsync(set, tab, running);
    }

    private async Task WatchSearchCompletionAsync(ResultSet set, TabViewModel tab, CancellationTokenSource running)
    {
        while (!running.IsCancellationRequested && !set.IsComplete && set.Issues.Count == 0) await Task.Delay(500);
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
        _ = Task.Run(() => session.Run(cts.Token));
        OpenResultSet(set, cts);
        _flatViews[set.Id] = cts;
    }

    private readonly Dictionary<string, CancellationTokenSource> _flatViews = new();

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
        if (left.Listing.State == ListingState.Loading || right.Listing.State == ListingState.Loading)
        {
            Notify("Wait until both listings are complete.");
            return;
        }
        var size = new CheckBox { Content = "Size", IsChecked = true };
        var time = new CheckBox { Content = "Modification time", IsChecked = true };
        var content = new CheckBox { Content = "Content (reads both files; slower)" };
        var body = new StackPanel { Spacing = 6, Children =
        {
            new TextBlock { Text = $"Compare {left.DisplayPath}\nwith {right.DisplayPath}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { size, time, content } },
            new TextBlock { Text = "Names always match; folders compare by presence. Items that differ or exist on one side only are marked in both panels.", Classes = { "muted", "small" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        } };
        var go = await Dialogs.ShowCustomAsync("Compare directories", body, [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Compare", "ok", IsDefault: true)]);
        if (go as string != "ok") return;
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
            var a = fs.GetVolumeInfo(lloc.Path).TimestampPrecision;
            var b = fs.GetVolumeInfo(rloc.Path).TimestampPrecision;
            tol = a > b ? a : b;
            if (tol < TimeSpan.FromSeconds(1)) tol = TimeSpan.FromSeconds(1);
        }
        if ((criteria & CompareCriteria.Content) != 0) Notify("Comparing contents…");
        var result = await Task.Run(() => DirectoryCompare.Compare(leftEntries, rightEntries, criteria, tol, (ln, rn) =>
        {
            using var sa = lp.OpenContent(lp.GetItemRef(lloc, leftEntries.First(e => e.Name == ln)));
            using var sb = rp.OpenContent(rp.GetItemRef(rloc, rightEntries.First(e => e.Name == rn)));
            return sa is null || sb is null ? null : DirectoryCompare.ContentEqual(sa, sb, CancellationToken.None);
        }, CancellationToken.None, caseInsensitiveNames: OperatingSystem.IsWindows()));
        left.Listing.UnmarkEverything();
        right.Listing.UnmarkEverything();
        left.Listing.MarkNames(result.LeftMarks, true);
        right.Listing.MarkNames(result.RightMarks, true);
        var label = result.Describe(criteria, (criteria & CompareCriteria.Time) != 0 ? tol : TimeSpan.Zero);
        left.ComparisonLabel = label;
        right.ComparisonLabel = label;
        Notify(result.LeftMarks.Count + result.RightMarks.Count == 0 ? "The folders match." : label);
    }

    private static List<EntryData> Snapshot(ListingModel listing)
    {
        var list = new List<EntryData>(listing.TotalCount);
        for (int i = 0; i < listing.Store.Count; i++) list.Add(listing.Store[i]);
        return list;
    }

    // ---- Archives ----------------------------------------------------------------------------------------------

    /// <summary>Ctrl+PgDn on a file that is not a known archive: try it as ZIP (e.g. .docx, .apk) explicitly.</summary>
    internal bool TryOpenAsArchive(TabViewModel tab, in EntryData e)
    {
        if (e.IsContainer || tab.Location is null) return false;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        if (item.FileSystemPath is not { } path) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> sig = stackalloc byte[4];
            if (fs.Read(sig) < 4 || sig[0] != 'P' || sig[1] != 'K') return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        tab.Navigate(ZipProvider.ForFile(path));
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
            Notify("Focus or mark ZIP archives to unpack.");
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
            var root = ZipProvider.ForFile(archive);
            var provider = Services.Providers.Get(Schemes.Zip);
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
                Notify($"\"{Path.GetFileName(archive)}\" cannot be read as a ZIP archive: {ex.Message}", true);
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
        var infos = sel.Select(s => Services.Platform.FileOperations.TryGetInfo(s.FileSystemPath!)).Where(i => i is not null).Cast<FileSystemItemInfo>().ToList();
        bool? State(FileAttributes a) => infos.All(i => (i.Attributes & a) != 0) ? true : infos.All(i => (i.Attributes & a) == 0) ? false : null;
        CheckBox Box(string label, FileAttributes a) => new() { Content = label, IsThreeState = true, IsChecked = State(a), Tag = a };
        var boxes = new[] { Box("Read-only", FileAttributes.ReadOnly), Box("Hidden", FileAttributes.Hidden), Box("System", FileAttributes.System), Box("Archive", FileAttributes.Archive) };
        var modified = new TextBox { Text = infos.Count == 1 ? infos[0].ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty, PlaceholderText = "unchanged (yyyy-MM-dd HH:mm:ss)" };
        var created = new TextBox { Text = infos.Count == 1 ? infos[0].CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty, PlaceholderText = "unchanged" };
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
