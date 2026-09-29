using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.App.ViewModels;

/// <summary>
/// Integrated recovery (plan §17, P10): the deleted items of a disk image, or of a drive (read by FileCat itself when it
/// runs as administrator, otherwise through the administrator helper), browsed and recovered like any folder. A scan
/// opens in a tab of its own, so the panels stay where they are and the other panel remains the place to recover to.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>File systems whose deleted items a scan finds.</summary>
    private static readonly string[] RecoverableFormats = ["NTFS", "FAT", "FAT12", "FAT16", "FAT32", "exFAT"];

    private static bool IsRecoverableDrive(DriveTag drive) =>
        drive.Ready && drive.DriveType is "Fixed" or "Removable" && drive.Format is { } format && RecoverableFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Recover deleted files (plan §17): asks what to scan, a drive or a disk image, with the drive of this panel's folder
    /// (or the drive or image under the cursor) already chosen, and shows the scan in a new tab. Inside a scan of a FAT
    /// volume it also offers to search that volume's free space.
    /// </summary>
    private async Task FindDeletedAsync()
    {
        var tab = ActiveTab;
        var panel = Workspace.ActivePanel;
        if (tab?.Location is null || panel is null) return;
        var location = tab.Location;
        var icons = Services.Icons;
        var choices = new List<(ChoiceItem Item, Func<Task> Run)>();
        int selected = -1;
        if (location.Scheme == Schemes.Recovery)
        {
            var search = Services.Recovery.DescribeFreeSpaceSearch(location);
            if (search is { Searched: true })
            {
                Notify("This volume's free space was searched already.");
                return;
            }
            if (search is not null)
            {
                selected = choices.Count;
                choices.Add((new ChoiceItem("Search this volume's free space",
                    $"{search.Volume}: reads its {Formatters.SizeWithUnit(search.FreeBytes)} of free space for deleted folders' contents that FAT no longer points to"),
                    () => SearchFreeSpaceAsync(tab, location)));
            }
        }
        bool hasFocus = tab.Listing.TryGetFocused(out var focused);
        if (hasFocus && focused.Kind == EntryKind.File && DiskImages.IsImageName(focused.Name) && tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath is { } image)
        {
            if (selected < 0) selected = choices.Count;
            var entry = focused;
            var folder = Location.FileSystem(Path.GetDirectoryName(image)!);
            choices.Add((new ChoiceItem(Path.GetFileName(image), "The disk image under the cursor", "image") { Icon = () => icons.GetIcon(entry, folder) },
                () => OpenImageScanAsync(panel, image)));
        }
        if (OperatingSystem.IsWindows())
        {
            string? focusedDrive = hasFocus && focused.Kind == EntryKind.Drive && focused.Tag is DriveTag d ? d.RootPath : null;
            string? here = location.IsFileSystem ? Path.GetPathRoot(location.Path) : null;
            int preferred = -1;
            foreach (var drive in await RecoverableDrivesAsync())
            {
                bool isHere = string.Equals(drive.RootPath, here, StringComparison.OrdinalIgnoreCase);
                bool isFocused = string.Equals(drive.RootPath, focusedDrive, StringComparison.OrdinalIgnoreCase);
                if (isFocused || isHere && preferred < 0) preferred = choices.Count;
                var row = new EntryData(drive.RootPath.TrimEnd('\\'), EntryKind.Drive) { Tag = drive };
                string? folder = isHere ? location.Path : null;
                choices.Add((new ChoiceItem(DriveName(drive), RecoveryDriveDetail(drive), isHere ? "this folder's drive" : isFocused ? "under the cursor" : null)
                    { Icon = () => icons.GetIcon(row) }, () => FindDeletedOnDriveAsync(panel, drive, folder)));
            }
            if (selected < 0) selected = preferred;
        }
        choices.Add((new ChoiceItem("Disk image file…", "Choose a raw .img, .dd, or .bin file, or a fixed .vhd, on any drive"), () => ChooseImageAsync(panel, location)));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Recover deleted files", choices.Select(c => c.Item).ToList())
        {
            SelectedIndex = Math.Max(0, selected),
            Icons = icons,
            Hint = OperatingSystem.IsWindows()
                ? "Where were the files deleted? · Enter scans it; nothing there is changed · Esc closes"
                : "Drives are scanned directly in FileCat for Windows: image a drive first (for example with dd) · Esc closes",
        });
        if (r.Index < 0 || r.Index >= choices.Count) return;
        await choices[r.Index].Run();
    }

    /// <summary>"D: Data", or "D:" for a drive without a label.</summary>
    private static string DriveName(DriveTag drive) =>
        drive.RootPath.TrimEnd('\\') + (string.IsNullOrWhiteSpace(drive.Label) ? "" : " " + drive.Label!.Trim());

    private static string RecoveryDriveDetail(DriveTag drive)
    {
        string kind = drive.DriveType == "Removable" ? "removable drive" : "drive";
        string detail = $"{drive.Format} · {Formatters.SizeWithUnit(drive.TotalBytes)} {kind}";
        return string.Equals(Path.GetPathRoot(Environment.SystemDirectory), drive.RootPath, StringComparison.OrdinalIgnoreCase)
            ? detail + " · Windows runs from it and keeps writing to it"
            : detail;
    }

    /// <summary>The drives a scan can read (local NTFS, FAT, and exFAT), each as it describes itself within a moment.</summary>
    private async Task<IReadOnlyList<DriveTag>> RecoverableDrivesAsync()
    {
        if (Services.Providers.For(new Location(Schemes.Computer, string.Empty)) is not ComputerProvider computer) return [];
        var drives = await computer.QueryDrivesAsync(TimeSpan.FromMilliseconds(700), CancellationToken.None);
        return drives.Where(IsRecoverableDrive).ToList();
    }

    /// <summary>What the context menu offers for the item under the cursor: a scan of this drive or image, or nothing.</summary>
    public string? RecoveryOfferForFocus()
    {
        var tab = ActiveTab;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var f)) return null;
        if (f.Kind == EntryKind.Drive && f.Tag is DriveTag drive && OperatingSystem.IsWindows() && IsRecoverableDrive(drive)) return "Recover deleted files from this drive…";
        if (f.Kind == EntryKind.File && tab.Location.IsFileSystem && DiskImages.IsImageName(f.Name)) return "Recover deleted files from this disk image…";
        return null;
    }

    /// <summary>The context menu's recovery: straight to the scan of the drive or image under the cursor.</summary>
    public async void RecoverFromFocus()
    {
        try
        {
            var tab = ActiveTab;
            var panel = Workspace.ActivePanel;
            if (tab?.Location is null || panel is null || RecoveryOfferForFocus() is null || !tab.Listing.TryGetFocused(out var f)) return;
            if (f.Kind == EntryKind.Drive && f.Tag is DriveTag drive) await FindDeletedOnDriveAsync(panel, drive, null);
            else if (tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath is { } image) await OpenImageScanAsync(panel, image);
        }
        catch (Exception ex)
        {
            AppLog.Error("Recover deleted files failed", ex);
            Notify("Recover deleted files failed: " + ex.Message, true);
        }
    }

    /// <summary>The Windows context menu's recovery: the scan of that disk image, in a new tab of the active panel.</summary>
    public async void RecoverImage(string image)
    {
        try
        {
            if (Workspace.ActivePanel is { } panel) await OpenImageScanAsync(panel, image);
        }
        catch (Exception ex)
        {
            AppLog.Error("Recover deleted files failed", ex);
            Notify("Recover deleted files failed: " + ex.Message, true);
        }
    }

    private async Task ChooseImageAsync(PanelViewModel panel, Location current)
    {
        var top = View.TopLevel;
        if (top is null) return;
        var start = current.IsFileSystem ? await top.StorageProvider.TryGetFolderFromPathAsync(current.Path) : null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Disk image to recover deleted files from",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("Disk images") { Patterns = DiskImages.Extensions.Select(e => "*" + e).ToArray() }, FilePickerFileTypes.All],
        });
        if (files.Count == 0) return;
        if (files[0].TryGetLocalPath() is not { } image)
        {
            Notify("Choose a disk image on a drive of this computer.", true);
            return;
        }
        await OpenImageScanAsync(panel, image);
    }

    /// <summary>A disk image's deleted items in a new tab: its only volume directly, or the list of its volumes.</summary>
    private async Task OpenImageScanAsync(PanelViewModel panel, string image)
    {
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
        panel.OpenTab(RecoveryProvider.ForImage(image, volumes == 1 ? 1 : null));
        Notify("The deleted items are in a new tab: mark what to recover and copy it (F5) to a folder on another drive.");
    }

    /// <summary>
    /// Search free space, inside a recovery view: the scan read what the file system records; this also reads the
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

    /// <summary>Why drives cannot be scanned here, or null when they can (FileCat runs as administrator, or its helper can ask).</summary>
    private string? DriveScanProblem()
    {
        if (!OperatingSystem.IsWindows() || Services.Recovery.OpenDevice is null)
            return "Scanning drives directly is available in FileCat for Windows. Make an image of the drive on another drive (for example with dd), then recover deleted files from the image.";
        if (Environment.IsPrivilegedProcess || FileCat.Platform.Windows.Elevation.ElevationBroker.Locate(Services.Paths.IsPortable, out _) is not null) return null;
        return $"Reading a drive needs administrator rights, and {(Services.Paths.IsPortable ? "portable FileCat" : "this FileCat build")} has no installed helper to ask Windows for them. " +
               "Start FileCat as administrator (right-click it, then Run as administrator) to scan drives, or recover from a disk image of the drive.";
    }

    /// <summary>
    /// A drive: after a confirmation, its scan opens in a new tab (read by FileCat itself when it runs as administrator,
    /// otherwise through the helper, which Windows asks to approve). With <paramref name="folder"/>, the tab then goes to
    /// that folder, or as close to it as the scan goes.
    /// </summary>
    private async Task FindDeletedOnDriveAsync(PanelViewModel panel, DriveTag drive, string? folder)
    {
        if (DriveScanProblem() is { } problem)
        {
            Notify(problem, true);
            return;
        }
        if (drive.DriveType is "Network" or "CD-ROM" or "CDRom" || !drive.Ready)
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
        string how = Environment.IsPrivilegedProcess
            ? "FileCat runs as administrator and reads the drive itself, only reading: nothing on it is changed. "
            : "Windows asks for administrator approval, and FileCat's helper then only reads the drive: nothing on it is changed. ";
        string text = $"Scan {name} for deleted files? " + how +
                      (system ? "Windows runs from this drive and keeps writing to it, so deleted files can be overwritten at any moment; for the best chance, image the drive from another computer. " : "") +
                      (ownFiles && !system ? "FileCat keeps its own settings and logs on this disk, and writes to them while it works. " : "") +
                      "The scan opens in a new tab. Recover files to another disk, and write nothing to this one meanwhile.";
        if (!await Dialogs.ConfirmAsync("Recover deleted files", text, "Scan")) return;
        var root = Services.Recovery.ForDevice(device, name, 1);
        var scan = panel.OpenTab(root);
        if (folder is not null && folder.Length > drive.RootPath.Length)
            GoToFolderWhenScanned(scan, root, folder[drive.RootPath.Length..].Replace('\\', '/').Trim('/'));
    }

    /// <summary>
    /// Once the scan lists the volume, goes to the folder the user came from, or as close to it as the scan goes:
    /// folders with nothing deleted below them are not part of a scan. Leaving the tab's first listing cancels it.
    /// </summary>
    private void GoToFolderWhenScanned(TabViewModel scan, Location root, string folder)
    {
        if (folder.Length == 0) return;
        var listing = scan.Listing;
        void Handler(object? sender, ListingChange change)
        {
            if (!Equals(listing.Location, root))
            {
                listing.Changed -= Handler;
                return;
            }
            if (listing.State is ListingState.Loading or ListingState.Empty) return;
            listing.Changed -= Handler;
            if (listing.State == ListingState.Complete && Services.Recovery.ClosestFolder(root, folder) is { } closest && !Equals(closest, root))
                scan.Navigate(closest);
        }
        listing.Changed += Handler;
    }
}
