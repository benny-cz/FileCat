using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TransferEnumerationWarningTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-warning-controls", Guid.NewGuid().ToString("N"))).FullName;
    private static readonly byte[] Bytes = "owned complete member bytes"u8.ToArray();
    public void Dispose() => Directory.Delete(_root, true);

    public static TheoryData<JobKind, string, bool> WarningCases
    {
        get
        {
            var cases = new TheoryData<JobKind, string, bool>();
            foreach (var kind in new[] { JobKind.Copy, JobKind.Extract })
                foreach (string warning in new[] { "A member could not be listed.", "Duplicate names are listed separately.", "The archive is damaged after the listed members." })
                    foreach (bool nested in new[] { false, true }) cases.Add(kind, warning, nested);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(WarningCases))]
    public async Task Warning_membership_is_reported_and_never_marks_the_root_complete(JobKind kind, string warning, bool nested)
    {
        var provider = new OwnedProvider(warning, nested);
        var job = await Run(provider, new ItemRef(new Location(provider.Scheme, ""), "folder", EntryKind.Directory), kind);
        Emit("warning", kind, warning, nested, job);
        Assert.Equal(Bytes, File.ReadAllBytes(Path.Join(_root, "out", "folder", nested ? "sub/kept.bin" : "kept.bin")));
        Assert.Contains(job.Issues, i => i.Message.Contains(warning, StringComparison.Ordinal));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal(0, job.CompletedRootCount); Assert.Equal(1, job.FailedRootCount);
    }

    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Copy, true)]
    [InlineData(JobKind.Extract, false)]
    [InlineData(JobKind.Extract, true)]
    public async Task Complete_membership_still_marks_the_root_complete(JobKind kind, bool nested)
    {
        var provider = new OwnedProvider(null, nested);
        var job = await Run(provider, new ItemRef(new Location(provider.Scheme, ""), "folder", EntryKind.Directory), kind);
        Emit("positive", kind, null, nested, job);
        Assert.Equal(Bytes, File.ReadAllBytes(Path.Join(_root, "out", "folder", nested ? "sub/kept.bin" : "kept.bin")));
        Assert.Equal(JobState.Completed, job.State); Assert.Empty(job.Issues);
        Assert.Equal(1, job.CompletedRootCount); Assert.Equal(0, job.FailedRootCount);
    }

    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Copy, true)]
    [InlineData(JobKind.Extract, false)]
    [InlineData(JobKind.Extract, true)]
    public async Task Actual_damaged_TAR_folder_retains_its_warning_and_exact_readable_bytes(JobKind kind, bool gzip)
    {
        using var buffer = new MemoryStream();
        using (var writer = new TarWriter(buffer, TarEntryFormat.Ustar, true))
            foreach (string name in new[] { "folder/kept.bin", "folder/lost.bin" })
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Bytes) });
        byte[] tar = buffer.ToArray(); tar[1024 + 124] = (byte)'x';
        using (var oracle = new TarReader(new MemoryStream(tar)))
        { Assert.Equal("folder/kept.bin", oracle.GetNextEntry()!.Name); Assert.Throws<InvalidDataException>(() => oracle.GetNextEntry()); }
        string path = Path.Join(_root, gzip ? "owned.tar.gz" : "owned.tar");
        if (gzip) { using var stream = new GZipStream(File.Create(path), CompressionLevel.Fastest); stream.Write(tar); }
        else File.WriteAllBytes(path, tar);
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        var provider = new ArchiveProvider(Path.Join(_root, "scratch"), registry);
        string before = Hash(path);
        Job job;
        try { job = await Run(provider, new ItemRef(ArchiveProvider.ForFile(path), "folder", EntryKind.Directory), kind, registry); }
        finally { provider.Release(path); }
        Emit("actual-tar", kind, "damaged after", gzip, job);
        Assert.Equal(Bytes, File.ReadAllBytes(Path.Join(_root, "out", "folder", "kept.bin")));
        Assert.False(File.Exists(Path.Join(_root, "out", "folder", "lost.bin"))); Assert.Equal(before, Hash(path));
        Assert.Contains(job.Issues, i => i.Message.Contains("damaged after", StringComparison.Ordinal));
        Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal(0, job.CompletedRootCount); Assert.Equal(1, job.FailedRootCount);
    }

    private async Task<Job> Run(ResourceProvider provider, ItemRef item, JobKind kind, ProviderRegistry? registry = null)
    {
        registry ??= new ProviderRegistry(); registry.Register(provider);
        var manager = new JobManager(new PortableFileOperations(), registry, Path.Join(_root, "journal"));
        var job = manager.Submit(new JobRequest { Kind = kind, Sources = [item], Destination = Location.FileSystem(Path.Join(_root, "out")) });
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); limit.CancelAfter(TimeSpan.FromSeconds(15));
        while (!job.State.IsFinished()) await Task.Delay(10, limit.Token);
        return job;
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private void Emit(string control, JobKind kind, string? warning, bool nested, Job job)
        => output.WriteLine("TRANSFER_ENUMERATION_WARNING " + JsonSerializer.Serialize(new { control, kind = kind.ToString(), warning, nested,
            State = job.State.ToString(), job.CompletedRootCount, job.FailedRootCount,
            Issues = job.Issues.Select(i => new { i.Message, Severity = i.Severity.ToString(), Outcome = i.Outcome.ToString() }),
            Files = Directory.Exists(Path.Join(_root, "out")) ? Directory.GetFiles(Path.Join(_root, "out"), "*", SearchOption.AllDirectories).Select(p => new { Path = Path.GetRelativePath(_root, p), SHA256 = Hash(p) }) : [] }));

    private sealed class OwnedProvider(string? warning, bool nested) : ResourceProvider
    {
        public override string Scheme => "ownedtransferwarning";
        public override string GetDisplayPath(Location l) => l.Path;
        public override Location? GetParent(Location l) => null;
        public override Location GetChildLocation(Location l, in EntryData e) => l.WithPath(l.Path + "/" + e.Name);
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            bool leaf = !nested || l.Path.EndsWith("/sub", StringComparison.Ordinal);
            if (leaf && warning is not null) sink.ReportIssue(warning);
            sink.AddBatch([new EntryData(leaf ? "kept.bin" : "sub", leaf ? EntryKind.File : EntryKind.Directory, leaf ? Bytes.Length : -1)]);
            return Task.CompletedTask;
        }
        public override IContentSource OpenContent(ItemRef item) => new MemoryContentSource(item.Name, Bytes);
    }
}
