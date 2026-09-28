using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.App.ViewModels;

/// <summary>
/// Integrated recovery (plan §17, P10): the deleted items of a disk image, or of a drive read through the administrator
/// helper, browsed and recovered like any folder.
/// </summary>
public sealed partial class MainViewModel
{
    private async Task FindDeletedAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is null) return;
        bool hasFocus = tab.Listing.TryGetFocused(out var focused);
        if (hasFocus && focused.Kind == EntryKind.Drive && focused.Tag is DriveTag drive)
        {
            await FindDeletedOnDriveAsync(tab, drive);
            return;
        }
        string? image = hasFocus && focused.Kind == EntryKind.File ? tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath : null;
        if (image is null)
        {
            Notify("Focus a disk image (a raw .img, .dd, or .bin file, or a fixed .vhd) to find its deleted files, or a drive in This PC to scan the drive itself.", true);
            return;
        }
        int volumes;
        try
        {
            volumes = await Task.Run(() =>
            {
                using var source = new ImageFileSource(image);
                return PartitionTable.Find(source, []).Count;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notify($"\"{Path.GetFileName(image)}\" cannot be read as a disk image: {ex.Message}", true);
            return;
        }
        if (volumes == 0)
        {
            Notify($"\"{Path.GetFileName(image)}\" is not a disk image FileCat reads: it has no partition table and no NTFS, FAT, or exFAT file system.", true);
            return;
        }
        // One volume opens directly; a partitioned disk lists its volumes first.
        tab.Navigate(RecoveryProvider.ForImage(image, volumes == 1 ? 1 : null));
    }

    /// <summary>A drive: its volume is read through the administrator helper after an explicit confirmation here.</summary>
    private async Task FindDeletedOnDriveAsync(TabViewModel tab, DriveTag drive)
    {
        if (!OperatingSystem.IsWindows() || Services.Recovery.OpenDevice is null)
        {
            Notify("Scanning drives directly is available in FileCat for Windows. Make an image of the drive on another drive (for example with dd), then find deleted files in the image.", true);
            return;
        }
        if (drive.DriveType is "Network" or "CD-ROM" || !drive.Ready)
        {
            Notify($"{drive.RootPath} cannot be scanned for deleted files: only local drives with NTFS, FAT, or exFAT can.", true);
            return;
        }
        string? device = FileCat.Platform.Windows.Recovery.DeviceTopology.VolumeDevice(drive.RootPath);
        if (device is null)
        {
            Notify($"{drive.RootPath} is not a local volume FileCat can read directly.", true);
            return;
        }
        string name = $"drive {drive.RootPath.TrimEnd('\\')}" + (string.IsNullOrWhiteSpace(drive.Label) ? "" : $" ({drive.Label})");
        bool system = string.Equals(Path.GetPathRoot(Environment.SystemDirectory), drive.RootPath, StringComparison.OrdinalIgnoreCase);
        bool ownFiles = FileCat.Platform.Windows.Recovery.DeviceTopology.SharesDisk(device, Services.Paths.JournalDirectory) != false;
        string text = $"Scan {name} for deleted files? Windows asks for administrator approval, and FileCat's helper then only reads the drive: nothing on it is changed. " +
                      (system ? "Windows runs from this drive and keeps writing to it, so deleted files can be overwritten at any moment; for the best chance, image the drive from another computer. " : "") +
                      (ownFiles && !system ? "FileCat keeps its own settings and logs on this disk, and writes to them while it works. " : "") +
                      "Recover files to another disk, and write nothing to this one meanwhile.";
        if (!await Dialogs.ConfirmAsync("Find deleted files", text, "Scan")) return;
        tab.Navigate(Services.Recovery.ForDevice(device, name, 1));
    }
}
