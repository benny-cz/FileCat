using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.Platform.Windows;

/// <summary>Silent EFS key comparison candidate. No enrollment, setup file, export, default change or key caching.</summary>
internal sealed unsafe partial class WindowsEfsKeys(bool qualifyNativeArm64 = false) : IStorageEfsKeys
{
    internal const int CertificateBytes = 64 * 1024, ProviderBytes = 64 * 1024, ProviderCharacters = 2048;
    private const uint AcquireFlags = 0x4 | 0x8 | 0x40 | 0x10000; // COMPARE, NO_HEALING, SILENT, ALLOW_NCRYPT
    private const int NotFound = unchecked((int)0x80092004), SilentContext = unchecked((int)0x80090022), BadPublicKey = unchecked((int)0x80090015);
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContext { public uint Encoding; public byte* Encoded; public uint Length; public nint Info, Store; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProviderInfo
    {
        public nint Container, Provider;
        public uint Type, Flags, ParameterCount;
        public nint Parameters;
        public uint KeySpec;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public uint Length; public byte* Data; }
    private sealed record Binding(string Container, string Provider, uint Type, uint Flags, uint KeySpec);

    public StorageEfsKeyEvidence ProbeAssociatedCertificate(string invokerSid, string certificateSha1, CancellationToken ct) =>
        InspectAssociatedCertificate(invokerSid, certificateSha1, ct, useMatchedCertificate: null);

    private StorageEfsKeyEvidence InspectAssociatedCertificate(string invokerSid, string certificateSha1, CancellationToken ct,
        Action<nint>? useMatchedCertificate)
    {
        ValidateRequest(invokerSid, certificateSha1);
        ct.ThrowIfCancellationRequested();
        if (!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64)) return Evidence(invokerSid, certificateSha1, null, null, StorageEfsKeyState.Unsupported,
            StorageEfsKeyProvider.Unknown, null, "efs-key-host", "This key comparison candidate requires its qualified Windows 11 host.");
        RequireInvoker(invokerSid, ct);
        var allowance = StorageNativeAllowance.RequireCurrent();
        using var storeCharge = allowance.ClaimScratch();
        ct.ThrowIfCancellationRequested();
        nint store = OpenStore(10 /* CERT_STORE_PROV_SYSTEM_W */, 0, 0, 0x10000 | 0x8000 | 0x4000 /* CURRENT_USER | READONLY | OPEN_EXISTING */, "My");
        if (store == 0) return Error(invokerSid, certificateSha1, null, null, Marshal.GetLastPInvokeError(), "efs-key-store", "The invoking user's existing certificate store could not be opened.");
        try
        {
            ct.ThrowIfCancellationRequested(); using var certificateCharge = allowance.ClaimScratch();
            byte[] hash = Convert.FromHexString(certificateSha1); nint certificate;
            fixed (byte* bytes = hash)
            {
                var blob = new Blob { Length = (uint)hash.Length, Data = bytes };
                certificate = FindCertificate(store, 1, 0, 0x10000 /* CERT_FIND_HASH */, &blob, 0);
            }
            if (certificate == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                return Error(invokerSid, certificateSha1, error == NotFound ? false : null, null, error, "efs-certificate-missing", "The exact file-associated certificate was not found in CurrentUser My.");
            }
            try { return ProbeHeldCertificate(certificate, invokerSid, certificateSha1, ct, useMatchedCertificate); }
            finally { FreeCertificate(certificate); }
        }
        finally { CloseStore(store, 0); }
    }

