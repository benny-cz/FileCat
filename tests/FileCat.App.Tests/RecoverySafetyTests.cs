using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.State;
using FileCat.Recovery;
using FileCat.Recovery.Unix;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V09 (I09): FileCat does not scan a disk it writes to itself. It names its folders on that disk and the safe
/// way (started with all of its files elsewhere, --data); with its files elsewhere it scans, holding off what writes into
/// the user's folders on that disk.
/// </summary>
public sealed class RecoverySafetyTests
{
    [Fact]
    public void Mounted_mapped_file_system_is_a_recovery_volume_not_a_whole_disk()
    {
        var mapped = new UnixBlockDevice("/dev/dm-0", 536870912, "stripe", "mapped volume", false, null,
            ["/mnt/stripe"], "vfat") { IsLayeredVolume = true };
        Assert.True(MainViewModel.IsRecoverableVolume(mapped));
        Assert.False(MainViewModel.IsRecoverableVolume(mapped with { MountPoints = [] }));
        Assert.False(MainViewModel.IsRecoverableVolume(mapped with { FileSystem = "ext4" }));
        Assert.False(MainViewModel.IsRecoverableVolume(mapped with { IsLayeredVolume = false }));
        Assert.False(MainViewModel.IsWholeSourceChoice(mapped));
        Assert.True(MainViewModel.IsWholeSourceChoice(mapped with { MountPoints = [], FileSystem = null }));
    }

    [AvaloniaFact]
    public async Task Drive_letter_scan_refuses_a_replaced_backing_before_reading_it()
        => await RunReplacedDriveSelectionAsync(changedDiskNumber: false);

    [AvaloniaFact]
    public async Task Drive_letter_scan_refuses_a_reused_interface_with_a_new_disk_number()
        => await RunReplacedDriveSelectionAsync(changedDiskNumber: true);

    [AvaloniaFact]
    public async Task Whole_disk_scan_refuses_a_reader_without_the_selected_disk_number()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows disk interfaces are required.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var selected = Platform.Windows.Recovery.DeviceTopology.Disks().FirstOrDefault(d =>
                d.InterfacePath is not null && d.Identity is not null && d.Length > 0);
            if (selected is null) Assert.Skip("The test host has no verifiable disk interface.");
            var reader = new ReplacedReader(selected.Identity!, selected.Length);
            int opens = 0;
            services.Recovery.SharesDisk = (_, _) => false;
            services.Recovery.OpenDevice = (_, _, _) => { Interlocked.Increment(ref opens); return reader; };
            vm.HasWindowsRecoveryBroker = _ => true;
            vm.Dialogs = new AdmissionDialogs(() => { });
            var panel = vm.Workspace.Panels[0];
            var original = panel.ActiveTab;

            await vm.FindDeletedOnDiskAsync(panel, selected, () => false);
            Assert.NotSame(original, panel.ActiveTab);
            for (int i = 0; i < 250 && (Volatile.Read(ref opens) == 0 ||
                panel.ActiveTab!.Listing.State is ListingState.Loading or ListingState.Empty); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);

