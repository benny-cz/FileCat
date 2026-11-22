using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Tests;

// Owner-present hardware-key cases: an EFS certificate whose non-exportable key was generated on a smart card. The
// owner inserts the card, types its PIN when Windows asks and answers the provider's prompts; the tests never do.
public sealed unsafe partial class WindowsEfsKeyTests
{
    // Windows reaches an RSA card key through the CNG smart-card KSP or the CryptoAPI Base Smart Card CSP; EFS takes the latter.
    internal const string SmartCardKeyProvider = "Microsoft Smart Card Key Storage Provider";
    internal const string SmartCardCryptoProvider = "Microsoft Base Smart Card Crypto Provider";

    /// <summary>The certificate hash of the owner's smart-card EFS key, when the run says the card is in the requested state.</summary>
    internal static string SmartCardHash(bool present)
    {
        string? hash = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_SMART_CARD_SHA1");
        string state = present ? "present" : "absent";
        Assert.SkipUnless(hash is { Length: 40 } && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_SMART_CARD") == state,
            $"Requires the owner's smart-card EFS certificate with the card {state}.");
        return hash!.ToUpperInvariant();
    }

    [Fact]
    public void A_smart_card_key_is_compared_without_a_prompt_and_refused_for_PFX_backup_before_the_wizard()
    {
        NativeFixture();
        string hash = SmartCardHash(present: true);
        using var certificate = CurrentUserCertificate(hash);
        var provider = SmartCardProvider(certificate);
        string sid = Sid;
        using var native = StorageReviewResources.EnterNative(Ct);
        var service = new WindowsEfsKeys(WindowsStorageAbiTests.QualifyNativeArm64Jobs);

        var silent = service.ProbeAssociatedCertificate(sid, hash, Ct);
        ReportSmartCard("silent", silent);
        Assert.Equal(StorageEfsKeyState.MatchedAvailable, silent.State);
        Assert.Equal(provider, silent.Provider);
        Assert.True(silent.CertificatePresent);
        Assert.True(silent.ProviderAssociationPresent);
        Assert.Null(silent.Problem);
        Assert.Equal("efs-key-nonexportable", silent.BackupProblem?.Code);

        var association = new StorageEfsAssociationSnapshot(new(1, "00000000000000000000000000000001"), sid,
            [new(sid, hash, null)], [silent]);
        Assert.True(association.HasMatchedCurrentUserKey);
        nint owner = CreateProviderOwner(0, "STATIC", "FileCat EFS smart-card qualification", 0x10CF0000,
            80, 80, 420, 160, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        try
        {
            int comparisons = 0;
            var refused = service.BackUpAssociatedCertificate(association, hash, owner, true, Ct,
                invokeWizard: _ => throw new InvalidOperationException("A smart-card key reached the PFX export wizard."),
                invokeProviderOwner: callback =>
                {
                    comparisons++;
                    callback();
                });
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"EFS_SMART_CARD phase=backup state={refused.State} problem={refused.Problem?.Code ?? "none"} comparisons={comparisons}");
            // A CNG key is compared on the FileCat window's thread; a CryptoAPI key in the isolated worker process.
            Assert.Equal(provider == StorageEfsKeyProvider.Cng ? 1 : 0, comparisons);
            Assert.Equal(WindowsEfsKeys.BackupState.Unavailable, refused.State);
            Assert.Equal("efs-key-nonexportable", refused.Problem?.Code);
            Assert.Null(refused.Completion);

            using (StorageNativeAllowance.RequireCurrent().ClaimScratch())
            {
                var parented = provider == StorageEfsKeyProvider.Cng
                    ? WindowsEfsKeys.ProbeParentedCngCertificate(certificate.Handle, sid, hash, owner, true, Ct, invokeOwner: callback => callback())
                    : service.AccessReviewedCapiKey(association with
                    {
                        CurrentUserKeys = [silent with { State = StorageEfsKeyState.InteractionRequired, BackupProblem = null }],
                    }, hash, owner, true, Ct);
                ReportSmartCard("parented", parented);
                Assert.Equal(StorageEfsKeyState.MatchedAvailable, parented.State);
                Assert.Equal(provider, parented.Provider);
                Assert.Equal("efs-key-nonexportable", parented.BackupProblem?.Code);
            }

            // The card's provider association on another public key: both comparisons must refuse it.
            using var other = KeyFixture.Create(cng: true);
            using var different = X509CertificateLoader.LoadCertificate(other.Certificate.RawData);
            CopyProvider(certificate.Handle, different.Handle);
            using (StorageNativeAllowance.RequireCurrent().ClaimScratch())
            {
                var silentMismatch = WindowsEfsKeys.ProbeHeldCertificate(different.Handle, sid, different.Thumbprint, Ct);
                ReportSmartCard("silent-mismatch", silentMismatch);
                Assert.Equal(StorageEfsKeyState.Unavailable, silentMismatch.State);
                Assert.Equal("efs-key-mismatch", silentMismatch.Problem?.Code);
                // The isolated CryptoAPI worker opens only the exact stored certificate; its mismatch is a software-key case.
                if (provider != StorageEfsKeyProvider.Cng) return;
                var parentedMismatch = WindowsEfsKeys.ProbeParentedCngCertificate(different.Handle, sid, different.Thumbprint, owner, true, Ct,
                    invokeOwner: callback => callback());
                ReportSmartCard("parented-mismatch", parentedMismatch);
                Assert.Equal(StorageEfsKeyState.Unavailable, parentedMismatch.State);
                Assert.Equal("efs-key-mismatch", parentedMismatch.Problem?.Code);
            }
        }
        finally { Assert.True(DestroyProviderOwner(owner)); }
    }

