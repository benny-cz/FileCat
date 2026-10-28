using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FileCat.Core.State;

/// <summary>Refuse persistence through same-account writable paths when Windows gives this process a high token.</summary>
public static class WindowsTokenAuthority
{
    public static bool IsHighIntegrity
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            nint buffer = 0;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                GetTokenInformation(identity.Token, 25 /* TokenIntegrityLevel */, 0, 0, out int length);
                if (length <= 0 || length > 4096) return true;
                buffer = Marshal.AllocHGlobal(length);
                if (!GetTokenInformation(identity.Token, 25, buffer, length, out _)) return true;
                string sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                const string prefix = "S-1-16-";
                return !sid.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(sid.AsSpan(prefix.Length), out int rid) || rid >= 12288;
            }
            catch (Exception ex) when (ex is SystemException or UnauthorizedAccessException) { return true; }
            finally { if (buffer != 0) Marshal.FreeHGlobal(buffer); }
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint buffer, int length, out int needed);
}
