using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Jobs;

namespace FileCat.Recovery;

/// <summary>What says a volume is there.</summary>
public enum VolumeOrigin
{
    /// <summary>The partition table lists it (or the source is a single volume).</summary>
    Table,

    /// <summary>Only the copy of the GPT at the end of the disk lists it: the table at its start is damaged or erased.</summary>
    BackupTable,

    /// <summary>No table lists it: its file system was found where no partition is (a deleted partition, an erased table).</summary>
    Search,
}

/// <summary>A place on a source where a file system may start: a partition, or the whole source.</summary>
public sealed record VolumeSlot(int Number, long Offset, long Length, string? PartitionName)
{
    public VolumeOrigin Origin { get; init; }

    /// <summary>How the volume was found or read, when that is not simply the partition table ("its boot sector … is intact").</summary>
    public string? Found { get; init; }

    /// <summary>The boot sector to read the file system by, when the volume's first sector is damaged (a backup copy).</summary>
    public byte[]? Boot { get; init; }

    /// <summary>No partition table in use lists it.</summary>
    public bool Lost => Origin != VolumeOrigin.Table;

    /// <summary>The file system is lost as a whole, so its existing files are listed too, not only deleted ones.</summary>
    public bool WholeFileSystem => Lost || Boot is not null;
}

/// <summary>Finds volumes on a source: a file system at the very start, or MBR (with extended partitions) and GPT tables.</summary>
public static class PartitionTable
{
    private const int MaxLogicalPartitions = 128;
    private const int MaxGptEntries = 1024;

    public static IReadOnlyList<VolumeSlot> Find(IBlockSource source, List<string> warnings)
    {
        if (source.Length < 512) return [];
        var first = source.ReadExactly(0, 512);
        if (FileSystems.Detect(first) is not null) return [new VolumeSlot(1, 0, source.Length, null)];
        bool signature = first[510] == 0x55 && first[511] == 0xAA;
        bool protective = signature && Enumerable.Range(0, 4).Any(i => first[446 + i * 16 + 4] == 0xEE);
        if (protective || !signature)
        {
            // GPT: the table after the protective MBR, or its copy at the end of the disk when that one is damaged or was
            // erased (a disk whose first sectors were wiped may keep both). Checksums decide; a table whose checksums
            // both fail is still read, with a warning, rather than not at all.
            if (Gpt(source, primary: true, strict: true, warnings) is { } gpt) return gpt;
            if (Gpt(source, primary: false, strict: true, warnings) is { } backup) return backup;
            if (Gpt(source, primary: true, strict: false, warnings) is { } unchecked_)
            {
                warnings.Add("The GPT partition table's checksums do not match its contents, and its copy at the end of the disk is damaged too; FileCat read it as it is.");
                return unchecked_;
            }
            if (protective) warnings.Add("The GPT partition table is damaged, and so is its copy at the end of the disk; FileCat looked for the partitions without them.");
            return [];
        }
        return Mbr(source, first, warnings);
    }

