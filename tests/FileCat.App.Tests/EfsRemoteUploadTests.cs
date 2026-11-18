using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;

namespace FileCat.App.Tests;

public sealed class EfsRemoteUploadTests
{
    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Copy, true)]
    [InlineData(JobKind.Move, false)]
    [InlineData(JobKind.Move, true)]
    public async Task Efs_is_reviewed_before_a_server_stage_and_a_declined_move_keeps_its_source(JobKind kind, bool approve)
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        byte[] payload = [1, 4, 9, 16];
        string source = fixture.CreateEncryptedFile(payload);
        int questions = 0;
        Job job = await fixture.Run(source, kind, pending =>
        {
            var review = Assert.IsType<ConfirmRequest>(pending.Request);
            Assert.Equal("efs-remote-upload", review.ConfirmClass);
            Assert.Equal(DecisionAction.Skip, review.InitialAction);
            Assert.False(review.OffersApplyToAll);
            Assert.Contains(source, review.Message);
            Assert.Contains("/upload/secret.bin", review.Message);
            Assert.Empty(fixture.Names);
            questions++;
            return new(approve ? DecisionAction.Proceed : DecisionAction.Skip);
        });
        Assert.Equal(1, questions);
        if (approve)
        {
            Assert.Equal(["secret.bin"], fixture.Names);
            Assert.Equal(payload, fixture.Server.Lookup("/upload/secret.bin", true)!.Data);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("Uploaded without Windows EFS", StringComparison.Ordinal));
        }
        else Assert.Empty(fixture.Names);
        Assert.Equal(kind == JobKind.Copy || !approve, File.Exists(source));
        if (File.Exists(source)) Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
    }

    [Fact]
    public async Task Reopening_an_encrypted_upload_after_disconnect_requires_fresh_approval()
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        byte[] payload = new byte[512 * 1024];
        new Random(17).NextBytes(payload);
        string source = fixture.CreateEncryptedFile(payload);
        fixture.Server.DropAfterBytes = 150_000;
        int approvals = 0, errors = 0;
        var job = await fixture.Run(source, JobKind.Move, pending =>
        {
            if (pending.Request is ErrorRequest error)
            {
                Assert.Equal("disconnected", error.ErrorClass);
                errors++;
                fixture.Server.Down = false;
                return new(DecisionAction.Retry);
            }
            var confirm = Assert.IsType<ConfirmRequest>(pending.Request);
            Assert.Equal("efs-remote-upload", confirm.ConfirmClass);
            if (++approvals == 1)
            {
                Assert.Empty(fixture.Names);
                return new(DecisionAction.Proceed, ApplyToAll: true);
            }
            string staged = Assert.Single(fixture.Names);
            Assert.Equal(150_000, fixture.Server.Lookup("/upload/" + staged, true)!.Data.Length);
            Assert.Empty(fixture.Server.WritesAt);
            return new(DecisionAction.Skip);
        });
        Assert.Equal(2, approvals);
        Assert.Equal(1, errors);
        Assert.Empty(fixture.Names);
        Assert.Empty(fixture.Server.WritesAt);
        Assert.DoesNotContain(job.Issues, issue => issue.Message.Contains("Uploaded without Windows EFS", StringComparison.Ordinal));
        Assert.Contains(job.Issues, issue => issue.Message.Contains("earlier attempt", StringComparison.Ordinal));
        Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
        Assert.Equal(payload, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task A_clear_upload_cannot_retire_a_source_encrypted_before_the_local_move()
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        byte[] payload = [8, 4, 5, 3];
        string source = fixture.CreateEncryptedFile(payload);
        File.Decrypt(source);
        DateTime stamp = File.GetLastWriteTimeUtc(source);
        fixture.Server.AfterRename = () =>
        {
            File.Encrypt(source);
            File.SetLastWriteTimeUtc(source, stamp);
        };
        int questions = 0;
        var job = await fixture.Run(source, JobKind.Move, _ =>
        {
            questions++;
            return new(DecisionAction.Skip);
        });
        Assert.Equal(0, questions);
        Assert.Equal(payload, fixture.Server.Lookup("/upload/secret.bin", true)!.Data);
        Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
        Assert.Equal(payload, File.ReadAllBytes(source));
        Assert.Contains(job.Issues, issue => issue.Message.Contains("no matching approval", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Derived_archive_upload_checks_the_open_archive_before_staging(bool legacyZip, bool encrypted)
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        byte[] bytes = [23, 11, 7, 57];
        var item = fixture.CreateArchiveMember(legacyZip, encrypted, bytes);
        var job = await fixture.Run(item, JobKind.Copy, _ => throw new InvalidOperationException("Unexpected upload question."));
        if (encrypted)
        {
            Assert.Equal(JobState.Failed, job.State);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("archive uses Windows EFS", StringComparison.Ordinal));
            Assert.Empty(fixture.Names);
        }
        else
        {
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(bytes, fixture.Server.Lookup("/upload/member.bin", true)!.Data);
        }
    }

    private static void RequireEfsFixture() => Assert.SkipUnless(OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity &&
        Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Requires the disposable normal-user Windows NTFS fixture.");

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("filecat-efs-upload-").FullName;
        private readonly RemoteProfile _profile = new() { Name = "EFS upload test", Host = "files.example", User = "user" };
        private readonly SftpConnections _connections;
        private readonly JobManager _jobs;
        private readonly ZipProvider _zip;
        private readonly ArchiveProvider _archives;
        private string? _archivePath;
        internal FakeSftpServer Server { get; } = new();
        internal string[] Names => Server.Lookup("/upload", true)!.Children.Keys.ToArray();

        internal Fixture()
        {
            Server.Dir("/upload");
            var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
            for (int i = 0; i < 4; i++) interaction.Secrets.Enqueue("secret");
            _connections = new SftpConnections(id => id == _profile.Id ? _profile : null, new FakeConnector(Server),
                new HostKeyTrust(Path.Join(_root, "known_hosts"), []), new SessionSecretStore(), interaction);
            var providers = new ProviderRegistry();
            var files = new WindowsFileOperations();
            string cache = Path.Join(_root, "archive-cache");
            _zip = new ZipProvider(cache) { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetOpenedFileIdentity };
            _archives = new ArchiveProvider(cache, providers) { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetOpenedFileIdentity };
            providers.Register(new LocalFileSystemProvider());
            providers.Register(_zip);
            providers.Register(_archives);
            providers.Register(new SftpProvider(_connections, () => [_profile], p => p));
            SftpJobs.Register();
            _jobs = new JobManager(files, providers, Path.Join(_root, "journal"));
        }

        internal string CreateEncryptedFile(byte[] bytes)
        {
            string path = Path.Join(_root, "secret.bin");
            File.WriteAllBytes(path, bytes);
            File.Encrypt(path);
            return path;
        }

        internal ItemRef CreateArchiveMember(bool legacyZip, bool encrypted, byte[] bytes)
        {
            string path = _archivePath = Path.Join(_root, legacyZip ? "source.zip" : "source.tar");
            if (legacyZip)
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                using var member = archive.CreateEntry("member.bin").Open();
                member.Write(bytes);
            }
            else
            {
                using var file = File.Create(path);
                using var archive = new TarWriter(file);
                using var member = new MemoryStream(bytes, writable: false);
                archive.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "member.bin") { DataStream = member });
            }
            if (encrypted) File.Encrypt(path);
            var parent = legacyZip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
            return new ItemRef(parent, "member.bin", EntryKind.File, bytes.Length);
        }

        internal Task<Job> Run(string source, JobKind kind, Func<PendingDecision, Decision> decide) =>
            Run(ItemRef.ForFileSystemPath(source, EntryKind.File), kind, decide);

        internal async Task<Job> Run(ItemRef source, JobKind kind, Func<PendingDecision, Decision> decide)
        {
            var job = _jobs.Submit(new JobRequest
            {
                Kind = kind,
                Sources = [source],
                Destination = SftpProvider.At(_profile, "/upload"),
            });
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                while (!job.State.IsFinished() || _jobs.HasActiveWork)
                {
                    Assert.True(DateTime.UtcNow < deadline, "The EFS upload test did not finish.");
                    if (job.Decision is { Task.IsCompleted: false } pending) pending.Resolve(decide(pending));
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                return job;
            }
            finally { if (!job.State.IsFinished()) job.Cancel(); }
        }

        public void Dispose()
        {
            _connections.Dispose();
            if (_archivePath is { } archive)
            {
                _zip.Release(archive);
                _archives.Release(archive);
            }
            Directory.Delete(_root, recursive: true);
        }
    }
}
