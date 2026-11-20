using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Views;

public partial class MainWindow
{
    private ContextMenu? _historyMenu;
    internal ContextMenu? HistoryMenu => _historyMenu;

    private static bool IsHistoryCommand(string id) => id is CommandIds.Back or CommandIds.Forward;

    private RichTip HistoryToolbarTip(string id, string title) => new(title,
        _vm.Services.Keymap.GetGestureText(id),
        (_vm.Services.Commands.Get(id)?.Description ?? "") + " Right-click or press Shift+F10 for history.");

    private StackPanel BuildHistoryToolbarGroup(Button primary, Border primaryWrapper, string id)
    {
        primary.Focusable = true;
        primary.Classes.Add("historyPrimary");
        var dropDown = new Button { Classes = { "historyDropdown" }, Tag = id, Content = "▾", Focusable = true };
        var wrapper = new Border { Child = dropDown, Background = Brushes.Transparent };
        _toolbarButtons.Add((dropDown, wrapper, id));
        UpdateToolbarButton(dropDown, wrapper, id);
        primary.ContextRequested += (_, e) => { e.Handled = true; OpenHistoryMenu(primary, id); };
        dropDown.ContextRequested += (_, e) => { e.Handled = true; OpenHistoryMenu(dropDown, id); };
        dropDown.Click += (_, _) => OpenHistoryMenu(dropDown, id);
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { primaryWrapper, wrapper } };
    }

    private bool TryOpenHistoryFromKey(KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is not Button { Tag: string id } button || !IsHistoryCommand(id) ||
            (e.Key != Key.Apps || e.KeyModifiers != KeyModifiers.None) &&
            (e.Key != Key.F10 || e.KeyModifiers != KeyModifiers.Shift)) return false;
        e.Handled = true;
        OpenHistoryMenu(button, id);
        return true;
    }

    private void OpenHistoryMenu(Button opener, string id)
    {
        if (!opener.IsEnabled || _dialogs.IsOpen || _vm.Workspace.ActivePanel is not { ActiveTab: { } tab } panel) return;
        bool forward = id == CommandIds.Forward;
        var locations = tab.HistoryLocations(forward);
        if (locations.Length == 0) return;
        _historyMenu?.Close();
        long revision = tab.HistoryRevision;
        long generation = tab.NavigationGeneration;
        var origin = tab.Location;
        bool Current() => ReferenceEquals(_vm.Workspace.ActivePanel, panel) && ReferenceEquals(panel.ActiveTab, tab) &&
            tab.HistoryRevision == revision && tab.NavigationGeneration == generation && tab.Location == origin;

        var menu = new ContextMenu { Placement = PlacementMode.Bottom, MaxHeight = 420 };
        var items = locations.Select((location, i) =>
        {
            var providers = tab.Services.Providers;
            string path = Formatters.SafeName(providers.IsRegistered(location.Scheme) ? providers.Display(location) : location.ToString());
            var item = new MenuItem
            {
                Header = new TextBlock { Text = path, MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis },
                Icon = new Image { Source = tab.Services.Icons.GetPlaceIcon(HistoryIcon(location)), Width = 16, Height = 16, IsHitTestVisible = false },
            };
            AutomationProperties.SetName(item, path);
            ToolTip.SetTip(item, path);
            item.Click += (_, _) =>
            {
                if (!Current()) return;
                menu.Close();
                FocusActivePanel();
                tab.JumpHistory(forward, i + 1, revision);
            };
            return item;
        }).ToArray();
        menu.ItemsSource = items;
        AutomationProperties.SetName(menu, forward ? "Forward history" : "Back history");
        _historyMenu = menu;
        void NavigationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!Current()) menu.Close();
        }
        tab.PropertyChanged += NavigationChanged;
        menu.Closed += (_, _) =>
        {
            tab.PropertyChanged -= NavigationChanged;
            if (ReferenceEquals(_historyMenu, menu)) _historyMenu = null;
            _vm.ReconcileKeyBar(KeyMods.None);
        };
        menu.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            menu.Close();
            if (opener.IsEnabled) opener.Focus();
        }, RoutingStrategies.Tunnel);
        menu.Open(opener);
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_historyMenu, menu) && menu.IsOpen) items[0].Focus();
        }, DispatcherPriority.Input);
    }

    private static IconKind HistoryIcon(Location location) => location.Scheme switch
    {
        Schemes.FileSystem when PathUtil.IsUncPath(location.Path) => IconKind.NetworkDrive,
        Schemes.FileSystem when location.Path == Path.GetPathRoot(location.Path) => IconKind.Drive,
        Schemes.Computer => IconKind.Computer,
        Schemes.Network => location.Path.Length == 0 ? IconKind.Network : IconKind.Server,
        Schemes.Sftp or Schemes.Ftp => IconKind.Server,
        Schemes.Archive or Schemes.Zip => IconKind.Archive,
        Schemes.Recovery => IconKind.Drive,
        Schemes.Mtp => IconKind.Phone,
        Schemes.Registry => IconKind.RegistryKey,
        Schemes.ResultSet => IconKind.Collection,
        Schemes.HiddenData => IconKind.Stream,
        Schemes.Journal => IconKind.Document,
        Schemes.RecycleBin => IconKind.RecycleBin,
        _ => IconKind.Folder,
    };
}
