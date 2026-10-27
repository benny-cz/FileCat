using Avalonia.Controls;
using Avalonia.Automation;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Views;

/// <summary>FileCat's own item context menu. Shell context menus arrive only via the out-of-process host (TV-16).</summary>
public static class ContextMenuFactory
{
    /// <summary>Location actions only; a focused row or Commander mark never becomes an implicit target.</summary>
    public static ContextMenu BuildBackground(MainViewModel vm, PanelViewModel panel, TabViewModel tab, Location location)
    {
        var provider = vm.Services.Providers.For(location);
        var caps = provider.GetCapabilities(location);
        string? unavailable = MainViewModel.LocationWriteBlockReason(tab, location);
        bool canPaste = unavailable is null && location.IsFileSystem && (caps & LocationCapabilities.TransferTarget) != 0;
        bool canCreate = location.Scheme is Schemes.FileSystem or Schemes.Zip or Schemes.Sftp or Schemes.Mtp &&
                         (caps & LocationCapabilities.CreateDirectory) != 0 && unavailable is null;
        var items = new List<Control>();
        void Add(string id, bool enabled, string? reason = null)
        {
            var def = vm.Services.Commands.Get(id)!;
            var item = new MenuItem { Header = def.Title, Icon = MenuIconFactory.Create(id), IsEnabled = enabled };
            var chord = vm.Services.Keymap.GetChords(id).FirstOrDefault();
            if (chord.Key is not null && KeyMapper.ToGesture(chord) is { } gesture) item.InputGesture = gesture;
            if (!enabled)
            {
                ToolTip.SetTip(item, reason);
                ToolTip.SetShowOnDisabled(item, true);
                AutomationProperties.SetHelpText(item, reason);
            }
            item.Click += async (_, _) => await vm.ExecuteLocationContextAsync(id, panel, tab, location);
            items.Add(item);
        }
        Add(CommandIds.PasteFromClipboard, canPaste,
            unavailable ?? (location.IsFileSystem ? provider.ExplainUnavailable(location, LocationCapabilities.TransferTarget)
                : "Paste items into a file-system folder."));
        Add(CommandIds.MakeDirectory, canCreate,
            unavailable ?? ((caps & LocationCapabilities.CreateDirectory) == 0
                ? provider.ExplainUnavailable(location, LocationCapabilities.CreateDirectory)
                : "Create a folder in a file-system, ZIP, SFTP, or device location."));
        items.Add(new Separator());
        Add(CommandIds.Refresh, true);
        if (location.IsFileSystem) Add(CommandIds.OpenTerminal, true);
        var copy = new MenuItem
        {
            Header = location.IsFileSystem ? "Copy location path" : "Copy location name",
            Icon = MenuIconFactory.Create(CommandIds.CopyPaths),
        };
        copy.Click += async (_, _) => await vm.ExecuteLocationContextAsync("location.copyPath", panel, tab, location);
        items.Add(copy);
        return new ContextMenu { ItemsSource = items };
    }

    private static readonly string[] Items =
    [
        CommandIds.Open, CommandIds.OpenWithSystem, CommandIds.View, CommandIds.Edit, "-",
        CommandIds.Copy, CommandIds.Move, CommandIds.Rename, CommandIds.Delete, "-",
        CommandIds.CutToClipboard, CommandIds.CopyToClipboard, CommandIds.PasteFromClipboard, "-",
        CommandIds.OpenInTarget, CommandIds.Reveal, CommandIds.CopyPaths, "-",
        CommandIds.Properties, CommandIds.HiddenData, CommandIds.FileRecord, "more",
    ];

    private static readonly string[] MoreItems =
    [
        CommandIds.Duplicate, CommandIds.DeletePermanent, "-",
        CommandIds.CopyNames, CommandIds.OpenInNewTab, CommandIds.OpenTerminal, CommandIds.Checksum, CommandIds.VerifyChecksums,
    ];

    public static ContextMenu Build(MainViewModel vm)
    {
        var items = BuildItems(vm, Items);
        // A drive or a disk image: its deleted files, straight from here (plan §17), above Properties.
        if (vm.RecoveryOfferForFocus() is { } recovery)
        {
            var recover = new MenuItem { Header = recovery, Icon = MenuIconFactory.Create(CommandIds.FindDeleted) };
            recover.Click += (_, _) => vm.RecoverFromFocus();
            string? properties = vm.Services.Commands.Get(CommandIds.Properties)?.Title;
            int at = items.FindIndex(c => c is MenuItem { Header: string h } && h == properties);
            if (at < 0) items.AddRange([new Separator(), recover]);
            else items.InsertRange(at, [recover, new Separator()]);
        }
        // Beside checksum files or signatures (D-57): checking a file against them is one step away, above Properties.
        if (vm.ActiveTab is { HasSidecars: true } tab && tab.Listing.TryGetFocused(out var focused) && focused.Kind == Core.Resources.EntryKind.File
            && BuildItems(vm, [CommandIds.VerifyChecksums]) is [MenuItem verify])
        {
            string? properties = vm.Services.Commands.Get(CommandIds.Properties)?.Title;
            int at = items.FindIndex(c => c is MenuItem { Header: string h } && h == properties);
            if (at < 0) items.AddRange([new Separator(), verify]);
            else items.Insert(at, verify);
        }
        return new ContextMenu { ItemsSource = items };
    }

    private static List<Control> BuildItems(MainViewModel vm, IReadOnlyList<string> items)
    {
        var list = new List<Control>();
        foreach (var id in items)
        {
            if (id == "-")
            {
                if (list.Count > 0 && list[^1] is not Separator) list.Add(new Separator());
                continue;
            }
            if (id == "more")
            {
                var extra = BuildItems(vm, MoreItems);
                if (extra.Count > 0)
                    list.Add(new MenuItem { Header = "More FileCat actions", Icon = MenuIconFactory.Create("menu.more"), ItemsSource = extra });
                continue;
            }
            var def = vm.Services.Commands.Get(id);
            if (def is null) continue;
            var a = vm.GetAvailability(id);
            if (!a.Enabled) continue;
            var mi = new MenuItem { Header = def.Title, Icon = MenuIconFactory.Create(id) };
            var chord = vm.Services.Keymap.GetChords(id).FirstOrDefault();
            if (chord.Key is not null && KeyMapper.ToGesture(chord) is { } g) mi.InputGesture = g;
            mi.Click += (_, _) => vm.Execute(id);
            list.Add(mi);
        }
        if (list.Count > 0 && list[^1] is Separator) list.RemoveAt(list.Count - 1);
        return list;
    }
}
