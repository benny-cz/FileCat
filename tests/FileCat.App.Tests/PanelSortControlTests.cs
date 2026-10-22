using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Listing;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>FCUX-010: visible sort controls remain accessible outside populated custom lists.</summary>
public sealed class PanelSortControlTests
{
    [AvaloniaTheory]
    [InlineData(1200)]
    [InlineData(720)]
    public async Task Each_panel_exposes_sort_and_reversing_one_does_not_change_the_other(int width)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: width, height: 540);
        try
        {
            await WaitFor(() => vm.Workspace.Panels.All(p => p.ActiveTab?.Listing.State == ListingState.Complete));
            window.CaptureRenderedFrame();
            var views = window.GetVisualDescendants().OfType<PanelView>()
                .Where(v => v.DataContext is FileCat.App.ViewModels.PanelViewModel).ToArray();
            Assert.Equal(vm.Workspace.Panels.Count, views.Length);
            Assert.True(views.Length >= 2);
            foreach (var view in views)
            {
                var button = view.FindControl<Button>("SortButton")!;
                Assert.True(button.IsEffectivelyVisible);
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
                Assert.Equal("Sort files: Name, ascending", AutomationProperties.GetName(button));
            }

            var first = views.Single(v => ReferenceEquals(v.DataContext, vm.Workspace.Panels[0]));
            var second = views.Single(v => ReferenceEquals(v.DataContext, vm.Workspace.Panels[1]));
            var button1 = first.FindControl<Button>("SortButton")!;
            button1.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(first.SortMenu);
            var name = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource)
                .Single(item => (string)item.Header! == "Name");
            Assert.True(name.IsChecked);
            Assert.Contains("activate to reverse", AutomationProperties.GetName(name));
            name.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitFor(() => vm.Workspace.Panels[0].ActiveTab!.Listing.Sort.Descending);
            Assert.Equal("Sort files: Name, descending", AutomationProperties.GetName(button1));
            Assert.Equal("Sort files: Name, ascending",
                AutomationProperties.GetName(second.FindControl<Button>("SortButton")!));
            menu.Close();

            button1.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            menu = Assert.IsType<ContextMenu>(first.SortMenu);
            var size = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource)
                .Single(item => (string)item.Header! == "Size");
            size.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(SortField.Size, vm.Workspace.Panels[0].ActiveTab!.Listing.Sort.Field);
            Assert.Contains("Size, descending", AutomationProperties.GetName(button1));
            menu.Close();
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_menu_choice_from_a_retired_tab_cannot_sort_its_replacement()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.Panels[0];
            var old = panel.ActiveTab!;
            await WaitFor(() => old.Listing.State == ListingState.Complete);
            var view = window.GetVisualDescendants().OfType<PanelView>()
                .Single(v => ReferenceEquals(v.DataContext, panel));
            view.FindControl<Button>("SortButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(view.SortMenu);
            var stale = Assert.IsAssignableFrom<IEnumerable<MenuItem>>(menu.ItemsSource)
                .Single(item => (string)item.Header! == "Size");
            var replacement = panel.OpenTab(Location.FileSystem(Path.Combine(root, "files")));
            await WaitFor(() => replacement.Listing.State == ListingState.Complete);
            stale.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(SortField.Name, old.Listing.Sort.Field);
            Assert.Equal(SortField.Name, replacement.Listing.Sort.Field);
            menu.Close();
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task WaitFor(Func<bool> ready)
    {
        for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready());
    }
}
