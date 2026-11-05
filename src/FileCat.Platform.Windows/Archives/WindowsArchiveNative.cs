using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Archives;

// Observations of this held volume, not permission to enable a format or promise directory-entry durability.
internal sealed record WindowsArchiveVolume(string FileSystem, uint Flags, uint DeviceType, uint DeviceCharacteristics);

internal static unsafe partial class WindowsArchiveNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public nint Status, Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public uint Type, Characteristics; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }

    internal static WindowsArchiveVolume Volume(SafeFileHandle directory)
    {
        DeviceInfo device;
        IoStatus io;
        int status = NtQueryVolumeInformationFile(directory, &io, &device, (uint)sizeof(DeviceInfo), 4 /* FileFsDeviceInformation */);
        if (status < 0) throw NtFile.Error(status, "Query archive volume device");
        if (io.Information != sizeof(DeviceInfo) || device.Type != 7 /* FILE_DEVICE_DISK (underlying media, not driver type 8) */ ||
            (device.Characteristics & 0x10 /* FILE_REMOTE_DEVICE */) != 0)
            throw new NotSupportedException($"The held archive directory is not on a supported local disk filesystem (type={device.Type}, flags={device.Characteristics:X8}, bytes={io.Information}).");
        char* name = stackalloc char[261];
        if (!GetVolumeInformationByHandle(directory, null, 0, out _, out _, out uint flags, name, 261))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        string fileSystem = new(name);
        if (fileSystem != "NTFS" || (flags & 0x8 /* PERSISTENT_ACLS */) == 0 || (flags & 0x80000 /* READ_ONLY_VOLUME */) != 0)
            throw new NotSupportedException("Private archive storage is currently implemented only for writable local NTFS.");
        return new(fileSystem, flags, device.Type, device.Characteristics);
    }

    internal static uint Regular(SafeFileHandle handle, long maximumLength, bool artifact, bool mayRename)
    {
        if (GetFileType(handle) != 1 /* FILE_TYPE_DISK */) throw new IOException("Archive input is not a disk file.");
        StandardInfo info;
        if (!GetFileInformationByHandleEx(handle, 1 /* FileStandardInfo */, &info, (uint)sizeof(StandardInfo)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var basic = NtFile.GetBasic(handle);
        // Initial strict storage deliberately refuses every reparse type, including cloud placeholders. Hydration and
        // their provider-specific metadata require qualification before they can be admitted here.
        if (info.Directory != 0 || info.DeletePending != 0 || NtFile.IsDirectory(basic.FileAttributes) || NtFile.IsReparsePoint(basic.FileAttributes))
            throw new IOException("A regular, non-reparse archive file is required.");
        if (info.EndOfFile < 0 || info.EndOfFile > maximumLength) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The archive file exceeds its physical byte limit.");
        if (artifact && info.NumberOfLinks != 1) throw new IOException("A private archive artifact must have exactly one link.");
        if (mayRename && (basic.FileAttributes & (uint)FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException("The archive file is read-only.");
        return info.NumberOfLinks;
    }

    internal static void RequirePrivateSecurity(SafeFileHandle handle, string owner)
    {
        int error = GetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, 1 | 4 /* OWNER | DACL */, out _, out _, out _, out _, out nint pointer);
        try
        {
            if (error != 0) throw new Win32Exception(error);
            if (pointer == 0) throw new IOException("Archive artifact permissions are unavailable.");
            int length = GetSecurityDescriptorLength(pointer);
            if (length <= 0 || length > 64 * 1024) throw new IOException("Archive artifact permissions are unsupported.");
            byte[] bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length);
            var security = new RawSecurityDescriptor(bytes, 0);
            if (security.Owner?.Value != owner || (security.ControlFlags & ControlFlags.DiscretionaryAclProtected) == 0 ||
                security.DiscretionaryAcl is not { Count: 2 } acl)
                throw new UnauthorizedAccessException("Archive artifact permissions are no longer private.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in acl)
                if (entry is not CommonAce { AceQualifier: AceQualifier.AccessAllowed, IsCallback: false,
                        AceFlags: AceFlags.None, AccessMask: 0x1f01ff } ace ||
                    ace.SecurityIdentifier.Value != owner && ace.SecurityIdentifier.Value != "S-1-5-18" || !seen.Add(ace.SecurityIdentifier.Value))
                    throw new UnauthorizedAccessException("Archive artifact permissions are no longer private.");
        }
        finally { if (pointer != 0) LocalFree(pointer); }
    }

    internal static byte[] ReadOuterSecurity(SafeFileHandle handle)
    {
        // This is the explicitly queried permission/label subset. It does NOT imply that an audit SACL, resource
        // policy or other unqueried security metadata is absent; a release preservation profile must address those.
        int error = GetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, WindowsArchiveOuterMetadata.QueriedSecurityInformation,
            out _, out _, out _, out _, out nint pointer);
        try
        {
            if (error != 0) throw new Win32Exception(error);
            if (pointer == 0) throw new IOException("The archive permission descriptor is unavailable.");
            int length = GetSecurityDescriptorLength(pointer);
            if (length <= 0 || length > WindowsArchiveOuterMetadata.MaxSecurityBytes)
                throw new IOException("The archive permission descriptor exceeds the supported bound.");
            byte[] bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }
        finally { if (pointer != 0) LocalFree(pointer); }
    }

    internal static void Flush(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static void RequireOnlyDataStream(SafeFileHandle handle, long expectedLength)
    {
        // A new unmarked private archive needs exactly the unnamed data stream. A fixed bounded query suffices:
        // overflow, a following entry or any other name refuses rather than allocating from an untrusted list size.
        byte* bytes = stackalloc byte[256]; new Span<byte>(bytes, 256).Clear(); IoStatus io = default;
        int status = NtQueryInformationFile(handle, &io, bytes, 256, 22 /* FileStreamInformation */);
        if (status != 0 || io.Information is < 24 or > 256 || *(uint*)bytes != 0 ||
            *(long*)(bytes + 8) != expectedLength || *(long*)(bytes + 16) < 0) throw UnexpectedStreams();
        uint length = *(uint*)(bytes + 4);
        if (length != 0 && (length != 14 || io.Information < 24 + length || !new ReadOnlySpan<char>((char*)(bytes + 24), 7).SequenceEqual("::$DATA")))
            throw UnexpectedStreams();
    }
    private static ArchiveWriteException UnexpectedStreams() => new(ArchiveWriteReason.PublicationUnavailable,
        "The new archive's complete unnamed-stream-only state could not be established.");

    internal static void DeleteExact(SafeFileHandle handle)
    {
        uint flags = 1 | 2; // DELETE | POSIX_SEMANTICS. Never ignore read-only, reopen a path, or use a fallback.
        if (!SetFileInformationByHandle(handle, 21 /* FileDispositionInfoEx */, &flags, sizeof(uint)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryVolumeInformationFile(SafeFileHandle handle, IoStatus* io, void* buffer, uint size, int informationClass);
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationFile(SafeFileHandle handle, IoStatus* io, void* buffer, uint size, int informationClass);
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationByHandle(SafeFileHandle handle, char* label, uint labelSize, out uint serial,
        out uint maximumComponentLength, out uint flags, char* fileSystem, uint fileSystemSize);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(SafeFileHandle handle);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, void* buffer, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* buffer, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushFileBuffers(SafeFileHandle handle);
    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityInfo(SafeFileHandle handle, int objectType, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityDescriptorLength(nint descriptor);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
