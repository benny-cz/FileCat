using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>The disk images from eng/make-recovery-fixtures.sh, unpacked once per test run.</summary>
internal static class RecoveryFixtures
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "filecat-recovery-fixtures", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static readonly object Lock = new();

    public static string Image(string name)
    {
        lock (Lock)
        {
            var path = Path.Combine(Folder, name + ".img");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(Folder);
            using (var input = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Recovery", name + ".img.gz")), CompressionMode.Decompress))
            using (var output = File.Create(path + ".part"))
                input.CopyTo(output);
            File.Move(path + ".part", path);
            return path;
        }
    }

    /// <summary>
    /// What the fixture script wrote into a file of this name and size (packed/gaps.txt has 128 KiB of zeros after its
    /// first 70,000 bytes, which NTFS keeps as sparse compression units).
    /// </summary>
    public static byte[] Content(string name, long size)
    {
        var bytes = new List<byte>((int)size + 32);
        for (int line = 0; bytes.Count < size; line++) bytes.AddRange(Encoding.UTF8.GetBytes($"{name}:{line:D8}\n"));
        var content = bytes.Take((int)size).ToArray();
        if (name == "gaps.txt" && size > 70000) content.AsSpan(70000, (int)Math.Min(131072, size - 70000)).Clear();
        return content;
    }

    public static RecoveryItem? Find(RecoveryItem root, string path)
    {
        var node = root;
        foreach (var part in path.Split('/'))
        {
            node = node.Children.FirstOrDefault(c => c.Name == part);
            if (node is null) return null;
        }
        return node;
    }

    public static string Dump(RecoveryItem node, string indent = "")
    {
        var sb = new StringBuilder();
        foreach (var c in node.Children)
        {
            sb.Append(indent).Append(c.Name).Append(c.IsDirectory ? "/" : "").Append(c.IsDeleted ? " [deleted " + c.State + "]" : "")
              .Append(' ').Append(c.Size).Append(" | ").AppendLine(string.Join(" ", c.Evidence));
            if (c.IsDirectory) sb.Append(Dump(c, indent + "  "));
        }
        return sb.ToString();
    }
}

