using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>Current whole-volume selection from Windows' volume manager. A folder label or drive letter is never broker authority.</summary>
internal sealed partial record NtfsMetadataVolume(string GuidPath, string Serial, string RootFileId)
{
    private const string GuidPrefix = @"\\?\Volume{";

    internal static string? CanonicalGuid(string? path)
    {
        if (path is null || path.Length != 49 || !path.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith("}\\", StringComparison.Ordinal) ||
            !Guid.TryParseExact(path.AsSpan(GuidPrefix.Length, 36), "D", out var id) || id == Guid.Empty)
            return null;
        return GuidPrefix + id.ToString("D") + "}\\";
    }

    internal static NtfsMetadataVolume CaptureSelection(string selected, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        cancellation.ThrowIfCancellationRequested();
        var canonical = CanonicalGuid(selected);
        bool driveLetter = canonical is null;
        if (driveLetter)
        {
            if (selected.Length != 3 || !char.IsAsciiLetter(selected[0]) || selected[1] != ':' || selected[2] != '\\' ||
                Native.GetDriveType(selected) is not (2 or 3))
                throw new NotSupportedException("Choose a whole local NTFS volume. Use standard analysis for folders.");
            canonical = Native.FromMount(selected);
        }
        using var root = Native.OpenRoot(canonical!, cancellation);
        var captured = Native.Capture(canonical!, root);
        if (driveLetter)
        {
            using var selectedRoot = Native.OpenDirectory(selected);
            if (Native.Capture(canonical!, selectedRoot) != captured || Native.FromMount(selected) != canonical)
                throw new AnalysisSourceChangedException();
        }
        return captured;
    }

    internal SafeFileHandle ReopenRoot(CancellationToken cancellation)
    {
        var root = Native.OpenRoot(GuidPath, cancellation);
        try
        {
            if (Native.Capture(GuidPath, root) != this) throw new AnalysisSourceChangedException();
            return root;
        }
        catch { root.Dispose(); throw; }
    }

    internal static partial class Native
    {
        internal static string FromMount(string root)
        {
            var buffer = new char[64];
            return GetVolumeNameForVolumeMountPoint(root, buffer, (uint)buffer.Length) && CanonicalGuid(Contents(buffer)) is { } volume
                ? volume : throw new NotSupportedException("The selected drive is not a canonical local volume mount.");
        }

        internal static SafeFileHandle OpenRoot(string canonical, CancellationToken cancellation)
        {
            if (canonical != CanonicalGuid(canonical) || GetDriveType(canonical) is not (2 or 3) ||
                !IsPresent(canonical, cancellation))
                throw new NotSupportedException("The selected whole volume is unavailable.");
            return OpenDirectory(canonical);
        }

        internal static SafeFileHandle OpenDirectory(string path)
        {
            var root = CreateFile(path, 0x80 /* FILE_READ_ATTRIBUTES */, 7, 0, 3,
                0x02200000 /* BACKUP_SEMANTICS | OPEN_REPARSE_POINT */, 0);
            if (!root.IsInvalid) return root;
            root.Dispose();
            throw new IOException("The selected NTFS root could not be opened.");
        }

        internal static unsafe NtfsMetadataVolume Capture(string canonical, SafeFileHandle root)
        {
            var path = new char[64];
            uint written = GetFinalPathNameByHandle(root, path, (uint)path.Length, 1 /* VOLUME_NAME_GUID */);
            if (written == 0 || written >= path.Length || CanonicalGuid(new string(path, 0, (int)written)) != canonical)
                throw new AnalysisSourceChangedException();
            ushort* fileSystem = stackalloc ushort[16];
            if (!GetVolumeInformationByHandle(root, null, 0, null, null, null, fileSystem, 16) ||
                !new ReadOnlySpan<ushort>(fileSystem, 5).SequenceEqual(new ushort[] { 'N', 'T', 'F', 'S', 0 }))
                throw new NotSupportedException("The selected volume does not expose local NTFS metadata.");
            Span<byte> identity = stackalloc byte[24];
            fixed (byte* data = identity)
                if (!GetFileInformationByHandleEx(root, 18 /* FileIdInfo */, data, 24))
                    throw new IOException("The selected NTFS root has no verifiable identity.");
            ulong serial = BinaryPrimitives.ReadUInt64LittleEndian(identity);
            UInt128 file = BinaryPrimitives.ReadUInt128LittleEndian(identity[8..]);
            if (serial == 0 || file == 0) throw new IOException("The selected NTFS root has no verifiable identity.");
            return new(canonical, serial.ToString("X16"), file.ToString("X32"));
        }

        private static bool IsPresent(string canonical, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var buffer = new char[64];
            using var search = FindFirstVolume(buffer, (uint)buffer.Length);
            if (search.IsInvalid) throw new IOException("Windows volume enumeration is unavailable.");
            for (int i = 0; i < 1024; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (CanonicalGuid(Contents(buffer)) == canonical) return true;
                if (FindNextVolume(search, buffer, (uint)buffer.Length)) continue;
                int error = Marshal.GetLastPInvokeError();
                if (error == 18 /* ERROR_NO_MORE_FILES */) return false;
                throw new IOException("Windows volume enumeration could not finish.", new Win32Exception(error));
            }
            throw new NotSupportedException("Windows volume enumeration exceeded the supported limit.");
        }

        private static string Contents(char[] buffer)
        {
            int length = Array.IndexOf(buffer, '\0');
            return length < 0 ? "" : new string(buffer, 0, length);
        }

        private sealed class VolumeSearchHandle : SafeHandle
        {
            private VolumeSearchHandle() : base(-1, ownsHandle: true) { }
            public override bool IsInvalid => handle == -1 || handle == 0;
            protected override bool ReleaseHandle() => FindVolumeClose(handle);
        }

        [DllImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern VolumeSearchHandle FindFirstVolume([Out] char[] name, uint size);
        [DllImport("kernel32.dll", EntryPoint = "FindNextVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextVolume(VolumeSearchHandle handle, [Out] char[] name, uint size);
        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool FindVolumeClose(nint search);
        [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mount, [Out] char[] volume, uint size);
        [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint GetDriveType(string root);
        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint size, uint flags);
        [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetVolumeInformationByHandle(SafeFileHandle file, ushort* label, uint labelSize, uint* serial,
            uint* maximumComponent, uint* flags, ushort* format, uint formatSize);
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle file, int info, byte* buffer, uint capacity);
    }
}
