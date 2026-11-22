using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows.Permissions;

/// <summary>
/// The Windows processes whose permission readers and writers have native evidence (G-WIN): native x64, and native Arm64
/// since its smoke passed on Windows 11 on Arm. An x64 copy running emulated on Windows on Arm is neither.
/// </summary>
internal static class WindowsPermissionHost
{
    /// <summary>Why this process cannot use the named Windows permission route, or null when it can.</summary>
    internal static string? Refusal(string route) => Refusal(route, OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture,
        RuntimeInformation.OSArchitecture);

    internal static string? Refusal(string route, bool windows, Architecture process, Architecture os) =>
        !windows ? $"This {route} requires Windows." :
        process == os && process is Architecture.X64 or Architecture.Arm64 ? null :
        os == Architecture.Arm64 ? $"On Windows on Arm, this {route} needs the Arm64 build of FileCat; this copy runs emulated." :
        $"This {route} requires Windows x64 or Arm64.";
}
