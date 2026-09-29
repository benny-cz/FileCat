using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

/// <summary>Type-to-filter lists: location menu, histories, bookmarks, tabs, panels, palette (plan §11, §4.4).</summary>
public sealed partial class MainViewModel
{
    private async Task ShowLocationMenuAsync(PanelViewModel? panel)
    {
        if (panel?.ActiveTab is null) return;
        var items = new List<ChoiceItem>();
        var locations = new List<Location>();
        var icons = Services.Icons;
        void Add(string title, string? detail, Location loc, Func<Avalonia.Media.IImage?> icon)
        {
            items.Add(new ChoiceItem(title, detail) { Icon = icon });
            locations.Add(loc);
        }
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
        // The drives This PC lists, as each describes itself within a moment: a dropped network drive shows as not
        // responding instead of holding up the menu (the queries run off the UI thread).
        var computer = Services.Providers.For(new Location(Schemes.Computer, string.Empty)) as ComputerProvider;
        var drives = computer is null ? [] : await computer.QueryDrivesAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);
        foreach (var tag in drives)
        {
            string name = PathUtil.IsWindows ? tag.RootPath.TrimEnd('\\') : tag.RootPath;
            var drive = new EntryData(name, EntryKind.Drive) { Tag = tag };
            Add(name, DriveDetail(tag), Location.FileSystem(tag.RootPath), () => icons.GetIcon(drive));
        }
        var thisPc = new Location(Schemes.Computer, string.Empty); // "This PC" on Windows, "Computer" elsewhere
        Add(Services.Providers.Display(thisPc), "All drives", thisPc, () => icons.GetPlaceIcon(IconKind.Computer));
        if (Services.Providers.IsRegistered(Schemes.Mtp))
            Add("Phones and cameras", "Portable devices over MTP (unlock a phone and choose File transfer)", FileCat.Platform.Windows.Mtp.MtpProvider.Devices,
                () => icons.GetPlaceIcon(IconKind.Phone));
        int workingSets = Services.WorkingSets.All.Count;
        Add("Working sets", workingSets == 0 ? "Collect items from many folders (references, never copies)" : $"{Formatters.Plural(workingSets, "set", "sets")} of items collected from many folders",
            Core.Search.ResultSetProvider.WorkingSetList, () => icons.GetPlaceIcon(IconKind.Collection));
        if (Services.Providers.IsRegistered(Schemes.Registry))
        {
            foreach (var view in new[] { "default", "64", "32" })
                Add($"Registry ({FileCat.Platform.Windows.WindowsRegistryProvider.ViewLabel(view)})",
                    "Local Registry · keys and typed values", FileCat.Platform.Windows.WindowsRegistryProvider.Home(view),
                    () => icons.GetPlaceIcon(IconKind.RegistryKey));
        }
        foreach (var (name, folder) in new[]
                 {
                     ("Home", Environment.SpecialFolder.UserProfile), ("Desktop", Environment.SpecialFolder.DesktopDirectory),
                     ("Documents", Environment.SpecialFolder.MyDocuments),
                 })
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(p)) Add(name, p, Location.FileSystem(p), FolderIcon(Location.FileSystem(p)));
        }
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) Add("Downloads", downloads, Location.FileSystem(downloads), FolderIcon(Location.FileSystem(downloads)));
        foreach (var b in Services.History.Bookmarks.Where(b => b.Location is not null).OrderBy(b => b.Slot ?? 99))
            Add((b.Slot is { } s ? $"[{s}] " : "★ ") + (string.IsNullOrEmpty(b.Name) ? Services.Providers.Display(b.Location!) : b.Name), Services.Providers.Display(b.Location!), b.Location!,
                FolderIcon(b.Location!));
        foreach (var p in Services.Settings.RemoteProfiles)
            Add(RemoteProtocols.Describe(p.Protocol).Split(' ', ':')[0] + ": " + (p.Name.Length > 0 ? p.Name : p.Display), p.Display + (p.InitialPath is { Length: > 0 } ip ? " · " + ip : ""), Remote.Sftp.SftpProvider.At(p, p.InitialPath),
                () => icons.GetPlaceIcon(IconKind.Server));
        int connectIndex = items.Count;
        items.Add(new ChoiceItem("Connect to a server…", "SFTP, FTPS, or FTP: new or saved connection") { Icon = () => icons.GetPlaceIcon(IconKind.Server) });
        var r = await Dialogs.ChooseAsync(new ChoiceOptions($"Location for panel {panel.Number}", items)
        {
            Hint = "Type to filter · Enter opens · Shift+Enter opens in a new tab",
            Icons = icons,
        });
        if (r.Index < 0) return;
        if (r.Index == connectIndex)
        {
            await ConnectSftpAsync(panel);
            return;
        }
        if (r.Alternate) panel.OpenTab(locations[r.Index]);
        else panel.ActiveTab?.Navigate(locations[r.Index]);
        Workspace.Activate(panel);
        View.FocusActivePanel();
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
            items.Add(new ChoiceItem(Services.Providers.Display(h.Location!), h.LastUsedUtc.ToLocalTime().ToString("g"), h.Pinned ? "pinned" : null) { Pinned = h.Pinned });
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
        var r = await Dialogs.ChooseAsync(new ChoiceOptions(focus ? "Focus panel" : $"Target for panel {source.Number}", items)
        {
            Hint = focus ? "Type the panel number or filter · Enter focuses" : "The chosen panel receives F5/F6 from this panel",
        });
        if (r.Index < 0) return;
        var chosen = candidates[r.Index];
        if (focus) Workspace.Activate(chosen);
        else if (Workspace.Panels.Count <= 2) Notify("With two panels the target is always the other panel.");
        else
        {
            Workspace.SetTarget(source, chosen);
            Notify($"Panel {source.Number} now targets panel {chosen.Number}.");
        }
        View.FocusActivePanel();
    }

    public async Task ShowPaletteAsync()
    {
        var defs = Services.Commands.All
            .Where(d => !d.Id.StartsWith(CommandIds.BookmarkSetPrefix, StringComparison.Ordinal) && !d.Id.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal))
            .ToList();
        var items = defs.Select(d =>
        {
            var a = GetAvailability(d.Id);
            return new ChoiceItem(d.Title, a.Enabled ? d.Category : $"{d.Category} · unavailable: {a.Reason}", Services.Keymap.GetGestureText(d.Id), d.Id);
        }).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Command palette", items) { Hint = "Type to filter commands · Enter runs" });
        if (r.Index < 0) return;
        View.FocusActivePanel();
        await ExecuteAsync(defs[r.Index].Id);
    }

    private async Task ShowKeyboardReferenceAsync()
    {
        var commands = Services.Commands.All
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
        var id = await Dialogs.KeyboardReferenceAsync(commands);
        if (id is null) return;
        View.FocusActivePanel();
        await ExecuteAsync(id);
    }
}
