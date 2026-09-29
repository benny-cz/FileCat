using System.Collections.Concurrent;
using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Recovery;

/// <summary>
/// What a recovery row shows in the Kind and Details columns: the state, and why. A deleted folder has no content of its
/// own to judge: it says so, and its items carry their own states.
/// </summary>
public sealed record RecoveryEntryTag(RecoveryState? State, string Reasons, int Ordinal, bool Uncertain, bool DeletedFolder = false) : IDisplayDetails
{
    public string KindText => DeletedFolder ? "Deleted folder" : State switch
    {
        RecoveryState.Recoverable => "Recoverable",
        RecoveryState.Uncertain => "Uncertain",
        RecoveryState.Partial => "Partly lost",
        RecoveryState.Overwritten => "Overwritten",
        RecoveryState.NameOnly => "Name only",
        _ => string.Empty,
    };

    public string DetailsText => Reasons;
}

/// <summary>A volume row in a source's list of volumes.</summary>
public sealed record RecoveryVolumeTag(string FileSystem, string Details) : IDisplayDetails
{
    public string KindText => FileSystem;
    public string DetailsText => Details;
}

/// <summary>
/// The deleted items of a disk image or a drive as a read-only location (plan §17): the source's volumes, then each
/// volume's folders with the deleted files and folders they held. Nothing is ever written to the source: F3 previews
/// and F5 recovers into another folder, which for a drive must be on another physical disk. Locations: the source is the
/// container (a file-system location for an image, a <see cref="Schemes.Device"/> location for a drive),
/// <see cref="Location.Session"/> the volume number (none for the list of volumes), and <see cref="Location.Path"/> the
/// folder inside the volume ('/'-separated; items that share a name carry "\0" and their ordinal).
/// </summary>
public sealed class RecoveryProvider : ResourceProvider
{
    private const int MaxSessions = 4;
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _deviceNames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Devices that are one volume (a drive, a partition) rather than a whole disk.</summary>
    private readonly ConcurrentDictionary<string, bool> _volumeDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanLock = new();

    private sealed class Session(IBlockSource source, IReadOnlyList<RecoveryVolume> volumes) : IDisposable
    {
        public IBlockSource Source { get; } = source;
        public IReadOnlyList<RecoveryVolume> Volumes { get; set; } = volumes;
        public DateTime Used { get; set; } = DateTime.UtcNow;

        /// <summary>Scan again on next use, keeping the source (a drive keeps its helper session).</summary>
        public bool Stale { get; set; }

        /// <summary>That scan also searches free space (the user asked; minutes on large drives).</summary>
        public bool SearchFreeSpace { get; set; }

        /// <summary>That scan also searches all space no partition holds for deleted partitions (the user asked).</summary>
        public bool SearchDisk { get; set; }

        /// <summary>The volumes shown come from a scan that searched the whole disk for deleted partitions.</summary>
        public bool DiskSearched { get; set; }

        public IBlockSource Window(RecoveryVolume v) => new WindowSource(Source, v.Offset, v.Length, $"{Source.Description}, {v.Title}");

        public void Dispose() => Source.Dispose();
    }

    public override string Scheme => Schemes.Recovery;

    /// <summary>
    /// Opens a drive for reading (the Windows adapter: through the administrator helper, after the user approves); null
    /// where drives cannot be read directly. Arguments: the device, what the user knows it as, cancellation.
    /// </summary>
    public Func<string, string, CancellationToken, IBlockSource>? OpenDevice { get; set; }

    /// <summary>Whether a folder lies on a physical disk the device lies on: true, false, or null when unknown.</summary>
    public Func<string, string, bool?>? SharesDisk { get; set; }

