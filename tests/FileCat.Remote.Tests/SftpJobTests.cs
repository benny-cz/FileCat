using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class SftpJobTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-remote-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSftpServer _server = new();
    private readonly ScriptedInteraction _ui = new() { HostKeyAnswer = HostKeyDecision.AcceptOnce };
    private readonly RemoteProfile _profile = new() { Name = "Test", Host = "files.example", User = "user" };
    private readonly SftpConnections _connections;
    private readonly SftpProvider _sftp;
    private readonly MarkingOps _ops = new();
    private readonly JobManager _jobs;
    private readonly string _local;

    /// <summary>Records download marks (the portable file system has no Mark-of-the-Web).</summary>
    private sealed class MarkingOps : PortableFileOperations
    {
        public Dictionary<string, string> Marks { get; } = [];
        public override string? ReadOriginMark(string path) => Marks.GetValueOrDefault(path);
        public override bool WriteOriginMark(string path, string mark)
        {
            Marks[path] = mark;
            return true;
        }

        // Like an alternate data stream, the mark moves with the file.
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            base.Move(source, destination, replaceExisting, writeThrough);
            if (Marks.Remove(source, out var m)) Marks[destination] = m;
        }
    }

    public SftpJobTests()
    {
        _local = Directory.CreateDirectory(Path.Combine(_dir, "local")).FullName;
        _connections = new SftpConnections(id => id == _profile.Id ? _profile : null, new FakeConnector(_server),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts"), []), new SessionSecretStore(), _ui);
        for (int i = 0; i < 20; i++) _ui.Secrets.Enqueue("secret");
        _sftp = new SftpProvider(_connections, () => [_profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(_sftp);
        SftpJobs.Register();
        _jobs = new JobManager(_ops, providers, Path.Combine(_dir, "journal"));
    }

    public void Dispose()
    {
        _connections.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private Location Remote(string path) => SftpProvider.At(_profile, path);

    private ItemRef RemoteItem(string folder, string name, EntryKind kind = EntryKind.File) => new(Remote(folder), name, kind);

    private string LocalFile(string relative, string content)
    {
        string path = Path.Combine(_local, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<Job> RunAsync(JobRequest request, DecisionAction answer = DecisionAction.Skip)
    {
        var job = _jobs.Submit(request);
        while (!job.State.IsFinished())
        {
            job.Decision?.Resolve(new Decision(answer));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private IEnumerable<string> Names(string folder) => _server.Lookup(folder, true)!.Children.Keys;

    /// <summary>"Resume where safe" (P6): a dropped upload continues where the server's copy ends, once it is checked.</summary>
    [Fact]
    public async Task A_dropped_upload_continues_after_its_partial_copy_is_checked()
    {
        var data = new byte[5 * 1024 * 1024 + 99];
        new Random(8).NextBytes(data);
        string big = Path.Combine(_local, "big.bin");
        File.WriteAllBytes(big, data);
        _server.Dir("/up");
        _server.DropAfterBytes = 2 * 1024 * 1024 + 17;
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(big, EntryKind.File)], Destination = Remote("/up") });
        int asked = 0;
        while (!job.State.IsFinished())
        {
            if (job.Decision is { Task.IsCompleted: false } d)
            {
                asked++;
                _server.Down = false; // the connection is back
                d.Resolve(new Decision(DecisionAction.Retry));
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(1, asked);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(data, _server.Lookup("/up/big.bin", true)!.Data);
        Assert.Equal([2L * 1024 * 1024 + 17], _server.WritesAt);
        Assert.Contains(job.Issues, i => i.Message.Contains("continued at", StringComparison.Ordinal));
        Assert.Equal(["big.bin"], Names("/up")); // no temporary file left
        Assert.Equal(data.Length, job.BytesDone);
    }

    [Fact]
    public async Task A_dropped_upload_whose_source_changed_starts_again()
    {
        string big = Path.Combine(_local, "big.bin");
        File.WriteAllBytes(big, new byte[3 * 1024 * 1024]);
        _server.Dir("/up");
        _server.DropAfterBytes = 1024 * 1024;
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(big, EntryKind.File)], Destination = Remote("/up") });
        var changed = Enumerable.Repeat((byte)7, 3 * 1024 * 1024).ToArray();
        while (!job.State.IsFinished())
        {
            if (job.Decision is { Task.IsCompleted: false } d)
            {
                File.WriteAllBytes(big, changed); // edited while the connection was down
                File.SetLastWriteTimeUtc(big, DateTime.UtcNow.AddMinutes(1));
                _server.Down = false;
                d.Resolve(new Decision(DecisionAction.Retry));
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(changed, _server.Lookup("/up/big.bin", true)!.Data);
        Assert.Empty(_server.WritesAt); // nothing was continued: the whole file went again
        Assert.Equal(["big.bin"], Names("/up"));
    }

    [Fact]
    public async Task Uploads_publish_files_and_folders_through_temporary_names()
    {
        string a = LocalFile("a.txt", "alpha");
        LocalFile("tree/b.txt", "beta");
        LocalFile("tree/deep/c.txt", "gamma");
        File.SetLastWriteTimeUtc(a, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        _server.Dir("/up");
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(a, EntryKind.File), ItemRef.ForFileSystemPath(Path.Combine(_local, "tree"), EntryKind.Directory)],
            Destination = Remote("/up"),
        });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("alpha", _server.Read("/up/a.txt"));
        Assert.Equal("gamma", _server.Read("/up/tree/deep/c.txt"));
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), _server.Lookup("/up/a.txt", true)!.Modified);
        Assert.Equal(["a.txt", "tree"], Names("/up"));
        Assert.Equal("alpha".Length + "beta".Length + "gamma".Length, job.BytesDone);
        Assert.True(File.Exists(a));
    }

    [Fact]
    public async Task Replacing_is_atomic_where_the_server_can_and_explained_where_it_cannot()
    {
        string a = LocalFile("a.txt", "new");
        _server.File("/up/a.txt", "old");
        var request = new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(a, EntryKind.File)], Destination = Remote("/up"),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
        };
        Assert.Equal(JobState.Completed, (await RunAsync(request)).State);
        Assert.Equal("new", _server.Read("/up/a.txt"));

        _server.PosixRename = false;
        File.WriteAllText(a, "newer");
        var job = await RunAsync(request);
        Assert.Equal("newer", _server.Read("/up/a.txt"));
        Assert.Contains(job.Issues, i => i.Message.Contains("cannot replace a file in one step", StringComparison.Ordinal));
        Assert.Equal(["a.txt"], Names("/up"));

        var skip = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(a, EntryKind.File)], Destination = Remote("/up"),
            Options = new TransferOptions { Conflicts = ConflictPolicy.KeepBothRenameIncoming },
        });
        Assert.Equal(JobState.Completed, skip.State);
        Assert.Equal(["a (2).txt", "a.txt"], Names("/up"));
    }

    [Fact]
    public async Task Replacing_a_link_replaces_the_link_itself_never_its_target()
    {
        _server.File("/data/target.txt", "keep me");
        _server.Link("/up/a.txt", "/data/target.txt");
        string a = LocalFile("a.txt", "uploaded");
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(a, EntryKind.File)], Destination = Remote("/up"),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
        });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("keep me", _server.Read("/data/target.txt"));
        Assert.Null(_server.Lookup("/up/a.txt", false)!.LinkTarget);
        Assert.Equal("uploaded", _server.Read("/up/a.txt"));
    }

    [Fact]
    public async Task A_move_deletes_the_source_only_after_its_copy_is_published()
    {
        string a = LocalFile("m/a.txt", "moved");
        _server.Dir("/up");
        var job = await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(Path.Combine(_local, "m"), EntryKind.Directory)], Destination = Remote("/up") });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("moved", _server.Read("/up/m/a.txt"));
        Assert.False(Directory.Exists(Path.Combine(_local, "m")));

        // The server goes away: nothing is published, so the source stays.
        string b = LocalFile("b.txt", "stays");
        using (_connections.Lease(_profile.Id, TestContext.Current.CancellationToken)) { }
        _server.Down = true;
        var failed = await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(b, EntryKind.File)], Destination = Remote("/up") });
        Assert.NotEqual(JobState.Completed, failed.State);
        Assert.True(File.Exists(b));
        _server.Down = false;
        Assert.DoesNotContain(Names("/up"), n => n.StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Same_server_moves_rename_and_never_overwrite()
    {
        _server.File("/src/a.txt", "a");
        _server.File("/src/sub/b.txt", "b");
        _server.File("/dst/taken.txt", "taken");
        _server.File("/src/taken.txt", "mine");
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [RemoteItem("/src", "a.txt"), RemoteItem("/src", "sub", EntryKind.Directory), RemoteItem("/src", "taken.txt")],
            Destination = Remote("/dst"),
        });
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal("a", _server.Read("/dst/a.txt"));
        Assert.Equal("b", _server.Read("/dst/sub/b.txt"));
        Assert.Equal("taken", _server.Read("/dst/taken.txt"));
        Assert.Equal("mine", _server.Read("/src/taken.txt"));
        Assert.Equal(0, _server.Connects - 1);

        var into = await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [RemoteItem("/dst", "sub", EntryKind.Directory)], Destination = Remote("/dst/sub") });
        Assert.Contains(into.Issues, i => i.Message.Contains("into itself", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_empties_folders_bottom_up_and_removes_links_themselves()
    {
        _server.File("/keep/target.txt", "keep");
        _server.File("/gone/one.txt", "1");
        _server.File("/gone/deep/two.txt", "2");
        _server.Link("/gone/link-to-keep", "/keep");
        _server.File("/lone.txt", "x");
        var job = await RunAsync(new JobRequest { Kind = JobKind.Delete, Sources = [RemoteItem("/", "gone", EntryKind.Directory), RemoteItem("/", "lone.txt"), RemoteItem("/", "missing.txt")] });
        Assert.Equal(JobState.Completed, job.State);
        Assert.False(_server.Exists("/gone"));
        Assert.False(_server.Exists("/lone.txt"));
        Assert.Equal("keep", _server.Read("/keep/target.txt"));
        Assert.Contains(job.Issues, i => i.Message.Contains("already gone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rename_and_new_folder_work_and_refuse_taken_names()
    {
        _server.File("/r/a.txt", "a");
        _server.File("/r/b.txt", "b");
        Assert.Equal(JobState.Completed, (await RunAsync(new JobRequest { Kind = JobKind.Rename, Sources = [RemoteItem("/r", "a.txt")], NewName = "c.txt" })).State);
        Assert.Equal("a", _server.Read("/r/c.txt"));
        var taken = await RunAsync(new JobRequest { Kind = JobKind.Rename, Sources = [RemoteItem("/r", "c.txt")], NewName = "b.txt" });
        Assert.Equal(JobState.Failed, taken.State);
        Assert.Equal("b", _server.Read("/r/b.txt"));
        Assert.Equal(JobState.Completed, (await RunAsync(new JobRequest { Kind = JobKind.CreateDirectory, Destination = Remote("/r"), NewName = "made" })).State);
        Assert.True(_server.Lookup("/r/made", false)!.IsDirectory);
        Assert.Equal(JobState.Failed, (await RunAsync(new JobRequest { Kind = JobKind.CreateDirectory, Destination = Remote("/r"), NewName = "made" })).State);
    }

    [Fact]
    public async Task Moving_to_a_local_folder_deletes_on_the_server_only_what_arrived_completely()
    {
        _server.File("/m/whole/a.txt", "a");
        _server.File("/m/whole/deep/b.txt", "b");
        _server.File("/m/single.txt", "s");
        _server.File("/m/blocked.txt", "remote");
        string dest = Directory.CreateDirectory(Path.Combine(_dir, "moved")).FullName;
        File.WriteAllText(Path.Combine(dest, "blocked.txt"), "local");
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [RemoteItem("/m", "whole", EntryKind.Directory), RemoteItem("/m", "single.txt"), RemoteItem("/m", "blocked.txt")],
            Destination = Location.FileSystem(dest),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
        });
        Assert.Equal("b", File.ReadAllText(Path.Combine(dest, "whole", "deep", "b.txt")));
        Assert.Equal("s", File.ReadAllText(Path.Combine(dest, "single.txt")));
        Assert.False(_server.Exists("/m/whole"));
        Assert.False(_server.Exists("/m/single.txt"));
        // Skipped because of a conflict: it stays on the server, and the local file is untouched.
        Assert.Equal("remote", _server.Read("/m/blocked.txt"));
        Assert.Equal("local", File.ReadAllText(Path.Combine(dest, "blocked.txt")));
        Assert.Equal(JobState.CompletedWithIssues, job.State);
    }

    [Fact]
    public async Task Downloads_arrive_complete_and_marked_as_coming_from_the_server()
    {
        _server.File("/d/one.txt", "first");
        _server.File("/d/sub/two.txt", "second");
        _server.Link("/d/loop", "/d");
        string dest = Directory.CreateDirectory(Path.Combine(_dir, "down")).FullName;
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [RemoteItem("/", "d", EntryKind.Directory)],
            Destination = Location.FileSystem(dest),
        });
        Assert.Equal("first", File.ReadAllText(Path.Combine(dest, "d", "one.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(dest, "d", "sub", "two.txt")));
        Assert.False(Directory.Exists(Path.Combine(dest, "d", "loop")));
        Assert.Contains("ZoneId=3", _ops.Marks[Path.Combine(dest, "d", "one.txt")]);
        Assert.Contains("HostUrl=sftp://files.example/", _ops.Marks[Path.Combine(dest, "d", "one.txt")]);
        Assert.Contains(job.Issues, i => i.Message.Contains("not followed", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(dest, JournalRecovery.StagedPrefix + "*", SearchOption.AllDirectories));
    }
}
