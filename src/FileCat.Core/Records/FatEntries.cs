using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

/// <summary>
/// An item's own directory entry on a FAT12/16/32 or exFAT volume (D-56), found by walking its path from the root
/// through the raw volume: the bytes FAT keeps and Windows shows only in part. FAT: the 8.3 name and the undocumented
/// case bits Windows sets in the reserved byte (a lower-case name without a long-name entry), creation time to 10 ms,
/// access as a date, the first cluster, and the long-name entries with their checksum. exFAT: the entry set with its
/// checksum, all three times to 10 ms with their UTC offsets, the stream's "no FAT chain" flag and valid data length.
/// Reading changes nothing; damaged or unexpected structures end the walk with a reason.
/// </summary>
public static class FatEntries
{
    /// <summary>
    /// The entry of <paramref name="relativePath"/> ("docs\report.txt", separators of either kind) on the volume that
    /// <paramref name="read"/> reads (offset, count), or null with <paramref name="problem"/> saying why.
    /// </summary>
    public static InspectionSection? Describe(Func<long, int, byte[]> read, string relativePath, out string? problem)
    {
        problem = null;
        byte[] boot;
        try { boot = read(0, 512); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = "The volume could not be read: " + ex.Message; return null; }
        if (boot.Length < 512) { problem = "The volume's boot sector could not be read."; return null; }
        var parts = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        try
        {
            return boot.AsSpan(3, 8).SequenceEqual("EXFAT   "u8) ? ExFat.Describe(read, boot, parts, out problem) : Fat.Describe(read, boot, parts, out problem);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentOutOfRangeException)
        {
            problem = "Its directory entry could not be read: " + ex.Message;
            return null;
        }
    }

    /// <summary>An 8.3 entry's name as Windows shows it (tests).</summary>
    internal static string ShortNameOf(ReadOnlySpan<byte> entry) => Fat.ShortName(entry);

    // ---- FAT12/16/32 ------------------------------------------------------------------------------------------

