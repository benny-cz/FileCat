namespace FileCat.Core.Analysis;

/// <summary>Per-producer, bounded directory-prefix cache over retained captured metadata only.</summary>
internal sealed class AnalysisReportLineage
{
    private readonly AnalysisDatasetDescriptor _descriptor;
    private readonly Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> _read;
    internal AnalysisReportLineage(AnalysisDatasetLease source) : this(source.Descriptor, source.ReadPathAsync) { }
    internal AnalysisReportLineage(AnalysisDatasetDescriptor descriptor, Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> read)
    { _descriptor = descriptor; _read = read; }
    internal const long BudgetBytes = 2L * 1024 * 1024;
    internal const int MaximumEntries = 2048;
    private const string TooLong = "[Displayed location exceeds the report bound; use the full inventory for raw names.]";
    private sealed record Entry(ulong Id, string Root, string Location, bool Limited, bool RootPrefix, long Bytes);
    private readonly Dictionary<ulong, LinkedListNode<Entry>> _cache = new();
    private readonly LinkedList<Entry> _order = new();
    internal long CachedBytes { get; private set; }
    internal int CachedEntries => _cache.Count;
    internal long MetadataReads { get; private set; }

    internal async Task<string> ResolveAsync(AnalysisPathMetadata path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Find(path.Id, path.RootId) is { } ready) return ready.Location;
        var root = _descriptor.Roots.Single(value => value.Id == path.RootId);
        var pending = new List<(ulong Id, string Name, bool Directory)>();
        var current = path; int characters = 0, depth = 0;
        string prefix; bool limited, rootPrefix;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (current.RootId != path.RootId) throw new InvalidDataException("The captured report lineage changed roots.");
            if (Find(current.Id, path.RootId) is { } cached) { prefix = cached.Location; limited = cached.Limited; rootPrefix = cached.RootPrefix; break; }
            if (current.ParentId == 0)
            {
                prefix = AnalysisNames.Display(root.DisplayLocator, 4096); limited = false; rootPrefix = true;
                if (current.Kind == AnalysisKind.Directory) Remember(current.Id, path.RootId, prefix, false, true);
                break;
            }
            if (depth++ >= AnalysisLimits.Depth) throw new InvalidDataException("The captured report lineage exceeds its depth bound.");
            string name = AnalysisNames.DisplayName(current); characters = checked(characters + name.Length + 1);
            if (characters > AnalysisLimits.LocatorBytes)
            {
                if (path.Kind == AnalysisKind.Directory) Remember(path.Id, path.RootId, TooLong, true);
                return TooLong;
            }
            pending.Add((current.Id, name, current.Kind == AnalysisKind.Directory));
            if (Find(current.ParentId, path.RootId) is { } parent) { prefix = parent.Location; limited = parent.Limited; rootPrefix = parent.RootPrefix; break; }
            MetadataReads++; current = await _read(current.ParentId, cancellation).ConfigureAwait(false);
        }
        bool windows = root.NameRules == AnalysisNameRules.Windows;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            cancellation.ThrowIfCancellationRequested(); var item = pending[i];
            if (!limited)
            {
                string parent = rootPrefix ? windows ? prefix.TrimEnd('/', '\\') : prefix.TrimEnd('/') : prefix;
                if ((long)parent.Length + item.Name.Length + 1 > AnalysisLimits.LocatorBytes) { prefix = TooLong; limited = true; }
                else prefix = parent + (windows ? "\\" : "/") + item.Name;
            }
            rootPrefix = false;
            if (item.Directory) Remember(item.Id, path.RootId, prefix, limited);
        }
        return prefix;
    }
    private Entry? Find(ulong id, string root)
    {
        if (!_cache.TryGetValue(id, out var node)) return null;
        if (node.Value.Root != root) throw new InvalidDataException("The captured report lineage changed roots.");
        _order.Remove(node); _order.AddFirst(node); return node.Value;
    }
    private void Remember(ulong id, string root, string location, bool limited, bool rootPrefix = false)
    {
        long bytes = checked(192L + root.Length * 2L + location.Length * 2L);
        if (bytes > BudgetBytes) return;
        if (_cache.Remove(id, out var prior)) { _order.Remove(prior); CachedBytes -= prior.Value.Bytes; }
        while (_order.Last is { } last && (_cache.Count >= MaximumEntries || bytes > BudgetBytes - CachedBytes))
        { _cache.Remove(last.Value.Id); _order.RemoveLast(); CachedBytes -= last.Value.Bytes; }
        var node = _order.AddFirst(new Entry(id, root, location, limited, rootPrefix, bytes)); _cache.Add(id, node); CachedBytes += bytes;
    }
    internal void Clear() { _cache.Clear(); _order.Clear(); CachedBytes = 0; }
}
