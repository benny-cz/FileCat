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
        CommandIds.Copy, CommandIds.Move, CommandIds.Rename, CommandIds.Duplicate, CommandIds.Delete, CommandIds.DeletePermanent, "-",
        CommandIds.CopyToClipboard, CommandIds.CutToClipboard, CommandIds.PasteFromClipboard, CommandIds.CopyPaths, CommandIds.CopyNames, "-",
        CommandIds.OpenInNewTab, CommandIds.OpenInTarget, CommandIds.Reveal, CommandIds.OpenTerminal, "-", CommandIds.Checksum, CommandIds.Properties,
    ];

    public static ContextMenu Build(MainViewModel vm)
    {
        var menu = new ContextMenu();
        var list = new List<Control>();
        foreach (var id in Items)
        {
            if (id == "-")
            {
                if (list.Count > 0 && list[^1] is not Separator) list.Add(new Separator());
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
        menu.ItemsSource = list;
        return menu;
    }
}
