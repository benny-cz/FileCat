using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)] // As format leaves a root: no SE_DACL_AUTO_INHERITED
    [InlineData(true, false)]
    public async Task Selected_NTFS_volume_root_requires_approval_and_preserves_existing_children(bool protect, bool autoInherited = true)
    {
        RequireFixture();
        string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_ACL_WRITE_ROOT");
        if (path is null) Assert.Skip("Requires a second disposable fixed NTFS volume for root writes.");
        var volume = new DriveInfo(path);
        Assert.Equal(path, volume.RootDirectory.FullName);
        Assert.Equal("FileCat ACL Root Probe", volume.VolumeLabel);
        Assert.Equal(DriveType.Fixed, volume.DriveType);
        Assert.Equal("NTFS", volume.DriveFormat);
        Assert.False(path.StartsWith(Environment.GetEnvironmentVariable("SystemDrive")!, StringComparison.OrdinalIgnoreCase));
        var root = volume.RootDirectory;
        byte[] original = ReadDescriptor(path);
        var controlled = root.GetAccessControl(AccessControlSections.Access);
        controlled.SetAccessRuleProtection(protect, preserveInheritance: false);
        root.SetAccessControl(controlled);
        if (!autoInherited)
        {
            // SetFileSecurityW stores the DACL without auto-inheritance, as format leaves a new volume's root.
            var current = new RawSecurityDescriptor(ReadDescriptor(path), 0);
            var legacy = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent |
                (protect ? ControlFlags.DiscretionaryAclProtected : ControlFlags.None), null, null, null, current.DiscretionaryAcl);
            byte[] bytes = new byte[legacy.BinaryLength];
            legacy.GetBinaryForm(bytes, 0);
            Assert.True(SetFileSecurity(path, 4 /* DACL_SECURITY_INFORMATION */, bytes), $"SetFileSecurityW failed: {Marshal.GetLastPInvokeError()}");
        }
        DirectoryInfo? branch = null;
        DirectoryInfo? future = null;
        try
        {
            branch = Directory.CreateDirectory(Path.Combine(path, $"filecat-root-scope-{Guid.NewGuid():N}"));
            string nested = CreateFile(branch, "open.txt");
            using var hold = File.Open(nested, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Children are compared as stored. GetSecurityInfo infers inherited entries of a descriptor that is not
            // auto-inherited from its parent's current DACL, so that view changes with the root even when the child does not.
            byte[] branchBefore = ReadStoredDescriptor(branch.FullName);
            byte[] fileBefore = ReadStoredDescriptor(nested);
            byte[] baseline = ReadDescriptor(path);
            var control = WindowsPermissionDescriptor.Control(baseline);
            Assert.Equal(protect, control.IsProtected);
            Assert.Equal(autoInherited, control.IsAutoInherited);
            Assert.Contains(WindowsPermissionDescriptor.Dacl(baseline, Authority.Id).Entries, rule =>
                rule is { Kind: WindowsAceKind.Allow, Flags: 0x0b, Mask: uint mask } &&
                WindowsFileAccessRights.MapGeneric(mask) != mask);
            var source = ItemRef.ForFileSystemPath(path, EntryKind.Directory);
            var subject = FolderOnlySubject(source) with { Kind = PermissionObjectKind.VolumeRoot };
            var service = new WindowsPermissionService(qualifyVolumeRootWrites: true);
            var ct = TestContext.Current.CancellationToken;
            var change = new AddWindowsAce(WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Deny, WitnessAccount,
                WitnessRight, flags: (byte)(AceFlags.ObjectInherit | AceFlags.ContainerInherit)));
            using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
            var snapshot = inspection.Snapshot;
            Assert.True(service.CanReviewVolumeRoot(snapshot), snapshot.BindingProblem ?? snapshot.Access.Reason);
            Assert.False(service.CanReviewFolderOnly(snapshot));
            Assert.False(new WindowsPermissionService(qualifyFolderOnly: true).CanReviewVolumeRoot(snapshot));
            var draft = new PermissionDraft(PermissionScope.Objects, [change]);
            Assert.Throws<NotSupportedException>(() => service.PrepareFolderOnly(inspection, source, draft, ct));
            Assert.Throws<NotSupportedException>(() => WindowsPermissionService.Prepare(inspection, source, draft, ct));
            var prepared = service.PrepareFolderOnly(inspection, source, draft, ct, approveVolumeRoot: true);
            using var review = prepared.Review;
            var result = await RunJob(review, ct);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal("Windows NtSetSecurityObject volume-root DACL", Assert.Single(result.Components).NativeCall);
            var expected = Assert.IsType<WindowsDacl>(prepared.Expected.Access.Value?.Acl);
            byte[] after = ReadDescriptor(path);
            Assert.Equal(DaclBytes(expected.SecurityDescriptor.ToArray()), DaclBytes(after));
            Assert.Equal(control, WindowsPermissionDescriptor.Control(after));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(baseline, 4, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 4, Authority.Id));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(baseline, 8, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 8, Authority.Id));
            byte[] branchAfter = ReadStoredDescriptor(branch.FullName), fileAfter = ReadStoredDescriptor(nested);
            Assert.True(branchBefore.AsSpan().SequenceEqual(branchAfter),
                $"The existing folder changed: {Sddl(branchBefore)} -> {Sddl(branchAfter)}; root {Sddl(baseline)} -> {Sddl(after)}");
            Assert.True(fileBefore.AsSpan().SequenceEqual(fileAfter), $"The existing file changed: {Sddl(fileBefore)} -> {Sddl(fileAfter)}");
            future = Directory.CreateDirectory(Path.Combine(path, $"filecat-root-future-{Guid.NewGuid():N}"));
            Assert.Contains(WindowsPermissionDescriptor.Dacl(ReadDescriptor(future.FullName), Authority.Id).Entries,
                rule => rule is { Kind: WindowsAceKind.Deny, Principal: var principal, Mask: WitnessRight } && principal == WitnessAccount &&
                    (rule.Flags & (byte)AceFlags.Inherited) != 0);
        }
        finally
        {
            // The independently named volume is dedicated to these root-write cases.
            var restore = new DirectorySecurity();
            restore.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            root.SetAccessControl(restore);
            future?.Delete();
            branch?.Delete(recursive: true);
        }
    }

    private static byte[] ReadStoredDescriptor(string path)
    {
        const uint information = 7; // OWNER | GROUP | DACL
        _ = GetFileSecurity(path, information, null, 0, out uint length);
        byte[] descriptor = new byte[length];
        Assert.True(GetFileSecurity(path, information, descriptor, length, out _), $"GetFileSecurityW failed: {Marshal.GetLastPInvokeError()}");
        return descriptor;
    }

    private static string Sddl(byte[] descriptor)
    {
        var raw = new RawSecurityDescriptor(descriptor, 0);
        return $"0x{(int)raw.ControlFlags:X4} {raw.GetSddlForm(AccessControlSections.Access)}";
    }

    [LibraryImport("advapi32.dll", EntryPoint = "SetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileSecurity(string path, uint information, byte[] descriptor);

    [LibraryImport("advapi32.dll", EntryPoint = "GetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileSecurity(string path, uint information, byte[]? descriptor, uint length, out uint needed);
}
