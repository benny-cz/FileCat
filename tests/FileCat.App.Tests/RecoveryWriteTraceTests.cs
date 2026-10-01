using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V09 (I09): a whole recovery session the way a user goes through it, run under a write trace (Process
/// Monitor) on a disposable machine. FILECAT_V09_DISK names a physical disk to scan (or FILECAT_V09_DRIVE a drive such as
/// C:\), FILECAT_V09_DATA the folder FileCat keeps its files in (--data; left out, its usual places),
/// FILECAT_V09_OUT where a recovered file goes, FILECAT_V09_EXPECT "scan" or "refuse", FILECAT_V09_WAIT how long to stay
/// open afterwards (seconds, for the timed saves). Runs as administrator, where FileCat reads drives itself.
/// </summary>
public sealed class RecoveryWriteTraceTests
{
    [AvaloniaFact]
    public async Task A_recovery_session_writes_only_where_it_should()
    {
        string? disk = Environment.GetEnvironmentVariable("FILECAT_V09_DISK");
        string? drive = Environment.GetEnvironmentVariable("FILECAT_V09_DRIVE");
        // Linux and macOS: a device as UnixDisks lists it (/dev/loop5, /dev/sda, /dev/rdisk4), read directly (root, or
        // a device this user may read).
        string? unix = Environment.GetEnvironmentVariable("FILECAT_V09_UNIX_DEVICE");
        string? expect = Environment.GetEnvironmentVariable("FILECAT_V09_EXPECT");
        string? output = Environment.GetEnvironmentVariable("FILECAT_V09_OUT");
        bool windows = OperatingSystem.IsWindows() && (disk is not null || drive is not null);
        if (!windows && (unix is null || OperatingSystem.IsWindows()) || expect is not ("scan" or "refuse") || output is null)
            Assert.Skip("Set FILECAT_V09_DISK or FILECAT_V09_DRIVE (Windows) or FILECAT_V09_UNIX_DEVICE, FILECAT_V09_OUT and FILECAT_V09_EXPECT (scan, refuse); FILECAT_V09_DATA and FILECAT_V09_WAIT as needed.");
        if (windows && !Environment.IsPrivilegedProcess) Assert.Skip("Reading a drive without the installed helper needs administrator rights.");
        string? data = Environment.GetEnvironmentVariable("FILECAT_V09_DATA");
        int wait = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_V09_WAIT"), out int w) ? w : 70;
        var log = TestContext.Current.TestOutputHelper;
        void Log(string text) => log?.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} {text}");
        var ct = TestContext.Current.CancellationToken;

        var paths = data is null ? AppPaths.Resolve() : AppPaths.Resolve(dataRoot: data);
        Log($"FileCat's folders: settings {paths.SettingsDirectory}; local {paths.LocalDirectory}; scratch {paths.ListingScratchDirectory}; hex {paths.HexRecoveryDirectory}");
        var services = AppServices.CreateForPaths(paths);
        int opened = 0;
        var open = services.Recovery.OpenDevice!;
        services.Recovery.OpenDevice = (device, name, token) =>
        {
            Interlocked.Increment(ref opened);
            Log($"opening {device} ({name})");
            return open(device, name, token);
        };
        var vm = new ViewModels.MainViewModel(services);
        var window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
        vm.Initialize(null);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dialogs = (OverlayDialogService)vm.Dialogs;
        var panel = vm.Workspace.ActivePanel!;
        try
        {
            Task flow;
            if (!windows)
            {
                var device = FileCat.Recovery.Unix.UnixDisks.List().Single(d => d.Device == unix);
                Log($"{device.Device}: {device.Model}, {device.Bus}, {device.Length:N0} bytes; mounted at {string.Join(", ", device.MountPoints)}");
                flow = vm.FindDeletedOnUnixDeviceAsync(panel, device, null);
            }
            else if (disk is not null)
            {
                var chosen = FileCat.Platform.Windows.Recovery.DeviceTopology.Disks().Single(d => d.Number == int.Parse(disk, System.Globalization.CultureInfo.InvariantCulture));
                Log($"disk {chosen.Number}: {chosen.Model}, {chosen.Bus}, {chosen.Length:N0} bytes");
                flow = vm.FindDeletedOnDiskAsync(panel, chosen);
            }
            else
            {
                var info = new DriveInfo(drive!);
                flow = vm.FindDeletedOnDriveAsync(panel, new DriveTag(info.RootDirectory.FullName, info.VolumeLabel, info.DriveType.ToString(), info.DriveFormat,
                    info.AvailableFreeSpace, info.TotalSize, info.IsReady), null);
            }
            for (int i = 0; i < 1500 && !dialogs.IsOpen && !flow.IsCompleted; i++) await Task.Delay(20, ct);
            await Task.Delay(100, ct);
            Assert.True(dialogs.IsOpen, "No question was asked.");
            var texts = window.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();
            Log("asked: " + string.Join(" | ", texts.Where(t => t.Contains("scan", StringComparison.OrdinalIgnoreCase) || t.Contains("--data", StringComparison.Ordinal))));
            bool refused = window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Copy the command" && b.IsEffectivelyVisible);
            if (expect == "refuse")
            {
                Assert.True(refused, "The scan was not refused.");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await flow;
                Assert.Equal(0, opened);
                Log("refused; nothing opened");
                return;
            }
            Assert.False(refused, "The scan was refused.");
            var before = vm.ActiveTab;
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await flow;
            var scan = vm.ActiveTab!;
            Assert.NotSame(before, scan);
            async Task Complete()
            {
                for (int i = 0; i < 30_000 && scan.Listing.State is ListingState.Loading or ListingState.Empty; i++) await Task.Delay(20, ct);
                Assert.True(scan.Listing.State == ListingState.Complete, scan.Listing.Error);
            }
            await Complete();
            Log($"scanned: {scan.Listing.VisibleCount} rows at {services.Recovery.GetDisplayPath(scan.Location!)}");
            if (scan.Location!.Session is null)
            {
                // A whole disk lists its volumes: the first one that has deleted items.
                var volume = Enumerable.Range(0, scan.Listing.VisibleCount).Select(scan.Listing.GetVisible).First(e => e.Kind != EntryKind.Parent);
                scan.Navigate(services.Recovery.GetChildLocation(scan.Location!, volume)!);
                await Complete();
                Log($"volume: {scan.Listing.VisibleCount} rows at {services.Recovery.GetDisplayPath(scan.Location!)}");
            }
            // FILECAT_V09_RECOVER=0: the scan only (a source that may hold no deleted file, such as a system's EFI partition).
            if (Environment.GetEnvironmentVariable("FILECAT_V09_RECOVER") == "0")
            {
                Log($"scan only; waiting {wait} s with it open");
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                return;
            }
            // A deleted file whose content is there: viewed (F3), then recovered (F5's job) to the output folder.
            int file = -1;
            for (int depth = 0; depth < 4 && file < 0; depth++)
            {
                file = Enumerable.Range(0, scan.Listing.VisibleCount).FirstOrDefault(i => scan.Listing.GetVisible(i) is { Kind: EntryKind.File } e && !e.Has(EntryFlags.Unavailable) && e.Size > 0, -1);
                if (file >= 0) break;
                int folder = Enumerable.Range(0, scan.Listing.VisibleCount).FirstOrDefault(i => scan.Listing.GetVisible(i).Kind == EntryKind.Directory, -1);
                if (folder < 0) break;
                scan.Navigate(services.Recovery.GetChildLocation(scan.Location!, scan.Listing.GetVisible(folder))!);
                await Complete();
            }
            Assert.True(file >= 0, "No deleted file with its content was found: " + string.Join("; ", Enumerable.Range(0, scan.Listing.VisibleCount)
                .Select(scan.Listing.GetVisible).Select(e => $"{e.Name} {e.Kind} {e.Size} {e.Flags}")));
            scan.Listing.SetFocus(file);
            var item = scan.Listing.GetItemRef(scan.Listing.FocusedStoreIndex);
            Log($"recovering {item.Name} ({scan.Listing.GetVisible(file).Size:N0} bytes)");
            // Its content read as the viewer reads it (F3's window itself is not drawn here: headless Avalonia's stand-in
            // text layout never ends on some text with empty lines, which a recovered file may well be).
            using (var content = services.Recovery.OpenContent(item))
            {
                long total = 0;
                var buffer = new byte[64 * 1024];
                for (int n; content is not null && total < 1 << 20 && (n = content.Read(total, buffer)) > 0;) total += n;
                Log($"read {total:N0} bytes as the viewer would");
            }
            Directory.CreateDirectory(output);
            var job = services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [item], Destination = FileCat.Core.Resources.Location.FileSystem(output) });
            for (int i = 0; i < 6000 && job.State is not (JobState.Completed or JobState.Failed or JobState.Canceled or JobState.Interrupted or JobState.AwaitingDecision); i++) await Task.Delay(20, ct);
            Log($"copy: {job.State}; {string.Join("; ", job.Issues.Select(x => x.Message))}");
            Assert.Equal(JobState.Completed, job.State);
            Log($"waiting {wait} s with the scan open");
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
        }
        finally
        {
            // Closing as a user does: the workspace saved, jobs ended, services disposed.
            foreach (var viewer in ViewerWindow.OpenWindows.ToList()) viewer.Close();
            vm.SaveWorkspace(null);
            foreach (var job in services.Jobs.Jobs) job.Cancel();
            window.Close();
            services.Recovery.CloseAll();
            services.Dispose();
            Log($"closed; devices opened: {opened}");
        }
    }
}
