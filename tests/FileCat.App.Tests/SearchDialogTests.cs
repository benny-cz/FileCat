using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Find files: its buttons say when they would do something, and Enter on a result goes to it.</summary>
public sealed class SearchDialogTests
{
    [AvaloniaFact]
    public async Task Enter_on_a_result_goes_to_it_and_Go_to_waits_for_results()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++)
                await Task.Delay(20, ct);
            vm.Execute(CommandIds.FindFiles);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            Button ButtonNamed(string text) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);
            Assert.False(ButtonNamed("Go to").IsEnabled); // nothing found yet
            Assert.False(ButtonNamed("Show in panel").IsEnabled);

            window.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == "Names").Text = "b.txt";
            ButtonNamed("Search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var results = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Search results");
            for (int i = 0; i < 250 && results.ItemCount == 0; i++) await Task.Delay(20, ct);
            Assert.Equal(1, results.ItemCount);
            for (int i = 0; i < 50 && !ButtonNamed("Go to").IsEnabled; i++) await Task.Delay(20, ct);
            Assert.True(ButtonNamed("Go to").IsEnabled);
            Assert.True(ButtonNamed("Show in panel").IsEnabled);

            results.SelectedIndex = 0;
            results.Focus();
            string diag = "FOCUS1: " + window.FocusManager?.GetFocusedElement()?.GetType().Name + " / list focusable=" + results.Focusable;
            (results.ContainerFromIndex(0) as Control)?.Focus();
            diag += " FOCUS2: " + window.FocusManager?.GetFocusedElement()?.GetType().Name;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.False(dialogs.IsOpen, diag);
            for (int i = 0; i < 250 && !(listing.TryGetFocused(out var f) && f.Name == "b.txt"); i++) await Task.Delay(20, ct);
            Assert.True(listing.TryGetFocused(out var focused) && focused.Name == "b.txt");
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