    /// <summary>The deleted items of a disk image: its only volume, or the list of its volumes.</summary>
    public static Location ForImage(string imagePath, int? volume = null) =>
        new(Schemes.Recovery, string.Empty, Location.FileSystem(imagePath), volume?.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The deleted items of a drive or a disk (\\?\Volume{…} or \\.\PhysicalDriveN; /dev/sdb1 or /dev/sdb; /dev/rdisk4s1);
    /// <paramref name="name"/> is what the user calls it. With <paramref name="volume"/>, the device is one volume (a drive,
    /// a partition) and opens at that volume; without, it is a whole disk and opens at its list of volumes.
    /// </summary>
    public Location ForDevice(string device, string name, int? volume = null)
    {
        _deviceNames[device] = name;
        if (volume is not null) _volumeDevices[device] = true;
        return new(Schemes.Recovery, string.Empty, new Location(Schemes.Device, device), volume?.ToString(CultureInfo.InvariantCulture));
    }

    public static bool IsDevice(Location location) => location.Container?.Scheme == Schemes.Device;

    private static string SourcePath(Location location) =>
        location.Container is { } c && (c.IsFileSystem || c.Scheme == Schemes.Device) ? c.Path : throw new InvalidOperationException("A recovery location without its source.");

    private string SourceName(Location location) =>
        IsDevice(location) ? _deviceNames.GetValueOrDefault(SourcePath(location)) ?? SourcePath(location) : SourcePath(location);

    // ---- Navigation ---------------------------------------------------------------------------------------

    public override string GetDisplayPath(Location location)
    {
        var parts = new List<string> { SourceName(location) + " › deleted items" };
        if (location.Session is { } volume && !IsOnlyVolume(location)) parts.Add("volume " + volume);
        if (location.Path.Length > 0) parts.Add(string.Join(Path.DirectorySeparatorChar, location.Path.Split('/').Select(Display)));
        return string.Join(" › ", parts);
    }

    public override string GetDisplayName(Location location)
    {
        if (location.Path.Length > 0) return Display(location.Path[(location.Path.LastIndexOf('/') + 1)..]);
        if (location.Session is { } volume && !IsOnlyVolume(location)) return "Volume " + volume;
        return IsDevice(location) ? SourceName(location) : Path.GetFileName(SourcePath(location));
    }

    public override string? GetNameInParent(Location location)
    {
        if (location.Path.Length > 0) return location.Path[(location.Path.LastIndexOf('/') + 1)..].Split('\0')[0];
        if (location.Session is { } volume && !IsOnlyVolume(location)) return VolumeName(int.Parse(volume, CultureInfo.InvariantCulture));
        return IsDevice(location) ? null : Path.GetFileName(SourcePath(location));
    }

    public override Location? GetParent(Location location)
    {
        if (location.Path.Length > 0)
        {
            int slash = location.Path.LastIndexOf('/');
            return location.WithPath(slash < 0 ? string.Empty : location.Path[..slash]);
        }
        if (location.Session is not null && !IsOnlyVolume(location)) return new Location(Schemes.Recovery, string.Empty, location.Container);
        if (IsDevice(location)) return new Location(Schemes.Computer, string.Empty);
        return Location.FileSystem(Path.GetDirectoryName(SourcePath(location)) ?? SourcePath(location));
    }

    public override string GetDeviceKey(Location location) => IsDevice(location) ? SourcePath(location) : PathUtil.GetDeviceKey(SourcePath(location));

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.MoveSource => "Recovery never changes the source: copy (recover) items with F5 instead.",
        LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.Delete or LocationCapabilities.Rename or
            LocationCapabilities.TransferTarget or LocationCapabilities.ExternalEdit or LocationCapabilities.Recycle =>
            "Recovery only reads: nothing in the source is changed or cleaned up. Copy (recover) items with F5 to a folder.",
        _ => base.ExplainUnavailable(location, capability),
    };

    /// <summary>A drive's deleted files are recovered only to another physical disk: writing to the source can overwrite them.</summary>
    public override string? CheckTransferDestination(Location source, string destinationDirectory)
    {
        if (source.Scheme != Schemes.Recovery || !IsDevice(source)) return null;
        return SharesDisk?.Invoke(SourcePath(source), destinationDirectory) switch
        {
            false => null,
            true => $"Nothing was copied: {destinationDirectory} is on the same physical disk as {SourceName(source)}, and writing there can overwrite the very files being recovered. Choose a folder on another disk.",
            _ => $"Nothing was copied: FileCat cannot tell whether {destinationDirectory} is on the same physical disk as {SourceName(source)}, where writing could overwrite the files being recovered. Choose a folder on a disk you know is separate (another drive, a USB stick, or a network share).",
        };
    }

    /// <summary>A volume device holds one volume; an image's count is known once it was scanned.</summary>
    private bool IsOnlyVolume(Location location) =>
        IsVolumeDevice(location) || _sessions.TryGetValue(Key(location), out var s) && s.Volumes.Count == 1;

