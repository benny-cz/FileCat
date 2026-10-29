using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>
/// Which reparse points are links to another item: a symbolic link, a junction or mount point, a WSL symbolic link, an
/// app execution alias. Every other reparse point belongs to a component that keeps the item under its own name (a Cloud
/// Files placeholder such as OneDrive's, a deduplicated or compressed file): it is a file or folder like any other, read,
/// written and deleted as one (review finding F-H3 of f134ba37).
/// </summary>
public static partial class ReparseLinks
{
    public const uint SymbolicLinkTag = 0xA000000C, MountPointTag = 0xA0000003, WslSymbolicLinkTag = 0xA000001D,
        AppExecutionAliasTag = 0x8000001B;

    private const uint OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000, ReadAttributes = 0x80;
    private const int AttributeTagInfo = 9;

    public static bool IsLinkTag(uint tag) => tag is SymbolicLinkTag or MountPointTag or WslSymbolicLinkTag or AppExecutionAliasTag;

    /// <summary>
    /// Whether an existing item is a link. On Linux and macOS every reparse point is a symbolic link. On Windows an item
    /// whose reparse tag cannot be read counts as a link, so that it is never followed.
    /// </summary>
    public static bool IsLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
        if (!OperatingSystem.IsWindows()) return true;
        string full = Path.GetFullPath(path);
        string native = full.Length >= 248 && !full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\" + full : full;
        using var handle = CreateFile(native, ReadAttributes, FileShare.ReadWrite | FileShare.Delete, 0, OpenExisting,
            BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, AttributeTagInfo, out AttributeTag info, 8)) return true;
        return (info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0 && IsLinkTag(info.ReparseTag);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag
    {
        public uint FileAttributes, ReparseTag;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, FileShare share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTag info, uint size);
}
