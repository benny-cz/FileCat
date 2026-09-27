using System.Globalization;
using System.Text;
using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

public readonly record struct CommandAvailability(bool Enabled, string? Reason = null)
{
    public static readonly CommandAvailability Yes = new(true);
    public static CommandAvailability No(string reason) => new(false, reason);
}

/// <summary>
/// Command dispatch: every surface (keys, menus, key bar, palette) executes command ids through here.
/// Availability is computed from cached context without blocking I/O; execution revalidates (§4.4).
/// </summary>
public sealed partial class MainViewModel
{
    public CommandAvailability GetAvailability(string id)
    {
        var tab = ActiveTab;
        var loc = tab?.Location;
        var caps = loc is null ? LocationCapabilities.None : Services.Providers.For(loc).GetCapabilities(loc);
        bool hasItem = tab is not null && tab.Listing.TryGetFocused(out var f) && f.Kind != EntryKind.Parent || tab?.Listing.HasMarks == true;
        switch (id)
        {
            case CommandIds.MakeDirectory:
                if ((caps & LocationCapabilities.ReferenceContainer) != 0) return CommandAvailability.No("Result sets hold references to items elsewhere; create folders in a real location.");
                return (caps & LocationCapabilities.CreateDirectory) != 0 ? CommandAvailability.Yes : CommandAvailability.No(Explain(LocationCapabilities.CreateDirectory));
            case CommandIds.EditNew:
                return (caps & LocationCapabilities.CreateFile) != 0 ? CommandAvailability.Yes : CommandAvailability.No(Explain(LocationCapabilities.CreateFile));
            case CommandIds.Copy or CommandIds.Move or CommandIds.Delete or CommandIds.DeletePermanent or CommandIds.View or CommandIds.Edit
                or CommandIds.Rename or CommandIds.Duplicate or CommandIds.CopyNames or CommandIds.CopyPaths:
                return hasItem ? CommandAvailability.Yes : CommandAvailability.No("Nothing is focused or marked.");
            case CommandIds.Back:
                return tab?.CanGoBack == true ? CommandAvailability.Yes : CommandAvailability.No("No earlier location in this tab.");
            case CommandIds.Forward:
                return tab?.CanGoForward == true ? CommandAvailability.Yes : CommandAvailability.No("No later location in this tab.");
            case CommandIds.ReopenTab:
                return Workspace.RecentlyClosed.Count > 0 ? CommandAvailability.Yes : CommandAvailability.No("No recently closed tabs.");
            case CommandIds.ClosePanel:
                return Workspace.Panels.Count > 1 ? CommandAvailability.Yes : CommandAvailability.No("The last panel cannot be closed.");
            case CommandIds.AddPanel:
                return Workspace.Panels.Count < WorkspaceViewModel.MaxPanels ? CommandAvailability.Yes : CommandAvailability.No($"At most {WorkspaceViewModel.MaxPanels} panels fit a usable layout.");
            case CommandIds.CloseTab:
                return Workspace.ActivePanel?.Tabs.Count > 1 ? CommandAvailability.Yes : CommandAvailability.No("A panel always keeps one tab.");
        }
        return CommandAvailability.Yes;

        string Explain(LocationCapabilities c) => loc is null ? "No location." : Services.Providers.For(loc).ExplainUnavailable(loc, c);
    }

    /// <summary>Executes a command; failures become visible notifications, never silent no-ops.</summary>
    public async void Execute(string id)
    {
        try
        {
            await ExecuteAsync(id);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Command {id} failed", ex);
            Notify($"{Services.Commands.Get(id)?.Title ?? id} failed: {ex.Message}", true);
        }
    }

