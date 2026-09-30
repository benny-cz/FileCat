using Avalonia.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;

namespace FileCat.App.Views;

/// <summary>FileCat's own item context menu. Shell context menus arrive only via the out-of-process host (TV-16).</summary>
public static class ContextMenuFactory
{
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
