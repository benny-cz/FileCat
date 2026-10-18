using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>Toolbar, key bar, and search use the same current command admission.</summary>
public sealed class ToolbarAvailabilityTests
{
    private static Button Button(MainWindow window, string id) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Classes.Contains("toolbar") && Equals(b.Tag, id));

    private static void Matches(MainViewModel vm, MainWindow window, string id)
    {
        Dispatcher.UIThread.RunJobs();
        var button = Button(window, id);
        var availability = vm.GetAvailability(id);
        Assert.Equal(availability.Enabled, button.IsEnabled);
        var palette = vm.CommandSearchEntries().Single(e => e.Id == id);
        Assert.Equal(availability.Enabled, palette.Enabled);
        Assert.Equal(availability.Reason, palette.Reason);
        if (!availability.Enabled)
        {
            string reason = Assert.IsType<string>(availability.Reason);
            Assert.Contains(reason, AutomationProperties.GetHelpText(button));
            var wrapper = Assert.IsType<Border>(button.GetVisualParent());
            Assert.Contains(reason, Assert.IsType<RichTip>(ToolTip.GetTip(wrapper)).Text);
        }
    }

    [AvaloniaFact]
    public void Back_and_forward_follow_the_active_tabs_history()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            var first = panel.ActiveTab!;
            Assert.True(first.CanGoBack); // The window opened the test folder from This PC.
            Matches(vm, window, CommandIds.Back);
            first.GoBack();
            Matches(vm, window, CommandIds.Forward);

            var second = panel.OpenTab(Location.FileSystem(Path.Combine(root, "files")));
            Assert.False(second.CanGoBack);
            Matches(vm, window, CommandIds.Back);
            Assert.False(Button(window, CommandIds.Back).IsEnabled);
            panel.ActiveTab = first;
            Matches(vm, window, CommandIds.Forward);
            Assert.True(Button(window, CommandIds.Forward).IsEnabled);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Item_and_provider_changes_update_toolbar_with_a_discoverable_reason()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            Assert.Equal(ListingState.Complete, listing.State);

            listing.SetFocus(0); // Parent is not a file operation source.
            Dispatcher.UIThread.RunJobs();
            Matches(vm, window, CommandIds.Copy);
            Assert.False(Button(window, CommandIds.Copy).IsEnabled);
            Assert.False(vm.KeyBar[4].IsEnabled);

            listing.SetFocus(1);
            Dispatcher.UIThread.RunJobs();
            Matches(vm, window, CommandIds.Copy);
            Matches(vm, window, CommandIds.Move);
            Matches(vm, window, CommandIds.Delete);
            Assert.True(Button(window, CommandIds.Copy).IsEnabled);
            Assert.True(vm.KeyBar[4].IsEnabled);

            vm.ActiveTab.Navigate(new Location(Schemes.Computer, string.Empty));
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.VisibleCount > 0); i++)
                await Task.Delay(20, ct);
            Assert.Equal(ListingState.Complete, listing.State);
            listing.SetFocus(0);
            Dispatcher.UIThread.RunJobs();
            Matches(vm, window, CommandIds.Delete);
            Assert.False(Button(window, CommandIds.Delete).IsEnabled);
            Assert.False(vm.KeyBar[7].IsEnabled);
            Assert.Contains("Drives are not deleted", vm.GetAvailability(CommandIds.Delete).Reason);
            await vm.ExecuteAsync(CommandIds.Delete); // Admission is checked again when a stale command is invoked.
            Assert.Contains("Drives are not deleted", vm.Notification);
            Assert.True(File.Exists(Path.Combine(root, "files", "a.txt")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
