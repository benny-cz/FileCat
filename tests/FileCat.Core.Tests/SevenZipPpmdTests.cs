extern alias UpstreamSharpCompress;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using SharpCompress.Compressors.PPMd;
using UpstreamSharpCompress::SharpCompress.Archives;
using LzmaRangeDecoder = SharpCompress.Compressors.LZMA.RangeCoder.Decoder;
using UpstreamPpmdProperties = UpstreamSharpCompress::SharpCompress.Compressors.PPMd.PpmdProperties;
using UpstreamPpmdStream = UpstreamSharpCompress::SharpCompress.Compressors.PPMd.PpmdStream;
using UpstreamSevenZipArchive = UpstreamSharpCompress::SharpCompress.Archives.SevenZip.SevenZipArchive;

namespace FileCat.Core.Tests;

/// <summary>
/// 7z's PPMd (variant H with 7-Zip's range coder) through FileCat's shared PPMd model (release issue I14). Every PPMd 7z
/// fixture must decode byte-identically through FileCat's archive path, the upstream SharpCompress reader (whose PPMd-H is
/// UnRAR's, test-only) and 7-Zip's own extraction: pinned in FileCat.PPMd.json by eng/make-ppmd-fixtures.py, and run
/// again wherever 7-Zip is installed (FILECAT_7Z names it elsewhere).
/// </summary>
public sealed class SevenZipPpmdTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed record Member(string Path, long Bytes, string SHA256);

    private sealed record Fixture(string Name, long Bytes, string SHA256, string Owner, string? Switches, string[] Methods7Zip, Member[] Members7Zip);

    private sealed record Manifest(string Generator, string SevenZip, Fixture[] Fixtures);

    private static Manifest Load() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(Path.Combine(Fixtures, "FileCat.PPMd.json")))
        ?? throw new InvalidDataException("FileCat.PPMd.json is empty.");

    public static TheoryData<string> PpmdFixtures() => new(Load().Fixtures.Select(f => f.Name));

    [Theory]
    [MemberData(nameof(PpmdFixtures))]
    public void Members_decode_as_7zip_and_the_upstream_reader_decode_them(string name)
    {
        var fixture = Load().Fixtures.Single(f => f.Name == name);
        string path = Path.Combine(Fixtures, name);
        Assert.Equal(fixture.SHA256, Hash(File.ReadAllBytes(path)));
        Assert.All(fixture.Methods7Zip.Where(m => m.Contains(':')), method => Assert.Contains("PPMD:o", method, StringComparison.Ordinal));
        var expected = fixture.Members7Zip.Select(m => new Member(m.Path, m.Bytes, m.SHA256)).OrderBy(m => m.Path, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(expected);

        Assert.Equal(expected, Ours(path));
        Assert.Equal(expected, Upstream(path));
        if (SevenZipExecutable() is { } sevenZip) Assert.Equal(expected, SevenZipExtract(sevenZip, path));
        else TestContext.Current.TestOutputHelper?.WriteLine("7-Zip is not installed here: its extraction pinned in FileCat.PPMd.json is the reference.");
    }

    [Fact]
    public void A_member_larger_than_the_model_memory_restarts_the_model()
    {
        // FileCat.PPMd.restarts.7z holds one 768 KiB member in one PPMd folder (order 6, 256 KiB of model memory): its
        // packed stream starts right after the 32-byte start header, so the shared model can decode it on its own.
        var fixture = Load().Fixtures.Single(f => f.Name == "FileCat.PPMd.restarts.7z");
        var member = Assert.Single(fixture.Members7Zip);
        var (order, memory) = PpmdParameters(Assert.Single(fixture.Methods7Zip, m => m.Contains(':')));
        Assert.True(member.Bytes > memory, "The member must be larger than the model memory.");
        byte[] archive = File.ReadAllBytes(Path.Combine(Fixtures, fixture.Name));
        var decoder = new LzmaRangeDecoder();
        decoder.Init(new MemoryStream(archive, 32, archive.Length - 32));
        var model = new PpmdModelH<Ppmd7zRangeDecoder>();
        model.Init(order, memory);
        model.Coder = new Ppmd7zRangeDecoder(decoder);
        var output = new byte[member.Bytes];
        for (int i = 0; i < output.Length; i++)
        {
            int symbol = model.DecodeSymbol();
            Assert.InRange(symbol, 0, 255);
            output[i] = (byte)symbol;
        }
        Assert.Equal(member.SHA256, Hash(output));
        TestContext.Current.TestOutputHelper?.WriteLine($"{fixture.Name}: the model started {model.Starts} times for {member.Bytes} bytes in {memory} bytes of memory.");
        Assert.True(model.Starts >= 3, $"The model started only {model.Starts} times.");
    }

    [Theory]
    // Order 64 is beyond the 32 7-Zip writes but within what 7z's PPMd allows; 64 KiB fills (and restarts) quickly.
    [InlineData(2, 1 << 16, 300_000, 1)]
    [InlineData(6, 1 << 20, 250_000, 2)]
    [InlineData(16, 3 << 19, 200_000, 3)]
    [InlineData(33, 1 << 16, 200_000, 4)]
    [InlineData(64, 16 << 20, 150_000, 5)]
    public void Ppmd7z_streams_with_an_end_mark_decode_as_upstream_decodes_them(int order, int memory, int size, int seed)
    {
        byte[] content = PpmdBenchmark.Text(size, seed);
        var packed = new List<byte>();
        var encoder = new Rar3PpmdTestEncoder(sevenZip: true);
        encoder.StartBytes(packed, reset: true, order, memory);
        foreach (byte b in content) encoder.EncodeByte(b);
        encoder.EncodeEnd();
        encoder.Flush();
        byte[] properties = new byte[5];
        properties[0] = (byte)order;
        BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(1), memory);

        // FileCat's build: the stream ends at the end mark and stays ended.
        using (var ours = PpmdStream.Create(new PpmdProperties(properties), new MemoryStream([.. packed]), false))
        {
            Assert.Equal(content, ReadToEnd(ours));
            Assert.Equal(0, ours.Read(new byte[16], 0, 16));
        }
        // Upstream's PPMd-H (UnRAR's model) decodes the very same bytes before the mark (read no further: what it does
        // after the mark is no reference).
        using var upstream = UpstreamPpmdStream.Create(new UpstreamPpmdProperties(properties), new MemoryStream([.. packed]), false);
        byte[] reference = new byte[content.Length];
        upstream.ReadExactly(reference);
        Assert.Equal(content, reference);
    }

    [Theory]
    [InlineData(1, 1 << 20, typeof(InvalidDataException))]          // an order below 2
    [InlineData(65, 1 << 20, typeof(InvalidDataException))]         // an order above 64
    [InlineData(6, 2047, typeof(InvalidDataException))]             // less memory than the order-0 context needs
    [InlineData(6, -16, typeof(InvalidDataException))]              // a size with its top bit set
    [InlineData(6, (1 << 30) + 1, typeof(NotSupportedException))]   // more than FileCat gives a PPMd model
    public void Ppmd7z_parameters_out_of_range_are_refused_before_any_memory_is_taken(int order, int memory, Type error)
    {
        byte[] properties = new byte[5];
        properties[0] = (byte)order;
        BinaryPrimitives.WriteInt32LittleEndian(properties.AsSpan(1), memory);
        var thrown = Record.Exception(() => PpmdStream.Create(new PpmdProperties(properties), new MemoryStream(new byte[64]), false));
        Assert.IsType(error, thrown);
    }

    [Fact]
    public void Damaged_ppmd_data_is_refused_or_decoded_as_upstream_decodes_it()
    {
        // Damage aimed at the packed data itself (between the start header and the archive's header), where FileCat's model
        // and the guarded 7z coder take every byte. SharpCompress's entry streams do not check 7z's CRCs, so damaged data
        // may also decode to other bytes; then it must be the very bytes upstream's PPMd-H (UnRAR's model) decodes from it,
        // as both run the same deterministic model, unless upstream gives up first. FileCat may only refuse with the
        // exceptions its readers expect, and never hang.
        var run = Task.Factory.StartNew(() =>
        {
            var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in new[] { "FileCat.PPMd.o2.m64k.7z", "FileCat.PPMd.solid.7z", "7Zip.PPMd.7z" })
            {
                var original = Load().Fixtures.Single(f => f.Name == name).Members7Zip.OrderBy(m => m.Path, StringComparer.Ordinal).ToList();
                byte[] archive = File.ReadAllBytes(Path.Combine(Fixtures, name));
                long packedEnd = 32 + (long)BinaryPrimitives.ReadUInt64LittleEndian(archive.AsSpan(12, 8));
                var random = new Random(name.Length * 977);
                for (int round = 0; round < 30; round++)
                {
                    byte[] damaged = (byte[])archive.Clone();
                    for (int flips = 1 + random.Next(6); flips > 0; flips--)
                        damaged[32 + random.Next((int)(packedEnd - 32))] ^= (byte)(1 << random.Next(8));
                    string path = Path.Combine(_dir.Path, $"{round}-{name}");
                    File.WriteAllBytes(path, damaged);
                    List<Member>? ours = null, upstream = null;
                    try { ours = Ours(path); }
                    catch (Exception ex) when (ex is InvalidDataException or IOException) { }
                    try { upstream = Upstream(path); }
                    catch (Exception) { } // the reference may fail in any way it likes
                    string outcome = ours is null ? "refused" : ours.SequenceEqual(original) ? "same" : "changed";
                    if (ours is not null && upstream is not null) Assert.Equal(upstream, ours);
                    else if (ours is not null) outcome += ", upstream failed";
                    outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
                }
            }
            return outcomes;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(run.Wait(TimeSpan.FromMinutes(5)), "Reading damaged PPMd data did not end within five minutes (a loop?).");
        var result = run.Result;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("; ", result.Select(o => $"{o.Key}: {o.Value}")));
        Assert.True(result.Keys.Any(k => k != "same"), "No damaged copy decoded differently or was refused: the damage missed the PPMd data.");
    }

    private static (int Order, int Memory) PpmdParameters(string method)
    {
        var match = Regex.Match(method, @"PPMD:o(\d+):mem(\d+)$");
        Assert.True(match.Success, method);
        return (int.Parse(match.Groups[1].Value), 1 << int.Parse(match.Groups[2].Value));
    }

    /// <summary>Every file member as FileCat's archive provider lists and reads it (the path the file panels take).</summary>
    private List<Member> Ours(string path)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        string temp = _dir.Dir("tmp-" + Guid.NewGuid().ToString("N")[..8]);
        var zip = new ZipProvider(temp);
        providers.Register(zip);
        var archives = new ArchiveProvider(temp, providers);
        providers.Register(archives);
        zip.OtherArchives = archives;
        zip.SpoolForeignMember = archives.Spool;
        var members = new List<Member>();
        var folders = new Queue<Location>([archives.GetContainerLocation(path) ?? throw new InvalidDataException("not taken for an archive")]);
        while (folders.Count > 0)
        {
            var folder = folders.Dequeue();
            var entries = new List<EntryData>();
            archives.EnumerateAsync(folder, new Sink(entries), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            foreach (var entry in entries)
            {
                string memberPath = folder.Path.Length == 0 ? entry.Name : folder.Path + "/" + entry.Name;
                if (entry.Kind == EntryKind.Directory)
                {
                    folders.Enqueue(folder.WithPath(memberPath));
                    continue;
                }
                using var content = archives.OpenContent(archives.GetItemRef(folder, entry));
                byte[] bytes = content is null ? [] : new byte[content.Length];
                int total = 0, n;
                while (total < bytes.Length && (n = content!.Read(total, bytes.AsSpan(total))) > 0) total += n;
                Assert.Equal(bytes.Length, total);
                members.Add(new Member(memberPath, bytes.Length, Hash(bytes)));
            }
        }
        return [.. members.OrderBy(m => m.Path, StringComparer.Ordinal)];
    }

    /// <summary>Every file member as upstream SharpCompress (test-only) extracts it, in archive order.</summary>
    private static List<Member> Upstream(string path)
    {
        var members = new List<Member>();
        using var archive = UpstreamSevenZipArchive.OpenArchive(path);
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory) continue;
            string key = (entry.Key ?? "").Replace('\\', '/');
            if (entry.Size == 0)
            {
                members.Add(new Member(key, 0, Hash([])));
                continue;
            }
            using var content = reader.OpenEntryStream();
            byte[] bytes = ReadToEnd(content);
            members.Add(new Member(key, bytes.Length, Hash(bytes)));
        }
        return [.. members.OrderBy(m => m.Path, StringComparer.Ordinal)];
    }

    private static string? SevenZipExecutable() =>
        new[] { Environment.GetEnvironmentVariable("FILECAT_7Z"), @"C:\Program Files\7-Zip\7z.exe", "/usr/bin/7z", "/usr/bin/7za", "/opt/homebrew/bin/7zz" }
            .FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

    /// <summary>Every file 7-Zip itself extracts from the archive.</summary>
    private List<Member> SevenZipExtract(string sevenZip, string path)
    {
        string target = _dir.Dir("7z-" + Guid.NewGuid().ToString("N")[..8]);
        var start = new ProcessStartInfo(sevenZip) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "x", "-y", "-o" + target, path }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "7-Zip did not finish extracting within two minutes.");
        Assert.True(process.ExitCode == 0, $"7-Zip exit code {process.ExitCode}: {output.Result} {errors.Result}");
        return [.. Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Select(file => { byte[] bytes = File.ReadAllBytes(file); return new Member(Path.GetRelativePath(target, file).Replace('\\', '/'), bytes.Length, Hash(bytes)); })
            .OrderBy(m => m.Path, StringComparer.Ordinal)];
    }

    private static byte[] ReadToEnd(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }
}
