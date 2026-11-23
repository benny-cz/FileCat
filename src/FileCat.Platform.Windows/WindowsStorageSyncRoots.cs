using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FileCat.Platform.Windows;

/// <summary>
/// The Cloud Files sync roots registered on this computer for every account
/// (HKLM\…\Explorer\SyncRootManager\{id}\UserSyncRoots\{SID}), resolved to the volumes that hold them.
/// Storage admission uses this only when no Cloud Files filter answers the held-object membership query.
/// </summary>
internal static unsafe partial class WindowsStorageSyncRoots
{
    private const string ManagerKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";
    internal const int RegistrationLimit = 1024;
    private const long CacheMilliseconds = 2000;
    private sealed record Snapshot(long At, IReadOnlySet<string> Volumes);
    private static Snapshot? _cached;

    /// <summary>
    /// The volumes that hold a registered sync root, or null when the registrations could not be read. A root whose
    /// path is on no mounted volume is skipped: it cannot hold an object reached through a mounted volume path, and a
    /// sync root's own directory carries a Cloud Files reparse tag that the held no-follow walk refuses as an ancestor.
    /// A successful read is reused for two seconds so a recursive job does not rescan the registry for every item.
    /// </summary>
    internal static IReadOnlySet<string>? RegisteredRootVolumes()
    {
        long now = Environment.TickCount64;
        if (Volatile.Read(ref _cached) is { } cached && now - cached.At is >= 0 and < CacheMilliseconds) return cached.Volumes;
        if (RegisteredRootPaths() is not { } paths) return null;
        var volumes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
            if (VolumeOf(path) is { } volume) volumes.Add(volume);
        Volatile.Write(ref _cached, new Snapshot(now, volumes));
        return volumes;
    }

    /// <summary>Every registered root path, or null when the registrations could not be read completely within the bound.</summary>
    internal static IReadOnlyList<string>? RegisteredRootPaths()
    {
        try
        {
            using var manager = Registry.LocalMachine.OpenSubKey(ManagerKey);
            if (manager is null) return [];
            var paths = new List<string>();
            string[] ids = manager.GetSubKeyNames();
            if (ids.Length > RegistrationLimit) return null;
            foreach (string id in ids)
            {
                using var users = manager.OpenSubKey(id + @"\UserSyncRoots");
                if (users is null) continue;
                foreach (string account in users.GetValueNames())
                {
                    // A registration whose path is unreadable or malformed cannot be placed on a volume.
                    if (users.GetValue(account, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string path || path.Length == 0) return null;
                    if (paths.Count == RegistrationLimit) return null;
                    paths.Add(path);
                }
            }
            return paths;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>The volume GUID ("D" format) holding <paramref name="path"/>, following mount points; null when it cannot be resolved.</summary>
    internal static string? VolumeOf(string path)
    {
        if (path.Length is 0 or > 32_000 || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\\?\", StringComparison.Ordinal)) return null;
        var mount = new char[path.Length + 2];
        fixed (char* m = mount)
            if (!GetVolumePathName(path, m, (uint)mount.Length)) return null;
        string mountPoint = new string(mount).TrimEnd('\0');
        if (!mountPoint.EndsWith('\\')) mountPoint += '\\';
        var name = new char[64];
        fixed (char* n = name)
            if (!GetVolumeNameForVolumeMountPoint(mountPoint, n, (uint)name.Length)) return null;
        string volume = new string(name).TrimEnd('\0');
        return volume.Length == 49 && volume.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) && volume[48] == '\\' &&
            Guid.TryParse(volume.AsSpan(10, 38), out var guid) ? guid.ToString("D") : null;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathName(string fileName, char* volumePathName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, char* volumeName, uint bufferLength);
}
