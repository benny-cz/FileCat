using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Tests;

public sealed unsafe partial class WindowsEfsKeyTests
{
    private const uint AcquireCompareWithWindow = 0x4 | 0x8 | 0x80; // COMPARE, NO_HEALING, WINDOW_HANDLE

    [Fact]
    public void Interactive_Cng_backup_keeps_parented_access_after_the_reviewed_key_is_matched()
    {
        var pending = new StorageEfsKeyEvidence("S-1-5-21-1-2-3-1000", new string('A', 40), true, true,
            StorageEfsKeyState.InteractionRequired, StorageEfsKeyProvider.Cng, DateTime.UtcNow, null, null);
        Assert.True(WindowsEfsKeys.NeedsParentedCngBackupAccess(pending));
        Assert.True(WindowsEfsKeys.NeedsParentedCngBackupAccess(pending with { State = StorageEfsKeyState.MatchedAvailable }));
        Assert.False(WindowsEfsKeys.NeedsParentedCngBackupAccess(pending with { Provider = StorageEfsKeyProvider.Capi }));
        Assert.False(WindowsEfsKeys.NeedsParentedCngBackupAccess(pending with { CertificatePresent = false }));
        Assert.False(WindowsEfsKeys.NeedsParentedCngBackupAccess(pending with { ProviderAssociationPresent = false }));
        Assert.False(WindowsEfsKeys.NeedsParentedCngBackupAccess(pending with { State = StorageEfsKeyState.Unknown }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fresh_parented_acquisition_still_requires_the_exact_public_key(bool cng)
    {
        RequireParentedFixture();
        using var first = KeyFixture.Create(cng);
        using var other = KeyFixture.Create(cng);
        using var matching = X509CertificateLoader.LoadCertificate(first.Certificate.RawData);
        using var different = X509CertificateLoader.LoadCertificate(first.Certificate.RawData);
        CopyProvider(first.Certificate.Handle, matching.Handle);
        CopyProvider(other.Certificate.Handle, different.Handle);
        CompareWithOwner(matching, different, cng);
    }

    [Fact]
    public void Fresh_parented_acquisition_compares_a_virtual_TPM_private_key()
    {
        RequireParentedFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_TPM") == "1",
            "Requires the disposable Windows guest with a ready virtual TPM.");
        string name = "FileCat.EfsKeyQualification.ParentedTpm." + Guid.NewGuid().ToString("N");
        using var key = CngKey.Create(CngAlgorithm.Rsa, name, new CngKeyCreationParameters
        {
            Provider = CngProvider.MicrosoftPlatformCryptoProvider,
            KeyUsage = CngKeyUsages.Signing | CngKeyUsages.Decryption,
        });
        try
        {
            Assert.Equal(CngExportPolicies.None, key.ExportPolicy);
            using var rsa = new RSACng(key);
            using var certificate = CreateCertificate(name, rsa);
            using var matching = X509CertificateLoader.LoadCertificate(certificate.RawData);
            using var other = KeyFixture.Create(cng: true);
            using var different = X509CertificateLoader.LoadCertificate(other.Certificate.RawData);
            CopyProvider(certificate.Handle, matching.Handle);
            CopyProvider(certificate.Handle, different.Handle);
            CompareWithOwner(matching, different, cng: true);
        }
        finally
        {
            Assert.Equal(name, key.KeyName);
            key.Delete();
        }
    }

    [Fact]
    public void Isolated_CryptoAPI_review_compares_only_the_exact_stored_key()
    {
        RequireParentedFixture();
        using var first = KeyFixture.Create(cng: false);
        using var other = KeyFixture.Create(cng: false);
        using var wrong = X509CertificateLoader.LoadCertificate(first.Certificate.RawData);
        CopyProvider(other.Certificate.Handle, wrong.Handle);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        string sid = Sid, hash = first.Certificate.Thumbprint;
        var pending = new StorageEfsKeyEvidence(sid, hash, true, true, StorageEfsKeyState.InteractionRequired,
            StorageEfsKeyProvider.Capi, DateTime.UtcNow, null, null);
        Assert.True(WindowsEfsKeys.CanRequestKeyAccess(pending));
        Assert.False(WindowsEfsKeys.CanRequestKeyAccess(pending with { ProviderAssociationPresent = null }));
        var association = new StorageEfsAssociationSnapshot(new(1, "00000000000000000000000000000001"), sid,
            [new(sid, hash, null)], [pending]);
        var service = new WindowsEfsKeys(WindowsStorageAbiTests.QualifyNativeArm64Jobs);
        nint owner = CreateProviderOwner(0, "STATIC", "FileCat isolated EFS key qualification", 0x10CF0000,
            80, 80, 420, 160, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        try
        {
            using var allowance = StorageReviewResources.EnterNative(Ct);
            Assert.Equal("efs-key-owner", service.AccessReviewedCapiKey(association, hash, owner, false, Ct).Problem?.Code);
            Assert.Equal("efs-key-review", service.AccessReviewedCapiKey(association with { Users = [] }, hash, owner, true, Ct).Problem?.Code);
            store.Add(first.Certificate);
            try
            {
                var matched = service.AccessReviewedCapiKey(association, hash, owner, true, Ct);
                Assert.Equal(StorageEfsKeyState.MatchedAvailable, matched.State);
                Assert.Equal(StorageEfsKeyProvider.Capi, matched.Provider);
                Assert.Equal(hash, matched.CertificateSha1);
                Assert.Equal(sid, matched.InvokerSid);
                Assert.Equal("efs-key-nonexportable", matched.BackupProblem?.Code);
                var refused = service.BackUpAssociatedCertificate(association, hash, owner, true, Ct,
                    invokeWizard: _ => throw new InvalidOperationException("The non-exportable key must be refused before opening the wizard."));
                Assert.Equal(WindowsEfsKeys.BackupState.Unavailable, refused.State);
                Assert.Equal("efs-key-nonexportable", refused.Problem?.Code);
            }
            finally { store.Remove(first.Certificate); }
            store.Add(wrong);
            try
            {
                var mismatch = service.AccessReviewedCapiKey(association, hash, owner, true, Ct);
                Assert.Equal(StorageEfsKeyState.Unavailable, mismatch.State);
                Assert.Equal("efs-key-mismatch", mismatch.Problem?.Code);
            }
            finally { store.Remove(wrong); }
        }
        finally { Assert.True(DestroyProviderOwner(owner)); }
    }

    [Fact]
    public void Reviewed_parented_acquisition_requires_explicit_access_to_the_associated_cng_key()
    {
        RequireParentedFixture();
        using var fixture = KeyFixture.Create(cng: true);
        using var exportable = KeyFixture.Create(cng: true, exportable: true);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        string sid = Sid;
        string hash = fixture.Certificate.Thumbprint;
        var pending = new StorageEfsKeyEvidence(sid, hash, true, true, StorageEfsKeyState.InteractionRequired,
            StorageEfsKeyProvider.Cng, DateTime.UtcNow, null, null);
        var association = new StorageEfsAssociationSnapshot(new(1, "00000000000000000000000000000001"), sid,
            [new(sid, hash, null)], [pending]);
        var service = new WindowsEfsKeys(WindowsStorageAbiTests.QualifyNativeArm64Jobs);
        nint owner = CreateProviderOwner(0, "STATIC", "FileCat EFS reviewed key qualification", 0x10CF0000,
            80, 80, 420, 160, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        try
        {
            using var allowance = StorageReviewResources.EnterNative(Ct);
            var unapproved = service.AccessReviewedCngKey(association, hash, owner, false, Ct, UnexpectedDispatch);
            Assert.Equal("efs-key-owner", unapproved.Problem?.Code);
            var otherAssociation = service.AccessReviewedCngKey(association with { Users = [] }, hash, owner, true, Ct, UnexpectedDispatch);
            Assert.Equal("efs-key-review", otherAssociation.Problem?.Code);
            var capi = service.AccessReviewedCngKey(association with
            {
                CurrentUserKeys = [pending with { Provider = StorageEfsKeyProvider.Capi }],
            }, hash, owner, true, Ct, UnexpectedDispatch);
            Assert.Equal("efs-key-review", capi.Problem?.Code);
            var missing = service.AccessReviewedCngKey(association, hash, owner, true, Ct, UnexpectedDispatch);
            Assert.False(missing.CertificatePresent);

            store.Add(fixture.Certificate);
            try
            {
                store.Add(exportable.Certificate);
                int comparisons = 0;
                var compared = service.AccessReviewedCngKey(association, hash, owner, true, Ct, callback =>
                {
                    comparisons++;
                    callback();
                });
                Assert.Equal(1, comparisons);
                Assert.Equal(StorageEfsKeyState.MatchedAvailable, compared.State);
                Assert.Equal(StorageEfsKeyProvider.Cng, compared.Provider);
                Assert.Equal(sid, compared.InvokerSid);
                Assert.Equal(hash, compared.CertificateSha1);
                Assert.Equal("efs-key-nonexportable", compared.BackupProblem?.Code);

                var refused = service.BackUpAssociatedCertificate(association, hash, owner, true, Ct,
                    invokeWizard: _ => throw new InvalidOperationException("A non-exportable key reached the wizard."),
                    invokeProviderOwner: callback =>
                    {
                        comparisons++;
                        callback();
                    });
                Assert.Equal(2, comparisons);
                Assert.Null(refused.Completion);
                Assert.Equal("efs-key-nonexportable", refused.Problem?.Code);

                Func<WindowsEfsKeys.BackupResult>? deferredWizard = null;
                string exportableHash = exportable.Certificate.Thumbprint;
                var exportableAssociation = association with
                {
                    Users = [new(sid, exportableHash, null)],
                    CurrentUserKeys = [pending with { CertificateSha1 = exportableHash }],
                };
                var backup = service.BackUpAssociatedCertificate(exportableAssociation, exportableHash, owner, true, Ct,
                    invokeWizard: show =>
                    {
                        Assert.Equal(3, comparisons);
                        deferredWizard = show;
                        return new(WindowsEfsKeys.BackupState.Canceled, null, null);
                    },
                    invokeProviderOwner: callback =>
                    {
                        comparisons++;
                        callback();
                    });
                Assert.Equal("efs-backup-owner", backup.Problem?.Code);
                Assert.Null(backup.Completion);
                Assert.NotNull(deferredWizard);
                Assert.Equal("efs-backup-owner", deferredWizard().Problem?.Code);
            }
            finally
            {
                store.Remove(exportable.Certificate);
                store.Remove(fixture.Certificate);
            }
        }
        finally { Assert.True(DestroyProviderOwner(owner)); }

        static void UnexpectedDispatch(Action _) => Assert.Fail("The review did not authorize a provider comparison.");
    }

    private static void RequireParentedFixture()
    {
        NativeFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_KEY_WINDOW") == "1",
            "Run this foreground provider probe in a dedicated qualification process.");
    }

    private static void CompareWithOwner(X509Certificate2 matching, X509Certificate2 different, bool cng)
    {
        nint owner = CreateProviderOwner(0, "STATIC", "FileCat EFS key qualification", 0x10CF0000,
            80, 80, 420, 160, 0, 0, 0, 0);
        Assert.NotEqual(0, owner);
        try
        {
            if (cng) CheckCngWithOwner(matching, different, owner);
            else
            {
                CheckCapiWithOwner(matching.Handle, owner, shouldMatch: true);
                CheckCapiWithOwner(different.Handle, owner, shouldMatch: false);
            }
            TestContext.Current.TestOutputHelper?.WriteLine($"Provider={(!cng ? "CAPI" : "CNG")} matched and rejected mismatched keys.");
        }
        finally { Assert.True(DestroyProviderOwner(owner)); }
    }

    private static void CheckCngWithOwner(X509Certificate2 matching, X509Certificate2 different, nint owner)
    {
        using var allowance = StorageReviewResources.EnterNative(Ct);
        using var matchingSlot = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        using var differentSlot = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        string sid = Sid;
        var refused = WindowsEfsKeys.ProbeParentedCngCertificate(0, sid, matching.Thumbprint, owner, false, Ct);
        Assert.Equal("efs-key-owner", refused.Problem?.Code);
        var invalid = WindowsEfsKeys.ProbeParentedCngCertificate(0, sid, matching.Thumbprint, 0, true, Ct);
        Assert.Equal("efs-key-owner", invalid.Problem?.Code);

        Action? deferred = null;
        var delayed = WindowsEfsKeys.ProbeParentedCngCertificate(matching.Handle, sid, matching.Thumbprint, owner, true, Ct,
            invokeOwner: callback => deferred = callback);
        Assert.Equal("efs-key-owner", delayed.Problem?.Code);
        Assert.NotNull(deferred);
        deferred();

        var match = WindowsEfsKeys.ProbeParentedCngCertificate(matching.Handle, sid, matching.Thumbprint, owner, true, Ct,
            invokeOwner: callback => callback());
        Assert.Equal(StorageEfsKeyState.MatchedAvailable, match.State);
        Assert.Equal(StorageEfsKeyProvider.Cng, match.Provider);
        var mismatch = WindowsEfsKeys.ProbeParentedCngCertificate(different.Handle, sid, different.Thumbprint, owner, true, Ct);
        Assert.Equal(StorageEfsKeyState.Unavailable, mismatch.State);
        Assert.Equal("efs-key-mismatch", mismatch.Problem?.Code);
        Assert.Equal(unchecked((int)0x80090015), mismatch.NativeError);
    }

    // This control uses the process-wide CryptoAPI parent in the dedicated fixture process.
    private static void CheckCapiWithOwner(nint certificate, nint owner, bool shouldMatch)
    {
        bool acquired = Acquire(certificate, AcquireCompareWithWindow, (nint)(&owner), out nint key, out uint spec, out bool owned);
        int error = acquired ? 0 : Marshal.GetLastPInvokeError();
        try
        {
            Assert.Equal(shouldMatch, acquired);
            if (shouldMatch)
            {
                Assert.True(owned);
                Assert.NotEqual(0, key);
                Assert.Contains(spec, new uint[] { 1, 2 });
            }
            else
            {
                Assert.Equal(unchecked((int)0x80090015), error);
                Assert.False(owned);
            }
        }
        finally
        {
            bool released = !acquired || !owned || key == 0 || ReleaseCapiProvider(key, 0);
            nint cleared = 0;
            Assert.True(ClearCapiParent(0, 1 /* PP_CLIENT_HWND */, &cleared, 0));
            Assert.True(released);
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateProviderOwner(uint exStyle, string className, string caption, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyProviderOwner(nint window);
    [DllImport("advapi32.dll", EntryPoint = "CryptSetProvParam", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClearCapiParent(nint provider, uint property, nint* data, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CryptReleaseContext")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapiProvider(nint provider, uint flags);
}
