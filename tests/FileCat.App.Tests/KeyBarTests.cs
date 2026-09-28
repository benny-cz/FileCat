using Avalonia.Headless.XUnit;

namespace FileCat.App.Tests;

/// <summary>The function-key bar shows which keys apply to what is focused and marked right now.</summary>
public sealed class KeyBarTests
{
    [AvaloniaFact]
    public async Task The_context_menu_opens_from_the_keyboard_and_offers_what_applies()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            Assert.Contains(services.Keymap.GetChords(Core.Commands.CommandIds.ContextMenu), c => c.Key == "F10" && c.Mods == Core.Commands.KeyMods.Shift);
            Assert.Contains(services.Keymap.GetChords(Core.Commands.CommandIds.ContextMenu), c => c.Key == "Apps");
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            listing.SetFocus(1); // a.txt: a file does not open in a tab of its own
            Assert.False(vm.GetAvailability(Core.Commands.CommandIds.OpenInNewTab).Enabled);
            listing.SetFocus(0); // "..": a place
            Assert.True(vm.GetAvailability(Core.Commands.CommandIds.OpenInNewTab).Enabled);
            listing.SetFocus(1);
            var menu = Views.ContextMenuFactory.Build(vm);
            var actions = ((IEnumerable<object>)menu.ItemsSource!).OfType<Avalonia.Controls.MenuItem>().ToList();
            var titles = actions.Select(m => m.Header as string).ToList();
            Assert.Contains("Copy…", titles);
            Assert.DoesNotContain("Open focused folder in new tab", titles);
            Assert.All(actions, item => Assert.IsType<Avalonia.Controls.Image>(item.Icon));
            var more = Assert.Single(actions, m => Equals(m.Header, "More FileCat actions"));
            var moreTitles = ((IEnumerable<object>)more.ItemsSource!).OfType<Avalonia.Controls.MenuItem>().Select(m => m.Header as string);
            Assert.Contains("Delete permanently…", moreTitles);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Keys_that_act_on_items_light_up_as_focus_moves_off_the_parent_row()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            var view = vm.KeyBar[2]; // F3
            listing.SetFocus(0); // ".."
            await Task.Delay(50, ct);
            Assert.False(view.IsEnabled);

            listing.SetFocus(1); // a.txt
            for (int i = 0; i < 100 && !view.IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(view.IsEnabled);

            // Marks count as well: with an item marked, F3's neighbours act on it even from "..".
            listing.ToggleMark(2);
            listing.SetFocus(0);
            await Task.Delay(50, ct);
            Assert.True(vm.KeyBar[4].IsEnabled); // F5 copies the marked item
            listing.ToggleMark(2);
            for (int i = 0; i < 100 && vm.KeyBar[4].IsEnabled; i++) await Task.Delay(20, ct);
            Assert.False(vm.KeyBar[4].IsEnabled);

            // What the location cannot do stays dimmed even with an item focused, and says why (This PC: drives).
            vm.ActiveTab.Navigate(new Core.Resources.Location(Core.Resources.Schemes.Computer, string.Empty));
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount > 0); i++) await Task.Delay(20, ct);
            listing.SetFocus(0);
            for (int i = 0; i < 100 && !vm.KeyBar[2].IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(vm.KeyBar[4].IsEnabled); // F5 copies what is focused
            Assert.False(vm.KeyBar[7].IsEnabled); // F8
            Assert.False(vm.KeyBar[5].IsEnabled); // F6
            Assert.Contains("Drives are not deleted", vm.GetAvailability(Core.Commands.CommandIds.Delete).Reason, StringComparison.Ordinal);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
