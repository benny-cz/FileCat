using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>Reads the per-directory case-sensitive flag without changing the directory.</summary>
public static partial class WindowsCaseSensitivity
{
    private const int FileCaseSensitiveInfo = 23;
    private const uint CaseSensitiveDirectory = 1;
    private const uint OpenExisting = 3, BackupSemantics = 0x02000000;

    /// <summary>
    /// Use exact names when the flag cannot be read: an uncertain filesystem must not merge distinct files.
    /// </summary>
    public static StringComparer NamesIn(string folder)
    {
        if (!OperatingSystem.IsWindows()) return StringComparer.Ordinal;
        using var handle = CreateFile(folder, 0, FileShare.ReadWrite | FileShare.Delete, 0, OpenExisting, BackupSemantics, 0);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, out uint flags, sizeof(uint)))
            return StringComparer.Ordinal;
        return (flags & CaseSensitiveDirectory) != 0 ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, FileShare share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out uint info, uint size);
}
