using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class EditSessionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly MarkingOps _ops = new();
    private readonly JobManager _jobs;
    private readonly ZipProvider _zip;

    public EditSessionTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _zip = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        _providers.Register(_zip);
        _jobs = new JobManager(_ops, _providers, Path.Combine(_dir.Path, "journal"));
    }

    public void Dispose() => _dir.Dispose();

    private sealed class MarkingOps : PortableFileOperations
    {
        public readonly Dictionary<string, string> Marks = new(StringComparer.OrdinalIgnoreCase);
        public override string? ReadOriginMark(string path) => Marks.GetValueOrDefault(path);
        public override bool WriteOriginMark(string path, string mark)
        {
            Marks[path] = mark;
            return true;
        }
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }
        public void ReportIssue(string message) { }
    }

    private string MakeZip()
    {
        string zip = Path.Combine(_dir.Path, "docs.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        using (var w = new StreamWriter(archive.CreateEntry("notes/todo.txt").Open())) w.Write("buy milk");
        using (var w = new StreamWriter(archive.CreateEntry("other.txt").Open())) w.Write("other");
        return zip;
    }

    private ItemRef Member(string zip, string folder, string name)
    {
        var location = _zip.GetContainerLocation(zip)!.WithPath(folder);
        var rows = new List<EntryData>();
        _zip.EnumerateAsync(location, new Sink(rows), CancellationToken.None).GetAwaiter().GetResult();
        return _zip.GetItemRef(location, rows.Single(r => r.Name == name));
    }

    private async Task<Job> RunAsync(ArchivePlan plan)
    {
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        return job;
    }

    private static string ReadMember(string zip, string member)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry(member)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task An_edit_is_private_until_committed_and_stays_open_for_more_changes()
    {
        string zip = MakeZip();
        _ops.Marks[zip] = "[ZoneTransfer]\nZoneId=3";
        var store = new EditSessionStore(Path.Combine(_dir.Path, "sessions"), _ops);
        var session = store.Create(_zip, Member(zip, "notes", "todo.txt"));
        Assert.Equal("buy milk", File.ReadAllText(session.WorkingPath));
        Assert.Equal("todo.txt", Path.GetFileName(session.WorkingPath));
        Assert.True(_ops.Marks.ContainsKey(session.WorkingPath)); // the download mark travels with the copy
        Assert.Equal(EditState.Unchanged, store.StateOf(session));
        Assert.Equal(session.Id, store.Find(zip, "notes/todo.txt")!.Id);

        File.WriteAllText(session.WorkingPath, "buy oat milk");
        Assert.Equal(EditState.Modified, store.StateOf(session));
        Assert.Equal("buy milk", ReadMember(zip, "notes/todo.txt")); // nothing written back yet
        Assert.Equal(CommitCheck.Ready, store.Check(session));
        string sha = EditSessionStore.Hash(session.WorkingPath);
        var commit = await RunAsync(store.CommitPlan(session, rebase: false));
        Assert.Equal(JobState.Completed, commit.State);
        Assert.Equal("buy oat milk", ReadMember(zip, "notes/todo.txt"));
        session = store.Committed(session, sha);
        Assert.Equal(EditState.Unchanged, store.StateOf(session));
        Assert.Equal(CommitCheck.Ready, store.Check(session));

        // A restart finds the session; Discard removes the working copy and the record.
        var restored = Assert.Single(new EditSessionStore(store.Root, _ops).LoadAll());
        Assert.Equal(session with { }, restored);
        store.Discard(restored);
        Assert.Empty(store.LoadAll());
        Assert.False(File.Exists(session.WorkingPath));
    }

    [Fact]
    public async Task Conflicts_are_classified_so_nothing_is_overwritten_silently()
    {
        string zip = MakeZip();
        var store = new EditSessionStore(Path.Combine(_dir.Path, "sessions"), _ops);
        var session = store.Create(_zip, Member(zip, "notes", "todo.txt"));
        File.WriteAllText(session.WorkingPath, "mine");

        // Another change to the archive that leaves this member alone: an explicit rebase is safe.
        string other = _dir.File("new.txt", "unrelated");
        Assert.Equal(JobState.Completed, (await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.AddFile, "new.txt", other)]))).State);
        Assert.Equal(CommitCheck.ArchiveChanged, store.Check(session));
        var stale = await RunAsync(store.CommitPlan(session, rebase: false));
        Assert.Equal(JobState.Failed, stale.State); // the old baseline is refused
        Assert.Equal(JobState.Completed, (await RunAsync(store.CommitPlan(session, rebase: true))).State);
        Assert.Equal("mine", ReadMember(zip, "notes/todo.txt"));
        Assert.Equal("unrelated", ReadMember(zip, "new.txt"));

        // Someone else changes the member itself.
        var second = store.Create(_zip, Member(zip, "", "other.txt"));
        string theirs = _dir.File("theirs.txt", "their change");
        Assert.Equal(JobState.Completed, (await RunAsync(new ArchivePlan(zip, ArchiveBaseline.Of(zip), [new ArchiveChange(ArchiveChangeKind.Replace, "other.txt", theirs)]))).State);
        Assert.Equal(CommitCheck.MemberChanged, store.Check(second));

        File.Delete(zip);
        Assert.Equal(CommitCheck.ArchiveMissing, store.Check(second));
    }
}
