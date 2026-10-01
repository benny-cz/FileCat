using System.Buffers.Binary;
using System.Text;

namespace FileCat.Recovery;

/// <summary>
/// exFAT (Microsoft exFAT specification). Deleting clears the in-use bit of each entry in a file's entry set (0x85 → 0x05,
/// 0xC0 → 0x40, 0xC1 → 0x41) and frees its clusters in the allocation bitmap, but keeps name, size, first cluster, and
/// whether the file was stored in one piece. A fragmented file's chain survives in the FAT unless it was overwritten, so
/// FileCat follows it when it is complete and consistent, and says when it falls back to one continuous run. A lost file
/// system (<see cref="VolumeSlot.WholeFileSystem"/>) lists its existing files too.
/// </summary>
internal sealed class ExFatScanner
{
    private const int MaxDepth = 256;
    private const long MaxDirectoryBytes = 256L * 1024 * 1024;
    private const long MaxTableBytes = 256L * 1024 * 1024;

    private readonly IBlockSource _volume;
    private readonly int _clusterSize;
    private readonly long _heapOffset;
    private readonly uint _clusterCount;
    private readonly long _fatOffset;
    private readonly byte[] _fat;
    private byte[] _bitmap = [];
    private readonly HashSet<uint> _directories = [];
    private readonly RecoveryVolume _result;
    private readonly bool _whole;

