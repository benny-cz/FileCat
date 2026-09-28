using System.Buffers.Binary;
using System.Text;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>
/// A FAT32 volume built in memory, sparse: 512-byte clusters and enough of them (140,000) that cluster numbers use both
/// halves. Tests lay out folders and files where they like and delete them the way Windows does, which also erases the
/// upper half of each entry's first cluster number.
/// </summary>
internal sealed class Fat32Image : IBlockSource
{
    public const int ClusterSize = 512;
    private const int Reserved = 32, Fats = 2;
    private readonly int _fatSectors;
    private readonly Dictionary<long, byte[]> _sectors = [];

    public Fat32Image(uint clusters = 140_000)
    {
        Clusters = clusters;
        _fatSectors = (int)(((clusters + 2) * 4L + 511) / 512);
        long total = Reserved + Fats * _fatSectors + clusters;
        Length = total * 512;
        var boot = new byte[512];
        boot[0] = 0xEB;
        boot[1] = 0x58;
        boot[2] = 0x90;
        Encoding.ASCII.GetBytes("MSWIN4.1").CopyTo(boot, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11), 512);
        boot[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14), Reserved);
        boot[16] = Fats;
        boot[21] = 0xF8;
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32), (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36), (uint)_fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(44), 2);
        boot[66] = 0x29;
        Encoding.ASCII.GetBytes("TESTVOL    FAT32   ").CopyTo(boot, 71);
        boot[510] = 0x55;
        boot[511] = 0xAA;
        Write(0, boot);
        SetFat(0, 0x0FFFFFF8);
        SetFat(1, 0x0FFFFFFF);
        SetFat(2, 0x0FFFFFFF); // the root folder: one cluster
    }

    public uint Clusters { get; }
    public string Description => "test FAT32 volume";
    public long Length { get; }

    public void SetFat(uint cluster, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        for (int copy = 0; copy < Fats; copy++) Write((Reserved + copy * _fatSectors) * 512L + cluster * 4L, bytes);
    }

    /// <summary>Content from a cluster on, as one run (the table is left alone: deleted files' clusters are free).</summary>
    public void Put(uint cluster, ReadOnlySpan<byte> data) => Write((Reserved + Fats * _fatSectors + (cluster - 2)) * 512L, data);

    /// <summary>An existing file's content with its chain in the table.</summary>
    public void PutAllocated(uint cluster, ReadOnlySpan<byte> data)
    {
        Put(cluster, data);
        uint count = (uint)Math.Max(1, (data.Length + ClusterSize - 1) / ClusterSize);
        for (uint i = 0; i < count; i++) SetFat(cluster + i, i + 1 < count ? cluster + i + 1 : 0x0FFFFFFF);
    }

    /// <summary>A folder's first cluster: "." (itself), "..", then the entries.</summary>
    public void Folder(uint cluster, uint parent, params byte[][] entries) =>
        Put(cluster, [.. Entry(".          ", 0x10, cluster, 0), .. Entry("..         ", 0x10, parent, 0), .. entries.SelectMany(e => e)]);

    /// <summary>
    /// A short entry ("NAME    EXT"). Deleted the way Windows does it: the first letter becomes 0xE5 and the upper half
    /// of the first cluster number is erased.
    /// </summary>
    public static byte[] Entry(string name83, byte attributes, uint start, uint size, bool deleted = false)
    {
        var e = new byte[32];
        Encoding.ASCII.GetBytes(name83).CopyTo(e, 0);
        if (deleted) e[0] = 0xE5;
        e[11] = attributes;
        ushort date = (ushort)((2024 - 1980) << 9 | 5 << 5 | 17), time = (ushort)(10 << 11 | 30 << 5);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(14), time);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(16), date);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(22), time);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(24), date);
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(20), deleted ? (ushort)0 : (ushort)(start >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(26), (ushort)start);
        BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(28), size);
        return e;
    }

    public static byte[] Deleted(string name83, uint start, byte[] content) => Entry(name83, 0x20, start, (uint)content.Length, deleted: true);

    private void Write(long offset, ReadOnlySpan<byte> data)
    {
        for (int done = 0; done < data.Length;)
        {
            long sector = (offset + done) / 512;
            int within = (int)((offset + done) % 512);
            if (!_sectors.TryGetValue(sector, out var bytes)) _sectors[sector] = bytes = new byte[512];
            int n = Math.Min(512 - within, data.Length - done);
            data.Slice(done, n).CopyTo(bytes.AsSpan(within));
            done += n;
        }
    }

    public int Read(long offset, Span<byte> buffer)
    {
        int count = (int)Math.Max(0, Math.Min(buffer.Length, Length - offset));
        for (int done = 0; done < count;)
        {
            long sector = (offset + done) / 512;
            int within = (int)((offset + done) % 512);
            int n = Math.Min(512 - within, count - done);
            if (_sectors.TryGetValue(sector, out var bytes)) bytes.AsSpan(within, n).CopyTo(buffer.Slice(done, n));
            else buffer.Slice(done, n).Clear();
            done += n;
        }
        return count;
    }

    /// <summary>The image as a file (sparse where nothing was written), for the recovery provider.</summary>
    public void Save(string path)
    {
        using var file = File.Create(path);
        file.SetLength(Length);
        foreach (var (sector, bytes) in _sectors)
        {
            file.Position = sector * 512;
            file.Write(bytes);
        }
    }

    public void Dispose()
    {
    }

    public static byte[] Program(int length, int seed) => Data(length, seed, "MZ"u8);

    public static byte[] Data(int length, int seed, ReadOnlySpan<byte> start = default)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        start.CopyTo(bytes);
        return bytes;
    }

    public static byte[] Text(int length, string word) =>
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(word + " line\r\n", length / (word.Length + 7) + 1)))[..length];
}

