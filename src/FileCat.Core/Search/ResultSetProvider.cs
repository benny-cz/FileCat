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
    private readonly Dictionary<ItemRef, string> _notes = new();

    public string Id { get; } = id;
    public string Title { get; set; } = title;
    /// <summary>How the set was produced ("Search *.log in C:\x", "Flat view of D:\y").</summary>
    public string Provenance { get; set; } = provenance;
    public bool IsComplete { get; set; }
    public List<string> Issues { get; } = [];
    public event Action? Changed;

    /// <summary>A named working set collected by hand and kept between sessions (P7), rather than one query's results.</summary>
    public bool IsWorkingSet { get; init; }

    /// <summary>
    /// Items show their full folder rather than the one relative to where the search began (Find's results, which may
    /// come from several folders; working sets always do).
    /// </summary>
    public bool FullFolders { get; set; }

    /// <summary>
    /// The items are groups of alike files (Find's duplicates): the list keeps their order and each item's note names
    /// its group.
    /// </summary>
    public bool ShowsGroups { get; set; }

    /// <summary>A short note shown beside an item ("group 3 of 12 · 2 files").</summary>
    public void SetNote(ItemRef item, string note)
    {
        lock (_lock) _notes[item] = note;
    }

    public string? NoteOf(ItemRef item)
    {
        lock (_lock) return _notes.GetValueOrDefault(item);
    }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

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

    /// <summary>Adds references not yet in the set, stopping at <paramref name="limit"/> members; returns how many were added.</summary>
    public int AddRange(IEnumerable<(ItemRef Item, string Relative)> items, int limit = int.MaxValue)
    {
        int n = 0;
        lock (_lock)
        {
            foreach (var (item, rel) in items)
            {
                if (_items.Count >= limit) break;
                if (!_relative.TryAdd(item, rel)) continue;
                _items.Add(item);
                n++;
            }
            if (n > 0) ModifiedUtc = DateTime.UtcNow;
        }
        if (n > 0) Changed?.Invoke();
        return n;
    }

    public bool Contains(ItemRef item)
    {
        lock (_lock) return _relative.ContainsKey(item);
    }

    public void NotifyChanged() => Changed?.Invoke();

    public int Remove(IEnumerable<ItemRef> items)
    {
        int n = 0;
        lock (_lock)
        {
            try
            {
                foreach (var i in items)
                {
                    if (!_relative.Remove(i)) continue;
                    _notes.Remove(i);
                    n++;
                }
            }
            finally
            {
                // Compact once instead of scanning and shifting the member list for every selected item.
                // An interrupted selection still leaves all successfully removed references consistent.
                if (n > 0)
                {
                    _items.RemoveAll(i => !_relative.ContainsKey(i));
                    ModifiedUtc = DateTime.UtcNow;
                }
            }
        }
        Changed?.Invoke();
        return n;
    }

    /// <summary>Points a member at its new name or place after a rename or move made through the set.</summary>
    public bool Replace(ItemRef old, ItemRef replacement)
    {
        lock (_lock)
        {
            int i = _items.IndexOf(old);
            if (i < 0 || !_relative.Remove(old, out var rel)) return false;
            _notes.Remove(old, out var note);
            if (_relative.ContainsKey(replacement))
            {
                _items.RemoveAt(i); // already a member under its new identity
            }
            else
            {
                _items[i] = replacement;
                _relative[replacement] = rel;
                if (note is not null) _notes[replacement] = note;
            }
            ModifiedUtc = DateTime.UtcNow;
        }
        Changed?.Invoke();
        return true;
    }

    public List<(ItemRef Item, string Relative)> Snapshot()
    {
        lock (_lock) return _items.Select(i => (i, _relative[i])).ToList();
    }
}

/// <summary>
/// Result sets as panel locations: items are the originals and F3–F8 act on them after revalidation;
/// F7 is unavailable and removing from the set never deletes (plan §7.2, §11). Working sets are result sets kept
/// between sessions; their list is a location of its own.
/// </summary>
public sealed class ResultSetProvider(ProviderRegistry providers, IFileSystemOperations fs) : ResourceProvider
{
    public const string WorkingSetListPath = "working-sets";
    public const string WorkingSetPath = "working-set";

    private readonly ConcurrentDictionary<string, ResultSet> _sets = new(StringComparer.Ordinal);

    public override string Scheme => Schemes.ResultSet;

    /// <summary>The list of working sets (F7 creates one, Enter opens one).</summary>
    public static Location WorkingSetList { get; } = new(Schemes.ResultSet, WorkingSetListPath);

    public static bool IsWorkingSetList(Location? location) => location is { Scheme: Schemes.ResultSet, Path: WorkingSetListPath };

    public static bool IsWorkingSet(Location? location) => location is { Scheme: Schemes.ResultSet, Path: WorkingSetPath };

    /// <summary>Working sets and their list outlive the session (tabs and history keep them); other result sets do not.</summary>
    public static bool IsPersistent(Location? location) => IsWorkingSetList(location) || IsWorkingSet(location);

