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
        if (tab.Location.Scheme == Schemes.Recovery)
        {
            await SearchFreeSpaceAsync(tab, tab.Location);
            return;
        }
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

    /// <summary>
    /// Find deleted files again, inside a recovery view: the scan read what the file system records; this also reads the
    /// volume's free space once for deleted folders' lists of contents that FAT no longer points to.
    /// </summary>
    private async Task SearchFreeSpaceAsync(TabViewModel tab, Location location)
    {
        var search = Services.Recovery.DescribeFreeSpaceSearch(location);
        if (search is null)
        {
            Notify(location.Session is null
                ? "Open a volume first: its free space can then be searched for more deleted items."
                : "Only FAT volumes lose track of deleted folders' contents; this volume's scan already shows all it records.", true);
            return;
        }
        if (search.Searched)
        {
            Notify("This volume's free space was searched already.");
            return;
        }
        string open = search.OpenListings > 0
            ? $" {search.OpenListings} deleted folder{(search.OpenListings == 1 ? "'s list" : "s' lists")} of contents may go on there."
            : "";
        string text = $"The scan read what the file system records. Searching free space also reads all {Formatters.SizeWithUnit(search.FreeBytes)} of free space on {search.Volume}, " +
                      "once and in order, for deleted folders' lists of contents that FAT no longer points to." + open +
                      " That takes minutes on a USB stick and longer on large drives; nothing is written, and Esc stops it.";
        if (!await Dialogs.ConfirmAsync("Search free space", text, "Search")) return;
        Services.Recovery.SearchFreeSpace(location);
        tab.Refresh();
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
