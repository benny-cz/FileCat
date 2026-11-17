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
    private Dictionary<int, KeyChunk>? _chunks;
    private List<KeyChunk>? _denseChunks;
    private KeyChunk[] _dense = [];
    private Dictionary<Principal, (NameStatus Status, PrincipalOrderKey Key)>? _principals;
    private Dictionary<IComparable, IComparable>? _numbers;
    private long _bytes;
    private int _references = 1, _chars;
    private bool _sealed, _stopped, _ownerDisposed;
    private sealed class KeyChunk
    {
        public readonly IComparable?[] Keys = new IComparable?[ChunkSize];
        public readonly ulong[] Present = new ulong[4];
        public MetadataValue[]? Values;
        public bool Contains(int slot) => (Present[slot >> 6] & (1UL << (slot & 63))) != 0;
    }

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
    public IReadOnlyDictionary<Principal, (NameStatus Status, PrincipalOrderKey Key)>? PrincipalNames => _principals;
    internal int CanonicalNumberCount => _numbers?.Count ?? 0;
    public MetadataOrderCoverage Coverage => new(Count, Capacity, NameSummary);
    public MetadataKeyProvider Keys => (_, index, _) => Key(index);

    public bool TryAdd(int index, MetadataValue value, IComparable? key = null,
        PrincipalName? principalName = null, CompareInfo? comparison = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _references) == 0, this);
        if (_sealed) throw new InvalidOperationException("An ordering snapshot is immutable after capture.");
        if (_stopped) return false;
        if (Count >= _maxRows) return Stop(MetadataCapacity.Rows);
        if (!Payload(value.Value, out long valueBytes, out long valueChars) ||
            !Payload(key, out long keyBytes, out long keyChars)) return false;
        if (key is string) keyBytes += 64;
        PrincipalOrderKey? capturedPrincipal = null;
        bool newPrincipal = false;
        if (value.Value is Principal principal && principalName is { } name)
        {
            if (_principals is not null && _principals.TryGetValue(principal, out var prior)) capturedPrincipal = prior.Key;
            else newPrincipal = true;
            keyChars = capturedPrincipal?.RetainedCharacters ??
                principal.Id.Length + (long)principal.Authority.Length + (name.Status == NameStatus.Resolved ? name.Name?.Length ?? 0 : 0);
            keyBytes = 128 + 2 * keyChars;
        }
        bool number = key is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or TimeSpan;
        bool newNumber = false;
        if (number)
        {
            if (_numbers is not null && _numbers.TryGetValue(key!, out var canonical)) key = canonical;
            else newNumber = (_numbers?.Count ?? 0) < 256;
        }
        long principalChars = newPrincipal && value.Value is Principal pc ? pc.Id.Length + (long)pc.Authority.Length : 0;
        long chars = valueChars + keyChars + (value.Detail?.Length ?? 0L);
        chars += principalChars + (newPrincipal ? keyChars : 0);
        if (chars > _maxChars - (long)_chars) return Stop(MetadataCapacity.Characters);
        var entries = Chunk(index / ChunkSize);
        bool chunk = entries is null;
        bool hasValue = value != default;
        bool valuesBuffer = hasValue && entries?.Values is null;
        long bytes = 16 + valueBytes + keyBytes + (value.Detail is null ? 0 : 32 + 2L * value.Detail.Length)
            + (chunk ? ChunkSize * 8L + 512 : 0) + (_chunks is null && _denseChunks is null ? 256 : 0)
            + (valuesBuffer ? ChunkSize * 40L + 64 : 0)
            + (newPrincipal ? 384 + 2 * (principalChars + keyChars) + (_principals is null ? 256 : 0) : 0)
            + (newNumber ? 128 + (_numbers is null ? 256 : 0) : 0);
        if (!_budget.TryReserve(this, _bytes + bytes)) return Stop(MetadataCapacity.SharedMemory);
        _bytes += bytes;
        _chars += (int)chars;
        if (entries is null)
        {
            entries = new KeyChunk();
            if (index / ChunkSize == (_denseChunks?.Count ?? 0)) (_denseChunks ??= []).Add(entries);
            else (_chunks ??= []).Add(index / ChunkSize, entries);
        }
        int slot = index % ChunkSize;
        if (entries.Contains(slot)) throw new InvalidOperationException("Duplicate snapshot index.");
        if (value.Value is Principal p && principalName is { } n)
        {
            key = capturedPrincipal ?? new PrincipalOrderKey(p, n, comparison ?? CultureInfo.CurrentCulture.CompareInfo);
            if (newPrincipal) (_principals ??= []).Add(p, (n.Status, (PrincipalOrderKey)key));
        }
        else if (key is string text) key = new MetadataTextKey(text, comparison ?? CultureInfo.CurrentCulture.CompareInfo);
        if (newNumber) (_numbers ??= []).Add(key!, key!);
        if (valuesBuffer) entries.Values = new MetadataValue[ChunkSize];
        if (hasValue) entries.Values![slot] = value;
        entries.Keys[slot] = key;
        entries.Present[slot >> 6] |= 1UL << (slot & 63);
        Count++;
        return true;
    }

    private bool Stop(MetadataCapacity capacity) { Capacity |= capacity; _stopped = true; return false; }
    public void Seal()
    {
        // The compact prefix is built under the already charged per-chunk overhead, before comparisons.
        _dense = _denseChunks?.ToArray() ?? [];
        _sealed = true;
    }
    public IComparable? Key(int index)
    {
        if (index < 0) return null;
        int chunk = index >> 8;
        return chunk < _dense.Length ? _dense[chunk].Keys[index & 255] : Chunk(chunk)?.Keys[index & 255];
    }
    public bool TryValue(int index, out MetadataValue value)
    {
        var chunk = index < 0 ? null : Chunk(index / ChunkSize);
        int slot = index & 255;
        value = chunk?.Values is { } values ? values[slot] : default;
        return chunk?.Contains(slot) == true;
    }
    private KeyChunk? Chunk(int chunk) => chunk < (_denseChunks?.Count ?? 0) ? _denseChunks![chunk] :
        _chunks is not null && _chunks.TryGetValue(chunk, out var entries) ? entries : null;

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
            _dense = [];
            _principals = null;
            _numbers = null;
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
