using Avalonia.Controls;
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
        }
        finally
        {
            ThemeManager.Apply("Classic");
            AccessibilityTests.Close(services, window, root);
        }
    }
}
