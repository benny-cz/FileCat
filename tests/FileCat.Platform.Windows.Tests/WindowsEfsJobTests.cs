using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

[Collection("Windows storage job evidence")]
public sealed partial class WindowsEfsJobTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Another_users_file_with_allowed_ntfs_access_cannot_be_decrypted_without_an_efs_key()
    {
        RequireEfs();
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_NO_KEY_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires the disposable two-user NTFS fixture.");
        using var certificates = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        certificates.Open(OpenFlags.ReadOnly);
        AssertNoEfsCertificate();
        string source = Path.Join(root, "other-user.bin");
        Assert.Equal("matching plain ACL control", File.ReadAllText(Path.Join(root, "plain-control.bin")));
        Assert.True(IsEncrypted(source));
        Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(source));

        string privateRoot = Directory.CreateTempSubdirectory("filecat-efs-no-key-").FullName;
        try
        {
            var manager = Manager(privateRoot, new WindowsStorageTransforms().CreateEfsJobCandidate());
            int keyPrompts = 0;
            manager.DecisionRequested += pending => { keyPrompts++; pending.Resolve(new(DecisionAction.CancelJob)); };
            var job = Run(manager, StorageAction.DecryptEfs, [ItemRef.ForFileSystemPath(source, EntryKind.File)]);
            var result = Assert.IsType<StorageResultSnapshot>(job.StorageResult);
            string details = string.Join(", ", result.Reasons.Select(reason => reason.Problem.Code + " " + reason.Problem.Message));
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Equal(0, keyPrompts);
            Assert.Equal(0, result.Counts.Changed);
            Assert.Equal(0, result.Counts.Uncertain);
            Assert.False(result.ReviewPending);
            Assert.True(result.Reasons.Any(reason => reason.Problem is { Kind: StorageReasonKind.PermissionOrPolicy, Code: "efs-guard-open" }), details);
            Assert.Empty(Evidence(manager, job).Calls);
            Assert.True(History(manager, job).Ended);
            Assert.True(IsEncrypted(source));
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(source));
            AssertNoEfsCertificate();
            TestContext.Current.TestOutputHelper!.WriteLine($"{job.State}: {details}");
        }
        finally { Directory.Delete(privateRoot, recursive: true); }

        void AssertNoEfsCertificate() => Assert.DoesNotContain(certificates.Certificates,
            certificate => certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                .Any(usage => usage.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(oid => oid.Value == "1.3.6.1.4.1.311.10.3.4")));
    }

    [Fact]
    public void A_restored_fixture_key_can_decrypt_the_same_file_under_an_independent_windows_account()
    {
        RequireEfs();
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_RESTORED_KEY_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires the isolated backup and restore fixture.");
        using var identity = WindowsIdentity.GetCurrent();
        Assert.NotEqual(File.ReadAllText(Path.Join(root, "owner-sid.txt")).Trim(), identity.User!.Value);
        string hash = File.ReadAllText(Path.Join(root, "thumbprint.txt")).Trim();
        using var certificates = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        certificates.Open(OpenFlags.ReadOnly);
        using var exactKey = Assert.Single(certificates.Certificates.Find(X509FindType.FindByThumbprint, hash, validOnly: false));
        Assert.True(exactKey.HasPrivateKey);

        string source = Path.Join(root, "recovery-source.bin");
        Assert.True(IsEncrypted(source));
        Assert.Equal("synthetic recovery payload", File.ReadAllText(source));
        Assert.Equal("synthetic recovery stream", File.ReadAllText(source + ":fixture"));
        var operations = new WindowsFileOperations();
        string originalIdentity = Assert.IsType<string>(operations.GetFileIdentity(source));

        string privateRoot = Directory.CreateTempSubdirectory("filecat-efs-recovered-key-").FullName;
        try
        {
            var manager = Manager(privateRoot, new WindowsStorageTransforms().CreateEfsJobCandidate());
            int keyPrompts = 0;
            manager.DecisionRequested += pending => { keyPrompts++; pending.Resolve(new(DecisionAction.CancelJob)); };
            var job = Run(manager, StorageAction.DecryptEfs, [ItemRef.ForFileSystemPath(source, EntryKind.File)]);
            AssertCompleted(job, changed: 1, already: 0);
            Assert.Equal(0, keyPrompts);
            var call = Assert.Single(Evidence(manager, job).Calls);
            Assert.Equal(StorageCallDisposition.Observed, call.Disposition);
            Assert.Null(call.EfsReviewedKey);
            Assert.True(call.Result!.NativeCallAttempted);
            Assert.Equal(StorageObservationState.Enabled, call.Result.Before.Efs);
            Assert.Equal(StorageObservationState.Disabled, call.Result.After!.Efs);
            Assert.True(History(manager, job).Ended);
            Assert.False(IsEncrypted(source));
            Assert.Equal(originalIdentity, operations.GetFileIdentity(source));
            Assert.Equal("synthetic recovery payload", File.ReadAllText(source));
            Assert.Equal("synthetic recovery stream", File.ReadAllText(source + ":fixture"));
        }
        finally { Directory.Delete(privateRoot, recursive: true); }
    }

    [Fact]
    public void Backup_review_lists_only_selected_files_and_rechecks_their_exact_identities()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-selected-backup-").FullName;
        try
        {
            string first = Write(root, "first.bin"), second = Write(root, "second.bin"), clear = Write(root, "clear.bin");
            string folder = Directory.CreateDirectory(Path.Join(root, "unscanned")).FullName;
            string unselected = Write(folder, "unselected.bin");
            File.Encrypt(first); File.Encrypt(second); File.Encrypt(unselected);
            ItemRef Select(string path) => ItemRef.ForFileSystemPath(path, EntryKind.File);
            var manager = Manager(root, new WindowsStorageTransforms().CreateEfsJobCandidate());
            StorageEfsFileBackupReview reviewed;
            string hash;
            using (StorageReviewResources.EnterNative(Ct))
            {
                reviewed = manager.PrepareEfsFileBackup([Select(first), Select(second), Select(clear),
                    ItemRef.ForFileSystemPath(folder, EntryKind.Directory)], Ct);
                var key = Assert.Single(reviewed.Keys);
                Assert.Equal(StorageEfsKeyState.MatchedAvailable, key.State);
                hash = key.CertificateSha1;
                Assert.Equal(4, reviewed.Items.Length);
                Assert.Equal(hash, Assert.Single(reviewed.Items[0].Certificates));
                Assert.Equal(hash, Assert.Single(reviewed.Items[1].Certificates));
                Assert.NotEqual(reviewed.Items[0].Identity, reviewed.Items[1].Identity);
                Assert.Equal("efs-backup-encrypted-file", reviewed.Items[2].Problem?.Code);
                Assert.Equal("efs-backup-file-only", reviewed.Items[3].Problem?.Code);
                Assert.Empty(reviewed.Items[3].Certificates);

                var unowned = manager.BackUpEfsFileKey(reviewed, hash, ownerWindow: 0, Ct);
                Assert.False(unowned.WindowsReportedExport);
                Assert.Equal("efs-backup-owner", unowned.Problem?.Code);
                string outside = new(hash[0] == 'A' ? 'B' : 'A', 40);
                var refused = Assert.Throws<StorageInspectionException>(() => manager.BackUpEfsFileKey(reviewed, outside, ownerWindow: 0, Ct));
                Assert.Equal("efs-backup-reviewed-key", refused.Problem.Code);
            }

            // The replacements carry the same user's key, but the original review may cover only the original IDs.
            foreach (string path in new[] { first, second })
            {
                File.Move(path, path + ".original");
                File.WriteAllText(path, "Replacement bytes");
                File.Encrypt(path);
            }
            using (StorageReviewResources.EnterNative(Ct))
            {
                var changed = manager.BackUpEfsFileKey(reviewed, hash, ownerWindow: 0, Ct);
                Assert.False(changed.WindowsReportedExport);
                Assert.Empty(changed.CoveredItems);
                Assert.Equal("efs-backup-association-changed", changed.Problem?.Code);
            }
            Assert.Equal("Synthetic user content", File.ReadAllText(first + ".original"));
            Assert.Equal("Replacement bytes", File.ReadAllText(first));
            Assert.True(IsEncrypted(first));
            Assert.True(IsEncrypted(unselected));
            Assert.False(IsEncrypted(clear));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Files_encrypted_with_distinct_real_Efs_keys_keep_independent_backup_choices()
    {
        RequireEfs();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_BACKUP_MULTIPLE_KEYS") == "1" &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_ROTATE_KEY") == "1",
            "Requires a disposable account that allows changing its default EFS certificate.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-backup-distinct-keys-").FullName;
        try
        {
            string first = Write(root, "before-rotation.bin"), second = Write(root, "after-rotation.bin");
            File.Encrypt(first);
            var manager = Manager(root, new WindowsStorageTransforms().CreateEfsJobCandidate());
            var items = new[] { ItemRef.ForFileSystemPath(first, EntryKind.File), ItemRef.ForFileSystemPath(second, EntryKind.File) };
            string originalKey;
            using (StorageReviewResources.EnterNative(Ct))
                originalKey = Assert.Single(manager.PrepareEfsFileBackup([items[0]], Ct).Keys).CertificateSha1;

            WindowsEfsSetupTests.RotateDisposableDefaultKey();
            File.Encrypt(second);
            using (StorageReviewResources.EnterNative(Ct))
            {
                var review = manager.PrepareEfsFileBackup(items, Ct);
                Assert.Equal(2, review.Keys.Length);
                Assert.All(review.Keys, key => Assert.Equal(StorageEfsKeyState.MatchedAvailable, key.State));
                Assert.Equal(originalKey, Assert.Single(review.Items[0].Certificates));
                Assert.NotEqual(originalKey, Assert.Single(review.Items[1].Certificates));
                Assert.All(review.Items, item => Assert.Null(item.Problem));
            }
            Assert.True(IsEncrypted(first) && IsEncrypted(second));
            Assert.Equal("Synthetic user content", File.ReadAllText(first));
            Assert.Equal("Synthetic user content", File.ReadAllText(second));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Efs_round_trips_on_a_prepared_64_KiB_NTFS_volume_without_compression_support()
    {
        RequireEfs();
        string? canary = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_EFS_FRESH_64K_TARGET");
        if (string.IsNullOrWhiteSpace(canary)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_EFS_FRESH_64K_TARGET to a file on the newly formatted 64 KiB NTFS volume.");
        Assert.True(File.Exists(canary));
        string root = Directory.CreateTempSubdirectory("filecat-efs-large-cluster-").FullName;
        string scratch = Path.Join(Path.GetDirectoryName(canary), "filecat-efs-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string target = Write(scratch, "selected.bin");
            File.WriteAllText(target + ":fixture", "Named stream on the 64 KiB volume");
            using (var handle = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                TestContext.Current.TestOutputHelper?.WriteLine("Initial WOF observation: " + WindowsStorageBacking.Query(handle, Ct));
            var item = ItemRef.ForFileSystemPath(target, EntryKind.File);
            var adapter = new WindowsStorageTransforms();
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, ct) =>
            {
                var setup = adapter.InspectEfsSetup(sid, scope, true, ct);
                Assert.Null(setup.Problem);
                return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup,
                    Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1, acknowledged: true);
            }));

            using (var compression = manager.PrepareStorageReview([item], StorageAction.Compress, Ct))
                Assert.Equal("allocation-unit", Assert.Single(compression.Roots).Observation.Ineligible?.Code);
            using (var review = manager.PrepareStorageReview([item], StorageAction.EncryptEfs, Ct))
            {
                var reason = Assert.Single(review.Roots).Observation.Ineligible;
                Assert.True(reason is null, $"{reason?.Code}: {reason?.Message}");
            }
            var encrypted = Run(manager, StorageAction.EncryptEfs, [item]);
            Assert.True(encrypted.State == JobState.Completed, JobIssue.Summarize(encrypted.Issues));
            AssertCompleted(encrypted, changed: 1, already: 0);
            Assert.True(IsEncrypted(target));
            var decrypted = Run(manager, StorageAction.DecryptEfs, [item]);
            Assert.True(decrypted.State == JobState.Completed, JobIssue.Summarize(decrypted.Issues));
            AssertCompleted(decrypted, changed: 1, already: 0);
            Assert.False(IsEncrypted(target));
            Assert.Equal("Synthetic user content", File.ReadAllText(target));
            Assert.Equal("Named stream on the 64 KiB volume", File.ReadAllText(target + ":fixture"));
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Efs_refuses_a_prepared_Wof_backed_file()
    {
        RequireEfs();
        string? target = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_WOF_TARGET");
        if (string.IsNullOrWhiteSpace(target)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_WOF_TARGET to an owned WOF-backed file.");
        Assert.True(File.Exists(target));
        string root = Directory.CreateTempSubdirectory("filecat-efs-wof-").FullName;
        try
        {
            using (var handle = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(WindowsStorageBacking.Query(handle, Ct).External);
            var manager = Manager(root, new WindowsStorageTransforms().CreateEfsJobCandidate());
            using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(target, EntryKind.File)], StorageAction.EncryptEfs, Ct);
            Assert.Equal("external-backing", Assert.Single(review.Roots).Observation.Ineligible?.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Selected_Efs_jobs_share_one_lazy_key_choice_and_record_native_effects_on_disk()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
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
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64);
            int choices = 0;
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, token) =>
            {
                choices++;
                var setup = adapter.InspectEfsSetup(sid, scope, true, token);
                Assert.Null(setup.Problem);
                string hash = Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1;
                return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup, hash, acknowledged: true);
            }), qualifyNativeArm64);

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
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
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
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64);
            int choices = 0;
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, token) =>
            {
                choices++;
                var setup = adapter.InspectEfsSetup(sid, scope, true, token);
                Assert.Null(setup.Problem);
                return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup,
                    Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1, acknowledged: true);
            }), qualifyNativeArm64);
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
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        string root = Directory.CreateTempSubdirectory("filecat-efs-job-cancel-").FullName;
        try
        {
            string first = Write(root, "first.txt"), second = Write(root, "second.txt");
            int choices = 0;
            var adapter = new WindowsStorageTransforms(qualifyNativeArm64: qualifyNativeArm64);
            var manager = Manager(root, adapter.CreateEfsJobCandidate((job, _, _, _) =>
            {
                choices++;
                _ = RequestTestSetup(job, first);
                return null;
            }), qualifyNativeArm64);
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

    private static void RequireEfs(bool qualifyNativeArm64 = false)
    {
        Assert.SkipUnless(WindowsStorageTransforms.SupportsHost(qualifyNativeArm64) && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    private static JobManager Manager(string path, IStorageTransforms adapter, bool qualifyNativeArm64 = false) => new(new PortableFileOperations(), new ProviderRegistry(),
        Directory.CreateDirectory(Path.Join(path, "journal")).FullName, ArchiveJobBackends.Empty, adapter,
        qualifyNativeArm64 ? WindowsStorageReviewEvidence.CreateArm64QualificationJournal : WindowsStorageReviewEvidence.CreateJournal,
        qualifyNativeArm64 ? WindowsStorageReviewEvidence.Arm64QualificationBackend : WindowsStorageReviewEvidence.Backend);

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
