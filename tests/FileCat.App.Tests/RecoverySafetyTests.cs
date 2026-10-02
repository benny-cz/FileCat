using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V09 (I09): FileCat does not scan a disk it writes to itself. It names its folders on that disk and the safe
/// way (started with all of its files elsewhere, --data); with its files elsewhere it scans, holding off what writes into
/// the user's folders on that disk.
/// </summary>
public sealed class RecoverySafetyTests
{
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
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
        }
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
