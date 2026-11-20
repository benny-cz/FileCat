using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class BackingArchiveTargetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("hard", "portable")]
    [InlineData("symbolic", "portable")]
    [InlineData("hard", "unix")]
    [InlineData("symbolic", "unix")]
    public async Task Extraction_preserves_a_link_to_its_backing_archive(string link, string operations)
    {
        if (operations == "unix" && OperatingSystem.IsWindows()) Assert.Skip("Unix file operations are unavailable on Windows.");
        using var root = new TempDir();
        string destination = root.Dir("destination");
        string archive = Path.Combine(root.Path, "source.zip");
        string alias = Path.Combine(destination, "alias.zip");
        using (var zipFile = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var entry = zipFile.CreateEntry("alias.zip").Open()) entry.Write("entry bytes"u8);
        byte[] before = File.ReadAllBytes(archive);
        IFileSystemOperations fs = OperatingSystem.IsWindows() ? new IdentityAwareTestFiles()
            : operations == "unix" ? new UnixFileOperations() : new PortableFileOperations();
        if (link == "symbolic" && fs.CanCreateSymbolicLinks != true)
            Assert.Skip("Symbolic-link creation is unavailable on this guest.");
        fs.CreateLink(alias, archive, link == "hard" ? LinkKind.Hard : LinkKind.Symbolic, false);
        string backingIdentity = Assert.IsType<string>(fs.ObserveResolvedFile(archive).Identity);
        // The Windows Core fixture reports the link entry's identity; the production Windows adapter resolves it.
        if (!OperatingSystem.IsWindows() || link == "hard")
            Assert.Equal(backingIdentity, fs.ObserveResolvedFile(alias).Identity);

        var zip = new ZipProvider(root.Dir("spool"));
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(zip);
        var location = ZipProvider.ForFile(archive); var sink = new Sink();
        await zip.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        var item = zip.GetItemRef(location, Assert.Single(sink.Entries));
        var jobs = new JobManager(fs, providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending => { decisions.Add(pending.Request); pending.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Extract, Sources = [item], Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            if (OperatingSystem.IsWindows() && link == "hard")
            {
                Assert.Equal(JobState.Failed, job.State);
                Assert.Empty(decisions);
                Assert.Contains(job.Issues, i => i.Message.Contains("source image or container file", StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal(JobState.CompletedWithIssues, job.State);
                Assert.Single(decisions);
            }
            Assert.Equal(0, job.BytesDone);
            Assert.Equal(before, File.ReadAllBytes(archive));
            Assert.Equal(before, File.ReadAllBytes(alias));
            Assert.Equal([alias], Directory.GetFiles(destination));
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
            zip.Release(archive);
        }
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("keep-both")]
    [InlineData("sibling")]
    public async Task Extraction_preserves_its_backing_archive(string route)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination"); string archive = Path.Combine(destination, "owned.zip");
        byte[] content = "whole owned archive member bytes"u8.ToArray();
        string name = route == "sibling" ? "output.bin" : "owned.zip";
        using (var zipFile = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var entry = zipFile.CreateEntry(name).Open()) entry.Write(content);
        byte[] before = File.ReadAllBytes(archive);
        var zip = new ZipProvider(root.Dir("spool"));
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(zip);
        var location = ZipProvider.ForFile(archive); var sink = new Sink();
        await zip.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        var item = zip.GetItemRef(location, Assert.Single(sink.Entries));
        var jobs = new JobManager(new IdentityAwareTestFiles(), providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending => { decisions.Add(pending.Request); pending.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Extract, Sources = [item], Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = route == "keep-both" ? ConflictPolicy.KeepBothRenameIncoming : ConflictPolicy.Replace },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                route, job.State, job.BytesDone, Decisions = decisions.Select(d => new { d.Title, d.Message }),
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                SourceBeforeBytes = Convert.ToBase64String(before), SourceAfterBytes = Convert.ToBase64String(File.ReadAllBytes(archive)),
                SourceBeforeSHA256 = Convert.ToHexString(SHA256.HashData(before)), SourceAfterSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))),
                DestinationFiles = Directory.GetFiles(destination).Select(p => new { Name = Path.GetFileName(p), Bytes = Convert.ToBase64String(File.ReadAllBytes(p)) }),
            }));
            Assert.Equal(before, File.ReadAllBytes(archive)); Assert.Empty(decisions);
            if (route == "replace")
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone);
                Assert.Equal([archive], Directory.GetFiles(destination));
                Assert.Contains(job.Issues, i => i.Message.Contains("source image or container file", StringComparison.Ordinal));
            }
            else
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(content.Length, job.BytesDone);
                string result = Assert.Single(Directory.GetFiles(destination), p => p != archive);
                Assert.Equal(content, File.ReadAllBytes(result));
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
            zip.Release(archive);
        }
    }
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }
}
