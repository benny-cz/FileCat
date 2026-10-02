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
        bool registryItem = TryGetFocusedRegistryItem(out var focusedRegistry);
        if (LayoutAvailability(id) is { } layout) return layout;
        if (Core.Search.ResultSetProvider.IsWorkingSetList(loc))
        {
            // The list of working sets: file commands act on the sets themselves.
            switch (id)
            {
                case CommandIds.MakeDirectory:
                    return CommandAvailability.Yes;
                case CommandIds.Rename or CommandIds.Delete or CommandIds.DeletePermanent:
                    return hasItem ? CommandAvailability.Yes : CommandAvailability.No("No working set is focused. F7 creates one.");
            }
        }
        switch (id)
        {
            case CommandIds.RemoveFromSet:
                return (caps & LocationCapabilities.ReferenceContainer) != 0 && hasItem
                    ? CommandAvailability.Yes
                    : CommandAvailability.No("Remove from set works in search results and working sets; it never deletes anything.");
            case CommandIds.AddToWorkingSet:
                return hasItem || Core.Search.ResultSetProvider.IsWorkingSetList(loc) ? CommandAvailability.Yes : CommandAvailability.No("Nothing is focused or marked.");
            case CommandIds.RegistryExport:
                return registryItem || loc?.Scheme == Schemes.Registry && loc.Path.Length > 0
                    ? CommandAvailability.Yes : CommandAvailability.No("Choose a Registry key or value to export.");
            case CommandIds.RegistryImport:
                return loc?.Scheme == Schemes.Registry && loc.Path.Length > 0 &&
                    (caps & LocationCapabilities.CreateDirectory) != 0
                    ? CommandAvailability.Yes : CommandAvailability.No("Open a concrete HKCU, HKLM, or HKU Registry key to import into its scope.");
            case CommandIds.RegistrySaveData:
                return registryItem && focusedRegistry.Kind == EntryKind.RegistryValue ? CommandAvailability.Yes : CommandAvailability.No("Focus a Registry value to save its raw data.");
            case CommandIds.RegistryLoadData:
                if (loc?.Scheme != Schemes.Registry || loc.Path.Length == 0) return CommandAvailability.No("Open a Registry key to load value data into it.");
                return Platform.Windows.RegistryAliases.IsAliasPath(loc.Path) ? CommandAvailability.No(Platform.Windows.RegistryAliases.ReadOnlyReason) : CommandAvailability.Yes;
            case CommandIds.RegistryView:
                return loc?.Scheme == Schemes.Registry ? CommandAvailability.Yes : CommandAvailability.No("Open a Registry location first.");
            case CommandIds.RegistryWritable:
                return loc?.Scheme == Schemes.Registry && Platform.Windows.RegistryAliases.IsAliasPath(loc.Path)
                    ? CommandAvailability.Yes
                    : CommandAvailability.No("Only HKCR and HKCC are merged or alias views; other Registry keys are edited where they are shown.");
            case CommandIds.Edit or CommandIds.Delete or CommandIds.DeletePermanent or CommandIds.Rename
                when registryItem && Platform.Windows.RegistryAliases.IsAliasPath(focusedRegistry.Parent.Path):
                return CommandAvailability.No(Platform.Windows.RegistryAliases.ReadOnlyReason);
            case CommandIds.HexEdit:
            {
                if (tab is null || !tab.Listing.TryGetFocused(out var hexRow) || hexRow.IsContainer || hexRow.Kind == EntryKind.Parent)
                    return CommandAvailability.No("Focus a file to edit its bytes.");
                var hexPath = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath;
                if (hexPath is null) return CommandAvailability.No("Only files on a drive can be edited byte by byte; extract or copy this item first.");
                return PathUtil.IsUncPath(hexPath)
                    ? CommandAvailability.No("Hex editing works on local drives, where other computers cannot write the file; copy it to a local drive first.")
                    : CommandAvailability.Yes;
            }
            case CommandIds.HexRecovery:
                return CommandAvailability.Yes;
            case CommandIds.Edit when registryItem && focusedRegistry.Kind != EntryKind.RegistryValue:
                return CommandAvailability.No("Registry keys have no editable value data. Select a value, or use a named Registry command.");
            case CommandIds.Move when registryItem:
                return CommandAvailability.No("Moving a Registry item is not a single atomic operation. Use Copy, then review and delete the source explicitly.");
            case CommandIds.Duplicate when registryItem:
                return CommandAvailability.No("Choose a Registry key in the target panel, then use Copy.");
            case CommandIds.MakeDirectory:
                if ((caps & LocationCapabilities.ReferenceContainer) != 0)
                    return CommandAvailability.No(Core.Search.ResultSetProvider.IsWorkingSet(loc)
                        ? "A working set holds references to items elsewhere; create folders in a real location. F7 in the list of working sets (Backspace) creates another set."
                        : "Result sets hold references to items elsewhere; create folders in a real location.");
                return (caps & LocationCapabilities.CreateDirectory) != 0 ? CommandAvailability.Yes : CommandAvailability.No(Explain(LocationCapabilities.CreateDirectory));
            case CommandIds.EditNew:
                return (caps & LocationCapabilities.CreateFile) != 0 ? CommandAvailability.Yes : CommandAvailability.No(Explain(LocationCapabilities.CreateFile));
            case CommandIds.OpenInNewTab:
                // Only what opens as a place (a folder, a drive, an archive): a file's own tab would be empty.
                return tab?.Location is { } openLoc && tab.Listing.TryGetFocused(out var openItem) &&
                       Services.Providers.For(openLoc).GetChildLocation(openLoc, openItem) is not null
                    ? CommandAvailability.Yes
                    : CommandAvailability.No("The focused item is not a folder.");
            // What the location itself cannot do is dimmed with its reason (the key bar, the palette), not offered and refused.
            case CommandIds.Delete or CommandIds.DeletePermanent when loc is not null && loc.Scheme != Schemes.ResultSet && (caps & LocationCapabilities.Delete) == 0:
                return CommandAvailability.No(Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.Delete));
            case CommandIds.Move when loc is not null && (caps & LocationCapabilities.MoveSource) == 0:
                return CommandAvailability.No(Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.MoveSource) + " Use F5 to copy instead.");
            // Cutting is moving later: where nothing can be moved from, it is not offered either.
            case CommandIds.CutToClipboard when loc is not null && (caps & LocationCapabilities.MoveSource) == 0:
                return CommandAvailability.No(Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.MoveSource) + " Copy items to the clipboard (Ctrl+C) instead.");
            case CommandIds.Edit when loc?.Scheme == Schemes.Recovery:
                return CommandAvailability.No(Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.ExternalEdit) + " View it with F3.");
            case CommandIds.Rename when loc is not null && (caps & LocationCapabilities.Rename) == 0:
                return CommandAvailability.No(Services.Providers.For(loc).ExplainUnavailable(loc, LocationCapabilities.Rename));
            case CommandIds.Copy or CommandIds.Move or CommandIds.Delete or CommandIds.DeletePermanent or CommandIds.View or CommandIds.Edit
                or CommandIds.Rename or CommandIds.Duplicate or CommandIds.CopyNames or CommandIds.CopyPaths:
                return hasItem ? CommandAvailability.Yes : CommandAvailability.No(NothingChosenReason(tab));
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
        QuietConnect = false;
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
                Services.Metadata.Invalidate(); // Reread means every column, permissions and versions too
                if (tab?.Location is { Scheme: Schemes.Recovery } scanned) Services.Recovery.Forget(scanned); // and a fresh scan
                tab?.Refresh();
                break;
            case CommandIds.GoTo:
                View.FocusPathBox();
                break;
            case CommandIds.Home:
                tab?.Navigate(WorkspaceViewModel.DefaultLocation());
                break;
            case CommandIds.ToggleHidden:
                Services.Settings.ShowHidden = ShowHiddenItems = !Services.Settings.ShowHidden;
                foreach (var p in Workspace.Panels)
                    foreach (var t in p.Tabs) t.Listing.ShowHidden = Services.Settings.ShowHidden;
                Notify(Services.Settings.ShowHidden ? "Hidden and system items are shown (dimmed)." : "Hidden and system items are hidden.");
                break;
            case CommandIds.LocationMenuSource:
                await ShowLocationMenuAsync(panel);
                break;
            case CommandIds.LocationMenuTarget:
                // Where F5 and F6 go, pointed elsewhere without leaving the source (D-53). One panel is its own target.
                if (Workspace.Panels.Count < 2) await ShowLocationMenuAsync(panel);
                else if (RequireTarget() is { } target) await ShowLocationMenuAsync(target, activate: false);
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
            case CommandIds.MoveTabLeft:
            case CommandIds.MoveTabRight:
                if (panel?.ActiveTab is { } moving) panel.MoveTab(moving, panel.Tabs.IndexOf(moving) + (id == CommandIds.MoveTabLeft ? -1 : 1));
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
                // Every panel keeps a usable size: names, sizes, and dates must stay readable (plan §4.2).
                if (panel is not null && !RoomForPanel(panel, Core.State.DockSide.Right)) break;
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
            case CommandIds.CountFolderSizes:
                if (tab is not null) CountFolderSizes(tab);
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
                // The search box in the menu bar; the same search in a dialog where there is no box.
                if (!View.FocusCommandSearch()) await ShowPaletteAsync();
                break;
            case CommandIds.Help:
                await ShowKeyboardReferenceAsync();
                break;
            case CommandIds.Menu:
                View.OpenMenuBar();
                break;
            case CommandIds.ContextMenu:
                View.ShowContextMenu();
                break;
            case CommandIds.ThemePick:
                await ChooseThemeAsync();
                break;
            case CommandIds.ToggleToolbar:
                Services.Settings.ShowToolbar = ShowToolbar = !ShowToolbar;
                Services.SaveSettings();
                break;
            case CommandIds.ToggleDriveButtons:
                Services.Settings.ShowDriveButtons = ShowDriveButtons = !ShowDriveButtons;
                Services.SaveSettings();
                break;
            case CommandIds.ThemeCycle:
            case CommandIds.ThemePrevious:
                Services.Settings.Theme = ThemeManager.CycleThemeName(Services.Settings.Theme, id == CommandIds.ThemePrevious ? -1 : 1);
                ThemeManager.Apply(Services.Settings.Theme);
                Services.Icons.ClearCache();
                Services.SaveSettings();
                Notify($"Theme: {ThemeManager.DisplayName(Services.Settings.Theme)}. View → Theme… shows them all with a preview.");
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
            case CommandIds.HiddenData:
                OpenHiddenData();
                break;
            case CommandIds.FileRecord:
                ShowFileRecord();
                break;
            case CommandIds.OpenTerminal:
                if (tab?.Location is { IsFileSystem: true } tl) Services.Shell.OpenTerminal(tl.Path, Services.Settings.Terminal.Shell);
                else if (tab?.Location is { Scheme: Schemes.Sftp } remote) OpenSshTerminal(remote);
                else Notify("A terminal opens in a folder on disk, or as an SSH session on an SFTP server.");
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
                await Views.AboutDialog.ShowAsync(this);
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
        if (fi < 0 || !listing.TryGetFocused(out var e)) return;
        // The next item has the cursor then: holding Space marks, and sizes, everything below.
        listing.SetFocus(fi + 1);
        if (e.Kind == EntryKind.Parent) return;
        listing.ToggleMark(fi);
        bool marked = listing.IsVisibleMarked(fi);
        var key = tab.GetHashCode() + "|" + e.Name;
        if (!marked)
        {
            CancelSizing(key);
            return;
        }
        if (!e.IsContainer || !Services.Settings.SizeFolderOnSpace || e.Has(EntryFlags.Link)) return;
        int storeIndex = listing.GetStoreIndex(fi);
        if (listing.GetItemRef(storeIndex).FileSystemPath is not { } path) return;
        if (!Services.Settings.SizeFolderOnSlowLocations && (e.Has(EntryFlags.Offline) || IsSlowLocation(path)))
        {
            Notify("Marked without sizing: folder sizing is off for network, removable, and cloud locations (Settings → Behavior). Count in the status line counts them anyway.");
            return;
        }
        SizeFolder(tab, listing, e, storeIndex);
    }

    /// <summary>
    /// Counts the sizes of the marked folders not counted yet, or, with none marked, of the folder under the cursor:
    /// what Count in the status line does. Asked for, it counts on network, removable, and cloud locations too (Space
    /// does only if the settings say so); links are never followed.
    /// </summary>
    public void CountFolderSizes(TabViewModel tab)
    {
        var listing = tab.Listing;
        if (tab.Location is not { IsFileSystem: true })
        {
            Notify("Folder sizes are counted on drives and shares.");
            return;
        }
        var folders = listing.MarkedCount > 0 ? listing.MarkedUnsizedFolders() : [];
        if (listing.MarkedCount == 0 && listing.TryGetFocused(out var focused) && focused.Kind == EntryKind.Directory && !focused.Has(EntryFlags.Link))
            folders.Add(listing.FocusedStoreIndex);
        if (folders.Count == 0)
        {
            Notify(listing.MarkedCount > 0 ? "The marked folders' sizes are counted already." : "Mark folders, or put the cursor on one, to count their sizes.");
            return;
        }
        foreach (int si in folders) SizeFolder(tab, listing, listing.Store[si], si);
    }

    /// <summary>
    /// Counts a folder's size in the background at low priority (Esc stops it). Its row shows the size as it grows, and
    /// the tab's status line says it is counting.
    /// </summary>
    private void SizeFolder(TabViewModel tab, ListingModel listing, EntryData e, int storeIndex)
    {
        if (listing.GetItemRef(storeIndex).FileSystemPath is not { } path || tab.Location is not { } vol) return;
        var key = tab.GetHashCode() + "|" + e.Name;
        var token = BeginSizing(key);
        tab.SizingFolders++;
        tab.UpdateStatus();
        // The count is the tab's until it ends, or until the tab leaves the folder or closes: then it stops at once, as its
        // size could not land, and the next folder's status line must not say it is counting (V12). From then on nothing
        // it posts touches the listing, which a closed tab has disposed (release issue I88).
        bool open = true, left = false, closed = false;
        void Ended()
        {
            if (!open) return;
            open = false;
            listing.Changed -= Moved;
            tab.Closed -= Closed;
            tab.SizingFolders = Math.Max(0, tab.SizingFolders - 1);
        }
        void Moved(object? sender, ListingChange change)
        {
            if (Equals(listing.Location, vol)) return;
            left = true;
            Ended();
            CancelSizing(key, token);
            tab.UpdateStatus();
        }
        void Closed()
        {
            left = closed = true;
            Ended();
            CancelSizing(key, token);
        }
        listing.Changed += Moved;
        tab.Closed += Closed;
        // Esc (or unmarking it) ends it in the tab at once too, not when a call held by a slow disk returns.
        OnSizingStopped(key, token, () =>
        {
            Ended();
            if (!closed) tab.UpdateStatus();
        });
        var name = e.Name;
        var device = Services.Providers.For(vol).GetDeviceKey(vol);
        var fs = Services.Platform.FileOperations;
        _ = Services.Io.Run(device, Core.Threading.IoPriority.Background, ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            // The folder's time as the counting begins: the size stays through refreshes while the folder keeps it (I32).
            long? modified = null;
            try { modified = Directory.GetLastWriteTimeUtc(path).Ticks; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            // Which folder this is, before and after: one deleted and made again, or replaced, under the same name while
            // it was counted is another folder, and the size counted is not its size (V12).
            string? before = fs.GetFileIdentity(path);
            // Sizes land only while the panel still shows that folder's parent: another folder may hold one of the same name.
            var size = DirectorySizer.Compute(path, p => Services.Ui.Post(() =>
            {
                if (!left && listing.Location == vol) listing.SetComputedSize(name, p.Bytes, false);
            }), linked.Token);
            return (Size: size, Modified: modified, Replaced: before is not null && fs.GetFileIdentity(path) != before);
        }, token).ContinueWith(t =>
        {
            Services.Ui.Post(() =>
            {
                EndSizing(key, token);
                Ended();
                if (!left && listing.Location == vol)
                {
                    if (t.IsCompletedSuccessfully && t.Result.Replaced)
                    {
                        listing.SetComputedSize(name, -1, false);
                        Notify($"\"{name}\" was replaced while its size was counted; count it again.");
                    }
                    else if (t.IsCompletedSuccessfully)
                    {
                        listing.SetComputedSize(name, t.Result.Size.Bytes, true, t.Result.Modified);
                        if (t.Result.Size.Inaccessible > 0) Notify($"\"{name}\": {t.Result.Size.Inaccessible} folder(s) could not be read; the size is a lower bound.");
                    }
                    else if (t.IsCanceled && listing.FindStoreIndex(name) is var si && si >= 0 && !listing.IsMarked(si))
                    {
                        listing.SetComputedSize(name, -1, false);
                    }
                }
                if (!closed) tab.UpdateStatus();
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

    /// <summary>Lists open viewer and hex editor windows; Enter brings one to the front.</summary>
    private async Task ShowViewerWindowsAsync()
    {
        var windows = FileCat.App.Views.ViewerWindow.OpenWindows
            .Select(w => (Window: (Avalonia.Controls.Window)w, w.DisplayName, Kind: "Viewer"))
            .Concat(FileCat.App.Views.HexEditorWindow.OpenWindows
                .Select(w => (Window: (Avalonia.Controls.Window)w, w.DisplayName, Kind: "Hex editor")))
            .Concat(FileCat.App.Views.CompareWindow.OpenWindows
                .Select(w => (Window: (Avalonia.Controls.Window)w, DisplayName: w.Title ?? "Compare", Kind: "Comparison"))).ToList();
        if (windows.Count == 0)
        {
            Notify("No viewer, hex editor, or comparison windows are open. F3 opens the focused file in a viewer.");
            return;
        }
        var items = windows.Select(w => new ChoiceItem(Path.GetFileName(w.DisplayName.TrimEnd('\\', '/')) + " · " + w.Kind, w.DisplayName)).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Viewer and editor windows", items) { Hint = "Type to filter · Enter switches to the window · Esc closes" });
        if (r.Index < 0 || r.Index >= windows.Count) return;
        var chosen = windows[r.Index].Window;
        if (chosen.WindowState == Avalonia.Controls.WindowState.Minimized) chosen.WindowState = Avalonia.Controls.WindowState.Normal;
        chosen.Activate();
    }

    private async Task ClearHistoryAsync()
    {
        int pinned = Services.History.Folders.Count(h => h.Pinned) + Services.History.Files.Count(h => h.Pinned);
        var buttons = new List<DialogButton> { new("Cancel", "cancel", IsCancel: true) };
        if (pinned > 0) buttons.Add(new DialogButton("Clear, including pinned", "all", IsDanger: true));
        buttons.Add(new DialogButton("Clear history", "clear", IsDefault: true));
        var text = "Forget recent folders and files, command lines, copy destinations, masks, search terms, and recently used commands? Bookmarks stay."
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
        var r = await Dialogs.PromptAsync(new PromptOptions("Quick filter", "Show only the files that match (folders show unless the mask names them, as in src\\); empty shows all. Marks on hidden items are kept:")
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
