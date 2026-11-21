using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App.Tests;

public sealed class EfsRemoteUploadTests
{
    [Theory]
    [InlineData(JobKind.Copy, false, false)]
    [InlineData(JobKind.Copy, true, false)]
    [InlineData(JobKind.Move, false, false)]
    [InlineData(JobKind.Move, true, false)]
    [InlineData(JobKind.Copy, true, true)]
    [InlineData(JobKind.Move, false, true)]
    public async Task Efs_is_reviewed_before_a_server_stage_and_a_declined_move_keeps_its_source(JobKind kind, bool approve, bool sparse)
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        string source = sparse ? fixture.CreateSparseFile(encrypted: true) : fixture.CreateEncryptedFile([1, 4, 9, 16]);
        byte[] payload = File.ReadAllBytes(source);
        int questions = 0;
        string? reviewedStage = null, writtenStage = null;
        fixture.Server.DuringUpload = () => writtenStage = Assert.Single(fixture.Names);
        Job job = await fixture.Run(source, kind, pending =>
        {
            var review = Assert.IsType<ConfirmRequest>(pending.Request);
            Assert.Equal("efs-remote-upload", review.ConfirmClass);
            Assert.Equal(DecisionAction.Skip, review.InitialAction);
            Assert.False(review.OffersApplyToAll);
            Assert.Contains(source, review.Message);
            Assert.Contains("/upload/secret.bin", review.Message);
            Assert.Equal(sparse, review.Message.Contains("server copy may use more disk space", StringComparison.Ordinal));
            reviewedStage = ReviewedStage(review, "Temporary server file: ");
            Assert.Empty(fixture.Names);
            questions++;
            return new(approve ? DecisionAction.Proceed : DecisionAction.Skip);
        });
        Assert.Equal(1, questions);
        if (approve)
        {
            Assert.Equal(["secret.bin"], fixture.Names);
            Assert.Equal(reviewedStage, writtenStage);
            Assert.Equal(payload, fixture.Server.Lookup("/upload/secret.bin", true)!.Data);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("Uploaded without Windows EFS", StringComparison.Ordinal));
        }
        else Assert.Empty(fixture.Names);
        Assert.Equal(approve && sparse, HasSparseNotice(job));
        Assert.Equal(kind == JobKind.Copy || !approve, File.Exists(source));
        if (File.Exists(source)) Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
    }

    [Fact]
    public async Task A_clear_sparse_upload_warns_after_the_logical_bytes_are_published()
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        string source = fixture.CreateSparseFile(encrypted: false);
        var job = await fixture.Run(source, JobKind.Copy, _ => throw new InvalidOperationException("Unexpected upload decision."));
        Assert.Equal(["secret.bin"], fixture.Names);
        Assert.Equal(File.ReadAllBytes(source), fixture.Server.Lookup("/upload/secret.bin", true)!.Data);
        Assert.True(HasSparseNotice(job));
        Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.SparseFile));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Reopening_an_encrypted_upload_after_disconnect_reviews_the_resume_and_restart_names(bool approveRetry, bool serverCanResume)
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        string source = fixture.CreateSparseFile(encrypted: true);
        byte[] payload = File.ReadAllBytes(source);
        fixture.Server.DropAfterBytes = 150_000;
        fixture.Server.ContinuesUploads = serverCanResume;
        int approvals = 0, errors = 0;
        string? initialStage = null, restartStage = null, writtenRestart = null;
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
                initialStage = ReviewedStage(confirm, "Temporary server file: ");
                return new(DecisionAction.Proceed, ApplyToAll: true);
            }
            string staged = Assert.Single(fixture.Names);
            Assert.Equal(initialStage, staged);
            Assert.Equal(staged, ReviewedStage(confirm, "Temporary server file to resume: "));
            restartStage = ReviewedStage(confirm, "Temporary server file if restarted: ");
            Assert.NotEqual(staged, restartStage);
            Assert.Equal(150_000, fixture.Server.Lookup("/upload/" + staged, true)!.Data.Length);
            Assert.Empty(fixture.Server.WritesAt);
            fixture.Server.DuringUpload = () => writtenRestart = Assert.Single(fixture.Names);
            return new(approveRetry ? DecisionAction.Proceed : DecisionAction.Skip);
        });
        Assert.Equal(2, approvals);
        Assert.Equal(1, errors);
        if (approveRetry)
        {
            Assert.Equal(["secret.bin"], fixture.Names);
            Assert.Equal(payload, fixture.Server.Lookup("/upload/secret.bin", true)!.Data);
            Assert.False(File.Exists(source));
            if (serverCanResume) Assert.Equal([150_000L], fixture.Server.WritesAt);
            else
            {
                Assert.Empty(fixture.Server.WritesAt);
                Assert.Equal(restartStage, writtenRestart);
            }
            Assert.True(HasSparseNotice(job));
        }
        else
        {
            Assert.Empty(fixture.Names);
            Assert.Empty(fixture.Server.WritesAt);
            Assert.DoesNotContain(job.Issues, issue => issue.Message.Contains("Uploaded without Windows EFS", StringComparison.Ordinal));
            Assert.Contains(job.Issues, issue => issue.Message.Contains("earlier attempt", StringComparison.Ordinal));
            Assert.False(HasSparseNotice(job));
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(payload, File.ReadAllBytes(source));
        }
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

    [Fact]
    public async Task A_clear_upload_holds_its_source_against_native_efs_conversion_through_publication()
    {
        RequireEfsFixture();
        using var fixture = new Fixture();
        byte[] payload = [31, 41, 59, 26];
        string source = fixture.CreateEncryptedFile(payload);
        File.Decrypt(source);
        Assert.False(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
        Exception? conversionFailure = null;
        int attempted = 0;
        fixture.Server.AfterRename = () =>
        {
            attempted++;
            try { File.Encrypt(source); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { conversionFailure = error; }
        };
        var job = await fixture.Run(source, JobKind.Copy, _ => throw new InvalidOperationException("Unexpected upload decision."));
        Assert.Equal(1, attempted);
        Assert.NotNull(conversionFailure);
        Assert.False(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(payload, fixture.Server.Lookup("/upload/secret.bin", true)!.Data);
        File.Encrypt(source);
        Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
        Assert.Equal(payload, File.ReadAllBytes(source));
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

    private static void RequireEfsFixture()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Requires the disposable normal-user Windows NTFS fixture.");
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE") == "Arm64")
        {
            Assert.True(WindowsStorageTransforms.IsNativeArm64QualificationHost);
            string root = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
            Assert.Equal(Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ABI_ROOT"), root, ignoreCase: true);
        }
    }

    private static string ReviewedStage(ConfirmRequest review, string label)
    {
        string line = Assert.Single(review.Message.Split('\n'), line => line.StartsWith(label, StringComparison.Ordinal));
        string name = line[(line.LastIndexOf('/') + 1)..];
        Assert.StartsWith(JournalRecovery.StagedPrefix, name);
        return name;
    }

    private static bool HasSparseNotice(Job job) => job.Issues.Any(issue => issue.Outcome == StepOutcome.Committed &&
        issue.Message.Contains("server copy may use more disk space", StringComparison.Ordinal));

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSparse(SafeFileHandle file, uint operation, nint input, uint inputBytes, nint output,
        uint outputBytes, out uint written, nint overlapped);

    internal static void WriteSparseFile(string path)
    {
        using var file = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite);
        Assert.True(SetSparse(file, 0x000900c4 /* FSCTL_SET_SPARSE */, 0, 0, 0, 0, out _, 0));
        RandomAccess.SetLength(file, 2 * 1024 * 1024);
        RandomAccess.Write(file, [1, 4, 9, 16], 0);
        RandomAccess.Write(file, [23, 11, 7], 1024 * 1024);
    }

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

        internal string CreateSparseFile(bool encrypted)
        {
            string path = Path.Join(_root, "secret.bin");
            WriteSparseFile(path);
            if (encrypted) File.Encrypt(path);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.SparseFile));
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
