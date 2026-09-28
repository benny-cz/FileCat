using System.Collections.Concurrent;
using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Recovery;

/// <summary>What a recovery row shows in the Kind and Details columns: the state, and why.</summary>
public sealed record RecoveryEntryTag(RecoveryState? State, string Reasons, int Ordinal, bool Uncertain) : IDisplayDetails
{
    public string KindText => State switch
    {
        RecoveryState.Recoverable => "Recoverable",
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
    private readonly object _scanLock = new();

    private sealed class Session(IBlockSource source, IReadOnlyList<RecoveryVolume> volumes) : IDisposable
    {
        public IBlockSource Source { get; } = source;
        public IReadOnlyList<RecoveryVolume> Volumes { get; set; } = volumes;
        public DateTime Used { get; set; } = DateTime.UtcNow;

        /// <summary>Scan again on next use, keeping the source (a drive keeps its helper session).</summary>
        public bool Stale { get; set; }

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

    /// <summary>The deleted items of a drive (a volume device such as \\?\Volume{…}); <paramref name="name"/> is what the user calls it.</summary>
    public Location ForDevice(string device, string name, int? volume = null)
    {
        _deviceNames[device] = name;
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
        IsDevice(location) && SourcePath(location).StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ||
        _sessions.TryGetValue(Key(location), out var s) && s.Volumes.Count == 1;

    private static string VolumeName(int number) => "Volume " + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A path segment as shown: the name without the ordinal that tells same-named items apart.</summary>
    private static string Display(string segment) => segment.Split('\0')[0];

    // ---- Listing --------------------------------------------------------------------------------------------

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var session = GetSession(location, ct);
        if (location.Session is null)
        {
            var rows = new List<EntryData>();
            for (int i = 0; i < session.Volumes.Count; i++)
            {
                var v = session.Volumes[i];
                int deleted = Count(v.Root);
                string details = v.FileSystem == "Unknown" ? string.Join(" ", v.Warnings)
                    : $"{(string.IsNullOrWhiteSpace(v.Label) ? "" : v.Label!.Trim() + " · ")}{RecoveryItem.Bytes(v.Length)} · {deleted} deleted item{(deleted == 1 ? "" : "s")}";
                rows.Add(new EntryData(VolumeName(i + 1), EntryKind.Directory)
                {
                    Tag = new RecoveryVolumeTag(v.FileSystem, details),
                    Flags = v.FileSystem == "Unknown" ? EntryFlags.Unavailable : EntryFlags.None,
                });
            }
            sink.AddBatch(rows.ToArray());
            return Task.CompletedTask;
        }
        var (volume, folder) = Resolve(session, location);
        foreach (var warning in volume.Warnings) sink.ReportIssue(warning);
        var batch = new List<EntryData>(folder.Children.Count);
        foreach (var item in folder.Children)
        {
            ct.ThrowIfCancellationRequested();
            bool lost = !item.IsDirectory && item.State is RecoveryState.Overwritten or RecoveryState.NameOnly;
            batch.Add(new EntryData(item.Name, item.IsDirectory ? EntryKind.Directory : EntryKind.File, item.IsDirectory ? -1 : item.Size, item.ModifiedUtc?.Ticks ?? 0)
            {
                Created = item.CreatedUtc?.Ticks ?? 0,
                Tag = new RecoveryEntryTag(item.IsDeleted ? item.State : null, string.Join(" ", item.Evidence), item.Ordinal, item.NameUncertain),
                Flags = lost ? EntryFlags.Unavailable : EntryFlags.None,
            });
        }
        sink.AddBatch(batch.ToArray());
        return Task.CompletedTask;
    }

    private static int Count(RecoveryItem folder) => folder.Children.Sum(c => (c.IsDeleted ? 1 : 0) + (c.IsDirectory ? Count(c) : 0));

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
        var found = folder.Children.FirstOrDefault(c => c.Name == item.Name && c.Ordinal == item.Ordinal && !c.IsDirectory)
                    ?? throw new FileNotFoundException($"\"{item.Name}\" is not among the deleted items any more.");
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
                folder = folder.Children.FirstOrDefault(c => c.IsDirectory && c.Name == parts[0] && c.Ordinal == ordinal)
                         ?? throw new DirectoryNotFoundException($"\"{parts[0]}\" is not among the deleted items any more.");
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

    /// <summary>The scan of the location's source: made once (it only reads), kept while the source is unchanged.</summary>
    private Session GetSession(Location location, CancellationToken ct)
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
                    cached.Volumes = Scan(cached.Source, device, ct);
                    cached.Stale = false;
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

    private static IReadOnlyList<RecoveryVolume> Scan(IBlockSource source, bool device, CancellationToken ct)
    {
        var volumes = RecoveryScanner.Scan(source, ct);
        if (device)
            foreach (var v in volumes)
                v.Warnings.Insert(0, "This drive is in use while FileCat reads it: Windows and programs can change it at any moment, so this is a snapshot, and deleted files can be overwritten while you work. Recover to another disk, and write nothing to this one meanwhile.");
        return volumes;
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