    public async Task ExecuteAsync(string id)
    {
        var availability = GetAvailability(id);
        if (!availability.Enabled)
        {
            Notify(availability.Reason ?? "Not available here.");
            return;
        }
        var tab = ActiveTab;
        var panel = Workspace.ActivePanel;
        var listing = tab?.Listing;

        if (id.StartsWith(CommandIds.BookmarkGoPrefix, StringComparison.Ordinal))
        {
            GoToBookmark(int.Parse(id[CommandIds.BookmarkGoPrefix.Length..], CultureInfo.InvariantCulture), target: false);
            return;
        }
        if (id.StartsWith(CommandIds.BookmarkTargetPrefix, StringComparison.Ordinal))
        {
            GoToBookmark(int.Parse(id[CommandIds.BookmarkTargetPrefix.Length..], CultureInfo.InvariantCulture), target: true);
            return;
        }
        if (id.StartsWith(CommandIds.BookmarkSetPrefix, StringComparison.Ordinal))
        {
            SetBookmark(int.Parse(id[CommandIds.BookmarkSetPrefix.Length..]));
            return;
        }
        if (id.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal))
        {
            int profile = int.Parse(id[CommandIds.ColumnProfilePrefix.Length..], CultureInfo.InvariantCulture);
            if (tab is null) return;
            if (profile >= Services.Columns.Count)
            {
                Notify($"There is no column profile {profile}. Settings → Columns defines up to {Controls.ColumnProfileSet.MaxProfiles} profiles.");
                return;
            }
            tab.ColumnProfile = profile;
            Notify($"Columns: {Services.Columns.NameOf(profile)} (Alt+{profile})");
            return;
        }