    /// <summary>
    /// A GPT: the header in the second sector, or (<paramref name="primary"/> false) its copy in the last one. When
    /// <paramref name="strict"/>, the header's and the entries' checksums must match.
    /// </summary>
    private static List<VolumeSlot>? Gpt(IBlockSource source, bool primary, bool strict, List<string> warnings)
    {
        foreach (int sector in new[] { 512, 4096 })
        {
            long headerAt = primary ? sector : (source.Length / sector - 1) * sector;
            if (source.Length < sector * 2 || headerAt < sector) continue;
            var header = source.ReadExactly(headerAt, 92);
            if (!header.AsSpan(0, 8).SequenceEqual("EFI PART"u8)) continue;
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
            if (strict)
            {
                if (headerSize is < 92 or > 512) continue;
                var whole = source.ReadExactly(headerAt, (int)headerSize);
                uint stored = BinaryPrimitives.ReadUInt32LittleEndian(whole.AsSpan(16));
                whole.AsSpan(16, 4).Clear();
                if (Crc32.HashToUInt32(whole) != stored) continue;
            }
            long entriesLba = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(72));
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84));
            if (count > MaxGptEntries || size is < 128 or > 4096 || entriesLba <= 0 || entriesLba * sector >= source.Length)
            {
                if (!strict) warnings.Add("The GPT partition table is damaged; FileCat looked for file systems without it.");
                if (strict) continue;
                return null;
            }
            var slots = new List<VolumeSlot>();
            var table = source.ReadExactly(entriesLba * sector, (int)(count * size));
            if (strict && Crc32.HashToUInt32(table) != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(88))) continue;
            for (int i = 0; i < count; i++)
            {
                var entry = table.AsSpan(i * (int)size, (int)size);
                var type = new Guid(entry[..16]);
                if (type == Guid.Empty || type == MicrosoftReserved) continue;
                long firstLba = BinaryPrimitives.ReadInt64LittleEndian(entry[32..]);
                long lastLba = BinaryPrimitives.ReadInt64LittleEndian(entry[40..]);
                if (firstLba <= 0 || lastLba < firstLba || firstLba * sector >= source.Length)
                {
                    warnings.Add($"GPT partition {i + 1} lies outside the source and was left out.");
                    continue;
                }
                string name = Encoding.Unicode.GetString(entry.Slice(56, Math.Min(72, entry.Length - 56))).TrimEnd('\0');
                slots.Add(new VolumeSlot(slots.Count + 1, firstLba * sector, (lastLba - firstLba + 1) * sector, name.Length > 0 ? name : null)
                {
                    Origin = primary ? VolumeOrigin.Table : VolumeOrigin.BackupTable,
                    Found = primary ? null : "only the copy of the GPT partition table at the end of the disk lists it: the table at its start is damaged or was erased",
                });
            }
            return slots;
        }
        return null;
    }

    private static readonly Guid MicrosoftReserved = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");

    private static List<VolumeSlot> Mbr(IBlockSource source, byte[] mbr, List<string> warnings)
    {
        const int Sector = 512;
        var slots = new List<VolumeSlot>();
        for (int i = 0; i < 4; i++)
        {
            var entry = mbr.AsSpan(446 + i * 16, 16);
            byte type = entry[4];
            long start = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            long count = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (type == 0 || count == 0) continue;
            if (type is 0x05 or 0x0F or 0x85)
            {
                Logical(source, start * Sector, slots, warnings);
                continue;
            }
            Add(source, slots, start * Sector, count * Sector, warnings);
        }
        return slots;
    }

    private static void Logical(IBlockSource source, long extendedStart, List<VolumeSlot> slots, List<string> warnings)
    {
        const int Sector = 512;
        long ebr = extendedStart;
        for (int n = 0; n < MaxLogicalPartitions && ebr > 0 && ebr + Sector <= source.Length; n++)
        {
            var sector = source.ReadExactly(ebr, Sector);
            if (sector[510] != 0x55 || sector[511] != 0xAA)
            {
                warnings.Add("An extended partition's table is damaged; later logical partitions may be missing.");
                return;
            }
            long start = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(446 + 8));
            long count = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(446 + 12));
            if (sector[446 + 4] != 0 && count > 0) Add(source, slots, ebr + start * Sector, count * Sector, warnings);
            long next = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(462 + 8));
            if (sector[462 + 4] == 0 || next == 0) return;
            long following = extendedStart + next * Sector;
            if (following <= ebr) return; // tables only point forward; anything else is a loop
            ebr = following;
        }
    }

    private static void Add(IBlockSource source, List<VolumeSlot> slots, long offset, long length, List<string> warnings)
    {
        if (offset <= 0 || offset >= source.Length)
        {
            warnings.Add("A partition lies outside the source and was left out.");
            return;
        }
        slots.Add(new VolumeSlot(slots.Count + 1, offset, Math.Min(length, source.Length - offset), null));
    }
}

/// <summary>Recognizes the file systems FileCat reads from their first sector.</summary>
public static class FileSystems
{
    public const string Ntfs = "NTFS", ExFat = "exFAT", Fat = "FAT";

    /// <summary>Why a volume FileCat does not read shows nothing: what it is, when its first sectors tell.</summary>
    public static string Unrecognized(IBlockSource volume)
    {
        byte[] head;
        try
        {
            head = volume.ReadExactly(0, (int)Math.Min(2048, volume.Length));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return "No NTFS, FAT, or exFAT file system was found here.";
        }
        bool Has(int at, ReadOnlySpan<byte> text) => head.Length >= at + text.Length && head.AsSpan(at, text.Length).SequenceEqual(text);
        if (Has(3, "-FVE-FS-"u8)) return "This volume is encrypted with BitLocker: FileCat cannot look for deleted files inside it.";
        if (Has(3, "ReFS"u8)) return "This volume has a ReFS file system, which FileCat does not read (it reads NTFS, FAT, and exFAT).";
        if (Has(1080, [0x53, 0xEF])) return "This volume has an ext2, ext3, or ext4 file system (Linux), which FileCat does not read (it reads NTFS, FAT, and exFAT).";
        if (Has(32, "NXSB"u8)) return "This volume is an APFS container (macOS), which FileCat does not read (it reads NTFS, FAT, and exFAT).";
        if (Has(1024, "H+"u8) || Has(1024, "HX"u8)) return "This volume has an HFS+ file system (macOS), which FileCat does not read (it reads NTFS, FAT, and exFAT).";
        return "No NTFS, FAT, or exFAT file system was found here.";
    }