    /// <summary>A device that is one volume (a drive, a partition), not a whole disk.</summary>
    private bool IsVolumeDevice(Location location) =>
        IsDevice(location) && (SourcePath(location).StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) || _volumeDevices.ContainsKey(SourcePath(location)));

    private static string VolumeName(int number) => "Volume " + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A path segment as shown: the name without the ordinal that tells same-named items apart.</summary>
    private static string Display(string segment) => segment.Split('\0')[0];

    // ---- Listing --------------------------------------------------------------------------------------------

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var session = GetSession(location, ct, sink);
        if (location.Session is null)
        {
            var rows = new List<EntryData>();
            for (int i = 0; i < session.Volumes.Count; i++)
            {
                var v = session.Volumes[i];
                int count = v.WholeFileSystem ? Files(v.Root) : Count(v.Root);
                string what = v.WholeFileSystem ? $"{count} file{(count == 1 ? "" : "s")}" : $"{count} deleted item{(count == 1 ? "" : "s")}";
                string? origin = v.Origin switch
                {
                    VolumeOrigin.Search => $"Lost partition at {PartitionSearch.Place(v.Offset)}",
                    VolumeOrigin.BackupTable => "Listed only in the backup partition table",
                    _ => v.DamagedStart ? "First sector damaged" : null,
                };
                string details = v.FileSystem == "Unknown" ? string.Join(" ", v.Warnings)
                    : string.Join(" · ", new[] { origin, string.IsNullOrWhiteSpace(v.Label) ? null : v.Label!.Trim(), RecoveryItem.Bytes(v.Length), what }.Where(s => s is not null));
                rows.Add(new EntryData(VolumeName(i + 1), EntryKind.Directory)
                {
                    Tag = new RecoveryVolumeTag(v.FileSystem, details),
                    Flags = v.FileSystem == "Unknown" ? EntryFlags.Unavailable : EntryFlags.None,
                });
            }
            sink.AddBatch(rows.ToArray());
            if (DescribeDiskSearch(session, location) is { } search)
                sink.ReportIssue(search.Searched
                    ? $"The search for deleted partitions read all {RecoveryItem.Bytes(search.Unpartitioned)} of this disk that is in no partition."
                    : $"{RecoveryItem.Bytes(search.Unpartitioned)} of this disk is in no partition. Recover deleted files (Tools menu) here offers to search it for deleted partitions.");
            return Task.CompletedTask;
        }
        var (volume, folder) = Resolve(session, location);
        if (location.Path.Length == 0 && Explain(volume) is { } explanation) sink.ReportIssue(explanation);
        foreach (var warning in volume.Warnings) sink.ReportIssue(warning);
        // An empty volume says why rather than looking like a failed listing (its own warnings, if any, explain more).
        if (location.Path.Length == 0 && folder.Children.Count == 0 && volume.Warnings.Count == 0)
            sink.ReportIssue(volume.WholeFileSystem
                ? $"No files were found on this {volume.FileSystem} volume."
                : $"No deleted items were found on this {volume.FileSystem} volume. Deleted files leave traces only until their entries or space are used again.");
        if (location.Path.Length == 0 && volume.OpenListings > 0 && !volume.FreeSpaceSearched)
            sink.ReportIssue($"The lists of contents of {volume.OpenListings} deleted folder{(volume.OpenListings == 1 ? "" : "s")} may go on where FAT no longer points. " +
                             $"Recover deleted files (Tools menu) here offers to search the {RecoveryItem.Bytes(volume.FreeBytes ?? 0)} of free space for the rest.");
        var batch = new List<EntryData>(folder.Children.Count);
        foreach (var item in folder.Children)
        {
            ct.ThrowIfCancellationRequested();
            bool lost = !item.IsDirectory && item.State is RecoveryState.Overwritten or RecoveryState.NameOnly;
            batch.Add(new EntryData(item.Name, item.IsDirectory ? EntryKind.Directory : EntryKind.File, item.IsDirectory ? -1 : item.Size, item.ModifiedUtc?.Ticks ?? 0)
            {
                Created = item.CreatedUtc?.Ticks ?? 0,
                Tag = new RecoveryEntryTag(item.IsDeleted || volume.WholeFileSystem && !item.IsDirectory ? item.State : null, string.Join(" ", item.Evidence), item.Ordinal,
                    item.NameUncertain, item.IsDirectory && item.IsDeleted),
                Flags = lost ? EntryFlags.Unavailable : EntryFlags.None,
            });
        }
        sink.AddBatch(batch.ToArray());
        return Task.CompletedTask;
    }

    private static int Count(RecoveryItem folder) => folder.Children.Sum(c => (c.IsDeleted ? 1 : 0) + (c.IsDirectory ? Count(c) : 0));

    private static int Files(RecoveryItem folder) => folder.Children.Sum(c => c.IsDirectory ? Files(c) : 1);

    /// <summary>Why a volume lists all of its files rather than its deleted ones, and how FileCat came to read it.</summary>
    private static string? Explain(RecoveryVolume volume) => volume.Origin switch
    {
        VolumeOrigin.Search => $"This partition is in no partition table: it was deleted, or the table was lost. FileCat found it because {volume.Found}. " +
                               "All of its files are listed, not only deleted ones; each can be recovered until something is written over that part of the disk.",
        VolumeOrigin.BackupTable => "Only the copy of the GPT partition table at the end of the disk lists this partition: the table at its start is damaged or was erased. " +
                                    "All of its files are listed, not only deleted ones.",
        _ when volume.DamagedStart => $"This volume's first sector is damaged, so the operating system may not read it (Windows calls such a volume RAW). FileCat read it because {volume.Found}. " +
                                      "All of its files are listed, not only deleted ones.",
        _ => null,
    };

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return GetParent(parent);
        if (entry.Kind != EntryKind.Directory) return null;
        if (parent.Session is null)
            return entry.Name.StartsWith("Volume ", StringComparison.Ordinal) && int.TryParse(entry.Name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? new Location(Schemes.Recovery, string.Empty, parent.Container, n.ToString(CultureInfo.InvariantCulture))
                : null;
        string segment = entry.Tag is RecoveryEntryTag { Ordinal: > 0 } t ? entry.Name + "\0" + t.Ordinal.ToString(CultureInfo.InvariantCulture) : entry.Name;
        return parent.WithPath(parent.Path.Length == 0 ? segment : parent.Path + "/" + segment);
    }

    public override ItemRef GetItemRef(Location listing, in EntryData entry) =>
        entry.Tag is RecoveryEntryTag { Ordinal: > 0 } t
            ? new ItemRef(listing, entry.Name, entry.Kind, entry.Size, entry.Modified) { Ordinal = t.Ordinal, Flags = entry.Flags }
            : base.GetItemRef(listing, entry);

    // ---- Content ----------------------------------------------------------------------------------------------

    public override IContentSource? OpenContent(ItemRef item)
    {
        var session = GetSession(item.Parent, CancellationToken.None);
        var (volume, folder) = Resolve(session, item.Parent);
        var found = folder.Child(item.Name, item.Ordinal) is { IsDirectory: false } file ? file
                    : throw new FileNotFoundException($"\"{item.Name}\" is not among the deleted items any more.");
        if (found.State == RecoveryState.Overwritten)
            throw new InvalidDataException($"\"{found.Name}\" is overwritten: the space it used holds other data now, so nothing of it can be recovered.");
        if (found.State == RecoveryState.NameOnly)
            throw new InvalidDataException($"Only the name of \"{found.Name}\" survives: {string.Join(" ", found.Evidence)}");
        return new RecoveryContent(session.Window(volume), found);
    }

    // ---- Sessions ---------------------------------------------------------------------------------------------

    private static (RecoveryVolume Volume, RecoveryItem Folder) Resolve(Session session, Location location)
    {
        if (!int.TryParse(location.Session, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > session.Volumes.Count)
            throw new DirectoryNotFoundException("This volume is not on the source any more.");
        var volume = session.Volumes[number - 1];
        var folder = volume.Root;
        if (location.Path.Length > 0)
        {
            foreach (var segment in location.Path.Split('/'))
            {
                var parts = segment.Split('\0');
                int ordinal = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int o) ? o : 0;
                folder = folder.Child(parts[0], ordinal) is { IsDirectory: true } child ? child
                         : throw new DirectoryNotFoundException($"\"{parts[0]}\" is not among the deleted items any more.");
            }
        }
        return (volume, folder);
    }

    /// <summary>An image is scanned again when it changes; a drive is identified by its device alone.</summary>
    private static string Key(Location location)
    {
        string path = SourcePath(location);
        if (IsDevice(location)) return "device|" + path;
        var info = new FileInfo(path);
        return $"{info.FullName}|{(info.Exists ? info.Length : -1)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}";
    }

    /// <summary>What searching a volume's free space involves, for the user to decide (<see cref="SearchFreeSpace"/>).</summary>
    public sealed record FreeSpaceSearch(string Volume, long FreeBytes, int OpenListings, bool Searched);

    /// <summary>
    /// The volume shown at <paramref name="location"/>, if its free space can be searched (FAT, already scanned); null
    /// otherwise: other file systems keep deleted folders' contents where the scan finds them.
    /// </summary>
    public FreeSpaceSearch? DescribeFreeSpaceSearch(Location location)
    {
        if (location.Scheme != Schemes.Recovery || location.Session is null || !_sessions.TryGetValue(Key(location), out var session)) return null;
        if (!int.TryParse(location.Session, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > session.Volumes.Count) return null;
        var volume = session.Volumes[number - 1];
        return volume.FreeBytes is { } free ? new FreeSpaceSearch(volume.Title, free, volume.OpenListings, volume.FreeSpaceSearched) : null;
    }

    /// <summary>
    /// The folder of a scanned volume closest to <paramref name="path"/> ('/'-separated, inside the volume): the path
    /// itself when the scan shows it, else its deepest ancestor that it shows (a scan leaves out folders with nothing
    /// deleted below them). Letter case is ignored, as NTFS, FAT, and exFAT ignore it; a live folder is preferred to a
    /// deleted one of the same name. Null before the volume was scanned.
    /// </summary>
    public Location? ClosestFolder(Location volume, string path)
    {
        if (volume.Scheme != Schemes.Recovery || !_sessions.TryGetValue(Key(volume), out var session) || session.Stale) return null;
        if (!int.TryParse(volume.Session, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > session.Volumes.Count) return null;
        var folder = session.Volumes[number - 1].Root;
        var segments = new List<string>();
        foreach (var name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = folder.Children.Where(c => c.IsDirectory && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.IsDeleted).ThenBy(c => c.Name == name ? 0 : 1).FirstOrDefault();
            if (next is null) break;
            segments.Add(next.Ordinal > 0 ? next.Name + "\0" + next.Ordinal.ToString(CultureInfo.InvariantCulture) : next.Name);
            folder = next;
        }
        return volume.WithPath(string.Join('/', segments));
    }

    /// <summary>What searching a disk for deleted partitions involves: the space no partition holds, and whether it was searched.</summary>
    public sealed record DiskSearch(long Unpartitioned, bool Searched);

    /// <summary>Space in no partition smaller than this is left alone by the hint (partitions are aligned with room before them).</summary>
    private const long NoticeableGap = 4L * 1024 * 1024;

    /// <summary>
    /// The disk shown at <paramref name="location"/>, if it can be searched for deleted partitions: a disk image or a whole
    /// disk (not a volume) with noticeable space in no partition, already scanned. Null otherwise.
    /// </summary>
    public DiskSearch? DescribeDiskSearch(Location location) =>
        location.Scheme == Schemes.Recovery && _sessions.TryGetValue(Key(location), out var session) ? DescribeDiskSearch(session, location) : null;

    private DiskSearch? DescribeDiskSearch(Session session, Location location)
    {
        if (IsVolumeDevice(location)) return null;
        long free = PartitionSearch.Gaps(session.Source.Length, session.Volumes.Select(v => (v.Offset, v.Length)))
            .Where(g => g.Length >= NoticeableGap).Sum(g => g.Length);
        return free > 0 || session.DiskSearched ? new DiskSearch(free, session.DiskSearched) : null;
    }

    /// <summary>The next listing of the location's source scans it again and searches all space in no partition for deleted partitions.</summary>
    public void SearchDisk(Location location)
    {
        if (location.Scheme == Schemes.Recovery && _sessions.TryGetValue(Key(location), out var session))
        {
            session.SearchDisk = true;
            session.Stale = true;
        }
    }

    /// <summary>The next listing of the location's source scans it again and searches its FAT volumes' free space too.</summary>
    public void SearchFreeSpace(Location location)
    {
        if (location.Scheme == Schemes.Recovery && _sessions.TryGetValue(Key(location), out var session))
        {
            session.SearchFreeSpace = true;
            session.Stale = true;
        }
    }

    /// <summary>The scan of the location's source: made once (it only reads), kept while the source is unchanged.</summary>
    private Session GetSession(Location location, CancellationToken ct, IEnumerationSink? sink = null)
    {
        string path = SourcePath(location);
        bool device = IsDevice(location);
        if (!device && !File.Exists(path)) throw new FileNotFoundException("The disk image no longer exists.", path);
        string key = Key(location);
        if (_sessions.TryGetValue(key, out var cached) && !cached.Stale)
        {
            cached.Used = DateTime.UtcNow;
            return cached;
        }
        lock (_scanLock)
        {
            if (_sessions.TryGetValue(key, out cached))
            {
                if (cached.Stale)
                {
                    bool searchFreeSpace = cached.SearchFreeSpace, searchDisk = cached.SearchDisk;
                    cached.SearchFreeSpace = cached.SearchDisk = false;
                    try
                    {
                        cached.Volumes = Scan(cached.Source, device, ct, searchFreeSpace, sink, searchDisk);
                        cached.DiskSearched = searchDisk;
                        cached.Stale = false;
                    }
                    catch (OperationCanceledException) when (searchFreeSpace || searchDisk)
                    {
                        cached.Stale = false; // stopped: what the quick scan found stays
                        throw;
                    }
                }
                cached.Used = DateTime.UtcNow;
                return cached;
            }
            // An image that changed since its scan is scanned again; the old session goes.
            if (!device)
                foreach (var old in _sessions.Where(s => s.Key.StartsWith(Path.GetFullPath(path) + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                    if (_sessions.TryRemove(old.Key, out var stale)) stale.Dispose();
            IBlockSource source = device
                ? (OpenDevice ?? throw new NotSupportedException("Reading drives directly is not available here: make an image of the drive on another drive, then open the image."))
                    .Invoke(path, SourceName(location), ct)
                : new ImageFileSource(path);
            try
            {
                var session = new Session(source, Scan(source, device, ct));
                _sessions[key] = session;
                while (_sessions.Count > MaxSessions)
                {
                    var oldest = _sessions.OrderBy(s => s.Value.Used).First();
                    if (_sessions.TryRemove(oldest.Key, out var evicted)) evicted.Dispose();
                }
                return session;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }
    }

    private static IReadOnlyList<RecoveryVolume> Scan(IBlockSource source, bool device, CancellationToken ct, bool searchFreeSpace = false, IEnumerationSink? sink = null,
        bool searchDisk = false)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var volumes = RecoveryScanner.Scan(source, ct, new RecoveryScanOptions
        {
            SearchFreeSpace = searchFreeSpace,
            Progress = sink is null ? null : (done, total) => sink.ReportProgress(SearchProgress(done, total, clock.Elapsed)),
            SearchDisk = searchDisk,
            DiskProgress = sink is null ? null : (done, total) => sink.ReportProgress(SearchProgress(done, total, clock.Elapsed, "Searching for deleted partitions")),
        });
        if (device)
            foreach (var v in volumes)
                v.Warnings.Insert(0, "This drive is in use while FileCat reads it: Windows and programs can change it at any moment, so this is a snapshot, and deleted files can be overwritten while you work. Recover to another disk, and write nothing to this one meanwhile.");
        return volumes;
    }

    /// <summary>"Searching free space: 40% (3 of 7.4 GiB), about 3 minutes left".</summary>
    internal static string SearchProgress(long done, long total, TimeSpan elapsed, string what = "Searching free space")
    {
        string text = $"{what}: {(total > 0 ? done * 100 / total : 100)}% ({RecoveryItem.Bytes(done)} of {RecoveryItem.Bytes(total)})";
        if (done <= 0 || done >= total || elapsed < TimeSpan.FromSeconds(3)) return text;
        double left = elapsed.TotalSeconds * (total - done) / done;
        return text + (left < 90 ? $", about {Math.Max(1, (int)Math.Round(left / 10) * 10)} seconds left" : $", about {(int)Math.Round(left / 60)} minutes left");
    }

    /// <summary>Reread: scans the source again (a drive keeps its approved helper session; an image is simply read again).</summary>
    public void Forget(Location location)
    {
        if (location.Scheme != Schemes.Recovery) return;
        if (IsDevice(location))
        {
            if (_sessions.TryGetValue(Key(location), out var session)) session.Stale = true;
            return;
        }
        string image = Path.GetFullPath(SourcePath(location));
        foreach (var s in _sessions.Where(s => s.Key.StartsWith(image + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            if (_sessions.TryRemove(s.Key, out var old)) old.Dispose();
    }

    /// <summary>Ends every session: drives' helper sessions close, image files are released.</summary>
    public void CloseAll()
    {
        foreach (var key in _sessions.Keys.ToList())
            if (_sessions.TryRemove(key, out var session)) session.Dispose();
    }
}
