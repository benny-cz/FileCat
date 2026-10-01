using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// The file inspectors read files the user merely selects (trust boundary B02, plan V24): a damaged executable, picture,
/// recording, package or page must give a report with warnings, never an exception, a hang or an unbounded allocation,
/// and its report must render. Like the archive damage campaign, each round's damage depends only on the format and the
/// round's number (FILECAT_INSPECT_FUZZ_ROUNDS, _FORMAT and _START for long runs and replays).
/// </summary>
[Collection(nameof(AllocationMeasured))]
public sealed class InspectorFuzzTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    [Theory]
    [InlineData("pe")] // a native Windows program, fixed bytes (the archive fixtures' test.exe)
    [InlineData("pe-managed")] // this build's FileCat.Core.dll: replays only within one build
    [InlineData("jpeg-photo")] // the archive fixtures' test.jpg
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("gif")]
    [InlineData("elf")]
    [InlineData("elf-symbols")]
    [InlineData("macho")]
    [InlineData("macho-signed")]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("mp3")]
    [InlineData("mp4")]
    [InlineData("webm")]
    [InlineData("html")]
    [InlineData("apk")]
    public void Damaged_files_give_reports_with_warnings_never_exceptions(string format)
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_INSPECT_FUZZ_ROUNDS"), out var r) ? r : 200;
        int first = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_INSPECT_FUZZ_START"), out var s) ? s : 0;
        if (Environment.GetEnvironmentVariable("FILECAT_INSPECT_FUZZ_FORMAT") is { Length: > 0 } only && only != format)
            Assert.Skip($"FILECAT_INSPECT_FUZZ_FORMAT picks {only}.");
        var original = Seed(format);
        Assert.NotNull(Inspect(original, out var failure));
        Assert.Null(failure);
        long most = 0;
        int mostRound = first;
        for (int round = first; round < first + rounds; round++)
        {
            var data = Damage(format, original, round);
            long allocated = 0;
            Exception? error = null;
            var reading = Task.Run(() =>
            {
                long start = GC.GetAllocatedBytesForCurrentThread();
                try { Inspect(data, out error); }
                finally { allocated = GC.GetAllocatedBytesForCurrentThread() - start; }
            });
            if (!reading.Wait(TimeSpan.FromSeconds(30)))
                throw new Xunit.Sdk.XunitException($"{format}, round {round}: still inspecting after 30 s (a loop on damaged data?).");
            if (error is not null) throw new Xunit.Sdk.XunitException($"{format}, round {round}: {error}");
            if (allocated > most) (most, mostRound) = (allocated, round);
            Assert.True(allocated <= 256L << 20, $"{format}, round {round} allocated {allocated >> 20} MiB inspecting a {original.Length >> 10} KiB file.");
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{format}: rounds {first}..{first + rounds - 1}; most allocated by one round: {most >> 20} MB (round {mostRound})");
    }

    /// <summary>Rounds that once failed in long runs, kept so every run repeats them.</summary>
    [Theory]
    [InlineData("pe", 197769)] // an optional header shorter than its fields: the linker version was read past its end
    public void Rounds_that_once_failed_stay_fixed(string format, int round)
    {
        Inspect(Damage(format, Seed(format), round), out var failure);
        Assert.Null(failure);
    }

    /// <summary>One round's damage: bytes changed, more often in the first and last kilobyte, sometimes cut short.</summary>
    private static byte[] Damage(string format, byte[] original, int round)
    {
        var random = new Random(round * 7919 + format.Length * 31);
        var data = (byte[])original.Clone();
        int flips = 1 + random.Next(round < 20 ? 4 : 32);
        for (int f = 0; f < flips; f++)
        {
            int at = random.Next(3) switch
            {
                0 => random.Next(Math.Min(data.Length, 1024)),
                1 => data.Length - 1 - random.Next(Math.Min(data.Length, 1024)),
                _ => random.Next(data.Length),
            };
            data[at] = random.Next(3) switch { 0 => 0xFF, 1 => 0x00, _ => (byte)random.Next(256) };
        }
        if (random.Next(16) == 0) data = data[..random.Next(1, data.Length)];
        return data;
    }

    /// <summary>Inspects and renders the report; any exception but cancellation and I/O failure comes out in <paramref name="unexpected"/>.</summary>
    private static InspectionReport? Inspect(byte[] data, out Exception? unexpected)
    {
        unexpected = null;
        try
        {
            var report = Inspectors.Inspect(new MemoryContentSource("x", data), TestContext.Current.CancellationToken);
            _ = report?.ToText();
            return report;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or IOException))
        {
            unexpected = ex;
            return null;
        }
    }

    private static byte[] Seed(string format) => format switch
    {
        "pe" => FromArchive("exe", "test.exe"),
        "pe-managed" => File.ReadAllBytes(typeof(Inspectors).Assembly.Location),
        "jpeg-photo" => FromArchive("jpg", "test.jpg"),
        "png" => InspectorTests.Png(),
        "jpeg" => InspectorTests.Jpeg(),
        "gif" => InspectorTests.Gif(),
        "elf" => MoreInspectorTests.Elf(),
        "elf-symbols" => MoreInspectorTests.ElfWithSymbols(),
        "macho" => MoreInspectorTests.MachO(),
        "macho-signed" => MoreInspectorTests.MachOSigned(),
        "wav" => MoreInspectorTests.Wav()[..4096],
        "flac" => MoreInspectorTests.Flac(),
        "mp3" => MoreInspectorTests.Mp3()[..2048],
        "mp4" => MoreInspectorTests.Mp4(),
        "webm" => MoreInspectorTests.Webm(),
        "html" => MoreInspectorTests.Html(),
        "apk" => MoreInspectorTests.Zip(("AndroidManifest.xml", MoreInspectorTests.BinaryManifest())),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>A member of the TAR+xz fixture, read through FileCat's archive provider.</summary>
    private static byte[] FromArchive(string folder, string name)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        string temp = Path.Combine(Path.GetTempPath(), "filecat-inspect-fuzz-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var archives = new ArchiveProvider(temp, providers);
            providers.Register(archives);
            var location = archives.GetContainerLocation(Path.Combine(Fixtures, "Tar.tar.xz"))!.WithPath(folder);
            var entries = new List<EntryData>();
            archives.EnumerateAsync(location, new ListSink(entries), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            using var content = archives.OpenContent(archives.GetItemRef(location, entries.Single(e => e.Name == name)))!;
            var bytes = new byte[content.Length];
            int total = 0;
            for (int n; total < bytes.Length && (n = content.Read(total, bytes.AsSpan(total))) > 0;) total += n;
            return bytes;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class ListSink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }
}
