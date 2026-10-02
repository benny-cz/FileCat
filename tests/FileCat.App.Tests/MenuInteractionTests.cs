using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class MenuInteractionTests
{
    [AvaloniaTheory]
    [InlineData("System")]
    [InlineData("Psychedelic")]
    public async Task An_unchanged_theme_update_keeps_the_open_menu_and_its_commands(string theme)
    {
        string before = ThemeManager.RequestedName;
        var (services, _, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            ThemeManager.Apply(theme);
            await Task.Delay(100, ct);
            var menu = window.FindControl<Menu>("MainMenu")!;
            var item = menu.Items.OfType<MenuItem>().First();
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await Task.Delay(100, ct);
            Assert.True(item.IsSubMenuOpen);

            // Windows can repeat its color notification when a native popup is created.
            for (int i = 0; i < 3; i++)
            {
                ThemeManager.Apply(theme);
                await Task.Delay(100, ct);
                Assert.True(item.IsSubMenuOpen, "An unchanged theme update closed the menu");
                Assert.Same(item, menu.Items[0]);
            }
            menu.Close();
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
            ThemeManager.Apply(before);
        }
    }

    [AvaloniaFact]
    public async Task A_mouse_click_keeps_each_main_menu_open_after_the_button_is_released()
    {
        var (services, _, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var menu = window.FindControl<Menu>("MainMenu")!;
            await Task.Delay(100, ct);
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.NotEmpty(items);
            foreach (var item in items)
            {
                var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await Task.Delay(100, ct);
                Assert.True(item.IsSubMenuOpen, $"{item.Header}: menu closed after the mouse click");
                menu.Close();
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
