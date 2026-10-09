using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class SyncTargetPublicationTests(ITestOutputHelper output)
{
    public static TheoryData<SyncMode, string, string, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<SyncMode, string, string, bool, bool>();
            foreach (var mode in new[] { SyncMode.Update, SyncMode.Mirror })
                foreach (string change in new[] { "unchanged", "length", "time", "gone" })
                    foreach (string phase in new[] { "before-copy", "after-initial-check", "during-copy", "retry-publication" })
                        foreach (bool nested in new[] { false, true })
                            foreach (bool left in new[] { false, true }) cases.Add(mode, change, phase, nested, left);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Synchronization_rechecks_compared_targets_at_publication(SyncMode mode, string change, string phase, bool nested, bool left)
    {
        using var rig = new Rig(mode, change, phase, nested, left);
        var comparison = TreeCompare.Compare(rig.Providers, Location.FileSystem(rig.Left), Location.FileSystem(rig.Right),
            CompareCriteria.Size | CompareCriteria.Time, TimeSpan.FromSeconds(2), OperatingSystem.IsWindows(), TestContext.Current.CancellationToken);
        var plan = SyncPlanner.Propose(comparison, left, mode, OperatingSystem.IsWindows());
        var item = Assert.Single(plan.Where(i => i.Entry.RelativePath == (nested ? "nested/notes.txt" : "notes.txt")));
        Assert.Equal(mode == SyncMode.Update ? SyncAction.ReplaceOlder : SyncAction.Replace, item.Action);
        var request = Assert.Single(SyncPlanner.BuildRequests([item], left, Location.FileSystem(rig.TargetRoot), rig.Providers, deletePermanently: true));
        var expected = Assert.Single(request.ExpectedTargets!);
        var initial = rig.Snapshot();
        if (phase == "before-copy") rig.Mutate();
        var job = rig.Jobs.Submit(request); rig.Jobs.MaxConcurrent = 4; rig.Jobs.Schedule();
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || rig.Jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20)); await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        var after = rig.Snapshot();
        string sourceAfter = Hash(File.ReadAllBytes(rig.Source));
        string[] names = Directory.GetFiles(Path.GetDirectoryName(rig.Target)!).Select(Path.GetFileName).ToArray()!;
        var calls = rig.Calls.ToArray();
        output.WriteLine("SYNC_TARGET_PUBLICATION " + JsonSerializer.Serialize(new
        {
            Mode = mode.ToString(), change, phase, nested, left, Action = item.Action.ToString(),
            Expected = new { expected.Value.Size, expected.Value.ModifiedTicks }, Initial = initial, During = rig.During, After = after,
            Calls = calls, rig.Decisions, State = job.State.ToString(), job.BytesDone,
            Issues = job.Issues.Select(i => new { i.Message, Outcome = i.Outcome.ToString() }).ToArray(),
            SourceBefore = rig.SourceHash, SourceAfter = sourceAfter, Sentinel = File.ReadAllText(rig.Sentinel), Names = names,
            rig.Root, rig.Source, rig.Target, ActualTreeComparePlannerAndJob = true, ActualOwnedFilesAndPortableCopyMove = true,
            ControlledMutationAndFirstMoveFailure = true, NativeWindowsEngineAtomicCASFullByteOrCandidateQualified = false,
        }));
        Assert.Equal(rig.SourceHash, sourceAfter); Assert.Equal("independent owned file", File.ReadAllText(rig.Sentinel));
        Assert.NotNull(rig.During);
        Assert.Equal(change == "gone" ? [] : new[] { "notes.txt" }, names);
        if (change == "unchanged")
        {
            Assert.Equal(rig.SourceHash, after.SHA256); Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(phase == "retry-publication" ? 1 : 0, rig.Decisions);
            Assert.Equal(phase == "retry-publication" ? 2 : 1, calls.Count(c => c.Operation == "Move"));
        }
        else
        {
            Assert.Equal(rig.During, after);
            Assert.Contains(job.Issues, i => i.Message.StartsWith("Not replaced: the file here changed after the comparison", StringComparison.Ordinal));
            Assert.Equal(phase == "retry-publication" ? 1 : 0, rig.Decisions);
            Assert.Equal(phase == "retry-publication" ? 1 : 0, calls.Count(c => c.Operation == "Move"));
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed record Snapshot(bool Exists, long Length, long ModifiedTicks, string? SHA256);
    private sealed record Call(string Operation, string Path, string? Source = null);
    private sealed class Rig : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("fc-sync-target-").FullName;
        public string Left, Right, TargetRoot, Source, Target, Sentinel, SourceHash;
        public ProviderRegistry Providers { get; } = new();
        public JobManager Jobs { get; }
        public ConcurrentQueue<Call> Calls { get; } = new();
        public int Decisions;
        public Snapshot? During;
        private readonly string change, phase;
        private bool mutated;
        public Rig(SyncMode mode, string change, string phase, bool nested, bool left)
        {
            this.change = change; this.phase = phase;
            Left = Directory.CreateDirectory(Path.Join(Root, "left")).FullName; Right = Directory.CreateDirectory(Path.Join(Root, "right")).FullName;
            TargetRoot = left ? Right : Left; string sourceRoot = left ? Left : Right;
            Source = Path.Join(sourceRoot, nested ? "nested" : "", "notes.txt"); Target = Path.Join(TargetRoot, nested ? "nested" : "", "notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!); Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
            File.WriteAllBytes(Source, "mine edited bytes"u8.ToArray()); File.WriteAllBytes(Target, "base"u8.ToArray());
            var old = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Source, mode == SyncMode.Update ? old.AddDays(2) : old); File.SetLastWriteTimeUtc(Target, old);
            SourceHash = Hash(File.ReadAllBytes(Source)); Sentinel = Path.Join(Root, "sentinel"); File.WriteAllText(Sentinel, "independent owned file");
            Providers.Register(new LocalFileSystemProvider());
            Jobs = new JobManager(new Files(this), Providers, Path.Join(Root, "journal")) { MaxConcurrent = 0 };
            Jobs.DecisionRequested += d =>
            {
                Interlocked.Increment(ref Decisions);
                if (phase == "retry-publication" && Decisions == 1) { Mutate(); d.Resolve(new Decision(DecisionAction.Retry)); }
                else d.Resolve(new Decision(DecisionAction.Skip));
            };
        }
        public Snapshot Snapshot() => File.Exists(Target) ? new(true, new FileInfo(Target).Length, File.GetLastWriteTimeUtc(Target).Ticks, Hash(File.ReadAllBytes(Target))) : new(false, 0, 0, null);
        public void Mutate()
        {
            Assert.False(mutated); mutated = true;
            if (change == "length") { var time = File.GetLastWriteTimeUtc(Target); File.WriteAllBytes(Target, "theirs, longer"u8.ToArray()); File.SetLastWriteTimeUtc(Target, time); }
            else if (change == "time") File.SetLastWriteTimeUtc(Target, File.GetLastWriteTimeUtc(Target).AddSeconds(1));
            else if (change == "gone") File.Delete(Target);
            During = Snapshot();
        }
        public void Dispose()
        {
            foreach (var j in Jobs.Jobs) j.Cancel(); Assert.True(SpinWait.SpinUntil(() => !Jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(Root)); Directory.Delete(Root, recursive: true); Assert.False(Directory.Exists(Root));
        }
        private sealed class Files(Rig rig) : PortableFileOperations
        {
            private bool failedMove;
            public override FileSystemItemInfo? TryGetInfo(string path)
            {
                var result = base.TryGetInfo(path);
                if (path == rig.Target)
                {
                    rig.Calls.Enqueue(new("Stat", path));
                    if (rig.phase == "after-initial-check" && !rig.mutated) rig.Mutate();
                }
                return result;
            }
            public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
            {
                rig.Calls.Enqueue(new("Copy", destination, source)); base.CopyFile(source, destination, options, progress, ct);
                if (rig.phase == "during-copy" && !rig.mutated) rig.Mutate();
            }
            public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
            {
                rig.Calls.Enqueue(new("Move", destination, source));
                if (rig.phase == "retry-publication" && !failedMove) { failedMove = true; throw new IOException("Owned first publication failure"); }
                base.Move(source, destination, replaceExisting, writeThrough);
            }
        }
    }
}