            Assert.Equal(1, opens);
            Assert.Equal(0, reader.Reads);
            Assert.Equal(1, reader.Disposals);
            Assert.Equal(ListingState.Failed, panel.ActiveTab!.Listing.State);
            Assert.Contains("identity changed", panel.ActiveTab!.Listing.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task RunReplacedDriveSelectionAsync(bool changedDiskNumber)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows volume selection is required.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string drive = Path.GetPathRoot(root)!;
            string? device = Platform.Windows.Recovery.DeviceTopology.VolumeDevice(drive);
            var numbers = device is null ? null : Platform.Windows.Recovery.DeviceTopology.DisksOf(device);
            var selected = numbers is { Count: 1 }
                ? Platform.Windows.Recovery.DeviceTopology.Disks().SingleOrDefault(d => d.Number == numbers[0])
                : null;
            if (Platform.Windows.Recovery.SelectedDiskInterfaceSource.VolumeSelectionIdentity(selected) is null)
                Assert.Skip("The test volume has no verifiable single-disk interface identity.");
            string replacementIdentity = changedDiskNumber
                ? Assert.IsType<string>(Platform.Windows.Recovery.SelectedDiskInterfaceSource.VolumeSelectionIdentity(
                    selected! with { Number = selected.Number == int.MaxValue ? selected.Number - 1 : selected.Number + 1 }))
                : "volume|replacement-interface|same-disk-guid";
            var reader = new ReplacedReader(replacementIdentity);
            int opens = 0;
            services.Recovery.SharesDisk = (_, _) => false;
            services.Recovery.OpenDevice = (_, _, _) => { Interlocked.Increment(ref opens); return reader; };
            vm.HasWindowsRecoveryBroker = _ => true;
            vm.Dialogs = new AdmissionDialogs(() => { });
            var panel = vm.Workspace.Panels[0];
            var original = panel.ActiveTab;

            await vm.FindDeletedOnDriveAsync(panel,
                new Core.FileSystem.DriveTag(drive, "fixture", "Fixed", "NTFS", 0, 0, true), null, () => false);
            Assert.NotSame(original, panel.ActiveTab);
            for (int i = 0; i < 250 && (Volatile.Read(ref opens) == 0 ||
                panel.ActiveTab!.Listing.State is ListingState.Loading or ListingState.Empty); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);

            Assert.Equal(1, opens);
            Assert.Equal(0, reader.Reads);
            Assert.Equal(1, reader.Disposals);
            Assert.Equal(ListingState.Failed, panel.ActiveTab!.Listing.State);
            Assert.Contains("identity changed", panel.ActiveTab!.Listing.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class ReplacedReader(string selectionIdentity, long length = 64 * 1024 * 1024) : IBlockSource, IDeviceSelectionIdentity
    {
        public string Description => "byte-cloned replacement";
        public long Length => length;
        public string? SelectionIdentity => selectionIdentity;
        public int Reads { get; private set; }
        public int Disposals { get; private set; }
        public int Read(long offset, Span<byte> buffer)
        {
            Reads++;
            int count = (int)Math.Min(buffer.Length, Length - offset);
            buffer[..count].Clear();
            return count;
        }
        public void Dispose() => Disposals++;
    }

    [AvaloniaTheory]
    [InlineData("windows-drive", true)]
    [InlineData("windows-drive", null)]
    [InlineData("windows-drive", false)]
    [InlineData("windows-disk", true)]
    [InlineData("windows-disk", null)]
    [InlineData("windows-disk", false)]
    [InlineData("unix-disk", true)]
    [InlineData("unix-disk", null)]
    [InlineData("unix-disk", false)]
    [InlineData("unix-partition", true)]
    [InlineData("unix-partition", null)]
    [InlineData("unix-partition", false)]
    public async Task Device_admission_rechecks_a_process_inventory_changed_during_confirmation(string route, bool? lateProcess)
    {
        if (route.StartsWith("windows-", StringComparison.Ordinal) && !OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows device admission routes need Windows; their device reader is replaced.");
            return;
        }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        int state = 0, opens = 0, initialChecks = 0;
        var checkedSources = new System.Collections.Concurrent.ConcurrentQueue<string>();
        bool? Census()
        {
            Interlocked.Increment(ref initialChecks);
            return Volatile.Read(ref state) switch { 0 => false, 1 => true, _ => null };
        }
        var dialogs = new AdmissionDialogs(() =>
        {
            Assert.Equal(1, Volatile.Read(ref initialChecks));
            Volatile.Write(ref state, lateProcess == false ? 0 : lateProcess == true ? 1 : 2);
        });
        vm.Dialogs = dialogs;
        services.Recovery.SharesDisk = (device, _) => { checkedSources.Enqueue(device); return false; };
        // Recording admission oracle: no actual device is opened, even by the failing baseline.
        services.Recovery.OpenDevice = (_, _, _) =>
        {
            Interlocked.Increment(ref opens);
            throw new IOException("Recording fixture reader; no device access.");
        };
        vm.HasWindowsRecoveryBroker = _ => true;
        var panel = vm.Workspace.Panels[0];
        var original = panel.ActiveTab;
        try
        {
            if (route == "windows-drive")
            {
                string drive = Path.GetPathRoot(root)!;
                await vm.FindDeletedOnDriveAsync(panel, new Core.FileSystem.DriveTag(drive, "fixture", "Fixed", "NTFS", 0, 0, true), null, Census);
            }
            else if (route == "windows-disk")
                await vm.FindDeletedOnDiskAsync(panel, new Platform.Windows.Recovery.PhysicalDisk(99999, 1024, "fixture", "virtual", false, [], "recording-identity", @"\\?\recording-disk-interface"), Census);
            else
            {
                string image = Path.Combine(root, "device-reader-fixture.img");
                File.WriteAllBytes(image, new byte[1024]);
                var device = new FileCat.Recovery.Unix.UnixBlockDevice(image, 1024, "fixture", "virtual", false,
                    route == "unix-partition" ? "fixture-disk" : null, [], null)
                {
                    Identity = Core.FileSystem.UnixFiles.Stat(image, followLinks: true)?.Identity.ToString() ?? "recording-identity",
                };
                await vm.FindDeletedOnUnixDeviceAsync(panel, device, null, Census);
            }
            if (route == "windows-disk")
            {
                Assert.NotEmpty(checkedSources);
                Assert.All(checkedSources, device => Assert.Equal(@"\\?\recording-disk-interface", device));
            }
            Assert.Equal(1, dialogs.ScanConfirmations);
            Assert.Contains(dialogs.KeyPoints, point => point.StartsWith("Source: ", StringComparison.Ordinal));
            Assert.Contains(dialogs.KeyPoints, point => point.Contains("Recover files to another disk", StringComparison.Ordinal));
            if (lateProcess == false)
            {
                Assert.NotSame(original, panel.ActiveTab);
                for (int i = 0; i < 250 && Volatile.Read(ref opens) == 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
                Assert.True(Volatile.Read(ref opens) > 0);
                Assert.Empty(dialogs.Alerts);
            }
            else
            {
                Assert.Same(original, panel.ActiveTab);
                Assert.Equal(0, Volatile.Read(ref opens));
                Assert.Contains(dialogs.Alerts, text => text.Contains("other FileCat", StringComparison.Ordinal));
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_protected_Unix_device_offers_read_only_authorization_before_the_scan()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || Environment.IsPrivilegedProcess)
            Assert.Skip("This tests ordinary Linux or macOS device access.");
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string path = Path.Combine(root, "protected-device.img");
            File.WriteAllBytes(path, new byte[1024]);
            File.SetUnixFileMode(path, UnixFileMode.None);
            var device = new FileCat.Recovery.Unix.UnixBlockDevice(path, 1024, "fixture", "virtual", false, "fixture-disk", [], null)
            {
                Identity = Core.FileSystem.UnixFiles.Stat(path, followLinks: true)?.Identity.ToString(),
            };
            var panel = vm.Workspace.Panels[0];
            var original = panel.ActiveTab;
            var dialogs = new AdmissionDialogs(() => { }, "Authorize and scan", approve: false);
            vm.Dialogs = dialogs;
            services.Recovery.SharesDisk = (_, _) => false;
            services.Recovery.OpenDevice = (_, _, _) => throw new InvalidOperationException("Cancel must leave the device unopened.");
            await vm.FindDeletedOnUnixDeviceAsync(panel, device, null, () => false);
            Assert.Equal(1, dialogs.ScanConfirmations);
            Assert.Contains(dialogs.KeyPoints, point => point.Contains("read-only access", StringComparison.Ordinal));
            Assert.Same(original, panel.ActiveTab);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class AdmissionDialogs(Action confirmed, string expectedAction = "Scan", bool approve = true) : IDialogService
    {
        public int ScanConfirmations { get; private set; }
        public IReadOnlyList<string> KeyPoints { get; private set; } = [];
        public List<string> Alerts { get; } = [];
        public bool IsOpen => false;
        public Task<bool> ConfirmReviewAsync(string title, string message, IReadOnlyList<string> keyPoints,
            string confirmText = "OK", bool danger = false, string cancelText = "Cancel")
        {
            KeyPoints = keyPoints;
            return ConfirmAsync(title, message, confirmText, danger, cancelText);
        }
        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel")
        {
            Assert.Equal(expectedAction, confirmText);
            ScanConfirmations++;
            confirmed();
            return Task.FromResult(approve);
        }
        public Task AlertAsync(string title, string message) { Alerts.Add(message); return Task.CompletedTask; }
        public Task<PromptResult?> PromptAsync(PromptOptions options) => throw new InvalidOperationException("Unexpected prompt");
        public Task<ChoiceResult> ChooseAsync(ChoiceOptions options) => throw new InvalidOperationException("Unexpected choice");
        public Task<KeyboardReferenceChoice?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands, string? selectedId = null, bool remapOnly = false) => throw new InvalidOperationException("Unexpected keyboard dialog");
        public Task<object?> ShowCustomAsync(string title, Avalonia.Controls.Control content, IReadOnlyList<DialogButton> buttons,
            Avalonia.Controls.Control? initialFocus = null, Func<bool>? canConfirm = null, DialogCloser? closer = null,
            bool resizable = false, Avalonia.Controls.Control? pinnedReview = null, DialogBehavior? behavior = null) => throw new InvalidOperationException("Unexpected custom dialog");
    }

    [AvaloniaFact]
    public void A_disk_that_holds_FileCats_own_files_is_not_scanned_and_the_safe_way_is_given()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var vm = new MainViewModel(services);
        var paths = services.Paths;
        try
        {
            // Only the scratch of large listings lies on the scanned disk: refused, saying so, with the command that is safe.
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, paths.ListingScratchDirectory);
            var safety = StorageSafety(vm, @"\\.\PhysicalDrive9", "disk 9");
            Assert.NotNull(safety.Refusal);
            Assert.Contains("FileCat does not scan disk 9", safety.Refusal, StringComparison.Ordinal);
            Assert.Contains("the scratch of large listings (" + paths.ListingScratchDirectory + ")", safety.Refusal, StringComparison.Ordinal);
            Assert.DoesNotContain("settings and history", safety.Refusal, StringComparison.Ordinal);
            Assert.Contains("--data", safety.Command, StringComparison.Ordinal);
            Assert.Contains(safety.Command!, safety.Refusal, StringComparison.Ordinal);

            // A folder that cannot be placed counts as on the disk: refused as well.
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, paths.JournalDirectory) ? null : false;
            safety = StorageSafety(vm, @"\\.\PhysicalDrive9", "disk 9");
            Assert.Contains("cannot tell", safety.Refusal, StringComparison.Ordinal);
            Assert.Contains("logs, journals, caches and temporary files", safety.Refusal, StringComparison.Ordinal);

            // All of FileCat's folders elsewhere: scanned. GnuPG's folder on that disk: gpg is held off while the scan is open.
            string gpg = Core.Verification.OpenPgp.Home();
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, gpg);
            safety = StorageSafety(vm, @"\\.\PhysicalDrive9", "disk 9");
            Assert.Null(safety.Refusal);
            Assert.True(safety.PauseSignatures);
            Assert.Contains("GnuPG", safety.HeldOff, StringComparison.Ordinal);

            // Nothing on that disk: nothing held off.
            services.Recovery.SharesDisk = (_, _) => false;
            safety = StorageSafety(vm, @"\\.\PhysicalDrive9", "disk 9");
            Assert.Equal((null, false, false), (safety.Refusal, safety.PauseShellPictures, safety.PauseSignatures));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void AnalysisScratchChoiceStaysWithTheLaunch()
    {
        string folder = Path.Combine(Path.GetTempPath(), "alternate scratch");
        var options = StartupOptions.Parse(["--analysis-scratch", folder, "files"]);
        Assert.Equal(folder, options.AnalysisScratchParent);
        Assert.Single(options.ToForwardArgs());
        Assert.DoesNotContain(folder, options.ToForwardArgs());
        var missing = StartupOptions.Parse(["--analysis-scratch", "--data", folder]);
        Assert.Equal("", missing.AnalysisScratchParent);
        Assert.Equal(Path.GetFullPath(folder), missing.DataRoot);
    }

    [Fact]
    public void Data_names_a_folder_of_its_own_and_another_instance()
    {
        var options = StartupOptions.Parse(["--data", "recovery data", @"C:\folder"]);
        Assert.Equal(Path.GetFullPath("recovery data"), options.DataRoot);
        Assert.Equal([@"C:\folder"], options.Locations);
        Assert.Null(StartupOptions.Parse(["--data"]).DataRoot);

        // The usual FileCat of a profile is told apart by its instance: one started with --data asks whether it runs.
        string profile = "recovery-test-" + Guid.NewGuid().ToString("N")[..8];
        Assert.False(SingleInstance.UsualInstanceRunning(profile));
        Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile, NewInstance = false }));
        SingleInstance.StartServer(profile, null);
        try
        {
            // Windows asks for the instance's mutex; Linux and macOS for its pipe, which leaves no files behind.
            bool running = false;
            for (int i = 0; i < 50 && !running; i++)
            {
                running = SingleInstance.UsualInstanceRunning(profile);
                if (!running) Thread.Sleep(20);
            }
            Assert.True(running);
        }
        finally
        {
            SingleInstance.Release();
        }
        // The server stops when its task sees the release: until then its pipe still takes connections (CI on Linux and
        // macOS asked at once and was answered).
        bool stopped = false;
        for (int i = 0; i < 50 && !stopped; i++)
        {
            stopped = !SingleInstance.UsualInstanceRunning(profile);
            if (!stopped) Thread.Sleep(20);
        }
        Assert.True(stopped);
    }

    /// <summary>
    /// Profile names that differ only in characters a folder name drops are one profile: its files and its instance
    /// (before, "Work!" and "Work" shared one profile's settings and journals as two instances at once; found reviewing V23
    /// B12). A name with nothing usable is the default profile.
    /// </summary>
    [Fact]
    public void Profile_names_that_name_one_folder_are_one_instance()
    {
        string profile = "work-" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), "filecat-profile-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(AppPaths.Resolve(profile, overrideRoot: root).SettingsDirectory, AppPaths.Resolve(profile + "!", overrideRoot: root).SettingsDirectory);
            Assert.Equal("default", AppPaths.ProfileFolderName(".."));
            Assert.Equal(AppPaths.Resolve(overrideRoot: root).SettingsDirectory, AppPaths.Resolve("..", overrideRoot: root).SettingsDirectory);
        }
        finally { Directory.Delete(root, recursive: true); }

        Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile + "!", NewInstance = false }));
        SingleInstance.StartServer(profile + "!", null);
        try
        {
            bool running = false;
            for (int i = 0; i < 50 && !running; i++)
            {
                running = SingleInstance.UsualInstanceRunning(profile);
                if (!running) Thread.Sleep(20);
            }
            Assert.True(running);
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    private static bool PathIn(string folder, string root) => Core.FileSystem.PathUtil.IsSameOrUnder(folder, root);

    // Storage cases control the process inventory separately; the census has its own live/unknown-inventory cases.
    private static MainViewModel.DiskSafety StorageSafety(MainViewModel vm, string device, string name, string? baseDirectory = null) =>
        vm.CheckDiskSafety(device, name, baseDirectory, () => false);

    [Fact]
    public void The_process_census_ignores_itself_detects_a_live_process_and_refuses_an_unavailable_inventory()
    {
        Assert.False(SingleInstance.OtherFileCatRunning(() => []));
        Assert.False(SingleInstance.OtherFileCatRunning(() => [System.Diagnostics.Process.GetCurrentProcess()]));
        Assert.Null(SingleInstance.OtherFileCatRunning(() => throw new System.ComponentModel.Win32Exception(5)));
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("ComSpec")! : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("set /p filecat_test_wait=");
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("read filecat_test_wait");
        }
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            Assert.True(SingleInstance.OtherFileCatRunning(() => [System.Diagnostics.Process.GetProcessById(child.Id)]));
            Assert.False(SingleInstance.OtherFileCatRunning(
                () => [System.Diagnostics.Process.GetProcessById(child.Id)], additionallyExcludedProcessId: child.Id));
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
        }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);

    // I658: while another account runs a process, an ordinary census must not answer that no FileCat runs. With the default
    // /proc it sees the process and cannot read its image. With /proc mounted hidepid=invisible or ptraceable it does not see
    // it at all, and with noaccess .NET leaves it out of its list, as it cannot read its /proc entry. Set
    // FILECAT_TEST_OTHER_ACCOUNT_PROCESS to such a process's ID, and remount /proc in each mode (I658's record).
    [Fact]
    public void A_census_beside_another_accounts_process_does_not_answer_none()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux accounts and /proc."); return; }
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_TEST_OTHER_ACCOUNT_PROCESS"), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int other))
        {
            Assert.Skip("Set FILECAT_TEST_OTHER_ACCOUNT_PROCESS to the ID of a process that another account runs.");
            return;
        }
        // The premise: the process runs and is not this account's, as signal 0 to it is refused (EPERM).
        Assert.True(Kill(other, 0) != 0 && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == 1,
            $"Process {other} is not another account's running process.");
        var all = System.Diagnostics.Process.GetProcesses();
        bool listed;
        try { listed = all.Any(process => process.Id == other); }
        finally { foreach (var process in all) process.Dispose(); }
        var reasons = new List<string>();
        bool? running = SingleInstance.OtherFileCatRunning(null, null, null, (pid, name, reason) => reasons.Add($"{pid} {name}: {reason}"));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Process {other} listed: {listed}; {all.Length} processes listed; census: {running switch { true => "running", false => "none", null => "unknown" }}; " +
            string.Join("; ", reasons));
        Assert.NotEqual(false, running);
    }

    // I658: in a PID namespace of its own, such as a container's, /proc shows none of the processes outside it, by any
    // account, root included. Set FILECAT_TEST_OWN_PID_NAMESPACE=1 where that holds, as in a container (I658's record).
    [Fact]
    public void A_census_in_a_PID_namespace_of_its_own_does_not_answer_none()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux PID namespaces."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_OWN_PID_NAMESPACE") != "1")
        {
            Assert.Skip("Set FILECAT_TEST_OWN_PID_NAMESPACE=1 in a PID namespace other than the system's.");
            return;
        }
        string? space = new FileInfo("/proc/self/ns/pid").LinkTarget;
        Assert.NotEqual("pid:[4026531836]", space); // The system's own PID namespace has this fixed inode number.
        var reasons = new List<string>();
        bool? running = SingleInstance.OtherFileCatRunning(null, null, null, (pid, name, reason) => reasons.Add($"{pid} {name}: {reason}"));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"PID namespace {space}; census: {running switch { true => "running", false => "none", null => "unknown" }}; " + string.Join("; ", reasons));
        Assert.NotEqual(false, running);
    }

    [Fact]
    public void The_Windows_idle_accounting_entry_is_skipped_before_its_inaccessible_liveness_query()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows idle process metadata.");
        Assert.False(SingleInstance.OtherFileCatRunning(
            () => [System.Diagnostics.Process.GetProcessById(0)],
            _ => throw new InvalidOperationException("PID 0 must never reach candidate identification.")));
    }

    [Fact]
    public void Privileged_recovery_census_accepts_only_the_matching_complete_reply()
    {
        const string nonce = "0123456789ABCDEF0123456789ABCDEF";
        Assert.False(RecoveryProcessCensus.ParseReply($"FileCatRecoveryProcessCensusV1:{nonce}:clear\n", nonce, 0));
        Assert.True(RecoveryProcessCensus.ParseReply($"FileCatRecoveryProcessCensusV1:{nonce}:running\n", nonce, 0));
        Assert.Null(RecoveryProcessCensus.ParseReply($"FileCatRecoveryProcessCensusV1:{nonce}:clear\n", nonce, 1));
        Assert.Null(RecoveryProcessCensus.ParseReply($"FileCatRecoveryProcessCensusV1:{nonce[..^1]}0:clear\n", nonce, 0));
        Assert.Null(RecoveryProcessCensus.ParseReply($"FileCatRecoveryProcessCensusV1:{nonce}:clear\nextra", nonce, 0));
        Assert.Null(RecoveryProcessCensus.ParseReply($"FileCatRecoveryProcessCensusV1:{nonce}:unknown\n", nonce, 0));
    }

    /// <summary>
    /// I689: on Windows without administrator rights the census cannot complete (other accounts' processes cannot be
    /// queried), so an unknown goes on to the administrator helper, which takes the census with full rights. A FileCat
    /// this census does see still refuses here; Linux still asks polkit's root census; elsewhere unknown still refuses.
    /// </summary>
    [Theory]
    [InlineData(null, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(null, false, false, null)]
    [InlineData(null, true, false, false)]
    [InlineData(true, true, false, true)]
    public void An_unknown_census_without_administrator_rights_on_Windows_defers_to_the_helper(bool? ordinary, bool linux,
        bool helperTakesCensus, bool? expected)
    {
        int asked = 0;
        bool? decided = RecoveryProcessCensus.Decide(() => ordinary, linux ? () => { asked++; return false; } : null, helperTakesCensus);
        Assert.Equal(expected, decided);
        Assert.Equal(linux && ordinary is null ? 1 : 0, asked);
    }

    [Fact]
    public void Privileged_recovery_census_retries_a_transient_unknown_but_never_assumes_a_persistent_one_is_clear()
    {
        var transient = new Queue<bool?>([null, false]);
        Assert.False(RecoveryProcessCensus.RepeatUncertain(transient.Dequeue, () => { }));
        var writer = new Queue<bool?>([null, true]);
        Assert.True(RecoveryProcessCensus.RepeatUncertain(writer.Dequeue, () => { }));
        int attempts = 0;
        Assert.Null(RecoveryProcessCensus.RepeatUncertain(() => { attempts++; return null; }, () => { }));
        Assert.Equal(3, attempts);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(null)]
    [InlineData(false)]
    public void Device_recovery_waits_for_other_FileCat_processes_even_with_its_own_folders_elsewhere(bool? otherRunning)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-other-process-" + Guid.NewGuid().ToString("N"));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(dataRoot: root));
        var vm = new MainViewModel(services);
        services.Recovery.SharesDisk = (_, _) => false;
        try
        {
            var safety = vm.CheckDiskSafety("test-device", "test disk", otherProcesses: () => otherRunning);
            if (otherRunning == false) Assert.Null(safety.Refusal);
            else
            {
                Assert.NotNull(safety.Refusal);
                Assert.Contains("other FileCat", safety.Refusal);
                Assert.Null(safety.Command);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Renamed_apphosts_are_identified_by_their_bound_entry_assembly_and_incomplete_reads_are_unknown()
    {
        string apphost = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FileCat.exe" : "FileCat");
        using (var input = File.OpenRead(apphost)) Assert.True(SingleInstance.IsFileCatAppHost(input));
        // Exercise a binding split across two reads, including its preceding path separator.
        var bytes = new byte[8300];
        System.Text.Encoding.UTF8.GetBytes("/FileCat.dll\0").CopyTo(bytes, 8187);
        using (var input = new MemoryStream(bytes)) Assert.True(SingleInstance.IsFileCatAppHost(input));
        using (var input = new MemoryStream("OtherFileCat.dll\0"u8.ToArray())) Assert.False(SingleInstance.IsFileCatAppHost(input));
        using (var input = new OversizeReadStream()) Assert.Null(SingleInstance.IsFileCatAppHost(input));
        using (var input = new MemoryStream(new byte[64 * 1024 * 1024 + 1])) Assert.False(SingleInstance.IsFileCatAppHost(input));
        using (var input = new MemoryStream(new byte[8 * 1024 * 1024])) Assert.False(SingleInstance.IsFileCatAppHost(input));
    }

    private sealed class OversizeReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => (long)SingleInstance.RecoveryProcessImageReadLimit + 1;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("An oversized image must not be read.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Only_a_known_kernel_thread_is_excluded_from_the_Linux_executable_inventory()
    {
        Assert.True(SingleInstance.KernelThreadFromStat("42 (kworker) S 1 2 3 4 5 2097152 0 0"));
        Assert.False(SingleInstance.KernelThreadFromStat("42 (a name ) with spaces) S 1 2 3 4 5 4194304 0 0"));
        Assert.Null(SingleInstance.KernelThreadFromStat("42 (missing flags) S 1"));
        Assert.Null(SingleInstance.KernelThreadFromStat("42 (invalid) S 1 2 3 4 5 unknown"));
    }

    [Fact]
    public void Only_a_Linux_process_whose_threads_have_all_exited_counts_as_gone()
    {
        Assert.Equal('Z', SingleInstance.StateFromStat("10210 (sd_espeak-ng-mb) Z 10198 10198 10198 0 -1 4227084 0"));
        Assert.Equal('S', SingleInstance.StateFromStat("42 (a name ) with spaces) S 1 2 3 4 5 4194304 0 0"));
        Assert.Null(SingleInstance.StateFromStat("42 (no state)"));
        Assert.Null(SingleInstance.StateFromStat("no command"));
        // A zombie is its process's only thread; a thread-group leader that exited while another thread runs is not gone.
        Assert.True(SingleInstance.ExitedFromStatus("Name:\tsd_espeak-ng-mb\nUmask:\t0002\nState:\tZ (zombie)\nTgid:\t10210\nThreads:\t1\n"));
        Assert.True(SingleInstance.ExitedFromStatus("State:\tX (dead)\nThreads:\t1\n"));
        Assert.False(SingleInstance.ExitedFromStatus("State:\tZ (zombie)\nThreads:\t2\n"));
        Assert.False(SingleInstance.ExitedFromStatus("State:\tS (sleeping)\nThreads:\t1\n"));
        Assert.Null(SingleInstance.ExitedFromStatus("State:\tZ (zombie)\n"));
        Assert.Null(SingleInstance.ExitedFromStatus("Threads:\t1\n"));
    }

    // I681: a zombie of any program, unreaped by its parent, has no image to identify, and made every Linux census unknown,
    // so recovery of a mounted disk was refused for as long as it stayed. The census now passes over it, but still not
    // over a thread-group leader that exited while another of its threads runs: that process can write.
    [Fact]
    public void A_Linux_census_passes_over_a_zombie_but_not_over_a_running_process_whose_leader_exited()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux zombies and /proc."); return; }
        // The shell starts a child that exits at once, then becomes sleep, which never reaps it: the child stays a zombie.
        using var holder = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            ArgumentList = { "-c", "sleep 0 & echo $!; exec sleep 30" },
        })!;
        try
        {
            int zombie = int.Parse(holder.StandardOutput.ReadLine()!, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(WaitForLinuxState(zombie, 'Z'), $"Process {zombie} did not become a zombie.");
            System.Diagnostics.Process[] Zombie() => [System.Diagnostics.Process.GetProcessById(zombie)];
            Assert.False(SingleInstance.OtherFileCatRunning(Zombie, inspectInventory: true, listingLimit: () => null));
        }
        finally
        {
            if (!holder.HasExited) holder.Kill();
            holder.WaitForExit();
        }

        string? python = new[] { "/usr/bin/python3", "/bin/python3" }.FirstOrDefault(File.Exists);
        if (python is null) { Assert.Skip("python3, to end a process's main thread while another runs."); return; }
        using var leader = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(python)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            ArgumentList = { "-c", "import ctypes, threading, time; threading.Thread(target=time.sleep, args=(30,)).start(); print('ready', flush=True); ctypes.CDLL(None).pthread_exit(None)" },
        })!;
        try
        {
            Assert.Equal("ready", leader.StandardOutput.ReadLine());
            Assert.True(WaitForLinuxState(leader.Id, 'Z'), $"Process {leader.Id}'s main thread did not exit.");
            System.Diagnostics.Process[] Running() => [System.Diagnostics.Process.GetProcessById(leader.Id)];
            Assert.NotEqual(false, SingleInstance.OtherFileCatRunning(Running, inspectInventory: true, listingLimit: () => null));
        }
        finally
        {
            if (!leader.HasExited) leader.Kill();
            leader.WaitForExit();
        }
    }

    private static bool WaitForLinuxState(int pid, char state)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            string? stat = null;
            try { stat = File.ReadAllText($"/proc/{pid.ToString(System.Globalization.CultureInfo.InvariantCulture)}/stat"); }
            catch (IOException) { }
            if (stat is not null && SingleInstance.StateFromStat(stat) == state) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    [Fact]
    public void An_unreadable_process_identity_is_unknown_but_a_known_FileCat_still_takes_precedence()
    {
        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("ComSpec")! : "/bin/sh",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            ArgumentList = { OperatingSystem.IsWindows() ? "/c" : "-c", OperatingSystem.IsWindows() ? "set /p filecat_wait=" : "read filecat_wait" },
        })!;
        try
        {
            System.Diagnostics.Process[] Inventory() => [System.Diagnostics.Process.GetProcessById(child.Id)];
            Assert.Null(SingleInstance.OtherFileCatRunning(Inventory, _ => null));
            Assert.Null(SingleInstance.OtherFileCatRunning(Inventory, _ => throw new UnauthorizedAccessException()));
            Assert.False(SingleInstance.OtherFileCatRunning(Inventory, _ => false));
            Assert.True(SingleInstance.OtherFileCatRunning(Inventory, _ => true));
            int calls = 0;
            Assert.True(SingleInstance.OtherFileCatRunning(() => [System.Diagnostics.Process.GetProcessById(child.Id),
                System.Diagnostics.Process.GetProcessById(child.Id)], _ => ++calls == 1 ? null : true));
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_portable_recovery_finds_the_per_user_owner_and_independent_windows(bool independent)
    {
        string profile = "portable-fallback-" + Guid.NewGuid().ToString("N")[..16];
        string root = Path.Combine(Path.GetTempPath(), "filecat-portable-probe-" + Guid.NewGuid().ToString("N"));
        var usual = AppPaths.Usual(profile);
        var previous = App.StartupOptions;
        Assert.False(Directory.Exists(usual.LocalDirectory));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, AppPaths.PortableMarker), "owned test marker");
        try
        {
            Assert.False(SingleInstance.UsualInstanceRunning(profile, root));
            Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile, NewInstance = independent }));
            SingleInstance.StartServer(profile, null);
            Assert.True(SingleInstance.UsualInstanceRunning(profile, root));
            Assert.True(SpinWait.SpinUntil(() => SingleInstance.UsualWriteFolders(profile, root) is not null, 5000));
            Assert.False(Directory.Exists(Path.Combine(root, "Data"))); // Discovery is read-only.
            string data = Path.Combine(root, "recovering-data");
            App.StartupOptions = new StartupOptions { Profile = profile, DataRoot = data };
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(profile, dataRoot: data));
            var vm = new MainViewModel(services);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, usual.SettingsDirectory);
            Assert.Contains("still running", StorageSafety(vm, "test-device", "test disk", root).Refusal);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, usual.SettingsDirectory) ? null : false;
            Assert.NotNull(StorageSafety(vm, "test-device", "test disk", root).Refusal);
            services.Recovery.SharesDisk = (_, _) => false;
            Assert.Null(StorageSafety(vm, "test-device", "test disk", root).Refusal);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, Path.Combine(root, "Data"));
            Assert.Null(StorageSafety(vm, "test-device", "test disk", root).Refusal); // Inactive portable state is not a writer.
            SingleInstance.Release();
            Assert.False(SingleInstance.UsualInstanceRunning(profile, root));
        }
        finally
        {
            SingleInstance.Release();
            App.StartupOptions = previous;
            foreach (string folder in new[] { root, usual.SettingsDirectory, usual.LocalDirectory,
                         usual.ListingScratchDirectory, usual.HexRecoveryDirectory }.Distinct())
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_usual_profile_catalog_includes_portable_profiles_and_the_distinct_DEFAULT_profile(bool defaultAlias)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-portable-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, AppPaths.PortableMarker), "owned test marker");
        string other = defaultAlias ? "DEFAULT" : "portable-owner-" + Guid.NewGuid().ToString("N")[..16];
        try
        {
            Directory.CreateDirectory(AppPaths.Usual(other, root).LocalDirectory);
            var profiles = SingleInstance.UsualProfiles("recovering", root);
            Assert.NotNull(profiles);
            Assert.Contains(other, profiles);
            Assert.Contains("default", profiles);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Case_aliases_of_one_Windows_profile_find_the_running_instance()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows case-insensitive profile directories and instance names.");
            return;
        }
        string profile = "profile-case-" + Guid.NewGuid().ToString("N")[..16];
        var usual = AppPaths.Resolve(profile);
        try
        {
            var alias = AppPaths.Resolve(profile.ToUpperInvariant());
            Assert.True(PathIn(usual.LocalDirectory, alias.LocalDirectory) && PathIn(alias.LocalDirectory, usual.LocalDirectory));
            Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile }));
            SingleInstance.StartServer(profile, null);
            Assert.True(SingleInstance.UsualInstanceRunning(profile));
            Assert.True(SingleInstance.UsualInstanceRunning(profile.ToUpperInvariant()));
        }
        finally
        {
            SingleInstance.Release();
            foreach (string folder in new[] { usual.SettingsDirectory, usual.LocalDirectory }.Distinct())
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_independent_instance_is_visible_until_release_and_stale_files_are_ignored()
    {
        string profile = "independent-" + Guid.NewGuid().ToString("N")[..16];
        var usual = AppPaths.Usual(profile);
        try
        {
            Assert.False(SingleInstance.UsualInstanceRunning(profile));
            Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile, NewInstance = true }));
            SingleInstance.StartServer(profile, null); // Independent windows advertise their lifetime, not a listener.
            Assert.True(SingleInstance.UsualInstanceRunning(profile));
            Assert.NotNull(SingleInstance.UsualWriteFolders(profile));
            Assert.False(File.Exists(Path.Combine(usual.LocalDirectory, "instance.pipe")));
            Assert.Single(Directory.GetFiles(usual.InstancesDirectory, "running-*.lock"));
            Assert.Single(Directory.GetFiles(usual.InstancesDirectory, "running-*.json"));
            string metadata = Directory.GetFiles(usual.InstancesDirectory, "running-*.json")[0];
            string valid = File.ReadAllText(metadata);
            File.WriteAllText(metadata, "{}");
            Assert.Null(SingleInstance.UsualWriteFolders(profile));
            File.Delete(metadata);
            Assert.Null(SingleInstance.UsualWriteFolders(profile));
            File.WriteAllText(metadata, valid);
            Assert.NotNull(SingleInstance.UsualWriteFolders(profile));
            SingleInstance.Release();
            Assert.False(SingleInstance.UsualInstanceRunning(profile));
            Assert.Empty(Directory.GetFiles(usual.InstancesDirectory, "running-*.json"));
            File.WriteAllText(Path.Combine(usual.InstancesDirectory, "running-stale.lock"), "");
            File.WriteAllText(Path.Combine(usual.InstancesDirectory, "running-stale.json"), "incomplete metadata from a stopped process");
            Assert.False(SingleInstance.UsualInstanceRunning(profile));
        }
        finally
        {
            SingleInstance.Release();
            foreach (string folder in new[] { usual.SettingsDirectory, usual.LocalDirectory }.Distinct())
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void A_usual_instance_with_another_profile_is_guarded_before_scanning()
    {
        string ownerProfile = "guard-owner-" + Guid.NewGuid().ToString("N")[..16];
        string recoveringProfile = "guard-other-" + Guid.NewGuid().ToString("N")[..16];
        string root = Path.Combine(Path.GetTempPath(), "filecat-other-profile-" + Guid.NewGuid().ToString("N"));
        var previous = App.StartupOptions;
        var usual = AppPaths.Usual(ownerProfile);
        try
        {
            App.StartupOptions = new StartupOptions { Profile = recoveringProfile, DataRoot = root };
            AppPaths.Resolve(ownerProfile); // The actual window creates its profile state on Windows too.
            Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = ownerProfile }));
            SingleInstance.StartServer(ownerProfile, null);
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(recoveringProfile, dataRoot: root));
            var vm = new MainViewModel(services);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, usual.LocalDirectory);
            Assert.Contains("still running", StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, usual.LocalDirectory) ? null : false;
            Assert.NotNull(StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, _) => false;
            // Unix metadata may still be publishing at first; wait for the actual locations before this control.
            Assert.True(SpinWait.SpinUntil(() => StorageSafety(vm, "test-device", "test disk").Refusal is null, 5000));
        }
        finally
        {
            SingleInstance.Release();
            App.StartupOptions = previous;
            foreach (string folder in new[] { root, usual.SettingsDirectory, usual.LocalDirectory }.Distinct())
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void An_instance_connection_outside_the_temporary_folder_is_guarded_before_scanning()
    {
        string? ipc = SingleInstance.ExtraWriteFolder;
        if (ipc is null)
        {
            Assert.Skip("Run with a Unix TMPDIR too long for a socket to require the private IPC fallback.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "filecat-ipc-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var vm = new MainViewModel(services);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, ipc);
            var sameDisk = StorageSafety(vm, "test-device", "test disk");
            Assert.Contains("the instance connection", sameDisk.Refusal);
            Assert.Contains(ipc, sameDisk.Refusal);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, ipc) ? null : false;
            Assert.Contains("cannot tell", StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, _) => false;
            Assert.Null(StorageSafety(vm, "test-device", "test disk").Refusal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public void A_running_usual_instances_connection_is_guarded_and_an_unknown_location_is_refused()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix profile-local socket metadata and recovery guard.");
            return;
        }
        string profile = "ipc-guard-" + Guid.NewGuid().ToString("N")[..16];
        string root = Path.Combine(Path.GetTempPath(), "filecat-usual-ipc-" + Guid.NewGuid().ToString("N"));
        var previous = App.StartupOptions;
        var usual = AppPaths.Usual(profile);
        try
        {
            string data = Path.Combine(root, "data");
            App.StartupOptions = new StartupOptions { Profile = profile, DataRoot = data };
            Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile }));
            SingleInstance.StartServer(profile, null);
            Assert.True(SpinWait.SpinUntil(() => SingleInstance.UsualWriteFolders(profile) is not null, 5000));
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(profile, dataRoot: data));
            var vm = new MainViewModel(services);
            // Model an ordinary instance whose actual socket is on a disk different from this process's TMPDIR.
            string metadata = Path.Combine(usual.LocalDirectory, "instance.pipe");
            string connection = "/filecat-test-connection-" + Guid.NewGuid().ToString("N");
            string temporary = "/filecat-test-temporary-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(metadata, System.Text.Json.JsonSerializer.Serialize(new
            {
                Pipe = connection + "/FileCat-0000000000000000", TemporaryFolder = temporary,
            }));
            services.Recovery.SharesDisk = (_, folder) => folder == connection;
            var safety = StorageSafety(vm, "test-device", "test disk");
            Assert.Contains("the running FileCat's instance connection", safety.Refusal);
            services.Recovery.SharesDisk = (_, folder) => folder == connection ? null : false;
            Assert.Contains("may be on that disk", StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, folder) => folder == temporary;
            Assert.Contains("the running FileCat's runtime temporary files", StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, folder) => folder == temporary ? null : false;
            Assert.Contains("may be on that disk", StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, _) => false;
            Assert.Null(StorageSafety(vm, "test-device", "test disk").Refusal);
            foreach (string invalid in new[] { "old plain metadata", "{}", "{\"Pipe\":\"/tmp/FileCat-0000000000000000\",\"TemporaryFolder\":\"relative\"}", new string('a', 32769) })
            {
                File.WriteAllText(metadata, invalid);
                Assert.Contains("cannot tell where", StorageSafety(vm, "test-device", "test disk").Refusal);
            }
            File.Delete(metadata);
            Assert.Contains("cannot tell where its instance connection and temporary files are kept", StorageSafety(vm, "test-device", "test disk").Refusal);
        }
        finally
        {
            SingleInstance.Release();
            App.StartupOptions = previous;
            foreach (string folder in new[] { root, usual.SettingsDirectory, usual.LocalDirectory })
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_runtime_temporary_folder_is_guarded_before_scanning()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix runtime filesystem endpoints.");
            return;
        }
        string temporary = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.Combine(Path.GetTempPath(), "filecat-runtime-safety-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var vm = new MainViewModel(services);
            bool IsRuntimeFolder(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) == temporary;
            services.Recovery.SharesDisk = (_, folder) => IsRuntimeFolder(folder);
            var safety = StorageSafety(vm, "test-device", "test disk");
            Assert.Contains("runtime temporary files", safety.Refusal);
            services.Recovery.SharesDisk = (_, folder) => IsRuntimeFolder(folder) ? null : false;
            Assert.Contains("cannot tell", StorageSafety(vm, "test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, _) => false;
            Assert.Null(StorageSafety(vm, "test-device", "test disk").Refusal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
