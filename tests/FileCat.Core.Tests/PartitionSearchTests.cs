using System.Buffers.Binary;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>
/// Deleted partitions and damaged tables (D-46): the fixture disks from eng/make-recovery-fixtures.sh with their tables
/// erased or their first sectors damaged the way it happens on real disks. What was lost is found again, its whole file
/// system listed; nothing is found that is not there.
/// </summary>
public sealed class PartitionSearchTests : IDisposable
{
    private const int MiB = 1024 * 1024;
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static byte[] Fixture(string name) => File.ReadAllBytes(RecoveryFixtures.Image(name));

    private (IReadOnlyList<RecoveryVolume> Volumes, ImageFileSource Source) Scan(byte[] disk, RecoveryScanOptions? options = null)
    {
        string path = Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".img");
        File.WriteAllBytes(path, disk);
        var source = new ImageFileSource(path);
        return (RecoveryScanner.Scan(source, TestContext.Current.CancellationToken, options), source);
    }

    private static byte[] Recover(IBlockSource source, RecoveryVolume volume, RecoveryItem item)
    {
        using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), item);
        var data = new byte[content.Length];
        long done = 0;
        while (done < data.Length)
        {
            int n = content.Read(done, data.AsSpan((int)done));
            if (n <= 0) break;
            done += n;
        }
        return data;
    }

    /// <summary>A disk with an MBR whose partitions are (type, first sector, sectors), holding <paramref name="volumes"/> at their places.</summary>
    private static byte[] Disk(int mib, (byte Type, long Start, long Sectors)[] entries, params (long Offset, byte[] Bytes)[] volumes)
    {
        var disk = new byte[mib * MiB];
        for (int i = 0; i < entries.Length; i++)
        {
            var e = disk.AsSpan(446 + i * 16, 16);
            e[4] = entries[i].Type;
            BinaryPrimitives.WriteUInt32LittleEndian(e[8..], (uint)entries[i].Start);
            BinaryPrimitives.WriteUInt32LittleEndian(e[12..], (uint)entries[i].Sectors);
        }
        disk[510] = 0x55;
        disk[511] = 0xAA;
        foreach (var (offset, bytes) in volumes) bytes.CopyTo(disk, offset);
        return disk;
    }

    [Fact]
    public void Partitions_deleted_from_the_table_are_found_again_with_all_their_files()
    {
        var disk = Fixture("disk-mbr");
        disk.AsSpan(446, 64).Clear(); // every partition deleted: the table is empty, the volumes are still there
        var (volumes, source) = Scan(disk);
        using (source)
        {
            Assert.Equal(["FAT16", "exFAT"], volumes.Select(v => v.FileSystem));
            Assert.All(volumes, v => Assert.Equal(VolumeOrigin.Search, v.Origin));
            Assert.All(volumes, v => Assert.True(v.WholeFileSystem));
            Assert.Equal([1L * MiB, 21L * MiB], volumes.Select(v => v.Offset));
            Assert.Contains("boot sector at 1 MiB is intact", volumes[0].Found);
            Assert.Equal(["FIRST", "SECOND"], volumes.Select(v => v.Label?.Trim()));
            var first = RecoveryFixtures.Find(volumes[0].Root, "first.txt")!;
            var second = RecoveryFixtures.Find(volumes[1].Root, "second.txt")!;
            Assert.Equal(RecoveryFixtures.Content("first.txt", 7000), Recover(source, volumes[0], first));
            Assert.Equal(RecoveryFixtures.Content("second.txt", 9000), Recover(source, volumes[1], second));
        }
    }

    [Fact]
    public void A_GPT_disk_uses_the_copy_at_its_end_and_without_both_the_search_finds_the_partitions()
    {
        var disk = Fixture("disk-gpt");
        disk.AsSpan(512, 512).Clear(); // the GPT header at its start is erased
        var (volumes, source) = Scan(disk);
        using (source)
        {
            Assert.Equal(["FAT16", "exFAT"], volumes.Select(v => v.FileSystem));
            Assert.All(volumes, v => Assert.Equal(VolumeOrigin.BackupTable, v.Origin));
            Assert.NotNull(RecoveryFixtures.Find(volumes[1].Root, "second.txt"));
        }

        disk.AsSpan(disk.Length - 512, 512).Clear(); // and the copy at its end
        var (searched, again) = Scan(disk);
        using (again)
        {
            Assert.Equal(["FAT16", "exFAT"], searched.Select(v => v.FileSystem));
            Assert.All(searched, v => Assert.Equal(VolumeOrigin.Search, v.Origin));
            Assert.Contains(searched[0].Warnings, w => w.Contains("GPT partition table is damaged", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("ntfs", "NTFS")]
    [InlineData("fat32", "FAT32")]
    [InlineData("exfat", "exFAT")]
    public void A_volume_whose_first_sector_is_damaged_is_read_from_its_backup_boot_sector(string image, string fileSystem)
    {
        var bytes = Fixture(image);
        bytes.AsSpan(0, 512).Clear();
        var (volumes, source) = Scan(bytes);
        using (source)
        {
            var volume = Assert.Single(volumes);
            Assert.Equal(fileSystem, volume.FileSystem);
            Assert.Equal(VolumeOrigin.Table, volume.Origin); // the source's own file system, not a lost partition
            Assert.True(volume.DamagedStart);
            Assert.Contains("first sector is damaged", volume.Found);
            // The whole file system: existing files too, byte for byte, and the deleted ones as before.
            var keep = RecoveryFixtures.Find(volume.Root, "keep.txt");
            Assert.True(keep is { IsDeleted: false, State: RecoveryState.Recoverable }, RecoveryFixtures.Dump(volume.Root));
            Assert.Equal(RecoveryFixtures.Content("keep.txt", 1500), Recover(source, volume, keep!));
            var report = RecoveryFixtures.Find(volume.Root, "docs/report.txt")!;
            Assert.True(report.IsDeleted);
            Assert.Equal(RecoveryFixtures.Content("report.txt", 10000), Recover(source, volume, report));
        }
    }

    [Fact]
    public void A_listed_partition_with_a_damaged_first_sector_is_read_from_its_backup()
    {
        var ntfs = Fixture("ntfs");
        var disk = Disk(24, [(0x07, 2048, ntfs.Length / 512)], (MiB, ntfs));
        disk.AsSpan(MiB, 512).Clear();
        var (volumes, source) = Scan(disk);
        using (source)
        {
            var volume = Assert.Single(volumes);
            Assert.Equal("NTFS", volume.FileSystem);
            Assert.Equal(VolumeOrigin.Table, volume.Origin);
            Assert.True(volume.DamagedStart && volume.WholeFileSystem);
            Assert.Contains("NTFS's copy of it in its last sector", volume.Found);
            Assert.NotNull(RecoveryFixtures.Find(volume.Root, "keep.txt"));
        }
    }

    [Fact]
    public void The_deep_search_finds_what_is_not_where_partitions_usually_start()
    {
        // No table, and an NTFS volume at 1 MiB whose first sector is damaged: nothing the quick search reads shows it,
        // but its backup boot sector sits where the deep search reads (right before the megabyte it ends at).
        var ntfs = Fixture("ntfs");
        var disk = Disk(32, [], (MiB, ntfs));
        disk.AsSpan(MiB, 512).Clear();
        var (quick, source) = Scan(disk);
        using (source) Assert.Equal("Unknown", Assert.Single(quick).FileSystem);

        var progress = new List<(long Done, long Total)>();
        var (deep, again) = Scan(disk, new RecoveryScanOptions { SearchDisk = true, DiskProgress = (d, t) => progress.Add((d, t)) });
        using (again)
        {
            var volume = Assert.Single(deep);
            Assert.Equal(("NTFS", VolumeOrigin.Search, (long)MiB), (volume.FileSystem, volume.Origin, volume.Offset));
            Assert.Contains("NTFS's copy of it in its last sector", volume.Found);
            Assert.NotNull(RecoveryFixtures.Find(volume.Root, "keep.txt"));
            Assert.NotEmpty(progress);
            Assert.Equal(progress[^1].Total, progress[^1].Done);
        }
    }

    [Fact]
    public void Files_of_a_lost_partition_that_lie_under_a_newer_partition_are_uncertain()
    {
        // The exFAT partition was deleted, and a new partition made where its second megabyte on lies.
        var disk = Fixture("disk-mbr");
        var entries = disk.AsSpan(446, 64);
        entries[16..32].Clear();
        entries[20] = 0x83;
        BinaryPrimitives.WriteUInt32LittleEndian(entries[24..], 43008 + 2048);
        BinaryPrimitives.WriteUInt32LittleEndian(entries[28..], 38912);
        var (volumes, source) = Scan(disk);
        using (source)
        {
            Assert.Equal(["FAT16", "Unknown", "exFAT"], volumes.Select(v => v.FileSystem));
            var lost = volumes[2];
            Assert.Equal(VolumeOrigin.Search, lost.Origin);
            Assert.Contains(lost.Warnings, w => w.Contains("partition 2", StringComparison.Ordinal));
            var second = RecoveryFixtures.Find(lost.Root, "second.txt")!;
            Assert.Equal(RecoveryState.Uncertain, second.State);
            Assert.Contains("partition 2", second.Evidence[0]);
        }
    }

    [Fact]
    public void Nothing_is_found_where_there_is_nothing_and_listed_disks_are_unchanged()
    {
        var (empty, source) = Scan(Disk(16, []), new RecoveryScanOptions { SearchDisk = true });
        using (source) Assert.Equal("Unknown", Assert.Single(empty).FileSystem);

        var noise = new byte[8 * MiB];
        new Random(3).NextBytes(noise);
        var (random, again) = Scan(noise, new RecoveryScanOptions { SearchDisk = true });
        using (again) Assert.Equal("Unknown", Assert.Single(random).FileSystem);

        foreach (var image in new[] { "disk-mbr", "disk-gpt" })
        {
            var (listed, fixture) = Scan(Fixture(image), new RecoveryScanOptions { SearchDisk = true });
            using (fixture)
            {
                Assert.Equal(["FAT16", "exFAT"], listed.Select(v => v.FileSystem));
                Assert.All(listed, v => Assert.Equal((VolumeOrigin.Table, false), (v.Origin, v.WholeFileSystem)));
            }
        }
    }
}