    private ExFatScanner(IBlockSource volume, byte[] boot, VolumeSlot slot)
    {
        _volume = volume;
        _whole = slot.WholeFileSystem;
        int sectorShift = boot[108], clusterShift = boot[109];
        if (sectorShift is < 9 or > 12 || clusterShift > 25 - sectorShift) throw new InvalidDataException("the exFAT boot sector has impossible sector or cluster sizes.");
        int sector = 1 << sectorShift;
        _clusterSize = sector << clusterShift;
        _fatOffset = (long)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(80)) * sector;
        long fatLength = (long)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(84)) * sector;
        _heapOffset = (long)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(88)) * sector;
        // Only the clusters within the volume exist: a damaged boot sector declaring more made the table, the allocation
        // bitmap and every folder read as large as their limits allow (release issue I37, as in FAT).
        long present = (volume.Length - _heapOffset) / _clusterSize;
        if (present <= 0) throw new InvalidDataException("the exFAT cluster heap lies beyond the end of the volume.");
        _clusterCount = (uint)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(92)), present);
        RootCluster = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(96));
        long fatBytes = Math.Min(fatLength, (_clusterCount + 2L) * 4);
        if (fatBytes > MaxTableBytes) throw new InvalidDataException("the allocation table is larger than FileCat reads.");
        _fat = volume.ReadExactly(_fatOffset, (int)fatBytes);
        _result = new RecoveryVolume
        {
            FileSystem = FileSystems.ExFat,
            Offset = slot.Offset,
            Length = slot.Length,
            ClusterSize = _clusterSize,
            Root = new RecoveryItem { Name = string.Empty, IsDirectory = true },
        };
    }

    private uint RootCluster { get; }

    public static RecoveryVolume Scan(IBlockSource volume, byte[] boot, VolumeSlot slot, CancellationToken ct)
    {
        var scanner = new ExFatScanner(volume, boot, slot);
        if (!scanner.Valid(scanner.RootCluster)) throw new InvalidDataException("the root folder's first cluster is outside the volume.");
        scanner._directories.Add(scanner.RootCluster);
        var root = scanner.ReadRun(scanner.Chain(scanner.RootCluster, MaxDirectoryBytes, contiguous: false), MaxDirectoryBytes);
        scanner.LoadBitmap(root);
        scanner.ParseDirectory(scanner._result.Root, root, insideDeleted: false, depth: 0, ct);
        return scanner._result;
    }

    private bool Valid(uint cluster) => cluster >= 2 && cluster < _clusterCount + 2L;

    private uint Next(uint cluster) => cluster * 4L + 3 < _fat.Length ? BinaryPrimitives.ReadUInt32LittleEndian(_fat.AsSpan((int)(cluster * 4))) : 0;

    private long ClusterOffset(uint cluster) => _heapOffset + (long)(cluster - 2) * _clusterSize;

    private bool Allocated(uint cluster)
    {
        long bit = cluster - 2L;
        return bit / 8 < _bitmap.Length && (_bitmap[bit / 8] & 1 << (int)(bit % 8)) != 0;
    }

    /// <summary>The clusters of a run: contiguous, or along the FAT. Stops at the end mark, a bad link, a loop, or the limit.</summary>
    private List<uint> Chain(uint first, long bytes, bool contiguous)
    {
        long count = Math.Max(1, (bytes + _clusterSize - 1) / _clusterSize);
        var clusters = new List<uint>();
        if (contiguous)
        {
            for (long i = 0; i < count && Valid((uint)(first + i)); i++) clusters.Add((uint)(first + i));
            return clusters;
        }
        var seen = new HashSet<uint>();
        for (uint c = first; Valid(c) && clusters.Count < count && seen.Add(c); c = Next(c))
        {
            clusters.Add(c);
            if (Next(c) == 0xFFFFFFFF) break;
        }
        return clusters;
    }

    private byte[] ReadRun(List<uint> clusters, long limit)
    {
        long total = Math.Min((long)clusters.Count * _clusterSize, limit);
        var data = new byte[total];
        for (int i = 0; (long)i * _clusterSize < total; i++)
        {
            int n = _volume.Read(ClusterOffset(clusters[i]), data.AsSpan(i * _clusterSize, (int)Math.Min(_clusterSize, total - (long)i * _clusterSize)));
            if (n <= 0) return data[..(i * _clusterSize)];
        }
        return data;
    }

    private void LoadBitmap(byte[] root)
    {
        for (int at = 0; at + 32 <= root.Length; at += 32)
        {
            if (root[at] == 0) break;
            if (root[at] == 0x81 && (root[at + 1] & 1) == 0)
            {
                uint first = BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(at + 20));
                long length = (long)BinaryPrimitives.ReadUInt64LittleEndian(root.AsSpan(at + 24));
                if (!Valid(first) || length <= 0 || length > MaxTableBytes || length < (_clusterCount + 7) / 8) break;
                _bitmap = ReadRun(Chain(first, length, contiguous: false), length);
                if (_bitmap.Length < (_clusterCount + 7) / 8) _bitmap = ReadRun(Chain(first, length, contiguous: true), length);
                return;
            }
            if (root[at] == 0x83 && root[at + 1] is > 0 and <= 11)
                _result.Label = Encoding.Unicode.GetString(root, at + 2, root[at + 1] * 2);
        }
        throw new InvalidDataException("the allocation bitmap is missing, so free and used space cannot be told apart.");
    }

    private void ParseDirectory(RecoveryItem folder, byte[] data, bool insideDeleted, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth)
        {
            _result.Warnings.Add("Folders are nested deeper than FileCat follows; deeper deleted items are not listed.");
            return;
        }
        for (int at = 0; at + 32 <= data.Length; at += 32)
        {
            ct.ThrowIfCancellationRequested();
            byte type = data[at];
            if (type == 0x00) break;
            if (type is 0x85 or 0x05 && Set(data, at) is { } set)
            {
                Handle(folder, set, insideDeleted || type == 0x05, depth, ct);
                at += set.Secondaries * 32;
            }
        }
    }

    private sealed record EntrySet(int Secondaries, string Name, ushort Attributes, bool NoFatChain, uint FirstCluster, long DataLength, long ValidLength,
        DateTime? Modified, DateTime? Created, bool ChecksumMatches);

    /// <summary>A file entry with its stream-extension and name entries, when they still belong together.</summary>
    private static EntrySet? Set(byte[] data, int at)
    {
        int secondaries = data[at + 1];
        if (secondaries < 2 || secondaries > 18 || at + (secondaries + 1) * 32 > data.Length) return null;
        bool deleted = data[at] == 0x05;
        byte stream = data[at + 32];
        if (stream != (deleted ? 0x40 : 0xC0)) return null;
        int nameLength = data[at + 32 + 3];
        if (nameLength == 0 || (nameLength + 14) / 15 > secondaries - 1) return null;
        var name = new StringBuilder(nameLength);
        for (int i = 0; i < secondaries - 1 && name.Length < nameLength; i++)
        {
            int e = at + 64 + i * 32;
            if (data[e] != (deleted ? 0x41 : 0xC1)) return null; // the set's slots were reused
            for (int c = 0; c < 15 && name.Length < nameLength; c++) name.Append((char)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(e + 2 + c * 2)));
        }
        var set = data.AsSpan(at, (secondaries + 1) * 32).ToArray();
        ushort stored = BinaryPrimitives.ReadUInt16LittleEndian(set.AsSpan(2));
        if (deleted)
            for (int i = 0; i < set.Length; i += 32) set[i] |= 0x80; // the checksum was taken while the entries were in use
        return new EntrySet(secondaries, name.ToString(), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4)),
            (data[at + 32 + 1] & 2) != 0, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 32 + 20)),
            (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at + 32 + 24)), (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at + 32 + 8)),
            Timestamp(data.AsSpan(at + 12), data[at + 21], data[at + 23]), Timestamp(data.AsSpan(at + 8), data[at + 20], data[at + 22]),
            Checksum(set) == stored);
    }

    private void Handle(RecoveryItem folder, EntrySet set, bool deleted, int depth, CancellationToken ct)
    {
        bool isDirectory = (set.Attributes & 0x10) != 0;
        if (!deleted)
        {
            if (!isDirectory)
            {
                if (_whole) Existing(folder, set);
                return;
            }
            if (!Valid(set.FirstCluster) || !_directories.Add(set.FirstCluster)) return;
            var existing = new RecoveryItem { Name = set.Name, IsDirectory = true, ModifiedUtc = set.Modified, CreatedUtc = set.Created };
            folder.Children.Add(existing);
            var data = ReadRun(Chain(set.FirstCluster, Math.Min(set.DataLength, MaxDirectoryBytes), set.NoFatChain), MaxDirectoryBytes);
            ParseDirectory(existing, data, insideDeleted: false, depth + 1, ct);
            return;
        }
        var item = new RecoveryItem
        {
            Name = set.Name,
            IsDirectory = isDirectory,
            IsDeleted = true,
            Size = isDirectory ? 0 : set.DataLength,
            ModifiedUtc = set.Modified,
            CreatedUtc = set.Created,
        };
        if (!set.ChecksumMatches) item.Evidence.Add("The entry's checksum does not match: its details may have been changed since.");
        folder.Children.Add(item);
        if (set.DataLength == 0 || !Valid(set.FirstCluster))
        {
            if (set.DataLength > 0) item.Evidence.Add("No valid first cluster is recorded, so the content cannot be located.");
            item.Classify(_clusterSize);
            return;
        }
        var clusters = Locate(set, item);
        if (isDirectory)
        {
            item.Classify(_clusterSize);
            if (clusters.Any(Allocated) || !_directories.Add(set.FirstCluster))
            {
                item.Evidence.Add("Its list of contents is gone: the space it used is in use by other data now.");
                return;
            }
            ParseDirectory(item, ReadRun(clusters, MaxDirectoryBytes), insideDeleted: true, depth + 1, ct);
            item.Evidence.Add(item.Children.Count > 0 ? "Its list of contents survives." : "Its list of contents survives but is empty.");
            return;
        }
        var extents = new List<Extent>();
        long remaining = set.DataLength;
        foreach (var cluster in clusters)
        {
            long length = Math.Min(remaining, _clusterSize);
            long offset = set.DataLength - remaining;
            // Bytes past the valid data length were never written: they read as zeros by definition.
            var state = offset >= set.ValidLength ? ExtentState.Zero : Allocated(cluster) ? ExtentState.InUse : ExtentState.Free;
            Append(extents, ClusterOffset(cluster), length, state);
            remaining -= length;
        }
        if (remaining > 0) Append(extents, 0, remaining, ExtentState.Unreadable);
        item.Extents = extents;
        item.Classify(_clusterSize);
    }

    /// <summary>An existing file of a lost file system: its clusters are its own, in one run or along the FAT.</summary>
    private void Existing(RecoveryItem folder, EntrySet set)
    {
        var item = new RecoveryItem { Name = set.Name, Size = set.DataLength, ModifiedUtc = set.Modified, CreatedUtc = set.Created };
        folder.Children.Add(item);
        if (set.DataLength == 0 || !Valid(set.FirstCluster))
        {
            if (set.DataLength > 0) item.Evidence.Add("No valid first cluster is recorded, so the content cannot be located.");
            item.Classify(_clusterSize);
            return;
        }
        var extents = new List<Extent>();
        long remaining = set.DataLength;
        foreach (var cluster in Chain(set.FirstCluster, set.DataLength, set.NoFatChain))
        {
            long length = Math.Min(remaining, _clusterSize);
            // Bytes past the valid data length were never written: they read as zeros by definition.
            Append(extents, ClusterOffset(cluster), length, set.DataLength - remaining >= set.ValidLength ? ExtentState.Zero : ExtentState.Owned);
            remaining -= length;
        }
        if (remaining > 0)
        {
            Append(extents, 0, remaining, ExtentState.Unreadable);
            item.Evidence.Add("Its clusters end before its size: the rest is missing.");
        }
        item.Extents = extents;
        item.Classify(_clusterSize);
    }

    /// <summary>
    /// Where a deleted file's clusters were: one run when exFAT recorded it that way; otherwise the FAT chain when it is
    /// still complete, or one run from the first cluster as a last resort (said in the evidence).
    /// </summary>
    private List<uint> Locate(EntrySet set, RecoveryItem item)
    {
        long needed = (set.DataLength + _clusterSize - 1) / _clusterSize;
        if (set.NoFatChain)
        {
            item.Evidence.Add("exFAT recorded that it was stored in one piece.");
            return Chain(set.FirstCluster, set.DataLength, contiguous: true);
        }
        var chain = Chain(set.FirstCluster, set.DataLength, contiguous: false);
        if (chain.Count == needed && Next(chain[^1]) == 0xFFFFFFFF)
        {
            item.Evidence.Add(chain.Zip(chain.Skip(1)).All(p => p.Second == p.First + 1)
                ? "The allocation table still lists its clusters."
                : $"The allocation table still lists its {chain.Count} clusters, stored in pieces.");
            return chain;
        }
        item.Evidence.Add("The allocation table no longer lists its pieces: FileCat reads it as one continuous run from its first cluster.");
        return Chain(set.FirstCluster, set.DataLength, contiguous: true);
    }

    private static void Append(List<Extent> extents, long offset, long length, ExtentState state)
    {
        if (extents.Count > 0 && extents[^1] is var last && last.State == state && last.Offset + last.Length == offset)
            extents[^1] = last with { Length = last.Length + length };
        else extents.Add(new Extent(offset, length, state));
    }

    internal static ushort Checksum(ReadOnlySpan<byte> set)
    {
        ushort sum = 0;
        for (int i = 0; i < set.Length; i++)
        {
            if (i is 2 or 3) continue;
            sum = (ushort)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + set[i]);
        }
        return sum;
    }

    /// <summary>An exFAT timestamp with its 10-millisecond increment and UTC offset (local time when no offset is recorded).</summary>
    internal static DateTime? Timestamp(ReadOnlySpan<byte> raw, byte tenMs, byte utcOffset)
    {
        uint t = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        int second = (int)(t & 0x1F) * 2, minute = (int)(t >> 5 & 0x3F), hour = (int)(t >> 11 & 0x1F);
        int day = (int)(t >> 16 & 0x1F), month = (int)(t >> 21 & 0x0F), year = 1980 + (int)(t >> 25);
        if (t == 0 || day == 0 || month is 0 or > 12 || hour > 23 || minute > 59 || second > 59 || day > DateTime.DaysInMonth(year, month)) return null;
        var local = new DateTime(year, month, day, hour, minute, second).AddMilliseconds(Math.Min((int)tenMs, 199) * 10);
        if ((utcOffset & 0x80) == 0) return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        int quarters = (sbyte)(byte)(utcOffset << 1) >> 1; // seven-bit two's complement
        return DateTime.SpecifyKind(local.AddMinutes(-15 * quarters), DateTimeKind.Utc);
    }
}
