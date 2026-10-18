using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>FCUX-032: one explicit keyboard menu exposes the same provider-derived ancestors as pointer links.</summary>
public sealed class BreadcrumbMenuTests
{
    [AvaloniaFact]
    public async Task Keyboard_menu_exposes_ancestor_paths_and_open_copy_actions()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            string parent = Directory.CreateDirectory(Path.Combine(files, "sub")).FullName;
            string deeper = Directory.CreateDirectory(Path.Combine(parent, "deeper")).FullName;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Navigate(Location.FileSystem(deeper));
            await WaitFor(() => tab.Location?.Path == deeper, ct);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, panel));
            await WaitFor(() => view.PathLinksControl.Segments.Count >= 3, ct);
            window.CaptureRenderedFrame();
            Assert.Equal("Navigate", FileCat.App.ViewModels.MainMenuModel.PathOf(CommandIds.AncestorMenu));
            Assert.Contains("Current location:", AutomationProperties.GetName(view.PathLinksControl));
            Assert.Contains(deeper, AutomationProperties.GetName(view.PathLinksControl));

            window.FocusActivePanel();
            window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
            var menu = Assert.IsType<ContextMenu>(view.AncestorMenu);
            var groups = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource).ToList();
            Assert.True(groups.Count >= 2);
            Assert.Contains(parent, AutomationProperties.GetName(groups[0]));
            var actions = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(groups[0].ItemsSource).ToList();
            Assert.Equal(3, actions.Count);
            Assert.Contains(parent, AutomationProperties.GetName(actions[0]));
            Assert.Contains(parent, AutomationProperties.GetName(actions[1]));
            Assert.Contains(parent, AutomationProperties.GetName(actions[2]));

            ActivateWithKeyboard(groups[0], actions[2]);
            await WaitFor(() => vm.Notification == "Copied " + parent, ct);
            Assert.Equal(parent, await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            menu.Close();
            Assert.True(view.List.IsFocused);

            Assert.True(view.OpenAncestorMenu());
            menu = view.AncestorMenu!;
            groups = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource).ToList();
            actions = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(groups[0].ItemsSource).ToList();
            int tabs = panel.Tabs.Count;
            ActivateWithKeyboard(groups[0], actions[1]);
            Assert.Equal(tabs + 1, panel.Tabs.Count);
            Assert.Equal(parent, panel.ActiveTab!.Location!.Path);
            menu.Close();
            panel.CloseTab(panel.ActiveTab);
            panel.ActiveTab = tab;
            await WaitFor(() => view.PathLinksControl.Text == tab.DisplayPath, ct);

            Assert.True(view.OpenAncestorMenu());
            menu = view.AncestorMenu!;
            groups = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource).ToList();
            actions = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(groups[0].ItemsSource).ToList();
            ActivateWithKeyboard(groups[0], actions[0]);
            await WaitFor(() => tab.Location?.Path == parent, ct);
            menu.Close();
            Assert.True(view.List.IsFocused);
            await vm.ExecuteAsync(CommandIds.GoTo);
            Assert.True(view.FindControl<TextBox>("PathBox")!.IsFocused);

            void ActivateWithKeyboard(MenuItem ancestor, MenuItem action)
            {
                window.CaptureRenderedFrame();
                Assert.True(ancestor.Focus());
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.CaptureRenderedFrame();
                Assert.True(action.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Cancel_returns_to_current_list_without_replaying_a_stale_location_or_item()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            string deeper = Directory.CreateDirectory(Path.Combine(files, "sub", "deeper")).FullName;
            File.WriteAllText(Path.Combine(deeper, "old.txt"), "old");
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            tab.Navigate(Location.FileSystem(deeper));
            await WaitFor(() => tab.Location?.Path == deeper && tab.Listing.FocusName("old.txt"), ct);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, panel));
            await WaitFor(() => view.PathLinksControl.Segments.Count >= 3, ct);

            Assert.True(view.OpenAncestorMenu());
            var canceledMenu = view.AncestorMenu!;
            var firstAncestor = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(canceledMenu.ItemsSource).First();
            window.CaptureRenderedFrame();
            Assert.True(firstAncestor.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Null(view.AncestorMenu);
            Assert.True(view.List.IsFocused);
            Assert.Equal("old.txt", tab.Listing.GetVisible(tab.Listing.FocusedIndex).Name);

            Assert.True(view.OpenAncestorMenu());
            var menu = view.AncestorMenu!;
            var group = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource).First();
            var staleOpen = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(group.ItemsSource).First();
            tab.Navigate(Location.FileSystem(files));
            await WaitFor(() => tab.Location?.Path == files, ct);
            staleOpen.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();
            Assert.Equal(files, tab.Location!.Path);
            Assert.True(view.List.IsFocused);
            Assert.NotEqual("old.txt", tab.Listing.TryGetFocused(out var current) ? current.Name : null);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_location_without_breadcrumb_ancestors_explains_the_edit_route()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            panel.ActiveTab!.Navigate(new Location(FileCat.Core.Resources.Schemes.Computer, string.Empty));
            await WaitFor(() => panel.ActiveTab.Location?.Scheme == FileCat.Core.Resources.Schemes.Computer
                && window.GetVisualDescendants().OfType<PanelView>()
                    .Single(v => ReferenceEquals(v.DataContext, panel)).PathLinksControl.Segments.Count == 0,
                TestContext.Current.CancellationToken);
            await vm.ExecuteAsync(CommandIds.AncestorMenu);
            Assert.Contains("Ctrl+L", vm.Notification);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
        Assert.True(condition());
    }
}
