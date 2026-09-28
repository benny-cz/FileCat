using System.Buffers.Binary;
using System.Text;

namespace FileCat.Recovery;

/// <summary>
/// FAT12, FAT16, and FAT32 (Microsoft FAT specification). A deleted entry keeps its size and first cluster, but its first
/// short-name byte becomes 0xE5 and its cluster chain is freed, so FAT no longer says where a deleted file's other pieces
/// were: FileCat reads a deleted file as one run from its first cluster and says so. Long names come from the long-name
/// entries in front of the short one; their checksum also restores the short name's lost first letter.
/// </summary>
internal sealed class FatScanner
{
    private const int MaxDepth = 256;
    private const int MaxDirectoryClusters = 65536;
    private const long MaxFatBytes = 64L * 1024 * 1024; // 16 million clusters

    private readonly IBlockSource _volume;
    private readonly int _bits;
    private readonly int _clusterSize;
    private readonly long _dataOffset;
    private readonly uint _clusterCount;
    private readonly uint[] _fat;
    private readonly HashSet<uint> _directories = [];
    private readonly RecoveryVolume _result;
    private string? _label;

    private FatScanner(IBlockSource volume, byte[] boot, VolumeSlot slot)
    {
        _volume = volume;
        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int sectorsPerCluster = boot[13];
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
        int fats = boot[16];
        int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
        long total = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19)) is var t16 and > 0 ? t16 : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32));
        long fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22)) is var f16 and > 0 ? f16 : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36));
        long rootSectors = (rootEntries * 32L + bytesPerSector - 1) / bytesPerSector;
        long dataSectors = total - (reserved + fats * fatSectors + rootSectors);
        if (dataSectors <= 0) throw new InvalidDataException("the FAT boot sector describes no data area.");
        _clusterSize = bytesPerSector * sectorsPerCluster;
        _clusterCount = (uint)Math.Min(dataSectors / sectorsPerCluster, 0x0FFFFFF5);
        _bits = _clusterCount < 4085 ? 12 : _clusterCount < 65525 ? 16 : 32;
        long fatOffset = (long)reserved * bytesPerSector;
        RootOffset = fatOffset + fats * fatSectors * bytesPerSector;
        RootBytes = (int)(rootSectors * bytesPerSector);
        _dataOffset = RootOffset + RootBytes;
        RootCluster = _bits == 32 ? BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(44)) : 0;
        int labelAt = _bits == 32 ? 71 : 43;
        if (boot[_bits == 32 ? 66 : 38] == 0x29) _label = Ascii(boot.AsSpan(labelAt, 11)).Trim() is { Length: > 0 } l && l != "NO NAME" ? l : null;

        long fatBytes = Math.Min(fatSectors * bytesPerSector, (_clusterCount + 2L) * _bits / 8 + 2);
        if (fatBytes > MaxFatBytes) throw new InvalidDataException("the allocation table is larger than FileCat reads.");
        var table = volume.ReadExactly(fatOffset, (int)fatBytes);
        _fat = new uint[_clusterCount + 2];
        for (uint c = 0; c < _fat.Length; c++) _fat[c] = Entry(table, c);
        _result = new RecoveryVolume
        {
            FileSystem = "FAT" + _bits,
            Offset = slot.Offset,
            Length = slot.Length,
            ClusterSize = _clusterSize,
            Root = new RecoveryItem { Name = string.Empty, IsDirectory = true },
        };
    }

    private long RootOffset { get; }
    private int RootBytes { get; }
    private uint RootCluster { get; }

    public static RecoveryVolume Scan(IBlockSource volume, byte[] boot, VolumeSlot slot, CancellationToken ct)
    {
        var scanner = new FatScanner(volume, boot, slot);
        byte[] root;
        if (scanner._bits == 32)
        {
            scanner._directories.Add(scanner.RootCluster);
            root = scanner.ReadChain(scanner.RootCluster, MaxDirectoryClusters);
        }
        else
        {
            root = volume.ReadExactly(scanner.RootOffset, scanner.RootBytes);
        }
        scanner.ParseDirectory(scanner._result.Root, root, insideDeleted: false, depth: 0, ct);
        scanner._result.Label = scanner._label;
        return scanner._result;
    }

    private uint Entry(byte[] table, uint cluster)
    {
        switch (_bits)
        {
            case 12:
                int at = (int)(cluster * 3 / 2);
                if (at + 1 >= table.Length) return 0;
                int v = table[at] | table[at + 1] << 8;
                return (uint)((cluster & 1) != 0 ? v >> 4 : v & 0xFFF);
            case 16:
                return cluster * 2 + 1 < table.Length ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan((int)cluster * 2)) : 0u;
            default:
                return cluster * 4 + 3 < table.Length ? BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan((int)cluster * 4)) & 0x0FFFFFFF : 0u;
        }
    }

    private bool Valid(uint cluster) => cluster >= 2 && cluster < _clusterCount + 2;

    private bool IsEnd(uint value) => value >= (_bits switch { 12 => 0xFF8u, 16 => 0xFFF8u, _ => 0x0FFFFFF8u });

    private long ClusterOffset(uint cluster) => _dataOffset + (long)(cluster - 2) * _clusterSize;

    /// <summary>An allocated chain, as the table records it; stops at the end mark, a bad link, or a loop.</summary>
    private byte[] ReadChain(uint start, int maxClusters)
    {
        var clusters = new List<uint>();
        var seen = new HashSet<uint>();
        for (uint c = start; Valid(c) && clusters.Count < maxClusters && seen.Add(c); c = _fat[c])
        {
            clusters.Add(c);
            if (IsEnd(_fat[c]) || _fat[c] == 0) break;
        }
        var data = new byte[clusters.Count * _clusterSize];
        for (int i = 0; i < clusters.Count; i++)
        {
            int n = _volume.Read(ClusterOffset(clusters[i]), data.AsSpan(i * _clusterSize, _clusterSize));
            if (n < _clusterSize) return data[..(i * _clusterSize + Math.Max(0, n))];
        }
        return data;
    }

    private void ParseDirectory(RecoveryItem folder, byte[] data, bool insideDeleted, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth)
        {
            _result.Warnings.Add("Folders are nested deeper than FileCat follows; deeper deleted items are not listed.");
            return;
        }
        var longName = new List<byte[]>();
        for (int at = 0; at + 32 <= data.Length; at += 32)
        {
            ct.ThrowIfCancellationRequested();
            var e = data.AsSpan(at, 32);
            if (e[0] == 0x00) break; // the end of the directory
            byte attributes = e[11];
            if ((attributes & 0x3F) == 0x0F)
            {
                longName.Add(e.ToArray());
                continue;
            }
            var name = e[..11];
            bool deleted = e[0] == 0xE5 || insideDeleted;
            if (name[0] == (byte)'.' || (attributes & 0x08) != 0 && (attributes & 0x10) == 0)
            {
                // "." and "..", and the volume label (the label entry is what Windows shows).
                if ((attributes & 0x08) != 0 && e[0] != 0xE5 && depth == 0) _label = Ascii(name).Trim();
                longName.Clear();
                continue;
            }
            var (text, uncertain) = Name(name, e[12], longName);
            longName.Clear();
            uint start = BinaryPrimitives.ReadUInt16LittleEndian(e[26..]) | (_bits == 32 ? (uint)BinaryPrimitives.ReadUInt16LittleEndian(e[20..]) << 16 : 0);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(e[28..]);
            bool isDirectory = (attributes & 0x10) != 0;
            var modified = DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e[24..]), BinaryPrimitives.ReadUInt16LittleEndian(e[22..]), 0);
            var created = DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e[16..]), BinaryPrimitives.ReadUInt16LittleEndian(e[14..]), e[13]);
            if (!deleted)
            {
                if (!isDirectory || !Valid(start) || !_directories.Add(start)) continue;
                var existing = new RecoveryItem { Name = text, IsDirectory = true, ModifiedUtc = modified, CreatedUtc = created };
                folder.Children.Add(existing);
                ParseDirectory(existing, ReadChain(start, MaxDirectoryClusters), insideDeleted: false, depth + 1, ct);
                continue;
            }
            var item = new RecoveryItem
            {
                Name = text,
                IsDirectory = isDirectory,
                IsDeleted = true,
                Size = isDirectory ? 0 : size,
                ModifiedUtc = modified,
                CreatedUtc = created,
                NameUncertain = uncertain,
            };
            if (uncertain) item.Evidence.Add("The first letter of the name is lost (FAT overwrites it when a file is deleted); it is shown as _.");
            folder.Children.Add(item);
            if (isDirectory) DeletedDirectory(item, start, depth, ct);
            else Locate(item, start, size);
        }
    }

    /// <summary>A deleted file's content, read as one run from its first cluster (FAT forgets the rest of the chain).</summary>
    private void Locate(RecoveryItem item, uint start, uint size)
    {
        if (size == 0)
        {
            item.Classify(_clusterSize);
            return;
        }
        if (!Valid(start))
        {
            item.Evidence.Add("No valid first cluster is recorded, so the content cannot be located.");
            item.Classify(_clusterSize);
            return;
        }
        long needed = (size + (long)_clusterSize - 1) / _clusterSize;
        var extents = new List<Extent>();
        long remaining = size;
        for (long i = 0; i < needed; i++)
        {
            uint cluster = (uint)(start + i);
            long length = Math.Min(remaining, _clusterSize);
            var state = !Valid(cluster) ? ExtentState.Unreadable : _fat[cluster] == 0 ? ExtentState.Free : ExtentState.InUse;
            Append(extents, ClusterOffset(cluster), length, state);
            remaining -= length;
        }
        item.Extents = extents;
        if (needed > 1) item.Evidence.Add("FAT keeps no list of a deleted file's pieces: FileCat reads it as one continuous run from its first cluster.");
        if (extents.Any(e => e.State == ExtentState.InUse) && needed > 1 && extents[0].State == ExtentState.Free)
            item.Evidence.Add("Either the file was stored in pieces, or other data has taken part of its space since.");
        item.Classify(_clusterSize);
    }

    private void DeletedDirectory(RecoveryItem item, uint start, int depth, CancellationToken ct)
    {
        item.Classify(_clusterSize);
        if (!Valid(start) || _fat[start] != 0)
        {
            item.Evidence.Add("Its list of contents is gone: the space it used is in use by other data now.");
            return;
        }
        // A deleted folder's first cluster must still read as that folder: "." pointing to itself.
        var first = new byte[_clusterSize];
        if (_volume.Read(ClusterOffset(start), first) < _clusterSize || !IsDotEntry(first.AsSpan(0, 32), ".") || !IsDotEntry(first.AsSpan(32, 32), "..") ||
            (BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(26)) | (_bits == 32 ? (uint)BinaryPrimitives.ReadUInt16LittleEndian(first.AsSpan(20)) << 16 : 0)) != start)
        {
            item.Evidence.Add("Its list of contents has been overwritten.");
            return;
        }
        if (!_directories.Add(start)) return;
        // Following clusters belong to it while they are free and still hold entries (a folder rarely spans many).
        var data = new List<byte>(first);
        for (uint c = start + 1; Valid(c) && _fat[c] == 0 && data.Count / _clusterSize < MaxDirectoryClusters; c++)
        {
            if (data.Count >= 32 && Terminated(data)) break;
            var next = new byte[_clusterSize];
            if (_volume.Read(ClusterOffset(c), next) < _clusterSize || !LooksLikeEntries(next)) break;
            data.AddRange(next);
        }
        ParseDirectory(item, data.ToArray(), insideDeleted: true, depth + 1, ct);
        item.Evidence.Add(item.Children.Count > 0 ? "Its list of contents survives." : "Its list of contents survives but is empty.");
    }

    private static bool Terminated(List<byte> data)
    {
        for (int at = 0; at + 32 <= data.Count; at += 32)
            if (data[at] == 0) return true;
        return false;
    }

    private static bool LooksLikeEntries(byte[] cluster)
    {
        // Entries start with a name byte or a deletion mark and have sensible attributes; anything else is file data.
        for (int at = 0; at + 32 <= cluster.Length; at += 32)
        {
            byte b = cluster[at];
            if (b == 0) return at > 0;
            byte attributes = cluster[at + 11];
            if ((attributes & 0xC0) != 0) return false;
            if (b < 0x20 && b != 0x05) return false;
        }
        return true;
    }

    private static bool IsDotEntry(ReadOnlySpan<byte> e, string dots) =>
        (e[11] & 0x10) != 0 && e[..dots.Length].SequenceEqual(Encoding.ASCII.GetBytes(dots)) && e[dots.Length..11].IndexOfAnyExcept((byte)' ') < 0;

    private static void Append(List<Extent> extents, long offset, long length, ExtentState state)
    {
        if (extents.Count > 0 && extents[^1] is var last && last.State == state && last.Offset + last.Length == offset)
            extents[^1] = last with { Length = last.Length + length };
        else extents.Add(new Extent(offset, length, state));
    }

    /// <summary>The long name when its entries belong to this short name, else the short name (case flags applied).</summary>
    private static (string Name, bool Uncertain) Name(ReadOnlySpan<byte> shortName, byte caseFlags, List<byte[]> longName)
    {
        var sfn = shortName.ToArray();
        bool lostFirst = sfn[0] == 0xE5;
        if (sfn[0] == 0x05) sfn[0] = 0xE5; // a real 0xE5 first character
        if (longName.Count > 0 && longName.All(l => l[13] == longName[0][13]))
        {
            byte checksum = longName[0][13];
            bool matches = Checksum(sfn) == checksum;
            if (!matches && lostFirst)
            {
                // The checksum depends on the first byte one-to-one: exactly one value restores it.
                for (int b = 0x20; b < 0x100 && !matches; b++)
                {
                    sfn[0] = (byte)b;
                    matches = Checksum(sfn) == checksum;
                }
                if (matches) lostFirst = false;
            }
            if (matches && LongName(longName) is { Length: > 0 } text) return (text, false);
            if (!matches) sfn[0] = lostFirst ? (byte)'_' : sfn[0];
        }
        if (lostFirst) sfn[0] = (byte)'_';
        string baseName = Ascii(sfn.AsSpan(0, 8)).TrimEnd();
        string extension = Ascii(sfn.AsSpan(8, 3)).TrimEnd();
        if ((caseFlags & 0x08) != 0) baseName = baseName.ToLowerInvariant();
        if ((caseFlags & 0x10) != 0) extension = extension.ToLowerInvariant();
        return (extension.Length > 0 ? baseName + "." + extension : baseName, lostFirst);
    }

    private static string? LongName(List<byte[]> entries)
    {
        // Entries sit in front of the short one, the name's last part first.
        var sb = new StringBuilder();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            foreach (var (from, count) in new[] { (1, 5), (14, 6), (28, 2) })
            {
                for (int c = 0; c < count; c++)
                {
                    char ch = (char)BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(from + c * 2));
                    if (ch == '\0') return sb.ToString();
                    if (ch != '￿') sb.Append(ch);
                }
            }
        }
        return sb.ToString();
    }

    internal static byte Checksum(ReadOnlySpan<byte> shortName)
    {
        byte sum = 0;
        foreach (byte b in shortName[..11]) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + b);
        return sum;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '_';
        return new string(chars);
    }

    /// <summary>FAT times are local times of the machine that wrote them; FileCat assumes this machine's time zone.</summary>
    internal static DateTime? DosTime(ushort date, ushort time, byte tenths)
    {
        int day = date & 0x1F, month = date >> 5 & 0x0F, year = 1980 + (date >> 9);
        int second = (time & 0x1F) * 2 + tenths / 100, minute = time >> 5 & 0x3F, hour = time >> 11;
        if (date == 0 || day == 0 || month is 0 or > 12 || hour > 23 || minute > 59 || second > 59 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local).AddMilliseconds(tenths % 100 * 10).ToUniversalTime();
    }
}
