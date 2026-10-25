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
        Assert.Null(ProcessIdentity.ImagePath(0)); // The recovery census checks its PID and error separately.
        Assert.Null(ProcessIdentity.ImagePath(-1));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\Program Files\\dotnet\\sdk\\10.0.101\\MSBuild.dll\" /nodemode:1", false)]
    [InlineData("dotnet C:\\apps\\FileCat.dll --data D:\\state", true)]
    [InlineData("dotnet exec \"C:\\apps\\FileCat.dll\"", true)]
    [InlineData("dotnet C:\\apps\\OtherFileCat.dll", true)] // Conservative false positive, never a false negative.
    public void Managed_host_command_identification_distinguishes_unrelated_dotnet_workers(string command, bool expected)
    {
        Assert.Equal(expected, ProcessIdentity.IsFileCatManagedHostCommand(command));
    }

    [Fact]
    public void Missing_managed_host_command_never_clears_a_device_scan()
    {
        Assert.Null(ProcessIdentity.IsFileCatManagedHostCommand(null));
        Assert.Null(ProcessIdentity.IsFileCatManagedHostCommand("  "));
        Assert.Null(ProcessIdentity.CommandLine(-1));
    }

    [Theory]
    [InlineData(true, false, true)] // A FileCat apphost renamed dotnet.exe must still be caught.
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(null, false, null)]
    [InlineData(false, null, null)]
    public void Dotnet_named_processes_need_both_image_and_command_to_clear(
        bool? apphost, bool? managed, bool? expected)
    {
        Assert.Equal(expected, ProcessIdentity.CombineAppHostAndManagedCommand(apphost, managed));
    }

    [Fact]
    public void Windows_reads_a_live_non_FileCat_host_command_without_memory_access()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows WMI process metadata.");
        string? command = ProcessIdentity.CommandLine(Environment.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(command));
        Assert.False(ProcessIdentity.IsFileCatManagedHostCommand(command));
    }

    [Theory]
    [InlineData(0, "Idle", 0, 87)]
    [InlineData(4, "System", 0, 31)]
    [InlineData(164, "Registry", 0, 31)]
    [InlineData(3376, "Memory Compression", 0, 31)]
    [InlineData(336, "Secure System", 0, 31)]
    [InlineData(12332, "vmmemCmZygote", 0, 31)]
    public void Only_kernel_bookkeeping_entries_with_the_expected_no_image_failure_are_exempt(
        int pid, string name, int session, int error)
    {
        var missing = new ProcessIdentity.ImagePathResult(null, error);
        Assert.True(ProcessIdentity.IsKernelOnlyProcess(pid, name, session, missing));
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(pid, name, session, new("C:\\app.exe", 0)));
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(pid, name, session, new(null, 5 /* access denied */)));
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(pid, name, session, new(null, 0)));
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(pid, "FileCat", session, missing));
        if (pid is not 0 and not 4)
            Assert.False(ProcessIdentity.IsKernelOnlyProcess(pid, name, 1, missing));
    }

    [Fact]
    public void A_same_named_user_process_or_wrong_kernel_pid_never_gets_the_exemption()
    {
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(25, "Idle", 0, new(null, 87)));
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(25, "System", 0, new(null, 31)));
        Assert.False(ProcessIdentity.IsKernelOnlyProcess(4, "Registry", 0, new(null, 31)));
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
                    var blocked = ProcessIdentity.QueryImagePath(child.Id);
                    Assert.Null(blocked.Path);
                    Assert.Equal(5, blocked.Error);
                    Assert.False(ProcessIdentity.IsKernelOnlyProcess(child.Id, "Registry", 0, blocked));
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
