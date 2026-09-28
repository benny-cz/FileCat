using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// Handle-relative NT file primitives. Every open names one child of an already verified directory handle and never
/// follows a reparse point (FILE_OPEN_REPARSE_POINT), so a folder swapped for a link cannot redirect the operation.
/// </summary>
internal static unsafe partial class NtFile
{
    public const uint ReadData = 0x1, ListDirectory = 0x1, WriteData = 0x2, AddSubdirectory = 0x4, Traverse = 0x20,
        ReadAttributes = 0x80, WriteAttributes = 0x100, Delete = 0x10000, Synchronize = 0x100000,
        GenericRead = 0x80000000, GenericWrite = 0x40000000;
    public const uint ShareRead = 1, ShareWrite = 2, ShareDelete = 4, ShareAll = 7;
    public const uint Open = 1, Create = 2;
    public const uint DirectoryFile = 0x1, NonDirectoryFile = 0x40, OpenReparsePoint = 0x200000;
    private const uint SynchronousIo = 0x20, CaseInsensitive = 0x40;

    public const int NameNotFound = unchecked((int)0xC0000034), NameCollision = unchecked((int)0xC0000035),
        PathNotFound = unchecked((int)0xC000003A), NotADirectory = unchecked((int)0xC0000103);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length, MaximumLength;
        public char* Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length;
        public nint RootDirectory;
        public UnicodeString* ObjectName;
        public uint Attributes;
        public nint SecurityDescriptor, SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public nint Status, Information;
    }

    /// <summary>FILE_BASIC_INFO: zero times mean "unchanged" when set.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BasicInfo
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }

    /// <summary>
    /// Opens <paramref name="name"/> below <paramref name="root"/> (or an absolute NT path when root is null) and returns
    /// the NTSTATUS; <paramref name="handle"/> is invalid unless the status is a success.
    /// </summary>
    public static int TryOpen(SafeFileHandle? root, string name, uint access, uint share, uint disposition, uint options,
        out SafeFileHandle handle, uint fileAttributes = 0)
    {
        if (name.Length == 0 || name.Length > 32766) throw new ArgumentException("Invalid name.", nameof(name));
        bool added = false;
        try
        {
            root?.DangerousAddRef(ref added);
            fixed (char* chars = name)
            {
                var text = new UnicodeString { Length = (ushort)(name.Length * 2), MaximumLength = (ushort)(name.Length * 2), Buffer = chars };
                var attributes = new ObjectAttributes
                {
                    Length = sizeof(ObjectAttributes),
                    RootDirectory = root?.DangerousGetHandle() ?? 0,
                    ObjectName = &text,
                    Attributes = CaseInsensitive,
                };
                IoStatusBlock io;
                int status = NtCreateFile(out nint raw, access, &attributes, &io, null, fileAttributes, share, disposition,
                    options | SynchronousIo, null, 0);
                handle = new SafeFileHandle(status >= 0 ? raw : 0, ownsHandle: true);
                return status;
            }
        }
        finally
        {
            if (added) root!.DangerousRelease();
        }
    }

    public static SafeFileHandle OpenOrThrow(SafeFileHandle? root, string name, uint access, uint share, uint disposition, uint options,
        string what, uint fileAttributes = 0)
    {
        int status = TryOpen(root, name, access, share, disposition, options, out var handle, fileAttributes);
        if (status < 0) throw Error(status, what);
        return handle;
    }

    public static Exception Error(int status, string what)
    {
        int code = RtlNtStatusToDosError(status);
        var inner = new Win32Exception(code);
        return code switch
        {
            5 => new UnauthorizedAccessException($"{what}: {inner.Message}", inner),
            2 or 3 => new FileNotFoundException($"{what}: {inner.Message}", inner),
            _ => new IOException($"{what}: {inner.Message}", inner) { HResult = unchecked((int)0x80070000) | code },
        };
    }

    public static BasicInfo GetBasic(SafeFileHandle handle)
    {
        BasicInfo info;
        if (!GetFileInformationByHandleEx(handle, 0, &info, (uint)sizeof(BasicInfo))) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return info;
    }

    public static void SetBasic(SafeFileHandle handle, BasicInfo info)
    {
        if (!SetFileInformationByHandle(handle, 0, &info, (uint)sizeof(BasicInfo))) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public static bool IsReparsePoint(uint attributes) => (attributes & (uint)FileAttributes.ReparsePoint) != 0;
    public static bool IsDirectory(uint attributes) => (attributes & (uint)FileAttributes.Directory) != 0;

    /// <summary>Names in a directory (without "." and ".."), read through the handle, never by path.</summary>
    public static List<(string Name, uint Attributes)> List(SafeFileHandle directory)
    {
        const int FullDirectoryInfo = 14, FullDirectoryRestartInfo = 15, NoMoreFiles = 18;
        var result = new List<(string, uint)>();
        var buffer = new byte[64 * 1024];
        bool first = true;
        fixed (byte* start = buffer)
        {
            while (true)
            {
                if (!GetFileInformationByHandleEx(directory, first ? FullDirectoryRestartInfo : FullDirectoryInfo, start, (uint)buffer.Length))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == NoMoreFiles) return result;
                    throw new Win32Exception(error);
                }
                first = false;
                for (int offset = 0; ;)
                {
                    // FILE_FULL_DIR_INFO: attributes at 56, name length (bytes) at 60, name at 68.
                    byte* entry = start + offset;
                    uint attributes = *(uint*)(entry + 56);
                    int length = (int)*(uint*)(entry + 60);
                    var name = new string((char*)(entry + 68), 0, length / 2);
                    if (name is not ("." or "..")) result.Add((name, attributes));
                    int next = (int)*(uint*)entry;
                    if (next == 0) break;
                    offset += next;
                }
            }
        }
    }

    /// <summary>Alternate data stream names other than the default data stream (FileStreamInfo).</summary>
    public static List<string> Streams(SafeFileHandle file)
    {
        const int FileStreamInfo = 7, HandleEof = 38;
        var result = new List<string>();
        var buffer = new byte[16 * 1024];
        fixed (byte* start = buffer)
        {
            if (!GetFileInformationByHandleEx(file, FileStreamInfo, start, (uint)buffer.Length))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == HandleEof) return result; // no streams at all (for example a directory)
                return result; // FAT volumes and full buffers: no reliable list; nothing is claimed
            }
            for (int offset = 0; ;)
            {
                // FILE_STREAM_INFO: next (0), name length (4), size (8), allocation (16), name (24).
                byte* entry = start + offset;
                int length = (int)*(uint*)(entry + 4);
                var name = new string((char*)(entry + 24), 0, length / 2);
                if (!name.Equals("::$DATA", StringComparison.OrdinalIgnoreCase)) result.Add(name);
                int next = (int)*(uint*)entry;
                if (next == 0) break;
                offset += next;
            }
        }
        return result;
    }

    /// <summary>
    /// Deletes the object behind the handle (opened with DELETE): the link itself for a link. POSIX semantics remove the
    /// name at once; volumes without them fall back to classic delete-on-close.
    /// </summary>
    public static void DeleteOpen(SafeFileHandle handle)
    {
        const int DispositionInfoEx = 21, DispositionInfo = 4;
        const uint Delete = 0x1, Posix = 0x2, IgnoreReadOnly = 0x10;
        uint flags = Delete | Posix | IgnoreReadOnly;
        if (SetFileInformationByHandle(handle, DispositionInfoEx, &flags, 4)) return;
        int error = Marshal.GetLastPInvokeError();
        if (error is not (1 or 50 or 87)) throw new Win32Exception(error); // not supported by this file system: fall back
        var basic = GetBasic(handle);
        if ((basic.FileAttributes & (uint)FileAttributes.ReadOnly) != 0)
        {
            basic.FileAttributes &= ~(uint)FileAttributes.ReadOnly;
            if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
            basic.CreationTime = basic.LastAccessTime = basic.LastWriteTime = basic.ChangeTime = 0;
            SetBasic(handle, basic);
        }
        byte deleteFile = 1;
        if (!SetFileInformationByHandle(handle, DispositionInfo, &deleteFile, 1)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Renames or moves the object behind the handle to <paramref name="name"/> inside <paramref name="directory"/>.
    /// NtSetInformationFile takes the target folder as a handle (FILE_RENAME_INFORMATION.RootDirectory), so the
    /// destination is the folder already verified, not a path that could be swapped.
    /// </summary>
    public static void Rename(SafeFileHandle handle, SafeFileHandle directory, string name, bool replace)
    {
        const int RenameInformation = 10, RenameInformationEx = 65;
        const uint ReplaceIfExists = 0x1, Posix = 0x2, IgnoreReadOnly = 0x40;
        const int InvalidParameter = unchecked((int)0xC000000D), InvalidInfoClass = unchecked((int)0xC0000003), NotSupported = unchecked((int)0xC00000BB);
        // FILE_RENAME_INFORMATION: flags or ReplaceIfExists (0), root directory handle (8), name length in bytes (16), name (20).
        int size = 24 + name.Length * 2;
        var buffer = new byte[size];
        bool added = false;
        try
        {
            directory.DangerousAddRef(ref added);
            fixed (byte* start = buffer)
            {
                *(nint*)(start + 8) = directory.DangerousGetHandle();
                *(uint*)(start + 16) = (uint)(name.Length * 2);
                fixed (char* chars = name) Buffer.MemoryCopy(chars, start + 20, size - 20, name.Length * 2);
                IoStatusBlock io;
                *(uint*)start = replace ? ReplaceIfExists | Posix | IgnoreReadOnly : 0;
                int status = NtSetInformationFile(handle, &io, start, (uint)size, RenameInformationEx);
                if (status is InvalidParameter or InvalidInfoClass or NotSupported)
                {
                    // File systems without the extended form (FAT, exFAT): classic rename semantics.
                    *(uint*)start = replace ? 1u : 0u;
                    status = NtSetInformationFile(handle, &io, start, (uint)size, RenameInformation);
                }
                if (status >= 0) return;
                int error = RtlNtStatusToDosError(status);
                throw error switch
                {
                    80 or 183 => new IOException("An item with this name already exists at the destination.") { HResult = unchecked((int)0x80070000) | error },
                    17 => new IOException("The destination is on another drive; moving between drives is not supported here.") { HResult = unchecked((int)0x80070000) | error },
                    5 => new UnauthorizedAccessException(new Win32Exception(error).Message),
                    _ => new Win32Exception(error),
                };
            }
        }
        finally
        {
            if (added) directory.DangerousRelease();
        }
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetInformationFile(SafeFileHandle handle, IoStatusBlock* io, void* information, uint length, int informationClass);

    [LibraryImport("ntdll.dll")]
    private static partial int NtCreateFile(out nint handle, uint access, ObjectAttributes* attributes, IoStatusBlock* io,
        long* allocationSize, uint fileAttributes, uint share, uint disposition, uint options, void* ea, uint eaLength);

    [LibraryImport("ntdll.dll")]
    private static partial int RtlNtStatusToDosError(int status);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, void* buffer, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* buffer, uint size);
}
