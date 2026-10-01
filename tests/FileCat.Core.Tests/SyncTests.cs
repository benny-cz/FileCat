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

    private async Task Run(ProviderRegistry providers, List<JobRequest> requests)
    {
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
        var jobs = requests.Select(manager.Submit).ToList();
        foreach (var job in jobs)
        {
            while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(job.State == JobState.Completed, $"{job.Title}: {job.State} {string.Join("; ", job.Issues.Select(i => i.Message))}");
        }
    }

    [Fact]
    public async Task Mirror_makes_the_target_like_the_source_but_keeps_newer_target_files_unless_chosen()
    {
        var (left, right) = Trees();
        var providers = Providers();
        var plan = SyncPlanner.Propose(Compare(providers, left, right), sourceIsLeft: true, SyncMode.Mirror, targetIgnoresCase: OperatingSystem.IsWindows());
        var by = ByPath(plan);
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
        await Run(providers, SyncPlanner.BuildRequests(plan, sourceIsLeft: true, Location.FileSystem(right), providers, deletePermanently: true));

        Assert.Equal("a", File.ReadAllText(Path.Combine(right, "a.txt")));
        Assert.Equal("b-new", File.ReadAllText(Path.Combine(right, "b.txt")));
        Assert.Equal("left d", File.ReadAllText(Path.Combine(right, "d.txt")));
        Assert.Equal("g-new", File.ReadAllText(Path.Combine(right, "g.txt")));
        Assert.Equal("e", File.ReadAllText(Path.Combine(right, "sub", "e.txt")));
        Assert.Equal("f", File.ReadAllText(Path.Combine(right, "newdir", "f.txt")));
        Assert.True(File.Exists(Path.Combine(right, "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(right, "olddir")));
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
        Assert.Equal(2, job.Issues.Count(i => i.Message.StartsWith("Not removed: it changed after the comparison", StringComparison.Ordinal)));
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
        Assert.True(by["Readme.md"].Include && by["README.md"].Include && by["Notes.txt"].Include);
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
