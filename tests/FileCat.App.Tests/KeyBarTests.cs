using Avalonia.Headless.XUnit;

namespace FileCat.App.Tests;

/// <summary>The function-key bar shows which keys apply to what is focused and marked right now.</summary>
public sealed class KeyBarTests
{
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
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
