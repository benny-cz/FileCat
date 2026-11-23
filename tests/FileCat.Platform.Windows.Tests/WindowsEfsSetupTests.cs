using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEfsSetupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sid { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; } }
    private static StorageEfsConsent Consent() => StorageEfsConsent.Capture(Guid.NewGuid(), StorageAction.EncryptEfs, StorageScope.SelectedItems,
        [new StorageObjectIdentity(1, new string('A', 32)) { VolumeBinding = "11111111-1111-1111-1111-111111111111" }], new(true, true));
    private static void NativeFixture(bool qualifyNativeArm64 = false)
    {
        Assert.SkipUnless(WindowsStorageTransforms.SupportsHost(qualifyNativeArm64) && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Explicit zero-byte EFS setup in a disposable normal-user Windows VM; Windows may create/enroll this user's EFS key.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }
    private static string Namespace()
    {
        string directory = Directory.CreateTempSubdirectory("filecat-efs-setup-").FullName;
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"O:{Sid}D:P(A;OICI;FA;;;{Sid})(A;OICI;FA;;;SY)", AccessControlSections.Owner | AccessControlSections.Access);
        new DirectoryInfo(directory).SetAccessControl(security);
        return directory;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_or_canceled_setup_records_a_retained_artifact_even_without_a_recovery_dialog(bool canceled)
    {
        var job = new Job(new JobRequest { Kind = JobKind.StorageTransform }, "EFS setup", "local", [], []);
        const string retained = @"C:\owned\FileCat.EfsSetup\setup-retained.tmp";
        var setup = new StorageEfsSetupResult(null, true, canceled,
            new(StorageReasonKind.Unknown, canceled ? "efs-setup-canceled" : "efs-setup-streams", "Setup stopped.", true),
            new(StorageReasonKind.Unknown, "efs-setup-cleanup", "The setup artifact could not be removed.", true),
            retained, new StorageObjectIdentity(7, "held-object"));
        WindowsStorageTransforms.RecordSetupCleanupIssue(job, setup);
        var issue = Assert.Single(job.ReadIssues(0, 1));
        Assert.Equal(retained, issue.Path);
        Assert.Equal("efs-setup-cleanup", issue.Cause);
        Assert.Equal(StepOutcome.PartiallyApplied, issue.Outcome);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("could not be removed", issue.Message);
        Assert.Contains("held-object", issue.Certainty);
        Assert.Equal(1, job.IssueTotals.Error);
    }

    [Fact]
    public void Setup_requires_explicit_consent_before_resolving_a_workspace_or_claiming_native_capacity()
    {
        int resolutions = 0;
        var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => { resolutions++; throw new InvalidOperationException(); });
        Assert.Equal("efs-setup-consent", Assert.Throws<StorageInspectionException>(() => adapter.InspectEfsSetup("S-1-5-21-1-2-3-1001", Consent(), false, Ct)).Problem.Code);
        Assert.Equal(0, resolutions);
    }

    [Fact]
    public void An_explicit_zero_byte_encrypted_setup_file_has_actual_current_user_associations_and_is_cleaned_by_its_handle()
    {
        NativeFixture(); string directory = Namespace(); string? observed = null;
        try
        {
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory);
            using var native = StorageReviewResources.EnterNative(Ct);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct, path =>
            {
                observed = path; Assert.True(File.Exists(path)); Assert.Equal(0, new FileInfo(path).Length);
                Assert.True((File.GetAttributes(path) & FileAttributes.Encrypted) != 0);
                Assert.Throws<IOException>(() => File.Move(path, path + ".moved"));
            });
            Assert.True(result.ProvisioningAttempted); Assert.False(result.Canceled); Assert.Null(result.Problem); Assert.Null(result.CleanupProblem);
            Assert.NotNull(result.Association); Assert.True(result.Association.HasMatchedCurrentUserKey);
            Assert.Contains(result.Association.Users, user => user.Sid == Sid);
            Assert.All(result.Association.CurrentUserKeys, key => Assert.Equal(StorageEfsKeyState.MatchedAvailable, key.State));
            Assert.Null(result.RetainedSetupPath); Assert.Null(result.RetainedSetupIdentity); Assert.NotNull(observed); Assert.False(File.Exists(observed));
            Assert.Empty(Directory.EnumerateFiles(Path.Join(directory, "FileCat.EfsSetup")));
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_ZERO_SETUP users={result.Association.Users.Length} matchedKeys={result.Association.CurrentUserKeys.Length} cleanup=True");
        }
        finally { Directory.Delete(directory, true); }
    }

    /// <summary>
    /// A fresh Windows without an active sync provider can have the Cloud Files filter loaded but attached to no drive.
    /// The setup namespace then follows the held-walk rule: it is admitted when no sync root of any account is registered
    /// on the drive. Runs only where the filter does not answer for the setup drive.
    /// </summary>
    [Fact]
    public void Setup_runs_on_a_drive_where_no_Cloud_Files_filter_answers_and_no_sync_root_is_registered()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        NativeFixture(arm64); string directory = Namespace();
        try
        {
            string probe = Path.Join(directory, "probe.txt");
            File.WriteAllText(probe, "probe");
            int query;
            using (var held = File.OpenHandle(probe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                query = WindowsStorageCloudQuery.Query(held, Ct);
            File.Delete(probe);
            Assert.SkipUnless(query == unchecked((int)0x80070001), $"The Cloud Files filter answers on this drive (0x{query:X8}); the attached case is covered elsewhere.");
            string volume = WindowsStorageSyncRoots.VolumeOf(directory) ?? throw new InvalidOperationException("The setup volume did not resolve.");
            Assert.DoesNotContain(volume, WindowsStorageSyncRoots.RegisteredRootVolumes() ?? throw new InvalidOperationException("Sync roots unreadable."));
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory, qualifyNativeArm64: arm64);
            using var native = StorageReviewResources.EnterNative(Ct);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct);
            Assert.True(result.ProvisioningAttempted); Assert.False(result.Canceled);
            Assert.Null(result.Problem); Assert.Null(result.CleanupProblem);
            Assert.NotNull(result.Association); Assert.True(result.Association.HasMatchedCurrentUserKey);
            Assert.Null(result.RetainedSetupPath);
            Assert.Empty(Directory.EnumerateFiles(Path.Join(directory, "FileCat.EfsSetup")));
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SETUP_NO_CLOUD_FILTER volume={volume} query=0x{query:X8} matched=True");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Cancel_after_native_creation_retains_keys_and_cleans_only_the_created_zero_byte_object()
    {
        NativeFixture(); string directory = Namespace(); string? observed = null;
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct); using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, stop.Token, path => { observed = path; stop.Cancel(); });
            Assert.True(result.ProvisioningAttempted); Assert.True(result.Canceled); Assert.NotNull(result.Problem); Assert.Null(result.CleanupProblem);
            Assert.Null(result.Association); Assert.Null(result.RetainedSetupPath); Assert.NotNull(observed); Assert.False(File.Exists(observed));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void A_native_metadata_failure_is_reported_after_retiring_the_owned_empty_setup_file()
    {
        NativeFixture(); string directory = Namespace(); string? created = null;
        try
        {
            using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct, path =>
            {
                created = path;
                throw new System.ComponentModel.Win32Exception(5);
            });
            Assert.Equal("efs-setup-failed", result.Problem?.Code); Assert.Null(result.Association); Assert.Null(result.CleanupProblem);
            Assert.Null(result.RetainedSetupPath); Assert.NotNull(created); Assert.False(File.Exists(created));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Added_stream_data_is_not_accepted_as_setup_or_removed_as_an_empty_artifact()
    {
        NativeFixture(); string directory = Namespace(); string? retained = null;
        try
        {
            using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct, path =>
            {
                retained = path;
                using var stream = new FileStream(path + ":unexpected", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                stream.Write("test-injected data"u8);
            });
            Assert.Equal("efs-setup-streams", result.Problem?.Code); Assert.Null(result.Association);
            Assert.Equal("efs-setup-cleanup", result.CleanupProblem?.Code); Assert.Equal(retained, result.RetainedSetupPath);
            Assert.NotNull(result.RetainedSetupIdentity); Assert.True(File.Exists(retained));
            Assert.Equal("test-injected data", File.ReadAllText(retained + ":unexpected"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void A_private_directory_permission_change_stops_setup_and_still_retires_the_owned_empty_file()
    {
        NativeFixture(); string directory = Namespace(); string? created = null;
        try
        {
            using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct, path =>
            {
                created = path;
                var info = new DirectoryInfo(Path.GetDirectoryName(path)!);
                var security = info.GetAccessControl(AccessControlSections.Access);
                security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, AccessControlType.Allow));
                info.SetAccessControl(security);
            });
            Assert.NotNull(result.Problem); Assert.Null(result.Association); Assert.Null(result.CleanupProblem);
            Assert.Null(result.RetainedSetupPath); Assert.NotNull(created); Assert.False(File.Exists(created));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void A_preexisting_application_namespace_with_other_writers_is_refused_before_key_provisioning()
    {
        NativeFixture(); string directory = Namespace();
        try
        {
            var info = new DirectoryInfo(directory); var security = info.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, AccessControlType.Allow));
            info.SetAccessControl(security);
            using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct);
            Assert.False(result.ProvisioningAttempted); Assert.NotNull(result.Problem); Assert.Null(result.Association); Assert.Null(result.CleanupProblem);
            Assert.False(Directory.Exists(Path.Join(directory, "FileCat.EfsSetup")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void First_use_in_a_deliberately_fresh_identity_creates_an_actual_key_only_after_explicit_setup()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        NativeFixture(qualifyNativeArm64);
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_FIRST_USE") == "1",
            "Requires a separately prepared fresh disposable account with no existing EFS certificate.");
        Assert.Equal(0, EfsCertificateCount());
        string directory = Namespace();
        try
        {
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory, qualifyNativeArm64: qualifyNativeArm64);
            Assert.Equal("efs-setup-consent", Assert.Throws<StorageInspectionException>(() => adapter.InspectEfsSetup(Sid, Consent(), false, Ct)).Problem.Code);
            Assert.Equal(0, EfsCertificateCount());
            using var native = StorageReviewResources.EnterNative(Ct);
            var result = adapter.InspectEfsSetup(Sid, Consent(), true, Ct);
            Assert.True(result.ProvisioningAttempted); Assert.False(result.Canceled); Assert.Null(result.Problem); Assert.Null(result.CleanupProblem);
            Assert.True(result.Association?.HasMatchedCurrentUserKey); Assert.Null(result.RetainedSetupPath);
            Assert.True(EfsCertificateCount() > 0);
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_FIRST_USE account={Sid} beforeEfsCertificates=0 afterEfsCertificates={EfsCertificateCount()} matchedKeys={result.Association!.CurrentUserKeys.Length} cleanup=True");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static int EfsCertificateCount()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        try { store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly); }
        catch (CryptographicException error) when (error.HResult == unchecked((int)0x80092004)) { return 0; }
        var certificates = store.Certificates;
        try { return certificates.Cast<X509Certificate2>().Count(cert => cert.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .Any(extension => extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.4.1.311.10.3.4"))); }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }

    [Fact]
    public void The_resulting_user_file_association_is_inspected_through_its_original_live_Efs_binding()
        => ReviewResultingAssociation(changeKey: false);

    [Fact]
    public void A_real_key_change_after_setup_requires_review_after_the_current_file_is_encrypted()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_ROTATE_KEY") == "1",
            "Use only a separately prepared disposable account whose current EFS key can be changed.");
        ReviewResultingAssociation(changeKey: true);
    }

    private static void ReviewResultingAssociation(bool changeKey)
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        NativeFixture(qualifyNativeArm64);
        string directory = Namespace(); string path = Path.Join(directory, "user-item"); File.WriteAllText(path, "synthetic test content");
        try
        {
            using var native = StorageReviewResources.EnterNative(Ct);
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory, qualifyNativeArm64: qualifyNativeArm64);
            var target = StoragePaths.Capture([ItemRef.ForFileSystemPath(path, EntryKind.File)])[0];
            using var pins = StorageReviewResources.ReservePins(1); IStorageReviewLease pin;
            using (StorageReviewResources.EnterRootInspection(target)) pin = adapter.InspectEfsRoot(target, StorageAction.EncryptEfs, Ct);
            using (pin)
            {
                var consent = StorageEfsConsent.Capture(Guid.NewGuid(), StorageAction.EncryptEfs, StorageScope.SelectedItems, [pin.Observation.Identity], new(true, true));
                using (var unapproved = adapter.OpenEfsPath(pin, consent, null, Ct))
                    Assert.Equal("efs-recovery-required", Assert.Throws<StorageInspectionException>(() => unapproved.PrepareMutation(StorageAction.EncryptEfs, Ct)).Problem.Code);
                Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
                var setup = adapter.InspectEfsSetup(Sid, consent, true, Ct);
                Assert.Null(setup.Problem); Assert.True(setup.Association!.HasMatchedCurrentUserKey);
                var hash = Assert.Single(setup.Association.CurrentUserKeys).CertificateSha1;
                Assert.Throws<ArgumentException>(() => StorageEfsRecoveryApproval.DeclareArrangement(consent, setup, hash, acknowledged: false));
                Assert.Throws<ArgumentException>(() => StorageEfsRecoveryApproval.DeclareArrangement(consent, setup, new string('0', 40), acknowledged: true));
                var recovery = StorageEfsRecoveryApproval.DeclareArrangement(consent, setup, hash, acknowledged: true);
                Assert.Equal("efs-recovery-scope", Assert.Throws<StorageInspectionException>(() =>
                    adapter.OpenEfsPath(pin, consent with { Scope = StorageScope.ExistingDescendants }, recovery, Ct)).Problem.Code);
                using var item = adapter.OpenEfsPath(pin, consent, recovery, Ct);
                var prepared = item.PrepareMutation(StorageAction.EncryptEfs, Ct);
                Assert.Null(prepared.Ineligible);
                var header = new StorageJournalHeader(Guid.NewGuid(), DateTime.UtcNow, consent.Action, consent.Scope,
                    consent.ReviewRevision, [new(0, 0, path, pin.Observation)])
                { EfsConsent = consent, RequiresEfsRecoveryEvidence = true };
                using var evidence = new MemoryStream();
                using var writer = new StorageTransformJournalWriter(evidence, () => { }, header);
                var call = writer.FlushIntent(0, path, prepared, recovery);
                if (changeKey) RotateDisposableDefaultKey();
                Assert.True(call.TryAdmit());
                var result = item.ApplyAdmitted(StorageAction.EncryptEfs);
                writer.FlushResult(call, result);
                Assert.True(result.NativeCallAttempted);
                Assert.Equal(changeKey ? StorageEffect.Uncertain : StorageEffect.Changed, result.Effect);
                Assert.Equal(changeKey ? "efs-recovery-review-required" : null, result.Problem?.Code);
                var actual = adapter.InspectEfsAssociations(item, Ct);
                Assert.Equal(item.Identity, actual.Identity); Assert.Equal(Sid, actual.InvokerSid); Assert.True(actual.HasMatchedCurrentUserKey);
                var observed = Assert.Single(actual.CurrentUserKeys).CertificateSha1;
                Assert.True(result.EfsKeys?.UserAssociationsEstablished);
                Assert.Equal(new StorageEfsObservedKey(observed, StorageEfsKeyState.MatchedAvailable), Assert.Single(result.EfsKeys!.Keys));
                using var recorded = new MemoryStream(evidence.ToArray());
                var durable = JournalRecovery.DescribeStorageHistory(StorageTransformJournalReader.Read(recorded, ct: Ct));
                Assert.Equal(writer.Prefix, durable.Prefix);
                Assert.Equal(recovery.Evidence, durable.LastCall?.EfsReviewedKey);
                Assert.Equal(observed, Assert.Single(durable.LastCall!.Result!.EfsKeys!.Keys).CertificateSha1);
                Assert.Equal(changeKey, durable.EffectUncertain);
                if (changeKey)
                {
                    Assert.NotEqual(hash, observed);
                    Assert.Contains(observed, result.Problem!.Message, StringComparison.Ordinal);
                    Assert.Contains(hash, result.Problem.Message, StringComparison.Ordinal);
                }
                else Assert.Equal(hash, observed);
                TestContext.Current.TestOutputHelper?.WriteLine($"EFS_RECOVERY_KEY rotated={changeKey} expected={hash} observed={observed} result={result.Effect}");
                using var noOp = adapter.OpenEfsPath(pin, consent, null, Ct);
                Assert.Null(noOp.PrepareMutation(StorageAction.EncryptEfs, Ct).Ineligible);
                var skipped = noOp.ApplyAdmitted(StorageAction.EncryptEfs);
                Assert.Equal(StorageEffect.AlreadySatisfied, skipped.Effect);
                Assert.False(skipped.NativeCallAttempted);
            }
            Assert.Equal("synthetic test content", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Decrypt(path); Directory.Delete(directory, true); }
    }

    internal static void RotateDisposableDefaultKey()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_ROTATE_KEY"));
        using var cipher = Process.Start(new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cipher.exe"), "/k")
            { CreateNoWindow = true, UseShellExecute = false });
        Assert.NotNull(cipher);
        Assert.True(cipher.WaitForExit(30_000));
        Assert.Equal(0, cipher.ExitCode);
    }
}
