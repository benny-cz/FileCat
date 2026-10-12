using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>
/// A source's native metadata-change clock. It supplements identity, length and mtime when deciding whether a
/// read-only image scan can be reused: a writer can restore mtime after changing bytes in the same file.
/// </summary>
public static partial class SourceChangeToken
{
    public static string? FromPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return UnixFiles.Stat(path, followLinks: true) is { } info ? UnixToken(info) : null;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FromHandle(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string? FromHandle(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return UnixFiles.Stat(handle) is { } info ? UnixToken(info) : null;
        return GetFileInformationByHandleEx(handle, 0 /* FileBasicInfo */, out FileBasicInfo basic, (uint)Marshal.SizeOf<FileBasicInfo>())
            && basic.ChangeTime != 0
            ? basic.ChangeTime.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static string? UnixToken(UnixStat info) => info.HasChangeTime
        ? info.ChangeSeconds.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) + ":" +
          info.ChangeNanoseconds.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)
        : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileBasicInfo information, uint size);
}
