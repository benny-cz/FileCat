using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

public sealed class JobEngineTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly JobManager _jobs;
    private readonly string _src;
    private readonly string _dst;

    public JobEngineTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(new PortableFileOperations(), _providers, Path.Combine(_dir.Path, "journal"));
        _src = _dir.Dir("src");
        _dst = _dir.Dir("dst");
    }

    public void Dispose() => _dir.Dispose();

    private static ItemRef Item(string path) =>
        ItemRef.ForFileSystemPath(path, Directory.Exists(path) ? EntryKind.Directory : EntryKind.File);

    private static async Task<Job> WaitAsync(Job job, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Job still {job.State}");
            await Task.Delay(10);
        }
        return job;
    }

    private Job Submit(JobKind kind, IEnumerable<string> sources, string? dest = null, Action<TransferOptions>? configure = null, string? newName = null)
    {
        var options = new TransferOptions();
        configure?.Invoke(options);
        return _jobs.Submit(new JobRequest
        {
            Kind = kind,
            Sources = sources.Select(Item).ToList(),
            Destination = dest is null ? null : Location.FileSystem(dest),
            Options = options,
            NewName = newName,
        });
    }

    [Fact]
    public void Huge_job_uses_bounded_overlap_scopes_and_recovery_source_sample()
    {
        var sources = Enumerable.Range(0, 5000)
            .Select(i => ItemRef.ForFileSystemPath(Path.Combine(_src, $"f{i:0000}.txt"), EntryKind.File)).ToArray();
        var request = new JobRequest { Kind = JobKind.Copy, Sources = sources, Destination = Location.FileSystem(_dst) };
        var (title, device, reads, writes) = _jobs.Describe(request);
        Assert.Single(reads);
        Assert.Single(writes);
        Assert.Equal(_src, reads[0]);
        Assert.Equal(_dst, writes[0]);
        var job = new Job(request, title, device, reads, writes);
        using (JobJournal.Create(Path.Combine(_dir.Path, "journal"), job)) { }
        var interrupted = Assert.Single(JournalRecovery.Scan(Path.Combine(_dir.Path, "journal")));
        Assert.Equal(5000, interrupted.SourceCount);
        Assert.Equal(64, interrupted.Sources.Count);
    }
    [Fact]
    public async Task Copies_tree_and_leaves_no_staged_files()
    {
        File.WriteAllText(Path.Combine(_src, "a.txt"), "alpha");
        Directory.CreateDirectory(Path.Combine(_src, "sub", "deep"));
        File.WriteAllText(Path.Combine(_src, "sub", "deep", "b.bin"), new string('x', 100_000));
        Directory.CreateDirectory(Path.Combine(_src, "empty"));
        var job = await WaitAsync(Submit(JobKind.Copy, [_src], _dst));
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(_dst, "src", "a.txt")));
        Assert.Equal(100_000, new FileInfo(Path.Combine(_dst, "src", "sub", "deep", "b.bin")).Length);
        Assert.True(Directory.Exists(Path.Combine(_dst, "src", "empty")));
        Assert.Empty(Directory.GetFiles(_dst, JournalRecovery.StagedPrefix + "*", SearchOption.AllDirectories));
        Assert.Equal([0], job.CompletedRootIndices);
    }

    [Theory]
    [InlineData(ConflictPolicy.Skip, "old", 1)]
    [InlineData(ConflictPolicy.Replace, "new", 1)]
    [InlineData(ConflictPolicy.KeepBothRenameIncoming, "old", 2)]
    [InlineData(ConflictPolicy.KeepBothRenameExisting, "new", 2)]
    public async Task Conflict_policies(ConflictPolicy policy, string expectedContent, int expectedFiles)
    {
        var s = Path.Combine(_src, "f.txt");
        File.WriteAllText(s, "new");
        File.WriteAllText(Path.Combine(_dst, "f.txt"), "old");
        var job = await WaitAsync(Submit(JobKind.Copy, [s], _dst, o => o.Conflicts = policy));
        Assert.Equal(expectedContent, File.ReadAllText(Path.Combine(_dst, "f.txt")));
        Assert.Equal(expectedFiles, Directory.GetFiles(_dst).Length);
        Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues);
    }

    [Fact]
    public async Task Ask_policy_waits_for_a_decision_and_apply_to_all_is_job_scoped()
    {
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_src, $"f{i}.txt"), "new");
            File.WriteAllText(Path.Combine(_dst, $"f{i}.txt"), "old");
        }
        int asked = 0;
        _jobs.DecisionRequested += d =>
        {
            Interlocked.Increment(ref asked);
            d.Resolve(new Decision(DecisionAction.Replace, ApplyToAll: true));
        };
        var job = await WaitAsync(Submit(JobKind.Copy, Directory.GetFiles(_src), _dst));
        Assert.Equal(1, asked);
        Assert.All(Directory.GetFiles(_dst), f => Assert.Equal("new", File.ReadAllText(f)));
        Assert.Equal(JobState.Completed, job.State);
    }

    [Fact]
    public async Task Folder_cannot_be_copied_into_itself()
    {
        var inner = Directory.CreateDirectory(Path.Combine(_src, "inner")).FullName;
        var job = await WaitAsync(Submit(JobKind.Copy, [_src], inner));
        Assert.Equal(JobState.Failed, job.State);
        Assert.False(Directory.Exists(Path.Combine(inner, "src")));
    }

    [Fact]
    public async Task Same_volume_move_merges_folders_and_can_be_undone()
    {
        Directory.CreateDirectory(Path.Combine(_src, "m"));
        File.WriteAllText(Path.Combine(_src, "m", "one.txt"), "1");
        Directory.CreateDirectory(Path.Combine(_dst, "m"));
        File.WriteAllText(Path.Combine(_dst, "m", "two.txt"), "2");
        var job = await WaitAsync(Submit(JobKind.Move, [Path.Combine(_src, "m")], _dst));
        Assert.Equal(JobState.Completed, job.State);
        Assert.True(File.Exists(Path.Combine(_dst, "m", "one.txt")));
        Assert.True(File.Exists(Path.Combine(_dst, "m", "two.txt")));
        Assert.False(Directory.Exists(Path.Combine(_src, "m")));
        Assert.True(job.CanUndo);
        var report = UndoService.Undo(job, new PortableFileOperations());
        Assert.Contains(report, r => r.StartsWith("Restored"));
        Assert.True(File.Exists(Path.Combine(_src, "m", "one.txt")));
    }

    [Fact]
    public async Task Permanent_delete_removes_tree()
    {
        Directory.CreateDirectory(Path.Combine(_src, "t", "u"));
        File.WriteAllText(Path.Combine(_src, "t", "u", "x.txt"), "x");
        var ro = Path.Combine(_src, "t", "ro.txt");
        File.WriteAllText(ro, "r");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        _jobs.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.Proceed, true));
        var job = await WaitAsync(Submit(JobKind.Delete, [Path.Combine(_src, "t")]));
        Assert.Equal(JobState.Completed, job.State);
        Assert.False(Directory.Exists(Path.Combine(_src, "t")));
    }

    [Fact]
    public async Task Delete_of_a_directory_link_never_touches_the_target()
    {
        var target = _dir.Dir("target");
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        var link = Path.Combine(_src, "link");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // no privilege on this machine
        var job = await WaitAsync(Submit(JobKind.Delete, [link]));
        Assert.Equal(JobState.Completed, job.State);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public async Task Cancel_leaves_no_partial_destination()
    {
        var big = Path.Combine(_src, "big.bin");
        using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(200L * 1024 * 1024);
        var job = Submit(JobKind.Copy, [big], _dst, o => o.RateLimit = 5 * 1024 * 1024);
        while (job.BytesDone == 0 && !job.State.IsFinished()) await Task.Delay(10);
        job.Cancel();
        await WaitAsync(job);
        Assert.Equal(JobState.Canceled, job.State);
        Assert.False(File.Exists(Path.Combine(_dst, "big.bin")));
        Assert.Empty(Directory.GetFiles(_dst));
        Assert.True(File.Exists(big));
    }

    [Fact]
    public async Task Overlapping_job_waits_for_the_first()
    {
        var big = Path.Combine(_src, "big.bin");
        using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(60L * 1024 * 1024);
        var first = Submit(JobKind.Copy, [big], _dst, o => o.RateLimit = 40 * 1024 * 1024);
        var second = Submit(JobKind.Delete, [big]);
        await Task.Delay(100);
        Assert.Equal(JobState.Queued, second.State);
        Assert.Same(first, second.WaitingFor);
        await WaitAsync(first);
        await WaitAsync(second);
        Assert.True(File.Exists(Path.Combine(_dst, "big.bin")));
        Assert.False(File.Exists(big));
    }

    [Fact]
    public async Task Filtered_copy_transfers_only_matches()
    {
        File.WriteAllText(Path.Combine(_src, "a.cs"), "a");
        File.WriteAllText(Path.Combine(_src, "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(_src, "d"));
        File.WriteAllText(Path.Combine(_src, "d", "c.cs"), "c");
        var job = await WaitAsync(Submit(JobKind.Copy, [_src], _dst, o => o.Filter = Mask.Parse("*.cs")));
        Assert.True(File.Exists(Path.Combine(_dst, "src", "a.cs")));
        Assert.True(File.Exists(Path.Combine(_dst, "src", "d", "c.cs")));
        Assert.False(File.Exists(Path.Combine(_dst, "src", "b.txt")));
        Assert.NotEqual(JobState.Failed, job.State);
    }

    [Fact]
    public async Task Create_and_rename_record_undo()
    {
        var mk = await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.CreateDirectory, Destination = Location.FileSystem(_dst), NewName = Path.Combine("new", "nested") }));
        Assert.True(Directory.Exists(Path.Combine(_dst, "new", "nested")));
        UndoService.Undo(mk, new PortableFileOperations());
        Assert.False(Directory.Exists(Path.Combine(_dst, "new")));

        var f = Path.Combine(_src, "Name.txt");
        File.WriteAllText(f, "n");
        var rn = await WaitAsync(Submit(JobKind.Rename, [f], newName: "name.txt"));
        Assert.Equal(JobState.Completed, rn.State);
        Assert.Contains("name.txt", Directory.GetFiles(_src).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Journal_records_end_and_detects_interrupted_jobs()
    {
        File.WriteAllText(Path.Combine(_src, "a.txt"), "a");
        await WaitAsync(Submit(JobKind.Copy, [Path.Combine(_src, "a.txt")], _dst));
        var journalDir = Path.Combine(_dir.Path, "journal");
        Assert.Empty(JournalRecovery.Scan(journalDir));

        // Simulate a crash: a journal with an open publish intent and a leftover staged file.
        var staged = Path.Combine(_dst, JournalRecovery.StagedPrefix + "abcdef12-1.tmp");
        File.WriteAllText(staged, "partial");
        var path = Path.Combine(journalDir, "job-20260101000000-abcdef12.fcj");
        string Line(string json) => $"{Crc32.HashToUInt32(System.Text.Encoding.UTF8.GetBytes(json)):x8} {json}\n";
        var completed = string.Concat(Enumerable.Range(2, 500).Select(n =>
            Line($"{{\"t\":\"intent\",\"n\":{n},\"op\":\"copy\",\"path\":\"f{n}\"}}") +
            Line($"{{\"t\":\"done\",\"n\":{n}}}")));
        File.WriteAllText(path,
            Line("{\"t\":\"begin\",\"id\":\"abcdef12\",\"kind\":\"Copy\",\"title\":\"Copy x\",\"created\":\"2026-01-01T00:00:00Z\",\"sources\":[\"a\"]}") +
            Line($"{{\"t\":\"stagedir\",\"path\":{System.Text.Json.JsonSerializer.Serialize(_dst)}}}") +
            Line($"{{\"t\":\"intent\",\"n\":1,\"op\":\"publish\",\"path\":\"a\",\"target\":\"b\",\"staged\":{System.Text.Json.JsonSerializer.Serialize(staged)}}}") +
            completed +
            "deadbeef {\"t\":\"done\",\"n\":1}\n"); // torn/corrupt tail must be ignored
        var interrupted = Assert.Single(JournalRecovery.Scan(journalDir));
        Assert.Single(interrupted.OpenIntents);
        Assert.Equal(500, interrupted.CompletedSteps);
        Assert.Contains(staged, JournalRecovery.FindStagedLeftovers(interrupted));
        JournalRecovery.Close(interrupted, "cleaned");
        Assert.Empty(JournalRecovery.Scan(journalDir));
    }

    [Fact]
    public void Extracted_member_names_are_validated()
    {
        Assert.NotNull(SafeNames.Validate("../evil"));
        Assert.NotNull(SafeNames.Validate("a/b"));
        Assert.NotNull(SafeNames.Validate("file.txt:stream"));
        Assert.NotNull(SafeNames.Validate(".."));
        Assert.Null(SafeNames.Validate("ok.txt"));
    }

    [Fact]
    public async Task Captured_selection_runs_by_position_after_the_listing_moved_on()
    {
        File.WriteAllText(Path.Combine(_src, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_src, "b.txt"), "b");
        var store = new EntryStore();
        store.Append([new EntryData("a.txt", EntryKind.File, 1), new EntryData("missing.txt", EntryKind.File, 1), new EntryData("b.txt", EntryKind.File, 1)]);
        var snapshot = new SelectionSnapshot(_providers.Get(Schemes.FileSystem), Location.FileSystem(_src), store, [2, 1, 0]);
        store.Dispose(); // the listing navigated away; the lease keeps the capture readable
        var job = await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = snapshot, Destination = Location.FileSystem(_dst) }));
        Assert.Equal([0, 2], job.CompletedRootIndices);
        Assert.Equal([1], job.FailedRootIndices);
        Assert.Equal("b", File.ReadAllText(Path.Combine(_dst, "b.txt")));
        Assert.Equal(Location.FileSystem(_src), Assert.Single(ItemSources.Parents(snapshot)!));
        snapshot.Release();
        Assert.Throws<ObjectDisposedException>(() => store[0]);
        Assert.Throws<ObjectDisposedException>(() => snapshot[0]);
        Assert.Equal(1, snapshot.GetStoreIndex(1)); // positions stay mappable for unmarking
    }

    [Fact]
    public async Task Result_items_recreate_relative_folders_unless_flattened()
    {
        var deep = Directory.CreateDirectory(Path.Combine(_src, "x", "y")).FullName;
        File.WriteAllText(Path.Combine(deep, "r.txt"), "r");
        var item = new ItemRef(Location.FileSystem(deep), "r.txt", EntryKind.File) { RelativeFolder = Path.Combine("x", "y") };
        Assert.Equal(item, new ItemRef(Location.FileSystem(deep), "r.txt", EntryKind.File)); // not part of identity
        await WaitAsync(_jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [item], Destination = Location.FileSystem(_dst) }));
        Assert.True(File.Exists(Path.Combine(_dst, "x", "y", "r.txt")));
        var flat = Path.Combine(_dir.Path, "flat");
        await WaitAsync(_jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [item], Destination = Location.FileSystem(flat), Options = new TransferOptions { Flatten = true },
        }));
        Assert.True(File.Exists(Path.Combine(flat, "r.txt")));
    }

    [Fact]
    public async Task Unrecyclable_items_are_left_alone_or_deleted_by_consent_with_positions()
    {
        // The portable layer has no Recycle Bin on Windows, so every item is classified as unrecyclable.
        if (!OperatingSystem.IsWindows()) return;
        var files = Enumerable.Range(0, 3).Select(i => Path.Combine(_src, $"u{i}.txt")).ToList();
        foreach (var f in files) File.WriteAllText(f, "u");
        var kept = await WaitAsync(Submit(JobKind.Recycle, files));
        Assert.Equal([0, 1, 2], kept.FailedRootIndices);
        Assert.All(files, f => Assert.True(File.Exists(f)));
        Assert.Equal(3, kept.ItemsTotal);
        var deleted = await WaitAsync(Submit(JobKind.Recycle, files, configure: o => o.PermanentlyDeleteUnrecyclable = true));
        Assert.Equal([0, 1, 2], deleted.CompletedRootIndices);
        Assert.All(files, f => Assert.False(File.Exists(f)));
    }

    [Fact]
    public void Undo_is_not_recorded_partially_for_huge_operations()
    {
        var request = new JobRequest { Kind = JobKind.Move, Sources = [Item(_src)], Destination = Location.FileSystem(_dst) };
        var job = new Job(request, "Move", "local", [], []);
        for (int i = 0; i <= Job.UndoLimit; i++) job.AddUndo(new UndoStep(UndoKind.MoveBack, "a", "b", 0, 0));
        Assert.True(job.UndoTruncated);
        Assert.Empty(job.UndoSteps);
        job.AddUndo(new UndoStep(UndoKind.MoveBack, "a", "b", 0, 0));
        Assert.Empty(job.UndoSteps);
    }
}
