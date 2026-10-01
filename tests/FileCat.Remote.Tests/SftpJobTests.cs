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
    public async Task A_dropped_upload_starts_again_where_the_server_will_not_continue_it()
    {
        // Release issue I47: ProFTPD refused APPE after a break, and every Retry was refused the same way.
        var data = new byte[3 * 1024 * 1024 + 5];
        new Random(4).NextBytes(data);
        string big = Path.Combine(_local, "big.bin");
        File.WriteAllBytes(big, data);
        _server.Dir("/up");
        _server.ContinuesUploads = false;
        _server.DropAfterBytes = 2 * 1024 * 1024 + 17;
        var job = _jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(big, EntryKind.File)], Destination = Remote("/up") });
        int asked = 0;
        while (!job.State.IsFinished())
        {
            if (job.Decision is { Task.IsCompleted: false } d)
            {
                asked++;
                _server.Down = false;
                d.Resolve(new Decision(DecisionAction.Retry));
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(1, asked); // the break only: the refusal to continue is not a question
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(data, _server.Lookup("/up/big.bin", true)!.Data);
        Assert.Contains(job.Issues, i => i.Message.Contains("went again from the start: the server does not continue uploads", StringComparison.Ordinal));
        Assert.Equal(["big.bin"], Names("/up")); // the partial copy went
    }

    [Fact]
    public async Task Uploads_keep_modified_times_and_say_so_where_the_server_does_not()
    {
        // Release issue I43: vsftpd has no MFMT, so FileCat sent no time, and every upload silently showed its arrival.
        var when = new DateTime(2020, 2, 2, 2, 2, 2, DateTimeKind.Utc);
        foreach (string f in new[] { LocalFile("t/a.txt", "a"), LocalFile("t/b.txt", "b") }) File.SetLastWriteTimeUtc(f, when);
        var tree = ItemRef.ForFileSystemPath(Path.Combine(_local, "t"), EntryKind.Directory);
        _server.Dir("/up");

        var kept = await RunAsync(new JobRequest { Kind = JobKind.Copy, Sources = [tree], Destination = Remote("/up") });
        Assert.Equal(JobState.Completed, kept.State);
        Assert.Equal(when, _server.Lookup("/up/t/a.txt", followFinal: false)!.Modified);
        Assert.DoesNotContain(kept.Issues, i => i.Message.Contains("modified time", StringComparison.Ordinal));

        _server.KeepsTimes = false;
        _server.Dir("/up2");
        var lost = await RunAsync(new JobRequest { Kind = JobKind.Copy, Sources = [tree], Destination = Remote("/up2") });
        Assert.Equal(JobState.Completed, lost.State);
        var note = Assert.Single(lost.Issues, i => i.Message.Contains("modified time", StringComparison.Ordinal));
        Assert.Equal(IssueSeverity.Info, note.Severity);
        Assert.Contains("2 uploaded files", note.Message, StringComparison.Ordinal);

        // Times not to be kept: none is sent, so the server's own stands, and nothing is said about it.
        _server.KeepsTimes = true;
        _server.Dir("/up3");
        var asIs = await RunAsync(new JobRequest { Kind = JobKind.Copy, Sources = [tree], Destination = Remote("/up3"), Options = new TransferOptions { PreserveTimestamps = false } });
        Assert.Equal(JobState.Completed, asIs.State);
        Assert.NotEqual(when, _server.Lookup("/up3/t/a.txt", followFinal: false)!.Modified);
        Assert.DoesNotContain(asIs.Issues, i => i.Message.Contains("modified time", StringComparison.Ordinal));
    }

    [Theory]
    // Release issue I39: at 100 ms a continued SFTP upload writes 0.3 MB/s, a new one 5.7 MB/s (measured in the lab).
    [InlineData(48_000_000L, 128_000_000L, 5.7, 100, true, true)] // a third on the server, a slow link: starting again wins
    [InlineData(126_000_000L, 128_000_000L, 5.7, 100, true, false)] // nearly all there already: continuing wins
    [InlineData(48_000_000L, 128_000_000L, 60.0, 1, true, false)] // a LAN: continuing at 32 MB/s wins
    [InlineData(48_000_000L, 128_000_000L, 5.7, 100, false, false)] // FTP appends at full speed: always continue
    public void An_interrupted_upload_starts_again_only_where_that_is_quicker(long start, long size, double freshMBps, int roundTripMs, bool oneAtATime, bool anew)
    {
        long freshBytes = (long)(freshMBps * 1_000_000 * 10);
        Assert.Equal(anew, SftpUploadExecutor.QuickerAnew(start, size, freshBytes, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(roundTripMs), oneAtATime));
        // Without a measured pace (under a second), never.
        Assert.False(SftpUploadExecutor.QuickerAnew(start, size, freshBytes, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(roundTripMs), oneAtATime));
    }

    private string BigFile(int length, int seed, out byte[] data)
    {
        data = new byte[length];
        new Random(seed).NextBytes(data);
        string big = Path.Combine(_local, "big.bin");
        File.WriteAllBytes(big, data);
        return big;
    }

    /// <summary>The upload's partial copy on the server (its hidden temporary name).</summary>
    private FakeSftpServer.Node Partial(string folder) => _server.Lookup(folder, true)!.Children.Values.Single(n => n.Name.StartsWith(".fc-", StringComparison.Ordinal));

    /// <summary>Drops the upload part way; while it is down, <paramref name="meanwhile"/> runs, then the user retries.</summary>
    private async Task<Job> DroppedUploadAsync(string big, Action meanwhile, VerifyMode verify)
    {
        _server.Dir("/up");
        _server.DropAfterBytes = 2 * 1024 * 1024 + 17;
        var job = _jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(big, EntryKind.File)],
            Destination = Remote("/up"),
            Options = new TransferOptions { Verify = verify },
        });
        bool once = false;
        while (!job.State.IsFinished())
        {
            if (job.Decision is { Task.IsCompleted: false } d)
            {
                if (!once) meanwhile();
                once = true;
                _server.Down = false;
                d.Resolve(new Decision(DecisionAction.Retry));
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task A_dropped_upload_whose_partial_copy_changed_at_its_end_starts_again()
    {
        // Release plan V08: "alter resume tails". The check of the last 64 KiB sees it, and the whole file goes again.
        string big = BigFile(5 * 1024 * 1024, 10, out var data);
        var job = await DroppedUploadAsync(big, () => Partial("/up").Data[^1] ^= 0xFF, VerifyMode.Native);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Empty(_server.WritesAt);
        Assert.Equal(data, _server.Lookup("/up/big.bin", true)!.Data);
        Assert.Equal(["big.bin"], Names("/up"));
    }

    [Fact]
    public async Task A_resumed_upload_whose_partial_copy_changed_earlier_is_caught_by_read_back()
    {
        // "…and earlier content": a byte before the checked tail cannot be seen by the resume's check, so the upload
        // continues; reading the copy back before it is published catches it, and nothing takes the name.
        string big = BigFile(5 * 1024 * 1024, 11, out _);
        var job = await DroppedUploadAsync(big, () => Partial("/up").Data[100] ^= 0xFF, VerifyMode.ReadBack);
        Assert.Equal([2L * 1024 * 1024 + 17], _server.WritesAt);
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Contains(job.Issues, i => i.Message.Contains("Read-back verification found different content on the server", StringComparison.Ordinal));
        Assert.Empty(Names("/up"));
    }

    [Fact]
    public async Task Read_back_verification_catches_bytes_the_server_stored_differently()
    {
        // Release V08: "read back and compare content" was ignored for uploads, so a copy the server stored wrongly
        // passed its size check and was published.
        string big = BigFile(3 * 1024 * 1024, 12, out _);
        _server.Dir("/up");
        _server.CorruptAt = 1_000_000;
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(big, EntryKind.File)],
            Destination = Remote("/up"),
            Options = new TransferOptions { Verify = VerifyMode.ReadBack },
        });
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Contains(job.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("Read-back verification found different content on the server", StringComparison.Ordinal));
        Assert.Empty(Names("/up")); // nothing published, and the temporary copy is gone
    }

    [Fact]
    public async Task Read_back_verification_passes_a_faithful_upload_and_counts_both_readings()
    {
        string big = BigFile(3 * 1024 * 1024 + 3, 13, out var data);
        _server.Dir("/up");
        var job = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(big, EntryKind.File)],
            Destination = Remote("/up"),
            Options = new TransferOptions { Verify = VerifyMode.ReadBack },
        });
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(data, _server.Lookup("/up/big.bin", true)!.Data);
        Assert.Equal(2L * data.Length, job.VerifyBytesDone);
        Assert.Equal(job.VerifyBytesTotal, job.VerifyBytesDone);
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
    public async Task Links_are_not_renamed_moved_or_set_aside_where_the_server_may_rename_their_targets()
    {
        // Release issue I46: ProFTPD's SFTP renamed and moved a link's target instead of the link.
        _server.RenamesLinksThemselves = false;
        _server.File("/r/target.txt", "keep");
        _server.Link("/r/link.txt", "/r/target.txt");
        _server.Dir("/r/into");
        var rename = await RunAsync(new JobRequest { Kind = JobKind.Rename, Sources = [RemoteItem("/r", "link.txt")], NewName = "renamed.txt" });
        var move = await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [RemoteItem("/r", "link.txt")], Destination = Remote("/r/into") });
        string b = LocalFile("link.txt", "uploaded");
        var aside = await RunAsync(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(b, EntryKind.File)], Destination = Remote("/r"),
            Options = new TransferOptions { Conflicts = ConflictPolicy.KeepBothRenameExisting },
        });
        Assert.All(new[] { rename, move, aside }, j => Assert.Contains(j.Issues, i => i.Message.Contains("it is a link", StringComparison.Ordinal)));
        Assert.Equal(JobState.Failed, rename.State);
        Assert.Equal(JobState.Failed, move.State);
        // Nothing moved: the link and its target are where they were.
        Assert.Equal("/r/target.txt", _server.Lookup("/r/link.txt", followFinal: false)!.LinkTarget);
        Assert.Equal("keep", _server.Read("/r/target.txt"));
        Assert.Null(_server.Lookup("/r/renamed.txt", false));
        Assert.Null(_server.Lookup("/r/into/link.txt", false));
        // A file is renamed as before.
        Assert.Equal(JobState.Completed, (await RunAsync(new JobRequest { Kind = JobKind.Rename, Sources = [RemoteItem("/r", "target.txt")], NewName = "t2.txt" })).State);
    }

    /// <summary>
    /// "Only files matching" moves only those: the others stay where they were, with their folders, in both directions;
    /// a move the executor cannot filter (a rename on the server) is refused before anything changes.
    /// </summary>
    [Fact]
    public async Task A_filtered_move_moves_only_the_matching_files_and_keeps_the_rest()
    {
        var logs = new TransferOptions { Filter = Core.Selection.Mask.Parse("*.log") };
        LocalFile("f/a.log", "a");
        LocalFile("f/b.txt", "b");
        LocalFile("f/sub/c.log", "c");
        _server.Dir("/up");
        var up = await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [ItemRef.ForFileSystemPath(Path.Combine(_local, "f"), EntryKind.Directory)], Destination = Remote("/up"), Options = logs });
        Assert.Equal("a", _server.Read("/up/f/a.log"));
        Assert.Equal("c", _server.Read("/up/f/sub/c.log"));
        Assert.False(_server.Exists("/up/f/b.txt"));
        Assert.False(File.Exists(Path.Combine(_local, "f", "a.log")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_local, "f", "b.txt")));
        Assert.NotEqual(JobState.Failed, up.State);

        _server.File("/m2/logs/x.log", "x");
        _server.File("/m2/logs/y.txt", "y");
        string dest = Directory.CreateDirectory(Path.Combine(_dir, "down")).FullName;
        await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [RemoteItem("/m2", "logs", EntryKind.Directory)], Destination = Location.FileSystem(dest), Options = logs });
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "logs", "x.log")));
        Assert.False(File.Exists(Path.Combine(dest, "logs", "y.txt")));
        // The folder holds a file the filter left out, so it stays on the server, with that file.
        Assert.Equal("y", _server.Read("/m2/logs/y.txt"));

        _server.File("/src2/keep.txt", "k");
        var refused = await RunAsync(new JobRequest { Kind = JobKind.Move, Sources = [RemoteItem("/", "src2", EntryKind.Directory)], Destination = Remote("/up"), Options = logs });
        Assert.Contains(refused.Issues, i => i.Message.Contains("Only files matching", StringComparison.Ordinal));
        Assert.Equal("k", _server.Read("/src2/keep.txt"));
        Assert.False(_server.Exists("/up/src2"));
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
