using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// V12 and I06's last clause (FILECAT_ARCHIVE_SCALE=&lt;members&gt;): what an archive of many members costs to keep open.
/// Both archive providers keep the indexes of archives opened before (ArchiveIndexBudgetTests); this measures one index
/// of a ZIP and of a TAR with that many members (empty files in a thousand folders), as managed memory after a full
/// collection, beside the estimate the providers bound their caches by.
/// </summary>
public sealed class ArchiveIndexScaleTests
{
    private sealed class Count : IEnumerationSink
    {
        public int Entries;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries += entries.Length;
        public void ReportIssue(string message) { }
    }

    private static string Name(int i) => $"folder-{i % 1000:D4}/member-{i:D7}.txt";

    private static long Settled()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [Fact]
    public async Task One_index_of_an_archive_of_many_members()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_SCALE"), out int members) || members < 1000)
        {
            Assert.Skip("Set FILECAT_ARCHIVE_SCALE to a number of members (at least 1,000) to measure archive indexes.");
            return;
        }
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        using var dir = new TempDir();
        string zip = Path.Combine(dir.Path, "many.zip");
        string tar = Path.Combine(dir.Path, "many.tar");
        var clock = Stopwatch.StartNew();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            for (int i = 0; i < members; i++) archive.CreateEntry(Name(i), CompressionLevel.NoCompression);
        using (var stream = File.Create(tar))
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax))
            for (int i = 0; i < members; i++) writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, Name(i)));
        log?.WriteLine($"{members:N0} members written in {clock.Elapsed.TotalSeconds:F1} s: ZIP {new FileInfo(zip).Length >> 20:N0} MiB, TAR {new FileInfo(tar).Length >> 20:N0} MiB");

        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        foreach (var (what, location, provider) in new (string, Location, ResourceProvider)[]
                 {
                     ("ZIP", ZipProvider.ForFile(zip), new ZipProvider(Path.Combine(dir.Path, "zip-tmp"))),
                     ("TAR", ArchiveProvider.ForFile(tar), new ArchiveProvider(Path.Combine(dir.Path, "tar-tmp"), providers)),
                 })
        {
            long before = Settled();
            clock.Restart();
            var root = new Count();
            await provider.EnumerateAsync(location, root, ct);
            var opened = clock.Elapsed;
            var folder = new Count();
            await provider.EnumerateAsync(location.WithPath("folder-0007"), folder, ct);
            long held = Settled() - before;
            long estimated = provider switch { ZipProvider z => z.RetainedIndexBytes, ArchiveProvider t => t.RetainedIndexBytes, _ => 0 };
            log?.WriteLine($"{what}: opened in {opened.TotalSeconds:F2} s ({root.Entries:N0} folders at the top, {folder.Entries:N0} members in one); " +
                           $"its index holds {held >> 20:N0} MiB, {held / members:N0} bytes a member; estimated {estimated >> 20:N0} MiB");
            Assert.Equal(1000, root.Entries);
            Assert.Equal((members + 999) / 1000, folder.Entries);
            GC.KeepAlive(provider);
            (provider as IDisposable)?.Dispose();
        }
    }
}
