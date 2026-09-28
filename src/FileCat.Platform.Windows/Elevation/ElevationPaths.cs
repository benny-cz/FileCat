using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>
/// Paths across the elevation boundary (plan §6.2): drive letters are per logon session (mapped drives, SUBST), so the
/// requester resolves every path to its volume-GUID form, and the broker shows its own drive-letter resolution.
/// </summary>
public static partial class ElevationPaths
{
    private const uint OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const uint VolumeNameGuid = 0x1, DriveRemote = 4;

    /// <summary>
    /// The volume-GUID path (\\?\Volume{…}\…) of an existing local item. A final link is resolved to itself (the link),
    /// never to its target; folders on the way resolve to where they really are.
    /// </summary>
    public static string ToVolumePath(string path)
    {
        string full = Path.GetFullPath(path);
        if (PathUtil.IsUncPath(full) || GetDriveType(Path.GetPathRoot(full) ?? full) == DriveRemote)
            throw new NotSupportedException("Administrator rights on this computer do not change access to network shares, so network locations are not retried as administrator.");
        string native = full.Length >= 248 && !full.StartsWith(@"\\?\", StringComparison.Ordinal) ? @"\\?\" + full : full;
        using var handle = CreateFile(native, 0, 7, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, $"{full}: {new Win32Exception(error).Message}");
        }
        return FinalPath(handle, VolumeNameGuid) ?? throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static unsafe string? FinalPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new char[1024];
        while (true)
        {
            uint length;
            fixed (char* chars = buffer) length = GetFinalPathNameByHandle(handle, chars, (uint)buffer.Length, flags);
            if (length == 0) return null;
            if (length < buffer.Length) return new string(buffer, 0, (int)length);
            buffer = new char[length + 1];
        }
    }

    /// <summary>A drive-letter path for display, resolved by this process from the volume GUID (or the GUID path itself).</summary>
    public static unsafe string ToDisplayPath(string volumePath)
    {
        int close = volumePath.IndexOf("}\\", StringComparison.Ordinal);
        if (!volumePath.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) || close < 0) return volumePath;
        string volume = volumePath[..(close + 2)];
        var names = new char[1024];
        bool ok;
        fixed (char* chars = names) ok = GetVolumePathNamesForVolumeName(volume, chars, (uint)names.Length, out _);
        if (!ok) return volumePath;
        int end = Array.IndexOf(names, '\0');
        if (end <= 0) return volumePath;
        return new string(names, 0, end) + volumePath[(close + 2)..];
    }

    /// <summary>Whether two volume paths are on the same volume (a move is a rename only within one volume).</summary>
    public static bool SameVolume(string a, string b)
    {
        int ca = a.IndexOf("}\\", StringComparison.Ordinal), cb = b.IndexOf("}\\", StringComparison.Ordinal);
        return ca > 0 && ca == cb && string.Compare(a, 0, b, 0, ca, StringComparison.OrdinalIgnoreCase) == 0;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNamesForVolumeNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetVolumePathNamesForVolumeName(string volume, char* names, uint length, out uint needed);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);
}
