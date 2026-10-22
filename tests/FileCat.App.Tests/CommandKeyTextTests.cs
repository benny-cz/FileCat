using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>
/// A key that runs a command must not also type its character into the field the command opens, and nothing else
/// may be lost: macOS types into a text field without raising key-down, so the first character typed into the field
/// F7 or ⌘L had just focused was swallowed.
/// </summary>
public sealed class CommandKeyTextTests
{
    [AvaloniaFact]
    public async Task Text_typed_without_its_own_key_press_reaches_the_field_a_command_focused()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var box = await OpenPrompt(window, vm, Key.F7, PhysicalKey.F7, null);
            Assert.Equal(string.Empty, box.Text);
            // As macOS delivers typing into a focused text field: text input only.
            window.KeyTextInput("a");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("a", box.Text);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_command_key_does_not_type_its_own_character_into_the_field_it_opened()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            // Num+ opens Select by mask; its "+" arrives as text after the key press on platforms that send it.
            var box = await OpenPrompt(window, vm, Key.Add, PhysicalKey.NumPadAdd, "+");
            string before = box.Text ?? string.Empty;
            window.KeyTextInput("+");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, box.Text);
            window.KeyTextInput("x");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("x", box.Text);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task<TextBox> OpenPrompt(Views.MainWindow window, ViewModels.MainViewModel vm, Key key, PhysicalKey physical, string? symbol)
    {
        var listing = vm.ActiveTab!.Listing;
        await FileListFixture.Wait(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
        window.ActiveList!.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(key, RawInputModifiers.None, physical, symbol);
        await FileListFixture.Wait(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused));
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsFocused);
    }
}
