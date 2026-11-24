using System.Collections.Concurrent;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// Release issue I42 against real servers on this machine: OpenSSH's sshd (Linux and macOS; <see cref="TestSshd"/>) and
/// pyftpdlib. A small file costs SFTP an open and one more round of requests up (the writes, the time, a stat of the
/// handle, the close), then the rename; down, an open with a stat, the read, and a stat. Bytes and times survive the way
/// there and back, the time still reaches a server that refuses to set it through the handle, and a link put at a name
/// is refused, never followed.
/// </summary>
public sealed class SmallFileRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fc-i42-roundtrip-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class Interaction : IRemoteInteraction
    {
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.AcceptOnce;
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => new(TestFtpServer.Password, false);
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
        public HostKeyDecision DecideCertificate(RemoteProfile profile, CertificateCheck check) => HostKeyDecision.AcceptOnce;
        public bool AllowUnencrypted(RemoteProfile profile) => true;
    }

    private sealed class MarkingOps : PortableFileOperations
    {
        public override bool WriteOriginMark(string path, string mark) => true;
    }

    /// <summary>Counts every SFTP request of the connections it makes, by type.</summary>
    private sealed class CountingConnector(ISftpConnector inner) : ISftpConnector
    {
        public readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);

        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = inner.Connect(profile, context, ct);
            if (channel is SshNetChannel ssh) ssh.ObserveRequests(type => Counts.AddOrUpdate(type, 1, (_, n) => n + 1));
            return channel;
        }

        public Dictionary<string, int> Take()
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var key in Counts.Keys)
                if (Counts.TryRemove(key, out int n)) taken[key] = n;
            return taken;
        }
    }

    private sealed record Stack(SftpConnections Connections, JobManager Jobs, CountingConnector Counter, SftpProvider Provider) : IDisposable
    {
        public void Dispose() => Connections.Dispose();
    }

    private Stack Open(RemoteProfile profile, string name)
    {
        string state = Directory.CreateDirectory(Path.Combine(_dir, "state-" + name)).FullName;
        var counter = new CountingConnector(new SshNetConnector());
        var connections = new SftpConnections(id => id == profile.Id ? profile : null, new ProtocolConnector(counter, new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(state, "known_hosts"), []), new SessionSecretStore(), new Interaction(),
            new CertificateTrust(Path.Combine(state, "trusted_certificates")));
        var provider = new SftpProvider(connections, () => [profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(provider);
        SftpJobs.Register();
        return new Stack(connections, new JobManager(new MarkingOps(), providers, Path.Combine(state, "journal")), counter, provider);
    }

    private static async Task<Job> RunAsync(JobManager jobs, JobRequest request)
    {
        var job = jobs.Submit(request);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!job.State.IsFinished())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(2), "The job did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    /// <summary>Three files of distinct bytes, with modified times in whole seconds (what SFTP version 3 carries).</summary>
    private string Sources(string name, int count = 3)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        for (int i = 0; i < count; i++)
        {
            var bytes = new byte[4096 + 17 * i];
            new Random(42 + i).NextBytes(bytes);
            string path = Path.Combine(folder, $"note-{i}.bin");
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc).AddMinutes(-11 * i));
        }
        return folder;
    }

    private static void SameFiles(string expected, string actual)
    {
        var names = Directory.GetFiles(expected).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(names, Directory.GetFiles(actual).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        foreach (string name in names.OfType<string>())
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(expected, name)), File.ReadAllBytes(Path.Combine(actual, name)));
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(expected, name)), File.GetLastWriteTimeUtc(Path.Combine(actual, name)));
        }
    }

    private static int Count(Dictionary<string, int> counts, string type) => counts.GetValueOrDefault(type);

    [Fact]
    public async Task Small_files_cost_sftp_one_open_and_one_round_of_requests_each_way_and_come_back_whole()
    {
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        var ct = TestContext.Current.CancellationToken;
        string source = Sources("up-source");
        Directory.CreateDirectory(Path.Combine(sshd.Files, "up"));
        var profile = sshd.Profile;
        using var stack = Open(profile, "sftp");
        // Connected before counting: what the copies cost, not the sign-in.
        using (var warm = stack.Connections.Lease(profile.Id, ct)) Assert.NotNull(warm.Channel.Stat(sshd.Files));
        stack.Counter.Take();

        var files = Directory.GetFiles(source).Select(f => ItemRef.ForFileSystemPath(f, EntryKind.File)).ToArray();
        var up = await RunAsync(stack.Jobs, new JobRequest { Kind = JobKind.Copy, Sources = files, Destination = SftpProvider.At(profile, RemotePath.Combine(sshd.Files, "up")) });
        Assert.Equal(JobState.Completed, up.State);
        var sent = stack.Counter.Take();
        // Before I42 each file cost 14 requests (realpath before nearly each of them, the time as lstat and setstat, a stat
        // of the path), each waited for. Now: open; the write, fsetstat, fstat and close together; the rename. The
        // destination folder is listed once (realpath, opendir, two readdirs, close).
        Assert.Equal(3, Count(sent, "Open"));
        Assert.Equal(3, Count(sent, "Write"));
        Assert.Equal(3, Count(sent, "FSetStat"));
        Assert.Equal(3, Count(sent, "FStat"));
        Assert.Equal(3, Count(sent, "Rename"));
        Assert.Equal(3 + 1, Count(sent, "Close"));
        Assert.Equal(1, Count(sent, "RealPath"));
        Assert.Equal(0, Count(sent, "LStat") + Count(sent, "Stat") + Count(sent, "SetStat"));
        Assert.Equal(3 * 6 + 5, sent.Values.Sum());
        SameFiles(source, Path.Combine(sshd.Files, "up"));
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(sshd.Files, "up")), f => Path.GetFileName(f).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));

        string back = Directory.CreateDirectory(Path.Combine(_dir, "back")).FullName;
        var down = await RunAsync(stack.Jobs, new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [new ItemRef(SftpProvider.At(profile, sshd.Files), "up", EntryKind.Directory)],
            Destination = Location.FileSystem(back),
        });
        Assert.Equal(JobState.Completed, down.State);
        var read = stack.Counter.Take();
        // Before: 11.5 requests each (two realpath and lstat pairs, open and fstat, reads, close). Now: open with a stat of
        // the path; the read (SSH.NET also asks once past the end without waiting for it); a stat to see the file did not
        // change; the close, not waited for.
        Assert.Equal(3, Count(read, "Open"));
        Assert.Equal(6, Count(read, "Stat"));
        Assert.InRange(Count(read, "Read"), 3, 6);
        Assert.Equal(3 + 1, Count(read, "Close"));
        Assert.Equal(1, Count(read, "RealPath"));
        Assert.Equal(0, Count(read, "LStat") + Count(read, "FStat"));
        SameFiles(source, Path.Combine(back, "up"));
    }

    private static ISftpChannel Connect(TestSshd sshd)
    {
        var context = new ConnectContext
        {
            ApproveHostKey = key => key.AsSpan().SequenceEqual(sshd.HostKey),
            GetSecret = _ => null,
            AnswerPrompts = (_, _) => null,
        };
        return new SshNetConnector().Connect(sshd.Profile, context, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void An_upload_creates_its_file_exclusively_and_reports_the_bytes_and_time_the_server_holds()
    {
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        using var channel = Connect(sshd);
        var bytes = RandomNumberGenerator.GetBytes(100_000);
        var when = new DateTime(2021, 7, 8, 9, 10, 11, DateTimeKind.Utc);
        string path = RemotePath.Combine(sshd.Files, "fresh.bin");
        int asked = 0;
        var stat = channel.UploadNewStated(new MemoryStream(bytes), path, () =>
        {
            asked++;
            return when;
        });
        Assert.Equal(1, asked);
        string local = Path.Combine(sshd.Files, "fresh.bin");
        Assert.Equal(bytes, File.ReadAllBytes(local));
        Assert.Equal(when, File.GetLastWriteTimeUtc(local));
        Assert.Equal(new RemoteStat(false, false, bytes.Length, when), stat);

        // The name is taken: nothing is written over it, and it keeps its bytes and time.
        Assert.ThrowsAny<IOException>(() => channel.UploadNewStated(new MemoryStream([1, 2, 3]), path, () => DateTime.UtcNow));
        Assert.Equal(bytes, File.ReadAllBytes(local));
        Assert.Equal(when, File.GetLastWriteTimeUtc(local));

        // No time asked: the server's own stands.
        string plain = RemotePath.Combine(sshd.Files, "plain.bin");
        var before = DateTime.UtcNow.AddMinutes(-1);
        var plainStat = channel.UploadNewStated(new MemoryStream(bytes), plain, () => null);
        Assert.Equal(bytes.Length, plainStat!.Value.Size);
        Assert.True(File.GetLastWriteTimeUtc(Path.Combine(sshd.Files, "plain.bin")) > before);
    }

    [Theory]
    [InlineData("fsetstat", true)]
    [InlineData("fsetstat,setstat", false)]
    public void A_server_refusing_times_through_the_handle_still_gets_them_through_the_path(string denied, bool kept)
    {
        using var sshd = TestSshd.TryStart(deniedRequests: denied);
        if (sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        using var channel = Connect(sshd);
        var bytes = RandomNumberGenerator.GetBytes(5000);
        var when = new DateTime(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        string path = RemotePath.Combine(sshd.Files, "timed.bin");
        var stat = channel.UploadNewStated(new MemoryStream(bytes), path, () => when);
        string local = Path.Combine(sshd.Files, "timed.bin");
        Assert.Equal(bytes, File.ReadAllBytes(local));
        Assert.Equal(bytes.Length, stat!.Value.Size);
        // What the server reports is what it holds (to the second, as SFTP version 3 carries it), so the job tells the
        // time apart from the one asked for (I43).
        var held = File.GetLastWriteTimeUtc(local);
        Assert.Equal(held.AddTicks(-(held.Ticks % TimeSpan.TicksPerSecond)), stat.Value.ModifiedUtc);
        Assert.Equal(kept, stat.Value.ModifiedUtc == when);
    }

    [Fact]
    public void A_rename_or_a_new_folder_never_goes_through_a_link_put_at_the_name()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Needs symbolic links on the server's file system.");
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        using var channel = Connect(sshd);
        string outside = Directory.CreateDirectory(Path.Combine(_dir, "outside")).FullName;
        File.WriteAllText(Path.Combine(sshd.Files, "stage"), "staged");
        // A link where the copy takes its name, to a place outside that does not exist yet: SSH.NET's realpath followed
        // it, and the rename then created the file there.
        File.CreateSymbolicLink(Path.Combine(sshd.Files, "final"), Path.Combine(outside, "landed"));
        Assert.ThrowsAny<IOException>(() => channel.Rename(RemotePath.Combine(sshd.Files, "stage"), RemotePath.Combine(sshd.Files, "final")));
        Assert.False(File.Exists(Path.Combine(outside, "landed")));
        Assert.Equal("staged", File.ReadAllText(Path.Combine(sshd.Files, "stage")));

        // A link put at the staged name itself: renamed as the link, never its target.
        File.WriteAllText(Path.Combine(outside, "theirs"), "not ours");
        File.CreateSymbolicLink(Path.Combine(sshd.Files, "swapped"), Path.Combine(outside, "theirs"));
        channel.Rename(RemotePath.Combine(sshd.Files, "swapped"), RemotePath.Combine(sshd.Files, "published"));
        Assert.Equal("not ours", File.ReadAllText(Path.Combine(outside, "theirs")));
        Assert.NotNull(new FileInfo(Path.Combine(sshd.Files, "published")).LinkTarget);

        File.CreateSymbolicLink(Path.Combine(sshd.Files, "folder"), Path.Combine(outside, "made"));
        Assert.ThrowsAny<IOException>(() => channel.CreateDirectory(RemotePath.Combine(sshd.Files, "folder")));
        Assert.False(Directory.Exists(Path.Combine(outside, "made")));
    }

    [Fact]
    public void Sftp_content_is_asked_for_its_revision_again_once_it_has_been_read()
    {
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        var profile = sshd.Profile;
        using var stack = Open(profile, "revision");
        RevisionFollowsTheFile(stack, SftpProvider.At(profile, sshd.Files), sshd.Files, expectFirstQuestions: 0);
    }

    [Fact]
    public void Ftp_content_is_asked_for_its_revision_again_once_it_has_been_read()
    {
        var (pem, _) = TestFtpServer.SelfSigned("FileCat I42 revision");
        using var server = TestFtpServer.TryStart(pem);
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var profile = server.Profile(RemoteProtocols.FtpExplicitTls);
        using var stack = Open(profile, "ftp-revision");
        RevisionFollowsTheFile(stack, SftpProvider.At(profile, "/"), server.Root, expectFirstQuestions: null);
    }

    /// <summary>
    /// Before the first read the revision is the stat taken with the open; once read, it is asked again, so a change
    /// while the content was read is seen.
    /// </summary>
    private static void RevisionFollowsTheFile(Stack stack, Location folder, string localFolder, int? expectFirstQuestions)
    {
        string local = Path.Combine(localFolder, "watched.bin");
        File.WriteAllBytes(local, RandomNumberGenerator.GetBytes(10_000));
        File.SetLastWriteTimeUtc(local, new DateTime(2022, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        using var content = stack.Provider.OpenContent(new ItemRef(folder, "watched.bin", EntryKind.File, 10_000))!;
        stack.Counter.Take();
        var opened = content.GetRevision();
        Assert.Equal(new ContentRevision(10_000, new DateTime(2022, 2, 3, 4, 5, 6, DateTimeKind.Utc).Ticks), opened);
        if (expectFirstQuestions is { } questions) Assert.Equal(questions, stack.Counter.Take().Values.Sum());
        var buffer = new byte[4096];
        Assert.Equal(4096, content.Read(0, buffer));
        Assert.Equal(opened, content.GetRevision());
        File.AppendAllText(local, "grown while read");
        Assert.NotEqual(opened, content.GetRevision());
    }

    [Fact]
    public async Task Small_files_go_up_and_come_back_whole_over_ftps()
    {
        var (pem, _) = TestFtpServer.SelfSigned("FileCat I42 round trip");
        using var server = TestFtpServer.TryStart(pem);
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var profile = server.Profile(RemoteProtocols.FtpExplicitTls);
        using var stack = Open(profile, "ftps");
        string source = Sources("ftps-source");
        Directory.CreateDirectory(Path.Combine(server.Root, "up"));
        var up = await RunAsync(stack.Jobs, new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = Directory.GetFiles(source).Select(f => ItemRef.ForFileSystemPath(f, EntryKind.File)).ToArray(),
            Destination = SftpProvider.At(profile, "/up"),
        });
        Assert.Equal(JobState.Completed, up.State);
        SameFiles(source, Path.Combine(server.Root, "up"));
        string back = Directory.CreateDirectory(Path.Combine(_dir, "ftps-back")).FullName;
        var down = await RunAsync(stack.Jobs, new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [new ItemRef(SftpProvider.At(profile, "/"), "up", EntryKind.Directory)],
            Destination = Location.FileSystem(back),
        });
        Assert.Equal(JobState.Completed, down.State);
        SameFiles(source, Path.Combine(back, "up"));
    }
}
