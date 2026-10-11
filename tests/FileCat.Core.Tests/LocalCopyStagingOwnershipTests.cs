using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Actual local CreateNew collisions and engine-owned cancellation cleanup; no devices.</summary>
public sealed class LocalCopyStagingOwnershipTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string moment in new[] { "create", "io", "denied", "cancel", "unexpected" })
        foreach (bool collision in new[] { false, true }) yield return [moment, collision];
        foreach (string moment in new[] { "owned-io", "owned-cancel" }) yield return [moment, false];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Failed_local_copy_leaves_unowned_staging_bytes_and_cleans_its_own_partial(string moment, bool collision)
    {
        using var root = new TempDir();
        using var fixtureReady = new ManualResetEventSlim(false);
        string source = root.File("source/copy.bin"), destination = root.Dir("destination"), target = Path.Combine(destination, "copy.bin"), staged = "";
        byte[] incoming = Enumerable.Range(0, 4097).Select(i => (byte)(i * 29 + 11)).ToArray();
        byte[] previous = "complete unrelated local staging bytes"u8.ToArray(), original = "complete original local target bytes"u8.ToArray();
        File.WriteAllBytes(source, incoming); File.WriteAllBytes(target, original);
        var files = new Files(moment, fixtureReady); var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        jobs.JobAdded += job =>
        {
            staged = Path.Combine(destination, $"{JournalRecovery.StagedPrefix}{job.ShortId}-1.tmp");
            if (collision) File.WriteAllBytes(staged, previous);
            fixtureReady.Set();
        };
        jobs.DecisionRequested += pending => pending.Resolve(new Decision(DecisionAction.Skip));
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned local staging job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                moment, collision, job.State, job.BytesDone, files.Copies, files.ProgressCalls,
                SourceBytesAfter = Convert.ToBase64String(File.ReadAllBytes(source)),
                TargetBytesAfter = Convert.ToBase64String(File.ReadAllBytes(target)),
                KnownIncoming = Convert.ToBase64String(incoming), KnownOriginal = Convert.ToBase64String(original), KnownPrevious = Convert.ToBase64String(previous),
                StagedPath = staged, StagedExists = File.Exists(staged), StagedBytes = File.Exists(staged) ? Convert.ToBase64String(File.ReadAllBytes(staged)) : null,
                DestinationNames = Directory.GetFiles(destination).Select(Path.GetFileName),
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
            }));
            Assert.Equal(incoming, File.ReadAllBytes(source));
            Assert.Equal(moment == "create" && !collision ? incoming : original, File.ReadAllBytes(target));
            if (collision) Assert.Equal(previous, File.ReadAllBytes(staged)); else Assert.False(File.Exists(staged));
            bool completed = moment == "create" && !collision;
            Assert.Equal(completed ? JobState.Completed : moment is "cancel" or "owned-cancel" ? JobState.Canceled : JobState.Failed, job.State);
            Assert.Equal(completed ? incoming.Length : 0, job.BytesDone);
            Assert.Equal(collision ? new[] { Path.GetFileName(staged), "copy.bin" }.Order() : ["copy.bin"], Directory.GetFiles(destination).Select(Path.GetFileName).Order());
            Assert.Equal(1, files.Copies); Assert.Equal(completed || moment.StartsWith("owned-") ? 1 : 0, files.ProgressCalls);
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class Files(string moment, ManualResetEventSlim fixtureReady) : PortableFileOperations
    {
        public int Copies, ProgressCalls;
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            if (!fixtureReady.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned local staging fixture was not installed.");
            Copies++;
            switch (moment)
            {
                case "io": throw new IOException("owned pre-creation local I/O failure");
                case "denied": throw new UnauthorizedAccessException("owned pre-creation local refusal");
                case "cancel": throw new OperationCanceledException("owned pre-creation local cancellation");
                case "unexpected": throw new InvalidOperationException("owned pre-creation local unexpected failure");
            }
            base.CopyFile(source, destination, options, (done, total) =>
            {
                ProgressCalls++; var action = progress?.Invoke(done, total) ?? CopyProgressAction.Continue;
                if (moment == "owned-io") throw new IOException("owned post-write local I/O failure");
                return moment == "owned-cancel" ? CopyProgressAction.Cancel : action;
            }, ct);
        }
    }
}
