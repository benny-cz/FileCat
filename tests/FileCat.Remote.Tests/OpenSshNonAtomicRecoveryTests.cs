using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Exercise the non-atomic job path through a real OpenSSH transport with bounded, test-owned faults.</summary>
public sealed class OpenSshNonAtomicRecoveryTests
{
    [Theory]
    [InlineData("late-content")]
    [InlineData("backup")]
    [InlineData("publish")]
    [InlineData("cleanup")]
    public async Task Real_SFTP_transport_keeps_the_right_versions_and_recovery_after_uncertainty(string phase)
    {
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No user-mode OpenSSH sshd is available on this platform.");
        string state = Directory.CreateTempSubdirectory("filecat-openssh-recovery-").FullName;
        try
        {
            string target = Path.Combine(sshd.Files, "owned.bin");
            string source = Path.Combine(state, "incoming.bin");
            File.WriteAllText(target, "base");
            File.WriteAllText(source, "mine edited bytes");
            byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
            var fault = new Fault(phase, target);
            var profile = sshd.Profile;
            var ui = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
            using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
                new NonAtomicConnector(fault), new HostKeyTrust(Path.Combine(state, "known_hosts")),
                new SessionSecretStore(), ui);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            providers.Register(new SftpProvider(connections, () => [profile], p => p));
            SftpJobs.Register();
            string journal = Path.Combine(state, "journal");
            var jobs = new JobManager(new PortableFileOperations(), providers, journal);
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Move,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = SftpProvider.At(profile, sshd.Files), NewName = "owned.bin",
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30),
                    $"State={job.State}; Issues={string.Join("; ", job.Issues.Select(i => i.Message))}");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(fault.Fired, $"The owned {phase} fault did not reach the real SFTP transport.");
            var recoveries = JournalRecovery.Scan(journal);
            Assert.True(recoveries.Count == 1,
                $"State={job.State}; Issues={string.Join("; ", job.Issues.Select(i => i.Message))}");
            var intent = Assert.Single(recoveries[0].OpenIntents, i => i.Operation == "remote-replace-backup");
            string backup = intent.Target!;
            string stage = intent.Staged!;
            Assert.Equal(phase == "cleanup" ? JobState.CompletedWithIssues : JobState.Failed, job.State);
            if (phase == "cleanup") Assert.False(File.Exists(backup));
            else Assert.Equal(phase == "late-content" ? "evil" : "base", File.ReadAllText(backup));
            if (phase is "publish" or "cleanup")
            {
                Assert.Equal("mine edited bytes", File.ReadAllText(target));
                Assert.False(File.Exists(stage));
            }
            else
            {
                Assert.False(File.Exists(target));
                Assert.Equal("mine edited bytes", File.ReadAllText(stage));
            }
            if (phase == "cleanup") Assert.False(File.Exists(source));
            else Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(source)));
            Assert.Contains(job.Issues, issue => issue.Message.Contains(backup, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(state, recursive: true);
        }
    }

    private sealed class Fault(string phase, string target)
    {
        private int _fired;
        public string Phase => phase;
        public string Target => target;
        public bool Fired => Volatile.Read(ref _fired) != 0;
        public bool Fire() => Interlocked.Exchange(ref _fired, 1) == 0;
        public bool IsBackupPath(string path) => path.StartsWith(Path.GetDirectoryName(target) + "/.fc-", StringComparison.Ordinal);
    }

    private sealed class NonAtomicConnector(Fault fault) : ISftpConnector
    {
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
            => new NonAtomicChannel(new SshNetConnector().Connect(profile, context, ct), fault);
    }

    private sealed class NonAtomicChannel(ISftpChannel inner, Fault fault) : ISftpChannel
    {
        public bool IsConnected => inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        public bool AppendsOneRequestAtATime => inner.AppendsOneRequestAtATime;
        public bool RenamesLinksThemselves => inner.RenamesLinksThemselves;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct)
            => inner.List(path, ct).Select(entry => (IRemoteEntry)new Entry(entry, fault)).ToArray();
        public RemoteStat? Stat(string path) => inner.Stat(path);
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public Stream OpenRead(string path, long length) => inner.OpenRead(path, length);
        public Stream CreateNew(string path) => inner.CreateNew(path);
        public void UploadNew(Stream input, string path) => inner.UploadNew(input, path);
        public Stream OpenWriteAt(string path, long offset) => inner.OpenWriteAt(path, offset);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void Rename(string source, string target)
        {
            inner.Rename(source, target);
            if (fault.Phase == "publish" && target == fault.Target && fault.IsBackupPath(source) && fault.Fire())
                throw new IOException("The test server completed stage publication before the reply was lost.");
        }
        public bool TryReplace(string source, string target) => false;
        public void SetModified(string path, DateTime utc) => inner.SetModified(path, utc);
        public void Dispose() => inner.Dispose();
    }

    private sealed class Entry(IRemoteEntry inner, Fault fault) : IRemoteEntry
    {
        public string Name => inner.Name;
        public string FullPath => inner.FullPath;
        public bool IsDirectory => inner.IsDirectory;
        public bool IsLink => inner.IsLink;
        public long Size => inner.Size;
        public DateTime ModifiedUtc => inner.ModifiedUtc;
        public TimeSpan ModifiedPrecision => inner.ModifiedPrecision;
        public void MoveTo(string path)
        {
            bool backup = FullPath == fault.Target && fault.IsBackupPath(path);
            if (backup && fault.Phase == "late-content" && fault.Fire())
            {
                var stamp = File.GetLastWriteTimeUtc(fault.Target);
                File.WriteAllText(fault.Target, "evil");
                File.SetLastWriteTimeUtc(fault.Target, stamp);
            }
            inner.MoveTo(path);
            if (backup && fault.Phase == "backup" && fault.Fire())
                throw new IOException("The test server moved the old version before the reply was lost.");
        }
        public void Delete()
        {
            string path = FullPath;
            inner.Delete();
            if (fault.Phase == "cleanup" && fault.IsBackupPath(path) && File.Exists(fault.Target) && fault.Fire())
                throw new IOException("The test server deleted the backup before the reply was lost.");
        }
    }
}
