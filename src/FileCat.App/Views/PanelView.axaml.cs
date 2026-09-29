using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

public partial class PanelView : UserControl
{
    private static ContextMenu? _openItemMenu;
    private static long _menuRequest;
    private static bool _nativeMenuWarningShown;

    public PanelView()
    {
        InitializeComponent();
        List.ActivateRequested += (_, _) => Activated?.Invoke();
        List.OpenRequested += (_, _) => OpenRequested?.Invoke();
        List.MiddleClickRequested += (_, row) => MiddleClick?.Invoke(row);
        List.ContextMenuRequested += (_, point) => ShowContextMenu(point: point);
        List.GotFocus += (_, _) => Activated?.Invoke();
        // The number, or the tab strip beside the tabs, moves the whole panel.
        NumberBadge.PointerPressed += (_, e) =>
        {
            MoveRequested?.Invoke(e, NumberBadge);
            e.Handled = true;
        };
        TabHeader.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is null) MoveRequested?.Invoke(e, TabHeader);
        };
        PathBox.GotFocus += (_, _) =>
        {
            Activated?.Invoke();
            Dispatcher.UIThread.Post(PathBox.SelectAll, DispatcherPriority.Input);
        };
        // Typing a path suggests the folders that complete it (not while the box shows where the panel is); its keys
        // come first in OnPathKeyDown.
        _completion = new PathCompletion(PathBox, AddressRow, () => Panel?.Services.Settings.ShowHidden == true, () => Panel?.ActiveTab?.DisplayPath,
            handleKeys: false);
        _completion.Chosen += path => PathSubmitted?.Invoke(path);
        PathBox.LostFocus += (_, _) => RevertPath();
        PathBox.AddHandler(KeyDownEvent, OnPathKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            HookQuickView();
            ShowActiveTab();
        };
        // Resizing the panel or adding tabs can make the tabs fit, or not.
        TabScroller.ScrollChanged += (_, _) => UpdateTabOverflow();
    }

    private PanelViewModel? _hookedPanel;
    private PanelViewModel? _hookedSource;

    /// <summary>Keeps the quick-view pane attached to the source panel's current tab.</summary>
    private void HookQuickView()
    {
        if (_hookedPanel is not null) _hookedPanel.PropertyChanged -= OnPanelPropertyChanged;
        _hookedPanel = Panel;
        if (_hookedPanel is not null) _hookedPanel.PropertyChanged += OnPanelPropertyChanged;
        AttachQuickView();
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.QuickViewSource)) AttachQuickView();
        else if (e.PropertyName == nameof(PanelViewModel.ActiveTab)) ShowActiveTab();
    }

    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.ActiveTab)) QuickView.Attach(_hookedSource?.ActiveTab);
    }

    /// <summary>The active tab stays in sight when the tabs do not all fit (a new tab opens at the end).</summary>
    private void ShowActiveTab()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Panel?.ActiveTab is { } tab && TabStrip.ContainerFromItem(tab) is { } container) container.BringIntoView();
            UpdateTabOverflow();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateTabOverflow() => TabOverflow.IsVisible = TabScroller.Extent.Width > TabScroller.Viewport.Width + 1;

    private void OnTabOverflowClick(object? sender, RoutedEventArgs e)
    {
        Activated?.Invoke();
        if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel vm) vm.Execute(Core.Commands.CommandIds.TabList);
    }

    /// <summary>Whether the tab strip offers its "⋯" button, and whether the active tab is in sight (tests).</summary>
    internal (bool Overflowing, bool ActiveTabVisible) TabStripState()
    {
        bool visible = Panel?.ActiveTab is { } tab && TabStrip.ContainerFromItem(tab) is { } c &&
                       c.TranslatePoint(new Avalonia.Point(0, 0), TabScroller) is { } p && p.X >= -0.5 && p.X + c.Bounds.Width <= TabScroller.Viewport.Width + 0.5;
        return (TabOverflow.IsVisible, visible);
    }

    private void AttachQuickView()
    {
        if (_hookedSource is not null) _hookedSource.PropertyChanged -= OnSourcePropertyChanged;
        _hookedSource = Panel?.QuickViewSource;
        if (_hookedSource is not null) _hookedSource.PropertyChanged += OnSourcePropertyChanged;
        QuickView.Attach(_hookedSource?.ActiveTab);
    }

    public event Action? Activated;
    public event Action? OpenRequested;

    /// <summary>A press on the panel's number or tab strip that may start moving the panel (and the element pressed).</summary>
    public event Action<PointerPressedEventArgs, Control>? MoveRequested;
    public event Action<string>? PathSubmitted;
    public event Action? LocationMenuRequested;
    public event Action<int>? MiddleClick;

    private PanelViewModel? Panel => DataContext as PanelViewModel;

    public void FocusPathBox()
    {
        PathBox.Focus();
        PathBox.SelectAll();
    }

    private void RevertPath() => PathBox.Text = Panel?.ActiveTab?.DisplayPath;

    private readonly PathCompletion _completion;

    /// <summary>The folders suggested for what is typed; empty while none are shown (tests).</summary>
    internal IReadOnlyList<string> PathSuggestionsShown => _completion.Shown;

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (_completion.HandleKey(e))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var text = PathBox.Text ?? string.Empty;
            PathSubmitted?.Invoke(text);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RevertPath();
            List.Focus();
        }
        else if (e.Key == Key.Down && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            e.Handled = true;
            RevertPath();
            List.Focus();
        }
    }

    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TabViewModel tab } && Panel is { } p)
        {
            p.ActiveTab = tab;
            Activated?.Invoke();
            List.Focus();
        }
    }

    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Button { Tag: TabViewModel tab } button || Panel is not { } p) return;
        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            p.CloseTab(tab);
            e.Handled = true;
        }
        else if (e.InitialPressMouseButton == MouseButton.Right)
        {
            BuildTabMenu(p, tab).Open(button);
            e.Handled = true;
        }
    }

    /// <summary>Tab operations following Total Commander (plan §4.1).</summary>
    private ContextMenu BuildTabMenu(PanelViewModel panel, TabViewModel tab)
    {
        var ws = panel.Workspace;
        var items = new List<Control>();
        void Add(string header, string icon, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, Icon = MenuIconFactory.Create(icon), IsEnabled = enabled };
            mi.Click += (_, _) =>
            {
                action();
                List.Focus();
            };
            items.Add(mi);
        }
        Add("Close tab", "tab.close", () => panel.CloseTab(tab), panel.Tabs.Count > 1);
        Add("Close other tabs", "tab.closeOthers", () =>
        {
            foreach (var t in panel.Tabs.Where(t => !ReferenceEquals(t, tab) && !t.IsLocked).ToList()) panel.CloseTab(t);
        }, panel.Tabs.Count > 1);
        Add("Duplicate tab", "tab.duplicate", () =>
        {
            if (tab.Location is null) return;
            var dup = panel.OpenTab(tab.Location);
            var s = tab.ToState();
            s.Locked = false;
            dup.ApplyState(s);
        });
        items.Add(new Separator());
        Add(tab.IsLocked && !tab.ReturnToRoot ? "Unlock tab" : "Lock tab (navigation opens new tabs)", "tab.lock", () =>
        {
            bool lockIt = !(tab.IsLocked && !tab.ReturnToRoot);
            tab.IsLocked = lockIt;
            tab.ReturnToRoot = false;
            tab.LockedRoot = lockIt ? tab.Location : null;
        });
        Add(tab.IsLocked && tab.ReturnToRoot ? "Unlock tab" : "Lock tab at root (returns here when revisited)", "tab.lock", () =>
        {
            bool lockIt = !(tab.IsLocked && tab.ReturnToRoot);
            tab.IsLocked = lockIt;
            tab.ReturnToRoot = lockIt;
            tab.LockedRoot = lockIt ? tab.Location : null;
        });
        items.Add(new Separator());
        var target = ws.GetTarget(panel);
        Add(target is null ? "Move tab to target panel (no target)" : $"Move tab to panel {target.Number}", "tab.moveTarget", () =>
        {
            if (target is null || panel.Tabs.Count <= 1) return;
            target.AttachTab(panel.DetachTab(tab));
            ws.Activate(target);
        }, target is not null && panel.Tabs.Count > 1);
        Add(target is null ? "Copy tab to target panel (no target)" : $"Copy tab to panel {target.Number}", "tab.copyTarget", () =>
        {
            if (target is not null && tab.Location is { } l) target.OpenTab(l);
        }, target is not null);
        int index = panel.Tabs.IndexOf(tab);
        Add("Move tab left", "tab.left", () => panel.Tabs.Move(index, index - 1), index > 0);
        Add("Move tab right", "tab.right", () => panel.Tabs.Move(index, index + 1), index < panel.Tabs.Count - 1);
        return new ContextMenu { ItemsSource = items };
    }

    private void OnNewTabClick(object? sender, RoutedEventArgs e)
    {
        if (Panel is { ActiveTab.Location: { } loc } p)
        {
            p.OpenTab(loc);
            Activated?.Invoke();
            List.Focus();
        }
    }

    private void OnLocationMenuClick(object? sender, RoutedEventArgs e)
    {
        Activated?.Invoke();
        LocationMenuRequested?.Invoke();
    }

    private void OnDismissBanner(object? sender, RoutedEventArgs e)
    {
        if (Panel?.ActiveTab is { } t) t.Banner = null;
        List.Focus();
    }

    /// <summary>The item context menu: at the pointer for a right click, at the focused row from the keyboard.</summary>
    public void ShowContextMenu(bool atFocus = false, Avalonia.Point? point = null)
    {
        long request = ++_menuRequest;
        _openItemMenu?.Close();
        _openItemMenu = null;
        _ = ShowContextMenuAsync(atFocus, point, request);
    }

    private async Task ShowContextMenuAsync(bool atFocus, Avalonia.Point? point, long request)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
        if (OperatingSystem.IsWindows() && Panel?.ActiveTab is { Location.IsFileSystem: true } tab &&
            tab.Listing.MarkedCount <= WindowsContextMenu.MaxItems)
        {
            var paths = tab.Listing.GetSelection().Select(item => item.FileSystemPath).ToArray();
            if (WindowsContextMenu.CanShow(paths))
            {
                var anchor = point ?? (List.FocusedRowBounds() is { } bounds
                    ? new Avalonia.Point(bounds.Left + 12, bounds.Bottom) : new Avalonia.Point(0, 0));
                var screen = List.PointToScreen(anchor);
                var result = await WindowsContextMenu.ShowAsync(paths!, screen.X, screen.Y);
                if (request != _menuRequest) return;
                if (result == WindowsContextMenu.Result.Handled) return;
                if (result == WindowsContextMenu.Result.ActionFailed)
                {
                    vm.Notify("Windows could not complete that action.", true);
                    return;
                }
                if (result == WindowsContextMenu.Result.FileCatActions) atFocus = point is null;
                if (result == WindowsContextMenu.Result.Failed && !_nativeMenuWarningShown)
                {
                    _nativeMenuWarningShown = true;
                    vm.Notify("Windows menu unavailable here; showing FileCat actions.", true);
                }
            }
        }
        if (request != _menuRequest) return;
        var menu = ContextMenuFactory.Build(vm);
        if (atFocus && List.FocusedRowBounds() is { } row)
        {
            menu.Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft;
            menu.PlacementRect = row;
        }
        _openItemMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_openItemMenu, menu)) _openItemMenu = null; };
        menu.Open(List);
    }
}
