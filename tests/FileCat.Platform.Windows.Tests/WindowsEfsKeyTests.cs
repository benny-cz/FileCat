using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed unsafe partial class WindowsEfsKeyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sid
    {
        get
        {
            using var charge = StorageNativeAllowance.Current?.ClaimScratch();
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        }
    }
    private static void NativeFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.SupportsHost(WindowsStorageAbiTests.QualifyNativeArm64Jobs) &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Explicit EFS key qualification in the disposable Windows VM only; creates and deletes only uniquely named fixture keys/certificates.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    [Fact]
    public void Unsupported_hosts_do_not_report_missing_certificates_or_open_a_key_owner()
    {
        Assert.SkipWhen(WindowsStorageTransforms.IsQualifiedHost, "This negative host case is exercised on unqualified platforms.");
        Assert.Null(StorageNativeAllowance.Current);
        var result = new WindowsEfsKeys().ProbeAssociatedCertificate("S-1-5-21-1-2-3-1001", new string('A', 40), Ct);
        Assert.Equal(StorageEfsKeyState.Unsupported, result.State); Assert.Null(result.CertificatePresent); Assert.Null(result.ProviderAssociationPresent);
        Assert.Null(StorageNativeAllowance.Current);
    }
    [Fact]
    public void Invalid_association_hashes_fail_before_any_native_owner_is_required()
    {
        var service = new WindowsEfsKeys();
        foreach (string hash in new[] { "", new string('a', 40), new string('G', 40), new string('A', 64) })
            Assert.Throws<ArgumentException>(() => service.ProbeAssociatedCertificate("S-1-5-21-1-2-3-1001", hash, Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fresh_private_key_comparison_supports_Capi_and_Cng_without_exporting_private_material(bool cng)
    {
        NativeFixture(); using var fixture = KeyFixture.Create(cng);
        using var native = StorageReviewResources.EnterNative(Ct); using var sourceCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        var result = WindowsEfsKeys.ProbeHeldCertificate(fixture.Certificate.Handle, Sid, fixture.Certificate.Thumbprint, Ct);
        Assert.Equal(StorageEfsKeyState.MatchedAvailable, result.State); Assert.True(result.CertificatePresent); Assert.True(result.ProviderAssociationPresent);
        Assert.Equal(cng ? StorageEfsKeyProvider.Cng : StorageEfsKeyProvider.Capi, result.Provider); Assert.Null(result.Problem); Assert.Null(result.NativeError);
        Assert.Equal("efs-key-nonexportable", result.BackupProblem?.Code);
        TestContext.Current.TestOutputHelper?.WriteLine($"EFS_KEY_PROOF provider={result.Provider} state={result.State} nonExportableFixture=True");
        // Repeat against the original context: the service neither consumes it nor creates a cached comparison bypass.
        Assert.Equal(StorageEfsKeyState.MatchedAvailable, WindowsEfsKeys.ProbeHeldCertificate(fixture.Certificate.Handle, Sid, fixture.Certificate.Thumbprint, Ct).State);
    }

    [Fact]
    public void A_platform_Tpm_key_is_compared_from_a_fresh_certificate_context_without_export()
    {
        NativeFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_TPM") == "1",
            "Requires a disposable local Windows fixture with a ready TPM.");
        string name = "FileCat.EfsKeyQualification.Tpm." + Guid.NewGuid().ToString("N");
        var provider = CngProvider.MicrosoftPlatformCryptoProvider;
        using var key = CngKey.Create(CngAlgorithm.Rsa, name, new CngKeyCreationParameters
        {
            Provider = provider,
            KeyUsage = CngKeyUsages.Signing | CngKeyUsages.Decryption,
        });
        try
        {
            Assert.Equal(provider, key.Provider);
            Assert.Equal(CngExportPolicies.None, key.ExportPolicy);
            using var rsa = new RSACng(key);
            using var certificate = CreateCertificate(name, rsa);
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
            CopyProvider(certificate.Handle, publicOnly.Handle);

            using var native = StorageReviewResources.EnterNative(Ct);
            using var sourceCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
            using var copiedCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
            var result = WindowsEfsKeys.ProbeHeldCertificate(publicOnly.Handle, Sid, certificate.Thumbprint, Ct);
            Assert.Equal(StorageEfsKeyState.MatchedAvailable, result.State);
            Assert.Equal(StorageEfsKeyProvider.Cng, result.Provider);
            Assert.True(result.CertificatePresent);
            Assert.True(result.ProviderAssociationPresent);
            Assert.Null(result.NativeError);
            Assert.Equal("efs-key-nonexportable", result.BackupProblem?.Code);
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_TPM_KEY provider={key.Provider} state={result.State} exportPolicy={key.ExportPolicy}");
        }
        finally
        {
            Assert.Equal(name, key.KeyName);
            key.Delete();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cached_key_B_does_not_make_certificate_A_a_matching_key_even_when_direct_cached_acquisition_succeeds(bool cng)
    {
        NativeFixture(); using var a = KeyFixture.Create(cng); using var b = KeyFixture.Create(cng);
        using var publicA = X509CertificateLoader.LoadCertificate(a.Certificate.RawData);
        CopyProvider(b.Certificate.Handle, publicA.Handle);
        using var native = StorageReviewResources.EnterNative(Ct);
        using var sourceCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch(); using var cachedCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        const uint flags = 0x1 /* CACHE */ | 0x8 | 0x40 | 0x10000;
        Assert.True(Acquire(publicA.Handle, flags, 0, out _, out _, out bool ownsCached)); Assert.False(ownsCached);
        // This is the API's documented bypass: comparison is omitted for the already-cached B handle.
        Assert.True(Acquire(publicA.Handle, flags | 0x4 /* COMPARE */, 0, out _, out _, out ownsCached)); Assert.False(ownsCached);
        uint size = 0; Assert.True(GetProperty(publicA.Handle, 5, null, ref size));
        var rejected = WindowsEfsKeys.ProbeHeldCertificate(publicA.Handle, Sid, a.Certificate.Thumbprint, Ct);
        Assert.Equal(StorageEfsKeyState.Unavailable, rejected.State); Assert.Equal("efs-key-mismatch", rejected.Problem?.Code);
        Assert.Equal(unchecked((int)0x80090015), rejected.NativeError);
        TestContext.Current.TestOutputHelper?.WriteLine($"EFS_KEY_CACHED_CONTROL provider={(cng ? "Cng" : "Capi")} cachedComparisonSucceeded=True freshResult={rejected.State} code={rejected.Problem?.Code}");
        Assert.Equal(StorageEfsKeyState.MatchedAvailable, WindowsEfsKeys.ProbeHeldCertificate(a.Certificate.Handle, Sid, a.Certificate.Thumbprint, Ct).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_source_caching_preference_is_not_copied_into_the_fresh_context_or_healed_in_the_original(bool cng)
    {
        NativeFixture(); using var fixture = KeyFixture.Create(cng);
        uint length = 0; Assert.True(GetProperty(fixture.Certificate.Handle, 2, null, ref length));
        byte[] bytes = new byte[length];
        fixed (byte* buffer = bytes)
        {
            Assert.True(GetProperty(fixture.Certificate.Handle, 2, buffer, ref length));
            var info = (WindowsEfsKeys.ProviderInfo*)buffer; info->Flags |= 1;
            Assert.True(SetProperty(fixture.Certificate.Handle, 2, 0, buffer));
        }
        using var native = StorageReviewResources.EnterNative(Ct); using var sourceCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        Assert.Equal(StorageEfsKeyState.MatchedAvailable, WindowsEfsKeys.ProbeHeldCertificate(fixture.Certificate.Handle, Sid, fixture.Certificate.Thumbprint, Ct).State);
        fixed (byte* buffer = bytes)
        {
            length = (uint)bytes.Length; Assert.True(GetProperty(fixture.Certificate.Handle, 2, buffer, ref length));
            Assert.Equal(1u, ((WindowsEfsKeys.ProviderInfo*)buffer)->Flags & 1u);
        }
    }

    [Fact]
    public void Unqualified_provider_parameters_and_a_different_encoded_certificate_do_not_become_known_key_access()
    {
        NativeFixture(); using var fixture = KeyFixture.Create(false);
        using var publicOnly = X509CertificateLoader.LoadCertificate(fixture.Certificate.RawData);
        uint length = 0; Assert.True(GetProperty(fixture.Certificate.Handle, 2, null, ref length)); byte[] bytes = new byte[length];
        fixed (byte* buffer = bytes)
        {
            Assert.True(GetProperty(fixture.Certificate.Handle, 2, buffer, ref length));
            var info = (WindowsEfsKeys.ProviderInfo*)buffer;
            var parameter = new ProviderParameter { Parameter = uint.MaxValue };
            info->ParameterCount = 1; info->Parameters = (nint)(&parameter);
            Assert.True(SetProperty(publicOnly.Handle, 2, 0, buffer));
        }
        using var native = StorageReviewResources.EnterNative(Ct); using var sourceCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        var unknown = WindowsEfsKeys.ProbeHeldCertificate(publicOnly.Handle, Sid, publicOnly.Thumbprint, Ct);
        Assert.Equal(StorageEfsKeyState.Unknown, unknown.State); Assert.True(unknown.CertificatePresent); Assert.Null(unknown.ProviderAssociationPresent);
        Assert.Equal("efs-provider-unqualified", unknown.Problem?.Code);
        var different = WindowsEfsKeys.ProbeHeldCertificate(fixture.Certificate.Handle, Sid, new string('0', 40), Ct);
        Assert.Equal(StorageEfsKeyState.Unavailable, different.State); Assert.Equal("efs-certificate-mismatch", different.Problem?.Code);
        Assert.Null(different.CertificatePresent); Assert.Null(different.ProviderAssociationPresent);
    }

    [Fact]
    public void A_public_certificate_or_foreign_invoker_does_not_establish_private_key_access()
    {
        NativeFixture(); using var fixture = KeyFixture.Create(false); using var publicOnly = X509CertificateLoader.LoadCertificate(fixture.Certificate.RawData);
        using var native = StorageReviewResources.EnterNative(Ct); using var sourceCharge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        var unavailable = WindowsEfsKeys.ProbeHeldCertificate(publicOnly.Handle, Sid, publicOnly.Thumbprint, Ct);
        Assert.True(unavailable.CertificatePresent); Assert.False(unavailable.ProviderAssociationPresent); Assert.Equal(StorageEfsKeyState.Unavailable, unavailable.State);
        Assert.Throws<UnauthorizedAccessException>(() => WindowsEfsKeys.ProbeHeldCertificate(fixture.Certificate.Handle, "S-1-5-21-1-2-3-9999", fixture.Certificate.Thumbprint, Ct));
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsEfsKeys.ProbeHeldCertificate(fixture.Certificate.Handle, Sid, fixture.Certificate.Thumbprint, stopped.Token));
    }

    [Fact]
    public void Exact_current_user_store_lookup_does_not_select_another_certificate_or_enroll_a_key()
    {
        NativeFixture(); using var fixture = KeyFixture.Create(true); using var absent = KeyFixture.Create(false);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        var existing = store.Certificates; try { Assert.DoesNotContain(existing.Cast<X509Certificate2>(), cert => cert.Thumbprint == fixture.Certificate.Thumbprint); }
        finally { foreach (var cert in existing) cert.Dispose(); }
        store.Add(fixture.Certificate);
        try
        {
            using var native = StorageReviewResources.EnterNative(Ct);
            var service = new WindowsEfsKeys(WindowsStorageAbiTests.QualifyNativeArm64Jobs);
            Assert.Equal(StorageEfsKeyState.MatchedAvailable, service.ProbeAssociatedCertificate(Sid, fixture.Certificate.Thumbprint, Ct).State);
            var missing = service.ProbeAssociatedCertificate(Sid, absent.Certificate.Thumbprint, Ct);
            Assert.Equal(StorageEfsKeyState.Unavailable, missing.State); Assert.False(missing.CertificatePresent);
        }
        finally { store.Remove(fixture.Certificate); }
    }

    private static void CopyProvider(nint source, nint destination)
    {
        uint length = 0; Assert.True(GetProperty(source, 2, null, ref length)); Assert.InRange(length, 1u, (uint)WindowsEfsKeys.ProviderBytes);
        byte[] bytes = new byte[length];
        fixed (byte* buffer = bytes)
        { Assert.True(GetProperty(source, 2, buffer, ref length)); Assert.True(SetProperty(destination, 2, 0, buffer)); }
    }

    private static X509Certificate2 CreateCertificate(string name, RSA rsa)
    {
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.4.1.311.10.3.4") }, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
    private sealed class KeyFixture : IDisposable
    {
        private const string Prefix = "FileCat.EfsKeyQualification.";
        private readonly string _name;
        private readonly RSA _rsa;
        private readonly CngKey? _key;
        internal X509Certificate2 Certificate { get; }
        private KeyFixture(string name, RSA rsa, CngKey? key, X509Certificate2 certificate) { _name = name; _rsa = rsa; _key = key; Certificate = certificate; }
        internal static KeyFixture Create(bool cng, bool exportable = false)
        {
            string name = Prefix + Guid.NewGuid().ToString("N"); CngKey? key = null;
            RSA rsa;
            if (cng)
            {
                key = CngKey.Create(CngAlgorithm.Rsa, name, new CngKeyCreationParameters { Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                    KeyUsage = CngKeyUsages.AllUsages, ExportPolicy = exportable
                        ? CngExportPolicies.AllowExport | CngExportPolicies.AllowPlaintextExport : CngExportPolicies.None });
                rsa = new RSACng(key);
            }
            else
            {
                var capi = new RSACryptoServiceProvider(2048, new CspParameters(24) { KeyContainerName = name, KeyNumber = (int)KeyNumber.Exchange,
                    Flags = CspProviderFlags.NoPrompt | CspProviderFlags.UseNonExportableKey });
                Assert.False(capi.CspKeyContainerInfo.Exportable); rsa = capi;
            }
            return new(name, rsa, key, CreateCertificate(name, rsa));
        }
        public void Dispose()
        {
            Certificate.Dispose();
            if (_rsa is RSACryptoServiceProvider capi)
            {
                if (!_name.StartsWith(Prefix, StringComparison.Ordinal) || capi.CspKeyContainerInfo.KeyContainerName != _name) throw new IOException("Fixture key ownership changed; refusing deletion.");
                capi.PersistKeyInCsp = false;
            }
            _rsa.Dispose();
            if (_key is not null)
            {
                try
                {
                    if (!_name.StartsWith(Prefix, StringComparison.Ordinal) || _key.KeyName != _name) throw new IOException("Fixture CNG key ownership changed; refusing deletion.");
                    _key.Delete();
                }
                finally { _key.Dispose(); }
            }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderParameter { public uint Parameter; public nint Data; public uint Length, Flags; }
    [LibraryImport("crypt32.dll", EntryPoint = "CertGetCertificateContextProperty", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetProperty(nint cert, uint property, void* data, ref uint length);
    [LibraryImport("crypt32.dll", EntryPoint = "CertSetCertificateContextProperty", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetProperty(nint cert, uint property, uint flags, void* data);
    [LibraryImport("crypt32.dll", EntryPoint = "CryptAcquireCertificatePrivateKey", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Acquire(nint cert, uint flags, nint parameters, out nint key, out uint keySpec, [MarshalAs(UnmanagedType.Bool)] out bool callerFree);
}
