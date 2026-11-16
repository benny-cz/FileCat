using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

public sealed class MenuInteractionTests
{
    [AvaloniaTheory]
    [InlineData(1, null, CommandIds.MarkAll)]
    [InlineData(4, "_Arrange", CommandIds.EqualizePanels)]
    public async Task F10_arrows_and_enter_activate_the_menu_item_without_opening_the_panel_item(
        int menuIndex, string? submenuHeader, string command)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Complete; i++)
                await Task.Delay(20, ct);
            Assert.Equal(ListingState.Complete, tab.Listing.State);
            var location = tab.Location;
            tab.Listing.SetFocus(0); // Enter in the panel would navigate to its parent.
            vm.View.FocusActivePanel();
            var menu = window.FindControl<Menu>("MainMenu")!;

            void Press(PhysicalKey key)
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
                window.KeyReleaseQwerty(key, RawInputModifiers.None);
            }

            MenuItem SelectChild(MenuItem owner, Predicate<MenuItem> matches)
            {
                for (int i = 0; i < owner.ItemCount; i++)
                {
                    Press(PhysicalKey.ArrowDown);
                    if (window.FocusManager?.GetFocusedElement() is MenuItem focused && matches(focused))
                        return focused;
                }
                Assert.Fail($"Could not reach the requested item in {owner.Header} using Down.");
                return null!;
            }

            Press(PhysicalKey.F10);
            Assert.True(Assert.IsType<MenuItem>(menu.Items[0]).IsSubMenuOpen);
            for (int i = 0; i < menuIndex; i++) Press(PhysicalKey.ArrowRight);
            var top = Assert.IsType<MenuItem>(menu.Items[menuIndex]);
            Assert.True(top.IsSubMenuOpen);
            var owner = top;
            if (submenuHeader is not null)
            {
                owner = SelectChild(top, item => Equals(item.Header, submenuHeader));
                Press(PhysicalKey.ArrowRight);
                Assert.True(owner.IsSubMenuOpen);
            }
            var selected = SelectChild(owner, item => Equals(item.Tag, command));
            int activations = 0;
            selected.Click += (_, _) => activations++;

            Press(PhysicalKey.Enter);
            await Task.Delay(100, ct);
            Assert.Equal(1, activations);
            Assert.Equal(command == CommandIds.MarkAll ? 2 : 0, tab.Listing.MarkedCount);
            Assert.Equal(command, services.History.RecentCommands[0]);
            Assert.Equal(location, tab.Location);
            Assert.Equal(0, tab.Listing.FocusedIndex);
            Assert.False(top.IsSubMenuOpen);
            Assert.False(owner.IsSubMenuOpen);
            Assert.True(window.ActiveList!.IsFocused);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Menu_text_and_escape_do_not_reach_panel_search_or_notifications()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!;
            vm.View.FocusActivePanel();
            vm.Notify("Keep this notification while navigating the menu.");
            var notification = vm.Notification;
            var menu = window.FindControl<Menu>("MainMenu")!;

            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.F10, RawInputModifiers.None);
            var top = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.True(top.IsSubMenuOpen);
            Assert.IsType<MenuItem>(window.FocusManager?.GetFocusedElement());

            window.KeyTextInput("b");
            Assert.False(tab.IsQuickSearchActive);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(top.IsSubMenuOpen);
            Assert.Equal(notification, vm.Notification);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

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
