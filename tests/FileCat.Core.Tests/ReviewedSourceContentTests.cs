using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ReviewedSourceContentTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("same-bytes", JobKind.Copy)]
    [InlineData("tree-bytes", JobKind.Copy)]
    [InlineData("tree-added", JobKind.Copy)]
    [InlineData("tree-removed", JobKind.Copy)]
    [InlineData("unchanged", JobKind.Copy)]
    [InlineData("tree-unchanged", JobKind.Copy)]
    [InlineData("same-bytes", JobKind.Move)]
    [InlineData("tree-bytes", JobKind.Move)]
    [InlineData("tree-added", JobKind.Move)]
    [InlineData("tree-removed", JobKind.Move)]
    [InlineData("unchanged", JobKind.Move)]
    [InlineData("tree-unchanged", JobKind.Move)]
    public async Task A_queued_tree_review_refuses_changed_content_before_destination_creation(string change, JobKind kind)
    {
        using var f = new Fixture(); bool directory = change.StartsWith("tree-", StringComparison.Ordinal);
        if (directory) { File.Delete(f.Source); Directory.CreateDirectory(f.Source); File.WriteAllText(Path.Join(f.Source, "child"), "owned source bytes"); }
        var item = ItemRef.ForFileSystemPath(f.Source, directory ? EntryKind.Directory : EntryKind.File);
        var review = Assert.IsType<FileTreeReview>(FileTreeReview.Capture(f.Source, f.Files, f.Provider));
        var job = f.Jobs.Submit(new JobRequest { Kind = kind, Sources = [item], Destination = Location.FileSystem(f.Destination), ExpectedSourceTrees = new Dictionary<ItemRef, ReviewedSourceTree> { [item] = new(review, f.Provider) } });
        Assert.Equal(JobState.Queued, job.State); Change(f.Source, change);
        f.Jobs.MaxConcurrent = 4; f.Jobs.Schedule(); await Wait(job);
        bool unchanged = change is "unchanged" or "tree-unchanged";
        var target = Path.Join(f.Destination, "source.txt"); var content = directory ? Path.Join(target, "child") : target;
        Emit("queued-source-tree", new { change, kind, job.State, f.Files.Mutations, DestinationExists = Directory.Exists(f.Destination), TargetBytes = File.Exists(content) ? File.ReadAllText(content) : null, Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray() });
        if (unchanged) { Assert.Equal(JobState.Completed, job.State); Assert.Equal("owned source bytes", File.ReadAllText(content)); }
        else { Assert.Equal(0, f.Files.Mutations); Assert.False(Directory.Exists(f.Destination)); Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange); }
    }

    [Theory]
    [InlineData("same-bytes", JobKind.Copy)]
    [InlineData("tree-added", JobKind.Copy)]
    [InlineData("same-bytes", JobKind.Move)]
    [InlineData("tree-added", JobKind.Move)]
    public async Task A_later_root_content_change_is_refused_after_an_earlier_root_finishes(string change, JobKind kind)
    {
        using var f = new Fixture(); var secondPath = Path.Join(f.Root, "second");
        bool directory = change == "tree-added";
        if (directory) { Directory.CreateDirectory(secondPath); File.WriteAllText(Path.Join(secondPath, "child"), "owned source bytes"); }
        else File.WriteAllText(secondPath, "owned source bytes");
        var first = ItemRef.ForFileSystemPath(f.Source, EntryKind.File); var second = ItemRef.ForFileSystemPath(secondPath, directory ? EntryKind.Directory : EntryKind.File);
        var expected = new Dictionary<ItemRef, ReviewedSourceTree> { [first] = new(FileTreeReview.Capture(f.Source, f.Files, f.Provider)!, f.Provider), [second] = new(FileTreeReview.Capture(secondPath, f.Files, f.Provider)!, f.Provider) };
        f.Files.AfterPublish = () => Change(secondPath, change);
        var job = f.Jobs.Submit(new JobRequest { Kind = kind, Sources = [first, second], ExpectedSourceTrees = expected, Destination = Location.FileSystem(f.Destination) });
        f.Jobs.MaxConcurrent = 4; f.Jobs.Schedule(); await Wait(job);
        var secondTarget = Path.Join(f.Destination, "second");
        Emit("between-source-trees", new { change, kind, job.State, FirstBytes = File.ReadAllText(Path.Join(f.Destination, "source.txt")), SecondTargetExists = File.Exists(secondTarget) || Directory.Exists(secondTarget), SecondSourceExists = File.Exists(secondPath) || Directory.Exists(secondPath), Issues = job.Issues.Select(i => i.Outcome).ToArray() });
        Assert.Equal("owned source bytes", File.ReadAllText(Path.Join(f.Destination, "source.txt"))); Assert.False(File.Exists(secondTarget) || Directory.Exists(secondTarget)); Assert.True(File.Exists(secondPath) || Directory.Exists(secondPath)); Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Link_review_is_explicit_and_never_reads_a_target(bool allowLinks)
    {
        using var f = new Fixture(); var files = new LiteralLink(); var provider = new NoContent();
        var review = FileTreeReview.Capture(f.Source, files, provider, allowLinks: allowLinks);
        bool matched = review?.Matches(files, provider) ?? false; files.Changed = true;
        bool after = review?.Matches(files, provider) ?? false;
        Emit("literal-link-review", new { allowLinks, Reviewed = review is not null, matched, after, ContentItems = review?.Items.Count(i => i.Content is not null), SyntheticLink = true });
        Assert.Equal(allowLinks, review is not null); Assert.Equal(allowLinks, matched); Assert.False(after); if (review is not null) Assert.Null(Assert.Single(review.Items).Content);
    }

    private static void Change(string path, string change)
    {
        if (change is "same-bytes" or "tree-bytes")
        {
            var file = change == "tree-bytes" ? Path.Join(path, "child") : path; var time = File.GetLastWriteTimeUtc(file);
            File.WriteAllText(file, "other source bytes"); File.SetLastWriteTimeUtc(file, time);
        }
        else if (change is "tree-added" or "tree-removed")
        {
            var time = Directory.GetLastWriteTimeUtc(path);
            if (change == "tree-added") File.WriteAllText(Path.Join(path, "new-child"), "new child"); else File.Delete(Path.Join(path, "child"));
            Directory.SetLastWriteTimeUtc(path, time);
        }
    }
    private void Emit(string control, object detail) => output.WriteLine("REVIEWED_SOURCE_CONTENT " + JsonSerializer.Serialize(new { control, detail, OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false, AtomicOrNativeRecursiveIdentityQualified = false }));
    private static async Task Wait(Job job) { var clock = Stopwatch.StartNew(); while (!job.State.IsFinished()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned source-content job did not finish."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-source-content", Guid.NewGuid().ToString("N"))).FullName;
        public readonly Files Files = new(); public readonly LocalFileSystemProvider Provider = new(); public readonly JobManager Jobs;
        public string Source => Path.Join(Root, "source.txt"); public string Destination => Path.Join(Root, "not-created");
        public Fixture() { File.WriteAllText(Source, "owned source bytes"); var providers = new ProviderRegistry(); providers.Register(Provider); Jobs = new(Files, providers, Path.Join(Root, "journals")) { MaxConcurrent = 0 }; }
        public void Dispose() { foreach (var job in Jobs.Jobs) job.Cancel(); SpinWait.SpinUntil(() => !Jobs.HasActiveWork, TimeSpan.FromSeconds(3)); Directory.Delete(Root, true); }
    }
    private sealed class Files : PortableFileOperations
    {
        public int Mutations; public Action? AfterPublish;
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct) { Interlocked.Increment(ref Mutations); base.CopyFile(source, destination, options, progress, ct); var action = Interlocked.Exchange(ref AfterPublish, null); action?.Invoke(); }
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false) { Interlocked.Increment(ref Mutations); base.Move(source, destination, replaceExisting, writeThrough); var action = Interlocked.Exchange(ref AfterPublish, null); action?.Invoke(); }
        public override void DeleteFile(string path) { Interlocked.Increment(ref Mutations); base.DeleteFile(path); }
    }
    private sealed class LiteralLink : PortableFileOperations
    {
        public bool Changed;
        public override FileSystemItemInfo? TryGetInfo(string path) { var info = base.TryGetInfo(path); return info is null ? null : info with { IsLink = true, LinkTarget = Changed ? "other-target" : "reviewed-target" }; }
    }
    private sealed class NoContent : ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => default;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource? OpenContent(ItemRef item) => throw new InvalidOperationException("A literal-link review must not read target content");
    }
}
