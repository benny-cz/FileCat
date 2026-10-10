using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class RestrictedProcessFallbackTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    [InlineData(8192)]
    public unsafe void A_separate_token_lowered_to_ordinary_integrity_allows_fallback(int integrity)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows worker token boundary.");
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);
        if (!DuplicateTokenEx(identity.AccessToken, 0x88, 0, 2, 1, out var copy))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        using (copy)
        {
            var sid = new SecurityIdentifier($"S-1-16-{integrity}");
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            fixed (byte* address = bytes)
            {
                var label = new Label { SID = (nint)address, Attributes = 0x20 };
                if (!SetTokenInformation(copy, 25, ref label, Marshal.SizeOf<Label>() + bytes.Length))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            Assert.Equal($"S-1-16-{integrity}", ActualIntegritySID(copy));
            Assert.True(RestrictedProcess.CanUseFallbackToken(copy.DangerousGetHandle()));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Unavailable_token_identity_refuses_fallback(int handle)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows worker token boundary.");
        Assert.False(RestrictedProcess.CanUseFallbackToken(handle));
    }

    [Fact]
    public void A_token_handle_without_query_permission_refuses_fallback()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows worker token boundary.");
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);
        if (!DuplicateTokenEx(identity.AccessToken, 2, 0, 2, 1, out var copy))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        using (copy) Assert.False(RestrictedProcess.CanUseFallbackToken(copy.DangerousGetHandle()));
    }

    [Fact]
    public void The_actual_parent_token_allows_fallback_only_up_to_medium_integrity()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows worker token boundary.");
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        string actual = ActualIntegritySID(identity.AccessToken);
        uint integrity = uint.Parse(actual[(actual.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(integrity <= 8192, RestrictedProcess.CanUseFallbackToken(identity.AccessToken.DangerousGetHandle()));
    }

    private static string ActualIntegritySID(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token, 25, 0, 0, out int needed);
        nint data = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetTokenInformation(token, 25, data, needed, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            return new SecurityIdentifier(Marshal.ReadIntPtr(data)).Value;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Label { internal nint SID; internal uint Attributes; }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, nint attributes, int level, int type, out SafeAccessTokenHandle copy);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetTokenInformation(SafeAccessTokenHandle token, int information, ref Label data, int size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int information, nint data, int size, out int needed);
}
