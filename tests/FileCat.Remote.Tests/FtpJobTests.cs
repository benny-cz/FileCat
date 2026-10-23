using System.Diagnostics;
using System.Security.Cryptography;
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
    [InlineData(RemoteProtocols.Ftp)]
    [InlineData(RemoteProtocols.FtpExplicitTls)]
    [InlineData(RemoteProtocols.FtpImplicitTls)]
    public async Task Canceling_a_partial_FTP_upload_removes_its_unpublished_stage_and_preserves_the_source(string protocol)
    {
        bool encrypted = protocol != RemoteProtocols.Ftp;
        using var server = TestFtpServer.TryStart(
            encrypted ? TestFtpServer.SelfSigned("FileCat cancellation test").Pem : null,
            implicitTls: protocol == RemoteProtocols.FtpImplicitTls);
        if (server is null) Assert.Skip("No FTP/FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(protocol);
        profile.PlainTextAccepted = !encrypted;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
            new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_dir, "known_hosts")), new SessionSecretStore(),
            new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce },
            new CertificateTrust(Path.Combine(_dir, "cancel-trusted-certificates")));
        var remote = new SftpProvider(connections, () => [profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(remote);
        SftpJobs.Register();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir, "cancel-journal"));
        var bytes = new byte[8 * 1024 * 1024];
        new Random(14).NextBytes(bytes);
        string source = Path.Combine(_dir, "partial-upload.bin");
        File.WriteAllBytes(source, bytes);
        var originalHash = SHA256.HashData(bytes);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
            Options = new TransferOptions { RateLimit = 64 * 1024 },
        });
        try
        {
            string? staged = null;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && staged is null && !job.State.IsFinished())
            {
                staged = Directory.GetFiles(server.Root, $"{JournalRecovery.StagedPrefix}*")
                    .FirstOrDefault(path => new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024);
                if (staged is null) await Task.Delay(20, ct);
            }
            Assert.NotNull(staged);
            Assert.Equal(JobState.Running, job.State);
            Assert.False(File.Exists(Path.Combine(server.Root, "partial-upload.bin")));

            job.Cancel();
            deadline.Restart();
            while (!job.State.IsFinished() && deadline.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(20, ct);
            Assert.Equal(JobState.Canceled, job.State);
            Assert.Empty(Directory.GetFiles(server.Root));
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
        }
        finally
        {
            if (!job.State.IsFinished()) job.Cancel();
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Ftp, false)]
    [InlineData(RemoteProtocols.FtpExplicitTls, false)]
    [InlineData(RemoteProtocols.FtpImplicitTls, false)]
    [InlineData(RemoteProtocols.Ftp, true)]
    [InlineData(RemoteProtocols.FtpExplicitTls, true)]
    [InlineData(RemoteProtocols.FtpImplicitTls, true)]
    public async Task Canceling_or_skipping_after_an_FTP_connection_drops_reports_the_retained_unpublished_stage(string protocol, bool skip)
    {
        bool encrypted = protocol != RemoteProtocols.Ftp;
        using var server = TestFtpServer.TryStart(
            encrypted ? TestFtpServer.SelfSigned("FileCat dropped connection test").Pem : null,
            implicitTls: protocol == RemoteProtocols.FtpImplicitTls);
        if (server is null) Assert.Skip("No FTP/FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(protocol);
        profile.PlainTextAccepted = !encrypted;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
            new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_dir, "dropped-known-hosts")), new SessionSecretStore(),
            new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce },
            new CertificateTrust(Path.Combine(_dir, "dropped-trusted-certificates")));
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p));
        SftpJobs.Register();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir, "dropped-journal"));
        var bytes = new byte[8 * 1024 * 1024];
        new Random(119).NextBytes(bytes);
        byte[] originalHash = SHA256.HashData(bytes);
        string source = Path.Combine(_dir, "dropped-upload.bin");
        File.WriteAllBytes(source, bytes);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
            Options = new TransferOptions { RateLimit = 64 * 1024 },
        });
        try
        {
            string? stage = null;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && stage is null && !job.State.IsFinished())
            {
                stage = Directory.GetFiles(server.Root, $"{JournalRecovery.StagedPrefix}*")
                    .FirstOrDefault(path => new FileInfo(path).Length is > 0 and < 8 * 1024 * 1024);
                if (stage is null) await Task.Delay(20, ct);
            }
            Assert.NotNull(stage);
            Assert.Equal(JobState.Running, job.State);
            server.Stop();
            deadline.Restart();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && job.Decision is not { Task.IsCompleted: false } && !job.State.IsFinished())
                await Task.Delay(20, ct);
            Assert.True(job.Decision is { Task.IsCompleted: false },
                $"The dropped {protocol} upload did not reach its retry/skip/cancel decision: {job.State}.");

            if (skip) job.Decision!.Resolve(new Decision(DecisionAction.Skip));
            else job.Cancel();
            deadline.Restart();
            while (!job.State.IsFinished() && deadline.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(20, ct);
            // A single-item job whose only item failed is Failed; Skip declines retry,
            // and the issue records that choice even though no item could complete.
            JobState expected = skip ? JobState.Failed : JobState.Canceled;
            Assert.True(job.State == expected,
                $"Expected {expected} after the lost {protocol} connection, got {job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
            if (skip) Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Skipped);
            Assert.False(File.Exists(Path.Combine(server.Root, "dropped-upload.bin")));
            Assert.Equal([stage], Directory.GetFiles(server.Root));
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
            Assert.Contains(job.Issues, issue => issue.Message.Contains("partial copy is left on the server", StringComparison.Ordinal));
        }
        finally
        {
            if (!job.State.IsFinished()) job.Cancel();
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Ftp)]
    [InlineData(RemoteProtocols.FtpExplicitTls)]
    [InlineData(RemoteProtocols.FtpImplicitTls)]
    public async Task Retrying_after_an_FTP_connection_drops_reconnects_and_publishes_only_a_complete_move(string protocol)
    {
        bool encrypted = protocol != RemoteProtocols.Ftp;
        using var server = TestFtpServer.TryStart(
            encrypted ? TestFtpServer.SelfSigned("FileCat retry test").Pem : null,
            implicitTls: protocol == RemoteProtocols.FtpImplicitTls);
        if (server is null) Assert.Skip("No FTP/FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(protocol);
        profile.PlainTextAccepted = !encrypted;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
            new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_dir, "retry-known-hosts")), new SessionSecretStore(),
            new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce },
            new CertificateTrust(Path.Combine(_dir, "retry-trusted-certificates")));
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p));
        SftpJobs.Register();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir, "retry-journal"));
        var bytes = new byte[2 * 1024 * 1024];
        new Random(177).NextBytes(bytes);
        byte[] originalHash = SHA256.HashData(bytes);
        string source = Path.Combine(_dir, "retry-upload.bin");
        File.WriteAllBytes(source, bytes);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
            Options = new TransferOptions { RateLimit = 256 * 1024 },
        });
        try
        {
            string? stage = null;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && stage is null && !job.State.IsFinished())
            {
                stage = Directory.GetFiles(server.Root, $"{JournalRecovery.StagedPrefix}*")
                    .FirstOrDefault(path => new FileInfo(path).Length is > 0 and < 2 * 1024 * 1024);
                if (stage is null) await Task.Delay(20, ct);
            }
            Assert.NotNull(stage);
            server.Stop();
            deadline.Restart();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && job.Decision is not { Task.IsCompleted: false } && !job.State.IsFinished())
                await Task.Delay(20, ct);
            Assert.True(job.Decision is { Task.IsCompleted: false },
                $"The dropped {protocol} upload did not reach a retry decision: {job.State}.");
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
            Assert.False(File.Exists(Path.Combine(server.Root, "retry-upload.bin")));

            server.Restart();
            job.Decision!.Resolve(new Decision(DecisionAction.Retry));
            deadline.Restart();
            while (!job.State.IsFinished() && deadline.Elapsed < TimeSpan.FromSeconds(45))
                await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.False(File.Exists(source));
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(server.Root, "retry-upload.bin"))));
            Assert.Equal([Path.Combine(server.Root, "retry-upload.bin")], Directory.GetFiles(server.Root));
        }
        finally
        {
            if (!job.State.IsFinished()) job.Cancel();
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Ftp, false)]
    [InlineData(RemoteProtocols.FtpExplicitTls, false)]
    [InlineData(RemoteProtocols.FtpImplicitTls, false)]
    [InlineData(RemoteProtocols.Ftp, true)]
    [InlineData(RemoteProtocols.FtpExplicitTls, true)]
    [InlineData(RemoteProtocols.FtpImplicitTls, true)]
    public async Task A_changed_local_move_source_retries_the_new_version_or_discards_its_FTP_stage(string protocol, bool retry)
    {
        bool encrypted = protocol != RemoteProtocols.Ftp;
        using var server = TestFtpServer.TryStart(
            encrypted ? TestFtpServer.SelfSigned("FileCat changed source test").Pem : null,
            implicitTls: protocol == RemoteProtocols.FtpImplicitTls);
        if (server is null) Assert.Skip("No FTP/FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(protocol);
        profile.PlainTextAccepted = !encrypted;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
            new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_dir, "changed-known-hosts")), new SessionSecretStore(),
            new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce },
            new CertificateTrust(Path.Combine(_dir, "changed-trusted-certificates")));
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p));
        SftpJobs.Register();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir, "changed-journal"));
        var bytes = new byte[2 * 1024 * 1024];
        new Random(218).NextBytes(bytes);
        byte[] originalHash = SHA256.HashData(bytes);
        string source = Path.Combine(_dir, "changed-upload.bin");
        File.WriteAllBytes(source, bytes);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
            Options = new TransferOptions { RateLimit = 256 * 1024 },
        });
        try
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && !job.State.IsFinished() &&
                   !Directory.GetFiles(server.Root, $"{JournalRecovery.StagedPrefix}*")
                       .Any(path => new FileInfo(path).Length is > 0 and < 2 * 1024 * 1024))
                await Task.Delay(20, ct);
            Assert.Equal(JobState.Running, job.State);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(5));
            deadline.Restart();
            while (deadline.Elapsed < TimeSpan.FromSeconds(25) && job.Decision is not { Task.IsCompleted: false } && !job.State.IsFinished())
                await Task.Delay(20, ct);
            Assert.True(job.Decision is { Task.IsCompleted: false },
                $"The changed {protocol} source did not reach its failure decision: {job.State}.");
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
            byte[] retriedBytes = new byte[bytes.Length];
            new Random(219).NextBytes(retriedBytes);
            if (retry)
            {
                // The first source handle has closed by the time the decision appears.
                // A retry must recapture this newer version, not resume the old stage.
                File.WriteAllBytes(source, retriedBytes);
                File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(6));
            }
            job.Decision!.Resolve(new Decision(retry ? DecisionAction.Retry : DecisionAction.Skip));
            deadline.Restart();
            while (!job.State.IsFinished() && deadline.Elapsed < TimeSpan.FromSeconds(retry ? 45 : 20))
                await Task.Delay(20, ct);
            if (retry)
            {
                Assert.Equal(JobState.Completed, job.State);
                Assert.False(File.Exists(source));
                string published = Path.Combine(server.Root, "changed-upload.bin");
                Assert.Equal(SHA256.HashData(retriedBytes), SHA256.HashData(File.ReadAllBytes(published)));
                Assert.Equal([published], Directory.GetFiles(server.Root));
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State);
                Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
                Assert.Empty(Directory.GetFiles(server.Root));
                Assert.Contains(job.Issues, issue => issue.Message.Contains("source changed during uploading", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (!job.State.IsFinished()) job.Cancel();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_killed_FTP_upload_leaves_only_an_unpublished_stage_and_an_open_recovery_journal(bool move)
    {
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        const int size = 8 * 1024 * 1024;
        var source = Path.Combine(_dir, move ? "interrupted-move.bin" : "interrupted-copy.bin");
        var expected = new byte[size];
        new Random(move ? 241 : 240).NextBytes(expected);
        await File.WriteAllBytesAsync(source, expected, ct);
        var journalDirectory = Directory.CreateDirectory(Path.Combine(_dir, move ? "move-journal" : "copy-journal")).FullName;
        var harness = CrashHarnessPath();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(harness);
        start.ArgumentList.Add(server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(source);
        start.ArgumentList.Add(journalDirectory);
        start.ArgumentList.Add(move ? "move" : "copy");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated FTP crash harness.");
        try
        {
            string? staged = null;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30) && !child.HasExited)
            {
                staged = Directory.GetFiles(server.Root, $"{JournalRecovery.StagedPrefix}*")
                    .FirstOrDefault(path => new FileInfo(path).Length is > 0 and < size);
                if (staged is not null) break;
                await Task.Delay(25, ct);
            }
            string error = child.HasExited ? await child.StandardError.ReadToEndAsync(ct) : "worker still running";
            Assert.True(staged is not null && !child.HasExited,
                $"FTP upload did not reach a partial stage; exit={child.HasExited}; error={error}");
            // Kill the exact process we started. This is a real crash, not a cooperative Cancel.
            child.Kill(entireProcessTree: true);
            Assert.True(child.WaitForExit(10_000), "The isolated transfer worker did not exit.");
            Assert.False(File.Exists(Path.Combine(server.Root, Path.GetFileName(source))));
            Assert.Equal(SHA256.HashData(expected), SHA256.HashData(await File.ReadAllBytesAsync(source, ct)));

            IReadOnlyList<InterruptedJob> interrupted = [];
            deadline.Restart();
            while (deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                interrupted = JournalRecovery.Scan(journalDirectory);
                if (interrupted.Count == 1) break;
                await Task.Delay(25, ct);
            }
            var job = Assert.Single(interrupted);
            Assert.Equal(move ? "Move" : "Copy", job.Kind);
            Assert.Equal([source], JournalRecovery.LoadSources(job));
            Assert.True(JournalRecovery.IsInterrupted(job));
            Assert.StartsWith(".fc-" + Path.GetFileNameWithoutExtension(job.JournalPath)[^8..] + "-", Path.GetFileName(staged));
            Assert.Equal([staged], Directory.GetFiles(server.Root));
            Assert.InRange(new FileInfo(staged).Length, 1, size - 1);
            Assert.Empty(JournalRecovery.FindStagedLeftovers(job)); // The remote stage is not a local file.
            if (move)
                Assert.Contains(job.OpenIntents, intent => intent.Operation == "upload-move" &&
                    intent.Path == source && intent.Target == "/" + Path.GetFileName(source));
            // A fresh startup must still show this unresolved remote operation. The UI keeps
            // recovery tracking open until the user has inspected the server-side stage.
            Assert.Equal(job.JournalPath, Assert.Single(JournalRecovery.Scan(journalDirectory)).JournalPath);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit(10_000);
            }
        }
    }

    private static string CrashHarnessPath()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "FileCat.slnx"))) path = path.Parent;
        if (path is null) throw new InvalidOperationException("The Git checkout containing the FTP crash harness was not found.");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;
        if (configuration is not ("Release" or "Debug"))
            throw new InvalidOperationException("The FTP test configuration could not be determined.");
        var dll = Path.Combine(path.FullName, "tests", "FileCat.RemoteCrashHarness", "bin", configuration, "net10.0",
            "FileCat.RemoteCrashHarness.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build the isolated FTP crash harness with the test project.", dll);
        return dll;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FTP_replacement_refuses_a_target_changed_while_its_stage_is_uploading(bool retainSizeAndTime, bool encrypted)
    {
        using var server = TestFtpServer.TryStart(encrypted ? TestFtpServer.SelfSigned("FileCat replacement test").Pem : null);
        if (server is null) Assert.Skip("No FTP/FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(encrypted ? RemoteProtocols.FtpExplicitTls : RemoteProtocols.Ftp);
        profile.PlainTextAccepted = !encrypted;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
            new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_dir, "replace-known-hosts")), new SessionSecretStore(),
            new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce },
            new CertificateTrust(Path.Combine(_dir, "replace-trusted-certificates")));
        var remote = new SftpProvider(connections, () => [profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(remote);
        SftpJobs.Register();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir, "replace-journal"));
        var source = Path.Combine(_dir, "replace.bin");
        var target = Path.Combine(server.Root, "replace.bin");
        var bytes = new byte[1024 * 1024];
        new Random(15).NextBytes(bytes);
        File.WriteAllBytes(source, bytes);
        File.WriteAllText(target, "previous version");
        var originalTime = File.GetLastWriteTimeUtc(target);
        var originalHash = SHA256.HashData(bytes);
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, RateLimit = 256 * 1024 },
        });
        try
        {
            string? staged = null;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20) && staged is null && !job.State.IsFinished())
            {
                staged = Directory.GetFiles(server.Root, $"{JournalRecovery.StagedPrefix}*")
                    .FirstOrDefault(path => new FileInfo(path).Length is > 0 and < 1024 * 1024);
                if (staged is null) await Task.Delay(20, ct);
            }
            Assert.True(staged is not null, $"{job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
            Assert.Equal(JobState.Running, job.State);
            if (retainSizeAndTime)
            {
                byte[] changed = File.ReadAllBytes(target);
                changed[0] ^= 0x20;
                File.WriteAllBytes(target, changed);
                File.SetLastWriteTimeUtc(target, originalTime);
                Assert.Equal(changed.Length, new FileInfo(target).Length);
                Assert.Equal(originalTime, File.GetLastWriteTimeUtc(target));
            }
            else
            {
                File.WriteAllText(target, "newer version from another client");
                File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(2));
            }
            var changedHash = SHA256.HashData(File.ReadAllBytes(target));
            deadline.Restart();
            while (!job.State.IsFinished() && deadline.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(20, ct);
            Assert.Equal(JobState.Failed, job.State);
            Assert.Equal(changedHash, SHA256.HashData(File.ReadAllBytes(target)));
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
            Assert.Equal(["replace.bin"], Directory.GetFiles(server.Root).Select(Path.GetFileName));
            Assert.Contains(job.Issues, i => i.Message.Contains(retainSizeAndTime
                ? "destination content changed on the server" : "destination changed on the server", StringComparison.Ordinal));
        }
        finally
        {
            if (!job.State.IsFinished()) job.Cancel();
        }
    }

    [Theory]
    // Release issue I45: how precisely each listing line states its time.
    [InlineData("type=file;size=1001;modify=20261001050906;perm=adfrw; f1.bin", 0)]
    [InlineData("-rw-r--r--    1 1001     1001         1001 Oct 01 05:09 f1.bin", 60)]
    [InlineData("-rw-r--r--    1 1001     1001         1001 Mar 04  2021 note 00.bin", 86400)]
    [InlineData("lrwxrwxrwx    1 ftp      ftp            10 Jan 15  2019 link -> target.txt", 86400)]
    [InlineData("10-01-26  05:09AM                 1001 f1.bin", 60)]
    [InlineData(null, 0)]
    public void A_listing_line_states_its_time_to_the_second_minute_or_day(string? line, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), FtpChannel.TimePrecision(line));

    [Fact]
    public void A_servers_reply_is_quoted_in_its_own_words()
    {
        // FluentFTP's message repeats the code first; FileCat names the code once, after the server's words.
        var ex = new FluentFTP.Exceptions.FtpCommandException("451", "x.bin: Append/Restart not permitted, try again");
        Assert.Equal("x.bin: Append/Restart not permitted, try again", FtpChannel.ReplyText(ex));
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

    [Fact]
    public void A_dropped_encrypted_connection_is_described_by_what_happened()
    {
        // What .NET's TLS stream threw when the lab's FTPS server dropped an upload (E-V08-L1): its own words only point
        // further down, and the user saw them.
        var dropped = new IOException("The read operation failed, see inner exception.",
            new IOException("Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.",
                new System.Net.Sockets.SocketException(10054)));
        Assert.Equal("Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.", RemoteErrorText.Reason(dropped));
        Assert.Equal("The server refused.", RemoteErrorText.Reason(new IOException("The server refused.", new IOException("inner"))));
        Assert.Equal("see inner exception", RemoteErrorText.Reason(new IOException("see inner exception")));
    }

    [Theory]
    [InlineData("/dir/a\rb")]
    [InlineData("/dir/a\nb")]
    [InlineData("/dir/a\0b")]
    [InlineData("/dir/back\\slash")]
    [InlineData("/dir/ends with a space ")]
    [InlineData(" relative path")]
    public void Paths_FTP_cannot_carry_exactly_are_refused_before_anything_is_sent(string path) =>
        Assert.ThrowsAny<IOException>(() => FtpChannel.Safe(path));

    [Theory]
    [InlineData("/dir/semi;colon")]
    [InlineData("/dir/100%")]
    [InlineData("/dir/..leading dots")]
    [InlineData("/dir/gpj\u202Eexe.txt")]
    [InlineData("/dir/tab\tx")]
    [InlineData("/dir/ leading space")]
    [InlineData("/ spaced folder /file")]
    public void Names_FTP_can_carry_pass_unchanged(string path) => Assert.Equal(path, FtpChannel.Safe(path));

    [Theory]
    // Release issue I36: vsftpd's listing (no MLSD); FluentFTP's parser gave the names trimmed.
    [InlineData("-rw-------    1 1001     1001           14 Oct 01 01:40  leading space", "leading space", null, " leading space")]
    [InlineData("-rw-------    1 1001     1001           13 Oct 01 01:40   two leading", "two leading", null, "  two leading")]
    [InlineData("drwx------    2 1001     1001         4096 Oct 01 01:40  spaced folder", "spaced folder", null, " spaced folder")]
    [InlineData("-rw-r--r--    1 1001     1001           14 Jan 05  2024  old file", "old file", null, " old file")]
    [InlineData("lrwxrwxrwx    1 1001     1001            5 Oct 01 01:40  link with spaces  -> plain", "link with spaces", "plain", " link with spaces ")]
    // Names the parser already gets right stay as they are, even ones that look like listing fields.
    [InlineData("-rw-r--r--    1 1001     1001           14 Oct 01 01:40 two  inner", "two  inner", null, "two  inner")]
    [InlineData("-rw-r--r--    1 1001     1001           14 Oct 01 01:40 a -> b", "a -> b", null, "a -> b")]
    [InlineData("-rw-r--r--    1 1001     1001           14 Jan 05  2024 2024 report", "2024 report", null, "2024 report")]
    [InlineData("-rw-r--r--    1 owner           14 Oct 01 01:40  no group column", "no group column", null, " no group column")]
    // A line of another shape, or one that disagrees with the parser beyond spaces, leaves the parser's name.
    [InlineData("10-01-26  01:40AM                   14  dos listing", "dos listing", null, "dos listing")]
    [InlineData("-rw-r--r--    1 1001     1001           14 Oct 01 01:40 other", "different", null, "different")]
    [InlineData(null, "machine listing", null, "machine listing")]
    public void Listed_names_keep_the_spaces_at_their_edges(string? line, string parsed, string? linkTarget, string expected) =>
        Assert.Equal(expected, FtpChannel.ExactName(parsed, line, linkTarget));
}
