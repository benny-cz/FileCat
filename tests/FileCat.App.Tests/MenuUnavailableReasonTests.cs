using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace FileCat.App.Tests;

/// <summary>
/// UX audit C (archive writing): the menu bar said why a command was unavailable only in a tooltip, which a screen reader
/// does not get and which a disabled item did not show. Like the context menus and dependent settings, every disabled
/// command in an opened menu now shows its reason while disabled and carries it as help text.
/// </summary>
public sealed class MenuUnavailableReasonTests
{
    [AvaloniaFact]
    public void Every_unavailable_menu_bar_command_says_why_to_a_screen_reader()
    {
        var (services, _, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var menu = window.FindControl<Menu>("MainMenu")!;
            int unavailable = 0;
            foreach (var top in menu.Items.OfType<MenuItem>())
            {
                top.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs();
                foreach (var item in Commands(top))
                {
                    if (item.IsEnabled)
                    {
                        Assert.Null(AutomationProperties.GetHelpText(item));
                        continue;
                    }
                    unavailable++;
                    string? reason = ToolTip.GetTip(item) as string;
                    Assert.False(string.IsNullOrWhiteSpace(reason), $"{item.Header}: no reason");
                    Assert.True(ToolTip.GetShowOnDisabled(item), $"{item.Header}: the reason does not show while disabled");
                    Assert.Equal(reason, AutomationProperties.GetHelpText(item));
                }
                top.IsSubMenuOpen = false; Dispatcher.UIThread.RunJobs();
            }
            Assert.True(unavailable > 0, "A fresh window should have unavailable commands to check.");
        }
        finally { AccessibilityTests.Close(services, window, root); }

        static IEnumerable<MenuItem> Commands(MenuItem parent) =>
            parent.Items.OfType<MenuItem>().Where(item => item.Tag is string).Concat(parent.Items.OfType<MenuItem>().SelectMany(Commands));
    }
}
