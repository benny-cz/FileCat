using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>Native Linux flags for the supported process ABIs; asm-generic values are not universal.</summary>
internal static class LinuxOpenFlags
{
    internal const int CloseOnExec = 0x80000;
    internal static (int Directory, int NoFollow) Current => ForArchitecture(RuntimeInformation.ProcessArchitecture);
    internal static (int Directory, int NoFollow) ForArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => (0x10000, 0x20000),
        // ARM64 retains the AArch32-compatible directory/no-follow values.
        Architecture.Arm64 => (0x4000, 0x8000),
        _ => throw new PlatformNotSupportedException("Linux file-open flags require x64 or ARM64."),
    };
}
