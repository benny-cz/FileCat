using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Elevation;

public static partial class ElevationBroker
{
    private const string ClaimTimestamp = "CreatedUtc";

    /// <summary>A plan runs at most once: an atomic claim is stored in administrator-controlled HKLM.</summary>
    public static bool TryClaimNonce(string nonce, DateTime nowUtc)
    {
        ValidateNonce(nonce);
        using var key = Registry.LocalMachine.CreateSubKey(NonceKey, writable: true);
        return TryClaimNonce(key, nonce, nowUtc);
    }

    internal static bool TryClaimNonce(RegistryKey key, string nonce, DateTime nowUtc)
    {
        ValidateNonce(nonce);
        long timestamp = nowUtc.ToFileTimeUtc();
        long cutoff = nowUtc.AddDays(-2).ToFileTimeUtc();
        foreach (var name in key.GetValueNames())
            if (key.GetValue(name) is long stamp && stamp >= 0 && stamp < cutoff)
                key.DeleteValue(name, throwOnMissingValue: false);
        foreach (var name in key.GetSubKeyNames())
        {
            if (!IsNonce(name)) continue;
            bool expired;
            using (var claim = key.OpenSubKey(name))
                expired = claim?.GetValue(ClaimTimestamp) is long stamp && stamp >= 0 && stamp < cutoff;
            if (expired) key.DeleteSubKey(name, throwOnMissingSubKey: false);
        }
        // Honor records created by earlier FileCat versions.
        if (key.GetValue(nonce) is not null) return false;
        int error = RegCreateNonceKey(key.Handle, nonce, 0, null, 0,
            0x2001F /* KEY_READ | KEY_WRITE */, 0, out var handle, out uint disposition);
        using (handle)
        {
            if (error != 0) throw new Win32Exception(error);
            if (disposition != 1 /* REG_CREATED_NEW_KEY */) return false;
            // Existence is the claim. A failed timestamp write leaves it claimed, so a retry fails closed.
            using var claim = RegistryKey.FromHandle(handle, key.View);
            claim.SetValue(ClaimTimestamp, timestamp, RegistryValueKind.QWord);
        }
        return true;
    }

    private static bool IsNonce(string nonce) => nonce.Length == 32 && nonce.All(Uri.IsHexDigit);

    private static void ValidateNonce(string nonce)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        if (!IsNonce(nonce)) throw new ArgumentException("A plan identifier must contain 32 hexadecimal characters.", nameof(nonce));
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegCreateNonceKey(SafeRegistryHandle parent, string subKey, uint reserved, string? className,
        uint options, int access, nint securityAttributes, out SafeRegistryHandle result, out uint disposition);
}
