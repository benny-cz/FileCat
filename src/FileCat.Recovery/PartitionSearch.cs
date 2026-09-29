using System.Buffers.Binary;

namespace FileCat.Recovery;

/// <summary>
/// Finds file systems the partition table does not list (plan §17, D-46): partitions that were deleted, tables that were
/// erased, and volumes whose first sector is damaged, read from their backup boot sector. A boot sector's signature is
/// not enough: each find is checked where its file system keeps more of itself (NTFS: the MFT's first record where the
/// boot sector says; FAT: the allocation table's first entry, which repeats the media byte; exFAT: the boot region's
/// checksum). The quick search reads a few places where partitions usually start; the deep one reads every megabyte and
/// every cylinder boundary of the space no partition holds. Only that space is searched: a file system found inside a
/// partition would be a disk image stored in it, not a lost partition.
/// </summary>
public static class PartitionSearch
{
    private const int Sector = 512;
    private const long MiB = 1024 * 1024;
    /// <summary>Old MBR disks start partitions on cylinder boundaries (255 heads × 63 sectors), logical ones a track later.</summary>
    private const long Cylinder = 255L * 63 * Sector, Track = 63L * Sector;
    /// <summary>Space smaller than this holds no file system worth finding.</summary>
    private const long MinGap = 64 * 1024;
    private const int MaxQuickProbes = 1024;

    /// <summary>Space no partition holds.</summary>
    public readonly record struct Gap(long Offset, long Length)
    {
        public long End => Offset + Length;
    }

    /// <summary>The space none of <paramref name="volumes"/> holds (pieces of at least 64 KiB), in order.</summary>
    public static List<Gap> Gaps(long diskLength, IEnumerable<(long Offset, long Length)> volumes)
    {
        var gaps = new List<Gap>();
        long at = 0;
        foreach (var (offset, length) in volumes.OrderBy(v => v.Offset))
        {
            if (offset - at >= MinGap) gaps.Add(new Gap(at, offset - at));
            at = Math.Max(at, offset + length);
        }
        if (diskLength - at >= MinGap) gaps.Add(new Gap(at, diskLength - at));
        return gaps;
    }

    private static IEnumerable<(long, long)> Places(IEnumerable<VolumeSlot> slots) => slots.Select(s => (s.Offset, s.Length));

    /// <summary>
    /// Where partitions usually start in space no partition holds: right at its start, a track (63 sectors) later, at the
    /// next megabyte or cylinder; after each find, the same right after it (partitions usually follow one another). And
    /// the last sector of each such space, where a volume that ends there keeps its backup boot sector (NTFS).
    /// </summary>
    public static List<VolumeSlot> Quick(IBlockSource disk, IReadOnlyList<VolumeSlot> listed, CancellationToken ct)
    {
        var found = new List<VolumeSlot>();
        int probes = 0;
        foreach (var gap in Gaps(disk.Length, Places(listed)))
        {
            long from = gap.Offset;
            while (from < gap.End && probes < MaxQuickProbes)
            {
                VolumeSlot? hit = null;
                foreach (long p in Likely(from, gap.End))
                {
                    ct.ThrowIfCancellationRequested();
                    probes++;
                    if ((hit = Probe(disk, p, from, gap.End)) is not null) break;
                }
                if (hit is null) break;
                found.Add(hit);
                from = hit.Offset + hit.Length;
            }
            if (from < gap.End && probes++ < MaxQuickProbes && Probe(disk, gap.End, from, gap.End) is { } last) found.Add(last);
        }
        return found;
    }

    private static IEnumerable<long> Likely(long from, long end)
    {
        long exact = AlignUp(from, Sector);
        var places = new SortedSet<long>
        {
            exact, exact + Track, AlignUp(from, 4096), AlignUp(from, MiB), AlignUp(from + 1, MiB),
            AlignUp(from, Cylinder), AlignUp(from + 1, Cylinder), AlignUp(from + 1 - Track, Cylinder) + Track,
        };
        // A disk's first partition: after the GPT (sector 34), Apple's 40, some tools' 128, and the usual megabyte.
        if (from == 0) places.UnionWith([34 * Sector, 40 * Sector, 128 * Sector]);
        return places.Where(p => p < end);
    }

    /// <summary>
    /// Every megabyte and every cylinder boundary (and a track after it) of the space neither <paramref name="listed"/>
    /// nor <paramref name="known"/> holds; after a find, the search goes on right after it. Progress: bytes of that space
    /// searched, of its total.
    /// </summary>
    public static List<VolumeSlot> Deep(IBlockSource disk, IReadOnlyList<VolumeSlot> listed, IReadOnlyList<VolumeSlot> known,
        Action<long, long>? progress, CancellationToken ct)
    {
        var gaps = Gaps(disk.Length, Places(listed.Concat(known)));
        long total = gaps.Sum(g => g.Length), done = 0;
        var found = new List<VolumeSlot>();
        int probes = 0;
        foreach (var gap in gaps)
        {
            long from = gap.Offset;
            long p = AlignUp(from, Sector);
            while (p < gap.End)
            {
                if ((++probes & 63) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Invoke(done + (p - gap.Offset), total);
                }
                if (Probe(disk, p, from, gap.End) is { } hit)
                {
                    found.Add(hit);
                    from = p = hit.Offset + hit.Length;
                    continue;
                }
                p = Next(p);
            }
            if (from < gap.End && Probe(disk, gap.End, from, gap.End) is { } last) found.Add(last);
            done += gap.Length;
            progress?.Invoke(done, total);
        }
        return found;
    }

