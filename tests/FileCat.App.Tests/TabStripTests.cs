using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>A panel's tabs: the active one stays in sight, and "⋯" lists them all when they do not fit.</summary>
public sealed class TabStripTests
{
    [AvaloniaFact]
    public async Task The_active_tab_stays_in_sight_and_overflowing_tabs_offer_the_list()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var panel = vm.Workspace.Panels[0];
            vm.Workspace.Activate(panel);
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            await Task.Delay(100, ct);
            Assert.False(view.TabStripState().Overflowing); // one tab fits

            for (int i = 0; i < 12; i++) vm.Execute(CommandIds.NewTab);
            for (int i = 0; i < 100 && view.TabStripState() != (true, true); i++) await Task.Delay(20, ct);
            Assert.Equal(13, panel.Tabs.Count);
            Assert.Equal((true, true), view.TabStripState()); // the newest tab, at the end, is shown

            panel.ActiveTab = panel.Tabs[0];
            for (int i = 0; i < 100 && !view.TabStripState().ActiveTabVisible; i++) await Task.Delay(20, ct);
            Assert.True(view.TabStripState().ActiveTabVisible);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// A folder's name shows escaped wherever FileCat shows it, as in the file list (§18.3, V23 B14): its tab, and the
    /// path beside the command line; the path itself, which the edit box and every operation use, stays as it is.
    /// </summary>
    [AvaloniaFact]
    public async Task A_folders_name_cannot_turn_its_tab_or_path_around()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string name = "docs" + (char)0x202E + "fdp.exe";
            string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            tab.Navigate(Core.Resources.Location.FileSystem(folder));
            for (int i = 0; i < 100 && tab.DisplayPath != folder; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            string escaped = "docs" + (char)92 + "u202Efdp.exe";
            Assert.Equal(escaped, tab.TabHeader);
            Assert.EndsWith(escaped, tab.ShownPath, StringComparison.Ordinal);
            Assert.Equal(folder, tab.DisplayPath);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
