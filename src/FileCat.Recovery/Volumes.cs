using System.Buffers.Binary;
using System.Text;

namespace FileCat.Recovery;

/// <summary>A place on a source where a file system may start: a partition, or the whole source.</summary>
public sealed record VolumeSlot(int Number, long Offset, long Length, string? PartitionName);

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
        if (first[510] != 0x55 || first[511] != 0xAA) return [];
        if (Enumerable.Range(0, 4).Any(i => first[446 + i * 16 + 4] == 0xEE) && Gpt(source, warnings) is { } gpt) return gpt;
        return Mbr(source, first, warnings);
    }

    private static List<VolumeSlot>? Gpt(IBlockSource source, List<string> warnings)
    {
        foreach (int sector in new[] { 512, 4096 })
        {
            if (source.Length < sector * 2) continue;
            var header = source.ReadExactly(sector, 92);
            if (!header.AsSpan(0, 8).SequenceEqual("EFI PART"u8)) continue;
            long entriesLba = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(72));
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84));
            if (count > MaxGptEntries || size is < 128 or > 4096 || entriesLba <= 0 || entriesLba * sector >= source.Length)
            {
                warnings.Add("The GPT partition table is damaged; FileCat looked for file systems without it.");
                return null;
            }
            var slots = new List<VolumeSlot>();
            var table = source.ReadExactly(entriesLba * sector, (int)(count * size));
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
                slots.Add(new VolumeSlot(slots.Count + 1, firstLba * sector, (lastLba - firstLba + 1) * sector, name.Length > 0 ? name : null));
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

/// <summary>Scans a source: its volumes and, in each, the deleted items the file system still describes.</summary>
public static class RecoveryScanner
{
    public static IReadOnlyList<RecoveryVolume> Scan(IBlockSource source, CancellationToken ct)
    {
        var warnings = new List<string>();
        var slots = PartitionTable.Find(source, warnings);
        var volumes = new List<RecoveryVolume>();
        foreach (var slot in slots)
        {
            ct.ThrowIfCancellationRequested();
            var window = new CachedSource(new WindowSource(source, slot.Offset, slot.Length, $"{source.Description}, volume {slot.Number}"));
            RecoveryVolume volume;
            try
            {
                var boot = window.ReadExactly(0, 512);
                volume = FileSystems.Detect(boot) switch
                {
                    FileSystems.Fat => FatScanner.Scan(window, boot, slot, ct),
                    FileSystems.ExFat => ExFatScanner.Scan(window, boot, slot, ct),
                    FileSystems.Ntfs => NtfsScanner.Scan(window, boot, slot, ct),
                    _ => Unsupported(slot, "No NTFS, FAT, or exFAT file system was found here."),
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
            Number(volume.Root);
            if (volume.Orphans is { } orphans) Number(orphans);
            volumes.Add(volume);
        }
        if (volumes.Count == 0)
        {
            var none = Unsupported(new VolumeSlot(1, 0, source.Length, null), "No partition table and no NTFS, FAT, or exFAT file system were found.");
            none.Warnings.InsertRange(0, warnings);
            volumes.Add(none);
        }
        return volumes;
    }

    private static RecoveryVolume Unsupported(VolumeSlot slot, string reason)
    {
        var volume = new RecoveryVolume { FileSystem = "Unknown", Offset = slot.Offset, Length = slot.Length, Root = new RecoveryItem { Name = "", IsDirectory = true } };
        volume.Warnings.Add(reason);
        return volume;
    }

    /// <summary>Tells apart items that share a name in one folder, and drops folders with nothing deleted below them.</summary>
    internal static void Number(RecoveryItem folder)
    {
        folder.Children.RemoveAll(c => !c.IsDeleted && !c.HasDeletedDescendants);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var child in folder.Children)
        {
            child.Parent = folder;
            seen.TryGetValue(child.Name, out int count);
            child.Ordinal = count;
            seen[child.Name] = count + 1;
            if (child.IsDirectory) Number(child);
        }
    }
}