    /// <summary>Borrow an API-owned certificate context and its caller's scratch charge. Used by native qualification.</summary>
    internal static StorageEfsKeyEvidence ProbeHeldCertificate(nint certificate, string invokerSid, string certificateSha1, CancellationToken ct,
        Action<nint>? useMatchedCertificate = null)
    {
        ValidateRequest(invokerSid, certificateSha1); RequireInvoker(invokerSid, ct);
        var allowance = StorageNativeAllowance.RequireCurrent();
        if (certificate == 0) throw new ArgumentException("A held certificate context is required.", nameof(certificate));
        var source = (CertificateContext*)certificate;
        if (source->Encoded == null || source->Length is 0 or > CertificateBytes)
            return Evidence(invokerSid, certificateSha1, null, null, StorageEfsKeyState.Unknown, StorageEfsKeyProvider.Unknown, null,
                "efs-certificate-bound", "The exact certificate exceeds the bounded encoded-certificate view.");
        if (Convert.ToHexString(SHA1.HashData(new ReadOnlySpan<byte>(source->Encoded, (int)source->Length))) != certificateSha1)
            return Evidence(invokerSid, certificateSha1, null, null, StorageEfsKeyState.Unavailable, StorageEfsKeyProvider.Unknown, null,
                "efs-certificate-mismatch", "The certificate's encoded identity differs from the file-associated hash.");
        Binding binding;
        try { binding = ReadBinding(certificate, ct); }
        catch (KeyInspectionException error)
        { return Evidence(invokerSid, certificateSha1, true, error.NativeError == NotFound ? false : null, error.State, StorageEfsKeyProvider.Unknown, error.NativeError, error.Code, error.Message); }
        var provider = binding.Type == 0 ? StorageEfsKeyProvider.Cng : StorageEfsKeyProvider.Capi;
        using var freshCharge = allowance.ClaimScratch();
        ct.ThrowIfCancellationRequested();
        nint fresh = CreateContext(1 /* CERT_STORE_CERTIFICATE_CONTEXT */, 1 /* X509 */, source->Encoded, source->Length, 0, null);
        if (fresh == 0) return Error(invokerSid, certificateSha1, true, true, Marshal.GetLastPInvokeError(), "efs-fresh-context", "A fresh certificate comparison context could not be created.");
        try
        {
            ct.ThrowIfCancellationRequested();
            // CertCreateContext explicitly excludes extended properties. Copy no cached handles,
            // provider parameters or store context; only the observed named provider/key association.
            fixed (char* container = binding.Container)
            fixed (char* name = binding.Provider)
            {
                var copied = new ProviderInfo { Container = (nint)container, Provider = (nint)name, Type = binding.Type,
                    Flags = binding.Flags & ~1u /* remove caching preference in this private context only */, KeySpec = binding.KeySpec };
                if (!SetProperty(fresh, 2 /* CERT_KEY_PROV_INFO_PROP_ID */, 0, &copied))
                    return Error(invokerSid, certificateSha1, true, true, Marshal.GetLastPInvokeError(), "efs-key-binding", "The fresh context could not retain the exact provider association.");
            }
            foreach (uint property in new uint[] { 5 /* KEY_CONTEXT */, 1 /* KEY_PROV_HANDLE */, 78 /* NCRYPT_KEY_HANDLE */ })
            {
                ct.ThrowIfCancellationRequested(); uint size = 0;
                bool exists = GetProperty(fresh, property, null, ref size); int error = Marshal.GetLastPInvokeError();
                ct.ThrowIfCancellationRequested();
                if (exists || error != NotFound) return Evidence(invokerSid, certificateSha1, true, true, StorageEfsKeyState.Unknown, provider,
                    exists ? null : error, "efs-key-cache-ambiguous", "The fresh context's absence of cached key handles could not be established.");
            }
            using var keyCharge = allowance.ClaimScratch();
            ct.ThrowIfCancellationRequested();
            bool acquired = Acquire(fresh, AcquireFlags, 0, out nint key, out uint keySpec, out bool callerFree);
            int nativeError = acquired ? 0 : Marshal.GetLastPInvokeError();
            try
            {
                ct.ThrowIfCancellationRequested(); RequireInvoker(invokerSid, ct);
                if (!acquired) return Error(invokerSid, certificateSha1, true, true, nativeError, "efs-private-key", "The exact certificate's private key could not be silently acquired and compared.", provider);
                if (!callerFree || key == 0 || keySpec is not (1 or 2 or uint.MaxValue) || (keySpec == uint.MaxValue) != (binding.Type == 0))
                    return Evidence(invokerSid, certificateSha1, true, true, StorageEfsKeyState.Unknown, provider, null,
                        "efs-key-comparison-unestablished", "A fresh owned certificate-to-key comparison was not established; a cached or ambiguous result cannot authorize encryption.");
                useMatchedCertificate?.Invoke(fresh);
                return Evidence(invokerSid, certificateSha1, true, true, StorageEfsKeyState.MatchedAvailable, provider, null, null, null)
                    with { Subject = CertificateSubject(fresh) };
            }
            finally
            {
                if (acquired && callerFree && key != 0)
                { if (keySpec == uint.MaxValue) FreeCng(key); else ReleaseCapi(key, 0); }
            }
        }
        finally { FreeCertificate(fresh); }
    }

