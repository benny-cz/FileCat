using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

internal readonly record struct FileSystemReportedSpace(UInt128 Total, UInt128 Used, UInt128 Free, UInt128 Available, bool MayShareCapacity);

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
            int fd = checked((int)handle.DangerousGetHandle());
            var space = ReadUnixSpace(fd);
            return space is { } known ? Snapshot("unix-device:" + volumeSerial.ToString("X16"), known.Available, known.UnitBytes) : null;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or EntryPointNotFoundException or DllNotFoundException) { return null; }
        finally { if (retained) handle.DangerousRelease(); }
    }

    internal static FileSystemReportedSpace? QueryHeldUnixVolume(int descriptor, bool mayShareCapacity)
    {
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) return null;
        try
        {
            var raw = ReadUnixSpace(descriptor);
            if (raw is not { } space || space.Free > space.Total) return null;
            return ReportedSpace(space.Total, space.Total - space.Free, space.Free, space.Available, space.UnitBytes, mayShareCapacity);
        }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException) { return null; }
    }

    internal static FileSystemReportedSpace? QueryHeldWindowsVolume(SafeHandle handle)
    {
        if (!OperatingSystem.IsWindows() || handle.IsInvalid || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) return null;
        FullSizeEx size = default; CapacityIoStatus io = default;
        int status = QueryVolume(handle, &io, &size, (uint)sizeof(FullSizeEx), 14 /* FileFsFullSizeInformationEx */);
        if (status != 0 || io.Information != sizeof(FullSizeEx)) return null;
        return ReportedSpace(size.ActualTotal, size.Used, size.ActualAvailable, size.CallerAvailable,
            (ulong)size.SectorsPerUnit * size.BytesPerSector, size.ActualPoolUnavailable != 0 || size.PoolAvailable != 0);
    }

    private static FileSystemReportedSpace? ReportedSpace(ulong total, ulong used, ulong free, ulong available, ulong unitBytes, bool mayShareCapacity)
    {
        if (total is 0 or > long.MaxValue || used > total || free > total || available > free || unitBytes is 0 or > (1UL << 30)) return null;
        UInt128 unit = unitBytes;
        return new(total * unit, used * unit, free * unit, available * unit, mayShareCapacity);
    }

    private static (ulong Total, ulong Free, ulong Available, ulong UnitBytes)? ReadUnixSpace(int descriptor)
    {
        bool mac = OperatingSystem.IsMacOS();
        if (!mac && !OperatingSystem.IsLinux()) return null;
        byte* bytes = stackalloc byte[4096]; new Span<byte>(bytes, 4096).Clear();
        int result = mac && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? MacCapacity64(descriptor, bytes) : UnixCapacity(descriptor, bytes);
        if (result != 0) return null;
        // Qualified 64-bit statfs layouts. Darwin uses f_bsize; Linux reports allocation units in f_frsize.
        return (*(ulong*)(bytes + (mac ? 8 : 16)), *(ulong*)(bytes + (mac ? 16 : 24)),
            *(ulong*)(bytes + (mac ? 24 : 32)), mac ? *(uint*)bytes : *(ulong*)(bytes + 72));
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
    private struct FullSizeEx
    {
        internal ulong ActualTotal, ActualAvailable, ActualPoolUnavailable, CallerTotal, CallerAvailable, CallerPoolUnavailable;
        internal ulong Used, Reserved, VolumeReserve, AvailableCommitted, PoolAvailable;
        internal uint SectorsPerUnit, BytesPerSector;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CapacityIoStatus { internal nint Status, Information; }
    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryVolumeInformationFile")]
    private static partial int QueryVolume(SafeHandle handle, CapacityIoStatus* io, void* size, uint bytes, int kind);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int UnixCapacity(int fd, byte* bytes);
    [LibraryImport("libc", EntryPoint = "fstatfs$INODE64", SetLastError = true)]
    private static partial int MacCapacity64(int fd, byte* bytes);
}
