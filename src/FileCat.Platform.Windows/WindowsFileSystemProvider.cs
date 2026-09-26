using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using static FileCat.Platform.Windows.Native.NativeMethods;

namespace FileCat.Platform.Windows;

/// <summary>
/// Windows file-system provider: device keys per volume or SMB server (queues and hang isolation, §6.3)
/// and Recycle Bin availability per location (none on UNC shares and most removable media, §5.3).
/// </summary>
public sealed class WindowsFileSystemProvider : LocalFileSystemProvider
{
    public override string GetDeviceKey(Location location)
    {
        var path = location.Path;
        if (PathUtil.IsUncPath(path)) return PathUtil.GetDeviceKey(path);
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return "local";
        // Mapped network drives share the server's queue; local drives get their own.
        if (root.Length >= 2 && root[1] == ':' && GetDriveType(root) == DRIVE_REMOTE && WindowsNetwork.GetRemoteName(root[..2]) is { } unc)
            return PathUtil.GetDeviceKey(unc);
        return root.TrimEnd('\\').ToUpperInvariant();
    }

    protected override bool CanRecycle(Location location) => WindowsFileOperations.RecycleBinExists(location.Path);
}

/// <summary>Drive list with Windows drive types and mapped-drive remote names.</summary>
public sealed class WindowsComputerProvider : ComputerProvider
{
    protected override DriveTag QueryDrive(string root)
    {
        var tag = base.QueryDrive(root);
        uint type = GetDriveType(root);
        string typeName = type switch
        {
            DRIVE_REMOVABLE => "Removable",
            DRIVE_FIXED => "Fixed",
            DRIVE_REMOTE => "Network",
            DRIVE_CDROM => "CDRom",
            DRIVE_RAMDISK => "Ram",
            _ => tag.DriveType,
        };
        string? remote = type == DRIVE_REMOTE ? WindowsNetwork.GetRemoteName(root.TrimEnd('\\')) : null;
        return tag with { DriveType = typeName, RemoteName = remote, Label = tag.Label ?? remote };
    }
}
