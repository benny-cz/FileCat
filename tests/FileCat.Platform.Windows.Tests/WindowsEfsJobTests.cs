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
                var selected = review.ApproveEfs(StorageAction.EncryptEfs, StorageScope.SelectedItems, new(true, true));
                Assert.Throws<ArgumentException>(() => manager.SubmitStorage(review, selected with { Scope = StorageScope.ExistingDescendants }, _ => { }));
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
    public void Recursive_Efs_uses_held_ancestry_for_overlaps_and_starts_setup_below_an_encrypted_root()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-recursive-").FullName;
        string? readOnly = null;
        try
        {
            string parent = Directory.CreateDirectory(Path.Join(root, "selected")).FullName;
            string nested = Directory.CreateDirectory(Path.Join(parent, "nested")).FullName;
            string child = Write(nested, "child.txt");
            File.WriteAllText(child + ":notes", "Synthetic named stream");
            readOnly = Write(nested, "read-only.txt");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            string outside = Directory.CreateDirectory(Path.Join(root, "outside")).FullName;
            string outsideFile = Write(outside, "untouched.txt");
            Directory.CreateSymbolicLink(Path.Join(parent, "redirect"), outside);
            var targets = new[] { ItemRef.ForFileSystemPath(child, EntryKind.File), ItemRef.ForFileSystemPath(parent, EntryKind.Directory),
                ItemRef.ForFileSystemPath(nested, EntryKind.Directory) };
            var adapter = new WindowsStorageTransforms();
            int choices = 0;
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, token) =>
            {
                choices++;
                var setup = adapter.InspectEfsSetup(sid, scope, true, token);
                Assert.Null(setup.Problem);
                return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup,
                    Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1, acknowledged: true);
            }));
            AssertCompleted(Run(manager, StorageAction.EncryptEfs, [targets[1]]), changed: 1, already: 0);
            Assert.True(IsEncrypted(parent));
            Assert.False(IsEncrypted(nested) || IsEncrypted(child));

            var encrypted = Run(manager, StorageAction.EncryptEfs, targets, StorageScope.ExistingDescendants);
            Assert.Equal(2, choices);
            Assert.Equal(JobState.CompletedWithIssues, encrypted.State);
            Assert.Equal(2, encrypted.StorageResult!.Counts.Changed);
            Assert.Equal(1, encrypted.StorageResult.Counts.AlreadySatisfied);
            Assert.Equal(2, encrypted.StorageResult.Counts.Skipped);
            Assert.True(IsEncrypted(nested) && IsEncrypted(child));
            Assert.False(IsEncrypted(readOnly) || IsEncrypted(outsideFile));
            Assert.Equal("Synthetic named stream", File.ReadAllText(child + ":notes"));
            var evidence = Evidence(manager, encrypted);
            Assert.Equal(new[] { nested, child }, evidence.Calls.Select(call => call.Path));
            Assert.All(evidence.Calls, call => Assert.Equal(1, call.SourceIndex));
            Assert.Contains(encrypted.StorageResult.Reasons, reason => reason.Problem.Code == "read-only-file");
            Assert.Contains(encrypted.StorageResult.Reasons, reason => reason.Problem.Code == "reparse-entry");

            var decrypted = Run(manager, StorageAction.DecryptEfs, targets, StorageScope.ExistingDescendants);
            Assert.Equal(3, decrypted.StorageResult!.Counts.Changed);
            Assert.Equal(2, choices);
            Assert.False(IsEncrypted(parent) || IsEncrypted(nested) || IsEncrypted(child));
            Assert.Equal("Synthetic user content", File.ReadAllText(child));
            Assert.Equal("Synthetic named stream", File.ReadAllText(child + ":notes"));
        }
        finally
        {
            if (readOnly is not null && File.Exists(readOnly)) File.SetAttributes(readOnly, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
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
            var manager = Manager(root, new WindowsStorageTransforms().CreateEfsJobCandidate((job, _, _, _) =>
            {
                choices++;
                _ = RequestTestSetup(job, first);
                return null;
            }));
            JobState? whileWaiting = null;
            manager.DecisionRequested += pending =>
            {
                whileWaiting = pending.Job.State;
                pending.Resolve(new(DecisionAction.CancelJob));
            };
            var job = Run(manager, StorageAction.EncryptEfs,
                [ItemRef.ForFileSystemPath(first, EntryKind.File), ItemRef.ForFileSystemPath(second, EntryKind.File)]);
            Assert.Equal(JobState.Canceled, job.State);
            Assert.Equal(JobState.AwaitingDecision, whileWaiting);
            Assert.Equal(1, choices);
            Assert.Equal(0, job.StorageResult!.Counts.Changed);
            Assert.Empty(Evidence(manager, job).Calls);
            Assert.False(IsEncrypted(first) || IsEncrypted(second));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_unexpected_real_key_stops_the_selected_job_and_retains_the_exact_call()
    {
        RequireEfs();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_ROTATE_KEY") == "1",
            "Use only a disposable account whose current EFS key may change.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-job-rotated-").FullName;
        try
        {
            string first = Write(root, "first.txt"), second = Write(root, "second.txt");
            var adapter = new WindowsStorageTransforms();
            var manager = Manager(root, adapter.CreateEfsJobCandidate((job, sid, scope, token) =>
            {
                if (RequestTestSetup(job, first).Action != DecisionAction.Proceed) return null;
                var setup = adapter.InspectEfsSetup(sid, scope, true, token);
                Assert.Null(setup.Problem);
                string hash = Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1;
                var approval = StorageEfsRecoveryApproval.DeclareArrangement(scope, setup, hash, acknowledged: true);
                WindowsEfsSetupTests.RotateDisposableDefaultKey();
                return approval;
            }));
            bool prompted = false;
            manager.DecisionRequested += pending =>
            {
                prompted = pending.Job.State == JobState.AwaitingDecision && !IsEncrypted(first) && !IsEncrypted(second);
                pending.Resolve(new(DecisionAction.Proceed));
            };
            var job = Run(manager, StorageAction.EncryptEfs,
                [ItemRef.ForFileSystemPath(first, EntryKind.File), ItemRef.ForFileSystemPath(second, EntryKind.File)]);
            Assert.True(prompted);
            Assert.True(job.StorageResult!.ReviewPending);
            Assert.Equal(1, job.StorageResult.Counts.Uncertain);
            Assert.True(IsEncrypted(first));
            Assert.False(IsEncrypted(second));
            var call = Assert.Single(Evidence(manager, job).Calls);
            Assert.Equal(first, call.Path);
            Assert.Equal(StorageEffect.Uncertain, call.Result!.Effect);
            Assert.Equal("efs-recovery-review-required", call.Result.Problem!.Code);
            Assert.NotEqual(call.EfsReviewedKey!.CertificateSha1, Assert.Single(call.Result.EfsKeys!.Keys).CertificateSha1);
            Assert.True(History(manager, job).EffectUncertain);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Default_recovery_workflow_requires_setup_and_an_explicit_recovery_choice_after_backup_failure()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-job-recovery-workflow-").FullName;
        try
        {
            string path = Write(root, "clear.txt");
            var manager = Manager(root, new WindowsStorageTransforms().CreateEfsJobCandidate());
            var requests = new List<DecisionRequest>();
            bool allWaitingAndClear = true;
            manager.DecisionRequested += pending =>
            {
                requests.Add(pending.Request);
                allWaitingAndClear &= pending.Job.State == JobState.AwaitingDecision && !IsEncrypted(path);
                Decision answer = pending.Request switch
                {
                    ConfirmRequest => new(DecisionAction.Proceed),
                    StorageEfsRecoveryDecision recovery when !recovery.Keys.IsDefaultOrEmpty =>
                        new(requests.Count == 2 ? DecisionAction.BackUpEfsKey : DecisionAction.DeclareEfsRecovery)
                        { EfsRecovery = new(recovery.Keys[0].CertificateSha1, 0) },
                    _ => new(DecisionAction.CancelJob),
                };
                pending.Resolve(answer);
            };

            var job = Run(manager, StorageAction.EncryptEfs, [ItemRef.ForFileSystemPath(path, EntryKind.File)]);
            AssertCompleted(job, changed: 1, already: 0);
            Assert.True(allWaitingAndClear);
            Assert.Equal(3, requests.Count);
            var setup = Assert.IsType<ConfirmRequest>(requests[0]);
            Assert.Equal(DecisionAction.CancelJob, setup.InitialAction);
            Assert.False(setup.OffersApplyToAll);
            var initial = Assert.IsType<StorageEfsRecoveryDecision>(requests[1]);
            var retry = Assert.IsType<StorageEfsRecoveryDecision>(requests[2]);
            Assert.Null(initial.PreviousResult);
            Assert.Contains("visible FileCat window", retry.PreviousResult);
            Assert.Equal(initial.Keys, retry.Keys);
            var recorded = Assert.Single(Evidence(manager, job).Calls);
            Assert.Equal(StorageEfsRecoveryChoice.DeclaredArrangement, recorded.EfsReviewedKey!.Choice);
            Assert.Equal(Assert.Single(initial.Keys).CertificateSha1, recorded.EfsReviewedKey.CertificateSha1);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Decision RequestTestSetup(Job job, string first) => job.Ask(new ConfirmRequest("Set up the EFS test key",
        "Use the disposable VM account's EFS setup fixture.", first, "efs-test-setup", [DecisionAction.Proceed, DecisionAction.CancelJob])
    { InitialAction = DecisionAction.CancelJob, OffersApplyToAll = false });

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

    private static Job Run(JobManager manager, StorageAction action, IReadOnlyList<ItemRef> targets, StorageScope scope = StorageScope.SelectedItems)
    {
        using var review = manager.PrepareStorageReview(targets, action, Ct);
        using var finished = new ManualResetEventSlim();
        void OnFinished(Job _) => finished.Set();
        manager.JobFinished += OnFinished;
        try
        {
            var approval = review.ApproveEfs(action, scope, new(true, action == StorageAction.EncryptEfs));
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
