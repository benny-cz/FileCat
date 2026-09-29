using System.Buffers.Binary;
using System.Text;

namespace FileCat.Recovery;

/// <summary>
/// NTFS: deleted items are MFT records whose in-use flag is clear. A record keeps its names, parent reference, times, and
/// data runs until it is reused, and small files keep their content inside the record itself. Clusters are judged
/// against the volume's $Bitmap. Parents are matched by record number and sequence number, so a folder whose record was
/// reused never adopts the wrong children; such items go to "Orphans". Compressed and EFS-encrypted content is listed but
/// not recovered. A lost file system (<see cref="VolumeSlot.WholeFileSystem"/>) lists its existing files too, with the
/// clusters they own.
/// </summary>
internal sealed class NtfsScanner
{
    private const long MaxRecords = 16_000_000;
    private const long MaxBitmapBytes = 512L * 1024 * 1024;
    private const int MaxAttributeListEntries = 4096;
    private const long RootRecord = 5;

    private readonly IBlockSource _volume;
    private readonly int _sectorSize;
    private readonly long _clusterSize;
    private readonly int _recordSize;
    private readonly long _totalClusters;
    private List<(long Vcn, long Lcn, long Length)> _mftRuns = [];
    private byte[] _bitmap = [];
    private readonly RecoveryVolume _result;
    private readonly bool _whole;

    private sealed record Parsed(long Number, ushort Sequence, bool InUse, bool IsDirectory, string? Name, long ParentRecord, ushort ParentSequence,
        DateTime? Modified, DateTime? Created, Data? Stream, int NamedStreams);

    /// <summary>The unnamed $DATA stream: resident bytes, or runs over clusters (compressed in units of <see cref="UnitClusters"/>).</summary>
    private sealed record Data(byte[]? Resident, List<(long Vcn, long Lcn, long Length)> Runs, long Size, long InitializedSize, bool Compressed, bool Encrypted,
        bool RunsComplete, int UnitClusters);

