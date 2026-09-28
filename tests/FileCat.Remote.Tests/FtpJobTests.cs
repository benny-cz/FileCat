using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>FTP through the same jobs as SFTP: uploads publish through temporary names, downloads are marked.</summary>
public sealed class FtpJobTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-ftp-jobs", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

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

        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            base.Move(source, destination, replaceExisting, writeThrough);
            if (Marks.Remove(source, out var m)) Marks[destination] = m;
        }
    }

    [Fact]
    public async Task Uploads_publish_complete_files_and_replace_without_an_atomic_rename()
    {
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        profile.PlainTextAccepted = true;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts")), new SessionSecretStore(), new FtpInteraction());
        var sftp = new SftpProvider(connections, () => [profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(sftp);
        SftpJobs.Register();
        var ops = new MarkingOps();
        var jobs = new JobManager(ops, providers, Path.Combine(_dir, "journal"));
        var local = Directory.CreateDirectory(Path.Combine(_dir, "local")).FullName;
        File.WriteAllText(Path.Combine(local, "report.txt"), "version 2");
        File.WriteAllText(Path.Combine(server.Root, "report.txt"), "version 1");

        async Task<Job> Run(JobRequest request)
        {
            var job = jobs.Submit(request);
            while (!job.State.IsFinished())
            {
                job.Decision?.Resolve(new Decision(DecisionAction.Replace));
                await Task.Delay(10, ct);
            }
            return job;
        }

        var upload = await Run(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(Path.Combine(local, "report.txt"), EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
        });
        Assert.True(upload.State is JobState.Completed or JobState.CompletedWithIssues, string.Join("; ", upload.Issues.Select(i => i.Message)));
        Assert.Equal("version 2", File.ReadAllText(Path.Combine(server.Root, "report.txt")));
        Assert.Contains(upload.Issues, i => i.Message.Contains("cannot replace a file in one step"));
        Assert.Equal(["report.txt"], Directory.GetFiles(server.Root).Select(Path.GetFileName)); // no temporary names left

        var dest = Directory.CreateDirectory(Path.Combine(_dir, "down")).FullName;
        var download = await Run(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [new ItemRef(SftpProvider.At(profile, "/"), "report.txt", EntryKind.File)],
            Destination = Location.FileSystem(dest),
        });
        Assert.True(download.State == JobState.Completed, string.Join("; ", download.Issues.Select(i => i.Message)));
        Assert.Equal("version 2", File.ReadAllText(Path.Combine(dest, "report.txt")));
        Assert.Equal($"ftp://tester@127.0.0.1:{server.Port}/report.txt", sftp.GetDisplayPath(SftpProvider.At(profile, "/report.txt")));
        Assert.Contains($"HostUrl=ftp://127.0.0.1:{server.Port}/", ops.Marks[Path.Combine(dest, "report.txt")]);
    }

    [Theory]
    [InlineData("ftp://files.example/pub", RemoteProtocols.Ftp, 21, "anonymous", "/pub")]
    [InlineData("ftpes://me@files.example", RemoteProtocols.FtpExplicitTls, 21, "me", "~")]
    [InlineData("ftps://me@files.example:2990/data", RemoteProtocols.FtpImplicitTls, 2990, "me", "/data")]
    [InlineData("FTPS://files.example", RemoteProtocols.FtpImplicitTls, 990, "anonymous", "~")]
    [InlineData("sftp://me@files.example", RemoteProtocols.Sftp, 22, "me", "~")]
    public void Typed_addresses_choose_the_protocol_port_and_user(string address, string protocol, int port, string user, string path)
    {
        var added = new List<RemoteProfile>();
        using var connections = new SftpConnections(_ => null, new SshNetConnector(), new HostKeyTrust(Path.Combine(_dir, "kh")), new SessionSecretStore(), new FtpInteraction());
        var provider = new SftpProvider(connections, () => [], p =>
        {
            added.Add(p);
            return p;
        });
        Assert.True(provider.TryParse(address, null, out var location));
        var profile = Assert.Single(added);
        Assert.Equal((protocol, port, user, path), (profile.Protocol, profile.Port, profile.User, location!.Path));
        Assert.False(profile.PlainTextAccepted);
        Assert.False(provider.TryParse("http://files.example", null, out _));
    }
}
