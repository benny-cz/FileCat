using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// An elevated FileCat must not hand a worker that parses hostile input more authority than a standard session
/// has. Low integrity alone restricts writes: a token that still carries an enabled Administrators SID reads
/// everything administrators may read, and one that keeps its privileges can debug processes and bypass file
/// security. These controls check the worker token itself, so they hold wherever the suite runs.
/// </summary>
public sealed partial class RestrictedProcessLowTokenTests
{
    private const string AdminOnlySentinel = "FILECAT-ADMIN-ONLY-SENTINEL-7b1f";
    private const string UserReadableSentinel = "FILECAT-USER-READABLE-SENTINEL-7b1f";

    [Fact]
    public void An_elevated_parent_gives_its_worker_no_enabled_administrative_group()
    {
        RequireElevatedAdministrator();
        nint worker = WorkerToken();
        try
        {
            Assert.False(HasEnabledAdministratorsGroup(worker),
                "The low-integrity worker inherited an enabled Administrators SID from the elevated parent.");
        }
        finally { CloseHandle(worker); }
    }

    [Fact]
    public void An_elevated_parents_worker_token_holds_no_privilege_beyond_change_notify()
    {
        RequireElevatedAdministrator();
        nint worker = WorkerToken();
        try
        {
            var privileges = PrivilegeNames(worker);
            Assert.True(privileges.All(p => p == "SeChangeNotifyPrivilege"),
                "The low-integrity worker token holds: " + string.Join(", ", privileges));
        }
        finally { CloseHandle(worker); }
    }

    [Fact]
    public void An_elevated_parents_worker_token_cannot_read_a_file_only_administrators_may_read()
    {
        RequireElevatedAdministrator();
        var directory = Directory.CreateTempSubdirectory("filecat-worker-acl-");
        string adminOnly = Path.Combine(directory.FullName, "admin-only.txt");
        string userReadable = Path.Combine(directory.FullName, "user-readable.txt");
        nint worker = 0;
        try
        {
            File.WriteAllText(userReadable, UserReadableSentinel);
            File.WriteAllText(adminOnly, AdminOnlySentinel);
            new FileInfo(adminOnly).SetAccessControl(AdministratorsOnly());

            worker = WorkerToken();
            // The parent itself reads both; only the worker's authority is in question.
            Assert.Equal(AdminOnlySentinel, File.ReadAllText(adminOnly));
            Assert.True(CanRead(worker, userReadable), "The worker token cannot read the user's own ordinary file.");
            Assert.False(CanRead(worker, adminOnly), "The worker token can read a file only administrators may read.");
        }
        finally
        {
            if (worker != 0) CloseHandle(worker);
            Cleanup(directory, adminOnly);
        }
    }

    /// <summary>The same boundary through an actual started worker, the kind of program FileCat really starts.</summary>
    [Fact]
    public async Task An_elevated_parents_running_worker_cannot_read_a_file_only_administrators_may_read()
    {
        RequireElevatedAdministrator();
        var directory = Directory.CreateTempSubdirectory("filecat-worker-live-");
        string adminOnly = Path.Combine(directory.FullName, "admin-only.txt");
        string userReadable = Path.Combine(directory.FullName, "user-readable.txt");
        try
        {
            File.WriteAllText(userReadable, UserReadableSentinel);
            File.WriteAllText(adminOnly, AdminOnlySentinel);
            new FileInfo(adminOnly).SetAccessControl(AdministratorsOnly());

            using var worker = RestrictedProcess.Start(WorkerProbe, $"read \"{userReadable}\" \"{adminOnly}\"",
                lowIntegrity: true, memoryLimitBytes: 64L << 20);
            using var reader = new StreamReader(worker.Output);
            string output = await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.True(worker.LowIntegrity);
            Assert.Contains(UserReadableSentinel, output);
            Assert.Contains("refused admin-only.txt", output);
            Assert.DoesNotContain(AdminOnlySentinel, output);
        }
        finally { Cleanup(directory, adminOnly); }
    }

    /// <summary>FileCat's own helpers are plain .NET programs; this probe stands in for one.</summary>
    internal static string WorkerProbe
    {
        get
        {
            string probe = Path.Combine(AppContext.BaseDirectory, "FileCat.WorkerProbe.exe");
            if (!File.Exists(probe)) Assert.Fail("FileCat.WorkerProbe.exe is not beside the tests.");
            return probe;
        }
    }

