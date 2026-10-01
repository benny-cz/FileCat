using System.Collections.Concurrent;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Mtp;

/// <summary>A device in the list of phones and cameras.</summary>
public sealed record MtpDeviceTag(string DeviceId, string Manufacturer) : IDisplayDetails
{
    public string KindText => "Portable device";
    public string DetailsText => Manufacturer;
}

/// <summary>An object as listed: its device object ID (valid for this connection).</summary>
public sealed record MtpObjectTag(string ObjectId, bool IsStorage);

/// <summary>
/// Phones, cameras, and players over MTP through Windows Portable Devices (P8). Locations name folders by path from
/// the device's storages ("Internal storage/DCIM/Camera"), so tabs and history survive reconnecting; object IDs are
/// looked up per connection. MTP runs one operation at a time per device; jobs share one queue per device. Items with
/// a slash in their name, or sharing a name with a sibling, are shown but not acted on, since a path could not name them
/// unambiguously.
/// </summary>
public sealed class MtpProvider : ResourceProvider
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);

    private sealed class DeviceState(string name)
    {
        public readonly object Lock = new();
        public string Name = name;
        public WpdSession? Session;
        public readonly Dictionary<string, string> Ids = new(StringComparer.Ordinal) { [string.Empty] = Wpd.DeviceObjectId };

        /// <summary>Each storage's access by name, as last listed: what the device lets a computer do in it.</summary>
        public readonly Dictionary<string, StorageAccess> Access = new(StringComparer.Ordinal);

        /// <summary>What the device can do, as its driver said when the device was last opened.</summary>
        public DeviceAbilities Abilities = DeviceAbilities.Unknown;
    }

    public override string Scheme => Schemes.Mtp;

    /// <summary>The list of connected phones, cameras, and players.</summary>
    public static Location Devices { get; } = new(Schemes.Mtp, string.Empty);

    public static bool IsDeviceList(Location location) => location.Scheme == Schemes.Mtp && location.Session is null;

    private DeviceState State(string deviceId) =>
        _devices.GetOrAdd(deviceId, id => new DeviceState(WpdSession.ListDevices().FirstOrDefault(d => d.Id == id)?.Name ?? "Portable device"));

    /// <summary>The open session for a device, reopened after a disconnect.</summary>
    internal WpdSession Session(string deviceId)
    {
        var state = State(deviceId);
        lock (state.Lock)
        {
            if (state.Session is { IsBroken: false } open) return open;
            state.Session?.Dispose();
            state.Ids.Clear();
            state.Ids[string.Empty] = Wpd.DeviceObjectId;
            state.Session = WpdSession.Open(deviceId);
            state.Abilities = state.Session.Abilities;
            return state.Session;
        }
    }

    public override string GetDisplayPath(Location location)
    {
        if (IsDeviceList(location)) return "Phones and cameras";
        string name = State(location.Session!).Name;
        return location.Path.Length == 0 ? name : name + "\\" + location.Path.Replace('/', '\\');
    }

    public override string GetDisplayName(Location location) =>
        IsDeviceList(location) ? "Phones and cameras"
        : location.Path.Length == 0 ? State(location.Session!).Name
        : location.Path[(location.Path.LastIndexOf('/') + 1)..];

    public override Location? GetParent(Location location)
    {
        if (IsDeviceList(location)) return null;
        if (location.Path.Length == 0) return Devices;
        int slash = location.Path.LastIndexOf('/');
        return location.WithPath(slash < 0 ? string.Empty : location.Path[..slash]);
    }

    public override string? GetNameInParent(Location location) => IsDeviceList(location) ? null : GetDisplayName(location);

    public override string GetDeviceKey(Location location) => location.Session is { } id ? "mtp:" + id : "mtp";

    /// <summary>
    /// Inside a storage FileCat offers what the device can do there: reading always; creating, renaming and deleting as far
    /// as the device's driver lists them (an iPhone's lists deleting only) and the storage allows (a write-protected card
    /// is read-only). The device level lists storages only.
    /// </summary>
    public override LocationCapabilities GetCapabilities(Location location)
    {
        if (IsDeviceList(location) || location.Path.Length == 0) return LocationCapabilities.Enumerate;
        var (device, storage) = Allowed(location);
        return Offered(device, storage);
    }

    /// <summary>What FileCat offers inside a storage, given what the device can do and what the storage allows.</summary>
    internal static LocationCapabilities Offered(DeviceAbilities device, StorageAccess storage)
    {
        var offered = LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        if (storage == StorageAccess.ReadWrite)
        {
            if (device.CreateFolders) offered |= LocationCapabilities.CreateDirectory;
            if (device.CreateFiles) offered |= LocationCapabilities.TransferTarget;
            if (device.Rename) offered |= LocationCapabilities.Rename;
        }
        if (storage != StorageAccess.ReadOnly && device.Delete) offered |= LocationCapabilities.Delete;
        return offered;
    }

    /// <summary>
    /// What the device can do, as its driver said when the device was opened, and the access of the storage a location is
    /// in, as listed when the device's storages were last looked at (every way into a storage lists them first). Both allow
    /// everything while unknown, and the device decides, as it does for every change anyway.
    /// </summary>
    internal (DeviceAbilities Device, StorageAccess Storage) Allowed(Location location)
    {
        if (location.Session is not { } id || !_devices.TryGetValue(id, out var state)) return (DeviceAbilities.Unknown, StorageAccess.ReadWrite);
        int slash = location.Path.IndexOf('/');
        string storage = slash < 0 ? location.Path : location.Path[..slash];
        lock (state.Lock) return (state.Abilities, state.Access.GetValueOrDefault(storage, StorageAccess.ReadWrite));
    }

    private static void Remember(DeviceState state, IEnumerable<PortableObject> storages)
    {
        foreach (var s in storages)
            if (s.IsStorage) state.Access[s.Name] = s.Access;
    }

    /// <summary>For tests: a device's answers, as if it had been opened and its storages listed.</summary>
    internal void Assume(string deviceId, DeviceAbilities abilities, IReadOnlyDictionary<string, StorageAccess> storages)
    {
        var state = _devices.GetOrAdd(deviceId, _ => new DeviceState("Portable device"));
        lock (state.Lock)
        {
            state.Abilities = abilities;
            foreach (var (name, access) in storages) state.Access[name] = access;
        }
    }

    private const LocationCapabilities Changes = LocationCapabilities.CreateDirectory | LocationCapabilities.CreateFile | LocationCapabilities.Delete |
        LocationCapabilities.Recycle | LocationCapabilities.Rename | LocationCapabilities.TransferTarget | LocationCapabilities.MoveSource | LocationCapabilities.ExternalEdit;

    /// <summary>
    /// Why a change is not offered inside a storage because the device or the storage does not allow it ("the device ..."),
    /// or null when they do: then any other reason (no Recycle Bin, no moving off a device) is the general one.
    /// </summary>
    internal string? Refusal(Location location, LocationCapabilities capability)
    {
        if (IsDeviceList(location) || location.Path.Length == 0) return null;
        var (device, storage) = Allowed(location);
        return Refusal(device, storage, capability);
    }

    internal static string? Refusal(DeviceAbilities device, StorageAccess storage, LocationCapabilities capability)
    {
        if ((capability & Changes) == 0) return null;
        bool deleting = capability is LocationCapabilities.Delete or LocationCapabilities.Recycle or LocationCapabilities.MoveSource;
        if (storage == StorageAccess.ReadOnly)
            return "the device offers this storage read-only, so nothing on it can be added, renamed or deleted; copy files off it with F5.";
        if (storage == StorageAccess.ReadOnlyWithDeletion && !deleting)
            return "the device offers this storage read-only apart from deleting, so nothing can be added to it or renamed; copy files off it with F5.";
        string? refused = capability switch
        {
            _ when deleting => device.Delete ? null : "delete its files",
            LocationCapabilities.CreateDirectory => device.CreateFolders ? null : "create folders on it",
            LocationCapabilities.TransferTarget or LocationCapabilities.CreateFile => device.CreateFiles ? null : "add files to it",
            LocationCapabilities.Rename => device.Rename ? null : "rename its files",
            LocationCapabilities.ExternalEdit => device.CreateFiles ? null : "change its files",
            _ => null,
        };
        if (refused is null) return null;
        string offers = device switch
        {
            { CreateFolders: false, CreateFiles: false, Rename: false, Delete: true } => "; it offers its files to copy off and to delete, as iPhones do",
            { CreateFolders: false, CreateFiles: false, Rename: false, Delete: false } => "; it offers its files to copy off only",
            _ => string.Empty,
        };
        return $"the device does not let a computer {refused}{offers}.";
    }

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        _ when IsDeviceList(location) => "This is the list of phones and cameras: Enter opens a device.",
        _ when location.Path.Length == 0 => "Open a storage (internal memory or a card) to work with files.",
        _ when Refusal(location, capability) is { } why => char.ToUpperInvariant(why[0]) + why[1..],
        LocationCapabilities.Recycle => "Phones and cameras have no Recycle Bin; items are deleted permanently after you confirm.",
        LocationCapabilities.Watch => "Devices do not report changes; press Ctrl+R to refresh.",
        LocationCapabilities.CreateFile => "Create the file in a folder on disk, then copy it to the device with F5.",
        LocationCapabilities.MoveSource => "Moving off a device is not supported: copy with F5, then delete with F8.",
        LocationCapabilities.ExternalEdit => "Copy the file to a folder on disk to edit it, then copy it back.",
        _ => base.ExplainUnavailable(location, capability),
    };

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.Run(() =>
    {
        if (IsDeviceList(location))
        {
            var devices = WpdSession.ListDevices();
            foreach (var d in devices) _devices.GetOrAdd(d.Id, _ => new DeviceState(d.Name)).Name = d.Name;
            if (devices.Count > 0)
                sink.AddBatch(devices.Select(d => new EntryData(d.Name, EntryKind.Drive, -1, 0) { Tag = new MtpDeviceTag(d.Id, d.Manufacturer) }).ToArray());
            return;
        }
        string deviceId = location.Session!;
        var session = Session(deviceId);
        var children = session.Children(Resolve(location), ct);
        if (location.Path.Length == 0 && children.Count == 0)
            sink.ReportIssue("The device shows no storage. Unlock it and allow this computer (Trust on an iPhone, File transfer in an Android phone's USB options), then press Ctrl+R.");
        var state = State(deviceId);
        var names = children.GroupBy(c => c.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var batch = new List<EntryData>(children.Count);
        lock (state.Lock)
        {
            if (location.Path.Length == 0) Remember(state, children);
            foreach (var c in children)
            {
                bool unusable = c.Name.Contains('/') || c.Name.Contains('\\') || names[c.Name] > 1;
                if (!unusable) state.Ids[Join(location.Path, c.Name)] = c.Id;
                batch.Add(new EntryData(c.Name, c.IsFolder ? EntryKind.Directory : EntryKind.File, c.IsFolder ? -1 : c.Size,
                    c.ModifiedUtc == DateTime.MinValue ? 0 : c.ModifiedUtc.Ticks)
                {
                    Tag = new MtpObjectTag(c.Id, c.IsStorage),
                    Flags = (c.IsHidden ? EntryFlags.Hidden : EntryFlags.None) | (unusable ? EntryFlags.Unavailable : EntryFlags.None),
                });
            }
        }
        if (names.Any(n => n.Value > 1)) sink.ReportIssue("Some items share a name with another item here; FileCat shows them but does not act on them.");
        if (batch.Count > 0) sink.AddBatch(batch.ToArray());
    }, ct);

    private static string Join(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    /// <summary>The object ID of a folder location, looked up by name from the storages down (and remembered).</summary>
    internal string Resolve(Location location)
    {
        string deviceId = location.Session ?? throw new DirectoryNotFoundException("No device.");
        var state = State(deviceId);
        var session = Session(deviceId);
        lock (state.Lock)
            if (state.Ids.TryGetValue(location.Path, out var known)) return known;
        string path = string.Empty, id = Wpd.DeviceObjectId;
        foreach (var part in location.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Join(path, part);
            string? found;
            lock (state.Lock) state.Ids.TryGetValue(next, out found);
            if (found is null)
            {
                var children = session.Children(id, CancellationToken.None);
                if (path.Length == 0) lock (state.Lock) Remember(state, children);
                var matches = children.Where(c => c.Name == part).ToList();
                if (matches.Count != 1)
                    throw matches.Count == 0 ? new DirectoryNotFoundException($"\"{part}\" is no longer on the device.")
                        : new IOException($"Several items on the device are named \"{part}\"; FileCat cannot tell them apart.");
                found = matches[0].Id;
                lock (state.Lock) state.Ids[next] = found;
            }
            path = next;
            id = found;
        }
        return id;
    }

    /// <summary>The object of an item in a folder location, or null when it is not there.</summary>
    internal PortableObject? Find(Location folder, string name)
    {
        var session = Session(folder.Session!);
        string parentId = Resolve(folder);
        var state = State(folder.Session!);
        string? known;
        lock (state.Lock) state.Ids.TryGetValue(Join(folder.Path, name), out known);
        // The listing remembered the item: its own properties confirm it in one request. Listing the whole folder for every
        // file made copying photos off a phone quadratic in the folder's size.
        if (known is not null && session.Get(known) is { } remembered && remembered.Name == name && remembered.ParentId == parentId) return remembered;
        var matches = session.Children(parentId, CancellationToken.None).Where(c => c.Name == name).ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new IOException($"Several items on the device are named \"{name}\"; FileCat cannot tell them apart."),
        };
    }

    /// <summary>
    /// The item a new or renamed item named <paramref name="name"/> would collide with. Phones' storage usually ignores
    /// letter case while MTP lists names as given, so "Photo.jpg" and "photo.jpg" are one file there: writing one would
    /// overwrite the other. Null when there is none; an exact match wins over case variants.
    /// </summary>
    internal PortableObject? FindSameName(Location folder, string name)
    {
        var session = Session(folder.Session!);
        var matches = session.Children(Resolve(folder), CancellationToken.None).Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count <= 1) return matches.FirstOrDefault();
        var exact = matches.Where(m => m.Name == name).ToList();
        return exact.Count == 1 ? exact[0]
            : throw new IOException($"Several items on the device are named \"{name}\" in different letter case; FileCat cannot tell which one is meant.");
    }

    /// <summary>Forgets remembered object IDs below a folder after a change there.</summary>
    internal void Changed(Location folder)
    {
        if (folder.Session is not { } id || !_devices.TryGetValue(id, out var state)) return;
        lock (state.Lock)
            foreach (var key in state.Ids.Keys.Where(k => k.Length > 0 && (k == folder.Path || k.StartsWith(folder.Path.Length == 0 ? "" : folder.Path + "/", StringComparison.Ordinal))).ToList())
                if (key != folder.Path) state.Ids.Remove(key);
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return GetParent(parent);
        if (entry.Tag is MtpDeviceTag device) return new Location(Schemes.Mtp, string.Empty, session: device.DeviceId);
        if (entry.Kind != EntryKind.Directory || entry.Has(EntryFlags.Unavailable)) return null;
        return parent.WithPath(Join(parent.Path, entry.Name));
    }

    public override IContentSource? OpenContent(ItemRef item)
    {
        var obj = Find(item.Parent, item.Name) ?? throw new FileNotFoundException("The file is no longer on the device.");
        if (obj.IsFolder) throw new IOException("This is a folder.");
        return new MtpContentSource(Session(item.Parent.Session!), obj);
    }

    public void CloseAll()
    {
        foreach (var state in _devices.Values)
            lock (state.Lock)
            {
                state.Session?.Dispose();
                state.Session = null;
            }
    }
}

