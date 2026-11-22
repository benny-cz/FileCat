using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal static partial class WindowsFileSecurityPrivilege
{
    /// <summary>ERROR_PRIVILEGE_NOT_HELD: the acting token lacks a privilege the reviewed route requires.</summary>
    internal const int PrivilegeNotHeld = 1314;

    /// <summary>True for the errors Windows returns when a required privilege is absent from the token.</summary>
    internal static bool IsMissingPrivilege(long error) => error is PrivilegeNotHeld or 1300 /* ERROR_NOT_ALL_ASSIGNED */;

    /// <summary>The named privileges absent from this process's primary token, enabled or not.</summary>
    internal static IReadOnlyList<string> MissingFromProcess(params string[] names)
    {
        if (!OpenProcessToken(-1, 0x0008 /* TOKEN_QUERY */, out var token))
        {
            token?.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        using (token) return Missing(token, names);
    }

    internal static unsafe IReadOnlyList<string> Missing(SafeAccessTokenHandle token, params string[] names)
    {
        _ = GetTokenInformation(token, 3 /* TokenPrivileges */, null, 0, out uint length);
        if (length is < 4 or > 64 * 1024) throw new Win32Exception(Marshal.GetLastPInvokeError());
        byte[] buffer = new byte[length];
        fixed (byte* data = buffer)
            if (!GetTokenInformation(token, 3, data, length, out length))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        uint count = BitConverter.ToUInt32(buffer, 0);
        if (count > (length - 4) / 12) throw new InvalidDataException("Windows returned an invalid token privilege list.");
        var held = new HashSet<(uint, int)>();
        for (int i = 0; i < count; i++)
            held.Add((BitConverter.ToUInt32(buffer, 4 + i * 12), BitConverter.ToInt32(buffer, 8 + i * 12)));
        var missing = new List<string>();
        foreach (string name in names)
        {
            if (!LookupPrivilegeValue(null, name, out var identifier))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (!held.Contains((identifier.LowPart, identifier.HighPart))) missing.Add(name);
        }
        return missing;
    }

    internal static T Read<T>(bool audit, Func<T> action) =>
        Run(audit ? ["SeBackupPrivilege", "SeSecurityPrivilege"] : ["SeBackupPrivilege"], action);

    internal static T Write<T>(bool audit, Func<T> action) =>
        Run(audit ? ["SeBackupPrivilege", "SeRestorePrivilege", "SeSecurityPrivilege"] : ["SeBackupPrivilege", "SeRestorePrivilege"], action);

    internal static T WithOwnerAccess<T>(Func<T> action) => Run(["SeBackupPrivilege"], action, disableOwnerOverride: true);

    private static T Run<T>(ReadOnlySpan<string> privileges, Func<T> action, bool disableOwnerOverride = false)
    {
        // Adjust a duplicate. The helper's primary token and the consent thread never gain the privilege.
        using var impersonation = WindowsIdentity.GetCurrent(ifImpersonating: true);
        if (impersonation is not null) throw new UnauthorizedAccessException("The file security helper is already impersonating.");
        if (!OpenProcessToken(-1, 0x000a /* TOKEN_QUERY | TOKEN_DUPLICATE */, out var primary))
        {
            primary?.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        using (primary)
        {
            if (!DuplicateTokenEx(primary, 0x002c /* QUERY | IMPERSONATE | ADJUST_PRIVILEGES */, 0,
                    2 /* SecurityImpersonation */, 2 /* TokenImpersonation */, out var token))
            {
                token?.Dispose();
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            using (token)
            {
                if (disableOwnerOverride)
                {
                    SetPrivilege(token, "SeRestorePrivilege", enable: false);
                    SetPrivilege(token, "SeTakeOwnershipPrivilege", enable: false);
                }
                foreach (string name in privileges) SetPrivilege(token, name, enable: true);
                return WindowsIdentity.RunImpersonated(token, action);
            }
        }
    }

    private static void SetPrivilege(SafeAccessTokenHandle token, string name, bool enable)
    {
        if (!LookupPrivilegeValue(null, name, out var identifier))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var requested = new TokenPrivileges { Count = 1, Identifier = identifier, Attributes = enable ? 2u /* SE_PRIVILEGE_ENABLED */ : 0u };
        bool adjusted = AdjustTokenPrivileges(token, false, ref requested, 0, 0, 0);
        int error = Marshal.GetLastPInvokeError();
        if (!adjusted || error != 0 && (enable || error != 1300 /* ERROR_NOT_ALL_ASSIGNED */))
            throw new Win32Exception(error == 0 ? 1314 /* ERROR_PRIVILEGE_NOT_HELD */ : error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { internal uint LowPart; internal int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { internal uint Count; internal Luid Identifier; internal uint Attributes; }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint rights, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetTokenInformation(SafeAccessTokenHandle token, int information, byte* buffer, uint length,
        out uint returned);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(SafeAccessTokenHandle source, uint rights, nint attributes, int impersonation,
        int type, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out Luid identifier);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges requested, uint previousLength, nint previous, nint used);
}
