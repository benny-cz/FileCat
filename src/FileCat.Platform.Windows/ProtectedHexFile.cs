using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public readonly record struct WindowsFileIdentity(ulong VolumeSerial, string FileId)
{
    public override string ToString() => $"{VolumeSerial:X16}:{FileId}";
}

/// <summary>Local-file baseline held with write/delete sharing denied for the entire editing session.</summary>
public sealed partial class ProtectedHexFile : IContentSource
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, OpenExisting = 3;
    private const uint FlagOpenReparsePoint = 0x00200000, FlagRandomAccess = 0x10000000;
    private const uint AttributeReadOnly = 0x1, AttributeDirectory = 0x10, AttributeSparse = 0x200, AttributeReparsePoint = 0x400;
    private readonly SafeFileHandle _handle;
    private readonly string _path;
    private readonly long _length;
    private readonly WindowsFileIdentity _identity;

    public ProtectedHexFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _path = LocalFullPath(path);
        // Opening the link object itself (not its target) makes the link refusal race-free: a path swapped for a
        // link after any earlier check is still refused here.
        _handle = CreateFile(ExtendedPath(_path), GenericRead | GenericWrite, FileShareRead, IntPtr.Zero, OpenExisting,
            FlagOpenReparsePoint | FlagRandomAccess, IntPtr.Zero);
        if (_handle.IsInvalid) throw OpenError(Marshal.GetLastPInvokeError(), _path);
        try
        {
            uint attributes = Attributes(_handle);
            if ((attributes & AttributeReparsePoint) != 0)
                throw new NotSupportedException("This path is a link. Editing through it could change an unexpected file; open the file it points to instead.");
            if ((attributes & AttributeDirectory) != 0) throw new NotSupportedException("Folders have no bytes to edit.");
            IsSparse = (attributes & AttributeSparse) != 0;
            _length = RandomAccess.GetLength(_handle);
            if (_length == 0) throw new NotSupportedException("The file is empty. The hex editor changes existing bytes; it never adds bytes.");
            _identity = Identity(_handle);
        }
        catch { _handle.Dispose(); throw; }
    }

    public string DisplayName => _path;
    public string? LocalPath => _path;
    public long Length => _length;
    public bool CanSeek => true;
    public bool IsSparse { get; }
    public WindowsFileIdentity FileIdentity => _identity;
    internal SafeFileHandle Handle => _handle;

    public int Read(long offset, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return RandomAccess.Read(_handle, buffer, offset);
    }

    public ContentRevision? GetRevision()
    {
        try { return new ContentRevision(RandomAccess.GetLength(_handle), File.GetLastWriteTimeUtc(_path).Ticks, _identity.ToString()); }
        catch (IOException) { return null; }
    }

    public void ValidateForSave(IReadOnlyList<HexPatchRange> ranges)
    {
        if (RandomAccess.GetLength(_handle) != _length) throw new IOException("The protected file length changed; save is blocked.");
        using var pathHandle = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);
        if (Identity(pathHandle) != _identity) throw new IOException("The path now names a different file; save is blocked.");
        foreach (var range in ranges)
        {
            var actual = new byte[range.Original.Length];
            if (RandomAccess.Read(_handle, actual, range.Offset) != actual.Length ||
                !actual.AsSpan().SequenceEqual(range.Original))
                throw new IOException($"Original bytes at 0x{range.Offset:X} changed; save is blocked.");
        }
    }

    internal void Write(long offset, ReadOnlySpan<byte> bytes) => RandomAccess.Write(_handle, bytes, offset);
    internal void Flush() => RandomAccess.FlushToDisk(_handle);
    public void Dispose() => _handle.Dispose();

    /// <summary>A full local path; network locations are refused because other machines can bypass the write exclusion.</summary>
    internal static string LocalFullPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && !full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            full = full[4..];
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network)
            throw new NotSupportedException("Hex editing is available for files on local drives: other computers could still write a network file. Copy it to a local drive first.");
        return full;
    }

    private static string ExtendedPath(string full) => full.Length >= 248 ? @"\\?\" + full : full;

    private static Exception OpenError(int error, string path)
    {
        const int FileNotFound = 2, PathNotFound = 3, AccessDenied = 5, SharingViolation = 32, LockViolation = 33;
        return error switch
        {
            FileNotFound or PathNotFound => new FileNotFoundException("The file no longer exists.", path),
            SharingViolation or LockViolation => new IOException(
                "Another program has this file open for writing, or is deleting it. Close that program, then open the hex editor again."),
            AccessDenied when File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 =>
                new UnauthorizedAccessException("The file is read-only. Clear its read-only attribute first (File → Change attributes and times…), then open the hex editor again."),
            AccessDenied => new UnauthorizedAccessException("You do not have permission to change this file. To patch a protected file, edit a copy, then copy it back; if that copy is denied, the operations pane offers Retry as administrator."),
            _ => new Win32Exception(error),
        };
    }

    private static unsafe uint Attributes(SafeFileHandle handle)
    {
        const int FileAttributeTagInfo = 9;
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out AttributeTagInfo info, (uint)sizeof(AttributeTagInfo)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return info.FileAttributes;
    }

    public static unsafe WindowsFileIdentity Identity(SafeFileHandle handle)
    {
        const int FileIdInformation = 18;
        if (!GetFileInformationByHandleEx(handle, FileIdInformation, out FileIdInfo info, (uint)sizeof(FileIdInfo)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var bytes = new byte[16];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = info.FileId[i];
        return new WindowsFileIdentity(info.VolumeSerial, Convert.ToHexString(bytes));
    }

    // FILE_ID_INFO: 64-bit volume serial number followed by the 128-bit file ID (ReFS needs all 128 bits).
    private unsafe struct FileIdInfo
    {
        public ulong VolumeSerial;
        public fixed byte FileId[16];
    }

    // FILE_ATTRIBUTE_TAG_INFO
    private struct AttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition,
        uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileIdInfo information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out AttributeTagInfo information, uint size);
}