    private static class Fat
    {
        public static InspectionSection? Describe(Func<long, int, byte[]> read, byte[] boot, string[] parts, out string? problem)
        {
            problem = null;
            int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
            int sectorsPerCluster = boot[13];
            int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
            int fats = boot[16];
            int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(17));
            long totalSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19)) is var small and > 0 ? small : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32));
            long fatSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22)) is var fat16 and > 0 ? fat16 : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36));
            if (bytesPerSector is not (512 or 1024 or 2048 or 4096) || sectorsPerCluster == 0 || fats == 0 || fatSectors == 0)
                throw new InvalidDataException("the boot sector does not describe a FAT volume");
            long rootSectors = (rootEntries * 32L + bytesPerSector - 1) / bytesPerSector;
            long dataStart = reserved + fats * fatSectors + rootSectors;
            long clusters = (totalSectors - dataStart) / sectorsPerCluster;
            int bits = clusters < 4085 ? 12 : clusters < 65525 ? 16 : 32;
            long clusterBytes = (long)sectorsPerCluster * bytesPerSector;
            long fatOffset = (long)reserved * bytesPerSector;
            uint rootCluster = bits == 32 ? BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(44)) : 0;
            var volume = new Volume(read, fatOffset, fatSectors * bytesPerSector, dataStart * bytesPerSector, clusterBytes, bits, clusters);

            // The root: a fixed region on FAT12/16, a cluster chain on FAT32.
            List<(long Offset, int Length)> directory = bits == 32
                ? volume.Chain(rootCluster).Select(c => (volume.ClusterOffset(c), (int)clusterBytes)).ToList()
                : [((reserved + fats * fatSectors) * (long)bytesPerSector, (int)(rootSectors * bytesPerSector))];
            string where = "the root folder";
            for (int depth = 0; depth < parts.Length; depth++)
            {
                var entry = Find(volume, directory, parts[depth]);
                if (entry is null)
                {
                    problem = $"“{parts[depth]}” was not found in {where} (the volume's own entries were read).";
                    return null;
                }
                if (depth == parts.Length - 1) return Section(volume, entry, bits);
                if ((entry.Short[11] & 0x10) == 0)
                {
                    problem = $"“{parts[depth]}” is not a folder.";
                    return null;
                }
                directory = volume.Chain(entry.FirstCluster).Select(c => (volume.ClusterOffset(c), (int)clusterBytes)).ToList();
                where = $"“{parts[depth]}”";
            }
            problem = "The root folder has no directory entry of its own on FAT: its location is fixed by the boot sector.";
            return null;
        }

        private sealed record Entry(byte[] Short, List<byte[]> LongEntries, string? LongName, bool ChecksumOk, long Offset)
        {
            public uint FirstCluster => (uint)(BinaryPrimitives.ReadUInt16LittleEndian(Short.AsSpan(20)) << 16 | BinaryPrimitives.ReadUInt16LittleEndian(Short.AsSpan(26)));
        }

        /// <summary>The entry named <paramref name="name"/> (its long name, or its 8.3 name with the case bits applied).</summary>
        private static Entry? Find(Volume volume, List<(long Offset, int Length)> directory, string name)
        {
            var pending = new List<byte[]>();
            int scanned = 0;
            foreach (var (offset, length) in directory)
            {
                var data = volume.Read(offset, length);
                for (int at = 0; at + 32 <= data.Length; at += 32)
                {
                    if (++scanned > 65536) return null;
                    byte first = data[at];
                    if (first == 0x00) return null; // no entries after this one
                    var raw = data.AsSpan(at, 32).ToArray();
                    if (first == 0xE5)
                    {
                        pending.Clear();
                        continue;
                    }
                    if (raw[11] == 0x0F)
                    {
                        pending.Add(raw);
                        continue;
                    }
                    if ((raw[11] & 0x08) != 0) // the volume label
                    {
                        pending.Clear();
                        continue;
                    }
                    string shortName = ShortName(raw);
                    var (longName, ok) = LongName(pending, raw);
                    if (string.Equals(longName ?? shortName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(shortName, name, StringComparison.OrdinalIgnoreCase))
                        return new Entry(raw, [.. pending], longName, ok, offset + at);
                    pending.Clear();
                }
            }
            return null;
        }

        /// <summary>The 8.3 name as Windows shows it: the case bits of the reserved byte make the base or extension lower case.</summary>
        internal static string ShortName(ReadOnlySpan<byte> e)
        {
            var raw = e[..11].ToArray();
            if (raw[0] == 0x05) raw[0] = 0xE5; // a first byte of 0xE5 is stored as 0x05
            string @base = Encoding.Latin1.GetString(raw, 0, 8).TrimEnd(), ext = Encoding.Latin1.GetString(raw, 8, 3).TrimEnd();
            if ((e[12] & 0x08) != 0) @base = @base.ToLowerInvariant();
            if ((e[12] & 0x10) != 0) ext = ext.ToLowerInvariant();
            return ext.Length > 0 ? $"{@base}.{ext}" : @base;
        }

        /// <summary>The long name from the long-name entries before the short one (last written first), and whether their checksum is the short name's.</summary>
        private static (string? Name, bool ChecksumOk) LongName(List<byte[]> entries, byte[] shortEntry)
        {
            if (entries.Count == 0) return (null, true);
            byte sum = 0;
            for (int i = 0; i < 11; i++) sum = (byte)(((sum & 1) << 7) + (sum >> 1) + shortEntry[i]);
            var sb = new StringBuilder();
            bool ok = true;
            foreach (var e in Enumerable.Reverse(entries))
            {
                ok &= e[13] == sum;
                foreach (var (at, count) in new[] { (1, 5), (14, 6), (28, 2) })
                    for (int c = 0; c < count; c++)
                    {
                        char ch = (char)BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(at + c * 2));
                        if (ch is '\0' or '￿') goto next;
                        sb.Append(ch);
                    }
                next:;
            }
            return (sb.ToString(), ok);
        }

        private static InspectionSection Section(Volume volume, Entry entry, int bits)
        {
            var e = entry.Short;
            var fields = new List<(string, string)>
            {
                ("Short name", $"{ShortName(e)} (stored as “{Encoding.Latin1.GetString(e, 0, 11)}”)"),
            };
            if (entry.LongName is { } longName)
                fields.Add(("Long name", $"{longName} ({entry.LongEntries.Count} long-name entr{(entry.LongEntries.Count == 1 ? "y" : "ies")}, checksum {(entry.ChecksumOk ? "matches" : "does not match: they may belong to another name")})"));
            fields.Add(("Attributes", RecordText.Attributes(e[11])));
            byte nt = e[12];
            var cases = new List<string>();
            if ((nt & 0x08) != 0) cases.Add("base in lower case");
            if ((nt & 0x10) != 0) cases.Add("extension in lower case");
            fields.Add(("Reserved byte", $"0x{nt:X2}" + (cases.Count > 0 ? $" · {string.Join(", ", cases)} (Windows' undocumented case bits: a lower-case 8.3 name with no long name)" : "")));
            fields.Add(("Created", DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(16)), BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(14)), e[13]) + " (to 10 ms)"));
            fields.Add(("Modified", DosTime(BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(24)), BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(22)), 0) + " (to 2 seconds)"));
            fields.Add(("Accessed", DosDate(BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(18))) + " (a date only)"));
            uint first = entry.FirstCluster;
            long size = BinaryPrimitives.ReadUInt32LittleEndian(e.AsSpan(28));
            fields.Add(("Size", RecordText.Bytes(size)));
            fields.Add(("First cluster", first == 0 ? "none (empty)" : first.ToString("N0", CultureInfo.CurrentCulture)));
            if (first >= 2)
            {
                var chain = volume.Chain(first);
                fields.Add(("Clusters", $"{chain.Count:N0} in {Fragments(chain):N0} fragment{(Fragments(chain) == 1 ? "" : "s")} (FAT{bits} chain)"));
            }
            fields.Add(("Entry at", $"byte {entry.Offset:N0} of the volume"));
            var lines = RecordText.Wrap("FAT keeps local times with no time zone. The raw entries (long-name entries first, as they lie on the volume):").ToList();
            foreach (var raw in entry.LongEntries.Append(e)) lines.AddRange(RecordText.HexLines(raw, 32));
            return new InspectionSection($"Directory entry (FAT{bits})", fields) { Lines = lines };
        }
    }

    // ---- exFAT ------------------------------------------------------------------------------------------------

    private static class ExFat
    {
        public static InspectionSection? Describe(Func<long, int, byte[]> read, byte[] boot, string[] parts, out string? problem)
        {
            problem = null;
            int sectorShift = boot[108], clusterShift = boot[109];
            if (sectorShift is < 9 or > 12 || clusterShift > 25 - sectorShift) throw new InvalidDataException("the boot sector does not describe an exFAT volume");
            long sector = 1L << sectorShift, clusterBytes = sector << clusterShift;
            long fatOffset = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(80)) * sector;
            long fatLength = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(84)) * sector;
            long heap = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(88)) * sector;
            long clusters = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(92));
            uint root = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(96));
            var volume = new Volume(read, fatOffset, fatLength, heap, clusterBytes, 32, clusters);
            var directory = volume.Chain(root);
            string where = "the root folder";
            for (int depth = 0; depth < parts.Length; depth++)
            {
                var set = Find(volume, directory, parts[depth]);
                if (set is null)
                {
                    problem = $"“{parts[depth]}” was not found in {where} (the volume's own entries were read).";
                    return null;
                }
                if (depth == parts.Length - 1) return Section(volume, set);
                bool folder = (BinaryPrimitives.ReadUInt16LittleEndian(set.Entries[0].AsSpan(4)) & 0x10) != 0;
                if (!folder)
                {
                    problem = $"“{parts[depth]}” is not a folder.";
                    return null;
                }
                directory = set.NoFatChain ? Enumerable.Range(0, (int)Math.Min((set.Length + clusterBytes - 1) / clusterBytes, 1 << 20)).Select(i => set.FirstCluster + (uint)i).ToList() : volume.Chain(set.FirstCluster);
                where = $"“{parts[depth]}”";
            }
            problem = "The root folder has no directory entry of its own on exFAT: the boot sector points to it.";
            return null;
        }

        private sealed record EntrySet(List<byte[]> Entries, string Name, long Offset)
        {
            public byte[] Stream => Entries[1];
            public bool NoFatChain => (Stream[1] & 0x02) != 0;
            public uint FirstCluster => BinaryPrimitives.ReadUInt32LittleEndian(Stream.AsSpan(20));
            public long Length => (long)BinaryPrimitives.ReadUInt64LittleEndian(Stream.AsSpan(24));
        }

        private static EntrySet? Find(Volume volume, List<uint> directory, string name)
        {
            // A folder's entries as one run of bytes (at most 4 MiB): entry sets may cross cluster ends.
            var bytes = new List<byte>();
            var offsets = new List<long>();
            foreach (uint cluster in directory)
            {
                if (bytes.Count >= 4 << 20) break;
                long offset = volume.ClusterOffset(cluster);
                bytes.AddRange(volume.Read(offset, (int)volume.ClusterBytes));
                offsets.Add(offset);
            }
            var data = bytes.ToArray();
            for (int at = 0; at + 32 <= data.Length; at += 32)
            {
                byte type = data[at];
                if (type == 0x00) return null;
                if (type != 0x85) continue;
                int secondaries = data[at + 1];
                if (secondaries < 2 || at + 32 * (1 + secondaries) > data.Length) continue;
                var entries = Enumerable.Range(0, 1 + secondaries).Select(i => data.AsSpan(at + i * 32, 32).ToArray()).ToList();
                if (entries[1][0] != 0xC0) continue;
                int length = entries[1][3];
                var sb = new StringBuilder();
                foreach (var e in entries.Skip(2).Where(e => e[0] == 0xC1))
                    for (int c = 0; c < 15 && sb.Length < length; c++) sb.Append((char)BinaryPrimitives.ReadUInt16LittleEndian(e.AsSpan(2 + c * 2)));
                string entryName = sb.ToString();
                if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                {
                    int cluster = at / (int)volume.ClusterBytes;
                    long offset = cluster < offsets.Count ? offsets[cluster] + at % volume.ClusterBytes : -1;
                    return new EntrySet(entries, entryName, offset);
                }
                at += 32 * secondaries;
            }
            return null;
        }

        private static InspectionSection Section(Volume volume, EntrySet set)
        {
            var file = set.Entries[0];
            var stream = set.Stream;
            ushort stored = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(2));
            ushort computed = SetChecksum(set.Entries);
            var fields = new List<(string, string)>
            {
                ("Name", set.Name),
                ("Entry set", $"{set.Entries.Count} entries, checksum 0x{stored:X4} {(stored == computed ? "matches" : $"does not match (0x{computed:X4}): the set was changed without its checksum")}"),
                ("Attributes", RecordText.Attributes(BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4)))),
                ("Created", Stamp(file, 8, file[20], file[22])),
                ("Modified", Stamp(file, 12, file[21], file[23])),
                ("Accessed", Stamp(file, 16, 0, file[24])),
            };
            byte flags = stream[1];
            var flagWords = new List<string>();
            if ((flags & 0x01) != 0) flagWords.Add("allocation possible");
            if ((flags & 0x02) != 0) flagWords.Add("no FAT chain: its clusters are contiguous");
            fields.Add(("Stream flags", $"0x{flags:X2}" + (flagWords.Count > 0 ? " · " + string.Join(", ", flagWords) : "")));
            long valid = (long)BinaryPrimitives.ReadUInt64LittleEndian(stream.AsSpan(8));
            fields.Add(("Size", RecordText.Bytes(set.Length)));
            fields.Add(("Valid data", valid == set.Length ? "all of it" : $"{valid:N0} bytes (written so far; the rest reads as zeros)"));
            fields.Add(("First cluster", set.FirstCluster == 0 ? "none (empty)" : set.FirstCluster.ToString("N0", CultureInfo.CurrentCulture)));
            if (set.FirstCluster >= 2 && !set.NoFatChain)
            {
                var chain = volume.Chain(set.FirstCluster);
                fields.Add(("Clusters", $"{chain.Count:N0} in {Fragments(chain):N0} fragment{(Fragments(chain) == 1 ? "" : "s")} (FAT chain)"));
            }
            else if (set.FirstCluster >= 2)
                fields.Add(("Clusters", $"{(set.Length + volume.ClusterBytes - 1) / volume.ClusterBytes:N0}, contiguous"));
            fields.Add(("Name hash", $"0x{BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(4)):X4}"));
            if (set.Offset >= 0) fields.Add(("Entry set at", $"byte {set.Offset:N0} of the volume"));
            var lines = RecordText.Wrap("exFAT keeps each time to 10 ms with the offset from UTC of the clock that wrote it. The raw entries (file, stream, names):").ToList();
            foreach (var raw in set.Entries) lines.AddRange(RecordText.HexLines(raw, 32));
            return new InspectionSection("Directory entry (exFAT)", fields) { Lines = lines };
        }

        /// <summary>A packed DOS time and date with exFAT's 10 ms increment and UTC offset (15-minute steps, bit 7 marks it valid).</summary>
        private static string Stamp(byte[] e, int at, byte tenMs, byte offset)
        {
            uint stamp = BinaryPrimitives.ReadUInt32LittleEndian(e.AsSpan(at));
            string when = DosTime((ushort)(stamp >> 16), (ushort)stamp, tenMs);
            if ((offset & 0x80) == 0) return when + " (no time zone recorded)";
            int quarter = (sbyte)(byte)(offset << 1) >> 1;
            var zone = TimeSpan.FromMinutes(quarter * 15);
            return $"{when} UTC{(zone < TimeSpan.Zero ? "−" : "+")}{zone:hh\\:mm}";
        }

        /// <summary>The entry set checksum: every byte of every entry, but the checksum field itself.</summary>
        private static ushort SetChecksum(List<byte[]> entries)
        {
            ushort sum = 0;
            for (int i = 0; i < entries.Count; i++)
                for (int b = 0; b < 32; b++)
                {
                    if (i == 0 && b is 2 or 3) continue;
                    sum = (ushort)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + entries[i][b]);
                }
            return sum;
        }
    }

    // ---- Shared -----------------------------------------------------------------------------------------------

    /// <summary>A volume's FAT and data area: clusters numbered from 2.</summary>
    private sealed class Volume(Func<long, int, byte[]> read, long fatOffset, long fatLength, long dataOffset, long clusterBytes, int bits, long clusters)
    {
        private byte[]? _fat;

        public long ClusterBytes => clusterBytes;

        public long ClusterOffset(uint cluster) => dataOffset + (cluster - 2L) * clusterBytes;

        public byte[] Read(long offset, int length) => read(offset, length);

        /// <summary>The chain from <paramref name="first"/> (at most 1,000,000 clusters; a loop or a bad link ends it).</summary>
        public List<uint> Chain(uint first)
        {
            _fat ??= read(fatOffset, (int)Math.Min(fatLength, 64L << 20));
            var chain = new List<uint>();
            var seen = new HashSet<uint>();
            uint cluster = first;
            while (cluster >= 2 && cluster < clusters + 2 && chain.Count < 1_000_000 && seen.Add(cluster))
            {
                chain.Add(cluster);
                cluster = Next(cluster);
            }
            return chain;
        }

        private uint Next(uint cluster)
        {
            var fat = _fat!;
            switch (bits)
            {
                case 12:
                    int at = (int)(cluster + cluster / 2);
                    if (at + 1 >= fat.Length) return 0;
                    int pair = BinaryPrimitives.ReadUInt16LittleEndian(fat.AsSpan(at));
                    uint value = (uint)((cluster & 1) != 0 ? pair >> 4 : pair & 0xFFF);
                    return value >= 0xFF8 ? 0 : value;
                case 16:
                    if ((long)cluster * 2 + 2 > fat.Length) return 0;
                    ushort v16 = BinaryPrimitives.ReadUInt16LittleEndian(fat.AsSpan((int)cluster * 2));
                    return v16 >= 0xFFF8 ? 0u : v16;
                default:
                    if ((long)cluster * 4 + 4 > fat.Length) return 0;
                    uint v32 = BinaryPrimitives.ReadUInt32LittleEndian(fat.AsSpan((int)cluster * 4)) & 0x0FFF_FFFF;
                    return v32 >= 0x0FFF_FFF8 ? 0 : v32;
            }
        }
    }

    private static int Fragments(List<uint> chain)
    {
        int fragments = chain.Count > 0 ? 1 : 0;
        for (int i = 1; i < chain.Count; i++)
            if (chain[i] != chain[i - 1] + 1) fragments++;
        return fragments;
    }

    /// <summary>A DOS date and time with a 10 ms addition (0–199): "2026-09-30 10:00:01.23".</summary>
    private static string DosTime(ushort date, ushort time, byte tenMs)
    {
        if (date == 0) return "not set";
        int year = 1980 + (date >> 9), month = (date >> 5) & 15, day = date & 31;
        int hour = time >> 11, minute = (time >> 5) & 63, second = (time & 31) * 2 + tenMs / 100;
        int hundredths = tenMs % 100;
        return month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59
            ? $"invalid (date 0x{date:X4}, time 0x{time:X4})"
            : $"{year:0000}-{month:00}-{day:00} {hour:00}:{minute:00}:{second:00}.{hundredths:00}";
    }

    private static string DosDate(ushort date)
    {
        if (date == 0) return "not set";
        int year = 1980 + (date >> 9), month = (date >> 5) & 15, day = date & 31;
        return month is < 1 or > 12 || day is < 1 or > 31 ? $"invalid (0x{date:X4})" : $"{year:0000}-{month:00}-{day:00}";
    }
}
