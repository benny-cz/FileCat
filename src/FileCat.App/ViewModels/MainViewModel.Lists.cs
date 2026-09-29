using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

/// <summary>Type-to-filter lists: location menu, histories, bookmarks, tabs, panels, palette (plan §11, §4.4).</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The location menu (Alt+F1 for the source panel, Alt+F2 for the target, or a panel's own button): every place,
    /// as <see cref="Places"/> lists them. The drives are asked anew, each within a moment.
    /// </summary>
    /// <param name="activate">Whether the panel becomes the source; Alt+F2 points the target elsewhere and the keyboard stays.</param>
    private async Task ShowLocationMenuAsync(PanelViewModel? panel, bool activate = true)
    {
        if (panel?.ActiveTab is null) return;
        // The drives This PC lists, as each describes itself within a moment: a dropped network drive shows as not
        // responding instead of holding up the menu (the queries run off the UI thread).
        var computer = Services.Providers.For(new Location(Schemes.Computer, string.Empty)) as ComputerProvider;
        var drives = computer is null ? [] : await computer.QueryDrivesAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);
        var shown = Places(drives).SelectMany(p => p.Variants.Prepend(p)).ToList();
        var items = shown.Select(p => new ChoiceItem(p.Title, p.Detail) { Icon = p.Icon }).ToList();
        // A drive letter typed first opens that drive at once, as in Salamander's and Total Commander's drive menus.
        var letters = new Dictionary<char, int>();
        for (int i = 0; i < shown.Count; i++)
            if (shown[i].Letter is { } letter) letters[letter] = i;
        string role = Workspace.Panels.Count < 2 ? ""
            : ReferenceEquals(panel, Workspace.ActivePanel) ? " (source)"
            : ReferenceEquals(panel, Workspace.ActiveTarget) ? " (target)" : "";
        var r = await Dialogs.ChooseAsync(new ChoiceOptions($"Location for panel {panel.Number}{role}", items)
        {
            Hint = (letters.Count > 0 ? "A drive letter opens that drive · " : "") + "Type to filter · Enter opens · Shift+Enter opens in a new tab",
            Icons = Services.Icons,
            Accelerators = letters,
        });
        if (r.Index < 0) return;
        await OpenPlaceAsync(panel, shown[r.Index], r.Alternate, activate);
    }

    /// <summary>
    /// Opens a place in a panel, in a new tab when asked: a drive at the folder another panel shows there (Total
    /// Commander does the same), or the connection dialog.
    /// </summary>
    /// <param name="activate">Whether the panel becomes the source (the keyboard goes with it).</param>
    public async Task OpenPlaceAsync(PanelViewModel panel, Place place, bool newTab, bool activate = true)
    {
        if (place.Connects)
        {
            await ConnectSftpAsync(panel);
            return;
        }
        if (place.Location is not { } chosen) return;
        if (place.Drive is not null && FolderOnDrive(panel, chosen) is { } there) chosen = there;
        if (newTab) panel.OpenTab(chosen);
        else panel.ActiveTab?.Navigate(chosen);
        if (activate) Workspace.Activate(panel);
        View.FocusActivePanel();
    }

    /// <summary>A place button: the place opens in its panel, which becomes the source; a failure is said, not thrown.</summary>
    public async void OpenPlace(PanelViewModel panel, Place place, bool newTab)
    {
        try
        {
            await OpenPlaceAsync(panel, place, newTab);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Opening {place.Title} failed", ex);
            Notify($"{place.Title} could not be opened: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Every place the location menu offers, and the place buttons above each panel show (D-52, D-53), in the menu's
    /// order: <paramref name="drives"/>, This PC, phones, working sets, the Registry, the home and special folders,
    /// bookmarks, saved servers, and a new connection.
    /// </summary>
    public List<Place> Places(IReadOnlyList<DriveTag> drives)
    {
        var icons = Services.Icons;
        var places = new List<Place>();
        // Folders as Explorer shows them where it lists them (Desktop, Documents, and Downloads have their own icons).
        Func<Avalonia.Media.IImage?> FolderIcon(Location loc)
        {
            if (!loc.IsFileSystem) return loc.Scheme switch
            {
                Schemes.Registry => () => icons.GetPlaceIcon(IconKind.RegistryKey),
                Schemes.Computer => () => icons.GetPlaceIcon(IconKind.Computer),
                Schemes.Sftp or Schemes.Ftp => () => icons.GetPlaceIcon(IconKind.Server),
                _ => () => icons.GetPlaceIcon(IconKind.Folder),
            };
            var path = loc.Path.TrimEnd('\\', '/');
            var parent = Path.GetDirectoryName(path);
            var entry = new EntryData(Path.GetFileName(path), EntryKind.Directory);
            var folder = parent is null ? null : Location.FileSystem(parent);
            return () => icons.GetIcon(entry, folder);
        }
        foreach (var tag in drives)
        {
            string name = PathUtil.IsWindows ? tag.RootPath.TrimEnd('\\') : tag.RootPath;
            var drive = new EntryData(name, EntryKind.Drive) { Tag = tag };
            bool lettered = PathUtil.IsWindows && name.Length == 2 && name[1] == ':' && char.IsAsciiLetter(name[0]);
            places.Add(new Place(name, DriveDetail(tag), Location.FileSystem(tag.RootPath), () => icons.GetIcon(drive))
            {
                Group = PlaceGroup.Drives,
                Drive = tag,
                Letter = lettered ? char.ToUpperInvariant(name[0]) : null,
                BarLabel = lettered ? name[..1].ToUpperInvariant() : name == "/" ? "/" : Path.GetFileName(name.TrimEnd('/')),
            });
        }
        var thisPc = new Location(Schemes.Computer, string.Empty); // "This PC" on Windows, "Computer" elsewhere
        places.Add(new Place(Services.Providers.Display(thisPc), "All drives", thisPc, () => icons.GetPlaceIcon(IconKind.Computer)) { Group = PlaceGroup.Devices });
        if (Services.Providers.IsRegistered(Schemes.Mtp))
            places.Add(new Place("Phones and cameras", "Portable devices over MTP (unlock a phone and choose File transfer)", FileCat.Platform.Windows.Mtp.MtpProvider.Devices,
                () => icons.GetPlaceIcon(IconKind.Phone)) { Group = PlaceGroup.Devices });
        int workingSets = Services.WorkingSets.All.Count;
        places.Add(new Place("Working sets", workingSets == 0 ? "Collect items from many folders (references, never copies)" : $"{Formatters.Plural(workingSets, "set", "sets")} of items collected from many folders",
            Core.Search.ResultSetProvider.WorkingSetList, () => icons.GetPlaceIcon(IconKind.Collection)) { Group = PlaceGroup.Collections });
        if (Services.Providers.IsRegistered(Schemes.Registry))
        {
            var views = new[] { "default", "64", "32" }.Select(view => new Place($"Registry ({FileCat.Platform.Windows.WindowsRegistryProvider.ViewLabel(view)})",
                "Local Registry · keys and typed values", FileCat.Platform.Windows.WindowsRegistryProvider.Home(view),
                () => icons.GetPlaceIcon(IconKind.RegistryKey)) { Group = PlaceGroup.Collections }).ToList();
            places.Add(views[0] with { Variants = views.Skip(1).ToList() });
        }
        foreach (var (name, folder) in new[]
                 {
                     ("Home", Environment.SpecialFolder.UserProfile), ("Desktop", Environment.SpecialFolder.DesktopDirectory),
                     ("Documents", Environment.SpecialFolder.MyDocuments),
                 })
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(p)) places.Add(new Place(name, p, Location.FileSystem(p), FolderIcon(Location.FileSystem(p))) { Group = PlaceGroup.Folders });
        }
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) places.Add(new Place("Downloads", downloads, Location.FileSystem(downloads), FolderIcon(Location.FileSystem(downloads))) { Group = PlaceGroup.Folders });
        foreach (var b in Services.History.Bookmarks.Where(b => b.Location is not null).OrderBy(b => b.Slot ?? 99))
        {
            string display = Services.Providers.Display(b.Location!);
            // A bookmark set with Ctrl+Shift+digit is named by its whole path: on its button, its folder's name says enough.
            string name = string.IsNullOrEmpty(b.Name) || b.Name == display ? Services.Providers.For(b.Location!).GetDisplayName(b.Location!) : b.Name;
            places.Add(new Place((b.Slot is { } s ? $"[{s}] " : "★ ") + (string.IsNullOrEmpty(b.Name) ? display : b.Name), display, b.Location!, FolderIcon(b.Location!))
            {
                Group = PlaceGroup.Bookmarks,
                BarLabel = name,
                BarTip = b.Slot is { } slot ? $"Ctrl+{slot} opens it too" : null,
            });
        }
        foreach (var p in Services.Settings.RemoteProfiles)
        {
            string name = p.Name.Length > 0 ? p.Name : p.Display;
            places.Add(new Place(RemoteProtocols.Describe(p.Protocol).Split(' ', ':')[0] + ": " + name, p.Display + (p.InitialPath is { Length: > 0 } ip ? " · " + ip : ""), Remote.Sftp.SftpProvider.At(p, p.InitialPath),
                () => icons.GetPlaceIcon(IconKind.Server)) { Group = PlaceGroup.Servers, BarLabel = name });
        }
        places.Add(new Place("Connect to a server…", "SFTP, FTPS, or FTP: new or saved connection", null, () => icons.GetPlaceIcon(IconKind.Server))
        {
            Group = PlaceGroup.Servers,
            Connects = true,
            BarLabel = "Connect…",
        });
        return places;
    }

    /// <summary>
    /// The folder another panel shows on the drive <paramref name="root"/> (the active panel first, then the target, then
    /// the others), or null when none is there.
    /// </summary>
    private Location? FolderOnDrive(PanelViewModel panel, Location root)
    {
        var comparison = PathUtil.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var others = new[] { Workspace.ActivePanel, Workspace.ActiveTarget }.Concat(Workspace.Panels).OfType<PanelViewModel>().Distinct().Where(p => p != panel);
        foreach (var other in others)
        {
            if (other.ActiveTab?.Location is not { IsFileSystem: true } there) continue;
            string? otherRoot = PathUtil.IsWindows ? Path.GetPathRoot(there.Path) : UnixFiles.MountOf(there.Path)?.Name;
            if (otherRoot is not null && string.Equals(otherRoot.TrimEnd('\\', '/'), root.Path.TrimEnd('\\', '/'), comparison)) return there;
        }
        return null;
    }

    /// <summary>
    /// "SYSTEM · 214 GB free": the label (on Linux and macOS, where the label is the mount point, the file system
    /// instead), then free space; or why the drive says nothing.
    /// </summary>
    internal static string DriveDetail(DriveTag tag)
    {
        if (tag.DriveType is "Not responding" or "Unavailable") return tag.DriveType;
        if (!tag.Ready) return tag.DriveType == "CDRom" ? "No disc" : "Not ready";
        string name = !string.IsNullOrEmpty(tag.Label) && tag.Label != tag.RootPath ? tag.Label : tag.Format ?? tag.DriveType;
        return tag.FreeBytes >= 0 ? $"{name} · {Formatters.Size(tag.FreeBytes)} free" : name;
    }

    private async Task ShowFolderHistoryAsync(bool findFolder = false)
    {
        var entries = Services.History.Folders.Where(h => h.Location is not null)
            .OrderByDescending(h => h.Pinned).ThenByDescending(h => h.LastUsedUtc).ToList();
        var bookmarks = Services.History.Bookmarks.Where(b => b.Location is not null).ToList();
        var items = new List<ChoiceItem>();
        var locs = new List<Location>();
        foreach (var h in entries)
        {
            items.Add(new ChoiceItem(Services.Providers.Display(h.Location!), Formatters.Date(h.LastUsedUtc.ToUniversalTime().Ticks), h.Pinned ? "pinned" : null) { Pinned = h.Pinned });
            locs.Add(h.Location!);
        }
        int scanIndex = -1;
        if (findFolder)
        {
            foreach (var b in bookmarks)
            {
                items.Add(new ChoiceItem(Services.Providers.Display(b.Location!), "bookmark"));
                locs.Add(b.Location!);
            }
            if (ActiveTab?.Location is { IsFileSystem: true } here)
            {
                scanIndex = items.Count;
                items.Add(new ChoiceItem("Scan folders below this folder…", Services.Providers.Display(here), "bounded"));
                locs.Add(here);
            }
        }
        var r = await Dialogs.ChooseAsync(new ChoiceOptions(findFolder ? "Find folder (history, bookmarks, or a folder scan)" : "Folder history", items)
        {
            Hint = "Type to filter · Enter opens · Shift+Enter opens in the target panel · Insert pins · Ctrl+Del removes",
            AllowDelete = true,
            AllowPin = true,
        });
        ApplyHistoryEdits(Services.History.Folders, entries, r);
        if (r.Index < 0) return;
        if (r.Index == scanIndex)
        {
            await ScanFoldersAsync(locs[r.Index]);
            return;
        }
        var panel = r.Alternate ? RequireTarget() : Workspace.ActivePanel;
        panel?.ActiveTab?.Navigate(locs[r.Index]);
    }

    /// <summary>
    /// Find folder's explicit, bounded scan (plan §11): folders below <paramref name="root"/>, off the UI thread, at
    /// most <see cref="FolderScan.DefaultLimit"/> folders or five seconds, then a type-to-filter list.
    /// </summary>
    private async Task ScanFoldersAsync(Location root)
    {
        Notify($"Scanning folders below {Services.Providers.Display(root)}…");
        var (folders, stopped, unreadable) = await Task.Run(() => FolderScan.Run(root.Path, FolderScan.DefaultLimit, TimeSpan.FromSeconds(5)));
        if (folders.Count == 0)
        {
            Notify(stopped ? "The folder scan stopped before finding a folder." : "There are no folders below this one.");
            return;
        }
        var items = folders.Select(f => new ChoiceItem(Path.GetRelativePath(root.Path, f), null)).ToList();
        var scope = stopped ? $"the first {folders.Count:N0} found (the scan stopped at its limit)" : $"{folders.Count:N0}";
        var r = await Dialogs.ChooseAsync(new ChoiceOptions($"Folders below {Services.Providers.Display(root)}: {scope}", items)
        {
            Hint = "Type parts of the path · Enter opens · Shift+Enter opens in the target panel"
                + (unreadable > 0 ? $" · {Formatters.Plural(unreadable, "folder", "folders")} could not be read" : string.Empty),
        });
        if (r.Index < 0) return;
        var panel = r.Alternate ? RequireTarget() : Workspace.ActivePanel;
        panel?.ActiveTab?.Navigate(Location.FileSystem(folders[r.Index]));
    }


    private async Task ShowFileHistoryAsync()
    {
        var entries = Services.History.Files.Where(h => h.Location is not null && h.Name is not null)
            .OrderByDescending(h => h.Pinned).ThenByDescending(h => h.LastUsedUtc).ToList();
        var items = entries.Select(h => new ChoiceItem(h.Name!, Services.Providers.Display(h.Location!)) { Pinned = h.Pinned }).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("File history", items)
        {
            Hint = "Type to filter · Enter focuses the file · Insert pins · Ctrl+Del removes",
            AllowDelete = true,
            AllowPin = true,
        });
        ApplyHistoryEdits(Services.History.Files, entries, r);
        if (r.Index < 0) return;
        var h = entries[r.Index];
        ActiveTab?.Navigate(h.Location!, h.Name);
    }

    /// <summary>Applies pins and removals made in a history chooser (indices beyond the history are other rows).</summary>
    private void ApplyHistoryEdits(List<HistoryEntry> history, List<HistoryEntry> shown, ChoiceResult r)
    {
        foreach (int i in r.PinToggled)
        {
            if (i < shown.Count) shown[i].Pinned = !shown[i].Pinned;
        }
        foreach (var d in r.Deleted.OrderByDescending(i => i))
        {
            if (d < shown.Count) history.Remove(shown[d]);
        }
        if (r.PinToggled.Count > 0 || r.Deleted.Count > 0) Services.SaveHistory();
    }

    private async Task ShowBookmarksAsync()
    {
        var list = Services.History.Bookmarks.Where(b => b.Location is not null).OrderBy(b => b.Slot ?? 99).ThenBy(b => b.Name).ToList();
        var items = list.Select(b => new ChoiceItem((b.Slot is { } s ? $"[{s}] " : string.Empty) + b.Name, Services.Providers.Display(b.Location!), b.Slot is { } sl ? $"Ctrl+{sl}" : null)).ToList();
        items.Add(new ChoiceItem("+ Add current location", "Adds an unnumbered bookmark"));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Bookmarks", items)
        {
            Hint = "Enter opens · Shift+Enter opens in the target panel · Ctrl+Del removes · Ctrl+Shift+0–9 sets numbered slots, Ctrl+0–9 opens them, Alt+Shift+0–9 opens them in the target panel",
            AllowDelete = true,
        });
        foreach (var d in r.Deleted.OrderByDescending(i => i)) if (d < list.Count) Services.History.Bookmarks.Remove(list[d]);
        if (r.Deleted.Count > 0) Services.SaveHistory();
        if (r.Index < 0) return;
        if (r.Index == list.Count)
        {
            var loc = ActiveTab?.Location;
            if (loc is null) return;
            var name = await Dialogs.PromptAsync(new PromptOptions("Add bookmark", "Name:") { Text = Services.Providers.For(loc).GetDisplayName(loc) });
            if (name is null) return;
            Services.History.Bookmarks.Add(new BookmarkEntry { Name = name.Text, Location = loc });
            Services.SaveHistory();
            return;
        }
        var panel = r.Alternate ? RequireTarget() : Workspace.ActivePanel;
        panel?.ActiveTab?.Navigate(list[r.Index].Location!);
    }

    private async Task ShowTabListAsync()
    {
        var all = Workspace.Panels.SelectMany(p => p.Tabs.Select(t => (Panel: p, Tab: t))).ToList();
        var items = all.Select(x => new ChoiceItem(x.Tab.TabHeader, x.Tab.DisplayPath, $"panel {x.Panel.Number}")).ToList();
        int current = all.FindIndex(x => ReferenceEquals(x.Tab, ActiveTab));
        var closed = Workspace.RecentlyClosed.Where(s => s.Location is not null).ToList();
        items.AddRange(closed.Select(s => new ChoiceItem("↺ " + Services.Providers.For(s.Location!).GetDisplayName(s.Location!), Services.Providers.Display(s.Location!), "recently closed")));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Tabs", items) { SelectedIndex = Math.Max(0, current), Hint = "Type to filter · Enter switches · closed tabs reopen" });
        if (r.Index < 0) return;
        if (r.Index < all.Count)
        {
            var (p, t) = all[r.Index];
            p.ActiveTab = t;
            Workspace.Activate(p);
        }
        else
        {
            Workspace.ReopenClosed(r.Index - all.Count);
        }
        View.FocusActivePanel();
    }

    private async Task PickPanelAsync(bool focus)
    {
        var source = Workspace.ActivePanel;
        if (source is null) return;
        var candidates = Workspace.Panels.Where(p => focus || !ReferenceEquals(p, source)).ToList();
        if (candidates.Count == 0)
        {
            Notify("There is no other panel. Add one from the Panels menu.");
            return;
        }
        var items = candidates.Select(p => new ChoiceItem($"{p.Number}: {p.ActiveTab?.Title}", p.ActiveTab?.DisplayPath, p.IsActive ? "active" : p.IsTarget ? "target" : null)).ToList();
        // The current target is chosen: to keep it, or as the panel to go to (the active panel would change nothing).
        var target = Workspace.GetTarget(source);
        int preselected = target is null ? -1 : candidates.IndexOf(target);
        if (preselected < 0 && focus) preselected = (candidates.IndexOf(source) + 1) % candidates.Count;
        var r = await Dialogs.ChooseAsync(new ChoiceOptions(focus ? "Focus panel" : $"Target for panel {source.Number}", items)
        {
            Hint = focus ? "Type the panel number or filter · Enter focuses" : "The chosen panel receives F5/F6 from this panel",
            SelectedIndex = Math.Max(0, preselected),
        });
        if (r.Index < 0) return;
        var chosen = candidates[r.Index];
        if (focus) Workspace.Activate(chosen);
        else if (Workspace.Panels.Count <= 2) Notify("With two panels the target is always the other panel.");
        else
        {
            SetPanelTarget(source, chosen);
            return;
        }
        View.FocusActivePanel();
    }

    /// <summary>
    /// Makes <paramref name="target"/> the panel <paramref name="source"/> copies and moves to (three or more panels):
    /// Shift+F12, the active panel's "→ n" chip, or "Set as target" on the target itself. The keyboard stays in the active panel.
    /// </summary>
    public void SetPanelTarget(PanelViewModel source, PanelViewModel target)
    {
        if (Workspace.Panels.Count <= 2 || ReferenceEquals(source, target) || !Workspace.Panels.Contains(source) || !Workspace.Panels.Contains(target)) return;
        Workspace.SetTarget(source, target);
        Notify($"Panel {source.Number} now copies and moves to panel {target.Number} (F5, F6).");
        View.FocusActivePanel();
    }

    /// <summary>The command search in a dialog, for where the window shows no search box (plan §4.4).</summary>
    public async Task ShowPaletteAsync()
    {
        var entries = CommandSearchEntries();
        var indexes = entries.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var items = entries.Select(e => new ChoiceItem(e.Title, CommandSearch.Detail(e), e.Gesture, e.Id)).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Search commands", items)
        {
            Hint = "Type what you want to do (\"recover\", \"new folder\", \"zip\") · Enter runs · Esc closes",
            Search = q => CommandSearch.Rank(q, entries, Services.History.RecentCommands, int.MaxValue, allWhenEmpty: true)
                .Select(e => indexes[e.Id]).ToList(),
        });
        if (r.Index < 0) return;
        View.FocusActivePanel();
        RunFromSearch(entries[r.Index].Id);
    }

    /// <summary>
    /// Every command the search offers, with where it is in the menus and whether it applies here now. Numbered
    /// bookmark commands stay out (the bookmark list has them); column profiles show their names.
    /// </summary>
    internal IReadOnlyList<CommandSearch.Entry> CommandSearchEntries()
    {
        var entries = new List<CommandSearch.Entry>();
        foreach (var d in Services.Commands.All)
        {
            if (d.Id.StartsWith(CommandIds.BookmarkSetPrefix, StringComparison.Ordinal)
                || d.Id.StartsWith(CommandIds.BookmarkGoPrefix, StringComparison.Ordinal)
                || d.Id.StartsWith(CommandIds.BookmarkTargetPrefix, StringComparison.Ordinal))
                continue;
            string title = d.Title;
            if (d.Id.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal))
            {
                if (!int.TryParse(d.Id.AsSpan(CommandIds.ColumnProfilePrefix.Length), out int profile) || profile >= Services.Columns.Count) continue;
                title = "Columns: " + Services.Columns.NameOf(profile);
            }
            var availability = GetAvailability(d.Id);
            entries.Add(new CommandSearch.Entry(d.Id, title, d.Category, Services.Keymap.GetGestureText(d.Id),
                MainMenuModel.PathOf(d.Id), d.Keywords, availability.Enabled, availability.Reason, d.Description));
        }
        return entries;
    }

    /// <summary>Remembers a command run from the search, the palette, or a menu: the search lists it first when empty.</summary>
    public void RememberCommand(string id) => AppServices.RememberText(Services.History.RecentCommands, id, 20);

    /// <summary>Runs a command chosen in the search; one that applies here is remembered as recently used.</summary>
    internal void RunFromSearch(string id)
    {
        if (GetAvailability(id).Enabled) RememberCommand(id);
        Execute(id);
    }

    private async Task ShowKeyboardReferenceAsync()
    {
        // After a shortcut changes, the reference opens again on that command with the new shortcut shown.
        string? selected = null;
        while (true)
        {
            var choice = await Dialogs.KeyboardReferenceAsync(KeyboardHelpEntries(), selected);
            if (choice is null) return;
            if (choice.ChangeShortcut)
            {
                await ChangeShortcutAsync(choice.CommandId);
                selected = choice.CommandId;
                continue;
            }
            View.FocusActivePanel();
            await ExecuteAsync(choice.CommandId);
            return;
        }
    }

    private KeyboardHelpEntry[] KeyboardHelpEntries()
    {
        return Services.Commands.All
            .Where(d => !d.Id.StartsWith(CommandIds.BookmarkSetPrefix, StringComparison.Ordinal)
                && !d.Id.StartsWith(CommandIds.BookmarkGoPrefix, StringComparison.Ordinal)
                && !d.Id.StartsWith(CommandIds.BookmarkTargetPrefix, StringComparison.Ordinal)
                && !d.Id.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal))
            .Select(d =>
            {
                var availability = GetAvailability(d.Id);
                return new KeyboardHelpEntry(d.Id, d.Title, d.Category,
                    Services.Keymap.GetGestureText(d.Id), d.Description, availability.Enabled, availability.Reason);
            })
            .ToArray();
    }
}

