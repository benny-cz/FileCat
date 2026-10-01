using Avalonia.Headless.XUnit;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;

namespace FileCat.App.Tests;

/// <summary>
/// V12: a folder's counted size lands only on that folder. One deleted and made again, or replaced, under the same name
/// while it was counted is another folder: its row used to take the first one's size as counted. The file system's own
/// identity of the folder (a file ID, an inode) tells them apart; a test platform gives the folder another identity
/// after the count began, as a replacement would.
/// </summary>
public sealed class FolderCountIdentityTests
{
    private sealed class Identities(bool replaced) : PortableFileOperations
    {
        private int _asked;

        public override string? GetFileIdentity(string path) => path.EndsWith("counted", StringComparison.Ordinal)
            ? Interlocked.Increment(ref _asked) == 1 || !replaced ? "first" : "second"
            : null;
    }

    private sealed class TestPlatform : PortablePlatform
    {
        public TestPlatform(bool replaced) => FileOperations = new Identities(replaced);
    }

    [AvaloniaFact]
    public Task A_size_counted_for_a_folder_since_replaced_is_not_shown_on_its_successor() => Count(replaced: true);

    [AvaloniaFact]
    public Task A_size_counted_for_the_same_folder_is_shown() => Count(replaced: false);

    private static async Task Count(bool replaced)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The test platform stands in for Windows' through its factory.");
            return;
        }
        var platformBefore = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new TestPlatform(replaced);
        try
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                var ct = TestContext.Current.CancellationToken;
                string counted = Directory.CreateDirectory(Path.Combine(root, "files", "counted")).FullName;
                File.WriteAllBytes(Path.Combine(counted, "one.bin"), new byte[1000]);
                File.WriteAllBytes(Path.Combine(counted, "two.bin"), new byte[234]);
                var tab = vm.ActiveTab!;
                tab.Refresh();
                var listing = tab.Listing;
                for (int i = 0; i < 300 && !(listing.State == ListingState.Complete && listing.FocusName("counted")); i++) await Task.Delay(20, ct);
                Assert.True(listing.FocusName("counted"));
                vm.CountFolderSizes(tab);
                for (int i = 0; i < 300 && tab.SizingFolders > 0; i++) await Task.Delay(20, ct);
                Assert.Equal(0, tab.SizingFolders);
                for (int i = 0; i < 50; i++) await Task.Delay(10, ct); // the result's own post to the window
                long size = Enumerable.Range(0, listing.VisibleCount).Select(listing.GetVisible).First(e => e.Name == "counted").Size;
                if (replaced)
                {
                    Assert.True(size < 0, $"the successor shows {size} bytes counted for the folder before it");
                    Assert.Contains("was replaced while its size was counted", vm.Notification);
                }
                else Assert.Equal(1234, size);
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
}
