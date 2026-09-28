using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Large archive members are decompressed as they are read (plan §15): the first bytes come at once, copies keep
/// nothing aside, damage is reported where it is found, and members of one solid stream can be read alternately.
/// </summary>
public sealed class ProgressiveContentTests : IDisposable
{
    private const int Large = 40 * 1024 * 1024; // above the 32 MiB kept in memory
    private readonly TempDir _dir = new();
    private readonly string _spool;
    private readonly ProviderRegistry _providers = new();
    private readonly ZipProvider _zip;
    private readonly ArchiveProvider _archives;

    public ProgressiveContentTests()
    {
        _spool = _dir.Dir("spool");
        _providers.Register(new LocalFileSystemProvider());
        _zip = new ZipProvider(_spool);
        _archives = new ArchiveProvider(_spool, _providers);
        _providers.Register(_zip);
        _providers.Register(_archives);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>Data that compresses, unlike at every 16-byte block (its offset is written into it).</summary>
    private static byte[] Pattern(int length, int seed)
    {
        var data = new byte[length];
        for (int i = 0; i + 16 <= length; i += 16)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i), i ^ seed);
            "filecat-test"u8.CopyTo(data.AsSpan(i + 4));
        }
        return data;
    }

    private string MakeZip(string name, byte[] content, CompressionLevel level = CompressionLevel.Fastest)
    {
        string path = Path.Combine(_dir.Path, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry("big.bin", level).Open();
        stream.Write(content);
        return path;
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => list.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    private static ItemRef Member(ResourceProvider provider, Location folder, string name)
    {
        var list = new List<EntryData>();
        provider.EnumerateAsync(folder, new Sink(list), CancellationToken.None).GetAwaiter().GetResult();
        return provider.GetItemRef(folder, list.Single(e => e.Name == name));
    }

    private static byte[] ReadAt(IContentSource content, long offset, int count)
    {
        var buffer = new byte[count];
        int done = 0;
        while (done < count)
        {
            int n = content.Read(offset + done, buffer.AsSpan(done));
            if (n <= 0) break;
            done += n;
        }
        return buffer[..done];
    }

    private static byte[] ReadAll(IContentSource content)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[1024 * 1024];
        long at = 0;
        int n;
        while ((n = content.Read(at, buffer)) > 0)
        {
            copy.Write(buffer, 0, n);
            at += n;
        }
        return copy.ToArray();
    }

    [Fact]
    public void A_large_zip_member_reads_anywhere_without_extracting_it_first()
    {
        var data = Pattern(Large, 1);
        string zip = MakeZip("large.zip", data);
        using var content = _zip.OpenContent(Member(_zip, ZipProvider.ForFile(zip), "big.bin"))!;
        Assert.IsType<ProgressiveContent>(content);
        Assert.Equal(Large, content.Length);
        Assert.Equal(data[..65536], ReadAt(content, 0, 65536));
        Assert.Equal(data[^4096..], ReadAt(content, Large - 4096, 4096));        // ahead: produced up to there
        Assert.Equal(data[1_000_000..1_100_000], ReadAt(content, 1_000_000, 100_000)); // behind: from what was kept
        Assert.Empty(ReadAt(content, Large, 10));
        Assert.Single(Directory.GetFiles(_spool, "member-*.tmp"));
        content.Dispose();
        Assert.Empty(Directory.GetFiles(_spool, "member-*.tmp"));
    }

    [Fact]
    public void A_copy_keeps_nothing_aside_and_the_member_checksum_is_verified()
    {
        var data = Pattern(Large, 2);
        string zip = MakeZip("copy.zip", data, CompressionLevel.NoCompression);
        var member = Member(_zip, ZipProvider.ForFile(zip), "big.bin");
        using (var content = ProgressiveContent.Sequential(_zip.OpenContent(member))!)
        {
            Assert.Equal(data, ReadAll(content));
            Assert.Empty(Directory.GetFiles(_spool, "member-*.tmp"));
        }

        // One flipped byte in the stored data: the start still reads, and reaching the end reports the damage.
        var bytes = File.ReadAllBytes(zip);
        int at = bytes.AsSpan().IndexOf(data.AsSpan(20_000_000, 64)) ;
        bytes[at] ^= 0xFF;
        string damaged = Path.Combine(_dir.Path, "damaged.zip");
        File.WriteAllBytes(damaged, bytes);
        using var broken = ProgressiveContent.Sequential(_zip.OpenContent(Member(_zip, ZipProvider.ForFile(damaged), "big.bin")))!;
        Assert.Equal(data[..65536], ReadAt(broken, 0, 65536));
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(broken));
        Assert.Contains("checksum", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => broken.Read(Large, new byte[10])); // never a clean end afterwards
    }

    [Fact]
    public void A_small_damaged_zip_member_is_refused_when_opened()
    {
        string zip = Path.Combine(_dir.Path, "small.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var w = new StreamWriter(archive.CreateEntry("a.txt", CompressionLevel.NoCompression).Open()))
            w.Write("hello world, this is the content");
        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.AsSpan().IndexOf("hello world"u8)] = (byte)'j';
        File.WriteAllBytes(zip, bytes);
        var ex = Assert.Throws<InvalidDataException>(() => _zip.OpenContent(Member(_zip, ZipProvider.ForFile(zip), "a.txt")));
        Assert.Contains("checksum", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extracting_a_damaged_large_member_fails_it_and_leaves_no_partial_file()
    {
        var data = Pattern(Large, 3);
        string zip = MakeZip("job.zip", data, CompressionLevel.NoCompression);
        var bytes = File.ReadAllBytes(zip);
        bytes[bytes.AsSpan().IndexOf(data.AsSpan(30_000_000, 64))] ^= 0x01;
        File.WriteAllBytes(zip, bytes);
        var jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
        string target = _dir.Dir("out");
        var job = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [Member(_zip, ZipProvider.ForFile(zip), "big.bin")], Destination = Location.FileSystem(target) });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished() && DateTime.UtcNow < deadline) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains(job.Issues, i => i.Message.Contains("checksum", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(target, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_member_shorter_than_declared_is_reported_as_damaged()
    {
        var data = Pattern(Large, 4);
        string zip = MakeZip("short.zip", data);
        // Claim 8 MiB more in the central directory (where .NET reads sizes from).
        var bytes = File.ReadAllBytes(zip);
        int central = bytes.AsSpan().LastIndexOf([(byte)0x50, (byte)0x4B, (byte)0x01, (byte)0x02]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), (uint)(Large + 8 * 1024 * 1024));
        File.WriteAllBytes(zip, bytes);
        using var content = _zip.OpenContent(Member(_zip, ZipProvider.ForFile(zip), "big.bin"))!;
        Assert.Equal(data[..4096], ReadAt(content, 0, 4096));
        var ex = Assert.Throws<InvalidDataException>(() => ReadAll(content));
        Assert.Contains("ended after", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_of_one_compressed_stream_can_be_read_alternately()
    {
        var first = Pattern(Large, 5);
        var second = Pattern(Large + 12345, 6);
        string tgz = Path.Combine(_dir.Path, "pair.tar.gz");
        using (var file = File.Create(tgz))
        using (var gz = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "first.bin") { DataStream = new MemoryStream(first) });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "second.bin") { DataStream = new MemoryStream(second) });
        }
        var root = ArchiveProvider.ForFile(tgz);
        using var a = _archives.OpenContent(Member(_archives, root, "first.bin"))!;
        using var b = _archives.OpenContent(Member(_archives, root, "second.bin"))!;
        Assert.IsType<ProgressiveContent>(a);
        // Each read takes the shared cursor from the other, which reopens and skips forward when it continues.
        Assert.Equal(first[..1_000_000], ReadAt(a, 0, 1_000_000));
        Assert.Equal(second[..1_000_000], ReadAt(b, 0, 1_000_000));
        Assert.Equal(first[1_000_000..3_000_000], ReadAt(a, 1_000_000, 2_000_000));
        Assert.Equal(second[5_000_000..6_000_000], ReadAt(b, 5_000_000, 1_000_000));
        Assert.Equal(first, ReadAll(a));
        Assert.Equal(second, ReadAll(b));
    }

    [Fact]
    public void Content_keeps_its_archive_open_after_the_archive_left_the_cache()
    {
        var data = Pattern(Large, 7);
        string zip = MakeZip("kept.zip", data);
        var content = _zip.OpenContent(Member(_zip, ZipProvider.ForFile(zip), "big.bin"))!;
        Assert.Equal(data[..1000], ReadAt(content, 0, 1000));
        // Nine other archives push this one out of the provider's cache of eight.
        for (int i = 0; i < 9; i++) Member(_zip, ZipProvider.ForFile(MakeZip($"other{i}.zip", [1, 2, 3])), "big.bin");
        Assert.Equal(data[^1000..], ReadAt(content, Large - 1000, 1000));
        content.Dispose();
        // Nothing holds the archive any more.
        using (new FileStream(zip, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public void A_file_in_a_disc_image_is_read_in_place()
    {
        var data = Pattern(Large, 8);
        string iso = Path.Combine(_dir.Path, "disc.iso");
        var builder = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true };
        builder.AddFile("big.bin", data);
        builder.Build(iso);
        using var content = _archives.OpenContent(Member(_archives, ArchiveProvider.ForFile(iso), "big.bin"))!;
        Assert.Equal(data[^5000..], ReadAt(content, Large - 5000, 5000));
        Assert.Equal(data[7_777_777..7_888_888], ReadAt(content, 7_777_777, 111_111));
        Assert.Empty(Directory.GetFiles(_spool, "member-*.tmp"));
    }
}
