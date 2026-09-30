using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release issue I21: FileCat's window runs without administrator rights (AI-05), so the Registry must be browsable
/// that way in every view. The checks run on a thread impersonating a restricted copy of this process's token in which
/// the Administrators group is deny-only, as it is for a standard user or an administrator without elevation, so they
/// hold in elevated CI too.
/// </summary>
public sealed partial class RegistryStandardUserTests
{
    [Fact]
    public void Explicit_views_of_machine_and_user_hives_open_without_administrator_rights()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Registry is Windows'.");
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        var problems = new List<string>();
        AsStandardUser(() =>
        {
            foreach (var path in new[] { "HKLM", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\Microsoft", "HKU", $@"HKU\{sid}\Software", $@"HKU\{sid}_Classes", "HKCC" })
                foreach (var view in new[] { "default", "32", "64" })
                {
                    try
                    {
                        using var key = WindowsRegistryProvider.Open(new Location(Schemes.Registry, path, session: view), false);
                        _ = key.SubKeyCount;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or Win32Exception or System.Security.SecurityException)
                    {
                        problems.Add($"{path} [{view}]: {ex.GetType().Name}");
                    }
                }
        });
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void Each_view_of_the_machine_software_key_lists_what_Windows_lists_in_that_view()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Registry is Windows'.");
        IReadOnlyList<string> in32 = [], in64 = [];
        AsStandardUser(() =>
        {
            using (var k = WindowsRegistryProvider.Open(new Location(Schemes.Registry, @"HKLM\SOFTWARE", session: "32"), false)) in32 = k.GetSubKeyNames();
            using (var k = WindowsRegistryProvider.Open(new Location(Schemes.Registry, @"HKLM\SOFTWARE", session: "64"), false)) in64 = k.GetSubKeyNames();
        });
        // The oracle: .NET's own view-specific open of the same key (the 32-bit view lists a WOW6432Node entry too).
        string[] Windows(Microsoft.Win32.RegistryView view)
        {
            using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
            using var software = root.OpenSubKey("SOFTWARE")!;
            return software.GetSubKeyNames();
        }
        Assert.Equal(Windows(Microsoft.Win32.RegistryView.Registry32).Order(StringComparer.OrdinalIgnoreCase), in32.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Windows(Microsoft.Win32.RegistryView.Registry64).Order(StringComparer.OrdinalIgnoreCase), in64.Order(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Runs <paramref name="action"/> as this user with the Administrators group deny-only.</summary>
    private static void AsStandardUser(Action action)
    {
        using var self = WindowsIdentity.GetCurrent(TokenAccessLevels.Duplicate | TokenAccessLevels.Query | TokenAccessLevels.Impersonate | TokenAccessLevels.AssignPrimary);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var sid = new byte[admins.BinaryLength];
        admins.GetBinaryForm(sid, 0);
        var sidMemory = Marshal.AllocHGlobal(sid.Length);
        try
        {
            Marshal.Copy(sid, 0, sidMemory, sid.Length);
            var disable = new SidAndAttributes { Sid = sidMemory, Attributes = 0 };
            if (!CreateRestrictedToken(self.AccessToken, DisableMaxPrivilege, 1, ref disable, 0, 0, 0, 0, out var restricted))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            using (restricted) WindowsIdentity.RunImpersonated(restricted, action);
        }
        finally { Marshal.FreeHGlobal(sidMemory); }
    }

    private const uint DisableMaxPrivilege = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateRestrictedToken(SafeAccessTokenHandle existing, uint flags, uint disableSidCount, ref SidAndAttributes sidsToDisable,
        uint deletePrivilegeCount, nint privilegesToDelete, uint restrictedSidCount, nint sidsToRestrict, out SafeAccessTokenHandle newToken);
}