/// <summary>The groups of places, in the location menu's order; the place buttons leave a gap between groups.</summary>
public enum PlaceGroup { Drives, Devices, Collections, Folders, Bookmarks, Servers }

/// <summary>
/// A place the location menu (Alt+F1, Alt+F2) offers and the place buttons above each panel show (D-52, D-53).
/// <see cref="Location"/> is null only for a new connection.
/// </summary>
public sealed record Place(string Title, string? Detail, Location? Location, Func<Avalonia.Media.IImage?> Icon)
{
    public PlaceGroup Group { get; init; }

    /// <summary>The drive it is, which opens at the folder another panel shows there.</summary>
    public DriveTag? Drive { get; init; }

    /// <summary>The drive letter that opens it at once in the menu.</summary>
    public char? Letter { get; init; }

    /// <summary>What its button says beside the icon, where the icon alone would not tell it apart (a drive's letter, a bookmark's name).</summary>
    public string? BarLabel { get; init; }

    /// <summary>More for its button's tip (a bookmark's key).</summary>
    public string? BarTip { get; init; }

    /// <summary>Other views of the place (the Registry's 64-bit and 32-bit views): listed after it in the menu, on its button's menu.</summary>
    public IReadOnlyList<Place> Variants { get; init; } = [];

    /// <summary>Opens the connection dialog instead of a place.</summary>
    public bool Connects { get; init; }
}
