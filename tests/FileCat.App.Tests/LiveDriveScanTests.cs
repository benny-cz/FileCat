using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using FileCat.Recovery;
using FileCat.Tests;

namespace FileCat.App.Tests;

/// <summary>
/// Recover deleted files from a real drive, the way a user does it (read-only). FILECAT_RECOVERY_LIVE names the drive
/// (G:) and FILECAT_RECOVERY_LIVE_SERIAL its disk's serial number; any drive that is not a USB disk with exactly that
/// serial is refused. Runs only as administrator, where FileCat reads the drive itself.
/// </summary>
public sealed class LiveDriveScanTests
{
    [AvaloniaFact]
    public async Task The_drive_of_the_current_folder_is_scanned_in_a_new_tab()
    {
        using var usb = LiveUsbGuard.Capture();
        if (!Environment.IsPrivilegedProcess) Assert.Skip("Scanning a drive without the installed helper needs administrator rights.");
        string root = usb.Drive + "\\";
        var (services, vm, window, temp) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var folderTab = vm.ActiveTab!;
            folderTab.Navigate(Location.FileSystem(root));
            for (int i = 0; i < 250 && folderTab.Listing.State != Core.Listing.ListingState.Complete; i++) await Task.Delay(20, ct);
            var dialogs = (Views.OverlayDialogService)vm.Dialogs;
            vm.Execute(CommandIds.FindDeleted);
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            await Task.Delay(100, ct);
            // The chooser has this folder's drive chosen; Enter asks to confirm, and Enter scans.
            usb.Recheck();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && (!dialogs.IsOpen || !Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                     .OfType<Avalonia.Controls.Button>().Any(b => b.Content as string == "Scan" && b.IsEffectivelyVisible)); i++)
                await Task.Delay(20, ct);
            usb.Recheck();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 500 && ReferenceEquals(vm.ActiveTab, folderTab); i++) await Task.Delay(20, ct);
            var scan = vm.ActiveTab!;
            Assert.NotSame(folderTab, scan);
            Assert.Equal(root, folderTab.Location!.Path);
            Assert.True(RecoveryProvider.IsDevice(scan.Location!));
            for (int i = 0; i < 3000 && scan.Listing.State == Core.Listing.ListingState.Loading; i++) await Task.Delay(20, ct);
            Assert.True(scan.Listing.State == Core.Listing.ListingState.Complete, scan.Listing.Error);
            Assert.Contains(scan.Listing.Issues, issue => issue.Contains("This drive is in use while FileCat reads it", StringComparison.Ordinal));
        }
        finally
        {
            services.Recovery.CloseAll();
            AccessibilityTests.Close(services, window, temp);
        }
    }
}
