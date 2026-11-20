using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

public static unsafe partial class UnixFiles
{
    /// <summary>One fixed leaf relative to a held directory, without following the leaf's link.</summary>
    internal static UnixStat? StatChild(SafeFileHandle parent, string leaf)
    {
        if (string.IsNullOrEmpty(leaf) || leaf is "." or ".." || leaf.Contains('/') || leaf.Contains('\0')) throw new ArgumentException("A single Unix leaf is required.");
        byte* buffer = stackalloc byte[256]; bool added = false;
        try
        {
            parent.DangerousAddRef(ref added); int fd = checked((int)parent.DangerousGetHandle());
            if (OperatingSystem.IsLinux()) return Statx(fd, leaf, AtSymlinkNoFollow, StatxWanted, buffer) == 0 ? FromStatx(buffer) : null;
            if (OperatingSystem.IsMacOS())
                return (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? MacFstatAt(fd, leaf, buffer, 0x20) : MacFstatAtInode64(fd, leaf, buffer, 0x20)) == 0 ? FromMacStat(buffer) : null;
            return null;
        }
        finally { if (added) parent.DangerousRelease(); }
    }
    [LibraryImport("libc", EntryPoint = "fstatat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacFstatAt(int parent, string leaf, byte* buffer, int flags);
    [LibraryImport("libc", EntryPoint = "fstatat$INODE64", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacFstatAtInode64(int parent, string leaf, byte* buffer, int flags);
}