    /// <summary>The next place the deep search reads after <paramref name="p"/>: a megabyte, a cylinder, or a track after one.</summary>
    private static long Next(long p) => Math.Min(AlignUp(p + 1, MiB), Math.Min(AlignUp(p + 1, Cylinder), AlignUp(p + 1 - Track, Cylinder) + Track));

    private static long AlignUp(long value, long unit) => value <= 0 ? 0 : (value + unit - 1) / unit * unit;

    /// <summary>
    /// A partition the table lists but whose first sector is not a file system any more, read from its backup boot sector
    /// when one survives: NTFS keeps it in the partition's last sector, FAT32 six sectors on, exFAT a whole boot region
    /// twelve sectors on. The slot comes back unchanged otherwise.
    /// </summary>
    public static VolumeSlot Repair(IBlockSource disk, VolumeSlot slot)
    {
        try
        {
            long end = slot.Offset + slot.Length;
            if (Probe(disk, slot.Offset, slot.Offset, end, backupsOnly: true) is { } fromStart && fromStart.Offset == slot.Offset)
                return slot with { Boot = fromStart.Boot, Found = fromStart.Found };
            if (Probe(disk, end, slot.Offset, end, backupsOnly: true) is { } fromEnd && fromEnd.Offset == slot.Offset)
                return slot with { Boot = fromEnd.Boot, Found = fromEnd.Found };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { }
        return slot;
    }

    /// <summary>
    /// A file system around <paramref name="p"/>, from one read: one that starts at <paramref name="p"/> (its boot sector,
    /// or, when that is damaged, FAT32's copy six sectors on and exFAT's boot region twelve sectors on), or one that ends at
    /// <paramref name="p"/> (NTFS's backup boot sector in its last sector). Nothing found may start before
    /// <paramref name="from"/> or at or after <paramref name="end"/>.
    /// </summary>
    private static VolumeSlot? Probe(IBlockSource disk, long p, long from, long end, bool backupsOnly = false)
    {
        long start = Math.Max(0, p - 4096);
        byte[] window;
        try
        {
            window = ReadUpTo(disk, start, (int)(p - start) + 13 * Sector);
        }
        catch (IOException)
        {
            return null; // an unreadable place: nothing to find there
        }
        byte[]? At(long offset) =>
            offset >= start && offset + Sector <= start + window.Length ? window.AsSpan((int)(offset - start), Sector).ToArray() : null;
        VolumeSlot? Accept(VolumeSlot? slot) => slot is not null && slot.Offset >= from && slot.Offset < end ? slot : null;

        if (At(p) is { } boot && !backupsOnly)
        {
            var own = FileSystems.Detect(boot) switch
            {
                FileSystems.Ntfs => Ntfs(disk, p, boot, backup: false),
                FileSystems.ExFat => ExFat(disk, p, p, boot),
                FileSystems.Fat => Fat(disk, p, boot, backup: false),
                _ => null,
            };
            if (Accept(own) is { } hit) return hit with { Found = $"its boot sector at {Place(p)} is intact" };
        }
        // A damaged first sector: FAT32 keeps a copy six sectors on, exFAT its whole boot region twelve sectors on.
        if (At(p + 6 * Sector) is { } fat32 && FileSystems.Detect(fat32) == FileSystems.Fat && IsFat32(fat32) &&
            BinaryPrimitives.ReadUInt16LittleEndian(fat32.AsSpan(50)) == 6 && Accept(Fat(disk, p, fat32, backup: true)) is { } fromCopy)
            return fromCopy with { Boot = fat32, Found = $"its first sector is damaged, and FAT32's copy of it six sectors on is intact" };
        if (At(p + 12 * Sector) is { } exfat && FileSystems.Detect(exfat) == FileSystems.ExFat && exfat[108] == 9 &&
            Accept(ExFat(disk, p, p + 12 * Sector, exfat)) is { } fromBackup)
            return fromBackup with { Boot = exfat, Found = "its first sector is damaged, and exFAT's backup boot region twelve sectors on is intact" };
        // A volume that ends here: NTFS keeps a copy of its boot sector in its last sector.
        foreach (int sector in new[] { Sector, 4096 })
            if (At(p - sector) is { } last && FileSystems.Detect(last) == FileSystems.Ntfs && Accept(Ntfs(disk, p - sector, last, backup: true)) is { } fromEnd)
                return fromEnd with { Boot = last, Found = $"its first sector is damaged, and NTFS's copy of it in its last sector ({Place(p - sector)}) is intact" };
        return null;
    }

    /// <summary>"1 MiB" at a megabyte, else "sector 63": how partition tools say where partitions start.</summary>
    internal static string Place(long offset) => offset % MiB == 0 ? $"{offset / MiB:N0} MiB" : $"sector {offset / Sector:N0}";

    private static bool IsFat32(byte[] boot) => BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(22)) == 0;

