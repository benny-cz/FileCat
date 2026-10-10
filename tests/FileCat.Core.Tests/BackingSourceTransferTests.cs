using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Real publication and cleanup, with explicitly recorded identity/path answers; no device is opened.</summary>
public sealed class BackingSourceTransferTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("direct")]
    [InlineData("final-path")]
    [InlineData("identity")]
    [InlineData("open-renamed")]
    [InlineData("metadata")]
    [InlineData("read")]
    [InlineData("close")]
    [InlineData("cross-source")]
    [InlineData("nested-container")]
    [InlineData("query-error")]
    [InlineData("keep-both")]
    [InlineData("skip")]
    [InlineData("healthy-replace")]
    public async Task A_stream_transfer_preserves_all_selected_backing_containers(string route)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination");
        string target = Path.Combine(destination, "copy.bin");
        string source = route is "direct" or "nested-container" or "keep-both" or "skip" ? target : Path.Combine(root.Path, "owned-image.bin");
        string other = Path.Combine(destination, "other.bin");
        byte[] original = "owned source container bytes, never an output"u8.ToArray();
        byte[] previous = "owned original destination bytes"u8.ToArray();
        byte[] incoming = "whole owned transfer bytes"u8.ToArray();
        File.WriteAllBytes(source, original);
        if (source != target) File.WriteAllBytes(target, previous);
        File.WriteAllBytes(other, previous);
        var files = new RecordingFiles(source, target, other, route);
        var provider = new RecordingProvider(files, incoming, route);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider()); providers.Register(provider);
        var container = Location.FileSystem(source);
        var parent = new Location(provider.Scheme, "", route == "nested-container" ? new Location("owned-inner", "member.arc", container) : container);
        ItemRef[] items = route == "cross-source"
            ? [new(new Location(provider.Scheme, "", Location.FileSystem(other)), "copy.bin", EntryKind.File),
               new(parent, "other.bin", EntryKind.File)]
            : [new(parent, "copy.bin", EntryKind.File, incoming.Length)];
        // The cross-source case makes each output replace the backing file of the other selected item.
        if (route == "cross-source")
        {
            source = target; File.WriteAllBytes(source, original); files.Source = source;
            items = [new(new Location(provider.Scheme, "", Location.FileSystem(other)), "copy.bin", EntryKind.File),
                     new(new Location(provider.Scheme, "", Location.FileSystem(source)), "other.bin", EntryKind.File)];
        }
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending => { decisions.Add(pending.Request); pending.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = items, Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = route switch
                {
                    "keep-both" => ConflictPolicy.KeepBothRenameIncoming,
                    "skip" => ConflictPolicy.Skip,
                    _ => ConflictPolicy.Replace,
                } },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                route, job.State, job.BytesDone, provider.Opens, provider.Closes, provider.Reads,
                files.AliasActive, files.SourceMoved, files.Published, files.Queries,
                Decisions = decisions.Select(d => new { d.Title, d.Message }),
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                SourceBeforeBytes = Convert.ToBase64String(original), SourceAfterBytes = Convert.ToBase64String(File.ReadAllBytes(source)),
                SourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
                DestinationFiles = Directory.GetFiles(destination).Select(p => new { Name = Path.GetFileName(p), Bytes = Convert.ToBase64String(File.ReadAllBytes(p)) }),
            }));
            Assert.Empty(decisions);
            Assert.Equal(original, File.ReadAllBytes(source)); Assert.Equal(previous, File.ReadAllBytes(other));
            Assert.DoesNotContain(Directory.GetFiles(destination), p => Path.GetFileName(p).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
            if (route is "keep-both" or "healthy-replace")
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(incoming.Length, job.BytesDone);
                Assert.Equal(1, files.Published); Assert.Equal(1, provider.Opens); Assert.Equal(1, provider.Closes);
                string result = route == "healthy-replace" ? target : Assert.Single(Directory.GetFiles(destination), p => p != source && p != other);
                Assert.Equal(incoming, File.ReadAllBytes(result));
            }
            else if (route == "skip")
            {
                Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal(0, job.BytesDone);
                Assert.Equal(0, provider.Opens); Assert.Equal(0, files.Published);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone); Assert.Equal(0, files.Published);
                bool late = route is "open-renamed" or "metadata" or "read" or "close";
                Assert.Equal(late ? 1 : 0, provider.Opens); Assert.Equal(provider.Opens, provider.Closes);
                if (route is "read" or "close") Assert.True(provider.Reads > 0);
                else Assert.Equal(0, provider.Reads);
                Assert.Contains(job.Issues, i => i.Message.Contains(route == "query-error" ? "owned identity query failed" : "source image or container file", StringComparison.Ordinal));
                if (source != target) Assert.Equal(previous, File.ReadAllBytes(target));
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
        }
    }

    private sealed class RecordingFiles(string source, string target, string other, string route) : PortableFileOperations
    {
        public string Source = source;
        public bool AliasActive = route is "identity" or "final-path";
        public bool SourceMoved;
        public int Published;
        public List<string> Queries { get; } = [];
        public override string? GetFileIdentity(string path)
        {
            Queries.Add("identity:" + path);
            if (path == target && route == "query-error") throw new IOException("owned identity query failed");
            if (path == Source) return SourceMoved ? "owned-replacement-source" : "owned-source";
            if (path == target) return AliasActive ? "owned-source" : "owned-target";
            if (path == other) return "owned-other";
            return null;
        }
        public override string? GetFinalPath(string path)
        {
            Queries.Add("final:" + path);
            return route == "final-path" && path == target ? Source : null;
        }
        public override void Move(string sourcePath, string destination, bool replaceExisting, bool writeThrough = false)
        {
            Published++; base.Move(sourcePath, destination, replaceExisting, writeThrough);
        }
    }

    private sealed class RecordingProvider(RecordingFiles files, byte[] bytes, string route) : ResourceProvider
    {
        public int Opens, Closes, Reads;
        public override string Scheme => "owned-backing-source";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item)
        {
            Opens++;
            if (route == "open-renamed") { files.AliasActive = true; files.SourceMoved = true; }
            return new Source(this, files, bytes, route);
        }
        private sealed class Source(RecordingProvider owner, RecordingFiles files, byte[] bytes, string route) : IContentSource
        {
            public string DisplayName => "owned test content";
            public long Length => bytes.Length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision()
            {
                if (route == "metadata") files.AliasActive = true;
                return null;
            }
            public int Read(long offset, Span<byte> buffer)
            {
                owner.Reads++;
                if (route == "read") files.AliasActive = true;
                int count = (int)Math.Min(buffer.Length, Math.Max(0, bytes.Length - offset));
                bytes.AsSpan((int)offset, count).CopyTo(buffer); return count;
            }
            public void Dispose()
            {
                owner.Closes++;
                if (route == "close") files.AliasActive = true;
            }
        }
    }
}
