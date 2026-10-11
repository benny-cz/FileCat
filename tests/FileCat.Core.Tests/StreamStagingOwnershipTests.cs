using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Actual owned destination collisions and failures before/after CreateNew; no physical source.</summary>
public sealed class StreamStagingOwnershipTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string moment in new[] { "create", "revision", "length", "cancel", "unexpected" })
        foreach (bool collision in new[] { false, true }) yield return [moment, collision];
        foreach (string moment in new[] { "read", "close", "publish", "verify" }) yield return [moment, false];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Cleanup_only_deletes_a_staging_file_this_copy_created(string moment, bool collision)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination"), target = Path.Combine(destination, "copy.bin"), staged = "";
        byte[] source = "complete owned staging source bytes"u8.ToArray(), previous = "complete unrelated preexisting staging bytes"u8.ToArray();
        var provider = new Provider(source, moment);
        var files = new Files(moment);
        var providers = new ProviderRegistry(); providers.Register(provider); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        jobs.JobAdded += job =>
        {
            staged = Path.Combine(destination, $"{JournalRecovery.StagedPrefix}{job.ShortId}-1.tmp");
            if (collision) File.WriteAllBytes(staged, previous);
        };
        jobs.DecisionRequested += pending => pending.Resolve(new Decision(DecisionAction.Skip));
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [new(new Location(provider.Scheme, ""), "copy.bin", EntryKind.File, source.Length)],
                Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, Verify = moment == "verify" ? VerifyMode.ReadBack : VerifyMode.Native },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned staging job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                moment, collision, job.State, job.BytesDone, provider.Opens, provider.Closes, provider.Reads, files.Published,
                StagedPath = staged, StagedExists = File.Exists(staged), StagedBytes = File.Exists(staged) ? Convert.ToBase64String(File.ReadAllBytes(staged)) : null,
                KnownPreviousBytes = Convert.ToBase64String(previous), KnownSourceBytes = Convert.ToBase64String(source),
                TargetBytes = File.Exists(target) ? Convert.ToBase64String(File.ReadAllBytes(target)) : null,
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                DestinationNames = Directory.GetFiles(destination).Select(p => Path.GetFileName(p)),
            }));
            Assert.Equal(provider.Opens, provider.Closes);
            if (collision) Assert.Equal(previous, File.ReadAllBytes(staged)); else Assert.False(File.Exists(staged));
            bool completed = moment == "create" && !collision;
            Assert.Equal(completed ? JobState.Completed : moment == "cancel" ? JobState.Canceled : JobState.Failed, job.State);
            Assert.Equal(completed ? source.Length : 0, job.BytesDone); Assert.Equal(completed ? 1 : 0, files.Published);
            if (completed) Assert.Equal(source, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
            Assert.Equal(collision ? [Path.GetFileName(staged)] : completed ? ["copy.bin"] : Array.Empty<string>(), Directory.GetFiles(destination).Select(Path.GetFileName));
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class Files(string moment) : PortableFileOperations
    {
        public int Published;
        public override void Move(string source, string target, bool replaceExisting, bool writeThrough = false)
        {
            if (moment == "publish") throw new IOException("owned publication failure");
            base.Move(source, target, replaceExisting, writeThrough); Published++;
        }
    }

    private sealed class Provider(byte[] bytes, string moment) : ResourceProvider
    {
        public int Opens, Closes, Reads;
        public override string Scheme => "owned-stream-staging";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item) => new Source(this, ++Opens, bytes, moment);
        private sealed class Source(Provider owner, int ordinal, byte[] bytes, string moment) : IContentSource
        {
            public string DisplayName => "owned staging source";
            public long Length => moment == "length" ? throw new IOException("owned pre-creation length failure") : bytes.Length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision() => moment switch
            {
                "revision" => throw new IOException("owned pre-creation revision failure"),
                "cancel" => throw new OperationCanceledException("owned pre-creation cancellation"),
                "unexpected" => throw new InvalidOperationException("owned pre-creation unexpected failure"),
                _ => null,
            };
            public int Read(long offset, Span<byte> buffer)
            {
                owner.Reads++;
                if (moment == "read") throw new IOException("owned post-creation read failure");
                int n = (int)Math.Min(buffer.Length, Math.Max(0, bytes.Length - offset));
                bytes.AsSpan((int)offset, n).CopyTo(buffer);
                if (moment == "verify" && ordinal == 2 && n > 0) buffer[0] ^= 0xff;
                return n;
            }
            public void Dispose()
            {
                owner.Closes++;
                if (moment == "close") throw new IOException("owned post-creation close failure");
            }
        }
    }
}
