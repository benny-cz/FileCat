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
        void Add(string title, string? detail, Location loc)
        {
            items.Add(new ChoiceItem(title, detail) { });
            locations.Add(loc);
        }
        foreach (var d in SafeDrives())
        {
            var root = d.Name;
            string detail;
            try { detail = d.IsReady ? $"{(string.IsNullOrEmpty(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel)} · {Formatters.Size(d.AvailableFreeSpace)} free" : $"{d.DriveType} · not ready"; }
            catch (Exception) { detail = d.DriveType.ToString(); }
            Add(PathUtil.IsWindows ? root.TrimEnd('\\') : root, detail, Location.FileSystem(root));
        }
        Add("This PC", "All drives", new Location(Schemes.Computer, string.Empty));
        foreach (var (name, folder) in new[]
                 {
                     ("Home", Environment.SpecialFolder.UserProfile), ("Desktop", Environment.SpecialFolder.DesktopDirectory),
                     ("Documents", Environment.SpecialFolder.MyDocuments),
                 })
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(p)) Add(name, p, Location.FileSystem(p));
        }
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) Add("Downloads", downloads, Location.FileSystem(downloads));
        foreach (var b in Services.History.Bookmarks.Where(b => b.Location is not null).OrderBy(b => b.Slot ?? 99))
            Add((b.Slot is { } s ? $"[{s}] " : "★ ") + (string.IsNullOrEmpty(b.Name) ? Services.Providers.Display(b.Location!) : b.Name), Services.Providers.Display(b.Location!), b.Location!);
        var r = await Dialogs.ChooseAsync(new ChoiceOptions($"Location for panel {panel.Number}", items)
        {
            Hint = "Type to filter · Enter opens · Shift+Enter opens in a new tab",
        });
        if (r.Index < 0) return;
        if (r.Alternate) panel.OpenTab(locations[r.Index]);
        else panel.ActiveTab?.Navigate(locations[r.Index]);
        Workspace.Activate(panel);
        View.FocusActivePanel();
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => PathUtil.IsWindows || d.Name == "/" || d.Name.StartsWith("/media", StringComparison.Ordinal) || d.Name.StartsWith("/Volumes", StringComparison.Ordinal) || d.Name.StartsWith("/mnt", StringComparison.Ordinal)).ToList(); }
        catch (Exception) { return []; }
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
        if (findFolder)
        {
            foreach (var b in bookmarks)
            {
                items.Add(new ChoiceItem(Services.Providers.Display(b.Location!), "bookmark"));
                locs.Add(b.Location!);
            }
        }
        var r = await Dialogs.ChooseAsync(new ChoiceOptions(findFolder ? "Find folder (history and bookmarks)" : "Folder history", items)
        {
            Hint = "Type to filter · Enter opens · Shift+Enter opens in the target panel · Del removes",
            AllowDelete = true,
        });
        foreach (var d in r.Deleted.OrderByDescending(i => i))
        {
            if (d < entries.Count) Services.History.Folders.Remove(entries[d]);
        }
        if (r.Index < 0) return;
        var panel = r.Alternate ? RequireTarget() : Workspace.ActivePanel;
        panel?.ActiveTab?.Navigate(locs[r.Index]);
    }

    private async Task ShowFileHistoryAsync()
    {
        var entries = Services.History.Files.Where(h => h.Location is not null && h.Name is not null)
            .OrderByDescending(h => h.Pinned).ThenByDescending(h => h.LastUsedUtc).ToList();
        var items = entries.Select(h => new ChoiceItem(h.Name!, Services.Providers.Display(h.Location!)) { Pinned = h.Pinned }).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("File history", items)
        {
            Hint = "Type to filter · Enter focuses the file · Del removes",
            AllowDelete = true,
        });
        foreach (var d in r.Deleted.OrderByDescending(i => i)) Services.History.Files.Remove(entries[d]);
        if (r.Index < 0) return;
        var h = entries[r.Index];
        ActiveTab?.Navigate(h.Location!, h.Name);
    }

    private async Task ShowBookmarksAsync()
    {
        var list = Services.History.Bookmarks.Where(b => b.Location is not null).OrderBy(b => b.Slot ?? 99).ThenBy(b => b.Name).ToList();
        var items = list.Select(b => new ChoiceItem((b.Slot is { } s ? $"[{s}] " : string.Empty) + b.Name, Services.Providers.Display(b.Location!), b.Slot is { } sl ? $"Ctrl+{sl}" : null)).ToList();
        items.Add(new ChoiceItem("+ Add current location", "Adds an unnumbered bookmark"));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Bookmarks", items)
        {
            Hint = "Enter opens · Shift+Enter opens in the target panel · Del removes · Ctrl+Shift+0–9 sets numbered slots",
            AllowDelete = true,
        });
        foreach (var d in r.Deleted.OrderByDescending(i => i)) if (d < list.Count) Services.History.Bookmarks.Remove(list[d]);
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
