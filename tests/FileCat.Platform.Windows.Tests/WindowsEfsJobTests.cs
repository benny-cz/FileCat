using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

[Collection("Windows storage job evidence")]
public sealed class WindowsEfsJobTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Selected_Efs_jobs_share_one_lazy_key_choice_and_record_native_effects_on_disk()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-job-").FullName;
        try
        {
            string already = Write(root, "already.txt");
            File.Encrypt(already);
            string clear = Write(root, "clear.txt");
            string selectedFolder = Directory.CreateDirectory(Path.Join(root, "default-folder")).FullName;
            string child = Write(selectedFolder, "existing-child.txt");
            File.WriteAllText(clear + ":fixture", "Named stream data");
            var items = new[] { ItemRef.ForFileSystemPath(already, EntryKind.File), ItemRef.ForFileSystemPath(clear, EntryKind.File),
                ItemRef.ForFileSystemPath(selectedFolder, EntryKind.Directory) };
            var adapter = new WindowsStorageTransforms();
            int choices = 0;
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, token) =>
            {
                choices++;
                var setup = adapter.InspectEfsSetup(sid, scope, true, token);
                Assert.Null(setup.Problem);
                string hash = Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1;
                return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup, hash, acknowledged: true);
            }));

            using (var review = manager.PrepareStorageReview(items, StorageAction.EncryptEfs, Ct))
            {
                Assert.Throws<ArgumentException>(() => review.ApproveEfs(StorageAction.EncryptEfs, StorageScope.SelectedItems, new(true, false)));
                Assert.Throws<NotSupportedException>(() => review.ApproveEfs(StorageAction.EncryptEfs, StorageScope.ExistingDescendants, new(true, true)));
                Assert.True(review.OwnsPins);
                Assert.Equal(0, choices);
            }
            var noop = Run(manager, StorageAction.EncryptEfs, [items[0]]);
            AssertCompleted(noop, changed: 0, already: 1);
            Assert.Empty(Evidence(manager, noop).Calls);
            Assert.Equal(0, choices);

            var encrypted = Run(manager, StorageAction.EncryptEfs, items);
            AssertCompleted(encrypted, changed: 2, already: 1);
            Assert.Equal(1, choices);
            Assert.True(IsEncrypted(already) && IsEncrypted(clear) && IsEncrypted(selectedFolder));
            Assert.False(IsEncrypted(child));
            Assert.Equal("Synthetic user content", File.ReadAllText(clear));
            Assert.Equal("Named stream data", File.ReadAllText(clear + ":fixture"));
            var evidence = Evidence(manager, encrypted);
            Assert.Equal([1, 2], evidence.Calls.Select(call => call.SourceIndex));
            Assert.All(evidence.Calls, call =>
            {
                Assert.NotNull(call.EfsReviewedKey);
                Assert.Equal(StorageEfsRecoveryChoice.DeclaredArrangement, call.EfsReviewedKey.Choice);
                Assert.Equal(call.EfsReviewedKey.CertificateSha1, Assert.Single(call.Result!.EfsKeys!.Keys).CertificateSha1);
                Assert.Equal(StorageCallDisposition.Observed, call.Disposition);
            });
            Assert.Equal(evidence.Calls[0].EfsReviewedKey, evidence.Calls[1].EfsReviewedKey);
            Assert.Equal(StorageJournalPolicy.EfsRecoveryVersion, History(manager, encrypted).JournalSchema);

            var decrypted = Run(manager, StorageAction.DecryptEfs, items);
            AssertCompleted(decrypted, changed: 3, already: 0);
            Assert.Equal(1, choices);
            Assert.False(IsEncrypted(already) || IsEncrypted(clear) || IsEncrypted(selectedFolder));
            Assert.Equal("Synthetic user content", File.ReadAllText(child));
            Assert.Equal("Named stream data", File.ReadAllText(clear + ":fixture"));
            Assert.All(Evidence(manager, decrypted).Calls, call => Assert.Null(call.EfsReviewedKey));
            Assert.Equal(StorageJournalPolicy.EfsVersion, History(manager, decrypted).JournalSchema);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Canceling_lazy_recovery_stops_before_any_native_intent()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-job-cancel-").FullName;
        try
        {
            string first = Write(root, "first.txt"), second = Write(root, "second.txt");
            int choices = 0;
            var manager = Manager(root, new WindowsStorageTransforms().CreateEfsJobCandidate((_, _, _, _) =>
            {
                choices++;
                return null;
            }));
            var job = Run(manager, StorageAction.EncryptEfs,
                [ItemRef.ForFileSystemPath(first, EntryKind.File), ItemRef.ForFileSystemPath(second, EntryKind.File)]);
            Assert.Equal(JobState.Canceled, job.State);
            Assert.Equal(1, choices);
            Assert.Equal(0, job.StorageResult!.Counts.Changed);
            Assert.Empty(Evidence(manager, job).Calls);
            Assert.False(IsEncrypted(first) || IsEncrypted(second));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void RequireEfs()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    private static JobManager Manager(string path, IStorageTransforms adapter) => new(new PortableFileOperations(), new ProviderRegistry(),
        Directory.CreateDirectory(Path.Join(path, "journal")).FullName, ArchiveJobBackends.Empty, adapter,
        WindowsStorageReviewEvidence.CreateJournal, WindowsStorageReviewEvidence.Backend);

    private static string Write(string root, string name)
    {
        string path = Path.Join(root, name);
        File.WriteAllText(path, "Synthetic user content");
        return path;
    }

    private static bool IsEncrypted(string path) => File.GetAttributes(path).HasFlag(FileAttributes.Encrypted);

    private static Job Run(JobManager manager, StorageAction action, IReadOnlyList<ItemRef> targets)
    {
        using var review = manager.PrepareStorageReview(targets, action, Ct);
        using var finished = new ManualResetEventSlim();
        void OnFinished(Job _) => finished.Set();
        manager.JobFinished += OnFinished;
        try
        {
            var approval = review.ApproveEfs(action, StorageScope.SelectedItems, new(true, action == StorageAction.EncryptEfs));
            var job = manager.SubmitStorage(review, approval, _ => { });
            Assert.True(finished.Wait(TimeSpan.FromSeconds(45), Ct));
            Assert.False(review.OwnsPins);
            return job;
        }
        finally { manager.JobFinished -= OnFinished; }
    }

    private static void AssertCompleted(Job job, int changed, int already)
    {
        Assert.True(job.State == JobState.Completed, $"{job.State}: " + string.Join(", ",
            job.StorageResult!.Reasons.Select(r => r.Problem.Message).Concat(job.Issues.Select(i => i.Message))));
        Assert.Equal(changed, job.StorageResult!.Counts.Changed);
        Assert.Equal(already, job.StorageResult.Counts.AlreadySatisfied);
    }

    private static StorageHistorySnapshot History(JobManager manager, Job job) =>
        manager.ReadStorageHistory(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), job.StorageResult!.DurablePrefix, Ct);

    private static StorageCallEvidencePage Evidence(JobManager manager, Job job) =>
        manager.ReadStorageCalls(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), job.StorageResult!.DurablePrefix!, ct: Ct);
}
