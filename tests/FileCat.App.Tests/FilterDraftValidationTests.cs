using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Search;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>FCUX-028: a rejected mask belongs to the filter field, not to location warnings or applied rows.</summary>
public sealed class FilterDraftValidationTests
{
    [AvaloniaTheory]
    [InlineData("\"*.log", "\"*.log\"")]
    [InlineData("/.*log", "/.*log/")]
    [InlineData("@no-such-filter-for-this-test", null)]
    public async Task Invalid_draft_keeps_focus_text_caret_and_applied_rows_until_repaired_or_reverted(
        string invalid, string? corrected)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            File.WriteAllText(Path.Combine(root, "files", "notes.log"), "log");
            Directory.CreateDirectory(Path.Combine(root, "files", "folder"));
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            tab.Refresh();
            await Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 5);
            var panel = vm.Workspace.Panels[0];
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var box = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "FilterBox");
            var error = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(b => b.Name == "FilterError");
            var announcement = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(b => b.Name == "FilterAnnouncement");
            Assert.Equal("Filter ready", ControlAutomationPeer.CreatePeerForElement(announcement).GetName());
            Assert.True(announcement.IsEffectivelyVisible);
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(announcement));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(error));
            Assert.True(tab.SetFilter("*.txt"));
            await Wait(() => tab.Listing.VisibleCount == 4);
            tab.Banner = "Registry notifications stopped. Reread this key to check for changes.";

            box.Focus();
            box.Text = invalid;
            box.CaretIndex = invalid.Length;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(box.IsFocused);
            Assert.Equal(invalid, box.Text);
            Assert.Equal(invalid.Length, box.CaretIndex);
            Assert.Equal("*.txt", tab.FilterText);
            Assert.Equal(4, tab.Listing.VisibleCount);
            Assert.NotNull(tab.FilterValidationError);
            Assert.True(error.IsVisible);
            Assert.Equal(tab.FilterValidationError, error.Text);
            Assert.Equal(tab.FilterValidationError, ControlAutomationPeer.CreatePeerForElement(error).GetName());
            Assert.Equal(tab.FilterValidationError, ControlAutomationPeer.CreatePeerForElement(announcement).GetName());
            Assert.Equal(tab.FilterValidationError, AutomationProperties.GetHelpText(box));
            Assert.StartsWith("Registry notifications stopped", tab.Banner);

            if (corrected is not null)
            {
                box.Text = corrected;
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Wait(() => tab.Listing.VisibleCount == 3);
                Assert.False(box.IsFocused);
                Assert.Null(tab.FilterValidationError);
                Assert.False(error.IsVisible);
                Assert.Equal("Filter ready", ControlAutomationPeer.CreatePeerForElement(announcement).GetName());
                Assert.Equal(corrected, tab.FilterText);
                Assert.StartsWith("Registry notifications stopped", tab.Banner);
            }
            else
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.False(box.IsFocused);
                Assert.Equal("*.txt", box.Text);
                Assert.Equal("*.txt", tab.FilterText);
                Assert.Null(tab.FilterValidationError);
                Assert.Equal("Filter ready", ControlAutomationPeer.CreatePeerForElement(announcement).GetName());
                Assert.Equal(4, tab.Listing.VisibleCount);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Blur_discards_unapplied_draft_and_all_items_keeps_unrelated_warning()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            var set = services.WorkingSets.Create("Filtered set");
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Wait(() => tab.Listing.State == ListingState.Complete);
            string warning = Assert.IsType<string>(tab.Banner);
            Assert.Contains("deletes the originals", warning);
            var panel = vm.Workspace.Panels[0];
            var view = window.GetVisualDescendants().OfType<PanelView>().First(v => ReferenceEquals(v.DataContext, panel));
            var box = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "FilterBox");
            var announcement = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(b => b.Name == "FilterAnnouncement");
            var list = view.GetVisualDescendants().OfType<FileCat.App.Controls.FileListControl>().Single();
            Assert.True(tab.SetFilter("*.txt"));
            await Wait(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);
            box.Focus();
            box.Text = "\"unfinished";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.NotNull(tab.FilterValidationError);
            Assert.Equal(tab.FilterValidationError, announcement.Text);
            Assert.Equal(warning, tab.Banner);

            list.Focus();
            Assert.Equal("*.txt", box.Text);
            Assert.Null(tab.FilterValidationError);
            Assert.Equal("Filter ready", announcement.Text);
            Assert.Equal("*.txt", tab.FilterText);
            Assert.Equal(warning, tab.Banner);
            Assert.True(tab.SetFilter(PanelView.AllItemsMask));
            Assert.Null(tab.FilterText);
            Assert.Equal(warning, tab.Banner);
            Assert.True(tab.SetFilter("*"));
            Assert.Null(tab.FilterText);
            Assert.Equal(warning, tab.Banner);

            box.Focus();
            box.Text = "\"stale";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var next = panel.OpenTab(Location.FileSystem(Path.Combine(root, "files")));
            await Wait(() => next.Listing.State == ListingState.Complete);
            Assert.Same(next, panel.ActiveTab);
            Assert.Null(tab.FilterValidationError);
            Assert.Equal(PanelView.AllItemsMask, box.Text);
            Assert.Equal("Filter ready", announcement.Text);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
