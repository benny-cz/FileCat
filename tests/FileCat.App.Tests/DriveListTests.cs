using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>This PC follows drives that come and go; a tab on a drive that is gone shows This PC (plan §8.2).</summary>
public sealed class DriveListTests
{
    /// <summary>The platform's drives plus folders that stand in for drives (the test decides when they come and go).</summary>
    private sealed class TestDrives(IEnumerable<string> roots) : ComputerProvider
    {
        public List<string> Roots { get; } = [.. roots];

        protected override IReadOnlyList<string> GetDriveRoots()
        {
            lock (Roots) return [.. Roots];
        }

        protected override DriveTag QueryDrive(string root) => new(root, "Test", "Fixed", "NTFS", 1L << 30, 4L << 30, true);
    }

    [AvaloniaFact]
    public async Task This_PC_follows_drives_and_a_tab_leaves_a_drive_that_is_gone()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string stick = Directory.CreateDirectory(Path.Combine(root, "stick")).FullName;
            string card = Directory.CreateDirectory(Path.Combine(root, "card")).FullName;
            string inside = Directory.CreateDirectory(Path.Combine(stick, "photos")).FullName;
            var real = (ComputerProvider)services.Providers.Get(Schemes.Computer);
            var drives = new TestDrives(real.CurrentRoots());
            services.Providers.Register(drives);
            services.Drives.Check(); // the list the watcher knows is now this one

            string Name(string path) => OperatingSystem.IsWindows() ? path.TrimEnd('\\') : path;
            var thisPc = vm.Workspace.Panels[0].ActiveTab!;
            thisPc.Navigate(new Location(Schemes.Computer, string.Empty));
            var onStick = vm.Workspace.Panels[1].ActiveTab!;
            bool Lists(string name) => thisPc.Listing.State == Core.Listing.ListingState.Complete && thisPc.Listing.FindStoreIndex(name) >= 0;
            for (int i = 0; i < 250 && thisPc.Listing.State != Core.Listing.ListingState.Complete; i++) await Task.Delay(20, ct);
            Assert.False(Lists(Name(stick)));

            // A drive arrives: This PC lists it.
            lock (drives.Roots) drives.Roots.AddRange([stick, card]);
            services.Drives.Check();
            for (int i = 0; i < 250 && !Lists(Name(stick)); i++) await Task.Delay(20, ct);
            Assert.True(Lists(Name(card)));
            // The status line says what the focused drive is and how full.
            thisPc.Listing.SetFocus(thisPc.Listing.GetVisibleIndex(thisPc.Listing.FindStoreIndex(Name(card))));
            Assert.Equal($"{Name(card)} Test · NTFS · {Formatters.SizeWithUnit(1L << 30)} free of {Formatters.SizeWithUnit(4L << 30)}, 75% used", thisPc.StatusRight);

            // A drive goes while a tab shows a folder on it: that tab shows This PC, and says why.
            onStick.Navigate(Location.FileSystem(inside));
            for (int i = 0; i < 250 && onStick.Listing.State != Core.Listing.ListingState.Complete; i++) await Task.Delay(20, ct);
            lock (drives.Roots) drives.Roots.Remove(stick);
            services.Drives.Check();
            for (int i = 0; i < 250 && onStick.Location?.Scheme != Schemes.Computer; i++) await Task.Delay(20, ct);
            Assert.Equal(Schemes.Computer, onStick.Location!.Scheme);
            Assert.Contains("was removed", vm.Notification);
            for (int i = 0; i < 250 && Lists(Name(stick)); i++) await Task.Delay(20, ct);
            Assert.False(Lists(Name(stick)));
            Assert.True(Lists(Name(card)));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
