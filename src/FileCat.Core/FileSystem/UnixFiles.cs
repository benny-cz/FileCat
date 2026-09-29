using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>What <c>stat</c> says about a file on Linux or macOS, links not followed.</summary>
public readonly record struct UnixStat(ulong Device, ulong Inode, bool IsLink, bool IsDirectory, long Size)
{
    /// <summary>The identity as the hex editor and its journal record it (the inode in the first 8 of 16 bytes).</summary>
    public FileIdentity Identity => new(Device, Inode.ToString("X16") + new string('0', 16));
}

/// <summary>
/// File facts Linux and macOS give only through their C libraries (P9): identity (device and inode), whether a path is a
/// link, a full flush to the medium, and which mount holds a path. The structure layouts are fixed per platform:
/// statx on Linux (one layout for every architecture), and the 64-bit-inode struct stat on macOS.
/// </summary>
public static unsafe partial class UnixFiles
{
    private const int AtFdCwd = -100, AtSymlinkNoFollow = 0x100, AtEmptyPath = 0x1000;
    private const uint StatxType = 0x1, StatxIno = 0x100, StatxSize = 0x200;
    private const int TypeMask = 0xF000, TypeLink = 0xA000, TypeDirectory = 0x4000;

    /// <summary>
    /// The path's own entry (a link is reported as a link), or with <paramref name="followLinks"/> what a link at its end
    /// leads to; null when it does not exist or cannot be read. Links on the way to the last part are always followed.
    /// </summary>
    public static UnixStat? Stat(string path, bool followLinks = false)
    {
        if (OperatingSystem.IsWindows()) return null;
        byte* buffer = stackalloc byte[256];
        try
        {
            if (OperatingSystem.IsLinux())
                return Statx(AtFdCwd, path, followLinks ? 0 : AtSymlinkNoFollow, StatxType | StatxIno | StatxSize, buffer) == 0 ? FromStatx(buffer) : null;
            if (OperatingSystem.IsMacOS())
            {
                bool arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
                int result = followLinks
                    ? arm ? MacStat(path, buffer) : MacStatInode64(path, buffer)
                    : arm ? MacLstat(path, buffer) : MacLstatInode64(path, buffer);
                return result == 0 ? FromMacStat(buffer) : null;
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        return null;
    }

    /// <summary>The full path with every link resolved (realpath), or null when it does not exist or cannot be read.</summary>
    public static string? RealPath(string path)
    {
        if (OperatingSystem.IsWindows()) return null;
        // PATH_MAX is 4096 on Linux and 1024 on macOS, the terminating zero included.
        byte* buffer = stackalloc byte[4096 + 1];
        try
        {
            return RealPathNative(path, buffer) is null ? null : Marshal.PtrToStringUTF8((nint)buffer);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The file an open handle refers to, or null when the system cannot say.</summary>
    public static UnixStat? Stat(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows()) return null;
        byte* buffer = stackalloc byte[256];
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            int fd = (int)handle.DangerousGetHandle();
            if (OperatingSystem.IsLinux())
                return Statx(fd, string.Empty, AtEmptyPath | AtSymlinkNoFollow, StatxType | StatxIno | StatxSize, buffer) == 0 ? FromStatx(buffer) : null;
            if (OperatingSystem.IsMacOS())
                return (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacFstat(fd, buffer) : MacFstatInode64(fd, buffer)) == 0
                    ? FromMacStat(buffer) : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
        return null;
    }

    // struct statx: u16 mode at 28, u64 ino at 32, u64 size at 40, u32 dev_major at 136, u32 dev_minor at 140.
    private static UnixStat FromStatx(byte* b)
    {
        int type = *(ushort*)(b + 28) & TypeMask;
        ulong device = ((ulong)*(uint*)(b + 136) << 32) | *(uint*)(b + 140);
        return new UnixStat(device, *(ulong*)(b + 32), type == TypeLink, type == TypeDirectory, (long)*(ulong*)(b + 40));
    }

    // struct stat (64-bit inodes): i32 dev at 0, u16 mode at 4, u64 ino at 8, i64 size at 96.
    private static UnixStat FromMacStat(byte* b)
    {
        int type = *(ushort*)(b + 4) & TypeMask;
        return new UnixStat((uint)*(int*)b, *(ulong*)(b + 8), type == TypeLink, type == TypeDirectory, *(long*)(b + 96));
    }

    /// <summary>
    /// Writes a file's data through to the medium: fsync, and on macOS F_FULLFSYNC, since there fsync leaves the data
    /// in the drive's cache.
    /// </summary>
    public static void FullSync(SafeFileHandle handle)
    {
        RandomAccess.FlushToDisk(handle);
        if (!OperatingSystem.IsMacOS()) return;
        const int FullFsync = 51;
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            // A file system without F_FULLFSYNC has had its fsync above.
            _ = Fcntl((int)handle.DangerousGetHandle(), FullFsync);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    /// <summary>The mounted file system that holds <paramref name="fullPath"/> (the longest mount point above it).</summary>
    public static DriveInfo? MountOf(string fullPath)
    {
        DriveInfo? best = null;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string root = drive.Name.EndsWith('/') ? drive.Name : drive.Name + "/";
                bool holds = drive.Name == "/" || fullPath == drive.Name || fullPath.StartsWith(root, StringComparison.Ordinal);
                if (holds && (best is null || drive.Name.Length > best.Name.Length)) best = drive;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return best;
    }

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial byte* RealPathNative(string path, byte* resolved);

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int directory, string path, int flags, uint mask, byte* buffer);

    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacLstat(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "stat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacStat(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "stat$INODE64", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacStatInode64(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "lstat$INODE64", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacLstatInode64(string path, byte* buffer);

    [LibraryImport("libc", EntryPoint = "fstat")]
    private static partial int MacFstat(int fd, byte* buffer);

    [LibraryImport("libc", EntryPoint = "fstat$INODE64")]
    private static partial int MacFstatInode64(int fd, byte* buffer);

    // fcntl is variadic; F_FULLFSYNC passes no argument, so the fixed two-argument form is the same call.
    [LibraryImport("libc", EntryPoint = "fcntl")]
    private static partial int Fcntl(int fd, int command);
}
