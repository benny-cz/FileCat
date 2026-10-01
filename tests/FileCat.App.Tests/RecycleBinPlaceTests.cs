using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Platform;

namespace FileCat.App.Tests;

/// <summary>
/// Windows' Recycle Bin among the places (the owner: "Recycle Bin icon is missing in panels. expected behavior is that it
/// opens the Recycle Bin windows, since only it can work with files inside"). A test platform's Shell stands in for
/// Windows': nothing opens on the desktop, the Shell is only asked.
/// </summary>
public sealed class RecycleBinPlaceTests
{
    private sealed class RecordingShell : PortableShellServices
    {
        public int Opened;
        public bool? HasItems;

        public override bool CanOpenRecycleBin => true;

        public override bool? RecycleBinHasItems() => HasItems;

        public override void OpenRecycleBin() => Opened++;
    }

    private sealed class TestPlatform : PortablePlatform
    {
        public TestPlatform(IShellServices shell) => Shell = shell;
    }

    [AvaloniaFact]
    public async Task The_Recycle_Bin_is_a_place_that_opens_its_own_window_and_no_tab()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' Recycle Bin."); return; }
        var shell = new RecordingShell { HasItems = true };
        var platformBefore = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new TestPlatform(shell);
        try
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                var places = vm.Places(vm.DriveButtons);
                var bin = Assert.Single(places, p => p.Title == "Recycle Bin");
                // Beside Downloads, where the owner wants it.
                int downloads = places.FindIndex(p => p.Title == "Downloads");
                if (downloads >= 0) Assert.Equal(downloads + 1, places.IndexOf(bin));
                Assert.Null(bin.Location);
                Assert.Equal(PlaceGroup.Folders, bin.Group);
                Assert.False(bin.OpensInPanel);
                Assert.NotNull(bin.Icon());
                var panel = vm.Workspace.Panels[0];
                var where = panel.ActiveTab!.Location;
                int tabs = panel.Tabs.Count;
                await vm.OpenPlaceAsync(panel, bin, newTab: false);
                // Asked for a new tab, too: the window opens, and no tab does.
                await vm.OpenPlaceAsync(panel, bin, newTab: true);
                Assert.Equal(2, shell.Opened);
                Assert.Equal(where, panel.ActiveTab!.Location);
                Assert.Equal(tabs, panel.Tabs.Count);
            }
            finally
            {
                AccessibilityTests.Close(services, window, root);
            }
        }
        finally
        {
            PlatformFactory.WindowsFactory = platformBefore;
        }
    }

    [AvaloniaFact]
    public void Where_the_system_has_no_such_window_there_is_no_such_place()
    {
        // The portable platform the tests run on: no recycle bin window to open.
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            Assert.False(services.Shell.CanOpenRecycleBin);
            Assert.DoesNotContain(vm.Places(vm.DriveButtons), p => p.Title == "Recycle Bin");
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
