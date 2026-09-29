using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>View → Theme…: every theme tried on FileCat itself, kept with Enter, undone with Esc.</summary>
public sealed class ThemePickerTests
{
    private static async Task Click(Window window, string text)
    {
        var ct = TestContext.Current.CancellationToken;
        for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == text && b.IsEffectivelyEnabled); i++)
            await Task.Delay(20, ct);
        window.GetVisualDescendants().OfType<Button>().Last(b => b.Content as string == text && b.IsEffectivelyVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task<ListBox> Picker(Window window)
    {
        for (int i = 0; i < 250; i++)
        {
            if (window.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(l => Avalonia.Automation.AutomationProperties.GetName(l) == "Themes") is { } list) return list;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("The theme picker did not open.");
    }

    [Theory]
    [InlineData("#FFFF00", "#FF000000")] // High Contrast's yellow: black text
    [InlineData("#23B04A", "#FF000000")] // a bright green
    [InlineData("#0078D4", "#FFFFFFFF")] // Classic's blue: white, as Windows does
    [InlineData("#1F1235", "#FFFFFFFF")]
    public void Text_on_an_accent_is_whichever_of_black_and_white_reads_better(string accent, string text) =>
        Assert.Equal(text, ThemeManager.OnColor(accent));

    [AvaloniaFact]
    public async Task Themes_are_previewed_live_then_kept_or_undone()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.Theme = "Classic";
            ThemeManager.Apply("Classic");

            // Esc (Cancel) after trying two themes: the one in use comes back and nothing is saved.
            var picking = vm.ChooseThemeAsync();
            var list = await Picker(window);
            Assert.Equal(ThemeManager.Names.Count, list.ItemCount);
            list.SelectedIndex = ThemeManager.Names.ToList().IndexOf("Cyberpunk");
            Assert.Equal("Cyberpunk", ThemeManager.Current.Name);
            list.SelectedIndex = ThemeManager.Names.ToList().IndexOf("Steampunk");
            Assert.Equal("Steampunk", ThemeManager.Current.Name);
            await Click(window, "Cancel");
            await picking;
            Assert.Equal("Classic", ThemeManager.Current.Name);
            Assert.Equal("Classic", services.Settings.Theme);

            // Keep: the tried theme stays and is saved.
            picking = vm.ChooseThemeAsync();
            list = await Picker(window);
            list.SelectedIndex = ThemeManager.Names.ToList().IndexOf("Psychedelic");
            await Click(window, "Keep");
            await picking;
            Assert.Equal("Psychedelic", ThemeManager.Current.Name);
            Assert.Equal("Psychedelic", services.Settings.Theme);

            // By keyboard alone, as the picker says: arrows try, Enter keeps.
            var ct = TestContext.Current.CancellationToken;
            picking = vm.ChooseThemeAsync();
            list = await Picker(window);
            for (int i = 0; i < 100 && window.FocusManager?.GetFocusedElement() is not ListBoxItem; i++) await Task.Delay(20, ct);
            int start = list.SelectedIndex;
            window.KeyPress(Avalonia.Input.Key.Up, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowUp, null);
            for (int i = 0; i < 100 && list.SelectedIndex == start; i++) await Task.Delay(20, ct);
            string tried = ThemeManager.Names[list.SelectedIndex];
            Assert.NotEqual("Psychedelic", tried);
            Assert.Equal(tried, ThemeManager.Current.Name);
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            for (int i = 0; i < 100 && !picking.IsCompleted; i++) await Task.Delay(20, ct);
            Assert.True(picking.IsCompleted, "Enter did not keep the theme.");
            Assert.Equal(tried, services.Settings.Theme);
        }
        finally
        {
            ThemeManager.Apply("Classic");
            AccessibilityTests.Close(services, window, root);
        }
    }
}