    [Fact]
    public void Without_the_smart_card_the_silent_check_cannot_authorize_new_encryption()
    {
        NativeFixture();
        string hash = SmartCardHash(present: false);
        using var certificate = CurrentUserCertificate(hash);
        var provider = SmartCardProvider(certificate);
        string sid = Sid;
        using var native = StorageReviewResources.EnterNative(Ct);
        var silent = new WindowsEfsKeys(WindowsStorageAbiTests.QualifyNativeArm64Jobs).ProbeAssociatedCertificate(sid, hash, Ct);
        ReportSmartCard("absent-silent", silent);
        Assert.NotEqual(StorageEfsKeyState.MatchedAvailable, silent.State);
        Assert.Equal(provider, silent.Provider);
        Assert.True(silent.CertificatePresent);
        Assert.True(silent.ProviderAssociationPresent);
        Assert.NotNull(silent.Problem);
        Assert.False(new StorageEfsAssociationSnapshot(new(1, "00000000000000000000000000000001"), sid,
            [new(sid, hash, null)], [silent]).HasMatchedCurrentUserKey);
    }

    [Fact]
    public void Without_the_card_explicit_access_is_not_offered_and_a_forced_request_stays_cancellable()
    {
        NativeFixture();
        string hash = SmartCardHash(present: false);
        using var certificate = CurrentUserCertificate(hash);
        var provider = SmartCardProvider(certificate);
        string sid = Sid;
        using var native = StorageReviewResources.EnterNative(Ct);
        var service = new WindowsEfsKeys(WindowsStorageAbiTests.QualifyNativeArm64Jobs);
        var silent = service.ProbeAssociatedCertificate(sid, hash, Ct);
        ReportSmartCard("absent-review", silent);
        // A removed USB key takes its reader with it: the silent check cannot even ask for interaction, so the review
        // offers no explicit access to this key.
        Assert.False(WindowsEfsKeys.CanRequestKeyAccess(silent));

        nint owner = CreateProviderOwner(0, "STATIC", "FileCat EFS smart-card access qualification", 0x10CF0000,
            80, 80, 420, 160, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        var baseline = new HashSet<nint>();
        EnumTopLevel((window, _) =>
        {
            if (IsWindowVisible(window)) baseline.Add(window);
            return true;
        }, 0);
        var seen = new ConcurrentDictionary<nint, string>();
        using var stop = new CancellationTokenSource();
        var watcher = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                RecordNewWindows(owner, baseline, seen);
                Thread.Sleep(200);
            }
        }) { IsBackground = true, Name = "FileCat smart-card prompt watcher" };
        try
        {
            watcher.Start();
            using var held = StorageNativeAllowance.RequireCurrent().ClaimScratch();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            var started = Stopwatch.StartNew();
            // Forced as if the review had reported interaction: the provider waits for a reader without any prompt.
            var pending = silent with { State = StorageEfsKeyState.InteractionRequired, Problem = null, NativeError = null,
                CertificatePresent = true, ProviderAssociationPresent = true };
            StorageEfsKeyEvidence? result = null;
            try
            {
                result = provider == StorageEfsKeyProvider.Cng
                    ? WindowsEfsKeys.ProbeParentedCngCertificate(certificate.Handle, sid, hash, owner, true, limit.Token,
                        invokeOwner: callback => callback())
                    : service.AccessReviewedCapiKey(
                        new StorageEfsAssociationSnapshot(new(1, "00000000000000000000000000000001"), sid, [new(sid, hash, null)], [pending]),
                        hash, owner, true, limit.Token);
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested && !Ct.IsCancellationRequested) { }
            double seconds = started.Elapsed.TotalSeconds;
            stop.Cancel();
            watcher.Join();
            if (result is not null) ReportSmartCard("absent-forced", result);
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SMART_CARD phase=absent-forced returned={result is not null} seconds={seconds:F1}");
            foreach (string window in seen.Values) TestContext.Current.TestOutputHelper?.WriteLine("EFS_SMART_CARD window " + window);
            Assert.NotEqual(StorageEfsKeyState.MatchedAvailable, result?.State);
            Assert.True(seconds < 45, $"The forced request outlived its cancellation: {seconds:F1} s.");
            Assert.DoesNotContain(Process.GetProcessesByName("FileCat.ShellHost"), worker =>
            {
                using (worker) return worker.SessionId == Process.GetCurrentProcess().SessionId &&
                    string.Equals(Path.GetDirectoryName(worker.MainModule?.FileName), AppContext.BaseDirectory.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            stop.Cancel();
            if (watcher.IsAlive) watcher.Join();
            Assert.True(DestroyProviderOwner(owner));
        }
    }

    private static X509Certificate2 CurrentUserCertificate(string hash)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        X509Certificate2? match = null;
        foreach (var candidate in store.Certificates)
        {
            if (match is null && string.Equals(candidate.Thumbprint, hash, StringComparison.OrdinalIgnoreCase)) match = candidate;
            else candidate.Dispose();
        }
        Assert.NotNull(match);
        return match;
    }

    private static StorageEfsKeyProvider SmartCardProvider(X509Certificate2 certificate)
    {
        var binding = ProviderOf(certificate);
        TestContext.Current.TestOutputHelper?.WriteLine($"EFS_SMART_CARD binding provider={binding.Provider} type={binding.Type}");
        if (binding.Type == 0)
        {
            Assert.Equal(SmartCardKeyProvider, binding.Provider);
            return StorageEfsKeyProvider.Cng;
        }
        Assert.Equal(SmartCardCryptoProvider, binding.Provider);
        Assert.True(WindowsEfsCapiWorker.Available, "The isolated CryptoAPI worker must ship beside the tests.");
        return StorageEfsKeyProvider.Capi;
    }

    private static (string Provider, uint Type) ProviderOf(X509Certificate2 certificate)
    {
        uint length = 0;
        Assert.True(GetProperty(certificate.Handle, 2, null, ref length));
        byte[] bytes = new byte[length];
        fixed (byte* buffer = bytes)
        {
            Assert.True(GetProperty(certificate.Handle, 2, buffer, ref length));
            var info = (WindowsEfsKeys.ProviderInfo*)buffer;
            return (Marshal.PtrToStringUni(info->Provider) ?? "", info->Type);
        }
    }

    private static void ReportSmartCard(string phase, StorageEfsKeyEvidence key) =>
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"EFS_SMART_CARD phase={phase} state={key.State} provider={key.Provider} certificate={key.CertificatePresent} " +
            $"association={key.ProviderAssociationPresent} error={(key.NativeError is { } error ? "0x" + error.ToString("X8") : "none")} " +
            $"problem={key.Problem?.Code ?? "none"} backup={key.BackupProblem?.Code ?? "none"}");

    // Every visible top-level window that appeared during the request, with its owner chain. The text is read without
    // sending messages, so a window whose thread waits on the provider cannot stall the watcher.
    private static void RecordNewWindows(nint owner, HashSet<nint> baseline, ConcurrentDictionary<nint, string> seen)
    {
        EnumTopLevel((window, _) =>
        {
            if (!IsWindowVisible(window) || baseline.Contains(window) || seen.ContainsKey(window)) return true;
            nint direct = GetOwner(window, 4 /* GW_OWNER */);
            bool owned = false;
            nint next = direct;
            for (int depth = 0; next != 0 && depth < 8; depth++)
            {
                owned |= next == owner;
                next = GetOwner(next, 4);
            }
            GetWindowThreadProcessId(window, out uint processId);
            string process;
            try { using var running = Process.GetProcessById((int)processId); process = running.ProcessName; }
            catch (Exception exited) when (exited is ArgumentException or InvalidOperationException) { process = "exited"; }
            var title = new char[256];
            var type = new char[256];
            int titleLength = InternalGetWindowText(window, title, title.Length);
            int typeLength = GetClassNameW(window, type, type.Length);
            string relation = direct == owner ? "FileCat" : direct == 0 ? "none" : "other";
            seen.TryAdd(window, $"owned={owned} direct={relation} process={process} " +
                $"class={new string(type, 0, typeLength)} title={new string(title, 0, titleLength)}");
            return true;
        }, 0);
    }

    private delegate bool TopLevelCallback(nint window, nint parameter);
    [DllImport("user32.dll", EntryPoint = "EnumWindows")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumTopLevel(TopLevelCallback callback, nint parameter);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern nint GetOwner(nint window, uint command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int InternalGetWindowText(nint window, char[] text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, char[] text, int length);
}
