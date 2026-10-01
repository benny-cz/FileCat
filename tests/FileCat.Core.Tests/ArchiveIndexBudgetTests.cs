using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// I06 (aggregate memory): the archive providers keep the indexes of archives opened before for going back into them,
/// eight at most and only while their estimated sizes together stay within a limit (an index of a million members holds
/// about 560 MiB, ArchiveIndexScaleTests); the least recently used go first, the two used last stay (two panels may be
/// showing them), and an index that still feeds a viewer closes only when the viewer is done.
/// </summary>
public sealed class ArchiveIndexBudgetTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class Count : IEnumerationSink
    {
        public int Entries;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries += entries.Length;
        public void ReportIssue(string message) { }
    }

    private static string Name(int i) => $"folder-{i % 100:D4}/member-{i:D7}.txt";

    private string Zip(string name, int members)
    {
        string path = Path.Combine(_dir.Path, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        for (int i = 0; i < members; i++) archive.CreateEntry(Name(i), CompressionLevel.NoCompression);
        return path;
    }

    private string Tar(string name, int members)
    {
        string path = Path.Combine(_dir.Path, name);
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, TarEntryFormat.Pax);
        for (int i = 0; i < members; i++) writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, Name(i)));
        return path;
    }

    private static int List(ResourceProvider provider, Location archive)
    {
        var sink = new Count();
        provider.EnumerateAsync(archive, sink, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        Thread.Sleep(20); // distinct moments of use
        return sink.Entries;
    }

    /// <summary>Each archive's index estimate, read from a provider that holds only it.</summary>
    private long Alone(Func<ResourceProvider> provider, Location archive, Func<ResourceProvider, long> held)
    {
        var p = provider();
        List(p, archive);
        long bytes = held(p);
        (p as IDisposable)?.Dispose();
        return bytes;
    }

    [Fact]
    public void Zip_indexes_of_earlier_archives_are_kept_within_the_limit_least_recently_used_first() =>
        KeptWithinTheLimit(
            () => new ZipProvider(Path.Combine(_dir.Path, "zip-tmp")),
            (n, members) => ZipProvider.ForFile(Zip(n + ".zip", members)),
            (p, limit) => ((ZipProvider)p).RetainedIndexLimitBytes = limit,
            p => ((ZipProvider)p).RetainedIndexBytes,
            p => ((ZipProvider)p).RetainedIndexes);

    [Fact]
    public void Tar_indexes_of_earlier_archives_are_kept_within_the_limit_least_recently_used_first()
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        KeptWithinTheLimit(
            () => new ArchiveProvider(Path.Combine(_dir.Path, "tar-tmp"), providers),
            (n, members) => ArchiveProvider.ForFile(Tar(n + ".tar", members)),
            (p, limit) => ((ArchiveProvider)p).RetainedIndexLimitBytes = limit,
            p => ((ArchiveProvider)p).RetainedIndexBytes,
            p => ((ArchiveProvider)p).RetainedIndexes);
    }

    private void KeptWithinTheLimit(Func<ResourceProvider> create, Func<string, int, Location> archive, Action<ResourceProvider, long> limit,
        Func<ResourceProvider, long> held, Func<ResourceProvider, int> kept)
    {
        var a = archive("a", 5000);
        var b = archive("b", 6000);
        var d = archive("d", 5500);
        long sa = Alone(create, a, held), sb = Alone(create, b, held), sd = Alone(create, d, held);
        Assert.True(sa < sb && sd > sa && sd < sb);

        var provider = create();
        // Room for two of them, not three.
        limit(provider, sb + sd);
        Assert.Equal(100, List(provider, a));
        Assert.Equal(100, List(provider, b));
        Assert.Equal(2, kept(provider));
        Assert.Equal(sa + sb, held(provider));
        // A is used again: B is now the least recently used, and goes when D comes.
        List(provider, a);
        Assert.Equal(100, List(provider, d));
        Assert.Equal(2, kept(provider));
        Assert.Equal(sa + sd, held(provider));

        // The two used last stay whatever the limit, as the two panels may be showing them; the one before goes.
        limit(provider, sa - 1);
        Assert.Equal(100, List(provider, b));
        Assert.Equal(2, kept(provider));
        Assert.Equal(sd + sb, held(provider));
        (provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void A_member_being_read_outlives_its_index_being_let_go()
    {
        // A member past the in-memory limit is read as it is decompressed, from the index's open archive.
        string path = Path.Combine(_dir.Path, "big.zip");
        var data = new byte[ZipProvider.MaxMemberInMemory + (3 << 20)];
        new Random(6).NextBytes(data);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var stream = archive.CreateEntry("big.bin", CompressionLevel.NoCompression).Open())
            stream.Write(data);
        var provider = new ZipProvider(Path.Combine(_dir.Path, "zip-tmp"));
        var big = ZipProvider.ForFile(path);
        Assert.Equal(1, List(provider, big));
        using var content = provider.OpenContent(new ItemRef(big, "big.bin", EntryKind.File, data.Length, 0))!;

        // Two other archives opened with no room left: the big archive's index is let go while its member is open.
        provider.RetainedIndexLimitBytes = 1;
        List(provider, ZipProvider.ForFile(Zip("other.zip", 10)));
        List(provider, ZipProvider.ForFile(Zip("third.zip", 10)));
        Assert.Equal(2, provider.RetainedIndexes);

        var read = new byte[data.Length];
        int total = 0;
        for (int n; total < read.Length && (n = content.Read(total, read.AsSpan(total))) > 0;) total += n;
        Assert.Equal(data.Length, total);
        Assert.True(data.AsSpan().SequenceEqual(read));
    }
}
