using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FileCat.Platform.Windows;

/// <summary>
/// Whether opening a file can make Windows ask the person for a smart card's PIN (I685): the file is EFS-encrypted, and
/// this user has an EFS certificate whose key is not in one of the computer's own software providers. Decided from the
/// certificate store's records without opening any key; the store is looked at again after a minute.
/// </summary>
internal static unsafe partial class WindowsEfsPrompts
{
    private const string EfsUsage = "1.3.6.1.4.1.311.10.3.4";
    private const uint KeyProviderInfo = 2; // CERT_KEY_PROV_INFO_PROP_ID
    private const uint MaxPropertyBytes = 64 * 1024;
    private static readonly TimeSpan Recheck = TimeSpan.FromMinutes(1);
    private static readonly object Gate = new();
    private static long _checked;
    private static bool _keyOffComputer;

    internal static bool MayAsk(string path)
    {
        if (!KeyOffComputer()) return false;
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Encrypted) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool KeyOffComputer()
    {
        lock (Gate)
        {
            if (_checked == 0 || Stopwatch.GetElapsedTime(_checked) >= Recheck)
            {
                _keyOffComputer = ReadKeyOffComputer();
                _checked = Stopwatch.GetTimestamp();
            }
            return _keyOffComputer;
        }
    }

    /// <summary>Whether this user's store holds an EFS certificate with a key outside the computer's software providers.</summary>
    internal static bool ReadKeyOffComputer()
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var certificates = store.Certificates;
            try
            {
                foreach (var certificate in certificates)
                    if (certificate.HasPrivateKey && IsEfs(certificate) && !IsLocalProvider(ProviderName(certificate))) return true;
                return false;
            }
            finally
            {
                foreach (var certificate in certificates) certificate.Dispose();
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>A provider whose keys Windows uses without a card or a question; an unknown one may need either.</summary>
    internal static bool IsLocalProvider(string? provider) => provider is not null && WindowsStorageTransforms.LocalKeyProviders.Contains(provider);

    private static bool IsEfs(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .Any(usage => usage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == EfsUsage));

    /// <summary>The provider named in the certificate's key record (CRYPT_KEY_PROV_INFO), read within its own buffer.</summary>
    private static string? ProviderName(X509Certificate2 certificate)
    {
        uint size = 0;
        if (!GetProperty(certificate.Handle, KeyProviderInfo, null, &size) || size < 2 * (uint)IntPtr.Size || size > MaxPropertyBytes) return null;
        byte[] buffer = new byte[size];
        fixed (byte* data = buffer)
        {
            if (!GetProperty(certificate.Handle, KeyProviderInfo, data, &size) || size > buffer.Length) return null;
            // pwszContainerName, then pwszProvName: both point into this same buffer.
            nint name = *(nint*)(data + IntPtr.Size);
            long offset = name - (nint)data;
            if (name == 0 || offset < 2 * IntPtr.Size || offset >= size || offset % 2 != 0) return null;
            var characters = MemoryMarshal.Cast<byte, char>(buffer.AsSpan((int)offset, (int)(size - offset) & ~1));
            int end = characters.IndexOf('\0');
            return end < 0 ? null : new string(characters[..end]);
        }
    }

    [LibraryImport("crypt32.dll", EntryPoint = "CertGetCertificateContextProperty", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProperty(nint certificate, uint property, byte* data, uint* size);
}