    /// <summary>The working sets, supplied by <see cref="WorkingSets"/>.</summary>
    internal Func<IReadOnlyList<ResultSet>>? WorkingSetsSource { get; set; }

    public ResultSet Create(string title, string provenance)
    {
        var set = new ResultSet(Guid.NewGuid().ToString("N")[..12], title, provenance);
        _sets[set.Id] = set;
        return set;
    }

    /// <summary>Registers a private result set when it becomes a browsable location, preserving any streaming producer.</summary>
    public void Adopt(ResultSet set) => _sets[set.Id] = set;

    internal void Forget(ResultSet set) => _sets.TryRemove(set.Id, out _);

    public static Location LocationOf(ResultSet set) => new(Schemes.ResultSet, set.IsWorkingSet ? WorkingSetPath : string.Empty, session: set.Id);

    public ResultSet? Get(Location location) => location.Session is { } id && _sets.TryGetValue(id, out var s) ? s : null;

    public int Remove(Location location, IEnumerable<ItemRef> items) => Get(location)?.Remove(items) ?? 0;

    public override string GetDisplayPath(Location location)
    {
        if (IsWorkingSetList(location)) return "Working sets";
        if (IsWorkingSet(location)) return Get(location) is { } w ? $"Working set: {w.Title}" : "Working set (deleted)";
        return Get(location) is { } s ? $"Results: {s.Title}" : "Results (closed)";
    }

    public override string GetDisplayName(Location location) =>
        IsWorkingSetList(location) ? "Working sets" : Get(location)?.Title ?? (IsWorkingSet(location) ? "Working set" : "Results");

    public override Location? GetParent(Location location) => IsWorkingSet(location) ? WorkingSetList : null;

    public override string? GetNameInParent(Location location) => IsWorkingSet(location) ? Get(location)?.Title : null;

    public override string GetDeviceKey(Location location) => "results";

    public override LocationCapabilities GetCapabilities(Location location) =>
        IsWorkingSetList(location)
            ? LocationCapabilities.Enumerate
            : LocationCapabilities.Enumerate | LocationCapabilities.ReferenceContainer | LocationCapabilities.ReadContent |
              LocationCapabilities.Delete | LocationCapabilities.Recycle | LocationCapabilities.Rename | LocationCapabilities.MoveSource | LocationCapabilities.ExternalEdit;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability)
    {
        if (IsWorkingSetList(location)) return "This is the list of working sets: F7 creates a set, Enter opens one.";
        if (IsWorkingSet(location) && capability is LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.TransferTarget)
            return "A working set holds references to items elsewhere: F5 from another panel adds references; create or copy items into a real folder.";
        return capability is LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.TransferTarget
            ? "A result set holds references to items elsewhere; create or copy items into a real folder."
            : base.ExplainUnavailable(location, capability);
    }

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        if (IsWorkingSetList(location))
        {
            var sets = WorkingSetsSource?.Invoke() ?? [];
            if (sets.Count > 0)
                sink.AddBatch(sets.Select(s => new EntryData(s.Title, EntryKind.Directory, -1, s.ModifiedUtc.Ticks) { Tag = new WorkingSetTag(s.Id, s.Count) }).ToArray());
            return Task.CompletedTask;
        }
        var set = Get(location) ?? throw new DirectoryNotFoundException(IsWorkingSet(location) ? "This working set was deleted." : "This result set was closed.");
        var batch = new List<EntryData>(256);
        var folders = new Dictionary<Location, string>();
        foreach (var (item, rel) in set.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            // Revalidate originals: vanished items stay visible as unavailable instead of disappearing silently.
            var info = item.FileSystemPath is { } p ? fs.TryGetInfo(p) : null;
            string? folder = null;
            if ((set.IsWorkingSet || set.FullFolders) && !folders.TryGetValue(item.Parent, out folder))
                folders[item.Parent] = folder = providers.TryGet(item.Parent.Scheme, out var owner) && owner is not null ? owner.GetDisplayPath(item.Parent) : item.Parent.ToString();
            var e = new EntryData(item.Name, item.Kind, info?.Size ?? item.Size, info?.ModifiedUtc.Ticks ?? item.Modified)
            {
                Tag = new ResultTag(item.Parent, rel, item.Kind) { Folder = folder, Ordinal = item.Ordinal, Note = set.NoteOf(item) },
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
            ? new ItemRef(r.Parent, entry.Name, entry.Kind, entry.Size, entry.Modified) { RelativeFolder = r.RelativeFolder, Flags = entry.Flags, Ordinal = r.Ordinal }
            : base.GetItemRef(listing, entry);

    public override bool ItemsShareListingParent => false;

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Tag is WorkingSetTag w) return new Location(Schemes.ResultSet, WorkingSetPath, session: w.Id);
        if (entry.Tag is not ResultTag r) return null;
        return providers.For(r.Parent).GetChildLocation(r.Parent, entry);
    }

    public override IContentSource? OpenContent(ItemRef item) => providers.For(item.Parent).OpenContent(item);
}