        switch (id)
        {
            // ---- Navigation ----------------------------------------------------------------------------
            case CommandIds.Open:
                OpenFocused(withSystem: false);
                break;
            case CommandIds.OpenWithSystem:
                OpenFocused(withSystem: true);
                break;
            case CommandIds.Parent:
                tab?.GoUp();
                break;
            case CommandIds.Enter:
                if (tab is not null && !tab.TryEnterFocused(out var entered) && !TryOpenAsArchive(tab, entered)) Notify("The focused item cannot be entered: it is neither a folder nor a ZIP-compatible archive.");
                break;
            case CommandIds.Root:
                tab?.GoRoot();
                break;
            case CommandIds.Back:
                tab?.GoBack();
                break;
            case CommandIds.Forward:
                tab?.GoForward();
                break;
            case CommandIds.Refresh:
                tab?.Refresh();
                break;
            case CommandIds.GoTo:
                View.FocusPathBox();
                break;
            case CommandIds.Home:
                tab?.Navigate(WorkspaceViewModel.DefaultLocation());
                break;
            case CommandIds.ToggleHidden:
                Services.Settings.ShowHidden = !Services.Settings.ShowHidden;
                foreach (var p in Workspace.Panels)
                    foreach (var t in p.Tabs) t.Listing.ShowHidden = Services.Settings.ShowHidden;
                Notify(Services.Settings.ShowHidden ? "Hidden and system items are shown (dimmed)." : "Hidden and system items are hidden.");
                break;
            case CommandIds.LocationMenuLeft:
                await ShowLocationMenuAsync(Workspace.Panels.Count == 2 ? Workspace.Panels[0] : panel);
                break;
            case CommandIds.LocationMenuRight:
                await ShowLocationMenuAsync(Workspace.Panels.Count == 2 ? Workspace.Panels[1] : Workspace.ActiveTarget ?? panel);
                break;
            case CommandIds.FolderHistory:
                await ShowFolderHistoryAsync();
                break;
            case CommandIds.FileHistory:
                await ShowFileHistoryAsync();
                break;
            case CommandIds.Bookmarks:
                await ShowBookmarksAsync();
                break;
            case CommandIds.FindFolder:
                await ShowFolderHistoryAsync(findFolder: true);
                break;

            // ---- Panels and tabs -----------------------------------------------------------------------
            case CommandIds.SwitchPanel:
                Workspace.SwitchToTarget();
                View.FocusActivePanel();
                break;
            case CommandIds.SwitchPanelBack:
                Workspace.SwitchBack();
                View.FocusActivePanel();
                break;
            case CommandIds.SwapPanels:
                SwapWithTarget();
                break;
            case CommandIds.OpenInTarget:
                OpenInTarget(newTab: false);
                break;
            case CommandIds.OpenInNewTargetTab:
                OpenInTarget(newTab: true);
                break;
            case CommandIds.TargetToSource:
                if (RequireTarget() is { } tgt && tab?.Location is { } here) tgt.ActiveTab?.Navigate(here);
                break;
            case CommandIds.NewTab:
                if (panel is not null && tab?.Location is { } l) panel.OpenTab(l);
                break;
            case CommandIds.DuplicateTab:
                if (panel is not null && tab?.Location is { } dl)
                {
                    var dup = panel.OpenTab(dl);
                    var state = tab.ToState();
                    state.Locked = false;
                    dup.ApplyState(state);
                }
                break;
            case CommandIds.CopyTabToTarget:
                if (RequireTarget() is { } tp && tab?.Location is { } cl) tp.OpenTab(cl);
                break;
            case CommandIds.CloseTab:
                if (tab is not null) panel?.CloseTab(tab);
                break;
            case CommandIds.NextTab:
                panel?.CycleTab(1);
                break;
            case CommandIds.PreviousTab:
                panel?.CycleTab(-1);
                break;
            case CommandIds.ReopenTab:
                Workspace.ReopenClosed();
                break;
            case CommandIds.TabList:
                await ShowTabListAsync();
                break;
            case CommandIds.LockTab:
                if (tab is not null)
                {
                    tab.IsLocked = !tab.IsLocked;
                    tab.LockedRoot = tab.IsLocked ? tab.Location : null;
                    Notify(tab.IsLocked ? "Tab locked: navigating from it opens a new tab." : "Tab unlocked.");
                }
                break;
            case CommandIds.OpenInNewTab:
                if (panel is not null && tab?.Location is { } ntl && tab.Listing.TryGetFocused(out var nf))
                {
                    var child = Services.Providers.For(ntl).GetChildLocation(ntl, nf);
                    if (child is not null) panel.OpenTab(child, activate: false);
                }
                break;
            case CommandIds.AddPanel:
                // Every panel keeps a usable width: names, sizes, and dates must stay readable (plan §4.2).
                if (View.TopLevel is { } top && top.Bounds.Width / (Workspace.Panels.Count + 1) < MinimumPanelWidth)
                {
                    Notify($"There is no room for another panel: each would be narrower than {MinimumPanelWidth} pixels. Widen the window or close a panel first.");
                    break;
                }
                Workspace.Activate(Workspace.AddPanel());
                View.FocusActivePanel();
                break;
            case CommandIds.ClosePanel:
                if (panel is not null && await Dialogs.ConfirmAsync("Close panel", $"Close panel {panel.Number} and its {Formatters.Plural(panel.Tabs.Count, "tab", "tabs")}? Closed tabs can be reopened with Ctrl+Shift+T.", "Close panel"))
                    Workspace.RemovePanel(panel);
                break;
            case CommandIds.MaximizePanel:
                Workspace.ToggleMaximize();
                break;
            case CommandIds.FocusPanelPicker:
                await PickPanelAsync(focus: true);
                break;
            case CommandIds.ChooseTarget:
                await PickPanelAsync(focus: false);
                break;

            // ---- Marking -------------------------------------------------------------------------------
            case CommandIds.MarkToggleDown:
                if (listing is not null)
                {
                    int fi = listing.FocusedIndex;
                    if (fi >= 0) listing.ToggleMark(fi);
                    listing.SetFocus(fi + 1);
                }
                break;
            case CommandIds.MarkToggle:
                ToggleMarkAndSize();
                break;
            case CommandIds.MarkSelectMask:
                await MarkByMaskAsync(true);
                break;
            case CommandIds.MarkUnselectMask:
                await MarkByMaskAsync(false);
                break;
            case CommandIds.MarkInvert:
                listing?.InvertMarks(includeDirectories: false);
                break;
            case CommandIds.MarkInvertAll:
                listing?.InvertMarks(includeDirectories: true);
                break;
            case CommandIds.MarkAll:
                if (listing is null) break;
                listing.MarkAll(true);
                if (listing.State == ListingState.Loading)
                    Notify($"Selected the {listing.MarkedCount:N0} items listed so far; this folder is still being read. Ctrl+Shift+A selects everything once the listing is complete.");
                break;
            case CommandIds.MarkAllComplete:
                if (tab is not null) SelectAllWhenComplete(tab);
                break;
            case CommandIds.MarkNone:
                listing?.UnmarkEverything();
                CancelAllSizing();
                break;
            case CommandIds.MarkSameExt:
                listing?.MarkSameExtension(true);
                break;
            case CommandIds.UnmarkSameExt:
                listing?.MarkSameExtension(false);
                break;
            case CommandIds.MarkSameName:
                listing?.MarkSameName(true);
                break;
            case CommandIds.UnmarkSameName:
                listing?.MarkSameName(false);
                break;
            case CommandIds.MarkRestore:
                if (listing is not null)
                {
                    if (!listing.HasLastOperation) Notify("No earlier operation selection in this tab.");
                    else if (!listing.RestoreSelection()) Notify("The earlier operation selection is no longer available after the folder changed.");
                }
                break;
            case CommandIds.UnmarkHidden:
                listing?.UnmarkHidden();
                break;

            // ---- View --------------------------------------------------------------------------------
            case CommandIds.SortName:
                tab?.SortBy(SortField.Name);
                break;
            case CommandIds.SortExtension:
                tab?.SortBy(SortField.Extension);
                break;
            case CommandIds.SortTime:
                tab?.SortBy(SortField.Modified);
                break;
            case CommandIds.SortSize:
                tab?.SortBy(SortField.Size);
                break;
            case CommandIds.SortNone:
                tab?.SortBy(SortField.None);
                break;
            case CommandIds.QuickFilter:
                await QuickFilterAsync();
                break;

            // ---- Clipboard (text) ----------------------------------------------------------------------
            case CommandIds.CopyNames:
                await CopyTextAsync(sel => string.Join(Environment.NewLine, sel.Select(s => s.Name)));
                break;
            case CommandIds.CopyPaths:
                await CopyTextAsync(sel => string.Join(Environment.NewLine, sel.Select(DisplayPathOf)));
                break;

            // ---- Application ---------------------------------------------------------------------------
            case CommandIds.Palette:
                await ShowPaletteAsync();
                break;
            case CommandIds.Help:
                await ShowKeyboardReferenceAsync();
                break;
            case CommandIds.Menu:
                View.OpenMenuBar();
                break;
            case CommandIds.ThemeCycle:
                Services.Settings.Theme = ThemeManager.NextThemeName(Services.Settings.Theme);
                ThemeManager.Apply(Services.Settings.Theme);
                Services.Icons.ClearCache();
                Notify($"Theme: {ThemeManager.Current.Name}{(Services.Settings.Theme == "System" ? " (follows the system)" : "")}");
                break;
            case CommandIds.CommandLineFocus:
                ShowCommandLine = true;
                View.FocusCommandLine();
                break;
            case CommandIds.CommandHistory:
                await ShowCommandHistoryAsync();
                break;
            case CommandIds.InsertName:
            case CommandIds.InsertPath:
                InsertIntoCommandLine(full: id == CommandIds.InsertPath);
                break;
            case CommandIds.Reveal:
                if (FocusedFileSystemPath() is { } rp) Services.Shell.Reveal(rp);
                break;
            case CommandIds.Properties:
                if (FocusedFileSystemPath() is { } pp && !Services.Shell.ShowProperties(pp)) Notify("No properties dialog is available for this item.");
                break;
            case CommandIds.OpenTerminal:
                if (tab?.Location is { IsFileSystem: true } tl) Services.Shell.OpenTerminal(tl.Path, Services.Settings.Terminal.Shell);
                else Notify("A terminal can be opened only in a file-system folder.");
                break;
            case CommandIds.CheckUpdates:
                await CheckForUpdatesAsync();
                break;
            case CommandIds.ClearHistory:
                await ClearHistoryAsync();
                break;
            case CommandIds.ViewerWindows:
                await ShowViewerWindowsAsync();
                break;
            case CommandIds.About:
                await Dialogs.AlertAsync("About FileCat", $"FileCat {typeof(MainViewModel).Assembly.GetName().Version}\nMIT-licensed file manager and system-resource navigator.\nPlatform: {Services.Platform.Name}\nProfile: {Services.Paths.ProfileName}{(Services.Paths.IsPortable ? " (portable)" : "")}\nData: {Services.Paths.SettingsDirectory}");
                break;
            case CommandIds.Exit:
                View.TopLevel?.GetType().GetMethod("Close", Type.EmptyTypes)?.Invoke(View.TopLevel, null);
                break;
            default:
                if (!await ExecuteToolCommandAsync(id) && !await ExecuteOperationCommandAsync(id) && !await ExecuteWorkspaceCommandAsync(id))
                    Notify($"\"{Services.Commands.Get(id)?.Title ?? id}\" is not available in this build yet.");
                break;
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

    private PanelViewModel? RequireTarget()
    {
        var target = Workspace.ActiveTarget;
        if (target is null) Notify("This panel has no target panel. Press Shift+F12 to choose one.", true);
        return target;
    }

    private string? FocusedFileSystemPath()
    {
        var tab = ActiveTab;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var f)) return null;
        if (f.Kind == EntryKind.Parent) return tab.Location.IsFileSystem ? tab.Location.Path : null;
        return tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath;
    }

    private string DisplayPathOf(ItemRef item) =>
        item.FileSystemPath ?? Services.Providers.Display(item.Parent).TrimEnd('\\', '/') + "\\" + item.Name;


    /// <summary>Help → Check for updates: an explicit request, so it runs even with the automatic check off.</summary>
    private async Task CheckForUpdatesAsync()
    {
        Notify("Checking for a newer FileCat release…");
        var r = await UpdateCheck.CheckAsync();
        ClearNotification();
        if (r.Newer && r.Url is { } url)
        {
            if (await Dialogs.ConfirmAsync("Update available", $"FileCat {r.Latest} is available; this is {r.Current}.\n\nFileCat never downloads or installs updates itself. Open the release page?", "Open release page"))
                Services.Shell.Open(url);
            return;
        }
        await Dialogs.AlertAsync("Check for updates", r.Error ?? $"FileCat {r.Current} is the latest release.");
    }

    /// <summary>The daily check at startup, only when enabled in Settings → Privacy.</summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        var r = await UpdateCheck.RunScheduledAsync(Services.Settings, Services.SaveSettings);
        if (r is { Newer: true }) Notify($"FileCat {r.Latest} is available (this is {r.Current}). Help → Check for updates opens its release page.");
    }
    private void OpenFocused(bool withSystem)
    {
        var tab = ActiveTab;
        if (tab is null) return;
        if (tab.Listing.State == ListingState.Failed)
        {
            // A share that needs credentials: Enter opens the Windows sign-in prompt, then retries (NET-004).
            var loc = tab.Location;
            bool unc = loc is not null && (loc.Scheme == Schemes.Network || loc.IsFileSystem && PathUtil.IsUncPath(loc.Path));
            if (unc && tab.Listing.Error is { } err && (err.Contains("credentials", StringComparison.OrdinalIgnoreCase) || err.Contains("denied", StringComparison.OrdinalIgnoreCase)))
            {
                var target = loc!.Scheme == Schemes.Network ? loc.Path : ShareRoot(loc.Path);
                _ = SignInThenRefreshAsync(tab, target);
                return;
            }
            tab.Refresh();
            return;
        }
        if (!withSystem && tab.TryEnterFocused(out _)) return;
        if (!tab.Listing.TryGetFocused(out var e)) return;
        var item = e.Kind == EntryKind.Parent ? null : tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var path = item?.FileSystemPath ?? (e.Kind == EntryKind.Parent && tab.Location!.IsFileSystem ? tab.Location.Path : null);
        if (path is null)
        {
            if (item is { Kind: EntryKind.RegistryKey } && e.Tag is FileCat.Platform.Windows.RegistryRowInfo { LinkTarget: not null } linkInfo)
            {
                _ = OpenRegistryLinkAsync(linkInfo.LinkTarget, tab);
                return;
            }
            if (item is { Kind: EntryKind.RegistryKey, Flags: var flags } &&
                item.Parent.Scheme == Schemes.Registry && (flags & EntryFlags.Link) != 0)
            {
                _ = OpenRegistryLinkFromResultAsync(item, tab);
                return;
            }
            if (item is { Kind: EntryKind.RegistryValue }) { ViewFocused(hex: false); return; }
            _ = OpenNonFileSystemItemAsync(tab, item);
            return;
        }
        // A shortcut to a folder opens that folder here, as in the references. Its raw target is read without the
        // Shell (nothing is resolved or searched); links to files open through the system as before.
        if (!withSystem && !e.IsContainer && e.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && Core.FileSystem.ShellLinkReader.TryRead(path, out var link) && link is { IsDirectory: true })
        {
            tab.Navigate(Location.FileSystem(link.Path));
            return;
        }
        if (!withSystem && !e.IsContainer && TryLaunchAssociation(Core.Tools.Associations.Open, path))
        {
            Services.RecordFile(tab.Location!, e.Name);
            return;
        }
        try
        {
            Services.Shell.Open(path);
            if (!e.IsContainer) Services.RecordFile(tab.Location!, e.Name);
        }
        catch (Exception ex)
        {
            Notify($"Could not open \"{e.Name}\": {ex.Message}", true);
        }
    }

    private static string ShareRoot(string unc)
    {
        var parts = unc.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : unc;
    }

    private async Task SignInThenRefreshAsync(TabViewModel tab, string remote)
    {
        if (await SignInToServerAsync(remote)) tab.Refresh();
    }

    private void SwapWithTarget()
    {
        var src = Workspace.ActivePanel?.ActiveTab;
        var target = RequireTarget()?.ActiveTab;
        if (src?.Location is not { } a || target?.Location is not { } b) return;
        src.Navigate(b);
        target.Navigate(a);
    }

    private void OpenInTarget(bool newTab)
    {
        var tab = ActiveTab;
        var target = RequireTarget();
        if (tab?.Location is null || target is null || !tab.Listing.TryGetFocused(out var f)) return;
        var loc = f.Kind == EntryKind.Parent
            ? Services.Providers.For(tab.Location).GetParent(tab.Location)
            : Services.Providers.For(tab.Location).GetChildLocation(tab.Location, f) ?? tab.Location;
        if (loc is null) return;
        if (newTab) target.OpenTab(loc);
        else target.ActiveTab?.Navigate(loc);
    }

    private void ToggleMarkAndSize()
    {
        var tab = ActiveTab;
        var listing = tab?.Listing;
        if (tab is null || listing is null) return;
        int fi = listing.FocusedIndex;
        if (fi < 0 || !listing.TryGetFocused(out var e) || e.Kind == EntryKind.Parent) return;
        listing.ToggleMark(fi);
        bool marked = listing.IsVisibleMarked(fi);
        var key = tab.GetHashCode() + "|" + e.Name;
        if (!marked)
        {
            CancelSizing(key);
            return;
        }
        if (!e.IsContainer || !Services.Settings.SizeFolderOnSpace || e.Has(EntryFlags.Link)) return;
        var item = listing.GetItemRef(listing.FocusedStoreIndex);
        if (item.FileSystemPath is not { } path) return;
        var vol = tab.Location!;
        if (!Services.Settings.SizeFolderOnSlowLocations && (e.Has(EntryFlags.Offline) || IsSlowLocation(path)))
        {
            Notify("Marked without sizing: folder sizing is off for network, removable, and cloud locations (Settings → Behavior).");
            return;
        }
        var token = BeginSizing(key);
        var name = e.Name;
        var device = Services.Providers.For(vol).GetDeviceKey(vol);
        _ = Services.Io.Run(device, Core.Threading.IoPriority.Background, ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            return DirectorySizer.Compute(path, p => Services.Ui.Post(() => listing.SetComputedSize(name, p.Bytes, false)), linked.Token);
        }, token).ContinueWith(t =>
        {
            Services.Ui.Post(() =>
            {
                EndSizing(key, token);
                if (t.IsCompletedSuccessfully)
                {
                    listing.SetComputedSize(name, t.Result.Bytes, true);
                    if (t.Result.Inaccessible > 0) Notify($"\"{name}\": {t.Result.Inaccessible} folder(s) could not be read; the size is a lower bound.");
                }
                else if (t.IsCanceled && listing.FindStoreIndex(name) is var si && si >= 0 && !listing.IsMarked(si))
                {
                    listing.SetComputedSize(name, -1, false);
                }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Network shares (mapped drive letters included), removable and optical media: Space marks without sizing there
    /// unless enabled (plan §4.3). The drive type comes from the mount manager, without touching the device.
    /// </summary>
    private bool IsSlowLocation(string path)
    {
        if (PathUtil.IsUncPath(path)) return true;
        try
        {
            // The drive letter's type on Windows (resolving mount points could reach a dead share); the mount point on Unix.
            var root = OperatingSystem.IsWindows() ? Path.GetPathRoot(path) : Services.Platform.FileOperations.GetVolumeRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType is DriveType.Network or DriveType.Removable or DriveType.CDRom;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Ctrl+Shift+A: selects everything once the listing is complete (a bounded membership pass, then frozen; later
    /// arrivals are never added). Navigating away cancels it.
    /// </summary>
    private void SelectAllWhenComplete(TabViewModel tab)
    {
        var listing = tab.Listing;
        if (listing.State != ListingState.Loading)
        {
            listing.MarkAll(true);
            return;
        }
        var location = listing.Location;
        Notify("Selecting everything once this folder is completely listed…");
        void Handler(object? sender, ListingChange change)
        {
            if (!Equals(listing.Location, location))
            {
                listing.Changed -= Handler;
                return;
            }
            if (listing.State == ListingState.Loading) return;
            listing.Changed -= Handler;
            listing.MarkAll(true);
            var incomplete = listing.Issues.Count > 0 ? " The listing reported problems; see the banner." : string.Empty;
            Notify($"Selected all {listing.MarkedCount:N0} items.{incomplete}");
        }
        listing.Changed += Handler;
    }

    private const double MinimumPanelWidth = 300;

    /// <summary>Lists open viewer windows; Enter brings one to the front (FAR's screen switcher, plan §4.1).</summary>
    private async Task ShowViewerWindowsAsync()
    {
        var windows = FileCat.App.Views.ViewerWindow.OpenWindows.ToList();
        if (windows.Count == 0)
        {
            Notify("No viewer windows are open. F3 opens the focused file in one.");
            return;
        }
        var items = windows.Select(w => new ChoiceItem(Path.GetFileName(w.DisplayName.TrimEnd('\\', '/')), w.DisplayName)).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Viewer windows", items) { Hint = "Type to filter · Enter switches to the viewer · Esc closes" });
        if (r.Index < 0 || r.Index >= windows.Count) return;
        var chosen = windows[r.Index];
        if (chosen.WindowState == Avalonia.Controls.WindowState.Minimized) chosen.WindowState = Avalonia.Controls.WindowState.Normal;
        chosen.Activate();
    }

    private async Task ClearHistoryAsync()
    {
        int pinned = Services.History.Folders.Count(h => h.Pinned) + Services.History.Files.Count(h => h.Pinned);
        var buttons = new List<DialogButton> { new("Cancel", "cancel", IsCancel: true) };
        if (pinned > 0) buttons.Add(new DialogButton("Clear, including pinned", "all", IsDanger: true));
        buttons.Add(new DialogButton("Clear history", "clear", IsDefault: true));
        var text = "Forget recent folders and files, command lines, copy destinations, masks, and search terms? Bookmarks stay."
            + (pinned > 0 ? $"\n\n{Formatters.Plural(pinned, "pinned entry stays", "pinned entries stay")} unless you clear them too." : string.Empty);
        var r = await Dialogs.ShowCustomAsync("Clear history", new Avalonia.Controls.TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 560 }, buttons);
        if (r is not ("clear" or "all")) return;
        Services.ClearHistory(includePinned: r as string == "all");
        Notify("History cleared. Bookmarks were kept.");
    }

    private async Task MarkByMaskAsync(bool select)
    {
        var listing = ActiveTab?.Listing;
        if (listing is null) return;
        var r = await Dialogs.PromptAsync(new PromptOptions(select ? "Select" : "Unselect", "Mask (e.g. *.cs;*.axaml|*Test*, /regex/, trailing \\ for folders, @saved filter):")
        {
            Text = Services.History.Masks.FirstOrDefault() ?? "*.*",
            History = Services.MaskSuggestions(),
            Validate = t => Mask.TryParse(t, out _, out var err) ? null : err,
            CheckboxText = "Include folders",
            CheckboxValue = false,
        });
        if (r is null) return;
        AppServices.RememberText(Services.History.Masks, r.Text);
        int n = listing.MarkByMask(Mask.Parse(r.Text), select, r.Checked);
        Notify($"{(select ? "Selected" : "Unselected")} {Formatters.Plural(n, "item", "items")} matching \"{r.Text}\".");
    }

    private async Task QuickFilterAsync()
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var r = await Dialogs.PromptAsync(new PromptOptions("Quick filter", "Show only items matching (empty shows all). Marks on hidden items are kept:")
        {
            Text = tab.Listing.Filter?.Text ?? string.Empty,
            History = Services.MaskSuggestions(),
            Validate = t => string.IsNullOrWhiteSpace(t) || Mask.TryParse(t, out _, out var err) ? null : err,
        });
        if (r is null) return;
        tab.SetFilter(r.Text);
    }

    private async Task CopyTextAsync(Func<IReadOnlyList<ItemRef>, string> format)
    {
        var sel = ActiveTab?.Listing.GetSelection();
        if (sel is null || sel.Count == 0 || View.Clipboard is not { } cb) return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(cb, format(sel));
        Notify($"Copied {Formatters.Plural(sel.Count, "entry", "entries")} to the clipboard.");
    }

    private void InsertIntoCommandLine(bool full)
    {
        var tab = ActiveTab;
        if (tab is null || !tab.Listing.TryGetFocused(out var f) || f.Kind == EntryKind.Parent) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var text = full ? DisplayPathOf(item) : f.Name;
        var quoted = Core.Tools.ShellQuoting.Quote(text, Services.Settings.Terminal.Shell);
        var current = CommandLineText;
        CommandLineText = current.Length == 0 || current.EndsWith(' ') ? current + quoted + " " : current + " " + quoted + " ";
        ShowCommandLine = true;
    }

    private void SetBookmark(int slot)
    {
        var loc = ActiveTab?.Location;
        if (loc is null) return;
        Services.History.Bookmarks.RemoveAll(b => b.Slot == slot);
        Services.History.Bookmarks.Add(new BookmarkEntry { Slot = slot, Location = loc, Name = Services.Providers.Display(loc) });
        Services.SaveHistory();
        Notify($"Bookmark {slot} set to {Services.Providers.Display(loc)}. Ctrl+{slot} returns here.");
    }

    private void GoToBookmark(int slot, bool target)
    {
        var b = Services.History.Bookmarks.FirstOrDefault(x => x.Slot == slot);
        if (b?.Location is null)
        {
            Notify($"Bookmark {slot} is not set. Ctrl+Shift+{slot} sets it to the current location.");
            return;
        }
        var panel = target ? RequireTarget() : Workspace.ActivePanel;
        panel?.ActiveTab?.Navigate(b.Location);
    }

    private async Task OpenNonFileSystemItemAsync(TabViewModel tab, ItemRef? item)
    {
        await Task.CompletedTask;
        Notify(item is null ? "Nothing to open." : $"\"{item.Name}\" can be viewed with F3; opening it with an application needs extraction first (F5).");
    }

    /// <summary>File-operation commands are wired by the operations layer (see MainViewModel.Operations.cs).</summary>
    private partial Task<bool> ExecuteOperationCommandAsync(string id);

    /// <param name="total">The full count when <paramref name="names"/> holds only the first names.</param>
    internal static string ExactList(IEnumerable<string> names, int max = 8, int total = -1)
    {
        var sb = new StringBuilder();
        int shown = 0, count = 0;
        foreach (var n in names)
        {
            count++;
            if (shown == max) continue;
            sb.Append("  • ").AppendLine(Formatters.SafeName(n));
            shown++;
        }
        if (total >= 0) count = total;
        if (count > shown) sb.Append("  … and ").Append((count - shown).ToString("N0")).AppendLine(" more");
        return sb.ToString();
    }
}
