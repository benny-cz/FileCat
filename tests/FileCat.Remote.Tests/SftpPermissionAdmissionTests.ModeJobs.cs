using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Remote.Tests;

public sealed partial class SftpPermissionAdmissionTests
{
    [Theory]
    [InlineData("apply-file")]
    [InlineData("apply-directory")]
    [InlineData("reconnect-before-final")]
    [InlineData("drift-on-reconnect")]
    [InlineData("drift-after-intent")]
    [InlineData("journal-refusal")]
    public async Task The_SFTP_mode_job_flushes_intent_before_dispatch_and_records_server_path_results(string outcome)
    {
        using var fixture = new Fixture();
        string itemName = "owned witness";
        string path = Path.Combine(fixture.Server.Files, itemName);
        bool directory = outcome == "apply-directory";
        if (directory) Directory.CreateDirectory(path, OwnerDirectory);
        else File.WriteAllText(path, "witness");
        File.SetUnixFileMode(path, UnixFileMode.None);
        var modified = File.GetLastWriteTimeUtc(path);
        string history = Directory.CreateTempSubdirectory("sftp-permission-history-").FullName;
        try
        {
            var registry = new ProviderRegistry();
            registry.Register(fixture.Provider);
            var manager = new JobManager(new PortableFileOperations(), registry, history, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, ct) =>
                {
                    var file = UnixAnalysisPrivateDirectory.CreatePermissionJournal(history, leaf, ct);
                    bool disconnected = false;
                    return file with { Verify = () =>
                    {
                        file.Verify();
                        if (outcome is not ("drift-after-intent" or "journal-refusal" or "reconnect-before-final" or "drift-on-reconnect")) return;
                        byte[] prefix = new byte[4096];
                        int read = RandomAccess.Read(file.Stream.SafeFileHandle, prefix, 0);
                        string records = Encoding.UTF8.GetString(prefix, 0, read);
                        if (outcome is "reconnect-before-final" or "drift-on-reconnect")
                        {
                            if (!disconnected && records.Contains("\"verification\":\"ServerPath\"", StringComparison.Ordinal))
                            {
                                disconnected = true;
                                fixture.Connector.Last!.Dispose();
                                if (outcome == "drift-on-reconnect") File.SetUnixFileMode(path, UnixFileMode.None);
                            }
                            return;
                        }
                        if (!records.Contains("\"permission-intent\"", StringComparison.Ordinal)) return;
                        if (outcome == "drift-after-intent") File.SetUnixFileMode(path, UnixFileMode.UserRead);
                        else throw new IOException("Fixture refused the permissions journal after the intent flush.");
                    } };
                });
            var subject = fixture.Subject(fixture.Server.Files, itemName, directory ? PermissionObjectKind.Directory : PermissionObjectKind.File);
            var source = new ItemRef(subject.OriginalLocation, itemName, directory ? EntryKind.Directory : EntryKind.File);
            ushort wanted = (ushort)(directory ? OwnerDirectory : UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var operation = fixture.Provider.PrepareSftpPermissionOperation(subject, source,
                new(PermissionScope.Objects, [new ChangePermissionMode(wanted, 0xfff)]), Cancellation);
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.JobFinished += job => done.TrySetResult(job);
            var job = manager.SubmitPermissions(operation);
            Assert.Same(job, await done.Task.WaitAsync(TimeSpan.FromSeconds(20), Cancellation));
            Assert.True(job.PermissionUsesServerPath);
            var result = Assert.Single(job.PermissionResults);
            Assert.Null(result.Identity);
            string journalPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(history, PermissionJournalNames.Directory), "*.fcj"));
            string[] records = File.ReadAllLines(journalPath);
            Assert.Contains("\"serverPath\":true", records[0]);
            Assert.Contains("\"sources\":[]", records[0]);
            Assert.Contains(records, row => row.Contains("\"permission-intent\"", StringComparison.Ordinal));
            Assert.DoesNotContain(records, row => row.Contains(itemName, StringComparison.Ordinal));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
            if (outcome == "journal-refusal")
            {
                Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(path));
                Assert.Equal(PermissionVerdict.FailedBeforeChange, result.Verdict);
                Assert.DoesNotContain(records, row => row.Contains("\"permission-outcome\"", StringComparison.Ordinal));
                using var pending = File.OpenRead(journalPath);
                var receipt = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(pending, journalPath));
                Assert.True(receipt.PermissionReview!.ServerPath);
                Assert.Empty(receipt.Sources);
                Assert.False(receipt.CanContinueTransfer);
            }
            else if (outcome == "drift-on-reconnect")
            {
                Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(path));
                Assert.Equal(PermissionVerdict.Verified, Assert.Single(result.Components).Verdict);
                Assert.Equal(PermissionVerdict.Partial, result.Verdict);
                Assert.Equal(2, fixture.Connector.Connections);
                Assert.Single(records, row => row.Contains("\"permission-intent\"", StringComparison.Ordinal));
                using var pending = File.OpenRead(journalPath);
                var receipt = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(pending, journalPath));
                Assert.True(receipt.PermissionReview!.ServerPath);
                Assert.Equal(PermissionVerdict.Partial, receipt.PermissionReview.ObjectVerdicts[0]);
                Assert.Equal(PermissionVerdict.Verified, Assert.Single(receipt.PermissionReview.Calls).Outcome);
                Assert.Empty(receipt.Sources);
            }
            else
            {
                bool applied = outcome is "apply-file" or "apply-directory" or "reconnect-before-final";
                Assert.Equal(applied ? (UnixFileMode)wanted : UnixFileMode.UserRead, File.GetUnixFileMode(path));
                Assert.Equal(applied ? PermissionVerdict.Verified : PermissionVerdict.Conflict, result.Verdict);
                var step = Assert.Single(result.Components);
                Assert.Equal(applied ? PermissionVerification.ServerPath : PermissionVerification.NotAttempted, step.Verification);
                if (applied) Assert.Contains(records, row => row.Contains("\"verification\":\"ServerPath\"", StringComparison.Ordinal));
                if (outcome == "reconnect-before-final")
                {
                    Assert.Equal(2, fixture.Connector.Connections);
                    Assert.Contains("observed again after the connection was lost", result.Detail);
                    Assert.Single(records, row => row.Contains("\"permission-intent\"", StringComparison.Ordinal));
                }
            }
        }
        finally
        {
            if (directory) File.SetUnixFileMode(path, OwnerDirectory);
            Directory.Delete(history, recursive: true);
        }
    }
}
