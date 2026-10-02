using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace FileCat.App.Tests;

public sealed class MenuInteractionTests
{
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