    private static string? CertificateSubject(nint certificate)
    {
        const uint simpleDisplay = 4;
        uint length = GetNameString(certificate, simpleDisplay, 0, 0, null, 0);
        if (length is < 2 or > 512) return null;
        Span<char> text = stackalloc char[(int)length];
        fixed (char* characters = text)
            return GetNameString(certificate, simpleDisplay, 0, 0, characters, length) == length
                ? new string(text[..^1]) : null;
    }

    private static Binding ReadBinding(nint certificate, CancellationToken ct)
    {
        uint requested = 0; ct.ThrowIfCancellationRequested();
        if (!GetProperty(certificate, 2, null, ref requested)) throw PropertyFailure(Marshal.GetLastPInvokeError());
        ct.ThrowIfCancellationRequested();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (requested < sizeof(ProviderInfo) || requested > ProviderBytes) throw Shape("The provider association exceeds the bounded metadata view.");
            byte* buffer = (byte*)NativeMemory.AllocZeroed(requested); uint returned = requested;
            try
            {
                ct.ThrowIfCancellationRequested();
                bool read = GetProperty(certificate, 2, buffer, ref returned); int error = Marshal.GetLastPInvokeError();
                ct.ThrowIfCancellationRequested();
                if (!read)
                {
                    if (error == 234 && returned > requested && attempt < 2) { requested = returned; continue; }
                    throw PropertyFailure(error);
                }
                if (returned < sizeof(ProviderInfo) || returned > requested) throw Shape("The provider association returned incomplete metadata.");
                var info = *(ProviderInfo*)buffer;
                if (info.ParameterCount != 0 || (info.Flags & ~(1u | 0x20u | 0x40u)) != 0 || info.Type != 0 && info.KeySpec is not (1 or 2))
                    throw Shape("This provider's parameters, flags or key specification need separate qualification; none were silently removed.");
                return new(ReadString(info.Container, buffer, returned), ReadString(info.Provider, buffer, returned), info.Type, info.Flags, info.KeySpec);
            }
            finally { NativeMemory.Free(buffer); }
        }
        throw Shape("The provider association did not settle within two bounded resizes.");
    }
    private static string ReadString(nint pointer, byte* begin, uint length)
    {
        nuint start = (nuint)begin, value = (nuint)pointer;
        if (value < start || value - start >= length || ((value - start) & 1) != 0) throw Shape("A provider name lies outside its returned buffer.");
        int available = (int)Math.Min((length - (value - start)) / 2, ProviderCharacters + 1u);
        var chars = new ReadOnlySpan<char>((void*)pointer, available); int end = chars.IndexOf('\0');
        if (end is < 1 or > ProviderCharacters) throw Shape("A complete bounded provider/container name was not returned.");
        return new(chars[..end]);
    }
    private static void ValidateRequest(string invokerSid, string certificateSha1)
    {
        if (string.IsNullOrEmpty(invokerSid) || invokerSid.Length > 256 || certificateSha1 is not { Length: 40 } || certificateSha1.Any(c => !char.IsAsciiHexDigitUpper(c)))
            throw new ArgumentException("Supply the invoking SID and exact canonical SHA-1 certificate hash from an actual EFS association.");
    }
    private static void RequireInvoker(string expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); using var charge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        if (WindowsTokenAuthority.IsHighIntegrity) throw new UnauthorizedAccessException("EFS key checks require the normal invoking user.");
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != expected) throw new UnauthorizedAccessException("The invoking EFS identity changed; the key check grants no cross-account authority.");
        ct.ThrowIfCancellationRequested();
    }
    private static StorageEfsKeyEvidence Error(string sid, string hash, bool? present, bool? association, int error, string code, string message,
        StorageEfsKeyProvider provider = StorageEfsKeyProvider.Unknown) =>
        Evidence(sid, hash, present, association, error == SilentContext ? StorageEfsKeyState.InteractionRequired :
            error is BadPublicKey or NotFound or 5 or unchecked((int)0x80090016) or unchecked((int)0x8009000D) ? StorageEfsKeyState.Unavailable : StorageEfsKeyState.Unknown,
            provider, error, error == BadPublicKey ? "efs-key-mismatch" : code,
            error == BadPublicKey ? "The private key does not match the exact file-associated certificate." : message);
    private static StorageEfsKeyEvidence Evidence(string sid, string hash, bool? present, bool? association, StorageEfsKeyState state,
        StorageEfsKeyProvider provider, int? error, string? code, string? message) => new(sid, hash, present, association, state, provider, DateTime.UtcNow, error,
            code is null ? null : new(state == StorageEfsKeyState.Unknown ? StorageReasonKind.Unknown : state == StorageEfsKeyState.Unsupported ? StorageReasonKind.WindowsLimitation : StorageReasonKind.PermissionOrPolicy, code, message!, true));
    private sealed class KeyInspectionException(StorageEfsKeyState state, string code, string message, int? error = null) : IOException(message)
    { internal StorageEfsKeyState State => state; internal string Code => code; internal int? NativeError => error; }
    private static KeyInspectionException Shape(string message) => new(StorageEfsKeyState.Unknown, "efs-provider-unqualified", message);
    private static KeyInspectionException PropertyFailure(int error) => new(error == NotFound ? StorageEfsKeyState.Unavailable : StorageEfsKeyState.Unknown,
        "efs-key-association", "The existing certificate's complete provider association could not be read; no enrollment or healing was attempted.", error);

    [LibraryImport("crypt32.dll", EntryPoint = "CertOpenStore", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)] private static partial nint OpenStore(nint provider, uint encoding, nint crypt, uint flags, string name);
    [LibraryImport("crypt32.dll", EntryPoint = "CertCloseStore", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseStore(nint store, uint flags);
    [LibraryImport("crypt32.dll", EntryPoint = "CertFindCertificateInStore", SetLastError = true)] private static partial nint FindCertificate(nint store, uint encoding, uint flags, uint type, void* parameter, nint previous);
    [LibraryImport("crypt32.dll", EntryPoint = "CertCreateContext", SetLastError = true)] private static partial nint CreateContext(uint type, uint encoding, byte* data, uint length, uint flags, void* parameters);
    [LibraryImport("crypt32.dll", EntryPoint = "CertFreeCertificateContext", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool FreeCertificate(nint certificate);
    [LibraryImport("crypt32.dll", EntryPoint = "CertGetNameStringW")] private static partial uint GetNameString(nint certificate, uint type, uint flags, nint parameter, char* name, uint length);
    [LibraryImport("crypt32.dll", EntryPoint = "CertGetCertificateContextProperty", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetProperty(nint certificate, uint property, void* data, ref uint length);
    [LibraryImport("crypt32.dll", EntryPoint = "CertSetCertificateContextProperty", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetProperty(nint certificate, uint property, uint flags, void* data);
    [LibraryImport("crypt32.dll", EntryPoint = "CryptAcquireCertificatePrivateKey", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Acquire(nint certificate, uint flags, nint parameters, out nint key, out uint keySpec, [MarshalAs(UnmanagedType.Bool)] out bool callerFree);
    [LibraryImport("advapi32.dll", EntryPoint = "CryptReleaseContext", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ReleaseCapi(nint provider, uint flags);
    [LibraryImport("ncrypt.dll", EntryPoint = "NCryptFreeObject")] private static partial int FreeCng(nint key);
}
