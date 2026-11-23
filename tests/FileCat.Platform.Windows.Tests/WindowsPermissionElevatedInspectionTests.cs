using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The read-only inspection an administrator (high-integrity) FileCat window runs with its own token in place of the
/// UAC-approved helper. The refusal runs at medium integrity. The reads need a high-integrity process whose backup and
/// security privileges are present but not already enabled: started from PowerShell or Explorer, not from Git Bash.
/// </summary>
public sealed partial class WindowsPermissionElevatedInspectionTests
{
    private const PermissionSections Requested = WindowsPermissionService.DiscretionarySections | PermissionSections.Audit;
    private const uint ReadControl = 0x20000, AccessSystemSecurity = 0x1000000, FileGenericRead = 0x120089;
    private const uint DaclInformation = 4, SaclInformation = 8, BackupSecurityInformation = 0x10000;
    private static readonly PermissionAuthority Authority = new(PermissionAuthorityKind.Local, "local", 0);

    // Administrators are denied READ_CONTROL. The OWNER RIGHTS entry withdraws the owner's implicit READ_CONTROL, which
    // would otherwise let this process read the DACL anyway: its token owns the file. Everyone's reads are audited.
    private const string Fixture = "D:P(D;;RC;;;BA)(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;OW)S:P(AU;SAFA;FR;;;WD)";