    public static string? Detect(ReadOnlySpan<byte> boot)
    {
        if (boot.Length < 512) return null;
        if (boot[3..11].SequenceEqual("NTFS    "u8)) return Ntfs;
        if (boot[3..11].SequenceEqual("EXFAT   "u8)) return ExFat;
        bool jump = boot[0] == 0xEB && boot[2] == 0x90 || boot[0] == 0xE9;
        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot[11..]);
        int sectorsPerCluster = boot[13];
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot[14..]);
        int fats = boot[16];
        uint fatSize = BinaryPrimitives.ReadUInt16LittleEndian(boot[22..]) is var small and > 0 ? small : BinaryPrimitives.ReadUInt32LittleEndian(boot[36..]);
        uint total = BinaryPrimitives.ReadUInt16LittleEndian(boot[19..]) is var t16 and > 0 ? t16 : BinaryPrimitives.ReadUInt32LittleEndian(boot[32..]);
        bool valid = jump && bytesPerSector is 512 or 1024 or 2048 or 4096 && sectorsPerCluster is > 0 and <= 128 &&
                     (sectorsPerCluster & (sectorsPerCluster - 1)) == 0 && reserved >= 1 && fats is >= 1 and <= 4 && fatSize > 0 && total > 0;
        return valid ? Fat : null;
    }
}

/// <summary>
/// Scans a source: its volumes and, in each, the deleted items the file system still describes. Volumes are the ones the
/// partition table lists (a damaged first sector read from its backup), then the ones found where no partition is
/// listed (<see cref="PartitionSearch"/>): a lost partition's whole file system is listed, existing files too.
/// </summary>
public static class RecoveryScanner
{
    public static IReadOnlyList<RecoveryVolume> Scan(IBlockSource source, CancellationToken ct, RecoveryScanOptions? options = null)
    {
        var warnings = new List<string>();
        var listed = PartitionTable.Find(source, warnings);
        // Partitions the table lists; a damaged first sector is read from its backup.
        var slots = listed.Select(s => s.Lost || FirstSectorKnown(source, s) ? s : PartitionSearch.Repair(source, s)).ToList();
        // Space no partition holds: partitions deleted from the table, or a table that was erased.
        var inUse = listed.Where(s => !s.Lost).ToList();
        var lost = PartitionSearch.Quick(source, listed, ct);
        if (options?.SearchDisk == true) lost.AddRange(PartitionSearch.Deep(source, listed, lost, options.DiskProgress, ct));
        // One found at the very start is the source's own file system (a volume, a superfloppy), read from its backup.
        foreach (var found in lost.OrderBy(s => s.Offset))
            slots.Add(found with { Number = slots.Count + 1, Origin = found.Offset == 0 ? VolumeOrigin.Table : found.Origin });
        var volumes = new List<RecoveryVolume>();
        foreach (var slot in slots)
        {
            ct.ThrowIfCancellationRequested();
            var window = new CachedSource(new WindowSource(source, slot.Offset, slot.Length, $"{source.Description}, volume {slot.Number}"));
            RecoveryVolume volume;
            try
            {
                var boot = slot.Boot ?? window.ReadExactly(0, 512);
                volume = FileSystems.Detect(boot) switch
                {
                    FileSystems.Fat => FatScanner.Scan(window, boot, slot, ct, options),
                    FileSystems.ExFat => ExFatScanner.Scan(window, boot, slot, ct),
                    FileSystems.Ntfs => NtfsScanner.Scan(window, boot, slot, ct),
                    _ => Unsupported(slot, FileSystems.Unrecognized(window)),
                };
            }
            catch (InvalidDataException ex)
            {
                volume = Unsupported(slot, "The file system is damaged beyond what FileCat reads: " + ex.Message);
            }
            catch (IOException ex)
            {
                volume = Unsupported(slot, "The volume could not be read: " + ex.Message);
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidCastException or OutOfMemoryException)
            {
                // A structure FileCat's readers did not expect: a damage report, never a crash (and a finding for tests).
                volume = Unsupported(slot, $"The file system is damaged beyond what FileCat reads (internal: {ex.GetType().Name}).");
            }
            volume.Warnings.InsertRange(0, warnings);
            warnings.Clear();
            volume.Found = slot.Found;
            volume.Origin = slot.Origin;
            volume.DamagedStart = slot.Boot is not null;
            Number(volume.Root, prune: !slot.WholeFileSystem);
            if (volume.Orphans is { } orphans) Number(orphans, prune: !slot.WholeFileSystem);
            if (slot.WholeFileSystem) DeletedBefore(volume.Root);
            if (slot.Lost) MarkOverlaps(volume, inUse);
            volumes.Add(volume);
        }
        if (volumes.Count == 0)
        {
            var none = Unsupported(new VolumeSlot(1, 0, source.Length, null),
                "No partition table and no NTFS, FAT, or exFAT file system were found where partitions usually start.");
            none.Warnings.InsertRange(0, warnings);
            volumes.Add(none);
        }
        return volumes;
    }

