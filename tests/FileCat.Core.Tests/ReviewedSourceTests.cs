using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ReviewedSourceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("removed", JobKind.Copy)]
    [InlineData("size", JobKind.Copy)]
    [InlineData("time", JobKind.Copy)]
    [InlineData("directory", JobKind.Copy)]
    [InlineData("unreviewed", JobKind.Copy)]
    [InlineData("io", JobKind.Copy)]
    [InlineData("unchanged", JobKind.Copy)]
    [InlineData("removed", JobKind.Move)]
    [InlineData("size", JobKind.Move)]
    [InlineData("time", JobKind.Move)]
    [InlineData("directory", JobKind.Move)]
    [InlineData("unreviewed", JobKind.Move)]
    [InlineData("io", JobKind.Move)]
    [InlineData("unchanged", JobKind.Move)]
    public async Task A_queued_reviewed_transfer_refuses_changed_roots_before_creating_its_destination(string change, JobKind kind)
    {
        using var f = new Fixture(); var item = ItemRef.ForFileSystemPath(f.Source, EntryKind.File);
        var review = Assert.IsType<SourcePathReview>(SourcePathReview.Capture(f.Source, f.Files));
        var job = f.Jobs.Submit(new JobRequest { Kind = kind, Sources = [item], Destination = Location.FileSystem(f.Destination), ExpectedSources = change == "unreviewed" ? new Dictionary<ItemRef, SourcePathReview>() : new Dictionary<ItemRef, SourcePathReview> { [item] = review } });
        Assert.Equal(JobState.Queued, job.State);
        switch (change)
        {
            case "removed": File.Delete(f.Source); break;
            case "size": File.AppendAllText(f.Source, "newer"); break;
            case "time": File.SetLastWriteTimeUtc(f.Source, File.GetLastWriteTimeUtc(f.Source).AddMinutes(2)); break;
            case "directory": File.Delete(f.Source); Directory.CreateDirectory(f.Source); break;
            case "io": f.Files.FailInfo = true; break;
        }
        f.Jobs.MaxConcurrent = 4; f.Jobs.Schedule(); await Wait(job);
        Emit("queued-root", new { change, kind, job.State, f.Files.Mutations, DestinationExists = Directory.Exists(f.Destination), Issues = job.Issues.Select(i => new { i.Outcome, i.Message }).ToArray() });
        if (change == "unchanged") { Assert.Equal(JobState.Completed, job.State); Assert.Equal("owned source bytes", File.ReadAllText(Path.Join(f.Destination, "source.txt"))); }
        else { Assert.Equal(0, f.Files.Mutations); Assert.False(Directory.Exists(f.Destination)); Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange); }
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task A_later_root_changed_while_the_first_root_transfers_is_refused(JobKind kind)
    {
        using var f = new Fixture(); var secondPath = Path.Join(f.Root, "second.txt"); File.WriteAllText(secondPath, "second source");
        var first = ItemRef.ForFileSystemPath(f.Source, EntryKind.File); var second = ItemRef.ForFileSystemPath(secondPath, EntryKind.File);
        var expected = new Dictionary<ItemRef, SourcePathReview> { [first] = SourcePathReview.Capture(f.Source, f.Files)!, [second] = SourcePathReview.Capture(secondPath, f.Files)! };
        f.Files.AfterPublish = () => File.AppendAllText(secondPath, " changed");
        var job = f.Jobs.Submit(new JobRequest { Kind = kind, Sources = [first, second], ExpectedSources = expected, Destination = Location.FileSystem(f.Destination) }); f.Jobs.MaxConcurrent = 4; f.Jobs.Schedule(); await Wait(job);
        Emit("between-roots", new { kind, job.State, FirstBytes = File.ReadAllText(Path.Join(f.Destination, "source.txt")), SecondExists = File.Exists(Path.Join(f.Destination, "second.txt")), SourceBytes = File.ReadAllText(secondPath), Issues = job.Issues.Select(i => i.Outcome).ToArray() });
        Assert.Equal("owned source bytes", File.ReadAllText(Path.Join(f.Destination, "source.txt"))); Assert.False(File.Exists(Path.Join(f.Destination, "second.txt"))); Assert.Equal("second source changed", File.ReadAllText(secondPath)); Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange);
    }

    private void Emit(string control, object detail) => output.WriteLine("REVIEWED_SOURCE " + JsonSerializer.Serialize(new { control, detail, OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false, AtomicOrRecursiveIdentityQualified = false }));
    private static async Task Wait(Job job) { var clock = Stopwatch.StartNew(); while (!job.State.IsFinished()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned reviewed-source job did not finish."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), "filecat-reviewed-source", Guid.NewGuid().ToString("N"))).FullName;
        public readonly Files Files = new(); public readonly JobManager Jobs; public string Source => Path.Join(Root, "source.txt"); public string Destination => Path.Join(Root, "not-created");
        public Fixture() { File.WriteAllText(Source, "owned source bytes"); var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); Jobs = new(Files, providers, Path.Join(Root, "journals")) { MaxConcurrent = 0 }; }
        public void Dispose() { foreach (var job in Jobs.Jobs) job.Cancel(); SpinWait.SpinUntil(() => !Jobs.HasActiveWork, TimeSpan.FromSeconds(3)); Directory.Delete(Root, true); }
    }
    private sealed class Files : PortableFileOperations
    {
        public bool FailInfo; public int Mutations; public Action? AfterPublish;
        public override FileSystemItemInfo? TryGetInfo(string path) { if (FailInfo) throw new IOException("Owned source info failed."); return base.TryGetInfo(path); }
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct) { Interlocked.Increment(ref Mutations); base.CopyFile(source, destination, options, progress, ct); var action = Interlocked.Exchange(ref AfterPublish, null); action?.Invoke(); }
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false) { Interlocked.Increment(ref Mutations); base.Move(source, destination, replaceExisting, writeThrough); var action = Interlocked.Exchange(ref AfterPublish, null); action?.Invoke(); }
        public override void DeleteFile(string path) { Interlocked.Increment(ref Mutations); base.DeleteFile(path); }
    }
}
