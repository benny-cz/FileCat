namespace FileCat.Core.Analysis;

/// <summary>Process-wide budget for immutable authenticated blocks. Keys contain opaque tokens, never native owners.</summary>
internal sealed class AnalysisScratchReadCache
{
    internal sealed class Owner;
    internal const long MaximumBytes = AnalysisLimits.MemoryBytes / 8;
    internal const int MaximumEntries = 512, EntryReservation = 256;
    internal static readonly AnalysisScratchReadCache Shared = new(MaximumBytes);
    private readonly object _gate = new();
    private readonly long _limit;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recent = [];
    private long _reserved;
    private readonly record struct Key(Owner Owner, int Block);
    private sealed record Entry(Key Key, byte[] Bytes, long Reservation);
    internal AnalysisScratchReadCache(long limit)
    {
        if (limit is <= EntryReservation or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit;
    }
    internal (long ReservedBytes, int Entries) Snapshot { get { lock (_gate) return (_reserved, _entries.Count); } }
    internal bool TryGet(Owner owner, int block, out byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(owner); if (block < 0) throw new ArgumentOutOfRangeException(nameof(block));
        lock (_gate)
        {
            if (!_entries.TryGetValue(new(owner, block), out var node)) { bytes = []; return false; }
            _recent.Remove(node); _recent.AddFirst(node); bytes = node.Value.Bytes; return true;
        }
    }
    // The caller transfers immutable block bytes only after authentication and block validation.
    internal void Put(Owner owner, int block, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(bytes);
        if (block < 0 || bytes.Length is < 1 or > AnalysisLimits.BatchBytes) throw new ArgumentOutOfRangeException(nameof(block));
        long reservation = EntryReservation + bytes.LongLength; if (reservation > _limit) return;
        var node = new LinkedListNode<Entry>(new(new(owner, block), bytes, reservation));
        lock (_gate)
        {
            if (_entries.TryGetValue(node.Value.Key, out var old)) Remove(old);
            while (_reserved > _limit - reservation || _entries.Count >= MaximumEntries) Remove(_recent.Last!);
            _entries.Add(node.Value.Key, node); _recent.AddFirst(node); _reserved += reservation;
        }
    }
    internal void RemoveOwner(Owner owner)
    {
        lock (_gate)
        {
            var node = _recent.First;
            while (node is not null)
            {
                var next = node.Next; if (ReferenceEquals(node.Value.Key.Owner, owner)) Remove(node); node = next;
            }
        }
    }
    private void Remove(LinkedListNode<Entry> node)
    { _entries.Remove(node.Value.Key); _recent.Remove(node); _reserved -= node.Value.Reservation; }
}