    /// <summary>A lost file system lists existing and deleted items alike: the deleted ones say so.</summary>
    private static void DeletedBefore(RecoveryItem folder)
    {
        foreach (var item in folder.Children)
        {
            if (item.IsDeleted) item.Evidence.Insert(0, "It was deleted before the file system was lost.");
            if (item.IsDirectory) DeletedBefore(item);
        }
    }

    /// <summary>Whether a listed partition's first sector is one of the file systems FileCat reads (else its backup is looked for).</summary>
    private static bool FirstSectorKnown(IBlockSource source, VolumeSlot slot)
    {
        try
        {
            return slot.Length < 512 || FileSystems.Detect(source.ReadExactly(slot.Offset, 512)) is not null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return true; // unreadable: the scan says so
        }
    }

    /// <summary>
    /// A lost partition may lie partly where a partition is now (created after it was deleted): files with bytes there may
    /// have been overwritten by it, so they are uncertain rather than recoverable, and say where.
    /// </summary>
    private static void MarkOverlaps(RecoveryVolume volume, IReadOnlyList<VolumeSlot> inUse)
    {
        var overlapping = inUse.Where(s => s.Offset < volume.Offset + volume.Length && volume.Offset < s.Offset + s.Length).ToList();
        if (overlapping.Count == 0) return;
        volume.Warnings.Add($"Part of this lost partition lies where partition {string.Join(" and ", overlapping.Select(s => s.Number))} is now: files there may have been overwritten.");
        void Visit(RecoveryItem folder)
        {
            foreach (var item in folder.Children)
            {
                if (item.IsDirectory)
                {
                    Visit(item);
                    continue;
                }
                if (item.State is not (RecoveryState.Recoverable or RecoveryState.Partial)) continue;
                var hit = overlapping.FirstOrDefault(s => item.Extents.Any(e => e.State is ExtentState.Free or ExtentState.Owned &&
                    volume.Offset + e.Offset < s.Offset + s.Length && s.Offset < volume.Offset + e.Offset + e.Length));
                if (hit is null) continue;
                item.State = RecoveryState.Uncertain;
                item.Evidence.Insert(0, $"Part of it lies where partition {hit.Number} is now, which may have overwritten it: check the file after recovering it.");
            }
        }
        Visit(volume.Root);
    }

    private static RecoveryVolume Unsupported(VolumeSlot slot, string reason)
    {
        var volume = new RecoveryVolume { FileSystem = "Unknown", Offset = slot.Offset, Length = slot.Length, Root = new RecoveryItem { Name = "", IsDirectory = true } };
        volume.Warnings.Add(reason);
        return volume;
    }

    /// <summary>
    /// Tells apart items that share a name in one folder and, with <paramref name="prune"/>, drops folders with nothing
    /// deleted below them (a lost file system keeps everything: all of it is lost).
    /// </summary>
    internal static void Number(RecoveryItem folder, bool prune = true)
    {
        if (prune) folder.Children.RemoveAll(c => !c.IsDeleted && !c.HasDeletedDescendants);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in folder.Children)
        {
            child.Parent = folder;
            seen.TryGetValue(child.Name, out int count);
            child.Ordinal = count;
            seen[child.Name] = count + 1;
            if (child.IsDirectory) Number(child, prune);
        }
    }
}
