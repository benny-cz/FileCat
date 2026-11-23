using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

// G-WIN: native x64 and native Arm64 are the qualified Windows permission hosts; an x64 copy running emulated on Windows on
// Arm is refused with a reason that names the Arm64 build.
public sealed class WindowsPermissionHostTests
{
    [Theory]
    [InlineData(Architecture.X64, Architecture.X64, null)]
    [InlineData(Architecture.Arm64, Architecture.Arm64, null)]
    [InlineData(Architecture.X64, Architecture.Arm64, "On Windows on Arm, this reader needs the Arm64 build of FileCat; this copy runs emulated.")]
    [InlineData(Architecture.X86, Architecture.X64, "This reader requires Windows x64 or Arm64.")]
    [InlineData(Architecture.X86, Architecture.X86, "This reader requires Windows x64 or Arm64.")]
    public void Only_native_x64_and_Arm64_processes_read_or_change_permissions(Architecture process, Architecture os, string? refusal) =>
        Assert.Equal(refusal, WindowsPermissionHost.Refusal("reader", windows: true, process, os));

    [Fact]
    public void Other_systems_are_refused() =>
        Assert.Equal("This permission writer requires Windows.",
            WindowsPermissionHost.Refusal("permission writer", windows: false, Architecture.X64, Architecture.X64));

    [Fact]
    public void This_native_process_is_admitted()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != RuntimeInformation.ProcessArchitecture)
            Assert.Skip("Requires a native Windows process.");
        Assert.Null(WindowsPermissionHost.Refusal("reader"));
    }
}
