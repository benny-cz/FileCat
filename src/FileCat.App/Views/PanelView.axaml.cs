using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

public partial class PanelView : UserControl
{
    public PanelView()
    {
        InitializeComponent();
        List.ActivateRequested += (_, _) => Activated?.Invoke();
        List.OpenRequested += (_, _) => OpenRequested?.Invoke();
        List.MiddleClickRequested += (_, row) => MiddleClick?.Invoke(row);
        List.ContextMenuRequested += (_, _) => ShowContextMenu();
        List.GotFocus += (_, _) => Activated?.Invoke();
        PathBox.GotFocus += (_, _) =>
        {
            Activated?.Invoke();
            Dispatcher.UIThread.Post(PathBox.SelectAll, DispatcherPriority.Input);
        };
        PathBox.LostFocus += (_, _) => RevertPath();
        PathBox.AddHandler(KeyDownEvent, OnPathKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => HookQuickView();
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
    }

    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.ActiveTab)) QuickView.Attach(_hookedSource?.ActiveTab);
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

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
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
        void Add(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) =>
            {
                action();
                List.Focus();
            };
            items.Add(mi);
        }
        Add("Close tab", () => panel.CloseTab(tab), panel.Tabs.Count > 1);
        Add("Close other tabs", () =>
        {
            foreach (var t in panel.Tabs.Where(t => !ReferenceEquals(t, tab) && !t.IsLocked).ToList()) panel.CloseTab(t);
        }, panel.Tabs.Count > 1);
        Add("Duplicate tab", () =>
        {
            if (tab.Location is null) return;
            var dup = panel.OpenTab(tab.Location);
            var s = tab.ToState();
            s.Locked = false;
            dup.ApplyState(s);
        });
        items.Add(new Separator());
        Add(tab.IsLocked && !tab.ReturnToRoot ? "Unlock tab" : "Lock tab (navigation opens new tabs)", () =>
        {
            bool lockIt = !(tab.IsLocked && !tab.ReturnToRoot);
            tab.IsLocked = lockIt;
            tab.ReturnToRoot = false;
            tab.LockedRoot = lockIt ? tab.Location : null;
        });
        Add(tab.IsLocked && tab.ReturnToRoot ? "Unlock tab" : "Lock tab at root (returns here when revisited)", () =>
        {
            bool lockIt = !(tab.IsLocked && tab.ReturnToRoot);
            tab.IsLocked = lockIt;
            tab.ReturnToRoot = lockIt;
            tab.LockedRoot = lockIt ? tab.Location : null;
        });
        items.Add(new Separator());
        var target = ws.GetTarget(panel);
        Add(target is null ? "Move tab to target panel (no target)" : $"Move tab to panel {target.Number}", () =>
        {
            if (target is null || panel.Tabs.Count <= 1) return;
            target.AttachTab(panel.DetachTab(tab));
            ws.Activate(target);
        }, target is not null && panel.Tabs.Count > 1);
        Add(target is null ? "Copy tab to target panel (no target)" : $"Copy tab to panel {target.Number}", () =>
        {
            if (target is not null && tab.Location is { } l) target.OpenTab(l);
        }, target is not null);
        int index = panel.Tabs.IndexOf(tab);
        Add("Move tab left", () => panel.Tabs.Move(index, index - 1), index > 0);
        Add("Move tab right", () => panel.Tabs.Move(index, index + 1), index < panel.Tabs.Count - 1);
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

    public void ShowContextMenu()
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
        var menu = ContextMenuFactory.Build(vm);
        menu.Open(List);
    }
}
