using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

internal enum StorageVisitAdmission { New, LiveDuplicate, ClosedDuplicate, Limit }
internal readonly record struct StorageVisitState(ulong Owners, StorageEffect? Effect, bool Live, bool SubtreeComplete);

/// <summary>Compact observation hints, never retained pathname or native-handle authority. Fixed hash buckets and
/// stable array chunks avoid a full-table resize. An identity whose owner closed cannot prove a rediscovery.</summary>
internal sealed class StorageVisitLedger
{
    internal const int ObservationLimit = 1_000_000;
    internal const long ByteLimit = 64L * 1024 * 1024;
    private const int ChunkSize = 1024, BucketCount = 65536, ArrayOverhead = 32;
    private readonly int[] _buckets;
    private readonly List<Record[]> _chunks = [];
    private readonly List<Volume> _volumes = [];
    private readonly int _limit;
    private readonly long _byteLimit;
    private long _bytes;
    private int _count, _observations;

    private readonly record struct Volume(ulong Serial, string? Binding);
    private readonly record struct Key(Guid Id, string? OpaqueId, int Volume);
    private struct Record
    {
        internal Key Key;
        internal ulong Owners;
        internal int Hash, Next;
        internal StorageEffect? Effect;
        internal bool Live, Complete;
    }

    internal int Count => _count;
    internal int Observations => _observations;
    internal long RetainedBytes => _bytes;
    internal bool ObserveUnbound()
    {
        if (_observations == _limit) return false;
        _observations++; return true;
    }
    internal StorageVisitLedger(int limit = ObservationLimit, long byteLimit = ByteLimit)
    {
        if (limit is < 1 or > ObservationLimit || byteLimit is < 1 or > ByteLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        _limit = limit; _byteLimit = byteLimit;
        // Include capacity for both bounded reference/index lists, array headers and their small owner objects.
        long fixedBytes = ArrayOverhead + (long)BucketCount * sizeof(int) + 32 * 1024;
        if (fixedBytes > byteLimit) throw new ArgumentOutOfRangeException(nameof(byteLimit), "The ledger needs its fixed bucket and owner capacity.");
        _buckets = new int[BucketCount]; _bytes = fixedBytes;
    }

    internal StorageVisitAdmission Admit(StorageObjectIdentity identity, ulong owners, out int receipt)
    {
        Validate(identity); if (owners == 0) throw new ArgumentOutOfRangeException(nameof(owners));
        receipt = -1;
        if (_observations == _limit) return StorageVisitAdmission.Limit;
        var volume = new Volume(identity.Volume, identity.VolumeBinding);
        int volumeIndex = _volumes.IndexOf(volume);
        bool newVolume = volumeIndex < 0;
        if (newVolume) volumeIndex = _volumes.Count;
        // Only the native adapter's canonical hexadecimal form is compacted; opaque IDs retain ordinal semantics.
        bool binary = identity.ObjectId.Length == 32 && identity.ObjectId.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F') &&
            Guid.TryParseExact(identity.ObjectId, "N", out _);
        Guid id = binary ? Guid.ParseExact(identity.ObjectId, "N") : default;
        var key = new Key(id, binary ? null : identity.ObjectId, volumeIndex);
        int hash = Hash(key), bucket = hash & (BucketCount - 1);
        for (int link = _buckets[bucket]; link != 0;)
        {
            int index = link - 1; ref var prior = ref At(index);
            if (prior.Hash == hash && prior.Key == key)
            {
                receipt = index;
                _observations++;
                return prior.Live ? StorageVisitAdmission.LiveDuplicate : StorageVisitAdmission.ClosedDuplicate;
            }
            link = prior.Next;
        }
        long extra = binary ? 0 : StringBytes(identity.ObjectId);
        if (newVolume) extra += 32 + (identity.VolumeBinding is { } binding ? StringBytes(binding) : 0);
        bool newChunk = _count % ChunkSize == 0;
        if (newChunk) extra += ArrayOverhead + (long)Unsafe.SizeOf<Record>() * ChunkSize;
        if (_bytes > _byteLimit - extra || newVolume && _volumes.Count == 64) return StorageVisitAdmission.Limit;
        if (newVolume) _volumes.Add(volume);
        if (newChunk) _chunks.Add(new Record[ChunkSize]);
        receipt = _count++;
        _observations++;
        At(receipt) = new() { Key = key, Owners = owners, Hash = hash, Next = _buckets[bucket], Live = true };
        _buckets[bucket] = receipt + 1; _bytes += extra;
        return StorageVisitAdmission.New;
    }

    internal void Settle(int receipt, StorageEffect effect)
    {
        if (!Enum.IsDefined(effect)) throw new ArgumentOutOfRangeException(nameof(effect));
        ref var item = ref Require(receipt);
        if (!item.Live || item.Effect is not null) throw new InvalidOperationException("Settle each admitted observation once while its held owner is live.");
        item.Effect = effect;
    }

    internal void Close(int receipt, bool subtreeComplete)
    {
        ref var item = ref Require(receipt);
        if (!item.Live || item.Effect is null) throw new InvalidOperationException("Retire each settled held observation once.");
        item.Complete = subtreeComplete; item.Live = false;
    }

    internal StorageVisitState Read(int receipt)
    {
        ref var item = ref Require(receipt);
        return new(item.Owners, item.Effect, item.Live, item.Complete);
    }
    private ref Record At(int index) => ref _chunks[index / ChunkSize][index % ChunkSize];
    private ref Record Require(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        return ref At(index);
    }
    private static long StringBytes(string text) => (24L + 2L * text.Length + 7) & ~7L;
    private static int Hash(Key key)
    {
        // Guid's ordinary hash can leave the low bits constant for sequential directory IDs. Mix both complete
        // 64-bit halves before selecting a bucket, including the volume and ordinal opaque identity when present.
        Span<byte> bytes = stackalloc byte[16]; key.Id.TryWriteBytes(bytes);
        ulong hash = Mix(BinaryPrimitives.ReadUInt64LittleEndian(bytes)) ^
            Mix(unchecked(BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]) + 0x9E3779B97F4A7C15UL)) ^ Mix((uint)key.Volume);
        if (key.OpaqueId is { } opaque) hash ^= Mix((uint)StringComparer.Ordinal.GetHashCode(opaque));
        return (int)((hash ^ (hash >> 32)) & int.MaxValue);
    }
    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
    private static void Validate(StorageObjectIdentity identity)
    {
        if (identity is null || string.IsNullOrEmpty(identity.ObjectId) || identity.ObjectId.Length > 256 || identity.VolumeBinding is { Length: > 128 })
            throw new ArgumentException("A visited object needs its bounded native identity.", nameof(identity));
    }
}
