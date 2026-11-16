using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

public static unsafe partial class FileSystemCapacity
{
    /// <summary>Available bytes on the exact held native directory's volume. Identity comes from that same held directory.</summary>
    internal static FileSystemCapacitySnapshot? QueryHeldDirectory(SafeFileHandle handle, ulong volumeSerial)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) return null;
        bool retained = false;
        try
        {
            handle.DangerousAddRef(ref retained); if (handle.IsInvalid) return null;
            if (OperatingSystem.IsWindows())
            {
                FullSize size = default; CapacityIoStatus io = default;
                int status = QueryVolume(handle, &io, &size, (uint)sizeof(FullSize), 7 /* FileFsFullSizeInformation */);
                if (status != 0 || io.Information != sizeof(FullSize) || size.CallerAvailableUnits < 0 || size.TotalUnits < 0 ||
                    size.CallerAvailableUnits > size.TotalUnits || size.SectorsPerUnit == 0 || size.BytesPerSector == 0) return null;
                return Snapshot("windows-volume:" + volumeSerial.ToString("X16"), (ulong)size.CallerAvailableUnits, (ulong)size.SectorsPerUnit * size.BytesPerSector);
            }
            bool mac = OperatingSystem.IsMacOS(); if (!mac && !OperatingSystem.IsLinux()) return null;
            byte* bytes = stackalloc byte[4096]; new Span<byte>(bytes, 4096).Clear();
            int fd = checked((int)handle.DangerousGetHandle());
            int result = mac && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? MacCapacity64(fd, bytes) : UnixCapacity(fd, bytes);
            if (result != 0) return null;
            // Qualified 64-bit statfs layouts. Darwin uses f_bsize; Linux reports allocation units in f_frsize.
            ulong units = *(ulong*)(bytes + (mac ? 24 : 32));
            ulong unitBytes = mac ? *(uint*)bytes : *(ulong*)(bytes + 72);
            return Snapshot("unix-device:" + volumeSerial.ToString("X16"), units, unitBytes);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or EntryPointNotFoundException or DllNotFoundException) { return null; }
        finally { if (retained) handle.DangerousRelease(); }
    }

    private static FileSystemCapacitySnapshot? Snapshot(string identity, ulong units, ulong unitBytes)
    {
        // Refuse implausible/negative native counters. Saturate only a valid nonnegative byte multiplication.
        if (units > long.MaxValue || unitBytes is 0 or > (1UL << 30)) return null;
        UInt128 bytes = (UInt128)units * unitBytes;
        return new(identity, bytes > long.MaxValue ? long.MaxValue : (long)bytes);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FullSize { internal long TotalUnits, CallerAvailableUnits, ActualAvailableUnits; internal uint SectorsPerUnit, BytesPerSector; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CapacityIoStatus { internal nint Status, Information; }
    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryVolumeInformationFile")]
    private static partial int QueryVolume(SafeFileHandle handle, CapacityIoStatus* io, FullSize* size, uint bytes, int kind);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int UnixCapacity(int fd, byte* bytes);
    [LibraryImport("libc", EntryPoint = "fstatfs$INODE64", SetLastError = true)]
    private static partial int MacCapacity64(int fd, byte* bytes);
}
