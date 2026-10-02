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
            var safety = vm.CheckDiskSafety(@"\\.\PhysicalDrive9", "disk 9");
            Assert.NotNull(safety.Refusal);
            Assert.Contains("FileCat does not scan disk 9", safety.Refusal, StringComparison.Ordinal);
            Assert.Contains("the scratch of large listings (" + paths.ListingScratchDirectory + ")", safety.Refusal, StringComparison.Ordinal);
            Assert.DoesNotContain("settings and history", safety.Refusal, StringComparison.Ordinal);
            Assert.Contains("--data", safety.Command, StringComparison.Ordinal);
            Assert.Contains(safety.Command!, safety.Refusal, StringComparison.Ordinal);

            // A folder that cannot be placed counts as on the disk: refused as well.
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, paths.JournalDirectory) ? null : false;
            safety = vm.CheckDiskSafety(@"\\.\PhysicalDrive9", "disk 9");
            Assert.Contains("cannot tell", safety.Refusal, StringComparison.Ordinal);
            Assert.Contains("logs, journals, caches and temporary files", safety.Refusal, StringComparison.Ordinal);

            // All of FileCat's folders elsewhere: scanned. GnuPG's folder on that disk: gpg is held off while the scan is open.
            string gpg = Core.Verification.OpenPgp.Home();
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, gpg);
            safety = vm.CheckDiskSafety(@"\\.\PhysicalDrive9", "disk 9");
            Assert.Null(safety.Refusal);
            Assert.True(safety.PauseSignatures);
            Assert.Contains("GnuPG", safety.HeldOff, StringComparison.Ordinal);

            // Nothing on that disk: nothing held off.
            services.Recovery.SharesDisk = (_, _) => false;
            safety = vm.CheckDiskSafety(@"\\.\PhysicalDrive9", "disk 9");
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
            var sameDisk = vm.CheckDiskSafety("test-device", "test disk");
            Assert.Contains("the instance connection", sameDisk.Refusal);
            Assert.Contains(ipc, sameDisk.Refusal);
            services.Recovery.SharesDisk = (_, folder) => PathIn(folder, ipc) ? null : false;
            Assert.Contains("cannot tell", vm.CheckDiskSafety("test-device", "test disk").Refusal);
            services.Recovery.SharesDisk = (_, _) => false;
            Assert.Null(vm.CheckDiskSafety("test-device", "test disk").Refusal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
