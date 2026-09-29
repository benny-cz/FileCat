using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.Core.Commands;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.App.Tests;

/// <summary>
/// Recover deleted files from a real drive, the way a user does it (read-only). FILECAT_RECOVERY_LIVE names the drive
/// (G:) and FILECAT_RECOVERY_LIVE_SERIAL its disk's serial number; any drive that is not a USB disk with exactly that
/// serial is refused. Runs only as administrator, where FileCat reads the drive itself.
/// </summary>
public sealed class LiveDriveScanTests
{
    private static string GuardedDrive()
    {
        string? drive = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE");
        string? serial = Environment.GetEnvironmentVariable("FILECAT_RECOVERY_LIVE_SERIAL");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(drive) || string.IsNullOrEmpty(serial))
            Assert.Skip("Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number).");
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                Assert.Skip("Scanning a drive without the installed helper needs the test to run as administrator.");
        string letter = drive.TrimEnd(':', '\\');
        var psi = new ProcessStartInfo("powershell", ["-NoProfile", "-Command",
            $"Get-Partition -DriveLetter {letter} | Get-Disk | ForEach-Object {{ \"$($_.BusType)|$($_.SerialNumber.Trim())\" }}"])
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        string line = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.True(line == "USB|" + serial, $"Refusing {drive}: its disk is {line}, not the USB disk with serial {serial}.");
        return letter + ":\\";
    }

    [AvaloniaFact]
    public async Task The_drive_of_the_current_folder_is_scanned_in_a_new_tab()
    {
        string root = GuardedDrive();
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
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && (!dialogs.IsOpen || !Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                     .OfType<Avalonia.Controls.Button>().Any(b => b.Content as string == "Scan" && b.IsEffectivelyVisible)); i++)
                await Task.Delay(20, ct);
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
