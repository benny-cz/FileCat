using Avalonia.Headless.XUnit;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// V12: a folder's count stops when its tab leaves the folder or closes. Its size could not land any more, and the tab's
/// next folder must not say it is counting, nor refuse Count, while the old count goes on. A test platform holds the
/// count at its start (the folder's identity is read before counting), as a slow disk or a dead share would.
/// </summary>
public sealed class FolderCountLeaveTests
{
    private sealed class Held : PortableFileOperations
    {
        public readonly ManualResetEventSlim Release = new();
        public readonly ManualResetEventSlim Asked = new();
        public int Asks;

        public override string? GetFileIdentity(string path)
        {
            if (!path.EndsWith("counted", StringComparison.Ordinal)) return null;
            Interlocked.Increment(ref Asks);
            Asked.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return "same";
        }
    }

    private sealed class TestPlatform : PortablePlatform
    {
        public TestPlatform(Held ops) => FileOperations = ops;
    }

    [AvaloniaFact]
    public Task Leaving_the_folder_stops_its_count() => Leave(close: false);

    [AvaloniaFact]
    public Task Closing_the_tab_stops_its_count() => Leave(close: true);

    private static async Task Leave(bool close)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The test platform stands in for Windows' through its factory.");
            return;
        }
        var held = new Held();
        var platformBefore = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new TestPlatform(held);
        try
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                var ct = TestContext.Current.CancellationToken;
                string files = Path.Combine(root, "files");
                File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(files, "counted")).FullName, "one.bin"), new byte[1000]);
                string elsewhere = Directory.CreateDirectory(Path.Combine(root, "elsewhere")).FullName;
                Directory.CreateDirectory(Path.Combine(elsewhere, "a"));
                var panel = vm.Workspace.Panels[0];
                var tab = close ? panel.OpenTab(Location.FileSystem(files)) : vm.ActiveTab!;
                tab.Refresh();
                var listing = tab.Listing;
                for (int i = 0; i < 300 && !(listing.State == ListingState.Complete && listing.FocusName("counted")); i++) await Task.Delay(20, ct);
                Assert.True(listing.FocusName("counted"));
                vm.CountFolderSizes(tab);
                Assert.True(held.Asked.Wait(TimeSpan.FromSeconds(10), ct), "the count did not start");
                Assert.Equal(1, tab.SizingFolders);

                if (close)
                {
                    panel.CloseTab(tab);
                    Assert.Equal(0, tab.SizingFolders);
                }
                else
                {
                    tab.Navigate(Location.FileSystem(elsewhere));
                    var next = tab.Listing;
                    for (int i = 0; i < 300 && !(next.State == ListingState.Complete && next.FocusName("a")); i++) await Task.Delay(20, ct);
                    // The next folder's own: nothing counting, and Count offered for a folder marked there.
                    Assert.Equal(0, tab.SizingFolders);
                    next.MarkNames(["a"], true);
                    tab.UpdateStatus();
                    Assert.DoesNotContain("counting", tab.StatusMarked);
                    Assert.True(tab.CanCountMarked, $"Count is refused in the next folder: \"{tab.StatusMarked}\"");
                }

                // Let the held count go on: it was stopped, and does not count the folder through.
                held.Release.Set();
                for (int i = 0; i < 50; i++) await Task.Delay(10, ct);
                Assert.Equal(1, held.Asks);
                Assert.Equal(0, tab.SizingFolders);
            }
            finally
            {
                held.Release.Set();
                AccessibilityTests.Close(services, window, root);
            }
        }
        finally
        {
            PlatformFactory.WindowsFactory = platformBefore;
        }
    }
}
