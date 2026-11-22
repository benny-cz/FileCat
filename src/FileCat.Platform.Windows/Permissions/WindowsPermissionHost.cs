using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows.Permissions;

/// <summary>
/// The Windows processes whose permission readers and writers have native evidence (G-WIN): native x64, and native Arm64 only
/// while its smoke runs with <see cref="Arm64Switch"/> set to 1. An x64 copy running emulated on Windows on Arm is neither.
/// </summary>
internal static class WindowsPermissionHost
{
    internal const string Arm64Switch = "FILECAT_QUALIFY_WINDOWS_ARM64_PERMISSIONS";

    /// <summary>Why this process cannot use the named Windows permission route, or null when it can.</summary>
    internal static string? Refusal(string route) => Refusal(route, OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture,
        RuntimeInformation.OSArchitecture, Environment.GetEnvironmentVariable(Arm64Switch) == "1");

    internal static string? Refusal(string route, bool windows, Architecture process, Architecture os, bool qualifyArm64) =>
        !windows ? $"This {route} requires Windows." :
        process == Architecture.X64 && os == Architecture.X64 ? null :
        process == Architecture.Arm64 && os == Architecture.Arm64 ? qualifyArm64 ? null : $"This {route} is not yet qualified on Windows on Arm." :
        os == Architecture.Arm64 ? $"On Windows on Arm, this {route} needs the Arm64 build of FileCat; this copy runs emulated." :
        $"This {route} requires Windows x64.";
}
