using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>The volume actually queried, separate from the scheduler's device grouping key.</summary>
public sealed record FileSystemCapacitySnapshot(string VolumeIdentity, long AvailableBytes);

/// <summary>Available-to-user bytes for the filesystem containing a directory, after resolving links.</summary>
public static unsafe partial class FileSystemCapacity
{
    public static FileSystemCapacitySnapshot? Query(string path)
    {
        try
        {
            string resolved = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows())
            {
                // DriveInfo maps mounted folders to the enclosing drive and rejects UNC shares.
                // The native calls use the volume containing this directory, including mount points.
                string? volume = WindowsContainingVolume(resolved);
                if (volume is null || !GetDiskFreeSpaceEx(volume, out ulong available, out _, out _)) return null;
                return new FileSystemCapacitySnapshot(volume, (long)Math.Min(available, long.MaxValue));
            }
            resolved = UnixFiles.RealPath(resolved) ?? resolved;
            var drives = DriveInfo.GetDrives();
            string? root = SelectUnixMountRoot(resolved, drives.Select(d => d.Name));
            DriveInfo? drive = drives.FirstOrDefault(d => d.Name == root);
            if (drive is null || !drive.IsReady) return null;
            long free = drive.AvailableFreeSpace;
            return free < 0 ? null : new FileSystemCapacitySnapshot(drive.Name, free);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A disconnected share or an inaccessible mount has no current capacity to display.
            return null;
        }
    }

    /// <summary>A UNC share, mapped drive or volume GUID has its own capacity identity.</summary>
    public static string? WindowsVolumeRoot(string resolvedPath)
    {
        string? root = Path.GetPathRoot(resolvedPath);
        return string.IsNullOrEmpty(root) || Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    }

    private static string? WindowsContainingVolume(string resolvedPath)
    {
        // A long mount path can be returned. The native API writes at most this many UTF-16 characters.
        char[] buffer = new char[32768];
        fixed (char* volume = buffer)
        {
            if (GetVolumePathName(resolvedPath, volume, buffer.Length))
                return new string(volume);
        }
        // UNC roots are still valid if a server cannot resolve a deeper path. The subsequent
        // free-space call decides whether the share is reachable; no local-drive fallback is used.
        return resolvedPath.StartsWith(@"\\", StringComparison.Ordinal) ? WindowsVolumeRoot(resolvedPath) : null;
    }

    /// <summary>Longest containing mount; a sibling with the same prefix is a different filesystem.</summary>
    public static string? SelectUnixMountRoot(string resolvedPath, IEnumerable<string> roots)
    {
        string? best = null;
        foreach (string name in roots)
        {
            string root = name == "/" ? "/" : name.TrimEnd('/');
            if (root.Length == 0) continue;
            bool contains = root == "/" && resolvedPath.StartsWith('/') ||
                            resolvedPath == root || resolvedPath.StartsWith(root + "/", StringComparison.Ordinal);
            if (contains && (best is null || root.Length > best.Length)) best = name;
        }
        return best;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathName(string path, char* volumePath, int capacity);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
}