    private static FileSecurity AdministratorsOnly()
    {
        var security = new FileSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetGroup(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static void Cleanup(DirectoryInfo directory, string adminOnly)
    {
        try
        {
            if (File.Exists(adminOnly))
            {
                var open = new FileSecurity();
                open.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
                new FileInfo(adminOnly).SetAccessControl(open);
            }
        }
        catch (UnauthorizedAccessException) { }
        finally { try { directory.Delete(recursive: true); } catch (IOException) { } }
    }

    private static void RequireElevatedAdministrator()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows worker token boundary.");
        using var parent = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(parent).IsInRole(WindowsBuiltInRole.Administrator))
            Assert.Skip("An elevated administrator parent is required.");
    }

    private static nint WorkerToken()
    {
        var method = typeof(RestrictedProcess).GetMethod("LowIntegrityToken", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(RestrictedProcess), "LowIntegrityToken");
        nint worker = (nint)(method.Invoke(null, null) ?? 0);
        Assert.NotEqual(0, worker);
        return worker;
    }

    /// <summary>Windows' own answer for this token and file, without starting anything.</summary>
    private static unsafe bool CanRead(nint token, string path)
    {
        if (!DuplicateTokenEx(token, TOKEN_QUERY | TOKEN_DUPLICATE, 0, SecurityImpersonation, TokenImpersonation, out nint client))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            var descriptor = new FileInfo(path).GetAccessControl(AccessControlSections.All).GetSecurityDescriptorBinaryForm();
            var mapping = new GENERIC_MAPPING
            {
                GenericRead = FILE_GENERIC_READ,
                GenericWrite = 0x120116,
                GenericExecute = 0x1200A0,
                GenericAll = 0x1F01FF,
            };
            var privileges = new byte[sizeof(uint) * 3 + 16];
            uint privilegeSize = (uint)privileges.Length;
            fixed (byte* raw = descriptor)
            fixed (byte* set = privileges)
            {
                if (!AccessCheck((nint)raw, client, FILE_GENERIC_READ, &mapping, (nint)set, ref privilegeSize,
                        out uint granted, out int allowed))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                return allowed != 0 && (granted & FILE_READ_DATA) != 0;
            }
        }
        finally { CloseHandle(client); }
    }

    private static bool HasEnabledAdministratorsGroup(nint token)
    {
        nint data = TokenInformation(token, TokenGroups);
        try
        {
            int count = Marshal.ReadInt32(data);
            for (int i = 0; i < count; i++)
            {
                // DWORD GroupCount, then SID_AND_ATTRIBUTES entries aligned to a pointer.
                nint entry = data + IntPtr.Size + i * (2 * IntPtr.Size);
                if ((Marshal.ReadInt32(entry, IntPtr.Size) & 0x4 /* SE_GROUP_ENABLED */) != 0 &&
                    new SecurityIdentifier(Marshal.ReadIntPtr(entry)).IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
                    return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private static List<string> PrivilegeNames(nint token)
    {
        nint data = TokenInformation(token, TokenPrivileges);
        try
        {
            var names = new List<string>();
            int count = Marshal.ReadInt32(data);
            for (int i = 0; i < count; i++)
            {
                nint entry = data + sizeof(int) + i * 12; // DWORD count, then packed LUID_AND_ATTRIBUTES
                long luid = (uint)Marshal.ReadInt32(entry) | ((long)Marshal.ReadInt32(entry, 4) << 32);
                int length = 64;
                var name = new char[length];
                names.Add(LookupPrivilegeNameW(null, ref luid, name, ref length) ? new string(name, 0, length) : "LUID " + luid);
            }
            return names;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private static nint TokenInformation(nint token, int information)
    {
        GetTokenInformation(token, information, 0, 0, out int needed);
        nint data = Marshal.AllocHGlobal(needed);
        if (!GetTokenInformation(token, information, data, needed, out _))
        {
            int error = Marshal.GetLastPInvokeError();
            Marshal.FreeHGlobal(data);
            throw new Win32Exception(error);
        }
        return data;
    }

    private const int TokenGroups = 2, TokenPrivileges = 3;
    private const uint TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8;
    private const int SecurityImpersonation = 2, TokenImpersonation = 2;
    private const uint FILE_READ_DATA = 0x1, FILE_GENERIC_READ = 0x120089;

    [StructLayout(LayoutKind.Sequential)]
    private struct GENERIC_MAPPING
    {
        public uint GenericRead, GenericWrite, GenericExecute, GenericAll;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int information, nint data, int size, out int needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(nint token, uint access, nint attributes, int level, int type, out nint copy);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool AccessCheck(nint descriptor, nint clientToken, uint desiredAccess, GENERIC_MAPPING* mapping,
        nint privilegeSet, ref uint privilegeSetLength, out uint grantedAccess, out int accessStatus);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeNameW(string? systemName, ref long luid, [Out] char[] name, ref int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