    private NtfsScanner(IBlockSource volume, byte[] boot, VolumeSlot slot)
    {
        _volume = volume;
        _whole = slot.WholeFileSystem;
        _sectorSize = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int perCluster = boot[13];
        if (_sectorSize is not (512 or 1024 or 2048 or 4096)) throw new InvalidDataException("the NTFS boot sector has an impossible sector size.");
        _clusterSize = (long)_sectorSize * (perCluster <= 0x80 ? perCluster : 1 << (256 - perCluster));
        if (_clusterSize is <= 0 or > 2 * 1024 * 1024) throw new InvalidDataException("the NTFS boot sector has an impossible cluster size.");
        long totalSectors = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(40));
        _totalClusters = totalSectors * _sectorSize / _clusterSize;
        MftCluster = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(48));
        sbyte perRecord = (sbyte)boot[64];
        _recordSize = perRecord > 0 ? (int)(perRecord * _clusterSize) : 1 << -perRecord;
        if (_recordSize is < 256 or > 65536 || MftCluster <= 0 || MftCluster >= _totalClusters) throw new InvalidDataException("the NTFS boot sector points outside the volume.");
        _result = new RecoveryVolume
        {
            FileSystem = FileSystems.Ntfs,
            Offset = slot.Offset,
            Length = slot.Length,
            ClusterSize = (int)_clusterSize,
            Root = new RecoveryItem { Name = string.Empty, IsDirectory = true },
        };
    }

    private long MftCluster { get; }

    public static RecoveryVolume Scan(IBlockSource volume, byte[] boot, VolumeSlot slot, CancellationToken ct) => new NtfsScanner(volume, boot, slot).Run(ct);

    private RecoveryVolume Run(CancellationToken ct)
    {
        // $MFT describes where the MFT itself lies; its first record sits at the cluster the boot sector names.
        var mftRecord = ReadRecordAt(MftCluster * _clusterSize) ?? throw new InvalidDataException("the MFT's own record is damaged.");
        _mftRuns = [(0, MftCluster, Math.Max(1, (_recordSize + _clusterSize - 1) / _clusterSize) * 16)];
        var mft = Parse(0, mftRecord, followLists: true) ?? throw new InvalidDataException("the MFT's own record is damaged.");
        if (mft.Stream is not { Resident: null } stream || stream.Runs.Count == 0) throw new InvalidDataException("the MFT's own data runs are missing.");
        _mftRuns = stream.Runs;
        long records = Math.Min(stream.Size / _recordSize, MaxRecords);
        if (Record(6) is { } bitmapRecord && Parse(6, bitmapRecord, followLists: true)?.Stream is { Resident: null } bitmap)
            _bitmap = ReadStream(bitmap, MaxBitmapBytes);
        if (_bitmap.Length < _totalClusters / 8) _result.Warnings.Add("The volume's $Bitmap could not be read completely; unread clusters count as in use.");
        if (Record(3) is { } volumeRecord) _result.Label = VolumeName(volumeRecord);

        var directories = new Dictionary<long, Parsed>();
        var listed = new List<Parsed>(); // deleted items; for a lost file system, existing files too
        for (long n = 0; n < records; n++)
        {
            if ((n & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (n < 16 && n != RootRecord) continue; // metadata files
            var raw = Record(n);
            if (raw is null || BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(32)) != 0) continue; // unreadable, or an extension record
            var parsed = Parse(n, raw, followLists: true);
            if (parsed is null || parsed.Name is null && n != RootRecord) continue;
            if (parsed.IsDirectory) directories[n] = parsed;
            if (!parsed.InUse || _whole && !parsed.IsDirectory) listed.Add(parsed);
        }

        var nodes = new Dictionary<long, RecoveryItem> { [RootRecord] = _result.Root };
        RecoveryItem? orphans = null;
        RecoveryItem Folder(Parsed item, int depth)
        {
            // The parent must be the folder the item was in: same record, and the sequence it had then.
            if (depth > 256 || !directories.TryGetValue(item.ParentRecord, out var parent) || !SameGeneration(parent, item.ParentSequence) || parent.Number == item.Number)
                return orphans ??= new RecoveryItem { Name = "Orphans", IsDirectory = true };
            if (nodes.TryGetValue(parent.Number, out var existing)) return existing;
            var node = new RecoveryItem
            {
                Name = parent.Name ?? $"Record {parent.Number}",
                IsDirectory = true,
                IsDeleted = !parent.InUse,
                ModifiedUtc = parent.Modified,
                CreatedUtc = parent.Created,
                RecordNumber = parent.Number,
            };
            if (node.IsDeleted) node.Evidence.Add("Its record survives; FileCat lists what it held from the records that point to it.");
            nodes[parent.Number] = node;
            Folder(parent, depth + 1).Children.Add(node);
            return node;
        }

        foreach (var item in listed)
        {
            ct.ThrowIfCancellationRequested();
            if (item.IsDirectory)
            {
                if (!nodes.ContainsKey(item.Number))
                {
                    var node = new RecoveryItem { Name = item.Name!, IsDirectory = true, IsDeleted = true, ModifiedUtc = item.Modified, CreatedUtc = item.Created, RecordNumber = item.Number };
                    node.Evidence.Add("Its record survives; FileCat lists what it held from the records that point to it.");
                    nodes[item.Number] = node;
                    Folder(item, 0).Children.Add(node);
                }
                continue;
            }
            var file = new RecoveryItem
            {
                Name = item.Name!,
                IsDeleted = !item.InUse,
                Size = item.Stream?.Size ?? 0,
                ModifiedUtc = item.Modified,
                CreatedUtc = item.Created,
                RecordNumber = item.Number,
                Resident = item.Stream?.Resident,
            };
            Describe(file, item);
            Folder(item, 0).Children.Add(file);
        }
        if (orphans is not null)
        {
            orphans.Evidence.Add("Deleted items whose folder is unknown: its record was reused or is damaged.");
            _result.Orphans = orphans;
            _result.Root.Children.Add(orphans);
        }
        return _result;
    }

    /// <summary>A deleted directory's sequence number is one higher than while it held its children (NTFS counts on free).</summary>
    private static bool SameGeneration(Parsed parent, ushort expected) =>
        parent.Sequence == expected || !parent.InUse && (parent.Sequence == (ushort)(expected + 1) || expected == ushort.MaxValue && parent.Sequence == 1);

    private void Describe(RecoveryItem file, Parsed item)
    {
        var stream = item.Stream;
        if (item.NamedStreams > 0) file.Evidence.Add($"It also had {item.NamedStreams} alternate data stream(s), which are not recovered.");
        if (stream is null)
        {
            file.Evidence.Add("Its record has no data stream.");
            file.Classify(_clusterSize);
            return;
        }
        if (stream.Encrypted)
        {
            file.Evidence.Add("It was encrypted with EFS: its content cannot be read without the owner's keys.");
            file.State = RecoveryState.NameOnly;
            return;
        }
        if (stream.Compressed)
        {
            DescribeCompressed(file, stream, item.InUse);
            return;
        }
        if (stream.Resident is not null)
        {
            file.Evidence.Add("Its content is kept inside its MFT record, which has not been reused.");
            file.Classify(_clusterSize);
            return;
        }
        if (!stream.RunsComplete) file.Evidence.Add("Its record no longer lists all of its pieces; the missing part comes back as zeros.");
        var extents = new List<Extent>();
        long remaining = stream.Size;
        long vcn = 0;
        foreach (var (runVcn, lcn, length) in stream.Runs.OrderBy(r => r.Vcn))
        {
            if (remaining <= 0) break;
            if (runVcn > vcn) Add(extents, 0, Math.Min(remaining, (runVcn - vcn) * _clusterSize), ExtentState.Unreadable, ref remaining);
            for (long c = 0; c < length && remaining > 0; c++)
            {
                long bytes = Math.Min(remaining, _clusterSize);
                long offsetInFile = stream.Size - remaining;
                var state = lcn < 0 || offsetInFile >= stream.InitializedSize ? ExtentState.Zero // sparse, or never written
                    : lcn + c >= _totalClusters ? ExtentState.Unreadable
                    : Allocated(lcn + c) ? item.InUse ? ExtentState.Owned : ExtentState.InUse : ExtentState.Free;
                Add(extents, lcn < 0 ? 0 : (lcn + c) * _clusterSize, bytes, state, ref remaining);
            }
            vcn = runVcn + length;
        }
        if (remaining > 0) Add(extents, 0, remaining, ExtentState.Unreadable, ref remaining);
        file.Extents = extents;
        if (extents.Count(e => e.State is ExtentState.Free or ExtentState.InUse) > 1 && stream.Runs.Count > 1)
            file.Evidence.Add($"Its record lists its {stream.Runs.Count} pieces.");
        file.Classify(_clusterSize);
    }

    /// <summary>
    /// A compressed stream, unit by unit: a unit whose stored clusters are all free comes back (decompressed when it was
    /// compressed); a unit with any cluster in use by other data now is lost as a whole, since compressed data cannot be
    /// read in part.
    /// </summary>
    private void DescribeCompressed(RecoveryItem file, Data stream, bool owned)
    {
        if (stream.UnitClusters == 0 || stream.Resident is not null)
        {
            file.Evidence.Add("NTFS stored it compressed, but its compression unit is not recorded.");
            file.State = RecoveryState.NameOnly;
            return;
        }
        long unitBytes = stream.UnitClusters * _clusterSize;
        long count = (stream.Size + unitBytes - 1) / unitBytes;
        if (count > 10_000_000 || unitBytes > int.MaxValue / 2)
        {
            file.Evidence.Add("Its compressed layout is larger than FileCat reads.");
            file.State = RecoveryState.NameOnly;
            return;
        }
        var runs = stream.Runs.OrderBy(r => r.Vcn).ToList();
        var units = new List<CompressedUnit>();
        var extents = new List<Extent>();
        for (long u = 0; u < count; u++)
        {
            long first = u * stream.UnitClusters, end = first + stream.UnitClusters;
            var pieces = new List<(long, long)>();
            long real = 0;
            bool lost = false, known = true;
            for (long vcn = first; vcn < end;)
            {
                var run = runs.FirstOrDefault(r => vcn >= r.Vcn && vcn < r.Vcn + r.Length);
                if (run.Length == 0)
                {
                    known = vcn * _clusterSize >= stream.Size; // clusters past the end need no run
                    break;
                }
                long take = Math.Min(end, run.Vcn + run.Length) - vcn;
                if (run.Lcn >= 0)
                {
                    long lcn = run.Lcn + (vcn - run.Vcn);
                    pieces.Add((lcn * _clusterSize, take * _clusterSize));
                    real += take;
                    for (long c = 0; c < take && !lost; c++) lost = lcn + c >= _totalClusters || !owned && Allocated(lcn + c);
                }
                vcn += take;
            }
            var kind = real == 0 ? CompressedUnitKind.Sparse : real >= stream.UnitClusters ? CompressedUnitKind.Raw : CompressedUnitKind.Compressed;
            lost |= !known;
            units.Add(new CompressedUnit(pieces, kind, lost));
            long length = Math.Min(unitBytes, stream.Size - u * unitBytes);
            var state = lost ? ExtentState.InUse : kind == CompressedUnitKind.Sparse ? ExtentState.Zero : owned ? ExtentState.Owned : ExtentState.Free;
            if (extents.Count > 0 && extents[^1].State == state) extents[^1] = extents[^1] with { Length = extents[^1].Length + length };
            else extents.Add(new Extent(0, length, state));
        }
        file.Compression = new CompressedLayout((int)unitBytes, units);
        file.Extents = extents;
        file.Evidence.Add($"NTFS stored it compressed in {units.Count} unit{(units.Count == 1 ? "" : "s")} of {RecoveryItem.Bytes(unitBytes)}; FileCat decompresses them (LZNT1).");
        file.Classify(_clusterSize);
    }

    private static void Add(List<Extent> extents, long offset, long length, ExtentState state, ref long remaining)
    {
        remaining -= length;
        if (extents.Count > 0 && extents[^1] is var last && last.State == state && (state is ExtentState.Zero or ExtentState.Unreadable || last.Offset + last.Length == offset))
            extents[^1] = last with { Length = last.Length + length };
        else extents.Add(new Extent(offset, length, state));
    }

    private bool Allocated(long cluster) => cluster / 8 >= _bitmap.Length || (_bitmap[cluster / 8] & 1 << (int)(cluster % 8)) != 0;

    // ---- Records ------------------------------------------------------------------------------------------

    /// <summary>MFT record <paramref name="number"/> with its fixups applied; null when unreadable or not a record.</summary>
    private byte[]? Record(long number)
    {
        long offset = number * _recordSize;
        long vcn = offset / _clusterSize;
        foreach (var (runVcn, lcn, length) in _mftRuns)
        {
            if (vcn < runVcn || vcn >= runVcn + length || lcn < 0) continue;
            return ReadRecordAt(lcn * _clusterSize + (offset - runVcn * _clusterSize));
        }
        return null;
    }

    private byte[]? ReadRecordAt(long offset)
    {
        byte[] raw;
        try { raw = _volume.ReadExactly(offset, _recordSize); }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { return null; }
        if (!raw.AsSpan(0, 4).SequenceEqual("FILE"u8)) return null;
        // Fixups: the last two bytes of every sector were swapped for the update sequence number when it was written.
        int usa = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(6));
        if (count < 2 || usa + count * 2 > raw.Length || (count - 1) * 512 > raw.Length) return null;
        ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(usa));
        for (int i = 1; i < count; i++)
        {
            int at = i * 512 - 2;
            if (BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(at)) != usn) return null; // a torn or damaged record
            raw[at] = raw[usa + i * 2];
            raw[at + 1] = raw[usa + i * 2 + 1];
        }
        return raw;
    }

    private Parsed? Parse(long number, byte[] record, bool followLists)
    {
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(22));
        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(16));
        string? name = null;
        int bestNamespace = -1;
        long parent = 0;
        ushort parentSequence = 0;
        DateTime? modified = null, created = null;
        Data? data = null;
        int named = 0;
        var pieces = new List<(long Vcn, long Lcn, long Length)>();
        long size = 0, initialized = 0, lastVcn = -1;
        int unitClusters = 0;
        bool compressed = false, encrypted = false, hasNonResident = false;
        byte[]? resident = null;
        List<long>? extensions = null;

        foreach (var (type, attr) in Attributes(record))
        {
            switch (type)
            {
                case 0x10 when attr[8] == 0:
                    var info = ResidentValue(attr);
                    if (info.Length >= 16)
                    {
                        created = FileTime(BinaryPrimitives.ReadInt64LittleEndian(info));
                        modified = FileTime(BinaryPrimitives.ReadInt64LittleEndian(info[8..]));
                    }
                    break;
                case 0x20 when followLists:
                    extensions = AttributeListRecords(attr, number);
                    break;
                case 0x30 when attr[8] == 0:
                    var fn = ResidentValue(attr);
                    if (fn.Length < 66) break;
                    int nameSpace = fn[65];
                    int rank = nameSpace switch { 1 or 3 => 3, 0 => 2, _ => 1 }; // Win32 names over POSIX over DOS 8.3 aliases
                    if (rank <= bestNamespace || 66 + fn[64] * 2 > fn.Length) break;
                    bestNamespace = rank;
                    name = Encoding.Unicode.GetString(fn.Slice(66, fn[64] * 2));
                    ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(fn);
                    parent = (long)(reference & 0xFFFFFFFFFFFF);
                    parentSequence = (ushort)(reference >> 48);
                    modified ??= FileTime(BinaryPrimitives.ReadInt64LittleEndian(fn[16..]));
                    break;
                case 0x80:
                    if (attr[9] != 0)
                    {
                        named++;
                        break;
                    }
                    ushort attrFlags = BinaryPrimitives.ReadUInt16LittleEndian(attr.AsSpan(12));
                    compressed |= (attrFlags & 0x00FF) != 0;
                    encrypted |= (attrFlags & 0x4000) != 0;
                    if (attr[8] == 0)
                    {
                        resident = ResidentValue(attr).ToArray();
                        size = resident.Length;
                        initialized = resident.Length;
                    }
                    else
                    {
                        hasNonResident = true;
                        // Compression unit: 2^n clusters, in the first instance of the attribute.
                        if (attr.Length > 34 && BinaryPrimitives.ReadInt64LittleEndian(attr.AsSpan(16)) == 0 && attr[34] is > 0 and <= 8) unitClusters = 1 << attr[34];
                        AddRuns(attr, pieces, ref size, ref initialized, ref lastVcn);
                    }
                    break;
            }
        }
        // Very fragmented files keep further data runs in extension records named by $ATTRIBUTE_LIST.
        if (extensions is not null)
        {
            foreach (var ext in extensions)
            {
                if (Record(ext) is not { } extRecord || (long)(BinaryPrimitives.ReadUInt64LittleEndian(extRecord.AsSpan(32)) & 0xFFFFFFFFFFFF) != number) continue;
                foreach (var (type, attr) in Attributes(extRecord))
                {
                    if (type != 0x80 || attr[9] != 0 || attr[8] == 0) continue;
                    hasNonResident = true;
                    AddRuns(attr, pieces, ref size, ref initialized, ref lastVcn);
                }
            }
        }
        if (resident is not null || hasNonResident)
        {
            long needed = (size + _clusterSize - 1) / _clusterSize;
            data = new Data(hasNonResident ? null : resident, pieces, size, initialized, compressed, encrypted, !hasNonResident || lastVcn + 1 >= needed, unitClusters);
        }
        return new Parsed(number, sequence, (flags & 1) != 0, (flags & 2) != 0, name, parent, parentSequence, modified, created, data, named);
    }

    private void AddRuns(byte[] attr, List<(long Vcn, long Lcn, long Length)> pieces, ref long size, ref long initialized, ref long lastVcn)
    {
        if (attr.Length < 64) return;
        long startVcn = BinaryPrimitives.ReadInt64LittleEndian(attr.AsSpan(16));
        lastVcn = Math.Max(lastVcn, BinaryPrimitives.ReadInt64LittleEndian(attr.AsSpan(24)));
        if (startVcn == 0)
        {
            size = BinaryPrimitives.ReadInt64LittleEndian(attr.AsSpan(48));
            initialized = BinaryPrimitives.ReadInt64LittleEndian(attr.AsSpan(56));
        }
        int at = BinaryPrimitives.ReadUInt16LittleEndian(attr.AsSpan(32));
        long vcn = startVcn, lcn = 0;
        while (at < attr.Length && attr[at] != 0 && pieces.Count < 1_000_000)
        {
            int lengthBytes = attr[at] & 0x0F, offsetBytes = attr[at] >> 4;
            if (lengthBytes is 0 or > 8 || offsetBytes > 8 || at + 1 + lengthBytes + offsetBytes > attr.Length) break;
            long length = (long)ReadUnsigned(attr.AsSpan(at + 1, lengthBytes));
            if (length <= 0) break;
            if (offsetBytes == 0)
            {
                pieces.Add((vcn, -1, length)); // sparse: no clusters, reads as zeros
            }
            else
            {
                lcn += ReadSigned(attr.AsSpan(at + 1 + lengthBytes, offsetBytes));
                if (lcn < 0 || lcn + length > _totalClusters) break;
                pieces.Add((vcn, lcn, length));
            }
            vcn += length;
            at += 1 + lengthBytes + offsetBytes;
        }
    }

    private static List<long> AttributeListRecords(byte[] attr, long self)
    {
        var records = new List<long>();
        if (attr[8] != 0) return records; // a non-resident list: rare, left out
        var list = ResidentValue(attr);
        for (int at = 0, n = 0; at + 26 <= list.Length && n < MaxAttributeListEntries; n++)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(list[at..]);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(list[(at + 4)..]);
            if (length < 26) break;
            long record = (long)(BinaryPrimitives.ReadUInt64LittleEndian(list[(at + 16)..]) & 0xFFFFFFFFFFFF);
            if (type == 0x80 && record != self && !records.Contains(record)) records.Add(record);
            at += length;
        }
        return records;
    }

    /// <summary>A resident attribute's value, or nothing when its recorded offset or length does not fit the attribute.</summary>
    private static ReadOnlySpan<byte> ResidentValue(byte[] attr)
    {
        if (attr.Length < 24) return default;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(attr.AsSpan(16));
        int at = BinaryPrimitives.ReadUInt16LittleEndian(attr.AsSpan(20));
        return at <= attr.Length && length <= (uint)(attr.Length - at) ? attr.AsSpan(at, (int)length) : default;
    }

    private static IEnumerable<(uint Type, byte[] Attribute)> Attributes(byte[] record)
    {
        int at = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(20));
        int used = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(24)), (uint)record.Length);
        for (int n = 0; at + 16 <= used && n < 512; n++)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(at));
            if (type == 0xFFFFFFFF) yield break;
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(at + 4));
            if (length < 16 || at + length > used) yield break;
            yield return (type, record.AsSpan(at, length).ToArray());
            at += length;
        }
    }

    private byte[] ReadStream(Data stream, long limit)
    {
        long size = Math.Min(stream.Size, limit);
        var data = new byte[size];
        foreach (var (vcn, lcn, length) in stream.Runs)
        {
            long start = vcn * _clusterSize;
            if (start >= size || lcn < 0) continue;
            int bytes = (int)Math.Min(length * _clusterSize, size - start);
            int done = 0;
            while (done < bytes)
            {
                int n = _volume.Read(lcn * _clusterSize + done, data.AsSpan((int)start + done, bytes - done));
                if (n <= 0) break;
                done += n;
            }
        }
        return data;
    }

    private static string? VolumeName(byte[] record)
    {
        foreach (var (type, attr) in Attributes(record))
        {
            if (type != 0x60 || attr[8] != 0) continue;
            var value = ResidentValue(attr);
            if (value.Length > 0) return Encoding.Unicode.GetString(value);
        }
        return null;
    }

    private static ulong ReadUnsigned(ReadOnlySpan<byte> bytes)
    {
        ulong v = 0;
        for (int i = bytes.Length - 1; i >= 0; i--) v = v << 8 | bytes[i];
        return v;
    }

    private static long ReadSigned(ReadOnlySpan<byte> bytes)
    {
        long v = (long)ReadUnsigned(bytes);
        int bits = bytes.Length * 8;
        return bits < 64 && (v & 1L << (bits - 1)) != 0 ? v - (1L << bits) : v;
    }

    private static DateTime? FileTime(long value) =>
        value is > 0 and < 0x7FFFFFFFFFFFFFFF && value < DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(value) : null;
}
