using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Recovery;
using FileCat.Recovery.Unix;

namespace FileCat.App.ViewModels;

/// <summary>
/// Integrated recovery (plan §17, P10): the deleted items of a disk image, a drive, or a whole disk (read by FileCat
/// itself when it runs as administrator, otherwise through the administrator helper), browsed and recovered like any
/// folder. A whole disk also shows partitions that were deleted, or whose table was lost (D-46). A scan opens in a tab of
/// its own, so the panels stay where they are and the other panel remains the place to recover to.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>File systems whose deleted items a scan finds, as Windows, Linux, and macOS name them.</summary>
    private static readonly string[] RecoverableFormats = ["NTFS", "FAT", "FAT12", "FAT16", "FAT32", "exFAT", .. UnixDisks.RecoverableTypes];

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
            // What a scan can still do here: search a FAT volume's free space, or the disk's space in no partition.
            if (Services.Recovery.DescribeFreeSpaceSearch(location) is { Searched: false } search)
            {
                selected = choices.Count;
                choices.Add((new ChoiceItem("Search this volume's free space",
                    $"{search.Volume}: reads its {Formatters.SizeWithUnit(search.FreeBytes)} of free space for deleted folders' contents that FAT no longer points to"),
                    () => SearchFreeSpaceAsync(tab, location)));
            }
            if (Services.Recovery.DescribeDiskSearch(location) is { Searched: false } disk)
            {
                if (selected < 0) selected = choices.Count;
                choices.Add((new ChoiceItem("Search this disk for deleted partitions",
                    $"Reads all {Formatters.SizeWithUnit(disk.Unpartitioned)} that lie in no partition, a megabyte at a time, for partitions that were deleted or whose table was lost"),
                    () => SearchDiskAsync(tab, location)));
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
            // Whole disks: their partitions, and the ones that were deleted or that Windows cannot read.
            foreach (var disk in await Task.Run(FileCat.Platform.Windows.Recovery.DeviceTopology.Disks))
            {
                var row = new EntryData(DiskName(disk), EntryKind.Drive) { Tag = new DriveTag(disk.Device, disk.Model, IsRemovable(disk) ? "Removable" : "Fixed", null, -1, disk.Length, true) };
                choices.Add((new ChoiceItem(DiskName(disk), DiskDetail(disk)) { Icon = () => icons.GetIcon(row) }, () => FindDeletedOnDiskAsync(panel, disk)));
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // Mounted FAT, exFAT, and NTFS volumes (this folder's chosen), then whole disks (D-47).
            var devices = await Task.Run(UnixDisks.List);
            string? here = location.IsFileSystem ? location.Path : null;
            string? focusedMount = hasFocus && focused.Kind == EntryKind.Drive && focused.Tag is DriveTag d ? d.RootPath : null;
            int preferred = -1;
            foreach (var volume in devices.Where(IsRecoverableVolume))
            {
                string mount = volume.MountPoints[0];
                bool isHere = here is not null && volume.MountPoints.Any(m => Holds(m, here));
                bool isFocused = volume.MountPoints.Contains(focusedMount);
                if (isFocused || isHere && preferred < 0) preferred = choices.Count;
                var row = new EntryData(mount, EntryKind.Drive) { Tag = new DriveTag(mount, null, volume.Removable ? "Removable" : "Fixed", volume.FileSystem, -1, volume.Length, true) };
                string? folder = isHere ? here : null;
                choices.Add((new ChoiceItem(mount, $"{volume.FileSystem} · {Formatters.SizeWithUnit(volume.Length)}{(volume.Bus.Length > 0 ? " " + volume.Bus : "")} · {volume.Name}",
                    isHere ? "this folder's drive" : isFocused ? "under the cursor" : null) { Icon = () => icons.GetIcon(row) }, () => FindDeletedOnUnixDeviceAsync(panel, volume, folder)));
            }
            if (selected < 0) selected = preferred;
            foreach (var disk in devices.Where(v => v.Disk is null))
            {
                var row = new EntryData(disk.Name, EntryKind.Drive) { Tag = new DriveTag(disk.Device, disk.Model, disk.Removable ? "Removable" : "Fixed", null, -1, disk.Length, true) };
                var parts = devices.Where(v => v.Disk == disk.Device).ToList();
                string mounted = string.Join(", ", parts.SelectMany(v => v.MountPoints).Concat(disk.MountPoints));
                string detail = $"Whole disk, {Formatters.SizeWithUnit(disk.Length)}{(disk.Bus.Length > 0 ? " " + disk.Bus : "")} · " +
                                (mounted.Length > 0 ? "mounted at " + mounted : parts.Count == 0 ? "no partitions" : "nothing mounted") + " · also finds deleted partitions";
                choices.Add((new ChoiceItem($"Disk {disk.Name}" + (disk.Model is null ? "" : ": " + disk.Model), detail) { Icon = () => icons.GetIcon(row) },
                    () => FindDeletedOnUnixDeviceAsync(panel, disk, null)));
            }
        }
        choices.Add((new ChoiceItem("Disk image file…", "Choose a raw .img, .dd, or .bin file, or a fixed .vhd, on any drive"), () => ChooseImageAsync(panel, location)));
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Recover deleted files", choices.Select(c => c.Item).ToList())
        {
            SelectedIndex = Math.Max(0, selected),
            Icons = icons,
            Hint = "Where were the files deleted? · Enter scans it; nothing there is changed · Esc closes",
        });
        if (r.Index < 0 || r.Index >= choices.Count) return;
        await choices[r.Index].Run();
    }

    /// <summary>"Disk 4: Kingston XS1000".</summary>
    private static string DiskName(FileCat.Platform.Windows.Recovery.PhysicalDisk disk) =>
        $"Disk {disk.Number}" + (disk.Model is null ? "" : ": " + disk.Model);

    private static bool IsRemovable(FileCat.Platform.Windows.Recovery.PhysicalDisk disk) => disk.Removable || disk.Bus is "USB" or "SD card" or "MMC";

    private static string DiskDetail(FileCat.Platform.Windows.Recovery.PhysicalDisk disk)
    {
        string size = Formatters.SizeWithUnit(disk.Length) + (disk.Bus.Length > 0 ? " " + disk.Bus : "");
        string drives = disk.Drives.Count == 0 ? "no drives" : (disk.Drives.Count == 1 ? "drive " : "drives ") + string.Join(", ", disk.Drives);
        string system = HoldsWindows(disk) ? " · Windows runs from it" : "";
        return $"Whole disk, {size} · {drives}{system} · also finds deleted partitions and ones Windows cannot read";
    }

    private static bool HoldsWindows(FileCat.Platform.Windows.Recovery.PhysicalDisk disk) =>
        disk.Drives.Contains(Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase);

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
        if (f.Kind == EntryKind.Drive && f.Tag is DriveTag drive && Services.Recovery.OpenDevice is not null && IsRecoverableDrive(drive)) return "Recover deleted files from this drive…";
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
            if (f.Kind == EntryKind.Drive && f.Tag is DriveTag drive)
            {
                if (OperatingSystem.IsWindows()) await FindDeletedOnDriveAsync(panel, drive, null);
                else if (await Task.Run(UnixDisks.List) is var devices && devices.FirstOrDefault(v => IsRecoverableVolume(v) && v.MountPoints.Contains(drive.RootPath)) is { } volume)
                    await FindDeletedOnUnixDeviceAsync(panel, volume, null);
                else Notify($"{drive.RootPath} is not a local volume FileCat can read directly.", true);
            }
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
        int volumes, lost;
        try
        {
            (volumes, lost) = await Task.Run(() =>
            {
                using var source = new ImageFileSource(image);
                // The table's volumes, and the ones found where partitions usually start (deleted, or a lost table).
                var listed = PartitionTable.Find(source, []);
                int found = PartitionSearch.Quick(source, listed, CancellationToken.None).Count;
                return (listed.Count + found, found + listed.Count(s => s.Lost));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notify($"\"{Path.GetFileName(image)}\" cannot be read as a disk image: {ex.Message}", true);
            return;
        }
        panel.OpenTab(RecoveryProvider.ForImage(image, volumes == 1 ? 1 : null));
        Notify(volumes == 0
            ? $"\"{Path.GetFileName(image)}\" shows no partition table and no NTFS, FAT, or exFAT file system where partitions usually start. Recover deleted files in its tab searches all of it for deleted partitions."
            : lost > 0
                ? $"{(lost == 1 ? "A partition" : $"{lost} partitions")} that the partition table no longer lists {(lost == 1 ? "was" : "were")} found: {(volumes == 1 ? "its files are" : "the image's partitions are")} in a new tab. Mark what to recover and copy it (F5) to a folder on another drive."
                : "The deleted items are in a new tab: mark what to recover and copy it (F5) to a folder on another drive.", volumes == 0);
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
        if (Services.Recovery.OpenDevice is null)
            return "Scanning drives directly is not available here. Make an image of the drive on another drive (for example with dd), then recover deleted files from the image.";
        if (!OperatingSystem.IsWindows()) return null; // the system asks for approval when the drive is opened
        if (Environment.IsPrivilegedProcess || FileCat.Platform.Windows.Elevation.ElevationBroker.Locate(Services.Paths.IsPortable, out _) is not null) return null;
        return $"Reading a drive needs administrator rights, and {(Services.Paths.IsPortable ? "portable FileCat" : "this FileCat build")} has no installed helper to ask Windows for them. " +
               "Start FileCat as administrator (right-click it, then Run as administrator) to scan drives, or recover from a disk image of the drive.";
    }

    /// <summary>
    /// A drive: after a confirmation, its scan opens in a new tab (read by FileCat itself when it runs as administrator,
    /// otherwise through the helper, which Windows asks to approve). With <paramref name="folder"/>, the tab then goes to
    /// that folder, or as close to it as the scan goes.
    /// </summary>
    internal async Task FindDeletedOnDriveAsync(PanelViewModel panel, DriveTag drive, string? folder)
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
        var safety = await Task.Run(() => CheckDiskSafety(device, name));
        if (safety.Refusal is not null)
        {
            await RefuseScanAsync(safety);
            return;
        }
        string how = Environment.IsPrivilegedProcess
            ? "FileCat runs as administrator and reads the drive itself, only reading: FileCat changes nothing on it. "
            : "Windows asks for administrator approval, and FileCat's helper then only reads the drive: FileCat changes nothing on it. ";
        // A drive with a letter is mounted: Windows writes its own pending changes to it, sooner while it is read (release
        // plan V09's trace of the system drive: NTFS wrote its metadata out as FileCat read the volume).
        string text = $"Scan {name} for deleted files? " + how +
                      (system ? "Windows runs from this drive and keeps writing to it, so deleted files can be overwritten at any moment; for the best chance, image the drive from another computer. "
                       : "Windows keeps it mounted and may still write its own pending changes to it while FileCat reads; for the best chance, scan it while nothing else uses it, or image it. ") +
                      safety.HeldOff +
                      "The scan opens in a new tab. Recover files to another disk, and write nothing to this one meanwhile.";
        HoldOff(safety);
        if (!await Dialogs.ConfirmAsync("Recover deleted files", text, "Scan"))
        {
            ReleaseHoldOff();
            return;
        }
        _deviceScanned = true;
        var root = Services.Recovery.ForDevice(device, name, 1);
        var scan = panel.OpenTab(root);
        if (folder is not null && folder.Length > drive.RootPath.Length)
            GoToFolderWhenScanned(scan, root, folder[drive.RootPath.Length..].Replace('\\', '/').Trim('/'));
    }

    /// <summary>
    /// A whole disk: after a confirmation, its scan opens in a new tab with the disk's partitions, those found where no
    /// partition is listed (deleted, or a lost table), and those whose first sector is damaged, read from a backup.
    /// </summary>
    internal async Task FindDeletedOnDiskAsync(PanelViewModel panel, FileCat.Platform.Windows.Recovery.PhysicalDisk disk)
    {
        if (DriveScanProblem() is { } problem)
        {
            Notify(problem, true);
            return;
        }
        string name = $"disk {disk.Number}" + (disk.Model is null ? "" : $" ({disk.Model})");
        bool system = HoldsWindows(disk);
        var safety = await Task.Run(() => CheckDiskSafety(disk.Device, name));
        if (safety.Refusal is not null)
        {
            await RefuseScanAsync(safety);
            return;
        }
        string how = Environment.IsPrivilegedProcess
            ? "FileCat runs as administrator and reads the disk itself, only reading: FileCat changes nothing on it. "
            : "Windows asks for administrator approval, and FileCat's helper then only reads the disk: FileCat changes nothing on it. ";
        string text = $"Scan {name} for deleted files? The scan lists the disk's partitions, and looks where partitions usually start for ones that were deleted or whose table was lost. " + how +
                      (system ? "Windows runs from this disk and keeps writing to it, so deleted files can be overwritten at any moment; for the best chance, image the disk from another computer. "
                       : disk.Drives.Count > 0 ? $"Windows keeps its drives mounted ({string.Join(", ", disk.Drives)}) and may still write its own pending changes to them while FileCat reads; for the best chance, take them offline in Disk Management first (the disk stays in this list). " : "") +
                      safety.HeldOff +
                      "The scan opens in a new tab. Recover files to another disk, and write nothing to this one meanwhile.";
        HoldOff(safety);
        if (!await Dialogs.ConfirmAsync("Recover deleted files", text, "Scan"))
        {
            ReleaseHoldOff();
            return;
        }
        _deviceScanned = true;
        // The disk's size when it was chosen: a disk plugged in meanwhile under the same number is refused.
        panel.OpenTab(Services.Recovery.ForDevice(disk.Device, name, length: disk.Length));
        Notify("The disk's partitions are in a new tab, lost ones marked as such: open one, mark what to recover, and copy it (F5) to a folder on another disk.");
    }

    /// <summary>
    /// Whether a device's scan can leave its disk alone (release plan V09, I09), worked out before anything is shown:
    /// <see cref="Refusal"/> says why it cannot (with <see cref="Command"/>, the way to start FileCat that can); otherwise
    /// what FileCat holds off while the scan is open, since the Shell and gpg write into folders of the user's on that disk.
    /// </summary>
    internal sealed record DiskSafety(string? Refusal, string? Command, bool PauseShellPictures, bool PauseSignatures)
    {
        /// <summary>What the confirmation says FileCat holds off while the scan is open.</summary>
        public string HeldOff => (PauseShellPictures, PauseSignatures) switch
        {
            (true, true) => "While the scan is open, FileCat asks Windows for no file pictures and runs no GnuPG checks: both write into folders on this disk. ",
            (true, false) => "While the scan is open, FileCat asks Windows for no file pictures: Windows writes them into a cache on this disk. ",
            (false, true) => "While the scan is open, FileCat runs no GnuPG checks: gpg writes into its folder on this disk. ",
            _ => "",
        };
    }

    /// <summary>
    /// FileCat writes to its own folders while it works (settings, history, logs, journals, caches, scratch, the helper's
    /// exchange), and each write can land where deleted files still lie. A warning followed by such writes would not keep
    /// them safe, so a device whose disk holds any of those folders, or where that cannot be told, is not scanned at all.
    /// A FileCat started with its files elsewhere (--data) also waits for the usual one to be closed when that one's files
    /// are on the disk.
    /// </summary>
    internal DiskSafety CheckDiskSafety(string device, string name)
    {
        var shares = Services.Recovery.SharesDisk;
        bool? Shares(string folder) => shares?.Invoke(device, folder);
        (List<string> Known, List<string> Unknown) Check(IEnumerable<(string What, string Folder)> folders)
        {
            var known = new List<string>();
            var unknown = new List<string>();
            foreach (var group in folders.GroupBy(f => f.What))
            {
                var answers = group.Select(f => Shares(f.Folder)).ToList();
                string shown = $"{group.Key} ({group.First().Folder})";
                if (answers.Contains(true)) known.Add(shown);
                else if (answers.Contains(null)) unknown.Add(shown);
            }
            return (known, unknown);
        }
        var (own, unsure) = Check(Services.Paths.WriteFolders);
        if (own.Count > 0 || unsure.Count > 0)
        {
            string command = DataCommand(SuggestedDataFolder(device) ?? (OperatingSystem.IsWindows() ? @"X:\FileCat data" : "/media/USB/FileCat data"));
            string refusal = $"FileCat does not scan {name}. " +
                             (own.Count > 0 ? $"It keeps files of its own on that disk and writes to them while it works, and each write can land where deleted files still lie: {string.Join("; ", own)}. " : "") +
                             (unsure.Count > 0 ? $"It cannot tell whether these files of its own, which it writes to while it works, are on that disk: {string.Join("; ", unsure)}. " : "") +
                             "\n\nTo recover from that disk, close FileCat and start it with all of its files in a folder on another disk (a USB stick, say), then scan the disk there and recover to another disk:\n\n" +
                             command + "\n\nSafer still: make an image of the disk from another computer, and recover from the image.";
            return new DiskSafety(refusal, command, false, false);
        }
        string? profile = App.StartupOptions.Profile;
        if (Services.Paths.DataRoot is not null && SingleInstance.UsualInstanceRunning(profile))
        {
            var (usual, usualUnsure) = Check(AppPaths.Usual(profile).WriteFolders);
            if (usual.Count > 0 || usualUnsure.Count > 0)
                return new DiskSafety($"FileCat does not scan {name} yet: the FileCat that keeps its files in their usual places is still running, and it writes to them while it works: " +
                                      string.Join("; ", usual.Concat(usualUnsure)) + (usual.Count > 0 ? " are on that disk." : " may be on that disk.") +
                                      " Close it, then ask for the scan again here.", null, false, false);
        }
        bool shellCache = OperatingSystem.IsWindows() && Services.ShellPictures is not null &&
                          Shares(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer")) != false;
        bool gpg = Shares(Core.Verification.OpenPgp.Home()) != false;
        return new DiskSafety(null, null, shellCache, gpg);
    }

    private async Task RefuseScanAsync(DiskSafety safety)
    {
        if (safety.Command is null)
        {
            await Dialogs.AlertAsync("Recover deleted files", safety.Refusal!);
            return;
        }
        if (await Dialogs.ConfirmAsync("Recover deleted files", safety.Refusal!, "Copy the command", cancelText: "Close"))
        {
            CopyTextToClipboard(safety.Command);
            Notify("The command is on the clipboard: close FileCat, then run it (the folder it names must be on another disk).");
        }
    }

    /// <summary>Set once a device's scan was confirmed: what was held off for it stays held off until FileCat closes.</summary>
    private bool _deviceScanned;

    /// <summary>
    /// From the moment a device is chosen, before its question is shown: what writes into folders on its disk (the
    /// Shell's picture caches, GnuPG's folder) is held off, for good once the scan is confirmed.
    /// </summary>
    private void HoldOff(DiskSafety safety)
    {
        if (safety.PauseShellPictures && Services.ShellPictures is { } shell) shell.Paused = true;
        if (safety.PauseSignatures && Core.Verification.VerificationService.Current is { } verification) verification.SignatureToolsPaused = true;
    }

    /// <summary>The question was declined: what was held off for it goes on, unless a device was scanned earlier.</summary>
    private void ReleaseHoldOff()
    {
        if (_deviceScanned) return;
        if (Services.ShellPictures is { } shell) shell.Paused = false;
        if (Core.Verification.VerificationService.Current is { } verification) verification.SignatureToolsPaused = false;
    }

    /// <summary>The command that starts this FileCat with everything it writes in <paramref name="folder"/>.</summary>
    private static string DataCommand(string folder)
    {
        // An AppImage runs from a folder that is gone once it ends: the image itself is what to start again.
        string program = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image ? image : Environment.ProcessPath ?? "FileCat";
        static string Quote(string text) => OperatingSystem.IsWindows() ? $"\"{text}\"" : "'" + text.Replace("'", "'\\''") + "'";
        return $"{Quote(program)} --data {Quote(folder)}";
    }

    /// <summary>A folder on a local drive known to lie on another disk than <paramref name="device"/>, with room; null when none is.</summary>
    private string? SuggestedDataFolder(string device)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string root = drive.RootDirectory.FullName;
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) continue;
                if (!OperatingSystem.IsWindows() && !(root.StartsWith("/media/", StringComparison.Ordinal) || root.StartsWith("/run/media/", StringComparison.Ordinal) ||
                                                      root.StartsWith("/mnt/", StringComparison.Ordinal) || root.StartsWith("/Volumes/", StringComparison.Ordinal))) continue;
                if (drive.AvailableFreeSpace >= 1L << 30 && Services.Recovery.SharesDisk?.Invoke(device, root) == false) return Path.Combine(root, "FileCat data");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>A mounted partition with a file system a scan reads.</summary>
    private static bool IsRecoverableVolume(UnixBlockDevice device) =>
        device.Disk is not null && device.MountPoints.Count > 0 && UnixDisks.RecoverableTypes.Contains(device.FileSystem);

    /// <summary>Whether <paramref name="path"/> lies at or below the mount point <paramref name="mount"/>.</summary>
    private static bool Holds(string mount, string path) => path == mount || mount == "/" || path.StartsWith(mount.TrimEnd('/') + "/", StringComparison.Ordinal);

    /// <summary>
    /// A partition or a whole disk on Linux or macOS (D-47): after a confirmation, its scan opens in a new tab (a partition
    /// at its files; with <paramref name="folder"/>, at that folder or as close as the scan goes). The drive is read
    /// directly when this user may, otherwise the system asks for approval first.
    /// </summary>
    internal async Task FindDeletedOnUnixDeviceAsync(PanelViewModel panel, UnixBlockDevice device, string? folder)
    {
        bool disk = device.Disk is null;
        string name = disk ? $"disk {device.Name}" + (device.Model is null ? "" : $" ({device.Model})") : $"{device.MountPoints.FirstOrDefault() ?? device.Name} ({device.Name})";
        bool direct = await Task.Run(() => CanRead(device.Device));
        string how = direct ? "FileCat can read it with your own rights, and only reads: FileCat changes nothing on it. "
            : OperatingSystem.IsMacOS() ? "macOS asks for an administrator's password, and FileCat then only reads it: FileCat changes nothing on it. "
            : "Your system asks for an administrator's password, and FileCat then only reads it: FileCat changes nothing on it. ";
        var mounts = disk ? (await Task.Run(UnixDisks.List)).Where(v => v.Disk == device.Device).SelectMany(v => v.MountPoints).ToList() : device.MountPoints.ToList();
        bool system = mounts.Contains("/") || mounts.Contains("/System/Volumes/Data");
        var safety = await Task.Run(() => CheckDiskSafety(device.Device, name));
        if (safety.Refusal is not null)
        {
            await RefuseScanAsync(safety);
            return;
        }
        string text = $"Scan {name} for deleted files? " + (disk ? "The scan lists the disk's partitions, and looks where partitions usually start for ones that were deleted or whose table was lost. " : "") + how +
                      (system ? "The system runs from this disk and keeps writing to it, so deleted files can be overwritten at any moment; for the best chance, image the disk from another computer. "
                       : mounts.Count > 0 ? $"It is mounted ({string.Join(", ", mounts)}), so programs can write to it while FileCat reads: unmounting it first keeps it unchanged (its disk stays in this list). " : "") +
                      safety.HeldOff +
                      "The scan opens in a new tab. Recover files to another disk, and write nothing to this one meanwhile.";
        HoldOff(safety);
        if (!await Dialogs.ConfirmAsync("Recover deleted files", text, "Scan"))
        {
            ReleaseHoldOff();
            return;
        }
        _deviceScanned = true;
        // Its size when it was chosen: a disk plugged in meanwhile under the same name is refused.
        var root = Services.Recovery.ForDevice(device.Device, name, disk ? null : 1, device.Length);
        var scan = panel.OpenTab(root);
        if (!disk && folder is not null && device.MountPoints.FirstOrDefault(m => Holds(m, folder)) is { } mount && folder.Length > mount.TrimEnd('/').Length)
            GoToFolderWhenScanned(scan, root, folder[mount.TrimEnd('/').Length..].Trim('/'));
    }

    /// <summary>Whether this user may open the device for reading without asking (root, Linux's disk group, an attached image).</summary>
    private static bool CanRead(string device)
    {
        try
        {
            using var handle = File.OpenHandle(device, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Search a disk for deleted partitions, inside its scan: the scan looked where partitions usually start; this reads
    /// every megabyte (and every cylinder boundary) of the space no partition holds, with its progress in the tab.
    /// </summary>
    private async Task SearchDiskAsync(TabViewModel tab, Location location)
    {
        var search = Services.Recovery.DescribeDiskSearch(location);
        if (search is null or { Searched: true })
        {
            Notify(search is null ? "This disk has no space outside its partitions to search." : "This disk was searched for deleted partitions already.");
            return;
        }
        string text = $"The scan looked where partitions usually start. Searching the disk also reads every megabyte of the {Formatters.SizeWithUnit(search.Unpartitioned)} " +
                      "that lie in no partition, for partitions that were deleted or whose table was lost, and for ones whose first sector is damaged. " +
                      "That takes minutes on a USB stick and can take hours on a large hard disk; nothing is written, and Esc stops it (what was found before stays).";
        if (!await Dialogs.ConfirmAsync("Search for deleted partitions", text, "Search")) return;
        Services.Recovery.SearchDisk(location);
        // What the search finds appears among the disk's partitions: it runs where they are listed.
        var partitions = new Location(Schemes.Recovery, string.Empty, location.Container);
        if (Equals(tab.Location, partitions)) tab.Refresh();
        else tab.Navigate(partitions);
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
