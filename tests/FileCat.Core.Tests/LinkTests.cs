using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class LinkTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly PortableFileOperations _fs = new();
    private readonly JobManager _jobs;

    public LinkTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _jobs = new JobManager(_fs, _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private static VolumeInfo Volume(string key, string fs = "NTFS", bool hard = true, bool symbolic = true, bool remote = false) =>
        new(key, fs, true, false, TimeSpan.FromTicks(1), hard, symbolic, remote, false, true);

    private static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

    private async Task<Job> RunAsync(JobRequest request)
    {
        var job = _jobs.Submit(request);
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        return job;
    }

    [Fact]
    public void Planner_explains_what_each_kind_of_link_cannot_do_here()
    {
        var ntfs = Volume("C:");
        var fat = Volume("E:", "exFAT", hard: false, symbolic: false);
        var share = Volume(@"\\server\share", remote: true);
        Assert.Contains("files", LinkPlanner.Check(LinkKind.Hard, true, ntfs, ntfs));
        Assert.Contains("same drive", LinkPlanner.Check(LinkKind.Hard, false, ntfs, Volume("D:")));
        Assert.Contains("exFAT", LinkPlanner.Check(LinkKind.Hard, false, fat, fat));
        Assert.Null(LinkPlanner.Check(LinkKind.Hard, false, ntfs, ntfs));
        Assert.Contains("symbolic links", LinkPlanner.Check(LinkKind.Symbolic, false, fat, ntfs));
        Assert.Null(LinkPlanner.Check(LinkKind.Symbolic, true, ntfs, share));
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("folders", LinkPlanner.Check(LinkKind.Junction, false, ntfs, ntfs));
            Assert.Contains("network", LinkPlanner.Check(LinkKind.Junction, true, ntfs, share));
            Assert.Null(LinkPlanner.Check(LinkKind.Junction, true, ntfs, Volume("D:")));
        }
        else Assert.Contains("only on Windows", LinkPlanner.Check(LinkKind.Junction, true, ntfs, ntfs));
    }

    [Fact]
    public void Preview_blocks_loops_collisions_and_taken_names_and_computes_relative_targets()
    {
        string folder = _dir.Dir("data");
        string file = _dir.File("data/report.txt");
        string links = _dir.Dir("links");
        _dir.File("links/taken.txt");
        var items = new[] { ItemRef.ForFileSystemPath(folder, EntryKind.Directory), ItemRef.ForFileSystemPath(file, EntryKind.File) };
        VolumeInfo Same(string _) => Volume("same");

        var relative = LinkPlanner.Preview(items, links, null, new LinkOptions(LinkKind.Symbolic, RelativeTarget: true), Same, Exists);
        Assert.All(relative, r => Assert.Null(r.Problem));
        Assert.Equal(Path.Combine("..", "data"), relative[0].TargetText);
        Assert.Equal(Path.Combine("..", "data", "report.txt"), relative[1].TargetText);
        Assert.Equal(folder, LinkPlanner.Preview(items, links, null, new LinkOptions(LinkKind.Symbolic), Same, Exists)[0].TargetText);

        Assert.Contains("loop", LinkPlanner.Preview(items[..1], folder, ["inside"], new LinkOptions(LinkKind.Symbolic), Same, Exists)[0].Problem);
        Assert.Contains("exists", LinkPlanner.Preview(items[1..], links, ["taken.txt"], new LinkOptions(LinkKind.Symbolic), Same, Exists)[0].Problem);
        Assert.Contains("same name", LinkPlanner.Preview(items, links, ["one", "one"], new LinkOptions(LinkKind.Symbolic), Same, Exists)[1].Problem);
        Assert.Contains("itself", LinkPlanner.Preview(items[1..], folder, null, new LinkOptions(LinkKind.Symbolic), Same, Exists)[0].Problem);
        Assert.NotNull(LinkPlanner.Preview(items[1..], links, ["a|b"], new LinkOptions(LinkKind.Symbolic), Same, Exists)[0].Problem);
    }

    [Fact]
    public async Task Symbolic_links_are_created_and_undo_removes_only_unchanged_links()
    {
        if (_fs.CanCreateSymbolicLinks != true) Assert.Skip("This account cannot create symbolic links (no Developer Mode or privilege).");
        string target = _dir.File("data/a.txt", "alpha");
        string folder = _dir.Dir("data/sub");
        _dir.File("data/sub/inner.txt", "kept");
        string links = _dir.Dir("links");
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.CreateLink,
            Sources = [ItemRef.ForFileSystemPath(target, EntryKind.File), ItemRef.ForFileSystemPath(folder, EntryKind.Directory)],
            Destination = Location.FileSystem(links),
            Link = new LinkOptions(LinkKind.Symbolic, RelativeTarget: true),
        });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(links, "a.txt")));
        Assert.Equal(Path.Combine("..", "data", "a.txt"), new FileInfo(Path.Combine(links, "a.txt")).LinkTarget);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(links, "sub", "inner.txt")));

        // One link is pointed elsewhere after it was made: undo keeps it and removes the other one, never a target.
        File.Delete(Path.Combine(links, "a.txt"));
        File.CreateSymbolicLink(Path.Combine(links, "a.txt"), "elsewhere.txt");
        var report = UndoService.Undo(job, _fs);
        Assert.Contains(report, l => l.StartsWith("Kept", StringComparison.Ordinal));
        Assert.Contains(report, l => l.StartsWith("Removed the created link", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(links, "sub")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(folder, "inner.txt")));
        Assert.Equal("alpha", File.ReadAllText(target));
    }

    [Fact]
    public async Task Hard_links_share_the_data_and_existing_names_are_never_replaced()
    {
        string original = _dir.File("data/h.txt", "one");
        string links = _dir.Dir("links");
        JobRequest Request() => new()
        {
            Kind = JobKind.CreateLink,
            Sources = [ItemRef.ForFileSystemPath(original, EntryKind.File)],
            Destination = Location.FileSystem(links),
            NewNames = ["h2.txt"],
            Link = new LinkOptions(LinkKind.Hard),
        };
        var job = await RunAsync(Request());
        Assert.Equal(JobState.Completed, job.State);
        File.WriteAllText(Path.Combine(links, "h2.txt"), "two");
        Assert.Equal("two", File.ReadAllText(original));

        var again = await RunAsync(Request());
        Assert.Equal(JobState.Failed, again.State);
        Assert.Contains(again.Issues, i => i.Message.Contains("exists", StringComparison.Ordinal));
        Assert.Equal("two", File.ReadAllText(Path.Combine(links, "h2.txt")));
    }
}
