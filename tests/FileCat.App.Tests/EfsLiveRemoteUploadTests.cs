using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.App.Tests;

public sealed class EfsLiveRemoteUploadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_real_transport_only_receives_efs_content_after_approval(bool approve)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && !WindowsTokenAuthority.IsHighIntegrity &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires the normal-user Windows EFS fixture.");
        string? host = Lab("HOST"), user = Lab("USER"), protocol = Lab("PROTOCOL"), parent = Lab("ROOT");
        bool hasPort = int.TryParse(Lab("PORT"), out int port);
        if (host is null || user is null || protocol is null || parent is null || !hasPort)
            Assert.Skip("Set FILECAT_EFS_UPLOAD_LAB_HOST, USER, PROTOCOL, ROOT and PORT for an owned test server.");
        Assert.True(protocol is RemoteProtocols.Sftp or RemoteProtocols.Ftp or RemoteProtocols.FtpExplicitTls or RemoteProtocols.FtpImplicitTls);
        Assert.StartsWith("/", parent);
        Assert.InRange(port, 1, 65535);
        string? password = Lab("PASSWORD"), keyFile = Lab("KEY"), fingerprint = Lab("FINGERPRINT");
        Assert.True(password is not null || keyFile is not null);
        if (protocol != RemoteProtocols.Ftp) Assert.False(string.IsNullOrWhiteSpace(fingerprint));
        var profile = new RemoteProfile
        {
            Name = "Owned EFS transport fixture", Host = host, User = user, Port = port, Protocol = protocol,
            PlainTextAccepted = protocol == RemoteProtocols.Ftp, Temporary = true,
            Auth = keyFile is null ? RemoteAuth.Password : RemoteAuth.Key, KeyFile = keyFile,
        };
        string root = Directory.CreateTempSubdirectory("filecat-efs-live-upload-").FullName;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
                new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()), new HostKeyTrust(Path.Join(root, "known_hosts"), []),
                new SessionSecretStore(), new LabInteraction(password, fingerprint), new CertificateTrust(Path.Join(root, "certificates")));
            using var probe = connections.Lease(profile.Id, ct);
            string folder = parent.TrimEnd('/') + "/filecat-efs-" + Guid.NewGuid().ToString("N");
            probe.Channel.CreateDirectory(folder);
            try
            {
                var providers = new ProviderRegistry();
                providers.Register(new LocalFileSystemProvider());
                providers.Register(new SftpProvider(connections, () => [profile], p => p));
                SftpJobs.Register();
                var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Join(root, "journal"));
                string source = Path.Join(root, "protected.bin");
                EfsRemoteUploadTests.WriteSparseFile(source);
                File.Encrypt(source);
                Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.SparseFile));
                byte[] payload = File.ReadAllBytes(source);
                var job = jobs.Submit(new JobRequest
                {
                    Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                    Destination = SftpProvider.At(profile, folder), Options = new TransferOptions { Verify = VerifyMode.ReadBack },
                });
                int questions = 0;
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(45);
                    while (!job.State.IsFinished() || jobs.HasActiveWork)
                    {
                        Assert.True(DateTime.UtcNow < deadline, "The owned live transfer did not retire.");
                        if (job.Decision is { Task.IsCompleted: false } pending)
                        {
                            var question = Assert.IsType<ConfirmRequest>(pending.Request);
                            Assert.Equal("efs-remote-upload", question.ConfirmClass);
                            Assert.Equal(DecisionAction.Skip, question.InitialAction);
                            Assert.Contains(folder + "/protected.bin", question.Message, StringComparison.Ordinal);
                            Assert.Contains(folder + "/" + JournalRecovery.StagedPrefix, question.Message, StringComparison.Ordinal);
                            Assert.Contains("server copy may use more disk space", question.Message, StringComparison.Ordinal);
                            Assert.Empty(probe.Channel.List(folder, ct));
                            questions++;
                            pending.Resolve(new Decision(approve ? DecisionAction.Proceed : DecisionAction.Skip));
                        }
                        await Task.Delay(10, ct);
                    }
                }
                finally { if (!job.State.IsFinished()) job.Cancel(); }
                Assert.Equal(1, questions);
                Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
                var published = probe.Channel.List(folder, ct);
                Assert.Equal(approve, job.Issues.Any(issue => issue.Outcome == StepOutcome.Committed &&
                    issue.Message.Contains("server copy may use more disk space", StringComparison.Ordinal)));
                if (approve)
                {
                    Assert.Equal("protected.bin", Assert.Single(published).Name);
                    using var accepted = probe.Channel.OpenRead(folder + "/protected.bin");
                    using var copy = new MemoryStream();
                    accepted.CopyTo(copy);
                    Assert.Equal(payload, copy.ToArray());
                    Assert.Contains(job.Issues, issue => issue.Message.Contains("Uploaded without Windows EFS", StringComparison.Ordinal));
                }
                else Assert.Empty(published);
            }
            finally
            {
                foreach (var entry in probe.Channel.List(folder, ct)) entry.Delete();
                probe.Channel.List(parent, ct).Single(entry => entry.Name == folder[(folder.LastIndexOf('/') + 1)..]).Delete();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string? Lab(string setting) => Environment.GetEnvironmentVariable("FILECAT_EFS_UPLOAD_LAB_" + setting);

    private sealed class LabInteraction(string? password, string? fingerprint) : IRemoteInteraction
    {
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => password is null ? null : new(password, false);
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts)
            => password is null ? null : prompts.Select(_ => password).ToArray();
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) =>
            string.Equals(check.Fingerprint, fingerprint, StringComparison.Ordinal) ? HostKeyDecision.AcceptOnce : HostKeyDecision.Reject;
        public HostKeyDecision DecideCertificate(RemoteProfile profile, CertificateCheck check) =>
            string.Equals(check.Certificate.Sha256, fingerprint, StringComparison.OrdinalIgnoreCase) ? HostKeyDecision.AcceptOnce : HostKeyDecision.Reject;
    }
}
