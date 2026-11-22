using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

// G-REMOTE server configurations. The Ubuntu harness starts an OpenSSH server on a stack the plan names and passes it in
// FILECAT_TEST_SFTP_STACK as "port|login|client key|host public key|local start directory|server start directory|expected".
//   applied  the server's no-follow chmod works (fchmodat2, or /proc visible to sftp-server): a reviewed mode change verifies.
//   refused  it does not (glibc 2.31; glibc 2.35, or 2.39 without fchmodat2, in a chroot without /proc): the server's
//            FAILURE with an unchanged LSTAT reports that the server could not apply the mode change, and the session
//            then refuses mode reviews while inspection continues.
//   owner    the login is a UID-0 alias in another user's start directory: admission refuses the item.
public sealed partial class SftpPermissionAdmissionTests
{
    [Fact]
    public async Task OpenSsh_server_stack_reports_its_mode_support_and_owner_evidence()
    {
        string[] parts = (Environment.GetEnvironmentVariable("FILECAT_TEST_SFTP_STACK") ?? string.Empty).Split('|');
        int port = 0;
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess || parts.Length != 7 || !int.TryParse(parts[0], out port))
            Assert.Skip("Requires a task-owned OpenSSH server stack.");
        string login = parts[1], key = parts[2], hostKey = parts[3], local = parts[4], remote = parts[5], expected = parts[6];
        Assert.Contains(expected, new[] { "applied", "refused", "owner" });
        var profile = new RemoteProfile { Name = "server stack", Host = "127.0.0.1", Port = port, User = login, Auth = RemoteAuth.Key, KeyFile = key };
        string state = Directory.CreateTempSubdirectory("sftp-stack-").FullName;
        string first = Path.Combine(local, "first"), second = Path.Combine(local, "second");
        foreach (string path in new[] { first, second })
        {
            File.WriteAllText(path, Path.GetFileName(path));
            File.SetUnixFileMode(path, (UnixFileMode)0x180);
        }
        try
        {
            var trust = new HostKeyTrust(Path.Combine(state, "known_hosts"), []);
            trust.Remember(profile.Host, profile.Port, Convert.FromBase64String(File.ReadAllText(hostKey).Split(' ')[1]));
            using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new SshNetConnector(),
                trust, new SessionSecretStore(), new FtpInteraction());
            var provider = new SftpProvider(connections, () => [profile], known => known);
            PermissionSubject Subject(string name) => new(SftpProvider.At(profile, remote), name, remote + "/" + name,
                new(PermissionAuthorityKind.RemoteSession, profile.Id, 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            PermissionDraft draft = new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]);

            var firstSubject = Subject("first");
            var inspected = Assert.Single(provider.InspectSftpPermissions(firstSubject.OriginalLocation, [firstSubject], Cancellation));
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{expected}: mode {inspected.Mode.Status} '{inspected.Mode.Reason}', editor refusal '{inspected.Sftp?.ChangeRefusal}'");
            Assert.Equal((ushort)0x180, inspected.Mode.Value);
            if (expected == "owner")
            {
                var refusal = Assert.Throws<RefusedOperationException>(() => provider.PrepareSftpPermissionPathReview(firstSubject, Cancellation));
                TestContext.Current.TestOutputHelper?.WriteLine($"owner: {refusal.Message}");
                Assert.Equal("The server could not corroborate the leaf owner as the signed-in account.", refusal.Message);
                Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(first));
                return;
            }

            string history = Path.Combine(state, "history");
            Directory.CreateDirectory(history);
            var registry = new ProviderRegistry();
            registry.Register(provider);
            var manager = new JobManager(new PortableFileOperations(), registry, history, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, ct) => UnixAnalysisPrivateDirectory.CreatePermissionJournal(history, leaf, ct));
            using var operation = provider.PrepareSftpPermissionOperation(firstSubject,
                new ItemRef(firstSubject.OriginalLocation, "first", EntryKind.File), draft, Cancellation);
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.JobFinished += job => done.TrySetResult(job);
            var submitted = manager.SubmitPermissions(operation);
            Assert.Same(submitted, await done.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation));
            var result = Assert.Single(submitted.PermissionResults);
            var step = Assert.Single(result.Components);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{expected}: {result.Verdict}/{step.Verdict} {step.Verification} error {step.NativeError}: {step.Detail}");
            if (expected == "applied")
            {
                Assert.Equal(PermissionVerdict.Verified, result.Verdict);
                Assert.Equal(PermissionVerification.ServerPath, step.Verification);
                Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(first));
                return;
            }

            Assert.Equal(PermissionVerdict.FailedBeforeChange, result.Verdict);
            Assert.Equal(4, step.NativeError); // SSH_FX_FAILURE
            Assert.Equal("The server could not apply this mode change.", step.Detail);
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(first));
            var secondSubject = Subject("second");
            var later = Assert.Single(provider.InspectSftpPermissions(secondSubject.OriginalLocation, [secondSubject], Cancellation));
            Assert.Equal((ushort)0x180, later.Mode.Value);
            Assert.Equal("The server has refused mode changes in this session.", later.Sftp!.ModeChangeRefusal);
            var again = Assert.ThrowsAny<Exception>(() => provider.PrepareSftpPermissionOperation(secondSubject,
                new ItemRef(secondSubject.OriginalLocation, "second", EntryKind.File), draft, Cancellation));
            Assert.Contains("refused mode changes in this session", again.Message);
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(second));
        }
        finally
        {
            Directory.Delete(state, recursive: true);
            foreach (string path in new[] { first, second }) File.Delete(path);
        }
    }
}
