using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

// Owner-present EFS jobs with a smart-card key that the account's EFS settings name as its current key. Windows asks
// the owner for the card PIN when it needs the private key; the tests never supply it.
public sealed partial class WindowsEfsJobTests
{
    private static readonly TimeSpan OwnerWait = TimeSpan.FromMinutes(5);

    [Fact]
    public void Efs_jobs_encrypt_and_decrypt_with_the_reviewed_smart_card_key()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        string hash = WindowsEfsKeyTests.SmartCardHash(present: true);
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_SMART_CARD_ROUND_TRIP") == "1",
            "Decryption needs the owner to type the card PIN when Windows asks.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-card-").FullName;
        try
        {
            string path = Write(root, "card.txt");
            File.WriteAllText(path + ":fixture", "Named stream data");
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64);
            StorageEfsKeyEvidence? reviewed = null;
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, token) =>
            {
                var setup = adapter.InspectEfsSetup(sid, scope, true, token);
                Assert.Null(setup.Problem);
                reviewed = Assert.Single(setup.Association!.CurrentUserKeys);
                return StorageEfsRecoveryApproval.DeclareArrangement(scope, setup, reviewed.CertificateSha1, acknowledged: true);
            }), qualifyNativeArm64);
            var item = ItemRef.ForFileSystemPath(path, EntryKind.File);

            var encrypted = Run(manager, StorageAction.EncryptEfs, [item], wait: OwnerWait);
            Assert.NotNull(reviewed);
            Report("encrypt-review", reviewed);
            Assert.Equal(hash, reviewed.CertificateSha1);
            Assert.Equal(StorageEfsKeyState.MatchedAvailable, reviewed.State);
            // EFS accepts an RSA card key only through the CryptoAPI Base Smart Card CSP binding.
            Assert.Equal(StorageEfsKeyProvider.Capi, reviewed.Provider);
            Assert.Equal("efs-key-nonexportable", reviewed.BackupProblem?.Code);
            AssertCompleted(encrypted, changed: 1, already: 0);
            Assert.True(IsEncrypted(path));
            var call = Assert.Single(Evidence(manager, encrypted).Calls);
            Assert.Equal(hash, call.EfsReviewedKey!.CertificateSha1);
            Assert.Equal(StorageEfsRecoveryChoice.DeclaredArrangement, call.EfsReviewedKey.Choice);
            Assert.Equal(hash, Assert.Single(call.Result!.EfsKeys!.Keys).CertificateSha1);

            // EFS keeps a card key it has used; drop that so decryption must reach the card, which may ask for its PIN.
            FlushEfsKeyCache();
            var watch = Stopwatch.StartNew();
            var decrypted = Run(manager, StorageAction.DecryptEfs, [item], wait: OwnerWait);
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SMART_CARD phase=decrypt seconds={watch.Elapsed.TotalSeconds:F1}");
            AssertCompleted(decrypted, changed: 1, already: 0);
            Assert.False(IsEncrypted(path));
            Assert.Equal("Synthetic user content", File.ReadAllText(path));
            Assert.Equal("Named stream data", File.ReadAllText(path + ":fixture"));
            TestContext.Current.TestOutputHelper?.WriteLine("EFS_SMART_CARD phase=round-trip encrypted=1 decrypted=1");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Without_the_smart_card_new_encryption_stops_before_any_native_intent()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        string hash = WindowsEfsKeyTests.SmartCardHash(present: false);
        string root = Directory.CreateTempSubdirectory("filecat-efs-card-absent-").FullName;
        try
        {
            string path = Write(root, "card-absent.txt");
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64);
            StorageEfsSetupResult? observed = null;
            ArgumentException? declaration = null;
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, sid, scope, token) =>
            {
                observed = adapter.InspectEfsSetup(sid, scope, true, token);
                declaration = Assert.Throws<ArgumentException>(() =>
                    StorageEfsRecoveryApproval.DeclareArrangement(scope, observed, hash, acknowledged: true));
                return null;
            }), qualifyNativeArm64);

            var job = Run(manager, StorageAction.EncryptEfs, [ItemRef.ForFileSystemPath(path, EntryKind.File)], wait: OwnerWait);
            Assert.NotNull(observed);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"EFS_SMART_CARD phase=absent-setup job={job.State} problem={observed.Problem?.Code ?? "none"} attempted={observed.ProvisioningAttempted}");
            foreach (var key in observed.Association?.CurrentUserKeys ?? []) Report("absent-setup-key", key);
            // Windows may switch the account to another EFS key it can use, or create one; the review shows that actual key,
            // and the declaration made for the card key cannot be applied to it.
            Assert.NotNull(declaration);
            Assert.DoesNotContain(observed.Association?.CurrentUserKeys ?? [], key =>
                key.CertificateSha1 == hash && key.State == StorageEfsKeyState.MatchedAvailable);
            Assert.Equal(JobState.Canceled, job.State);
            Assert.Equal(0, job.StorageResult!.Counts.Changed);
            Assert.Empty(Evidence(manager, job).Calls);
            Assert.False(IsEncrypted(path));
            Assert.Equal("Synthetic user content", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Without_the_smart_card_decryption_stops_and_leaves_the_file_encrypted()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        WindowsEfsKeyTests.SmartCardHash(present: false);
        string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_SMART_CARD_ENCRYPTED_FILE");
        Assert.SkipUnless(path is not null && File.Exists(path), "Requires a file encrypted to the card key before the card was removed.");
        Assert.True(IsEncrypted(path));
        string root = Directory.CreateTempSubdirectory("filecat-efs-card-decrypt-").FullName;
        try
        {
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64);
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, _, _, _) =>
                throw new InvalidOperationException("Decryption asks for no key choice.")), qualifyNativeArm64);
            var job = Run(manager, StorageAction.DecryptEfs, [ItemRef.ForFileSystemPath(path, EntryKind.File)], wait: OwnerWait);
            var result = Assert.IsType<StorageResultSnapshot>(job.StorageResult);
            var calls = Evidence(manager, job).Calls.ToList();
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SMART_CARD phase=absent-decrypt job={job.State} " +
                $"changed={result.Counts.Changed} uncertain={result.Counts.Uncertain} calls={calls.Count} " +
                $"result={calls.FirstOrDefault()?.Result?.Effect.ToString() ?? "none"} problem={calls.FirstOrDefault()?.Result?.Problem?.Code ?? "none"} " +
                $"reasons={string.Join(",", result.Reasons.Select(reason => reason.Problem.Code))}");
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Equal(0, result.Counts.Changed);
            Assert.True(IsEncrypted(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void FlushEfsKeyCache()
    {
        using var cipher = Process.Start(new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cipher.exe"), "/flushcache")
            { CreateNoWindow = true, UseShellExecute = false });
        Assert.NotNull(cipher);
        Assert.True(cipher.WaitForExit(30_000));
        Assert.Equal(0, cipher.ExitCode);
    }

    private static void Report(string phase, StorageEfsKeyEvidence key) =>
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"EFS_SMART_CARD phase={phase} certificate={key.CertificateSha1} state={key.State} provider={key.Provider} " +
            $"error={(key.NativeError is { } error ? "0x" + error.ToString("X8") : "none")} problem={key.Problem?.Code ?? "none"} " +
            $"backup={key.BackupProblem?.Code ?? "none"}");
}
