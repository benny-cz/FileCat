using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsEfsJobTests
{
    [Fact]
    public void Detaching_after_native_Efs_encryption_retains_the_reviewed_key_and_uncertain_effect() =>
        DetachAfterEfsCall(StorageAction.EncryptEfs, "FILECAT_TEST_EFS_ENCRYPT_DEVICE_LOSS_CONTROL");

    [Fact]
    public void Detaching_after_native_Efs_decryption_retains_an_uncertain_effect_without_key_evidence() =>
        DetachAfterEfsCall(StorageAction.DecryptEfs, "FILECAT_TEST_EFS_DECRYPT_DEVICE_LOSS_CONTROL");

    private static void DetachAfterEfsCall(StorageAction action, string environmentVariable)
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        string? control = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(control)) Assert.Skip("Requires the isolated NTFS virtual disk device-loss fixture.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(90));

        bool encrypt = action == StorageAction.EncryptEfs;
        string target = File.ReadAllText(Path.Join(control, "target.txt")).Trim();
        var drive = new DriveInfo(Path.GetPathRoot(target)!);
        Assert.Equal("FCDeviceLoss", drive.VolumeLabel);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.InRange(drive.TotalSize, 32L << 20, 320L << 20);
        Assert.False(IsEncrypted(target));
        string stream = target + ":device-loss-marker:$DATA";
        File.WriteAllBytes(stream, [0x00, 0x9a, 0x7e, 0xff]);
        if (!encrypt) File.Encrypt(target);
        Assert.Equal(!encrypt, IsEncrypted(target));
        var files = new WindowsFileOperations();
        string identity = Assert.IsType<string>(files.GetFileIdentity(target));
        byte[] payload = SHA256.HashData(File.ReadAllBytes(target));
        byte[] streamBytes = File.ReadAllBytes(stream);
        var source = ItemRef.ForFileSystemPath(target, EntryKind.File);

        string evidence = Directory.CreateTempSubdirectory("filecat-efs-detach-").FullName;
        var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => evidence, qualifyNativeArm64: qualifyNativeArm64);
        int approvals = 0;
        var attempts = new List<(StorageAction Action, bool Succeeded)>();
        var candidate = adapter.CreateEfsJobCandidate(ApproveRecovery, (returnedAction, succeeded) =>
        {
            attempts.Add((returnedAction, succeeded));
            File.WriteAllText(Path.Join(control, "prepared"), "");
            WaitForFixture("detached");
        });
        try
        {
            var manager = Manager(evidence, candidate, qualifyNativeArm64);
            var pending = new List<InterruptedJob>();
            manager.StorageReviewRequired += pending.Add;
            var interrupted = Run(manager, action, [source]);
            var result = Assert.IsType<StorageResultSnapshot>(interrupted.StorageResult);
            Assert.True(attempts.Count == 1, $"{interrupted.State}: " + string.Join(", ",
                result.Reasons.Select(reason => reason.Problem.Code + " " + reason.Problem.Message)
                    .Concat(interrupted.Issues.Select(issue => issue.Message))));
            Assert.Equal((action, true), attempts[0]);
            Assert.Equal(JobState.Failed, interrupted.State);
            Assert.Equal(new StorageOutcomeCounts(Uncertain: 1), result.Counts);
            Assert.Equal(StorageCompleteness.Incomplete, Assert.Single(result.Roots).Completeness);
            Assert.True(result.ReviewPending);
            Assert.False(result.PersistenceFault);

            string name = StorageJournalPolicy.FileName(interrupted.CreatedUtc, interrupted.Id);
            Assert.Equal(name, Assert.Single(pending).StorageJournalLeaf);
            var call = Assert.Single(Evidence(manager, interrupted).Calls);
            Assert.Equal(StorageCallDisposition.Observed, call.Disposition);
            var observed = Assert.IsType<StorageMutationResult>(call.Result);
            Assert.True(observed.NativeCallAttempted);
            Assert.Equal(StorageEffect.Uncertain, observed.Effect);
            Assert.Equal("efs-postcondition-unreadable", observed.Problem?.Code);
            Assert.Null(observed.After);
            Assert.Null(observed.EfsKeys);
            Assert.Equal(encrypt, call.EfsReviewedKey is not null);
            Assert.Equal(encrypt ? 1 : 0, approvals);
            var reviewed = manager.ReadStorageReview(name, stop.Token);
            Assert.True(reviewed.ReviewPending);
            Assert.False(reviewed.Current.History.Ended);
            Assert.Equal(encrypt ? StorageJournalPolicy.EfsRecoveryVersion : StorageJournalPolicy.EfsVersion,
                reviewed.Current.History.JournalSchema);
            File.Copy(Path.Join(evidence, "journal", name), Path.Join(control, "interrupted.journal"));
            File.WriteAllText(Path.Join(control, "retired"), "");
            WaitForFixture("reattached");

            VerifyCanary(encrypt);
            int laterCalls = 0;
            var reopened = Manager(evidence, adapter.CreateEfsJobCandidate(ApproveRecovery, (_, _) => laterCalls++), qualifyNativeArm64);
            var historyCandidate = reopened.StorageHistoryCandidate(name);
            Assert.True(reopened.IsInterrupted(historyCandidate, stop.Token));
            var reopenedReview = reopened.ReadStorageReview(name, stop.Token);
            Assert.Equal(reviewed.Current.Evidence, reopenedReview.Current.Evidence);
            var acknowledged = reopened.AcknowledgeStorageReview(reopenedReview.Current, "Reviewed the EFS operation after device loss.",
                StorageReviewChoice.AcknowledgeReviewedHistory, stop.Token);
            var retained = reopened.ReadRetainedStorageReview(Assert.IsType<string>(acknowledged.RetainedAcknowledgmentLeaf), stop.Token);
            Assert.False(reopened.IsInterrupted(historyCandidate, stop.Token));
            Assert.True(retained.Report.Reviewed.History.EffectUncertain);
            var expectedConsent = Assert.IsType<StorageEfsConsent>(reviewed.Current.History.EfsConsent);
            var retainedConsent = Assert.IsType<StorageEfsConsent>(retained.Report.Reviewed.History.EfsConsent);
            Assert.Equal(expectedConsent with { RootIdentities = [] }, retainedConsent with { RootIdentities = [] });
            Assert.Equal<StorageObjectIdentity>(expectedConsent.RootIdentities, retainedConsent.RootIdentities);
            Assert.Equal(call.EfsReviewedKey, retained.Report.Reviewed.History.LastCall?.EfsReviewedKey);
            Assert.Empty(reopened.Jobs);
            Assert.Equal(0, laterCalls);
            if (encrypt)
            {
                using var native = StorageReviewResources.EnterNative(stop.Token);
                var association = reopened.PrepareEfsFileBackup([source], stop.Token);
                var key = Assert.Single(association.Keys);
                Assert.Equal(call.EfsReviewedKey!.CertificateSha1, key.CertificateSha1);
                Assert.Equal(StorageEfsKeyState.MatchedAvailable, key.State);
            }
            VerifyCanary(encrypt);

            var restored = Run(reopened, encrypt ? StorageAction.DecryptEfs : StorageAction.EncryptEfs, [source]);
            AssertCompleted(restored, changed: 1, already: 0);
            Assert.Equal(1, laterCalls);
            Assert.Equal(1, approvals);
            VerifyCanary(!encrypt);
            File.Copy(Path.Join(evidence, "journal", StorageJournalPolicy.FileName(restored.CreatedUtc, restored.Id)),
                Path.Join(control, "fresh-retry.journal"));
        }
        finally { Directory.Delete(evidence, recursive: true); }

        StorageEfsRecoveryApproval ApproveRecovery(Job job, string sid, StorageEfsConsent scope, CancellationToken ct)
        {
            approvals++;
            var setup = adapter.InspectEfsSetup(sid, scope, true, ct);
            Assert.Null(setup.Problem);
            return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup,
                Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1, acknowledged: true);
        }

        void VerifyCanary(bool encrypted)
        {
            Assert.Equal(encrypted, IsEncrypted(target));
            Assert.Equal(identity, files.GetFileIdentity(target));
            Assert.Equal(payload, SHA256.HashData(File.ReadAllBytes(target)));
            Assert.Equal(streamBytes, File.ReadAllBytes(stream));
        }

        void WaitForFixture(string marker)
        {
            while (!File.Exists(Path.Join(control, marker)))
            {
                stop.Token.ThrowIfCancellationRequested();
                if (File.Exists(Path.Join(control, "aborted"))) throw new IOException("The isolated device-loss fixture could not proceed.");
                stop.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
