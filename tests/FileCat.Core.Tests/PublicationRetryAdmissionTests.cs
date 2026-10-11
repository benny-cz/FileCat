using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Owned real staging/publication with recorded backing-alias transitions; no device is opened.</summary>
public sealed class PublicationRetryAdmissionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("retry-identity")]
    [InlineData("retry-final-path")]
    [InlineData("auto-identity")]
    [InlineData("auto-final-path")]
    [InlineData("skip-error")]
    [InlineData("healthy-retry")]
    [InlineData("healthy-auto")]
    public async Task Publication_retries_recheck_backing_sources_and_retire_failed_byte_progress(string route)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination"), source = Path.Combine(root.Path, "owned-container.bin");
        string target = Path.Combine(destination, "copy.bin");
        byte[] original = "whole owned backing bytes"u8.ToArray(), incoming = "whole owned output bytes"u8.ToArray(), previous = "owned previous destination"u8.ToArray();
        File.WriteAllBytes(source, original); File.WriteAllBytes(target, previous);
        var files = new RetryFiles(source, target, route); var provider = new Provider(incoming);
        var providers = new ProviderRegistry(); providers.Register(provider); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending =>
        {
            decisions.Add(pending.Request);
            pending.Resolve(new Decision(route != "skip-error" && decisions.Count == 1 ? DecisionAction.Retry : DecisionAction.Skip));
        };
        try
        {
            var parent = new Location(provider.Scheme, "", Location.FileSystem(source));
            var job = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [new(parent, "copy.bin", EntryKind.File, incoming.Length)], Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Replace } });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned retry job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                route, job.State, job.BytesDone, files.Attempts, files.Published, files.AliasActive, provider.Opens, provider.Closes,
                Decisions = decisions.Select(d => new { d.Title, d.Message }),
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                SourceBeforeBytes = Convert.ToBase64String(original), SourceAfterBytes = Convert.ToBase64String(File.ReadAllBytes(source)),
                TargetBeforeBytes = Convert.ToBase64String(previous), TargetAfterBytes = Convert.ToBase64String(File.ReadAllBytes(target)),
                IncomingBytes = Convert.ToBase64String(incoming),
                DestinationFiles = Directory.GetFiles(destination).Select(p => new { Name = Path.GetFileName(p), Bytes = Convert.ToBase64String(File.ReadAllBytes(p)) }),
            }));
            Assert.Equal(original, File.ReadAllBytes(source)); Assert.Equal(1, provider.Opens); Assert.Equal(1, provider.Closes);
            Assert.DoesNotContain(Directory.GetFiles(destination), p => Path.GetFileName(p).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
            if (route.StartsWith("healthy-", StringComparison.Ordinal))
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(incoming.Length, job.BytesDone);
                Assert.Equal(2, files.Attempts); Assert.Equal(1, files.Published); Assert.Equal(incoming, File.ReadAllBytes(target));
                Assert.Equal(route == "healthy-auto" ? 0 : 1, decisions.Count);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone); Assert.Equal(0, files.Published);
                Assert.Equal(1, files.Attempts); Assert.Equal(previous, File.ReadAllBytes(target));
                Assert.Equal(route == "skip-error" ? 1 : 2, decisions.Count);
                Assert.Contains(job.Issues, i => i.Message.Contains(route == "skip-error" ? "owned publication failed" : "source image or container file", StringComparison.Ordinal));
            }
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class RetryFiles(string source, string target, string route) : PortableFileOperations
    {
        public int Attempts, Published;
        public bool AliasActive;
        public override string? GetFileIdentity(string path) => path == source || path == target && AliasActive && route.EndsWith("identity", StringComparison.Ordinal) ? "owned-backing" : "owned-distinct:" + path;
        public override string? GetFinalPath(string path) => path == target && AliasActive && route.EndsWith("final-path", StringComparison.Ordinal) ? source : null;
        public override void Move(string staged, string destination, bool replaceExisting, bool writeThrough = false)
        {
            Attempts++;
            if (Attempts == 1)
            {
                AliasActive = !route.StartsWith("healthy-", StringComparison.Ordinal) && route != "skip-error";
                throw new OwnedPublicationException(route.StartsWith("auto-", StringComparison.Ordinal) || route == "healthy-auto");
            }
            // Recorded adapter transition, with a real rename to the owned backing file. This is not a kernel alias claim.
            base.Move(staged, AliasActive ? source : destination, replaceExisting, writeThrough); Published++;
        }
        private sealed class OwnedPublicationException : IOException
        {
            public OwnedPublicationException(bool sharing) : base("owned publication failed") { HResult = sharing ? unchecked((int)0x80070020) : unchecked((int)0x8007001F); }
        }
    }
    private sealed class Provider(byte[] bytes) : ResourceProvider
    {
        public int Opens, Closes;
        public override string Scheme => "owned-publication-retry";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item) { Opens++; return new Source(this, bytes); }
        private sealed class Source(Provider owner, byte[] bytes) : IContentSource
        {
            public string DisplayName => "owned retry content"; public long Length => bytes.Length; public bool CanSeek => true; public string? LocalPath => null;
            public ContentRevision? GetRevision() => null;
            public int Read(long offset, Span<byte> buffer) { int n = (int)Math.Min(buffer.Length, Math.Max(0, bytes.Length - offset)); bytes.AsSpan((int)offset, n).CopyTo(buffer); return n; }
            public void Dispose() => owner.Closes++;
        }
    }
}
