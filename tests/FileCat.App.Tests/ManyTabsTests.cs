using Avalonia.Headless.XUnit;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// V12 (many tabs): only the active tab of each panel watches its folder (plan §8.2: not every persisted tab), so forty
/// open tabs hold two watches; a tab not watched while its folder changed shows the change once it is active again.
/// </summary>
public sealed class ManyTabsTests
{
    [AvaloniaFact]
    public async Task Only_the_active_tabs_watch_and_a_tab_catches_up_when_it_is_active_again()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tabs = new List<ViewModels.TabViewModel>();
            for (int i = 0; i < 40; i++)
            {
                string folder = Directory.CreateDirectory(Path.Combine(root, $"folder-{i:00}")).FullName;
                File.WriteAllText(Path.Combine(folder, "first.txt"), "1");
                tabs.Add(vm.Workspace.Panels[i % 2].OpenTab(Location.FileSystem(folder)));
            }
            var all = vm.Workspace.Panels.SelectMany(p => p.Tabs).ToList();
            Assert.True(all.Count >= 40);
            Assert.Equal(vm.Workspace.Panels.Count, all.Count(t => t.IsWatching));
            Assert.All(vm.Workspace.Panels, p => Assert.True(p.ActiveTab!.IsWatching));

            // A tab in the background, its folder changed meanwhile: active again, it lists the new file.
            var behind = tabs[4];
            var listing = behind.Listing;
            for (int i = 0; i < 300 && listing.State != ListingState.Complete; i++) await Task.Delay(20, ct);
            Assert.False(behind.IsWatching);
            await Task.Delay(50, ct);
            File.WriteAllText(Path.Combine(root, "folder-04", "arrived.txt"), "2");
            behind.Panel.ActiveTab = behind;
            Assert.True(behind.IsWatching);
            for (int i = 0; i < 300 && !listing.FocusName("arrived.txt"); i++) await Task.Delay(20, ct);
            Assert.True(listing.FocusName("arrived.txt"), "the tab did not catch up with its folder once active");
            Assert.Equal(vm.Workspace.Panels.Count, vm.Workspace.Panels.SelectMany(p => p.Tabs).Count(t => t.IsWatching));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