/// <summary>
/// FAT32 entries deleted by Windows lose the upper half of their first cluster number (found on a real USB stick, where
/// FileCat used to read such files from the wrong place and call them recoverable). Where the item listed before ends, the
/// one listed after starts, a folder's "." entry, and the data's own signature tell which place is right; otherwise the
/// item says its start is a guess.
/// </summary>
public sealed class ErasedFatStartTests
{
    private const uint Half = 0x10000;

    private static RecoveryVolume Scan(Fat32Image image) => Assert.Single(RecoveryScanner.Scan(image, TestContext.Current.CancellationToken));

    private static byte[] Recover(IBlockSource source, RecoveryVolume volume, RecoveryItem item)
    {
        using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), item);
        var data = new byte[content.Length];
        for (long done = 0; done < data.Length;)
        {
            int n = content.Read(done, data.AsSpan((int)done));
            if (n <= 0) break;
            done += n;
        }
        return data;
    }

    private static string Explain(RecoveryItem item) => $"{item.Name}: {item.State}. {string.Join(" ", item.Evidence)}";

    [Fact]
    public void Files_copied_one_after_another_are_found_where_they_really_start()
    {
        var image = new Fat32Image();
        // A deleted folder beyond cluster 65,535 and the files written into it, one after another.
        uint folder = Half + 4_464;
        var dll = Fat32Image.Program(1_300, 1);
        var text = Fat32Image.Text(900, "setup");
        var blob = Fat32Image.Data(400, 3);
        image.Put(2, [.. Fat32Image.Entry("SOURCES    ", 0x10, folder, 0, deleted: true)]);
        image.Folder(folder, 0,
            Fat32Image.Deleted("ACMIG   DLL", folder + 1, dll),
            Fat32Image.Deleted("NOTES   TXT", folder + 4, text),
            Fat32Image.Deleted("STATE   BIN", folder + 6, blob));
        image.Put(folder + 1, dll);
        image.Put(folder + 4, text);
        image.Put(folder + 6, blob);
        // Where the lower halves alone point, other deleted data that looks the part.
        image.Put(4_465, Fat32Image.Program(1_300, 9));
        image.Put(4_468, Fat32Image.Text(900, "other"));
        image.Put(4_470, Fat32Image.Data(400, 10));

        var volume = Scan(image);
        var sources = Assert.Single(volume.Root.Children); // "_OURCES": its first letter is lost
        Assert.Equal(3, sources.Children.Count);
        foreach (var (name, content) in new[] { ("ACMIG.DLL", dll), ("NOTES.TXT", text), ("STATE.BIN", blob) })
        {
            var item = sources.Children.Single(c => c.Name.EndsWith(name[1..], StringComparison.Ordinal));
            Assert.True(item.State == RecoveryState.Recoverable, Explain(item));
            Assert.Contains(item.Evidence, e => e.Contains("listed before it ends", StringComparison.Ordinal));
            Assert.Equal(content, Recover(image, volume, item));
        }
    }

    [Fact]
    public void A_file_is_placed_by_the_certain_item_listed_after_it()
    {
        var image = new Fat32Image();
        uint first = Half + 1_000;
        var data = Fat32Image.Data(700, 4); // two clusters, a type nothing recognizes
        uint folder = first + 2;
        image.Put(2, [.. Fat32Image.Deleted("FIRST   DAT", first, data), .. Fat32Image.Entry("NEXT       ", 0x10, folder, 0, deleted: true)]);
        image.Put(first, data);
        image.Put(1_000, Fat32Image.Data(700, 11)); // the lower half alone: other data
        image.Folder(folder, 0);

        var volume = Scan(image);
        var item = RecoveryFixtures.Find(volume.Root, "_IRST.DAT")!;
        Assert.True(item.State == RecoveryState.Recoverable, Explain(item));
        Assert.Contains(item.Evidence, e => e.Contains("listed after it starts", StringComparison.Ordinal));
        Assert.Equal(data, Recover(image, volume, item));
    }

    [Fact]
    public void The_only_place_whose_data_fits_the_type_is_where_the_file_starts()
    {
        var image = new Fat32Image();
        var jpeg = Fat32Image.Data(2_000, 5, [0xFF, 0xD8, 0xFF, 0xE0]);
        uint low = 300;
        image.Put(2, Fat32Image.Deleted("PHOTO   JPG", 2 * Half + low, jpeg));
        image.Put(2 * Half + low, jpeg);
        image.Put(low, Fat32Image.Text(2_000, "not a photo"));

        var volume = Scan(image);
        var item = RecoveryFixtures.Find(volume.Root, "_HOTO.JPG")!;
        Assert.True(item.State == RecoveryState.Recoverable, Explain(item));
        Assert.Contains(item.Evidence, e => e.Contains("only one holds data that begins like a .jpg file", StringComparison.Ordinal));
        Assert.Equal(jpeg, Recover(image, volume, item));
    }

    [Fact]
    public void A_file_nothing_places_says_its_start_is_a_guess()
    {
        var image = new Fat32Image();
        var data = Fat32Image.Data(600, 6);
        uint low = 500;
        image.PutAllocated(10, Fat32Image.Text(100, "kept"));
        image.Put(2, [.. Fat32Image.Entry("KEEP    TXT", 0x20, 10, 100), .. Fat32Image.Deleted("LONE    BIN", Half + low, data)]);
        image.Put(Half + low, data);
        image.Put(2 * Half + low, Fat32Image.Data(600, 12));

        var volume = Scan(image);
        var item = RecoveryFixtures.Find(volume.Root, "_ONE.BIN")!;
        Assert.True(item.State == RecoveryState.Uncertain, Explain(item));
        Assert.Equal(RecoveryItem.UncertainStart, item.Evidence[0]);
        using var content = new RecoveryContent(new WindowSource(image, volume.Offset, volume.Length, "volume"), item);
        Assert.Equal(RecoveryItem.UncertainStart, content.Caveat);
        Assert.Empty(content.MissingRanges);
    }

    [Fact]
    public void A_file_whose_every_possible_start_is_in_use_is_overwritten()
    {
        var image = new Fat32Image();
        uint low = 700;
        image.Put(2, Fat32Image.Deleted("GONE    TXT", Half + low, Fat32Image.Text(300, "gone")));
        for (uint c = low; c < image.Clusters + 2; c += Half) image.SetFat(c, 0x0FFFFFFF);

        var volume = Scan(image);
        var item = RecoveryFixtures.Find(volume.Root, "_ONE.TXT")!;
        Assert.Equal(RecoveryState.Overwritten, item.State);
        Assert.Contains(item.Evidence, e => e.Contains("All 3 places", StringComparison.Ordinal));
    }

    [Fact]
    public void A_deleted_folder_listing_goes_on_where_the_files_written_meanwhile_end()
    {
        var image = new Fat32Image();
        uint folder = Half + 14_464;
        image.Put(2, Fat32Image.Entry("BIG        ", 0x10, folder, 0, deleted: true));
        // The first cluster holds "." and ".." and 14 files; the listing's second cluster was added after the 14th was
        // written, right where it ended, and holds two more.
        var files = Enumerable.Range(1, 16).Select(i => (Name: $"F{i:D2}     BIN", Content: Fat32Image.Data(200, 100 + i))).ToList();
        var starts = new List<uint>();
        uint next = folder + 1;
        for (int i = 0; i < 16; i++)
        {
            if (i == 14) next++; // the listing's second cluster
            starts.Add(next++);
        }
        image.Folder(folder, 0, [.. files.Take(14).Select((f, i) => Fat32Image.Deleted(f.Name, starts[i], f.Content))]);
        image.Put(starts[13] + 1, [.. files.Skip(14).SelectMany((f, i) => Fat32Image.Deleted(f.Name, starts[14 + i], f.Content))]);
        for (int i = 0; i < 16; i++) image.Put(starts[i], files[i].Content);

        var volume = Scan(image);
        var big = Assert.Single(volume.Root.Children);
        Assert.Equal(16, big.Children.Count);
        Assert.Contains("Its list of contents survives.", big.Evidence);
        for (int i = 0; i < 16; i++)
        {
            var item = big.Children[i];
            Assert.True(item.State == RecoveryState.Recoverable, Explain(item));
            Assert.Equal(files[i].Content, Recover(image, volume, item));
        }
    }

    [Fact]
    public void A_deleted_folder_is_told_from_another_by_its_parent()
    {
        var image = new Fat32Image();
        // OLD (in the root, at 3,000) and NEW (in KEEP, at 3,000 + 65,536) share the lower half of their numbers, and both
        // start with a "." naming themselves; only ".." tells which is which.
        uint keep = 20, old = 3_000, @new = Half + 3_000;
        var inner = Fat32Image.Text(100, "inner");
        var outer = Fat32Image.Text(100, "outer");
        image.PutAllocated(keep, new byte[Fat32Image.ClusterSize]);
        image.Put(2, [.. Fat32Image.Entry("KEEP       ", 0x10, keep, 0), .. Fat32Image.Entry("OLD        ", 0x10, old, 0, deleted: true)]);
        image.Folder(keep, 0, Fat32Image.Entry("NEW        ", 0x10, @new, 0, deleted: true));
        image.Folder(@new, keep, Fat32Image.Deleted("INNER   TXT", @new + 1, inner));
        image.Put(@new + 1, inner);
        image.Folder(old, 0, Fat32Image.Deleted("OUTER   TXT", old + 1, outer));
        image.Put(old + 1, outer);

        var volume = Scan(image);
        var newFolder = RecoveryFixtures.Find(volume.Root, "KEEP/_EW")!;
        var oldFolder = RecoveryFixtures.Find(volume.Root, "_LD")!;
        Assert.Equal("_NNER.TXT", Assert.Single(newFolder.Children).Name);
        Assert.Equal("_UTER.TXT", Assert.Single(oldFolder.Children).Name);
        Assert.Equal(inner, Recover(image, volume, newFolder.Children[0]));
        Assert.Equal(outer, Recover(image, volume, oldFolder.Children[0]));
    }

    [Fact]
    public void File_data_is_not_taken_for_more_of_a_listing()
    {
        Assert.False(FatScanner.LooksLikeMoreEntries(Fat32Image.Data(512, 7)));
        Assert.False(FatScanner.LooksLikeMoreEntries(Fat32Image.Text(512, "2024-05-17 10:30:00 DONE")));
        Assert.False(FatScanner.LooksLikeMoreEntries(new byte[512]));
        Assert.False(FatScanner.LooksLikeMoreEntries([.. Fat32Image.Entry(".          ", 0x10, 5, 0), .. new byte[480]])); // a folder's start
        Assert.True(FatScanner.LooksLikeMoreEntries([.. Fat32Image.Entry("SETUP   EXE", 0x20, 5, 10, deleted: true), .. new byte[480]]));
    }

    [Theory]
    [InlineData("setup.exe", "4D5A9000", "Match")]
    [InlineData("setup.exe", "7F454C46", "Mismatch")]
    [InlineData("photo.JPG", "FFD8FFE1", "Match")]
    [InlineData("photo.jpg", "48656C6C", "Mismatch")]
    [InlineData("clip.mp4", "00000020667479706D703432", "Match")]
    [InlineData("notes.txt", "48656C6C6F0D0A", "Match")]
    [InlineData("notes.txt", "4D5A900003000000", "Mismatch")]
    [InlineData("setup.inf", "FFFE5B00", "Match")]
    [InlineData("setup.inf", "5B0056006500", "Match")]
    [InlineData("state.bin", "12345678", "Unknown")]
    [InlineData("setup.exe", "00000000", "Blank")]
    [InlineData("a.exe", "4D", "Unknown")] // one byte long: too short to tell
    public void Content_fits_its_type_by_its_first_bytes(string name, string hex, string fit) =>
        Assert.Equal(Enum.Parse<ContentFit>(fit), ContentSignature.Check(name, Convert.FromHexString(hex), hex.Length / 2));
}
