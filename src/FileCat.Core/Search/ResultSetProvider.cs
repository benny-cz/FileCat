using System.Collections.Concurrent;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>A named set of references to items elsewhere, with the query that produced it (plan §11).</summary>
public sealed class ResultSet(string id, string title, string provenance)
{
    private readonly object _lock = new();
    private readonly List<ItemRef> _items = [];
    private readonly Dictionary<ItemRef, string> _relative = new();

    public string Id { get; } = id;
    public string Title { get; set; } = title;
    /// <summary>How the set was produced ("Search *.log in C:\x", "Flat view of D:\y").</summary>
    public string Provenance { get; set; } = provenance;
    public bool IsComplete { get; set; }
    public List<string> Issues { get; } = [];
    public event Action? Changed;

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    public void Add(ItemRef item, string relativeFolder)
    {
        lock (_lock)
        {
            if (_relative.ContainsKey(item)) return;
            _items.Add(item);
            _relative[item] = relativeFolder;
        }
    }

    public void NotifyChanged() => Changed?.Invoke();

    public int Remove(IEnumerable<ItemRef> items)
    {
        int n = 0;
        lock (_lock)
        {
            foreach (var i in items)
            {
                if (_relative.Remove(i))
                {
                    _items.Remove(i);
                    n++;
                }
            }
        }
        Changed?.Invoke();
        return n;
    }

    public List<(ItemRef Item, string Relative)> Snapshot()
    {
        lock (_lock) return _items.Select(i => (i, _relative[i])).ToList();
    }
}

/// <summary>
/// Result sets as panel locations: items are the originals and F3–F8 act on them after revalidation;
/// F7 is unavailable and removing from the set never deletes (plan §7.2, §11).
/// </summary>
public sealed class ResultSetProvider(ProviderRegistry providers, IFileSystemOperations fs) : ResourceProvider
{
    private readonly ConcurrentDictionary<string, ResultSet> _sets = new(StringComparer.Ordinal);

    public override string Scheme => Schemes.ResultSet;

    public ResultSet Create(string title, string provenance)
    {
        var set = new ResultSet(Guid.NewGuid().ToString("N")[..12], title, provenance);
        _sets[set.Id] = set;
        return set;
    }

    public static Location LocationOf(ResultSet set) => new(Schemes.ResultSet, string.Empty, session: set.Id);

    public ResultSet? Get(Location location) => location.Session is { } id && _sets.TryGetValue(id, out var s) ? s : null;

    public int Remove(Location location, IEnumerable<ItemRef> items) => Get(location)?.Remove(items) ?? 0;

    public override string GetDisplayPath(Location location) => Get(location) is { } s ? $"Results: {s.Title}" : "Results (closed)";

    public override string GetDisplayName(Location location) => Get(location)?.Title ?? "Results";

    public override Location? GetParent(Location location) => null;

    public override string GetDeviceKey(Location location) => "results";

    public override LocationCapabilities GetCapabilities(Location location) =>
        LocationCapabilities.Enumerate | LocationCapabilities.ReferenceContainer | LocationCapabilities.ReadContent |
        LocationCapabilities.Delete | LocationCapabilities.Recycle | LocationCapabilities.Rename | LocationCapabilities.MoveSource | LocationCapabilities.ExternalEdit;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        capability is LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.TransferTarget
            ? "A result set holds references to items elsewhere; create or copy items into a real folder."
            : base.ExplainUnavailable(location, capability);

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var set = Get(location) ?? throw new DirectoryNotFoundException("This result set was closed.");
        var batch = new List<EntryData>(256);
        foreach (var (item, rel) in set.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            // Revalidate originals: vanished items stay visible as unavailable instead of disappearing silently.
            var info = item.FileSystemPath is { } p ? fs.TryGetInfo(p) : null;
            var e = new EntryData(item.Name, item.Kind, info?.Size ?? item.Size, info?.ModifiedUtc.Ticks ?? item.Modified)
            {
                Tag = new ResultTag(item.Parent, rel, item.Kind),
                Attributes = info is null ? 0 : (uint)info.Attributes,
                Flags = info is null && item.FileSystemPath is not null ? EntryFlags.Unavailable : info is null ? item.Flags : LocalFileSystemProvider.MapFlags(info.Attributes),
            };
            if (info is not null && info.IsDirectory) e.Size = -1;
            batch.Add(e);
            if (batch.Count == 256)
            {
                sink.AddBatch(batch.ToArray());
                batch.Clear();
            }
        }
        if (batch.Count > 0) sink.AddBatch(batch.ToArray());
        return Task.CompletedTask;
    }

    public override ItemRef GetItemRef(Location listing, in EntryData entry) =>
        entry.Tag is ResultTag r
            ? new ItemRef(r.Parent, entry.Name, entry.Kind, entry.Size, entry.Modified) { RelativeFolder = r.RelativeFolder, Flags = entry.Flags }
            : base.GetItemRef(listing, entry);

    public override bool ItemsShareListingParent => false;

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Tag is not ResultTag r) return null;
        return providers.For(r.Parent).GetChildLocation(r.Parent, entry);
    }

    public override IContentSource? OpenContent(ItemRef item) => providers.For(item.Parent).OpenContent(item);
}
