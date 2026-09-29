using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// The file the hex editor changes in place (ADR-05), held open for the whole editing session. On Windows the handle
/// denies other programs write and delete access until the editor closes. Linux and macOS have no such exclusion (their
/// locks are advisory, and an exclusive one would also keep readers such as FileCat's own viewer out), so other programs
/// may read and write the file, and every save first checks that the path still names this file and that each byte being
/// replaced still holds the value the editor showed.
/// </summary>
public sealed partial class ProtectedHexFile : IContentSource
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, OpenExisting = 3;
    private const uint FlagOpenReparsePoint = 0x00200000, FlagRandomAccess = 0x10000000;
    private const uint AttributeReadOnly = 0x1, AttributeDirectory = 0x10, AttributeSparse = 0x200, AttributeReparsePoint = 0x400;
    private const string LinkRefused = "This path is a link. Editing through it could change an unexpected file; open the file it points to instead.";
    private readonly SafeFileHandle _handle;
    private readonly string _path;
    private readonly long _length;
    private readonly FileIdentity _identity;

    public ProtectedHexFile(string path)
    {
        _path = LocalFullPath(path);
        if (!OperatingSystem.IsWindows())
        {
            (_handle, _identity) = OpenPosix(_path);
            try
            {
                _length = RandomAccess.GetLength(_handle);
                if (_length == 0) throw new NotSupportedException("The file is empty. The hex editor changes existing bytes; it never adds bytes.");
            }
            catch { _handle.Dispose(); throw; }
            return;
        }
        // Opening the link object itself (not its target) makes the link refusal race-free: a path swapped for a
        // link after any earlier check is still refused here.
        _handle = CreateFile(ExtendedPath(_path), GenericRead | GenericWrite, FileShareRead, IntPtr.Zero, OpenExisting,
            FlagOpenReparsePoint | FlagRandomAccess, IntPtr.Zero);
        if (_handle.IsInvalid) throw OpenError(Marshal.GetLastPInvokeError(), _path);
        try
        {
            uint attributes = Attributes(_handle);
            if ((attributes & AttributeReparsePoint) != 0) throw new NotSupportedException(LinkRefused);
            if ((attributes & AttributeDirectory) != 0) throw new NotSupportedException("Folders have no bytes to edit.");
            IsSparse = (attributes & AttributeSparse) != 0;
            _length = RandomAccess.GetLength(_handle);
            if (_length == 0) throw new NotSupportedException("The file is empty. The hex editor changes existing bytes; it never adds bytes.");
            _identity = Identity(_handle);
        }
        catch { _handle.Dispose(); throw; }
    }

    /// <summary>
    /// Linux and macOS: the path's own entry is checked before opening and again after, and must be the file the handle
    /// has, so a link (even one swapped in meanwhile) is refused.
    /// </summary>
    private static (SafeFileHandle Handle, FileIdentity Identity) OpenPosix(string path)
    {
        var before = UnixFiles.Stat(path) ?? throw new FileNotFoundException("The file no longer exists.", path);
        if (before.IsLink) throw new NotSupportedException(LinkRefused);
        if (before.IsDirectory) throw new NotSupportedException("Folders have no bytes to edit.");
        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, FileOptions.RandomAccess);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException("You do not have permission to change this file. Edit a copy, or change its permissions first (File → Change attributes and times…).");
        }
        catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new IOException("Another program holds a lock on this file. Close that program, then open the hex editor again.", ex);
        }
        try
        {
            var opened = UnixFiles.Stat(handle) ?? throw new IOException("The system did not say which file was opened, so the hex editor cannot protect it.");
            var after = UnixFiles.Stat(path);
            if (after is not { } now || now.IsLink || now.Identity != opened.Identity || opened.Identity != before.Identity)
                throw new NotSupportedException("The path changed while it was being opened (it may now be a link). Open it again.");
            return (handle, opened.Identity);
        }
        catch { handle.Dispose(); throw; }
    }

    public string DisplayName => _path;
    public string? LocalPath => _path;
    public long Length => _length;
    public bool CanSeek => true;
    public bool IsSparse { get; }
    public FileIdentity FileIdentity => _identity;
    internal SafeFileHandle Handle => _handle;

    /// <summary>
    /// Whether other programs cannot write the file while it is open (Windows). Elsewhere they can, and saving checks
    /// the bytes being replaced first.
    /// </summary>
    public static bool ExcludesWriters => OperatingSystem.IsWindows();

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
        if (PathIdentity(_path) != _identity) throw new IOException("The path now names a different file; save is blocked.");
        foreach (var range in ranges)
        {
            var actual = new byte[range.Original.Length];
            if (RandomAccess.Read(_handle, actual, range.Offset) != actual.Length ||
                !actual.AsSpan().SequenceEqual(range.Original))
                throw new IOException($"Original bytes at 0x{range.Offset:X} changed; save is blocked." +
                    (ExcludesWriters ? string.Empty : " Another program wrote the file while it was open; reopen it to see its bytes now."));
        }
    }

    /// <summary>
    /// What the path names now. Windows opens it (sharing everything, as the protected handle allows); Linux and macOS
    /// read the entry without opening it (a link there reads as no file).
    /// </summary>
    public static FileIdentity? PathIdentity(string path)
    {
        if (!OperatingSystem.IsWindows()) return UnixFiles.Stat(path) is { IsLink: false } entry ? entry.Identity : null;
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        return Identity(handle);
    }

    internal void Write(long offset, ReadOnlySpan<byte> bytes) => RandomAccess.Write(_handle, bytes, offset);
    internal void Flush() => UnixFiles.FullSync(_handle);
    public void Dispose() => _handle.Dispose();

    /// <summary>A full local path; network locations are refused because other machines can bypass the write exclusion.</summary>
    internal static string LocalFullPath(string path)
    {
        string full = Path.GetFullPath(path);
        const string NotLocal = "Hex editing is available for files on local drives: other computers could still write a network file. Copy it to a local drive first.";
        if (!OperatingSystem.IsWindows())
        {
            if (UnixFiles.MountOf(full) is { DriveType: DriveType.Network }) throw new NotSupportedException(NotLocal);
            return full;
        }
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) && !full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            full = full[4..];
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network)
            throw new NotSupportedException(NotLocal);
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

    /// <summary>The file a handle refers to: the volume and 128-bit file ID on Windows, the device and inode elsewhere.</summary>
    public static unsafe FileIdentity Identity(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows())
            return (UnixFiles.Stat(handle) ?? throw new IOException("The system did not say which file this is.")).Identity;
        const int FileIdInformation = 18;
        if (!GetFileInformationByHandleEx(handle, FileIdInformation, out FileIdInfo info, (uint)sizeof(FileIdInfo)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var bytes = new byte[16];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = info.FileId[i];
        return new FileIdentity(info.VolumeSerial, Convert.ToHexString(bytes));
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
