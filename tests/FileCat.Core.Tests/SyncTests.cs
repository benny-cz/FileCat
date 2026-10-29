using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class SyncTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly DateTime Old = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _dir.Dispose();

    private string Put(string relative, string content, DateTime time)
    {
        var path = _dir.File(relative, content);
        File.SetLastWriteTimeUtc(path, time);
        return path;
    }

    /// <summary>
    /// left: a (new), b (newer), c (same), d (different size, same time), g (older than the right's), sub/e (new), newdir/f (new folder)
    /// right: b (older), c (same), d, g (newer), sub/, x (only right), olddir/y (only right)
    /// </summary>
    private (string Left, string Right) Trees()
    {
        Put("L/a.txt", "a", Old);
        Put("L/b.txt", "b-new", New);
        Put("L/c.txt", "same", Old);
        Put("L/d.txt", "left d", Old);
        Put("L/g.txt", "g-old", Old);
        Put("L/sub/e.txt", "e", Old);
        Put("L/newdir/f.txt", "f", Old);
        Put("R/b.txt", "b-old", Old);
        Put("R/c.txt", "same", Old);
        Put("R/d.txt", "right d, longer", Old);
        Put("R/g.txt", "g-new", New);
        _dir.Dir("R/sub");
        Put("R/x.txt", "x", Old);
        Put("R/olddir/y.txt", "y", Old);
        return (Path.Combine(_dir.Path, "L"), Path.Combine(_dir.Path, "R"));
    }

    private static ProviderRegistry Providers()
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        return providers;
    }

    private static TreeCompareResult Compare(ProviderRegistry providers, string left, string right) =>
        TreeCompare.Compare(providers, Location.FileSystem(left), Location.FileSystem(right), CompareCriteria.Size | CompareCriteria.Time,
            TimeSpan.FromSeconds(2), OperatingSystem.IsWindows(), TestContext.Current.CancellationToken);

    private static Dictionary<string, SyncItem> ByPath(List<SyncItem> items) => items.ToDictionary(i => i.Entry.RelativePath);

    private async Task Run(ProviderRegistry providers, List<JobRequest> requests, bool allowUnboundTree = false)
    {
        var manager = new JobManager(new ReviewedReplacementTestFiles(), providers, Path.Combine(_dir.Path, "journal"));
        var jobs = requests.Select(manager.Submit).ToList();
        foreach (var job in jobs)
        {
            while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
            if (allowUnboundTree && job.Request.OnlyAsCompared && job.State == JobState.CompletedWithIssues)
                Assert.Contains(job.Issues, i => i.Message.Contains("cannot delete a bound, fully reviewed tree", StringComparison.Ordinal));
            else
                Assert.True(job.State == JobState.Completed, $"{job.Title}: {job.State} {string.Join("; ", job.Issues.Select(i => i.Message))}");
        }
    }

    [Fact]
    public void Folders_inside_each_other_are_not_offered_for_synchronizing()
    {
        // Release issue I94: a folder mirrored onto its parent finds the parent's copy of itself "only in the target",
        // and would remove it: the source.
        Put("P/a.txt", "a", Old);
        Put("P/backup/a.txt", "a", Old);
        string parent = Path.Combine(_dir.Path, "P"), inner = Path.Combine(parent, "backup");
        var plan = SyncPlanner.Propose(Compare(Providers(), parent, inner), sourceIsLeft: false, SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        var removal = Assert.Single(plan, i => i.Entry.RelativePath == "backup");
        Assert.Equal(SyncAction.Remove, removal.Action); // what would have happened

        Assert.StartsWith("The two folders overlap: one is inside the other.", SyncPlanner.Overlap(parent, inner, _ => null));
        Assert.NotNull(SyncPlanner.Overlap(inner, parent, _ => null));
        Assert.NotNull(SyncPlanner.Overlap(parent, parent, _ => null));
        Assert.Null(SyncPlanner.Overlap(parent, Path.Combine(_dir.Path, "Q"), _ => null));
        // Folders whose names only begin alike are apart.
        Assert.Null(SyncPlanner.Overlap(parent, parent + "-copy", _ => null));
    }

    [Fact]
    public async Task Mirror_copies_and_replaces_but_refuses_unbound_tree_removal()
    {
        var (left, right) = Trees();
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        var by = ByPath(plan);
        Assert.NotNull(by["x.txt"].Entry.RemovalReview);
        Assert.Equal((SyncAction.Copy, true), (by["a.txt"].Action, by["a.txt"].Include));
        Assert.Equal((SyncAction.ReplaceOlder, true), (by["b.txt"].Action, by["b.txt"].Include));
        Assert.Equal((SyncAction.Replace, true), (by["d.txt"].Action, by["d.txt"].Include));
        Assert.Equal((SyncAction.Replace, false), (by["g.txt"].Action, by["g.txt"].Include));
        Assert.Equal((SyncAction.Copy, true), (by["sub/e.txt"].Action, by["sub/e.txt"].Include));
        Assert.Equal((SyncAction.Copy, true), (by["newdir"].Action, by["newdir"].Include));
        Assert.Equal((SyncAction.Remove, true), (by["x.txt"].Action, by["x.txt"].Include));
        Assert.Equal((SyncAction.Remove, true), (by["olddir"].Action, by["olddir"].Include));
        Assert.DoesNotContain("c.txt", by.Keys);
        Assert.Equal("copy 3, replace 2, remove 2", SyncPlanner.Describe(plan));

        by["x.txt"].Include = false; // the user keeps one extra item
        await Run(providers, SyncPlanner.BuildRequests(plan, sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true),
            allowUnboundTree: true);

        Assert.Equal("a", File.ReadAllText(Path.Combine(right, "a.txt")));
        Assert.Equal("b-new", File.ReadAllText(Path.Combine(right, "b.txt")));
        Assert.Equal("left d", File.ReadAllText(Path.Combine(right, "d.txt")));
        Assert.Equal("g-new", File.ReadAllText(Path.Combine(right, "g.txt")));
        Assert.Equal("e", File.ReadAllText(Path.Combine(right, "sub", "e.txt")));
        Assert.Equal("f", File.ReadAllText(Path.Combine(right, "newdir", "f.txt")));
        Assert.True(File.Exists(Path.Combine(right, "x.txt")));
        Assert.True(Directory.Exists(Path.Combine(right, "olddir"))); // Portable adapter has no object-bound tree delete.
        // The source is never touched.
        Assert.Equal(7, Directory.GetFiles(left, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Mirror_removes_a_target_item_only_while_it_is_as_compared()
    {
        // Release plan DPI P10: the removals ran as ordinary deletions of whatever was at the path when they ran; a target
        // file edited while the plan was reviewed was deleted, here permanently.
        var (left, right) = Trees();
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        var by = ByPath(plan);
        Assert.Equal((SyncAction.Remove, true), (by["x.txt"].Action, by["x.txt"].Include));
        Assert.Equal((SyncAction.Remove, true), (by["olddir"].Action, by["olddir"].Include));
        File.WriteAllText(Path.Combine(right, "x.txt"), "x, edited after the comparison");
        File.WriteAllText(Path.Combine(right, "olddir", "z.txt"), "added after the comparison"); // the folder's time changes with it
        var requests = SyncPlanner.BuildRequests(plan.Where(i => i.Action == SyncAction.Remove), sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true);
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
        var job = manager.Submit(Assert.Single(requests));
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("x, edited after the comparison", File.ReadAllText(Path.Combine(right, "x.txt")));
        Assert.True(File.Exists(Path.Combine(right, "olddir", "z.txt")));
        Assert.Contains(job.Issues, i => i.Message.StartsWith("Not removed: it changed after the comparison", StringComparison.Ordinal));
        Assert.Contains(job.Issues, i => i.Message.StartsWith("Not removed: what it holds changed after the comparison that planned this (1 file, 1 byte then, 2 files, 27 bytes now)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mirror_keeps_a_same_metadata_replacement_of_a_compared_target_file()
    {
        string left = _dir.Dir("L");
        string original = Put("R/x.txt", "A", Old);
        string right = Path.GetDirectoryName(original)!;
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true,
            SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        Assert.Equal(SyncAction.Remove, Assert.Single(plan).Action);
        string retired = Path.Combine(_dir.Path, "retired-A.txt");
        File.Move(original, retired);
        File.WriteAllText(original, "B");
        File.SetLastWriteTimeUtc(original, Old);
        var request = Assert.Single(SyncPlanner.BuildRequests(plan, sourceIsLeft: true,
            Location.FileSystem(right), providers, deletePermanently: true));
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "adverse-journal"));
        var job = manager.Submit(request);
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("B", File.ReadAllText(original));
        Assert.NotEqual(JobState.Completed, job.State);
    }

    [Fact]
    public async Task An_adapter_without_bound_replace_keeps_both_source_and_destination()
    {
        string source = Put("source/item.bin", "INCOMING", Old);
        string dest = _dir.Dir("dest");
        string target = Put("dest/item.bin", "REVIEWED", Old);
        var providers = Providers();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "unbound-journal"));
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(dest),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
        });
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal("INCOMING", File.ReadAllText(source));
        Assert.Equal("REVIEWED", File.ReadAllText(target));
        Assert.Contains(job.Issues, i => i.Message.Contains("cannot bind replacement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mirror_removes_a_folder_only_while_all_it_holds_is_as_compared()
    {
        // A folder's own time changes only for its direct items, and on NTFS it is listed late and stays the same within a
        // clock tick: a change deeper in, or one at once, must still keep the folder (I50).
        Put("L/keep.txt", "k", Old);
        Put("R/keep.txt", "k", Old);
        Put("R/old/deep/w.txt", "written", Old);
        Put("R/gone/v.txt", "v", Old);
        var left = Path.Combine(_dir.Path, "L");
        var right = Path.Combine(_dir.Path, "R");
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        var by = ByPath(plan);
        Assert.Equal("Only in the target: Mirror removes it with all it holds (1 file and 1 folder, 7 bytes)", by["old"].Reason);
        Assert.Equal("Only in the target: Mirror removes it with all it holds (1 file, 1 byte)", by["gone"].Reason);
        Put("R/old/deep/w.txt", "WRITTEN", New); // the same size, another time, two levels down
        await Run(providers, [.. SyncPlanner.BuildRequests(plan.Where(i => i.Entry.RelativePath == "gone"), sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true)],
            allowUnboundTree: true);
        Assert.True(Directory.Exists(Path.Combine(right, "gone"))); // portable adapter conservatively keeps an unbound tree.
        var requests = SyncPlanner.BuildRequests(plan.Where(i => i.Entry.RelativePath == "old"), sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true);
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
        var job = manager.Submit(Assert.Single(requests));
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("WRITTEN", File.ReadAllText(Path.Combine(right, "old", "deep", "w.txt")));
        Assert.Contains(job.Issues, i => i.Message.StartsWith("Not removed: what it holds changed after the comparison", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mirror_replaces_a_target_file_only_while_it_is_as_compared()
    {
        // Release plan DPI P10: a replacement overwrote a target file edited while the plan was reviewed.
        var (left, right) = Trees();
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        var by = ByPath(plan);
        Assert.Equal((SyncAction.Replace, true), (by["d.txt"].Action, by["d.txt"].Include));
        File.WriteAllText(Path.Combine(right, "d.txt"), "right d, edited after the comparison");
        var requests = SyncPlanner.BuildRequests(plan.Where(i => i.Entry.RelativePath == "d.txt"), sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true);
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
        var job = manager.Submit(Assert.Single(requests));
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("right d, edited after the comparison", File.ReadAllText(Path.Combine(right, "d.txt")));
        Assert.Contains(job.Issues, i => i.Message.StartsWith("Not replaced: the file here changed after the comparison", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_copies_new_and_newer_items_and_removes_nothing()
    {
        var (left, right) = Trees();
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true, SyncMode.Update, targetIgnoresCase: OperatingSystem.IsWindows());
        var by = ByPath(plan);
        Assert.Equal((SyncAction.None, false), (by["x.txt"].Action, by["x.txt"].Include));
        Assert.Equal((SyncAction.Replace, false), (by["d.txt"].Action, by["d.txt"].Include));
        Assert.False(by["olddir"].CanInclude);
        Assert.Equal("copy 3, replace 1", SyncPlanner.Describe(plan));
        await Run(providers, SyncPlanner.BuildRequests(plan, sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true));
        Assert.Equal("b-new", File.ReadAllText(Path.Combine(right, "b.txt")));
        Assert.Equal("right d, longer", File.ReadAllText(Path.Combine(right, "d.txt")));
        Assert.True(File.Exists(Path.Combine(right, "x.txt")) && File.Exists(Path.Combine(right, "olddir", "y.txt")));

        // Right to left: the left receives what only the right has, and g, newer on the right.
        plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: false, SyncMode.Update, targetIgnoresCase: OperatingSystem.IsWindows());
        by = ByPath(plan);
        Assert.Equal(SyncAction.ReplaceOlder, by["g.txt"].Action);
        Assert.Equal(SyncAction.Copy, by["x.txt"].Action);
        await Run(providers, SyncPlanner.BuildRequests(plan, sourceIsLeft: false, Location.FileSystem(left), providers, deletePermanently: true));
        Assert.Equal("g-new", File.ReadAllText(Path.Combine(left, "g.txt")));
        Assert.Equal("y", File.ReadAllText(Path.Combine(left, "olddir", "y.txt")));
    }

    [Fact]
    public void Letter_case_collisions_and_unsafe_names_are_never_synchronized()
    {
        var folder = Location.FileSystem(_dir.Path);
        EntryData File(string name) => new(name, EntryKind.File, 1, Old.Ticks);
        var comparison = new TreeCompareResult(
        [
            new TreeDiffEntry("Readme.md", TreeDiffKind.LeftOnly, File("Readme.md"), null) { LeftFolder = folder, RightFolder = folder },
            new TreeDiffEntry("README.md", TreeDiffKind.RightOnly, null, File("README.md")) { LeftFolder = folder, RightFolder = folder },
            new TreeDiffEntry("notes.txt", TreeDiffKind.Same, File("notes.txt"), File("notes.txt")) { LeftFolder = folder, RightFolder = folder },
            new TreeDiffEntry("Notes.txt", TreeDiffKind.LeftOnly, File("Notes.txt"), null) { LeftFolder = folder, RightFolder = folder },
            new TreeDiffEntry(@"..\evil/x.txt", TreeDiffKind.LeftOnly, File("x.txt"), null) { LeftFolder = folder, RightFolder = folder },
            new TreeDiffEntry("ok.txt", TreeDiffKind.LeftOnly, File("ok.txt"), null) { LeftFolder = folder, RightFolder = folder },
        ], true, 1);
        var by = ByPath(SyncPlanner.Propose(comparison, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: true));
        Assert.All(new[] { "Readme.md", "README.md", "Notes.txt", @"..\evil/x.txt" }, p => Assert.False(by[p].CanInclude, p));
        Assert.True(by["ok.txt"].Include);
        // A case-sensitive target keeps them apart.
        by = ByPath(SyncPlanner.Propose(comparison, sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: false));
        // This synthetic comparison has no content review for the target-only README. Its name no longer collides,
        // but Mirror still cannot remove it without the new deletion review.
        Assert.True(by["Readme.md"].Include && by["Notes.txt"].Include);
        Assert.False(by["README.md"].CanInclude);
        Assert.False(by[@"..\evil/x.txt"].CanInclude);
    }

    [Fact]
    public void Relative_folders_below_a_destination_refuse_escapes()
    {
        var root = _dir.Path;
        Assert.Equal(Path.Combine(root, "a", "b"), RelativeFolders.Resolve(root, "a/b"));
        Assert.Throws<ArgumentException>(() => RelativeFolders.Resolve(root, "../x"));
        Assert.Throws<ArgumentException>(() => RelativeFolders.Resolve(root, @"a\..\..\x"));
        Assert.Throws<ArgumentException>(() => RelativeFolders.Resolve(root, "a/c:evil"));
    }
}
