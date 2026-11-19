using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Tests;

public sealed unsafe partial class WindowsEfsKeyTests
{
    private const uint AcquireCompareWithWindow = 0x4 | 0x8 | 0x80; // COMPARE, NO_HEALING, WINDOW_HANDLE

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
    public void Reviewed_parented_acquisition_requires_explicit_access_to_the_associated_cng_key()
    {
        RequireParentedFixture();
        using var fixture = KeyFixture.Create(cng: true);
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
            }
            finally { store.Remove(fixture.Certificate); }
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
