using System.Reflection;
using System.Runtime.InteropServices;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class SingleInstanceMetadataTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unix_fifo_at_an_instance_lock_keeps_recovery_safety_unknown(bool independent)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix instance-lock FIFO control."); return; }
        string profile = "instance-lock-fifo-" + Guid.NewGuid().ToString("N")[..16];
        var paths = AppPaths.Usual(profile);
        string directory = independent ? paths.InstancesDirectory : paths.LocalDirectory;
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, independent ? "running-" + Guid.NewGuid().ToString("N") + ".lock" : "instance.lock");
            Assert.Equal(0, MkFifo(file, 0x180));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(SingleInstance.UsualInstanceRunning(profile));
            Assert.Null(SingleInstance.UsualWriteFolders(profile));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Instance-safety census took {clock.Elapsed}.");
        }
        finally
        {
            foreach (string folder in new[] { paths.SettingsDirectory, paths.LocalDirectory }.Distinct())
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("OpenUnixLock", false)]
    [InlineData("OpenUnixLock", true)]
    [InlineData("OpenIndependentLock", false)]
    [InlineData("OpenIndependentLock", true)]
    public void Unix_instance_locks_refuse_a_fifo_without_waiting(string method, bool linked)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix instance-lock FIFO control."); return; }
        string root = Path.Combine(Path.GetTempPath(), "filecat-instance-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string fifo = Path.Combine(root, "owned-fifo");
            Assert.Equal(0, MkFifo(fifo, 0x180));
            string selected = linked ? Path.Combine(root, "instance.lock") : fifo;
            if (linked) File.CreateSymbolicLink(selected, fifo);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var error = Assert.Throws<TargetInvocationException>(() => OpenLock(method, selected, create: false));
            Assert.True(error.InnerException is IOException or UnauthorizedAccessException);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Instance-lock admission took {clock.Elapsed}.");

            string regular = Path.Combine(root, "regular.lock");
            File.WriteAllText(regular, string.Empty);
            using var held = Assert.IsType<FileStream>(OpenLock(method, regular, create: false));
            Assert.Null(OpenLock(method, regular, create: false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unix_active_instance_with_fifo_metadata_remains_unknown_to_recovery_safety()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix instance-metadata FIFO control."); return; }
        string profile = "instance-fifo-" + Guid.NewGuid().ToString("N")[..16];
        var paths = AppPaths.Usual(profile);
        try
        {
            Assert.False(SingleInstance.TryForward(new StartupOptions { Profile = profile, NewInstance = true }));
            SingleInstance.StartServer(profile, null);
            Assert.True(SingleInstance.UsualInstanceRunning(profile));
            Assert.NotNull(SingleInstance.UsualWriteFolders(profile));
            string metadata = Assert.Single(Directory.GetFiles(paths.InstancesDirectory, "running-*.json"));
            File.Delete(metadata);
            Assert.Equal(0, MkFifo(metadata, 0x180));

            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.Null(SingleInstance.UsualWriteFolders(profile));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Recovery-safety census took {clock.Elapsed}.");
        }
        finally
        {
            SingleInstance.Release();
            foreach (string folder in new[] { paths.SettingsDirectory, paths.LocalDirectory }.Distinct())
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unix_instance_metadata_refuses_an_unwritten_fifo_before_reading(bool linked)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix instance-metadata FIFO control."); return; }
        string root = Path.Combine(Path.GetTempPath(), "filecat-instance-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string fifo = Path.Combine(root, "owned-fifo");
        Assert.Equal(0, MkFifo(fifo, 0x180)); // owner-only 0600
        string selected = fifo;
        try
        {
            if (linked)
            {
                selected = Path.Combine(root, "instance.json");
                File.CreateSymbolicLink(selected, fifo);
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var error = Assert.Throws<TargetInvocationException>(() => Read(selected));
            Assert.IsType<IOException>(error.InnerException);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Instance metadata admission took {clock.Elapsed}.");

            string regular = Path.Combine(root, "regular.json");
            File.WriteAllText(regular, "{\"TemporaryFolder\":\"/tmp\"}");
            Assert.NotNull(Read(regular));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static object? Read(string path)
    {
        var method = typeof(SingleInstance).GetMethod("ReadInstanceMetadata", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(object));
        return method.Invoke(null, [path]);
    }

    private static object? OpenLock(string method, string path, bool create)
        => typeof(SingleInstance).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [path, create]);
}
