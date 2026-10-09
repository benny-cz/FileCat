using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class SyncLinkPublicationTests(ITestOutputHelper output)
{
    public static TheoryData<SyncMode, string, string, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<SyncMode, string, string, bool, bool>();
            foreach (var mode in new[] { SyncMode.Update, SyncMode.Mirror })
                foreach (string phase in new[] { "before-copy", "after-initial-check", "materialize-link", "retry-publication" })
                    foreach (string change in phase is "materialize-link" or "retry-publication"
                        ? new[] { "unchanged", "length", "time", "gone", "link", "directory" }
                        : new[] { "unchanged", "length", "time", "gone" })
                        foreach (bool nested in new[] { false, true })
                            foreach (bool left in new[] { false, true }) cases.Add(mode, change, phase, nested, left);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Synchronization_preserves_compared_targets_when_copying_file_links(SyncMode mode, string change, string phase, bool nested, bool left)
    {
        if (new PortableFileOperations().CanCreateSymbolicLinks != true)
            Assert.Skip("This account cannot create symbolic links; actual file-link publication is unavailable.");
        using var rig = new Rig(change, phase, nested, left);
        var criteria = mode == SyncMode.Update ? CompareCriteria.Size | CompareCriteria.Time : CompareCriteria.Size;
        var comparison = TreeCompare.Compare(rig.Providers, Location.FileSystem(rig.Left), Location.FileSystem(rig.Right),
            criteria, TimeSpan.FromSeconds(2), OperatingSystem.IsWindows(), TestContext.Current.CancellationToken);
        var item = Assert.Single(SyncPlanner.Propose(comparison, left, mode, OperatingSystem.IsWindows())
            .Where(i => i.Entry.RelativePath == (nested ? "nested/notes.txt" : "notes.txt")));
        Assert.Equal(mode == SyncMode.Update ? SyncAction.ReplaceOlder : SyncAction.Replace, item.Action);
        Assert.True((left ? item.Entry.Left : item.Entry.Right)!.Value.Has(EntryFlags.Link));
        var request = Assert.Single(SyncPlanner.BuildRequests([item], left, Location.FileSystem(rig.TargetRoot), rig.Providers, true));
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
        string[] names = Directory.GetFileSystemEntries(Path.GetDirectoryName(rig.Target)!).Select(Path.GetFileName).Order().ToArray()!;
        var calls = rig.Calls.ToArray();
        output.WriteLine("SYNC_LINK_PUBLICATION " + JsonSerializer.Serialize(new
        {
            Mode = mode.ToString(), change, phase, nested, left, Action = item.Action.ToString(),
            Expected = new { expected.Value.Size, expected.Value.ModifiedTicks }, Initial = initial, During = rig.During, After = after,
            Calls = calls, rig.Decisions, State = job.State.ToString(), job.BytesDone,
            Issues = job.Issues.Select(i => new { i.Message, Outcome = i.Outcome.ToString() }).ToArray(),
            SourceHash = Hash(File.ReadAllBytes(rig.Source)), SourceLink = new FileInfo(rig.Source).LinkTarget,
            ReferentHash = Hash(File.ReadAllBytes(rig.Referent)), OtherHash = Hash(File.ReadAllBytes(rig.Other)),
            Sentinel = File.ReadAllText(rig.Sentinel), Names = names, rig.Root, rig.Source, rig.Target, rig.Referent, rig.Other,
            ActualTreeComparePlannerAndJobAndPortableSymbolicLinks = true, ControlledMutationAndFirstMoveFailure = true,
            NativeWindowsCopyEngineAtomicCASFullByteOrCandidateQualified = false,
        }));
        Assert.Equal(Hash("mine edited bytes"u8.ToArray()), Hash(File.ReadAllBytes(rig.Source)));
        Assert.Equal(rig.Referent, new FileInfo(rig.Source).LinkTarget);
        Assert.Equal(Hash("independent other bytes"u8.ToArray()), Hash(File.ReadAllBytes(rig.Other)));
        Assert.Equal("independent owned file", File.ReadAllText(rig.Sentinel));
        Assert.NotNull(rig.During);
        Assert.Equal(change == "gone" ? [] : new[] { "notes.txt" }, names);
        if (change == "unchanged")
        {
            Assert.True(after.IsLink); Assert.Equal(rig.Referent, after.LinkTarget);
            Assert.Equal(Hash("mine edited bytes"u8.ToArray()), after.SHA256); Assert.Equal(JobState.Completed, job.State);
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
    private sealed record Snapshot(bool Exists, bool IsDirectory, bool IsLink, long Length, long ModifiedTicks, string? LinkTarget, string? SHA256, string? Child);
    private sealed record Call(string Operation, string Path, string? Source = null);
    private sealed class Rig : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("fc-sync-link-").FullName;
        public string Left, Right, TargetRoot, Source, Target, Referent, Other, Sentinel;
        public ProviderRegistry Providers { get; } = new();
        public JobManager Jobs { get; }
        public ConcurrentQueue<Call> Calls { get; } = new();
        public int Decisions;
        public Snapshot? During;
        private readonly string change, phase;
        private bool mutated;
        public Rig(string change, string phase, bool nested, bool left)
        {
            this.change = change; this.phase = phase;
            Left = Directory.CreateDirectory(Path.Join(Root, "left")).FullName; Right = Directory.CreateDirectory(Path.Join(Root, "right")).FullName;
            TargetRoot = left ? Right : Left; string sourceRoot = left ? Left : Right;
            Source = Path.Join(sourceRoot, nested ? "nested" : "", "notes.txt"); Target = Path.Join(TargetRoot, nested ? "nested" : "", "notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!); Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
            Referent = Path.Join(Root, "referent.txt"); Other = Path.Join(Root, "other.txt"); Sentinel = Path.Join(Root, "sentinel");
            File.WriteAllBytes(Referent, "mine edited bytes"u8.ToArray()); File.WriteAllBytes(Other, "independent other bytes"u8.ToArray());
            File.CreateSymbolicLink(Source, Referent); File.WriteAllBytes(Target, "base"u8.ToArray());
            File.SetLastWriteTimeUtc(Target, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            File.WriteAllText(Sentinel, "independent owned file");
            Providers.Register(new LocalFileSystemProvider());
            Jobs = new JobManager(new Files(this), Providers, Path.Join(Root, "journal")) { MaxConcurrent = 0 };
            Jobs.DecisionRequested += d =>
            {
                Interlocked.Increment(ref Decisions);
                if (phase == "retry-publication" && Decisions == 1) { Mutate(); d.Resolve(new Decision(DecisionAction.Retry)); }
                else d.Resolve(new Decision(DecisionAction.Skip));
            };
        }
        public Snapshot Snapshot()
        {
            var info = new PortableFileOperations().TryGetInfo(Target);
            return info is null ? new(false, false, false, 0, 0, null, null, null)
                : new(true, info.IsDirectory, info.IsLink, info.Size, info.ModifiedUtc.Ticks, info.LinkTarget,
                    info.IsDirectory ? null : Hash(File.ReadAllBytes(Target)), info.IsDirectory ? File.ReadAllText(Path.Join(Target, "kept.txt")) : null);
        }
        public void Mutate()
        {
            Assert.False(mutated); mutated = true;
            if (change == "length") { var time = File.GetLastWriteTimeUtc(Target); File.WriteAllBytes(Target, "theirs, longer"u8.ToArray()); File.SetLastWriteTimeUtc(Target, time); }
            else if (change == "time") File.SetLastWriteTimeUtc(Target, File.GetLastWriteTimeUtc(Target).AddSeconds(1));
            else if (change == "gone") File.Delete(Target);
            else if (change == "link") { File.Delete(Target); File.CreateSymbolicLink(Target, Other); }
            else if (change == "directory") { File.Delete(Target); Directory.CreateDirectory(Target); File.WriteAllText(Path.Join(Target, "kept.txt"), "directory owned contents"); }
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
            public override bool TryCopyLink(string source, string destination, bool isDirectory, out string? error)
            {
                rig.Calls.Enqueue(new("CreateLink", destination, source));
                bool copied = base.TryCopyLink(source, destination, isDirectory, out error);
                if (copied && rig.phase == "materialize-link" && !rig.mutated) rig.Mutate();
                return copied;
            }
            public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
            {
                rig.Calls.Enqueue(new("Move", destination, source));
                if (rig.phase == "retry-publication" && !failedMove) { failedMove = true; throw new IOException("Owned first link publication failure"); }
                base.Move(source, destination, replaceExisting, writeThrough);
            }
        }
    }
}
