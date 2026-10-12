using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.FileSystem;

/// <summary>
/// A source's native change token. It supplements identity, length and mtime when deciding whether a read-only
/// image scan can be reused: a writer can restore mtime after changing bytes in the same file.
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
        string? change = GetFileInformationByHandleEx(handle, 0 /* FileBasicInfo */, out FileBasicInfo basic, (uint)Marshal.SizeOf<FileBasicInfo>())
            && basic.ChangeTime != 0
            ? basic.ChangeTime.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) : null;
        // On NTFS/ReFS with an active journal, a USN changes even when a quick writer restores mtime and the
        // reported change clock has not advanced. A zero/unavailable USN is never treated as evidence of sameness.
        string? usn = WindowsUsn(handle);
        return usn is null ? change : "USN:" + usn + ":CT:" + (change ?? "unknown");
    }

    private static unsafe string? WindowsUsn(SafeFileHandle handle)
    {
        const uint readFileUsnData = 0x0009_00EB; // FSCTL_READ_FILE_USN_DATA
        Span<byte> request = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(request, 2); // minimum accepted USN_RECORD version
        BinaryPrimitives.WriteUInt16LittleEndian(request[2..], 3); // ReFS uses version 3
        Span<byte> response = stackalloc byte[1024];
        uint returned = 0;
        bool ok;
        fixed (byte* input = request)
        fixed (byte* output = response)
            ok = DeviceIoControl(handle, readFileUsnData, input, (uint)request.Length,
                output, (uint)response.Length, &returned, 0);
        if (!ok || returned < 32) return null;
        int recordLength = BinaryPrimitives.ReadInt32LittleEndian(response);
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(response[4..]);
        int usnOffset = version switch { 2 => 24, 3 => 40, _ => -1 };
        if (usnOffset < 0 || recordLength < usnOffset + 8 || recordLength > returned) return null;
        long usn = BinaryPrimitives.ReadInt64LittleEndian(response[usnOffset..]);
        return usn > 0 ? usn.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) : null;
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle handle, uint code, void* input, uint inputSize,
        void* output, uint outputSize, uint* returned, nint overlapped);
}