    [Fact]
    public void An_ordinary_window_refuses_the_elevated_read_and_keeps_its_binding()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Reads Windows security.");
        Assert.SkipWhen(WindowsTokenAuthority.IsHighIntegrity, "Runs at medium integrity; this test process is elevated.");
        var directory = Directory.CreateTempSubdirectory("filecat-acl-elevated-refused-");
        try
        {
            string path = Path.Combine(directory.FullName, "ordinary.txt");
            File.WriteAllText(path, "ordinary");
            var ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(Subject(path), PermissionSections.Ordinary | PermissionSections.Audit, ct);
            Assert.SkipUnless(WindowsPermissionService.CanBindAuthorizedInspection(inspection.Snapshot),
                "Requires a temporary folder on a fixed local NTFS volume.");
            Assert.False(WindowsPermissionService.CanInspectElevated(inspection.Snapshot));
            var refusal = Assert.Throws<NotSupportedException>(() => WindowsPermissionService.InspectElevated(inspection, Requested, ct));
            Assert.Equal("Only an administrator FileCat window reads Windows security with its own privileges.", refusal.Message);
            // Refused before anything was opened or read: the original inspection still holds its binding.
            using var binding = inspection.TransferLease<IDisposable>();
            Assert.NotNull(binding);
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(directory.FullName); }
    }

    [Fact]
    public void An_administrator_window_reads_audit_rules_and_a_DACL_denied_to_Administrators_without_changing_them()
    {
        RequireAdministratorWindow();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-elevated-");
        try
        {
            string path = CreateFixture(directory.FullName);
            var (readDenial, auditDenial) = Denials(path);
            Assert.SkipWhen(readDenial == 0 || auditDenial == 0,
                "This process already has backup or security privilege enabled (Git Bash enables them); run it from PowerShell or Explorer.");
            Assert.Equal(5, readDenial);
            Assert.True(auditDenial is 5 or 1314, new Win32Exception(auditDenial).Message);
            byte[] before = StoredDescriptor(path);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(Subject(path), PermissionSections.Ordinary | PermissionSections.Audit, ct);
            var ordinary = inspection.Snapshot;
            Assert.True(ordinary.Binding is { FileSystem: "NTFS" }, ordinary.BindingProblem);
            // Without backup intent this window cannot read the DACL, and the ordinary reader reads no audit rules.
            Assert.Equal(PermissionReadStatus.Denied, ordinary.Access.Status);
            Assert.Equal(PermissionReadStatus.Unsupported, ordinary.Audit.Status);
            Assert.True(WindowsPermissionService.CanInspectElevated(ordinary));

            using var elevated = WindowsPermissionService.InspectElevated(inspection, Requested, ct);
            Assert.Null(inspection.TransferLease<IDisposable>());
            var snapshot = elevated.Snapshot;
            Assert.Same(ordinary.Binding, snapshot.Binding);
            var stored = new RawSecurityDescriptor(before, 0);
            Assert.Equal(PermissionReadStatus.Present, snapshot.Owner.Status);
            Assert.Equal(stored.Owner!.Value, snapshot.Owner.Value?.Id);
            Assert.Equal(stored.Group?.Value, snapshot.Group.Value?.Id);
            Assert.Equal(PermissionReadStatus.Present, snapshot.Access.Status);
            var dacl = Assert.IsType<WindowsDacl>(snapshot.Access.Value?.Acl);
            Assert.Equal(AclBytes(stored.DiscretionaryAcl), AclBytes(new RawSecurityDescriptor(dacl.SecurityDescriptor.ToArray(), 0).DiscretionaryAcl));
            Assert.Contains(dacl.Entries, entry => entry is { Kind: WindowsAceKind.Deny, Mask: ReadControl } && entry.Principal?.Id == "S-1-5-32-544");
            Assert.Equal((stored.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0, snapshot.Inheritance.Value!.Value.IsProtected);

            Assert.Equal(PermissionReadStatus.Present, snapshot.Audit.Status);
            var sacl = Assert.IsType<WindowsSacl>(snapshot.Audit.Value?.Acl);
            var audit = Assert.Single(sacl.Entries);
            Assert.Equal("S-1-1-0", audit.Principal?.Id);
            Assert.Equal(FileGenericRead, audit.Mask);
            Assert.True(audit.AuditsSuccess && audit.AuditsFailure);
            Assert.Equal(AclBytes(stored.SystemAcl), sacl.Bytes.ToArray());
            Assert.Contains(snapshot.Capabilities, capability => capability is
                { Section: PermissionSections.Audit, Action: PermissionAction.Inspect, Availability: PermissionAvailability.Available });
            // Each section names the native call that read it, as the ordinary and helper reads do.
            Assert.Equal(ordinary.Access.Source, snapshot.Access.Source);
            Assert.Equal(ordinary.Access.Source, snapshot.Audit.Source);
            using (var binding = elevated.TransferLease<IDisposable>()) Assert.NotNull(binding);

            Assert.Equal(before, StoredDescriptor(path));
            // The privileges stayed with the reads: this thread is not impersonating and neither privilege is enabled now.
            Assert.Null(WindowsIdentity.GetCurrent(ifImpersonating: true));
            Assert.Equal((readDenial, auditDenial), Denials(path));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(directory.FullName); }
    }

    [Theory]
    [InlineData("renamed")]
    [InlineData("replaced")]
    [InlineData("security changed")]
    public void The_elevated_read_refuses_an_object_that_changed_after_its_inspection(string change)
    {
        RequireAdministratorWindow();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-elevated-stale-");
        try
        {
            string path = CreateFixture(directory.FullName), moved = Path.Combine(directory.FullName, "moved.txt");
            var ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(Subject(path), PermissionSections.Ordinary | PermissionSections.Audit, ct);
            Assert.True(WindowsPermissionService.CanInspectElevated(inspection.Snapshot), inspection.Snapshot.BindingProblem);
            if (change == "security changed") SetSecurity(path, Fixture.Replace("FR;;;WD", "FA;;;WD", StringComparison.Ordinal));
            else
            {
                File.Move(path, moved);
                if (change == "replaced") File.WriteAllText(path, "replacement");
            }
            string original = change == "security changed" ? path : moved;
            byte[] before = StoredDescriptor(original);
            byte[]? replacement = change == "replaced" ? StoredDescriptor(path) : null;

            Assert.Throws<PermissionBindingConflictException>(() => WindowsPermissionService.InspectElevated(inspection, Requested, ct));
            // The failed read took the binding and released it.
            Assert.Null(inspection.TransferLease<IDisposable>());
            Assert.Equal(before, StoredDescriptor(original));
            if (replacement is not null) Assert.Equal(replacement, StoredDescriptor(path));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(directory.FullName); }
    }

    // The helper's entry point and the administrator window share one read: for the same object they return the same
    // revision, and the helper still refuses a step whose reviewed file IDs name another object.
    [Fact]
    public void The_helper_read_agrees_with_the_administrator_window_and_refuses_another_object()
    {
        RequireAdministratorWindow();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-elevated-helper-");
        try
        {
            string path = CreateFixture(directory.FullName), other = Path.Combine(directory.FullName, "other.txt");
            File.WriteAllText(other, "other");
            var ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(Subject(path), PermissionSections.Ordinary | PermissionSections.Audit, ct);
            var binding = Assert.IsType<PermissionBinding>(inspection.Snapshot.Binding);
            // The step an installed requester sends for this object: its volume GUID path and every root-to-leaf file ID.
            string[] names = path[3..].Split('\\');
            string[] ids = Enumerable.Range(0, names.Length + 1).Select(depth => FileId(path[..3] + string.Join('\\', names[..depth]))).ToArray();
            Assert.Equal(binding.Identity.Object.FileId, ids[^1]);
            var scope = new ElevatedFileSecurityScope(binding.Identity.Object.VolumeSerial.ToString("X16", CultureInfo.InvariantCulture), ids,
                PermissionObjectKind.File, binding.LinkCount!.Value, 1, 1, "S-1-5-5-0-1", new string('4', 48), Requested, PermissionScope.Objects);
            var step = new ElevatedStep(ElevatedVerb.InspectFileSecurity)
            {
                Path = NtfsMetadataVolume.CanonicalGuid(binding.MountRoot) + string.Join('\\', names),
                FileSecurity = scope,
            };
            var helper = WindowsPermissionService.DecodeAuthorizedInspection(inspection.Snapshot, Requested,
                WindowsPermissionService.ReadAuthorized(step, ct));
            Assert.Equal(PermissionReadStatus.Present, helper.Access.Status);
            Assert.Equal(PermissionReadStatus.Present, helper.Audit.Status);
            var revision = PermissionRevision.Read(helper, Requested);
            Assert.NotNull(revision);
            using var elevated = WindowsPermissionService.InspectElevated(inspection, Requested, ct);
            Assert.Equal(revision, PermissionRevision.Read(elevated.Snapshot, Requested));

            var refused = WindowsPermissionService.ReadAuthorized(step with { FileSecurity = scope with { FileIds = [.. ids[..^1], FileId(other)] } }, ct);
            Assert.Equal(PermissionReadStatus.Stale, refused.Discretionary.Status);
            Assert.Equal(PermissionReadStatus.Stale, refused.Audit.Status);
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(directory.FullName); }
    }

    [Fact]
    public void An_administrator_window_refuses_an_unbound_object_or_other_sections_and_keeps_the_binding()
    {
        RequireAdministratorWindow();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-elevated-sections-");
        try
        {
            string path = Path.Combine(directory.FullName, "ordinary.txt");
            File.WriteAllText(path, "ordinary");
            var ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(Subject(path), PermissionSections.Ordinary, ct);
            Assert.True(WindowsPermissionService.CanInspectElevated(inspection.Snapshot), inspection.Snapshot.BindingProblem);
            foreach (var sections in new[] { PermissionSections.None, PermissionSections.Mode, Requested | PermissionSections.Flags })
                Assert.Equal("Request only Windows owner, group, access, inheritance or audit sections.",
                    Assert.Throws<NotSupportedException>(() => WindowsPermissionService.InspectElevated(inspection, sections, ct)).Message);
            using (var binding = inspection.TransferLease<IDisposable>()) Assert.NotNull(binding);

            using var unbound = PermissionSnapshots.Unavailable(Subject(path), Requested, PermissionReadStatus.Unsupported, "Not bound.");
            Assert.False(WindowsPermissionService.CanInspectElevated(unbound.Snapshot));
            Assert.Equal("A bound local Windows object on a fixed NTFS volume is required.",
                Assert.Throws<NotSupportedException>(() => WindowsPermissionService.InspectElevated(unbound, Requested, ct)).Message);
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(directory.FullName); }
    }

    [Fact]
    public void A_failed_read_names_this_administrator_window_or_the_approving_administrator()
    {
        var snapshot = PermissionSnapshots.Create(Subject(@"C:\fixture\file.txt"), PermissionModel.WindowsDescriptor,
            new PermissionBinding(new(new(1, "2"), "volume", PermissionObjectKind.File, Authority), "NTFS",
                @"\\?\Volume{00000000-0000-0000-0000-000000000001}\", 1, false), Requested, DateTimeOffset.UtcNow, "test");
        var noPrivilege = new FileSecurityInspection(new(PermissionReadStatus.Denied, NativeError: 1314), new(PermissionReadStatus.Denied, NativeError: 1300));
        var failed = new FileSecurityInspection(new(PermissionReadStatus.Stale), new(PermissionReadStatus.Failed, NativeError: 5));
        foreach (var (window, access, audit, accessFailed, auditFailed) in new[]
        {
            (false, "The approving administrator's Windows token lacks the backup privilege needed to read this section.",
                "The approving administrator's Windows token lacks the backup or system security privilege needed to read audit rules.",
                "The helper could not read the requested Windows security section.", "The helper could not read the requested Windows audit ACL."),
            (true, "This administrator window's Windows token lacks the backup privilege needed to read this section.",
                "This administrator window's Windows token lacks the backup or system security privilege needed to read audit rules.",
                "This administrator window could not read the requested Windows security section.",
                "This administrator window could not read the requested Windows audit ACL."),
        })
        {
            var denied = WindowsPermissionService.DecodeAuthorizedInspection(snapshot, Requested, noPrivilege, window);
            foreach (var section in new[] { denied.Owner.Reason, denied.Group.Reason, denied.Access.Reason, denied.Inheritance.Reason })
                Assert.Equal(access, section);
            Assert.Equal(PermissionReadStatus.Denied, denied.Access.Status);
            Assert.Equal(1314, denied.Access.NativeError);
            Assert.Equal(PermissionReadStatus.Denied, denied.Audit.Status);
            Assert.Equal(audit, denied.Audit.Reason);
            Assert.Equal(1300, denied.Audit.NativeError);
            var unread = WindowsPermissionService.DecodeAuthorizedInspection(snapshot, Requested, failed, window);
            Assert.Equal(PermissionReadStatus.Stale, unread.Access.Status);
            Assert.Equal(accessFailed, unread.Access.Reason);
            Assert.Equal(PermissionReadStatus.Failed, unread.Audit.Status);
            Assert.Equal(auditFailed, unread.Audit.Reason);
            Assert.Equal(5, unread.Audit.NativeError);
        }
    }

    private static void RequireAdministratorWindow() =>
        Assert.SkipUnless(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity && ElevationBroker.IsNativeFileSecurityHost,
            "Requires a high-integrity native x64 test process on Windows 11.");

    private static PermissionSubject Subject(string path) => new(Location.FileSystem(Path.GetDirectoryName(path)!), path, path,
        Authority, PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);

    private static string CreateFixture(string directory)
    {
        string path = Path.Combine(directory, "audited.txt");
        File.WriteAllText(path, "audited");
        SetSecurity(path, Fixture);
        return path;
    }

    // Writing a SACL needs the system security privilege, enabled for this thread alone.
    private static void SetSecurity(string path, string sddl)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        int error = WithPrivileges(["SeSecurityPrivilege"], () =>
            SetFileSecurity(path, DaclInformation | SaclInformation, bytes) ? 0 : Marshal.GetLastPInvokeError());
        Assert.True(error == 0, new Win32Exception(error).Message);
    }

    private static string FileId(string path)
    {
        using var handle = CreateFile(path, 0x80 /* FILE_READ_ATTRIBUTES */, 7, 0, 3, 0x02000000 | 0x00200000, 0);
        Assert.False(handle.IsInvalid, $"{path}: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
        return ProtectedHexFile.Identity(handle).FileId;
    }

    // Opens for the DACL and for the SACL without privileges, as a backup application would: 0 when either is granted.
    private static (int Read, int Audit) Denials(string path) => (OpenError(path, ReadControl), OpenError(path, AccessSystemSecurity));

    private static int OpenError(string path, uint access)
    {
        using var handle = CreateFile(path, access, 7, 0, 3 /* OPEN_EXISTING */, 0x02000000 | 0x00200000 /* backup semantics, open reparse point */, 0);
        return handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
    }

    // The complete stored descriptor, read with backup and security privilege enabled for this thread alone.
    private static unsafe byte[] StoredDescriptor(string path) => WithPrivileges(["SeBackupPrivilege", "SeSecurityPrivilege"], () =>
    {
        using var handle = CreateFile(path, ReadControl | AccessSystemSecurity, 7, 0, 3, 0x02000000 | 0x00200000, 0);
        Assert.False(handle.IsInvalid, $"{path}: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
        _ = GetKernelObjectSecurity(handle, BackupSecurityInformation, null, 0, out uint length);
        Assert.InRange(length, 20u, 65536u);
        byte[] bytes = new byte[length];
        fixed (byte* buffer = bytes)
            Assert.True(GetKernelObjectSecurity(handle, BackupSecurityInformation, buffer, length, out length),
                new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        return bytes[..(int)length];
    });

    private static byte[] AclBytes(GenericAcl? acl)
    {
        Assert.NotNull(acl);
        byte[] bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    // Enables the named privileges on a copy of the process token that only this thread uses, so other tests running
    // meanwhile keep their own access.
    private static unsafe T WithPrivileges<T>(string[] names, Func<T> action)
    {
        Assert.True(ImpersonateSelf(2 /* SecurityImpersonation */), $"ImpersonateSelf: {Marshal.GetLastPInvokeError()}");
        try
        {
            Assert.True(OpenThreadToken(GetCurrentThread(), 0x0020 | 0x0008 /* adjust privileges, query */, true, out nint token),
                $"OpenThreadToken: {Marshal.GetLastPInvokeError()}");
            try
            {
                foreach (string name in names)
                {
                    Assert.True(LookupPrivilegeValue(null, name, out long luid), $"LookupPrivilegeValue: {Marshal.GetLastPInvokeError()}");
                    var privileges = new byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(privileges, 1);
                    BinaryPrimitives.WriteInt64LittleEndian(privileges.AsSpan(4), luid);
                    BinaryPrimitives.WriteUInt32LittleEndian(privileges.AsSpan(12), 2 /* SE_PRIVILEGE_ENABLED */);
                    fixed (byte* state = privileges)
                        Assert.True(AdjustTokenPrivileges(token, false, state, 16, 0, 0) && Marshal.GetLastPInvokeError() == 0,
                            $"AdjustTokenPrivileges {name}: {Marshal.GetLastPInvokeError()}");
                }
            }
            finally { CloseHandle(token); }
            return action();
        }
        finally { RevertToSelf(); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetKernelObjectSecurity(SafeFileHandle handle, uint information, byte* descriptor, uint length, out uint needed);

    [LibraryImport("advapi32.dll", EntryPoint = "SetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileSecurity(string path, uint information, byte[] descriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImpersonateSelf(int level);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RevertToSelf();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentThread();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenThreadToken(nint thread, uint access, [MarshalAs(UnmanagedType.Bool)] bool openAsSelf, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, byte* state, uint length,
        nint previous, nint returned);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
