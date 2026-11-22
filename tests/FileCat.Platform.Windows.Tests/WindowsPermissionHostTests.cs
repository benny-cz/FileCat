using System.Runtime.InteropServices;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

// G-WIN: native x64 is the qualified Windows permission host; native Arm64 only under its smoke switch, and an x64 copy
// running emulated on Windows on Arm never.
public sealed class WindowsPermissionHostTests
{
    [Theory]
    [InlineData(Architecture.X64, Architecture.X64, false, null)]
    [InlineData(Architecture.X64, Architecture.X64, true, null)]
    [InlineData(Architecture.Arm64, Architecture.Arm64, true, null)]
    [InlineData(Architecture.Arm64, Architecture.Arm64, false, "This reader is not yet qualified on Windows on Arm.")]
    [InlineData(Architecture.X64, Architecture.Arm64, true, "On Windows on Arm, this reader needs the Arm64 build of FileCat; this copy runs emulated.")]
    [InlineData(Architecture.X86, Architecture.X64, true, "This reader requires Windows x64.")]
    public void Only_native_hosts_with_evidence_read_or_change_permissions(Architecture process, Architecture os, bool qualifyArm64, string? refusal) =>
        Assert.Equal(refusal, WindowsPermissionHost.Refusal("reader", windows: true, process, os, qualifyArm64));

    [Fact]
    public void Other_systems_are_refused() =>
        Assert.Equal("This permission writer requires Windows.",
            WindowsPermissionHost.Refusal("permission writer", windows: false, Architecture.X64, Architecture.X64, qualifyArm64: true));

    [Fact]
    public void This_process_is_admitted_on_a_native_x64_host()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Assert.Skip("Requires a native Windows x64 process.");
        Assert.Null(WindowsPermissionHost.Refusal("reader"));
    }

    [Fact]
    public void Native_Arm64_reads_and_writes_nothing_until_its_smoke_switch_is_set()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.Arm64 ||
            RuntimeInformation.ProcessArchitecture != Architecture.Arm64 || Environment.GetEnvironmentVariable(WindowsPermissionHost.Arm64Switch) == "1")
            Assert.Skip("Requires a native Windows on Arm process without the Arm64 permissions switch.");
        var directory = Directory.CreateTempSubdirectory("filecat-acl-arm64-");
        try
        {
            string path = Path.Combine(directory.FullName, "witness.txt");
            File.WriteAllText(path, "witness");
            var subject = new PermissionSubject(Location.FileSystem(directory.FullName), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow, ListedSize: new FileInfo(path).Length);
            using var inspection = new WindowsPermissionService().Inspect(subject, PermissionSections.Ordinary, TestContext.Current.CancellationToken);
            Assert.Equal(PermissionReadStatus.Unsupported, inspection.Snapshot.Access.Status);
            Assert.Equal("This reader is not yet qualified on Windows on Arm.", inspection.Snapshot.Access.Reason);
            var refusal = Assert.Throws<PlatformNotSupportedException>(() => WindowsPermissionToken.OrdinaryIdentity());
            Assert.Equal("This permission writer is not yet qualified on Windows on Arm.", refusal.Message);
        }
        finally { directory.Delete(recursive: true); }
    }
}
