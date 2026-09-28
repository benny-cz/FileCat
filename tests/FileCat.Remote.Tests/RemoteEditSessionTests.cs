using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class RemoteEditSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-remote-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSftpServer _server = new();
    private readonly RemoteProfile _profile = new() { Name = "Test", Host = "files.example", User = "user" };
    private readonly SftpConnections _connections;
    private readonly SftpProvider _sftp;
    private readonly JobManager _jobs;
    private readonly EditSessionStore _store;

    public RemoteEditSessionTests()
    {
        var ui = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
        for (int i = 0; i < 20; i++) ui.Secrets.Enqueue("secret");
        _connections = new SftpConnections(id => id == _profile.Id ? _profile : null, new FakeConnector(_server),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts"), []), new SessionSecretStore(), ui);
        _sftp = new SftpProvider(_connections, () => [_profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(_sftp);
        SftpJobs.Register();
        var fs = new PortableFileOperations();
        _jobs = new JobManager(fs, providers, Path.Combine(_dir, "journal"));
        _store = new EditSessionStore(Path.Combine(_dir, "sessions"), fs);
    }

    public void Dispose()
    {
        _connections.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private EditSessionRecord Start(string path)
    {
        var item = new ItemRef(SftpProvider.At(_profile, RemotePath.Parent(path)), RemotePath.Name(path), EntryKind.File);
        using var content = _sftp.OpenContent(item)!;
        return _store.CreateRemote(_profile.Id, _profile.Display, path, content, content.GetRevision()!.Value, _sftp.GetOriginMark(item.Parent));
    }

    private ContentRevision? Now(string path)
    {
        using var lease = _connections.Lease(_profile.Id, TestContext.Current.CancellationToken);
        return lease.Channel.Stat(path) is { } st ? new ContentRevision(st.Size, st.ModifiedUtc.Ticks) : null;
    }

    private async Task<Job> CommitAsync(EditSessionRecord session, ContentRevision? expected)
    {
        var job = _jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(session.WorkingPath, EntryKind.File)],
            Destination = SftpProvider.At(_profile, RemotePath.Parent(session.RemotePath)),
            NewName = session.DisplayName,
            ExpectedTarget = expected,
            Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
        });
        while (!job.State.IsFinished())
        {
            job.Decision?.Resolve(new Decision(DecisionAction.Skip));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task A_server_file_is_edited_in_a_private_copy_and_committed_over_the_version_it_started_from()
    {
        _server.File("/srv/app.conf", "port=80");
        var session = Start("/srv/app.conf");
        Assert.True(session.IsRemote);
        Assert.Equal(("app.conf", "user@files.example:/srv/app.conf"), (session.DisplayName, session.DisplayContainer));
        Assert.Equal("port=80", File.ReadAllText(session.WorkingPath));
        Assert.Equal(session.Id, _store.FindRemote(_profile.Id, "/srv/app.conf")!.Id);
        Assert.Null(_store.Find(session.WorkingPath, "app.conf"));
        Assert.Equal(EditState.Unchanged, _store.StateOf(session));

        File.WriteAllText(session.WorkingPath, "port=8080");
        Assert.Equal(EditState.Modified, _store.StateOf(session));
        Assert.Equal(CommitCheck.Ready, EditSessionStore.CheckRemote(session, Now("/srv/app.conf")));
        var job = await CommitAsync(session, session.RemoteBaseline);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("port=8080", _server.Read("/srv/app.conf"));
        session = _store.CommittedRemote(session, EditSessionStore.Hash(session.WorkingPath), Now("/srv/app.conf")!.Value);
        Assert.Equal(EditState.Unchanged, _store.StateOf(session));
        Assert.NotNull(session.LastCommitUtc);
        // A session written before server edits existed has no kind: it still loads, as an archive edit.
        string legacy = Directory.CreateDirectory(Path.Combine(_store.Root, "legacy01")).FullName;
        File.WriteAllText(Path.Combine(legacy, "session.json"),
            "{\"Version\":1,\"Id\":\"legacy01\",\"ArchivePath\":\"C:\\\\a.zip\",\"MemberPath\":\"x.txt\",\"WorkingPath\":\"C:\\\\x.txt\"}");
        var old = Assert.Single(_store.LoadAll(), r => r.Id == "legacy01");
        Assert.Equal(EditSessionRecord.ArchiveKind, old.Kind);
        Assert.False(old.IsRemote);
    }

    [Fact]
    public async Task A_change_on_the_server_is_never_overwritten_without_an_explicit_choice()
    {
        _server.File("/srv/notes.txt", "v1");
        var session = Start("/srv/notes.txt");
        File.WriteAllText(session.WorkingPath, "mine");
        _server.File("/srv/notes.txt", "theirs, longer");
        Assert.Equal(CommitCheck.MemberChanged, EditSessionStore.CheckRemote(session, Now("/srv/notes.txt")));

        // Even if the check were skipped, the job refuses to replace a different version.
        var refused = await CommitAsync(session, session.RemoteBaseline);
        Assert.Equal(JobState.Failed, refused.State);
        Assert.Contains(refused.Issues, i => i.Message.Contains("changed on the server", StringComparison.Ordinal));
        Assert.Equal("theirs, longer", _server.Read("/srv/notes.txt"));

        // The explicit overwrite replaces the version seen now.
        var overwrite = await CommitAsync(session, Now("/srv/notes.txt"));
        Assert.Equal(JobState.Completed, overwrite.State);
        Assert.Equal("mine", _server.Read("/srv/notes.txt"));

        // Gone from the server: creating it again never replaces something that appeared meanwhile.
        _server.Dir("/srv").Children.Remove("notes.txt");
        Assert.Equal(CommitCheck.ArchiveMissing, EditSessionStore.CheckRemote(session, Now("/srv/notes.txt")));
        Assert.Equal(JobState.Completed, (await CommitAsync(session, null)).State);
        Assert.Equal("mine", _server.Read("/srv/notes.txt"));
    }
}
