using FileCat.Core.FileSystem;
using FileCat.Core.Metadata;
using System.Globalization;

namespace FileCat.Core.Listing;

[Flags]
public enum MetadataCapacity { None = 0, Rows = 1, Characters = 2, SharedMemory = 4 }
public sealed record MetadataOrderCoverage(int Count, MetadataCapacity Capacity, string? Names);

/// <summary>Append during capture, then memory-only lookups. Storage grows only for admitted store indices.
/// Every buffer and bounded immutable payload is reserved before retention; the last actual user releases it.</summary>
public sealed class MetadataOrderSnapshot : IDisposable
{
    private const int ChunkSize = 256;
    private readonly object _gate = new();
    private readonly IndexMemoryBudget _budget;
    private readonly int _maxRows, _maxChars;
    private Dictionary<int, Item?[]>? _chunks;
    private List<Item?[]>? _denseChunks;
    private Dictionary<Principal, NameStatus>? _principals;
    private long _bytes;
    private int _references = 1, _chars;
    private bool _sealed, _stopped, _ownerDisposed;
    private sealed record Item(MetadataValue Value, IComparable? Key);

    public MetadataOrderSnapshot(IndexMemoryBudget budget, int maxRows = 1_000_000, int maxChars = 16_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRows);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);
        _budget = budget; _maxRows = maxRows; _maxChars = maxChars;
    }

    public int Count { get; private set; }
    public MetadataCapacity Capacity { get; private set; }
    public long ReservedBytes => _bytes;
    public int RetainedCharacters => _chars;
    public void Limit(MetadataCapacity capacity) => Stop(capacity);
    public string? NameSummary { get; set; }
    public IReadOnlyDictionary<Principal, NameStatus>? PrincipalNames => _principals;
    public MetadataOrderCoverage Coverage => new(Count, Capacity, NameSummary);
    public MetadataKeyProvider Keys => (_, index, _) => Key(index);

    public bool TryAdd(int index, MetadataValue value, IComparable? key = null,
        PrincipalName? principalName = null, CompareInfo? comparison = null)
    {
        if (_sealed) throw new InvalidOperationException("An ordering snapshot is immutable after capture.");
        if (_stopped) return false;
        if (Count >= _maxRows) return Stop(MetadataCapacity.Rows);
        if (!Payload(value.Value, out long valueBytes, out long valueChars) ||
            !Payload(key, out long keyBytes, out long keyChars)) return false;
        if (key is string) keyBytes += 64;
        if (value.Value is Principal principal && principalName is { } name)
        {
            keyChars = principal.Id.Length + (long)principal.Authority.Length + (name.Status == NameStatus.Resolved ? name.Name?.Length ?? 0 : 0);
            keyBytes = 128 + 2 * keyChars;
        }
        bool newPrincipal = value.Value is Principal pp && principalName is not null &&
            (_principals is null || !_principals.ContainsKey(pp));
        long principalChars = newPrincipal && value.Value is Principal pc ? pc.Id.Length + (long)pc.Authority.Length : 0;
        long chars = valueChars + keyChars + (value.Detail?.Length ?? 0L);
        chars += principalChars;
        if (chars > _maxChars - (long)_chars) return Stop(MetadataCapacity.Characters);
        var entries = Chunk(index / ChunkSize);
        bool chunk = entries is null;
        long bytes = 160 + valueBytes + keyBytes + (value.Detail is null ? 0 : 32 + 2L * value.Detail.Length)
            + (chunk ? ChunkSize * 8L + 256 : 0) + (_chunks is null && _denseChunks is null ? 256 : 0)
            + (newPrincipal ? 256 + 2 * principalChars + (_principals is null ? 256 : 0) : 0);
        if (!_budget.TryReserve(this, _bytes + bytes)) return Stop(MetadataCapacity.SharedMemory);
        _bytes += bytes;
        _chars += (int)chars;
        if (entries is null)
        {
            entries = new Item?[ChunkSize];
            if (index / ChunkSize == (_denseChunks?.Count ?? 0)) (_denseChunks ??= []).Add(entries);
            else (_chunks ??= []).Add(index / ChunkSize, entries);
        }
        if (entries[index % ChunkSize] is not null) throw new InvalidOperationException("Duplicate snapshot index.");
        if (value.Value is Principal p && principalName is { } n)
        {
            key = new PrincipalOrderKey(p, n, comparison ?? CultureInfo.CurrentCulture.CompareInfo);
            if (newPrincipal) (_principals ??= []).Add(p, n.Status);
        }
        else if (key is string text) key = new MetadataTextKey(text, comparison ?? CultureInfo.CurrentCulture.CompareInfo);
        entries[index % ChunkSize] = new(value, key);
        Count++;
        return true;
    }

    private bool Stop(MetadataCapacity capacity) { Capacity |= capacity; _stopped = true; return false; }
    public void Seal() => _sealed = true;
    public IComparable? Key(int index) => Find(index)?.Key;
    public bool TryValue(int index, out MetadataValue value)
    {
        var item = Find(index);
        value = item?.Value ?? default;
        return item is not null;
    }
    private Item?[]? Chunk(int chunk) => chunk < (_denseChunks?.Count ?? 0) ? _denseChunks![chunk] :
        _chunks is not null && _chunks.TryGetValue(chunk, out var entries) ? entries : null;
    private Item? Find(int index) => index >= 0 ? Chunk(index / ChunkSize)?[index % ChunkSize] : null;

    public IDisposable Lease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerDisposed, this);
            _references++;
            return new SnapshotLease(this);
        }
    }
    private sealed class SnapshotLease(MetadataOrderSnapshot owner) : IDisposable
    {
        private MetadataOrderSnapshot? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_ownerDisposed) return;
            _ownerDisposed = true;
            Release();
        }
    }
    private void Release()
    {
        lock (_gate)
        {
            if (--_references != 0) return;
            _chunks = null;
            _denseChunks = null;
            _principals = null;
            _budget.Release(this);
            _bytes = 0;
        }
    }

    // No arbitrary object graph can be retained by a custom field. Strings are charged per reference.
    private static bool Payload(object? value, out long bytes, out long chars)
    {
        chars = 0;
        bytes = 32;
        switch (value)
        {
            case null: bytes = 0; return true;
            case string s: chars = s.Length; bytes += 2 * chars; return true;
            case Principal p: chars = p.Id.Length + (long)p.Authority.Length; bytes += 64 + 2 * chars; return true;
            case PrincipalOrderKey p: chars = p.RetainedCharacters; bytes += 96 + 2 * chars; return true;
            case MetadataTextKey text: chars = text.RetainedCharacters; bytes += 64 + 2 * chars; return true;
            case Version: bytes = 64; return true;
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                or DateTime or DateTimeOffset or TimeSpan or UnixFileMode or ValueTuple<int, int>: return true;
            default: return false;
        }
    }

    public static MetadataOrderSnapshot Capture(EntryStore store, int count, string id, MetadataKeyProvider? input,
        IndexMemoryBudget budget, CancellationToken ct)
    {
        var snapshot = new MetadataOrderSnapshot(budget);
        try
        {
            if (input is not null)
                for (int i = 0; i < count; i++)
                {
                    if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                    var key = input(store, i, id);
                    if (key is not null && !snapshot.TryAdd(i, default, key) && snapshot.Capacity != MetadataCapacity.None) break;
                }
            snapshot.Seal();
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }
}

internal sealed class MetadataTextKey(string text, CompareInfo comparison) : IComparable
{
    public int RetainedCharacters => text.Length;
    public int CompareTo(object? other) => other is MetadataTextKey key ? comparison.Compare(text, key.Text, CompareOptions.None)
        : throw new ArgumentException("Expected a captured text key.");
    private string Text => text;
}