public sealed class RecoveryEngineTests
{
    private static (IReadOnlyList<RecoveryVolume> Volumes, ImageFileSource Source) Scan(string image)
    {
        var source = new ImageFileSource(RecoveryFixtures.Image(image));
        return (RecoveryScanner.Scan(source, TestContext.Current.CancellationToken), source);
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

    [Theory]
    [InlineData("fat12", "FAT12")]
    [InlineData("fat16", "FAT16")]
    [InlineData("fat32", "FAT32")]
    [InlineData("exfat", "exFAT")]
    [InlineData("ntfs", "NTFS")]
    public void Deleted_files_come_back_byte_for_byte_and_nothing_is_claimed_that_is_not_there(string image, string fileSystem)
    {
        var (volumes, source) = Scan(image);
        using (source)
        {
            var volume = Assert.Single(volumes);
            Assert.Equal(fileSystem, volume.FileSystem);
            Assert.Equal("FIXTURE", volume.Label?.Trim());
            string tree = RecoveryFixtures.Dump(volume.Root);
            TestContext.Current.TestOutputHelper?.WriteLine(tree);

            // Deleted last, with nothing written after them: all of these must come back exactly.
            foreach (var (path, size) in new[]
            {
                ("docs/report.txt", 10000), ("docs/Long file name with spaces.txt", 5000), ("docs/Příliš žluťoučký kůň.txt", 3000),
                ("tiny.txt", 60), ("photos/a.jpg", 70000), ("photos/b.jpg", 12345),
            })
            {
                var item = RecoveryFixtures.Find(volume.Root, path);
                Assert.True(item is not null, $"{path} was not found in:\n{tree}");
                Assert.True(item.IsDeleted);
                Assert.Equal(size, item.Size);
                Assert.True(item.State == RecoveryState.Recoverable, $"{path}: {item.State} ({string.Join(" ", item.Evidence)})");
                Assert.Equal(RecoveryFixtures.Content(Path.GetFileName(path), size), Recover(source, volume, item));
            }
            var photos = RecoveryFixtures.Find(volume.Root, "photos")!;
            Assert.True(photos.IsDeleted && photos.IsDirectory);
            var docs = RecoveryFixtures.Find(volume.Root, "docs")!;
            Assert.False(docs.IsDeleted); // still there: only its deleted contents are listed

            // Existing files are not listed.
            Assert.Null(RecoveryFixtures.Find(volume.Root, "keep.txt"));
            // The truthfulness rule: every byte FileCat hands out as recovered is the file's own; lost bytes are declared.
            foreach (var item in All(volume.Root).Where(i => !i.IsDirectory && i.State is RecoveryState.Recoverable or RecoveryState.Partial))
            {
                var original = RecoveryFixtures.Content(item.Name, item.Size);
                using var content = new RecoveryContent(new WindowSource(source, volume.Offset, volume.Length, "volume"), item);
                var recovered = Recover(source, volume, item);
                var lost = content.MissingRanges;
                Assert.Equal(item.State == RecoveryState.Partial, lost.Count > 0);
                for (long at = 0; at < item.Size; at++)
                {
                    if (lost.Any(m => at >= m.Offset && at < m.Offset + m.Length))
                    {
                        Assert.Equal(0, recovered[at]); // lost bytes are zeros, and said to be lost
                        continue;
                    }
                    Assert.True(original[at] == recovered[at], $"{item.Name}: byte {at} is handed out as recovered but differs.\n{tree}");
                }
            }
            // Its entry survives in old/, but the clusters it had now belong (almost all) to fill/zeros.bin.
            var overwritten = RecoveryFixtures.Find(volume.Root, "old/overwritten.txt");
            Assert.True(overwritten is not null, "old/overwritten.txt was not found in:\n" + tree);
            Assert.True(overwritten.State is RecoveryState.Overwritten or RecoveryState.Partial, overwritten.State.ToString());
            Assert.Equal(20000, overwritten.Size);
            if (fileSystem == "NTFS")
            {
                // Compressed (LZNT1) files, one with all-zero units that NTFS keeps sparse: decompressed exactly.
                foreach (var (path, size) in new[] { ("packed/notes.txt", 200000), ("packed/gaps.txt", 231072) })
                {
                    var packed = RecoveryFixtures.Find(volume.Root, path);
                    Assert.True(packed is { State: RecoveryState.Recoverable, Compression: not null }, $"{path}: {packed?.State}\n{tree}");
                    Assert.Equal(RecoveryFixtures.Content(Path.GetFileName(path), size), Recover(source, volume, packed!));
                }
                Assert.Contains(RecoveryFixtures.Find(volume.Root, "packed/gaps.txt")!.Compression!.Units, u => u.Kind == CompressedUnitKind.Sparse);
            }
            // A file written in two pieces: exFAT's surviving chain and NTFS's runs give it back whole; FAT cannot know.
            var frag = RecoveryFixtures.Find(volume.Root, "frag-a.bin")!;
            Assert.Equal(fileSystem is "exFAT" or "NTFS" ? RecoveryState.Recoverable : RecoveryState.Partial, frag.State);
        }
    }

    private static IEnumerable<RecoveryItem> All(RecoveryItem node) => node.Children.SelectMany(c => c.IsDirectory ? All(c).Prepend(c) : [c]);

    [Theory]
    [InlineData("disk-mbr")]
    [InlineData("disk-gpt")]
    public void Partitioned_disks_show_each_volume(string image)
    {
        var (volumes, source) = Scan(image);
        using (source)
        {
            Assert.Equal(["FAT16", "exFAT"], volumes.Select(v => v.FileSystem));
            Assert.Equal(["FIRST", "SECOND"], volumes.Select(v => v.Label?.Trim()));
            var first = RecoveryFixtures.Find(volumes[0].Root, "first.txt")!;
            var second = RecoveryFixtures.Find(volumes[1].Root, "second.txt")!;
            Assert.Equal(RecoveryFixtures.Content("first.txt", 7000), Recover(source, volumes[0], first));
            Assert.Equal(RecoveryFixtures.Content("second.txt", 9000), Recover(source, volumes[1], second));
        }
    }

    [Fact]
    public void A_fragmented_FAT_file_is_honest_about_the_part_it_cannot_know()
    {
        var (volumes, source) = Scan("fat32");
        using (source)
        {
            var frag = RecoveryFixtures.Find(volumes[0].Root, "frag-a.bin")!;
            Assert.Equal(RecoveryState.Partial, frag.State);
            Assert.Contains(frag.Evidence, e => e.Contains("one continuous run", StringComparison.Ordinal));
            using var content = new RecoveryContent(new WindowSource(source, volumes[0].Offset, volumes[0].Length, "volume"), frag);
            Assert.Equal([(16384L, 24576L)], content.MissingRanges); // the second piece is not where FAT would guess
            Assert.Equal(RecoveryFixtures.Content("frag-a.bin", 16384), Recover(source, volumes[0], frag)[..16384]);
        }
    }

    /// <summary>Patches bytes of a source in memory, so a test can damage an image without touching it.</summary>
    private sealed class PatchedSource(IBlockSource inner, Dictionary<long, byte> patches) : IBlockSource
    {
        public string Description => inner.Description;
        public long Length => inner.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            int n = inner.Read(offset, buffer);
            foreach (var (at, value) in patches)
                if (at >= offset && at < offset + n) buffer[(int)(at - offset)] = value;
            return n;
        }

        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public void A_short_name_without_its_long_name_shows_its_lost_first_letter_as_underscore()
    {
        var image = RecoveryFixtures.Image("fat16");
        var bytes = File.ReadAllBytes(image);
        // The deleted short entry of docs/report.txt, and the long-name entry in front of it, which is made to look like
        // something else (as when its slot is reused).
        int at = bytes.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xE5, (byte)'E', (byte)'P', (byte)'O', (byte)'R', (byte)'T', 0x20, 0x20, (byte)'T', (byte)'X', (byte)'T']);
        Assert.True(at > 32);
        Assert.Equal(0x0F, bytes[at - 32 + 11]);
        using var source = new PatchedSource(new ImageFileSource(image), new() { [at - 32 + 11] = 0x20, [at - 32] = (byte)'Z' });
        var volume = Assert.Single(RecoveryScanner.Scan(source, TestContext.Current.CancellationToken));
        var docs = RecoveryFixtures.Find(volume.Root, "docs")!;
        var report = docs.Children.Single(c => c.Name.EndsWith("EPORT.TXT", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("_", report.Name);
        Assert.True(report.NameUncertain);
        Assert.Contains(report.Evidence, e => e.Contains("first letter", StringComparison.Ordinal));
        Assert.Equal(RecoveryState.Recoverable, report.State);
    }

    [Fact]
    public void Scanning_never_writes_to_the_image()
    {
        var image = RecoveryFixtures.Image("ntfs");
        var before = SHA256.HashData(File.ReadAllBytes(image));
        var stamp = File.GetLastWriteTimeUtc(image);
        var (_, source) = Scan("ntfs");
        source.Dispose();
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(image)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(image));
    }
}
