using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TransferRevalidationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> TreeCases()
    {
        foreach (int length in new[] { 4, 65537 })
        foreach (JobKind kind in new[] { JobKind.Copy, JobKind.Move })
        foreach (string change in new[] { "unchanged", "content-restamped", "added", "removed" })
            yield return [length, kind, change];
    }

    [Theory]
    [MemberData(nameof(TreeCases))]
    public async Task A_reviewed_tree_refuses_a_later_changed_or_new_child(int length, JobKind kind, string change)
        => await CheckTree(length, kind, change, rename: false);

    public static IEnumerable<object[]> RenameTreeCases()
    {
        foreach (int length in new[] { 4, 65537 })
        foreach (string change in new[] { "unchanged", "content-restamped", "added", "removed" }) yield return [length, change];
    }

    [Theory]
    [MemberData(nameof(RenameTreeCases))]
    public async Task A_same_volume_merge_refuses_a_later_changed_or_new_child(int length, string change)
        => await CheckTree(length, JobKind.Move, change, rename: true);

    private async Task CheckTree(int length, JobKind kind, string change, bool rename)
    {
        using var f = new Fixture(length);
        f.Files.RouteThroughCopy = !rename;
        string root = Directory.CreateDirectory(Path.Join(f.Root, "source-tree")).FullName;
        var leaves = new[] { "one", "two", "three" }.Select(n =>
        {
            var p = Path.Join(Directory.CreateDirectory(Path.Join(root, n)).FullName, "leaf.dat");
            File.WriteAllBytes(p, f.Original); return p;
        }).ToArray();
        if (rename)
            foreach (var n in new[] { "one", "two", "three" }) Directory.CreateDirectory(Path.Join(f.Destination, "source-tree", n));
        var provider = new LocalFileSystemProvider();
        var reviewed = Assert.IsType<FileTreeReview>(FileTreeReview.Capture(root, f.Files, provider));
        string? first = null, changed = null;
        Action<string, string> mutate = (source, destination) =>
        {
            first = source; changed = leaves.First(p => p != source);
            string parent = Path.GetDirectoryName(changed)!; var stamp = Directory.GetLastWriteTimeUtc(parent);
            if (change == "content-restamped") WriteRestamped(changed, f.Changed);
            else if (change == "added") { changed = Path.Join(parent, "unreviewed.dat"); File.WriteAllBytes(changed, f.Changed); }
            else if (change == "removed") File.Delete(changed);
            Directory.SetLastWriteTimeUtc(parent, stamp);
        };
        if (rename) f.Files.AfterRename = mutate; else f.Files.AfterCopy = mutate;
        var item = ItemRef.ForFileSystemPath(root, EntryKind.Directory);
        var job = await f.Run(new JobRequest
        {
            Kind = kind, Sources = [item], Destination = Location.FileSystem(f.Destination),
            ExpectedSourceTrees = new Dictionary<ItemRef, ReviewedSourceTree> { [item] = new(reviewed, provider) },
        });
        Assert.NotNull(first); Assert.NotNull(changed);
        string targetRoot = Path.Join(f.Destination, "source-tree");
        string changedTarget = Path.Join(targetRoot, Path.GetRelativePath(root, changed));
        string firstTarget = Path.Join(targetRoot, Path.GetRelativePath(root, first));
        Emit("tree-after-first-copy", new
        {
            length, kind, change, rename, job.State, FirstSource = first, ChangedSource = changed,
            FirstTargetSHA256 = Hash(firstTarget), ChangedSourceSHA256 = Hash(changed), ChangedTargetSHA256 = Hash(changedTarget),
            OriginalSHA256 = Digest(f.Original), ChangedSHA256 = Digest(f.Changed),
            Copies = f.Files.Copies.Select(p => new { p.Source, p.Destination }).ToArray(),
            Moves = f.Files.Moves.Select(p => new { p.Source, p.Destination }).ToArray(), Deletes = f.Files.Deletes.ToArray(),
            Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray(),
        }, syntheticDestinationVolumeRouting: !rename);
        Assert.Equal(Digest(f.Original), Hash(firstTarget));
        if (change == "unchanged")
        {
            Assert.Equal(JobState.Completed, job.State);
            foreach (var leaf in leaves) Assert.Equal(Digest(f.Original), Hash(Path.Join(targetRoot, Path.GetRelativePath(root, leaf))));
            Assert.Equal(kind == JobKind.Copy, Directory.Exists(root));
        }
        else
        {
            Assert.False(File.Exists(changedTarget));
            if (change != "removed") Assert.Equal(Digest(f.Changed), Hash(changed));
            Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange);
            Assert.DoesNotContain(f.Files.Copies, p => p.Source == changed);
            Assert.DoesNotContain(f.Files.Moves, p => p.Source == changed);
        }
    }

    public static IEnumerable<object[]> MoveCases()
    {
        foreach (int length in new[] { 4, 1048576 })
        foreach (VerifyMode verify in new[] { VerifyMode.Native, VerifyMode.ReadBack })
        foreach (string moment in new[] { "after-copy", "after-publish" })
        foreach (string change in new[] { "unchanged", "source-restamped", "target-restamped", "target-removed" })
            yield return [length, verify, moment, change];
    }

    [Theory]
    [MemberData(nameof(MoveCases))]
    public async Task A_copied_move_keeps_the_source_when_the_published_bytes_change(int length, VerifyMode verify, string moment, string change)
    {
        using var f = new Fixture(length);
        string source = Path.Join(f.Root, "source.dat"); File.WriteAllBytes(source, f.Original);
        string name = moment == "after-publish" ? "published.dat" : "source.dat";
        string target = Path.Join(f.Destination, name); bool applied = false;
        void Mutate(string written)
        {
            applied = true;
            if (change == "source-restamped") WriteRestamped(source, f.Changed);
            else if (change == "target-restamped") WriteRestamped(written, f.Changed);
            else if (change == "target-removed") File.Delete(written);
        }
        if (moment == "after-copy") f.Files.AfterCopy = (_, written) => Mutate(written);
        else f.Files.AfterPublish = written => { Assert.Equal(target, written); Mutate(written); };
        var job = await f.Run(new JobRequest
        {
            Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(f.Destination), NewName = name,
            Options = new TransferOptions { Verify = verify },
        });
        Assert.True(applied);
        Emit("move-before-source-delete", new
        {
            length, verify, moment, change, job.State, SourceSHA256 = Hash(source), TargetSHA256 = Hash(target),
            OriginalSHA256 = Digest(f.Original), ChangedSHA256 = Digest(f.Changed),
            Copies = f.Files.Copies.Select(p => new { p.Source, p.Destination }).ToArray(), Deletes = f.Files.Deletes.ToArray(),
            job.VerifyBytesDone, job.VerifyBytesTotal,
            Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray(),
        });
        if (change == "unchanged")
        {
            Assert.Equal(JobState.Completed, job.State); Assert.False(File.Exists(source));
            Assert.Equal(Digest(f.Original), Hash(target)); Assert.Single(f.Files.Deletes, p => p == source);
        }
        else
        {
            Assert.Equal(change == "source-restamped" ? Digest(f.Changed) : Digest(f.Original), Hash(source));
            Assert.DoesNotContain(source, f.Files.Deletes);
            Assert.NotEqual(JobState.Completed, job.State); Assert.NotEmpty(job.Issues);
        }
    }

    public static IEnumerable<object[]> CopyCases()
    {
        foreach (int length in new[] { 4, 65537 })
        foreach (VerifyMode verify in new[] { VerifyMode.Native, VerifyMode.ReadBack })
        foreach (string route in new[] { "direct", "staged", "replace" })
        foreach (bool changed in new[] { false, true }) yield return [length, verify, route, changed];
    }

    [Theory]
    [MemberData(nameof(CopyCases))]
    public async Task The_written_bytes_must_match_the_review_before_a_copy_is_published(int length, VerifyMode verify, string route, bool changed)
    {
        using var f = new Fixture(length);
        string source = Path.Join(f.Root, "source.dat"); File.WriteAllBytes(source, f.Original);
        string name = route == "direct" ? "source.dat" : "published.dat";
        string target = Path.Join(f.Destination, name);
        var existing = f.Changed.Select(b => (byte)(b ^ 0x33)).ToArray();
        if (route == "replace") { Directory.CreateDirectory(f.Destination); File.WriteAllBytes(target, existing); }
        var provider = new LocalFileSystemProvider(); var item = ItemRef.ForFileSystemPath(source, EntryKind.File);
        var reviewed = Assert.IsType<FileTreeReview>(FileTreeReview.Capture(source, f.Files, provider)); bool applied = false;
        f.Files.BeforeCopy = () => { applied = true; if (changed) WriteRestamped(source, f.Changed); };
        var job = await f.Run(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [item], Destination = Location.FileSystem(f.Destination), NewName = name,
            ExpectedSourceTrees = new Dictionary<ItemRef, ReviewedSourceTree> { [item] = new(reviewed, provider) },
            Options = new TransferOptions { Verify = verify, Conflicts = ConflictPolicy.Replace },
        });
        Assert.True(applied);
        Emit("copied-reviewed-bytes", new
        {
            length, verify, route, changed, job.State, SourceSHA256 = Hash(source), TargetSHA256 = Hash(target),
            OriginalSHA256 = Digest(f.Original), ChangedSHA256 = Digest(f.Changed), ExistingSHA256 = Digest(existing),
            Copies = f.Files.Copies.Select(p => new { p.Source, p.Destination }).ToArray(), Deletes = f.Files.Deletes.ToArray(),
            Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray(),
        });
        Assert.Equal(changed ? Digest(f.Changed) : Digest(f.Original), Hash(source));
        if (changed)
        {
            Assert.Equal(route == "replace" ? Digest(existing) : null, Hash(target));
            Assert.NotEqual(JobState.Completed, job.State); Assert.NotEmpty(job.Issues);
        }
        else { Assert.Equal(JobState.Completed, job.State); Assert.Equal(Digest(f.Original), Hash(target)); }
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(1048576, false)]
    [InlineData(1048576, true)]
    public async Task An_actual_Windows_sharing_refusal_keeps_the_copied_move_source(int length, bool lockTarget)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The FileShare.None preflight is an actual Windows sharing refusal.");
        using var f = new Fixture(length); string source = Path.Join(f.Root, "source.dat"); File.WriteAllBytes(source, f.Original);
        string target = Path.Join(f.Destination, "published.dat"); FileStream? held = null; bool refused = false;
        f.Files.AfterPublish = written =>
        {
            string path = lockTarget ? written : source;
            held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Throws<IOException>(() => { using var probe = File.OpenRead(path); }); refused = true;
        };
        Job job;
        try { job = await f.Run(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(f.Destination), NewName = "published.dat" }); }
        finally { held?.Dispose(); }
        Emit("move-native-sharing", new
        {
            length, lockTarget, job.State, ActualNativeSharingRefusalVerified = refused,
            SourceSHA256 = Hash(source), TargetSHA256 = Hash(target), OriginalSHA256 = Digest(f.Original),
            Copies = f.Files.Copies.Select(p => new { p.Source, p.Destination }).ToArray(), Deletes = f.Files.Deletes.ToArray(),
            Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray(),
        });
        Assert.True(refused); Assert.Equal(Digest(f.Original), Hash(source)); Assert.Equal(Digest(f.Original), Hash(target));
        Assert.DoesNotContain(source, f.Files.Deletes); Assert.NotEqual(JobState.Completed, job.State);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(65537, false)]
    [InlineData(65537, true)]
    public async Task A_same_volume_replace_rechecks_source_bytes_after_the_conflict_answer(int length, bool changed)
    {
        using var f = new Fixture(length); f.Files.RouteThroughCopy = false;
        string source = Path.Join(f.Root, "source.dat"); File.WriteAllBytes(source, f.Original);
        Directory.CreateDirectory(f.Destination); string target = Path.Join(f.Destination, "source.dat");
        var existing = f.Changed.Select(b => (byte)(b ^ 0x33)).ToArray(); File.WriteAllBytes(target, existing);
        var provider = new LocalFileSystemProvider(); var item = ItemRef.ForFileSystemPath(source, EntryKind.File);
        var reviewed = Assert.IsType<FileTreeReview>(FileTreeReview.Capture(source, f.Files, provider)); bool answered = false;
        f.DecisionAnswer = DecisionAction.Replace;
        f.BeforeDecision = () => { answered = true; if (changed) WriteRestamped(source, f.Changed); };
        var job = await f.Run(new JobRequest
        {
            Kind = JobKind.Move, Sources = [item], Destination = Location.FileSystem(f.Destination),
            ExpectedSourceTrees = new Dictionary<ItemRef, ReviewedSourceTree> { [item] = new(reviewed, provider) },
        });
        Emit("rename-after-conflict", new
        {
            length, changed, answered, job.State, SourceSHA256 = Hash(source), TargetSHA256 = Hash(target),
            OriginalSHA256 = Digest(f.Original), ChangedSHA256 = Digest(f.Changed), ExistingSHA256 = Digest(existing),
            Moves = f.Files.Moves.Select(p => new { p.Source, p.Destination }).ToArray(),
            Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray(),
        }, syntheticDestinationVolumeRouting: false);
        Assert.True(answered);
        if (changed)
        {
            Assert.Equal(Digest(f.Changed), Hash(source)); Assert.Equal(Digest(existing), Hash(target));
            Assert.Empty(f.Files.Moves); Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange);
        }
        else { Assert.Equal(JobState.Completed, job.State); Assert.False(File.Exists(source)); Assert.Equal(Digest(f.Original), Hash(target)); }
    }

    private void Emit(string control, object detail, bool syntheticDestinationVolumeRouting = true) => output.WriteLine("TRANSFER_REVALIDATION " + JsonSerializer.Serialize(new
    {
        control, detail, OwnedRealFiles = true, SyntheticDestinationVolumeRouting = syntheticDestinationVolumeRouting,
        ActualMountedOrNativeDesktopQualified = false, AtomicHandleOrPathRaceQualified = false, PhysicalSource = false,
    }));
    private static string? Hash(string path) => File.Exists(path) ? Digest(File.ReadAllBytes(path)) : null;
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void WriteRestamped(string path, byte[] bytes)
    {
        var stamp = File.GetLastWriteTimeUtc(path); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, stamp);
    }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-transfer-version", Guid.NewGuid().ToString("N"))).FullName;
        public readonly byte[] Original, Changed;
        public readonly Files Files;
        private readonly JobManager _jobs;
        public Action? BeforeDecision;
        public DecisionAction DecisionAnswer = DecisionAction.Skip;
        public string Destination => Path.Join(Root, "destination");
        public Fixture(int length)
        {
            Original = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
            Changed = Original.ToArray(); Changed[0] ^= 0x55; Changed[^1] ^= 0x22;
            Files = new(Destination); var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
            _jobs = new(Files, providers, Path.Join(Root, "journals")) { MaxConcurrent = 0 };
            _jobs.DecisionRequested += d => { BeforeDecision?.Invoke(); d.Resolve(new Decision(DecisionAnswer)); };
        }
        public async Task<Job> Run(JobRequest request)
        {
            var job = _jobs.Submit(request); _jobs.MaxConcurrent = 4; _jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || _jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Owned transfer did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            return job;
        }
        public void Dispose()
        {
            foreach (var job in _jobs.Jobs) job.Cancel();
            SpinWait.SpinUntil(() => !_jobs.HasActiveWork, TimeSpan.FromSeconds(3)); Directory.Delete(Root, true);
        }
    }
    private sealed class Files(string destinationRoot) : PortableFileOperations
    {
        public Action? BeforeCopy;
        public Action<string, string>? AfterCopy;
        public Action<string>? AfterPublish;
        public Action<string, string>? AfterRename;
        public bool RouteThroughCopy = true;
        public readonly System.Collections.Concurrent.ConcurrentQueue<(string Source, string Destination)> Copies = new();
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Deletes = new();
        public readonly System.Collections.Concurrent.ConcurrentQueue<(string Source, string Destination)> Moves = new();
        public override string GetVolumeRoot(string path) => RouteThroughCopy && path.StartsWith(destinationRoot, PathUtil.SafetyComparison) ? destinationRoot : base.GetVolumeRoot(path);
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            Copies.Enqueue((source, destination)); Interlocked.Exchange(ref BeforeCopy, null)?.Invoke();
            base.CopyFile(source, destination, options, progress, ct);
            Interlocked.Exchange(ref AfterCopy, null)?.Invoke(source, destination);
        }
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            base.Move(source, destination, replaceExisting, writeThrough);
            Moves.Enqueue((source, destination)); Interlocked.Exchange(ref AfterRename, null)?.Invoke(source, destination);
            Interlocked.Exchange(ref AfterPublish, null)?.Invoke(destination);
        }
        public override void DeleteFile(string path) { Deletes.Enqueue(path); base.DeleteFile(path); }
    }
}
