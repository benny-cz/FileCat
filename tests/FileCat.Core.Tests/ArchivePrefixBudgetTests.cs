using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchivePrefixBudgetTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _root = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generated_directory_prefixes_are_charged_and_evicted(bool zip)
    {
        const int depth = 1024;
        string folder = string.Join('/', Enumerable.Repeat("d", depth));
        long minimumPrefixBytes = 2L * depth * depth; // UTF-16 length of d, d/d, d/d/d, ...
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        ResourceProvider provider = zip ? new ZipProvider(_root.Path) : new ArchiveProvider(_root.Path, registry);
        try
        {
            if (provider is ZipProvider zp) zp.RetainedIndexLimitBytes = 64 << 10;
            else ((ArchiveProvider)provider).RetainedIndexLimitBytes = 64 << 10;
            long[] estimates = new long[3];
            for (int i = 0; i < 3; i++)
            {
                string path = Path.Combine(_root.Path, $"archive-{i}." + (zip ? "zip" : "tar"));
                string member = folder + "/payload.bin";
                byte[] bytes = Enumerable.Range(0, 256).Select(n => (byte)(n * 17 + 9)).ToArray();
                if (zip)
                {
                    using var output = File.Create(path);
                    using var archive = new ZipArchive(output, ZipArchiveMode.Create);
                    using var body = archive.CreateEntry(member, CompressionLevel.NoCompression).Open();
                    body.Write(bytes);
                }
                else
                {
                    using var output = File.Create(path);
                    using var archive = new TarWriter(output, TarEntryFormat.Pax);
                    using var body = new MemoryStream(bytes);
                    archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, member) { DataStream = body });
                }
                var location = zip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
                var top = new Sink();
                await provider.EnumerateAsync(location, top, TestContext.Current.CancellationToken);
                Assert.Single(top.Names);
                Assert.Equal("d", top.Names[0]);
                estimates[i] = zip ? ((ZipProvider)provider).RetainedIndexBytes : ((ArchiveProvider)provider).RetainedIndexBytes;
            }
            long retained = estimates[0];
            int indexes = zip ? ((ZipProvider)provider).RetainedIndexes : ((ArchiveProvider)provider).RetainedIndexes;
            output.WriteLine("PREFIX_BUDGET " + JsonSerializer.Serialize(new
            {
                zip, depth, minimumPrefixBytes, firstEstimate = retained,
                afterSecondEstimate = estimates[1], afterThirdEstimate = estimates[2],
                retainedIndexesAfterThird = indexes, archiveCount = 3, limitBytes = 64 << 10,
            }));
            // The first archive alone owns every generated prefix string. The third opening must evict one old index.
            Assert.True(retained >= minimumPrefixBytes, $"retained={retained}; minimum prefix bytes={minimumPrefixBytes}");
            Assert.Equal(2, indexes);
        }
        finally { (provider as IDisposable)?.Dispose(); }
    }

    public void Dispose() => _root.Dispose();

    private sealed class Sink : IEnumerationSink
    {
        public List<string> Names { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Names.Add(entry.Name); }
        public void ReportIssue(string message) => throw new Xunit.Sdk.XunitException(message);
    }
}
