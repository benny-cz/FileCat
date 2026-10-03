using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ProcessIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public void A_limited_query_reads_the_current_executable_and_unavailable_identity_stays_unknown()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows process metadata API.");
        Assert.Equal(Environment.ProcessPath, ProcessIdentity.ImagePath(Environment.ProcessId), ignoreCase: true);
        Assert.Null(ProcessIdentity.ImagePath(0)); // Idle is not openable; this is not an absence exemption.
        Assert.Null(ProcessIdentity.ImagePath(-1));
    }

    [Fact]
    public unsafe void A_process_with_unreadable_modules_is_identified_without_memory_access_and_query_denial_is_unknown()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows process metadata permissions.");
        string executable = Environment.GetEnvironmentVariable("ComSpec")!;
        using var child = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            ArgumentList = { "/d", "/c", "set /p filecat_identity_wait=" },
        })!;
        // Retain the original handle before limiting this owned child's future opens. Cleanup uses that handle.
        var ownedHandle = child.SafeHandle;
        try
        {
            SetOwnedDacl(ownedHandle, "D:(D;;0x410;;;WD)(A;;GA;;;WD)"); // Deny QUERY_INFORMATION and VM_READ.
            // Elevated runners can bypass a process DACL with debug privileges. Restrict only this test's
            // impersonation scope, so both positive/negative permission controls exercise the actual DACL.
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate | TokenAccessLevels.Impersonate);
            if (!CreateRestrictedToken(identity.AccessToken, 1 /* DISABLE_MAX_PRIVILEGE */, 0, 0, 0, 0, 0, 0, out var restricted))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            using (restricted)
                WindowsIdentity.RunImpersonated(restricted, () =>
                {
                    using var fresh = Process.GetProcessById(child.Id);
                    var denied = Assert.Throws<Win32Exception>(() => _ = fresh.MainModule);
                    Assert.Equal(5, denied.NativeErrorCode);
                    string? image = ProcessIdentity.ImagePath(child.Id);
                    Assert.Equal(executable, image, ignoreCase: true);
                    output.WriteLine($"Owned child {child.Id}: MainModule error {denied.NativeErrorCode}; limited image {image}");

                    SetOwnedDacl(ownedHandle, "D:(D;;0x1000;;;WD)(A;;GA;;;WD)"); // Deny the limited query as a negative control.
                    Assert.Null(ProcessIdentity.ImagePath(child.Id));
                    output.WriteLine($"Owned child {child.Id}: limited query denial returns unknown.");
                });
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
        }
    }

    private static unsafe void SetOwnedDacl(SafeProcessHandle process, string sddl)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        fixed (byte* data = bytes)
            if (!SetKernelObjectSecurity(process, 4 /* DACL_SECURITY_INFORMATION */, data))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetKernelObjectSecurity(SafeProcessHandle process, uint information, byte* descriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateRestrictedToken(SafeAccessTokenHandle token, uint flags, uint disableSids, nint sids,
        uint deletePrivileges, nint privileges, uint restrictSids, nint restrictedSids, out SafeAccessTokenHandle restricted);
}
