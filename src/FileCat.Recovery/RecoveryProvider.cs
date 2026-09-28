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
/// The deleted items of a disk image as a read-only location (plan §17): the image's volumes, then each volume's folders
/// with the deleted files and folders they held. Nothing is ever written to the image: F3 previews and F5 recovers into
/// another folder. Locations: the image file is the container, <see cref="Location.Session"/> the volume number (none
/// for the list of volumes), and <see cref="Location.Path"/> the folder inside the volume ('/'-separated; items that share
/// a name carry "\0" and their ordinal).
/// </summary>
public sealed class RecoveryProvider : ResourceProvider
{
    private const int MaxSessions = 4;
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanLock = new();

    private sealed class Session(IBlockSource source, IReadOnlyList<RecoveryVolume> volumes) : IDisposable
    {
        public IBlockSource Source { get; } = source;
        public IReadOnlyList<RecoveryVolume> Volumes { get; } = volumes;
        public DateTime Used { get; set; } = DateTime.UtcNow;

        public IBlockSource Window(RecoveryVolume v) => new WindowSource(Source, v.Offset, v.Length, $"{Source.Description}, {v.Title}");

        public void Dispose() => Source.Dispose();
    }

    public override string Scheme => Schemes.Recovery;

    /// <summary>The deleted items of a disk image: its only volume, or the list of its volumes.</summary>
    public static Location ForImage(string imagePath, int? volume = null) =>
        new(Schemes.Recovery, string.Empty, Location.FileSystem(imagePath), volume?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Whether a file looks like a disk image FileCat can scan: a partition table or a supported boot sector.</summary>
    public static bool LooksLikeImage(string path)
    {
        try
        {
            using var source = new ImageFileSource(path);
            return PartitionTable.Find(source, []).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static string Image(Location location) =>
        location.Container is { IsFileSystem: true } c ? c.Path : throw new InvalidOperationException("A recovery location without its disk image.");

    // ---- Navigation ---------------------------------------------------------------------------------------

    public override string GetDisplayPath(Location location)
    {
        var parts = new List<string> { Image(location) + " › deleted items" };
        if (location.Session is { } volume && !IsOnlyVolume(location)) parts.Add("volume " + volume);
        if (location.Path.Length > 0) parts.Add(string.Join(Path.DirectorySeparatorChar, location.Path.Split('/').Select(Display)));
        return string.Join(" › ", parts);
    }

    public override string GetDisplayName(Location location)
    {
        if (location.Path.Length > 0) return Display(location.Path[(location.Path.LastIndexOf('/') + 1)..]);
        return location.Session is { } volume && !IsOnlyVolume(location) ? "Volume " + volume : Path.GetFileName(Image(location));
    }

    public override string? GetNameInParent(Location location)
    {
        if (location.Path.Length > 0) return location.Path[(location.Path.LastIndexOf('/') + 1)..].Split('\0')[0];
        if (location.Session is { } volume && !IsOnlyVolume(location)) return VolumeName(int.Parse(volume, CultureInfo.InvariantCulture));
        return Path.GetFileName(Image(location));
    }

    public override Location? GetParent(Location location)
    {
        if (location.Path.Length > 0)
        {
            int slash = location.Path.LastIndexOf('/');
            return location.WithPath(slash < 0 ? string.Empty : location.Path[..slash]);
        }
        if (location.Session is not null && !IsOnlyVolume(location)) return new Location(Schemes.Recovery, string.Empty, location.Container);
        return Location.FileSystem(Path.GetDirectoryName(Image(location)) ?? Image(location));
    }

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(Image(location));

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.MoveSource => "Recovery never changes the image: copy (recover) items with F5 instead.",
        LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.Delete or LocationCapabilities.Rename or
            LocationCapabilities.TransferTarget or LocationCapabilities.ExternalEdit or LocationCapabilities.Recycle =>
            "Recovery only reads: nothing in the image is changed or cleaned up. Copy (recover) items with F5 to a folder.",
        _ => base.ExplainUnavailable(location, capability),
    };

    private bool IsOnlyVolume(Location location) =>
        _sessions.TryGetValue(Key(Image(location)), out var s) && s.Volumes.Count == 1;

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
            throw new DirectoryNotFoundException("This volume is not on the disk image any more.");
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

    private static string Key(string image)
    {
        var info = new FileInfo(image);
        return $"{info.FullName}|{(info.Exists ? info.Length : -1)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}";
    }

    /// <summary>The scan of the location's image: made once (it only reads), kept while the image is unchanged.</summary>
    private Session GetSession(Location location, CancellationToken ct)
    {
        string image = Image(location);
        if (!File.Exists(image)) throw new FileNotFoundException("The disk image no longer exists.", image);
        string key = Key(image);
        if (_sessions.TryGetValue(key, out var cached))
        {
            cached.Used = DateTime.UtcNow;
            return cached;
        }
        lock (_scanLock)
        {
            if (_sessions.TryGetValue(key, out cached)) return cached;
            // An image that changed since its scan is scanned again; the old session goes.
            foreach (var old in _sessions.Where(s => s.Key.StartsWith(Path.GetFullPath(image) + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                if (_sessions.TryRemove(old.Key, out var stale)) stale.Dispose();
            var source = new ImageFileSource(image);
            try
            {
                var session = new Session(source, RecoveryScanner.Scan(source, ct));
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

    /// <summary>Forgets a scan (Reread scans again, for example after the image was replaced).</summary>
    public void Forget(Location location)
    {
        if (location.Scheme != Schemes.Recovery) return;
        string image = Path.GetFullPath(Image(location));
        foreach (var s in _sessions.Where(s => s.Key.StartsWith(image + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            if (_sessions.TryRemove(s.Key, out var old)) old.Dispose();
    }
}