/// <summary>
/// A file on a device as random-access content: reading moves forward through one transfer, and reading behind the
/// current position starts a new transfer (devices read sequentially).
/// </summary>
internal sealed class MtpContentSource(WpdSession session, PortableObject obj) : IContentSource
{
    private readonly object _lock = new();
    private Stream? _stream;
    private long _position;

    public string DisplayName => obj.Name;
    public long Length => obj.Size;
    public bool CanSeek => true;
    public string? LocalPath => null;

    public int Read(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            if (offset >= obj.Size || buffer.Length == 0) return 0;
            if (_stream is null || offset < _position)
            {
                _stream?.Dispose();
                _stream = session.OpenRead(obj.Id);
                _position = 0;
            }
            var skip = new byte[64 * 1024];
            while (_position < offset)
            {
                int n = _stream.Read(skip, 0, (int)Math.Min(skip.Length, offset - _position));
                if (n <= 0) return 0;
                _position += n;
            }
            int total = 0;
            var chunk = new byte[Math.Min(buffer.Length, 1024 * 1024)];
            while (total < buffer.Length)
            {
                int n = _stream.Read(chunk, 0, Math.Min(chunk.Length, buffer.Length - total));
                if (n <= 0) break;
                chunk.AsSpan(0, n).CopyTo(buffer[total..]);
                total += n;
                _position += n;
            }
            return total;
        }
    }

    public ContentRevision? GetRevision() => new ContentRevision(obj.Size, obj.ModifiedUtc.Ticks);

    public void Dispose()
    {
        lock (_lock)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
