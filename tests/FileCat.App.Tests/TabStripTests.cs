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
}
