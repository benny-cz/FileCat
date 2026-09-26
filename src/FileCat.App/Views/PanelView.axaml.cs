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
        if (e.InitialPressMouseButton == MouseButton.Middle && sender is Button { Tag: TabViewModel tab } && Panel is { } p)
        {
            p.CloseTab(tab);
            e.Handled = true;
        }
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
