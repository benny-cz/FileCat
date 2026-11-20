using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

public sealed class LinuxArchiveCgroupTests
{
    [Fact]
    public void An_empty_nonce_is_refused_before_any_native_control_operation() =>
        Assert.Throws<ArgumentException>(() => LinuxArchiveCgroup.Create(Guid.Empty));

    [Fact]
    public void Unsupported_platforms_cannot_dispatch_the_Linux_controller()
    {
        if (OperatingSystem.IsLinux()) Assert.Skip("Unsupported-platform guard.");
        Assert.Throws<PlatformNotSupportedException>(() => LinuxArchiveCgroup.Create(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("max\n")]
    [InlineData("-1\n")]
    [InlineData("\n")]
    [InlineData("1\n2\n")]
    [InlineData("1\n0\nmalformed")]
    public void Non_numeric_control_values_cannot_touch_even_an_invalid_handle(string value)
    {
        using var invalid = new SafeFileHandle((nint)(-1), ownsHandle: false);
        Assert.Throws<ArgumentException>(() => LinuxArchiveNative.WriteAt(invalid, "memory.max", value));
    }

    [Fact]
    public void A_private_native_group_has_exact_limits_and_is_removed_only_when_empty()
    {
        RequireNative(); Guid nonce = Guid.NewGuid(); string path;
        using (var group = LinuxArchiveCgroup.Create(nonce))
        {
            path = group.Path; Assert.Equal(nonce, group.Nonce); Assert.EndsWith("filecat-archive-" + nonce.ToString("N"), group.RelativePath);
            group.VerifyLimits();
            Assert.Equal("0\n", group.ReadControl("memory.current")); Assert.Equal("0\n", group.ReadControl("pids.current"));
            Assert.Empty(group.ReadControl("cgroup.procs")); Assert.Contains("populated 0", group.ReadControl("cgroup.events"));
            Assert.Contains("usage_usec 0", group.ReadControl("cpu.stat"));
            Assert.Equal(LinuxArchiveCgroup.MemoryMaximum.ToString(System.Globalization.CultureInfo.InvariantCulture),
                File.ReadAllText(Path.Combine(path, "memory.max")).TrimEnd('\n'));
            group.Dispose(); group.Dispose();
            Assert.Throws<ObjectDisposedException>(() => group.VerifyLimits());
        }
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void A_nonce_collision_never_reuses_changes_or_removes_the_existing_kernel_group()
    {
        RequireNative(); Guid nonce = Guid.NewGuid();
        using var original = LinuxArchiveCgroup.Create(nonce);
        string before = File.ReadAllText(Path.Combine(original.Path, "memory.max"));
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable, Assert.Throws<ArchiveWriteException>(() => LinuxArchiveCgroup.Create(nonce)).Reason);
        original.VerifyLimits(); Assert.True(Directory.Exists(original.Path));
        Assert.Equal(before, File.ReadAllText(Path.Combine(original.Path, "memory.max")));
    }

    [Fact]
    public void Changed_native_limits_are_not_accepted_as_a_qualified_memory_boundary()
    {
        RequireNative(); using var group = LinuxArchiveCgroup.Create(Guid.NewGuid());
        File.WriteAllText(Path.Combine(group.Path, "memory.max"), (LinuxArchiveCgroup.MemoryMaximum + 4096).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable, Assert.Throws<ArchiveWriteException>(group.VerifyLimits).Reason);
    }

    [Fact]
    public void A_regular_filesystem_with_plausible_control_names_is_not_a_native_controller()
    {
        RequireNative(); using var root = new TempDir(); string value = "805306368\n";
        string canary = root.File("memory.max", value);
        using var held = LinuxArchiveNative.OpenDirectory(root.Path);
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable,
            Assert.Throws<ArchiveWriteException>(() => LinuxArchiveNative.RequireCgroupFileSystem(held)).Reason);
        Assert.Equal(value, File.ReadAllText(canary));
    }

    [Fact]
    public void A_directory_link_cannot_supply_a_control_root()
    {
        RequireNative(); using var root = new TempDir(); string target = root.Dir("target"), alias = Path.Combine(root.Path, "alias");
        Directory.CreateSymbolicLink(alias, target);
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable, Assert.Throws<ArchiveWriteException>(() => LinuxArchiveNative.OpenDirectory(alias)).Reason);
        Assert.True(Directory.Exists(target));
    }

    private static void RequireNative()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux cgroup-v2 controls.");
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_NATIVE") != "1")
            Assert.Skip("Set FILECAT_ARCHIVE_LINUX_NATIVE=1 only in an owned ordinary-user delegated native qualification lane.");
    }
}