    /// <summary>An NTFS boot sector at <paramref name="at"/> (or its backup there), checked by the MFT record it points to.</summary>
    private static VolumeSlot? Ntfs(IBlockSource disk, long at, byte[] boot, bool backup)
    {
        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        if (bytesPerSector is not (512 or 1024 or 2048 or 4096)) return null;
        int raw = boot[13];
        long sectorsPerCluster = raw <= 0x80 ? raw : 256 - raw <= 20 ? 1L << (256 - raw) : 0;
        long total = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(40));
        long mft = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(48));
        if (sectorsPerCluster == 0 || total <= 0 || mft <= 0 || mft > total / sectorsPerCluster) return null;
        // The backup sits in the sector right after the volume's own (NTFS counts one sector less than its partition).
        long start = backup ? at - total * bytesPerSector : at;
        if (start < 0 || start >= disk.Length) return null;
        // The MFT's first record, $MFT's own, begins with "FILE" where the boot sector says the MFT is.
        if (!Begins(disk, start + mft * sectorsPerCluster * bytesPerSector, "FILE"u8)) return null;
        return new VolumeSlot(0, start, Math.Min((total + 1) * bytesPerSector, disk.Length - start), null) { Origin = VolumeOrigin.Search };
    }

    /// <summary>A FAT boot sector for a volume at <paramref name="at"/>, checked by its allocation table's first entry.</summary>
    private static VolumeSlot? Fat(IBlockSource disk, long at, byte[] boot, bool backup)
    {
        if (boot[510] != 0x55 || boot[511] != 0xAA) return null;
        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
        long total = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19)) is var t16 and > 0 ? t16 : BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32));
        byte media = boot[21];
        if (backup && reserved <= 6) return null; // the copy lies among the reserved sectors
        if (media is not (0xF0 or >= 0xF8) || at >= disk.Length) return null;
        // The allocation table's first entry repeats the media byte, and its other bits are all ones.
        var fat = ReadUpTo(disk, at + (long)reserved * bytesPerSector, 2);
        if (fat.Length < 2 || fat[0] != media || fat[1] != 0xFF) return null;
        return new VolumeSlot(0, at, Math.Min(total * bytesPerSector, disk.Length - at), null) { Origin = VolumeOrigin.Search };
    }

    /// <summary>An exFAT volume at <paramref name="at"/>, checked by the checksum of its boot region at <paramref name="region"/>.</summary>
    private static VolumeSlot? ExFat(IBlockSource disk, long at, long region, byte[] boot)
    {
        int shift = boot[108];
        if (shift is < 9 or > 12 || at >= disk.Length) return null;
        int bytesPerSector = 1 << shift;
        var sectors = ReadUpTo(disk, region, 12 * bytesPerSector);
        if (sectors.Length < 12 * bytesPerSector) return null;
        // The boot checksum covers sectors 0–10 (but for the volume flags and the percentage in use); sector 11 repeats it.
        uint sum = 0;
        for (int i = 0; i < 11 * bytesPerSector; i++)
        {
            if (i is 106 or 107 or 112) continue;
            sum = ((sum & 1) != 0 ? 0x80000000u : 0) + (sum >> 1) + sectors[i];
        }
        for (int i = 11 * bytesPerSector; i < 12 * bytesPerSector; i += 4)
            if (BinaryPrimitives.ReadUInt32LittleEndian(sectors.AsSpan(i)) != sum) return null;
        long length = (long)BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(72)) * bytesPerSector;
        if (length <= 0) return null;
        return new VolumeSlot(0, at, Math.Min(length, disk.Length - at), null) { Origin = VolumeOrigin.Search };
    }

    private static bool Begins(IBlockSource disk, long offset, ReadOnlySpan<byte> signature)
    {
        if (offset < 0 || offset + signature.Length > disk.Length) return false;
        var head = ReadUpTo(disk, offset, signature.Length);
        return head.AsSpan().SequenceEqual(signature);
    }

    /// <summary>Up to <paramref name="length"/> bytes at <paramref name="offset"/>: fewer at the end of the disk.</summary>
    private static byte[] ReadUpTo(IBlockSource disk, long offset, int length)
    {
        if (offset >= disk.Length) return [];
        var buffer = new byte[(int)Math.Min(length, disk.Length - offset)];
        int done = 0;
        while (done < buffer.Length)
        {
            int n = disk.Read(offset + done, buffer.AsSpan(done));
            if (n <= 0) break;
            done += n;
        }
        return done == buffer.Length ? buffer : buffer[..done];
    }
}
