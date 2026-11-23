using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
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
            RunCipher("/flushcache");
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
    public void Without_the_smart_card_decryption_asks_before_Windows_waits_and_a_skip_leaves_the_file_encrypted()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        WindowsEfsKeyTests.SmartCardHash(present: false);
        string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_SMART_CARD_ENCRYPTED_FILE");
        Assert.SkipUnless(path is not null && File.Exists(path), "Requires a file encrypted to the card key before the card was removed.");
        Assert.True(IsEncrypted(path));
        DecryptAnsweringSkip(path, "absent-decrypt", qualifyNativeArm64);
    }

    [Fact]
    public void A_file_whose_only_key_names_a_smart_card_is_asked_about_before_Windows_waits_for_a_reader()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(qualifyNativeArm64);
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_NO_CARD_READER") == "1",
            "Requires a disposable EFS account on a Windows host with no smart card reader connected.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-card-wait-").FullName;
        try
        {
            string path = Write(root, "card-only.txt");
            File.Encrypt(path);
            string hash = Assert.Single(EfsUsers(path));
            // The file's only user certificate is the account's current EFS key. While its key association names a
            // smart-card container, Windows must reach that card to decrypt the file.
            using (CardBoundKey.Rebind(hash))
            {
                RunCipher("/flushcache");
                // Each step that precedes the guard runs on its own bounded thread, so a step Windows holds is named.
                var users = Within("card-only-users", TimeSpan.FromSeconds(60), () => EfsUsers(path));
                Assert.Equal(hash, Assert.Single(users));
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                string sid = identity.User!.Value;
                var key = Within("card-only-key", TimeSpan.FromSeconds(60), () =>
                {
                    using var native = StorageReviewResources.EnterNative(Ct);
                    return new WindowsEfsKeys(qualifyNativeArm64).ProbeAssociatedCertificate(sid, hash, Ct);
                });
                Report("card-only-key", key);
                Assert.NotNull(WindowsStorageTransforms.KeyWaitProblem(new(new(1, "00000000000000000000000000000001"), sid,
                    [new(sid, hash, null)], [key]), "card-only.txt"));
                DecryptAnsweringSkip(path, "card-only-decrypt", qualifyNativeArm64, TimeSpan.FromSeconds(60));
            }
        }
        finally
        {
            // A Windows wait that outlives the job keeps the file open; that must not hide the assertion above.
            try { Directory.Delete(root, recursive: true); }
            catch (IOException error) { TestContext.Current.TestOutputHelper?.WriteLine("EFS_SMART_CARD cleanup " + error.Message); }
        }
    }

    [Fact]
    public void Only_a_key_provider_that_may_answer_later_leads_to_the_key_wait_question()
    {
        const string sid = "S-1-5-21-1-2-3-1000";
        string card = new('C', 40), software = new('D', 40);
        StorageEfsKeyEvidence Key(string hash, StorageEfsKeyState state, int? error) =>
            new(sid, hash, true, true, state, StorageEfsKeyProvider.Capi, DateTime.UtcNow, error, null);
        StorageProblem? Ask(params StorageEfsKeyEvidence[] keys) => WindowsStorageTransforms.KeyWaitProblem(
            new(new(1, "00000000000000000000000000000001"), sid,
                [new(sid, card, "FileCat card fixture"), new(sid, software, null)], [.. keys]), "report.txt");

        Assert.Null(Ask());
        Assert.Null(Ask(Key(card, StorageEfsKeyState.Unknown, unchecked((int)0x8010002E)), Key(software, StorageEfsKeyState.MatchedAvailable, null)));
        Assert.Null(Ask(Key(card, StorageEfsKeyState.Unavailable, unchecked((int)0x80092004))));
        Assert.Null(Ask(Key(card, StorageEfsKeyState.Unknown, unchecked((int)0x80090016))));
        Assert.Null(Ask(Key(card, StorageEfsKeyState.Unknown, null)));

        var reader = Assert.IsType<StorageProblem>(Ask(Key(card, StorageEfsKeyState.Unknown, unchecked((int)0x8010002E))));
        Assert.Equal("efs-key-not-reachable", reader.Code);
        Assert.Contains("FileCat card fixture (CCCCCCCC", reader.Message);
        Assert.Contains("report.txt", reader.Message);
        Assert.Contains("no smart card reader or security key is connected", reader.Message);
        Assert.Contains("no smart card is inserted", Ask(Key(card, StorageEfsKeyState.Unknown, unchecked((int)0x8010000C)))!.Message);
        var interaction = Assert.IsType<StorageProblem>(Ask(Key(software, StorageEfsKeyState.InteractionRequired, unchecked((int)0x80090022))));
        Assert.Contains(software, interaction.Message);
        Assert.Contains("needs your interaction", interaction.Message);

        // A smart card's container can be unknown until the card is present: the silent check reports the key missing,
        // yet Windows waits for a card. A missing key with a local software provider is refused at once.
        const int badKeyset = unchecked((int)0x80090016);
        Assert.Null(Ask(Key(software, StorageEfsKeyState.Unavailable, badKeyset) with { ProviderName = "Microsoft Enhanced Cryptographic Provider v1.0" }));
        Assert.Null(Ask(Key(software, StorageEfsKeyState.Unavailable, badKeyset) with { ProviderName = "Microsoft Software Key Storage Provider" }));
        Assert.Contains("smart card or security key that is not available now", Ask(Key(card, StorageEfsKeyState.Unavailable, badKeyset) with
            { ProviderName = WindowsEfsKeyTests.SmartCardCryptoProvider })!.Message);
        Assert.Contains("Contoso Token CSP", Ask(Key(card, StorageEfsKeyState.Unknown, unchecked((int)0x8009000D)) with
            { ProviderName = "Contoso Token CSP" })!.Message);
    }

    // Decrypts one file whose key cannot be reached now and answers Skip to the question that must come before any wait.
    private static void DecryptAnsweringSkip(string path, string phase, bool qualifyNativeArm64, TimeSpan? wait = null)
    {
        string root = Directory.CreateTempSubdirectory("filecat-efs-key-wait-job-").FullName;
        try
        {
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64);
            var manager = Manager(root, adapter.CreateEfsJobCandidate((_, _, _, _) =>
                throw new InvalidOperationException("Decryption asks for no key choice.")), qualifyNativeArm64);
            var asked = new List<ConfirmRequest>();
            var watch = Stopwatch.StartNew();
            double askedAfter = -1;
            manager.DecisionRequested += pending =>
            {
                asked.Add(Assert.IsType<ConfirmRequest>(pending.Request));
                askedAfter = watch.Elapsed.TotalSeconds;
                pending.Resolve(new(DecisionAction.Skip));
            };
            var job = Run(manager, StorageAction.DecryptEfs, [ItemRef.ForFileSystemPath(path, EntryKind.File)], wait: wait ?? OwnerWait);
            var result = Assert.IsType<StorageResultSnapshot>(job.StorageResult);
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SMART_CARD phase={phase} job={job.State} asked={asked.Count} asked-after={askedAfter:F1} " +
                $"total={watch.Elapsed.TotalSeconds:F1} changed={result.Counts.Changed} skipped={result.Counts.Skipped} " +
                $"reasons={string.Join(",", result.Reasons.Select(reason => reason.Problem.Code))}");
            var request = Assert.Single(asked);
            TestContext.Current.TestOutputHelper?.WriteLine("EFS_SMART_CARD question " + request.Message);
            Assert.Equal("confirm-storage-key-wait", request.ClassKey);
            Assert.Equal(DecisionAction.Retry, request.InitialAction);
            Assert.InRange(askedAfter, 0, 30);
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal(new StorageOutcomeCounts(Skipped: 1), result.Counts);
            Assert.Contains(result.Reasons, reason => reason.Problem.Code == "efs-key-skipped");
            Assert.Empty(Evidence(manager, job).Calls);
            Assert.True(IsEncrypted(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Runs one native step on a dedicated thread and names it if Windows holds it past the limit.
    private static T Within<T>(string step, TimeSpan limit, Func<T> work)
    {
        T? value = default;
        Exception? failure = null;
        var watch = Stopwatch.StartNew();
        var thread = new Thread(() =>
        {
            try { value = work(); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true, Name = "FileCat EFS step " + step };
        thread.Start();
        bool finished = thread.Join(limit);
        TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SMART_CARD step={step} finished={finished} seconds={watch.Elapsed.TotalSeconds:F1}");
        Assert.True(finished, "Windows held " + step + " for more than " + limit.TotalSeconds + " s.");
        if (failure is not null) throw new InvalidOperationException(step + " failed: " + failure.Message, failure);
        return value!;
    }

    private static IReadOnlyList<string> EfsUsers(string path)
    {
        Assert.Equal(0u, QueryEfsUsers(path, out nint users));
        try
        {
            var problems = new List<string>();
            var entries = WindowsFileRecords.ReadEfsList(users, problems);
            Assert.Empty(problems);
            return entries.Select(entry => entry.Thumbprint).ToArray();
        }
        finally { FreeEfsUsers(users); }
    }

    private static void RunCipher(string arguments)
    {
        using var cipher = Process.Start(new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cipher.exe"), arguments)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        Assert.NotNull(cipher);
        string output = cipher.StandardOutput.ReadToEnd();
        Assert.True(cipher.WaitForExit(60_000));
        Assert.True(cipher.ExitCode == 0, "cipher " + arguments + " exited " + cipher.ExitCode + ": " + output);
    }

    // Points an existing CurrentUser My certificate's key association at a container on a smart card, then restores the
    // exact original association. Windows needs that card to decrypt; with no reader connected, no card can answer.
    // The store stays open while its certificate's property changes, so each change reaches the persisted store.
    private sealed class CardBoundKey : IDisposable
    {
        private readonly X509Store _store;
        private readonly X509Certificate2 _certificate;
        private nint _original;

        private CardBoundKey(X509Store store, X509Certificate2 certificate, nint original)
        { _store = store; _certificate = certificate; _original = original; }

        internal static CardBoundKey Rebind(string thumbprint)
        {
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            X509Certificate2? certificate = null;
            nint original = 0;
            try
            {
                store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                foreach (var candidate in store.Certificates)
                {
                    if (certificate is null && string.Equals(candidate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)) certificate = candidate;
                    else candidate.Dispose();
                }
                Assert.NotNull(certificate);
                uint length = 0;
                Assert.True(GetProviderBytes(certificate.Handle, 2 /* CERT_KEY_PROV_INFO_PROP_ID */, 0, ref length));
                original = Marshal.AllocHGlobal((int)length); // the saved strings stay at the addresses its pointers name
                Assert.True(GetProviderBytes(certificate.Handle, 2, original, ref length));
                nint container = Marshal.StringToHGlobalUni("FileCat-card-fixture-" + Guid.NewGuid().ToString("N"));
                nint provider = Marshal.StringToHGlobalUni(WindowsEfsKeyTests.SmartCardCryptoProvider);
                try
                {
                    var info = new CardProviderInfo { Container = container, Provider = provider, Type = 1, KeySpec = 1 };
                    Assert.True(SetCardProvider(certificate.Handle, 2, 0, ref info));
                }
                finally { Marshal.FreeHGlobal(container); Marshal.FreeHGlobal(provider); }
                Assert.Equal(WindowsEfsKeyTests.SmartCardCryptoProvider, PersistedProvider(thumbprint));
                var bound = new CardBoundKey(store, certificate, original);
                store = null!; certificate = null; original = 0;
                return bound;
            }
            finally
            {
                if (original != 0) Marshal.FreeHGlobal(original);
                certificate?.Dispose();
                store?.Dispose();
            }
        }

        // The provider a freshly opened store names for the certificate, as FileCat's key check will read it.
        private static string? PersistedProvider(string thumbprint)
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            string? provider = null;
            foreach (var candidate in store.Certificates)
            {
                using (candidate)
                {
                    if (!string.Equals(candidate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)) continue;
                    uint length = 0;
                    if (!GetProviderBytes(candidate.Handle, 2, 0, ref length)) continue;
                    nint buffer = Marshal.AllocHGlobal((int)length);
                    try
                    {
                        if (GetProviderBytes(candidate.Handle, 2, buffer, ref length))
                            provider = Marshal.PtrToStringUni(Marshal.PtrToStructure<CardProviderInfo>(buffer).Provider);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            return provider;
        }

        public void Dispose()
        {
            if (_original == 0) return;
            try { Assert.True(SetProviderBytes(_certificate.Handle, 2, 0, _original)); }
            finally
            {
                Marshal.FreeHGlobal(_original); _original = 0;
                _certificate.Dispose(); _store.Dispose();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CardProviderInfo
    {
        public nint Container, Provider;
        public uint Type, Flags, ParameterCount;
        public nint Parameters;
        public uint KeySpec;
    }

    [DllImport("crypt32.dll", EntryPoint = "CertSetCertificateContextProperty", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCardProvider(nint certificate, uint property, uint flags, ref CardProviderInfo info);
    [DllImport("crypt32.dll", EntryPoint = "CertSetCertificateContextProperty", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProviderBytes(nint certificate, uint property, uint flags, nint data);
    [DllImport("crypt32.dll", EntryPoint = "CertGetCertificateContextProperty", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProviderBytes(nint certificate, uint property, nint data, ref uint length);
    [DllImport("advapi32.dll", EntryPoint = "QueryUsersOnEncryptedFile", CharSet = CharSet.Unicode)]
    private static extern uint QueryEfsUsers(string path, out nint users);
    [DllImport("advapi32.dll", EntryPoint = "FreeEncryptionCertificateHashList")]
    private static extern void FreeEfsUsers(nint users);

    private static void Report(string phase, StorageEfsKeyEvidence key) =>
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"EFS_SMART_CARD phase={phase} certificate={key.CertificateSha1} state={key.State} provider={key.Provider} " +
            $"error={(key.NativeError is { } error ? "0x" + error.ToString("X8") : "none")} problem={key.Problem?.Code ?? "none"} " +
            $"backup={key.BackupProblem?.Code ?? "none"}");
}
