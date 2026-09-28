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
        CommandIds.Properties, "more",
    ];

    private static readonly string[] MoreItems =
    [
        CommandIds.Duplicate, CommandIds.DeletePermanent, "-",
        CommandIds.CopyNames, CommandIds.OpenInNewTab, CommandIds.OpenTerminal, CommandIds.Checksum,
    ];

    public static ContextMenu Build(MainViewModel vm)
    {
        return new ContextMenu { ItemsSource = BuildItems(vm, Items) };
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
