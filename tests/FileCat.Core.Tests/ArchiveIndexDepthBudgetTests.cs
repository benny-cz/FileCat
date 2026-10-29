using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveIndexDepthBudgetTests
{
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries.Add(entry); }
        public void ReportIssue(string message) { }
    }

    [Fact]
    public void Deep_member_is_refused_before_ancestor_expansion()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "deep.tar");
        using (var file = File.Create(path))
        using (var writer = new TarWriter(file, TarEntryFormat.Pax))
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile,
                string.Join('/', Enumerable.Repeat("a", 257)) + "/file"));

        using var reader = ArchiveFormats.Open(path, ArchiveKind.Tar, "deep.tar");
        var error = Assert.ThrowsAny<IOException>(() => ArchiveIndex.Build(reader, new FileInfo(path).Length, TestContext.Current.CancellationToken));
        Assert.Contains("path components", error.Message);
    }

    [Fact]
    public async Task Public_ZIP_listing_refuses_one_deep_member_and_accepts_the_depth_boundary()
    {
        using var dir = new TempDir();
        static void MakeZip(string path, int segments)
        {
            using var file = File.Create(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            using var member = archive.CreateEntry(string.Join('/', Enumerable.Repeat("a", segments - 1)) + "/file").Open();
            member.WriteByte(42);
        }
        string boundary = Path.Combine(dir.Path, "boundary.zip");
        string deep = Path.Combine(dir.Path, "deep.zip");
        MakeZip(boundary, ArchivePaths.MaxMemberSegments);
        MakeZip(deep, 8192);
        var provider = new ZipProvider(Path.Combine(dir.Path, "spools"));
        var listed = new Sink();
        await provider.EnumerateAsync(ZipProvider.ForFile(boundary), listed, TestContext.Current.CancellationToken);
        Assert.Single(listed.Entries);
        Assert.Equal("a", listed.Entries[0].Name);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = await Assert.ThrowsAnyAsync<IOException>(async () =>
            await provider.EnumerateAsync(ZipProvider.ForFile(deep), new Sink(), TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("path components", error.Message);
        Assert.True(allocated < 10L * 1024 * 1024, $"Deep ZIP listing allocated {allocated:N0} bytes.");
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "spools")) &&
                     Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "spools")).Any());
    }

    [Fact]
    public async Task Public_ZIP_listing_refuses_aggregate_generated_ancestor_budget()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "many-long-ancestors.zip");
        using (var file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            string segment = new('q', 240);
            for (int i = 0; i < 5; i++)
            {
                string first = ((char)('a' + i)) + segment[1..];
                string member = string.Join('/', new[] { first }.Concat(Enumerable.Repeat(segment, 254))) + "/file";
                Assert.True(member.Length < ushort.MaxValue);
                using var output = archive.CreateEntry(member).Open();
                output.WriteByte(42);
            }
        }
        var provider = new ZipProvider(Path.Combine(dir.Path, "spools"));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = await Assert.ThrowsAnyAsync<IOException>(async () =>
            await provider.EnumerateAsync(ZipProvider.ForFile(path), new Sink(), TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("generated directory paths", error.Message);
        Assert.True(allocated < 128L * 1024 * 1024, $"Aggregate ZIP index refusal allocated {allocated:N0} bytes.");
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "spools")) &&
                     Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "spools")).Any());
    }
}
