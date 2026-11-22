using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// G-ACCESS (ordinary, W2): the discretionary estimate's AuthZ evaluation for deny-only groups, restricting SIDs, the
/// owner's implicit rights and a conditional rule. Each estimate is compared with what the same token can open.
/// </summary>
public sealed partial class WindowsAccessEstimateTokenTests
{
    private const uint ReadData = 0x1, WriteData = 0x2, ReadAttributes = 0x80, ReadControl = 0x20000, WriteDac = 0x40000,
        Synchronize = 0x100000;
    private static readonly SecurityIdentifier Everyone = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsers = new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    // A group made deny-only matches only deny rules: the read it granted disappears from the estimate and the open.
    [Fact]
    public void A_deny_only_group_stops_granting_in_the_estimate_and_the_open()
    {
        var directory = RequireFixture("filecat-acl-deny-only-");
        try
        {
            using var identity = CurrentIdentity();
            Assert.Contains(AuthenticatedUsers, identity.Groups!.OfType<SecurityIdentifier>());
            string path = CreateWitness(directory, "group.txt");
            SetDacl(path, (AuthenticatedUsers, ReadData | ReadAttributes | Synchronize));
            var dacl = ReadDacl(path);
            Assert.Equal(ReadData, Granted(identity.AccessToken, dacl) & ReadData);
            Assert.Equal(0, OpenError(identity.AccessToken, path, ReadData | Synchronize));
            using var denyOnly = Restrict(identity.AccessToken, disable: [AuthenticatedUsers], restrict: []);
            Assert.False(IsTokenRestricted(denyOnly));
            Assert.Equal(0u, Granted(denyOnly, dacl) & ReadData);
            Assert.Equal(5, OpenError(denyOnly, path, ReadData | Synchronize));
        }
        finally { directory.Delete(recursive: true); }
    }

    // A token with restricting SIDs needs both passes: Everyone may only read, so writing is refused although the user's
    // own rule grants it.
    [Fact]
    public void Restricting_SIDs_need_both_passes_in_the_estimate_and_the_open()
    {
        var directory = RequireFixture("filecat-acl-restricted-");
        try
        {
            using var identity = CurrentIdentity();
            var user = identity.User!;
            string path = CreateWitness(directory, "restricted.txt");
            SetDacl(path, (user, ReadData | WriteData | ReadAttributes | Synchronize), (Everyone, ReadData | ReadAttributes | Synchronize));
            var dacl = ReadDacl(path);
            Assert.Equal(ReadData | WriteData, Granted(identity.AccessToken, dacl) & (ReadData | WriteData));
            Assert.Equal(0, OpenError(identity.AccessToken, path, WriteData | Synchronize));
            using var restricted = Restrict(identity.AccessToken, disable: [], restrict: [Everyone]);
            Assert.True(IsTokenRestricted(restricted));
            uint granted = Granted(restricted, dacl);
            Assert.Equal(ReadData, granted & (ReadData | WriteData));
            Assert.Equal(0, OpenError(restricted, path, ReadData | Synchronize));
            Assert.Equal(5, OpenError(restricted, path, WriteData | Synchronize));
        }
        finally { directory.Delete(recursive: true); }
    }

    // The owner implicitly may read and change the DACL. An OWNER RIGHTS rule replaces that implicit grant, and the
    // estimate and the open both lose the DACL change.
    [Fact]
    public void An_owner_rights_rule_replaces_the_owner_implicit_rights_in_the_estimate_and_the_open()
    {
        var directory = RequireFixture("filecat-acl-owner-rights-");
        try
        {
            using var identity = CurrentIdentity();
            var user = identity.User!;
            string plain = CreateWitness(directory, "owner.txt"), limited = CreateWitness(directory, "owner-rights.txt");
            SetDacl(plain, (user, ReadData | ReadAttributes | Synchronize));
            SetDacl(limited, (user, ReadData | ReadAttributes | Synchronize), (OwnerRights, ReadControl));
            foreach (string path in new[] { plain, limited })
                if (!user.Equals(new FileInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
                    Assert.Skip("Requires a token whose new files are owned by its user.");
            Assert.Equal(ReadControl | WriteDac, Granted(identity.AccessToken, ReadDacl(plain)) & (ReadControl | WriteDac));
            Assert.Equal(0, OpenError(identity.AccessToken, plain, WriteDac));
            Assert.Equal(ReadControl, Granted(identity.AccessToken, ReadDacl(limited)) & (ReadControl | WriteDac));
            Assert.Equal(5, OpenError(identity.AccessToken, limited, WriteDac));
        }
        finally { directory.Delete(recursive: true); }
    }

    // A conditional rule (an allow callback rule on a user claim) is not evaluated: the estimate names the reason and
    // every right stays incomplete rather than granted or denied.
    [Fact]
    public void A_conditional_rule_leaves_every_estimated_right_incomplete()
    {
        var directory = RequireFixture("filecat-acl-conditional-");
        try
        {
            using var identity = CurrentIdentity();
            string path = CreateWitness(directory, "conditional.txt");
            string sddl = $"D:P(A;;FA;;;{identity.User!.Value})(XA;;FR;;;WD;(@User.Department == \"Finance\"))";
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out nint descriptor, out _))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                if (!SetFileSecurity(path, 4 /* DACL; the descriptor carries the protected flag */, descriptor)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            finally { LocalFree(descriptor); }
            var subject = new PermissionSubject(Location.FileSystem(directory.FullName), path, path, new(PermissionAuthorityKind.Local, "local", 0),
                PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            var report = new WindowsPermissionService(qualifyAccessEstimates: true).EstimateAccess(subject, TestContext.Current.CancellationToken);
            Assert.Contains("cannot evaluate", report.Reason);
            Assert.Null(report.GrantedMask);
            Assert.All(report.Checks, check => Assert.Equal(PermissionReadStatus.Incomplete, check.Status));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static DirectoryInfo RequireFixture(string prefix)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows x64 fixed NTFS fixture.");
        var directory = Directory.CreateTempSubdirectory(prefix);
        // The folder alone lets this user remove the witnesses, whose own DACLs grant no DELETE.
        using var user = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
        return directory;
    }

    private static WindowsIdentity CurrentIdentity() => WindowsIdentity.GetCurrent(
        TokenAccessLevels.Query | TokenAccessLevels.Duplicate | TokenAccessLevels.Impersonate | TokenAccessLevels.AssignPrimary);

    private static string CreateWitness(DirectoryInfo directory, string name)
    {
        string path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, "estimate witness");
        return path;
    }

    // A protected DACL with exactly these allow rules; the folder still lets its owner delete the file afterwards.
    private static void SetDacl(string path, params (SecurityIdentifier Sid, uint Rights)[] rules)
    {
        var acl = new RawAcl(GenericAcl.AclRevision, rules.Length);
        foreach (var (sid, rights) in rules)
            acl.InsertAce(acl.Count, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, unchecked((int)rights), sid, false, null));
        var descriptor = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected | ControlFlags.SelfRelative,
            null, null, null, acl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (!SetFileSecurity(path, 4 /* DACL */, handle.AddrOfPinnedObject())) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally { handle.Free(); }
    }

    private static WindowsDacl ReadDacl(string path)
    {
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        return WindowsPermissionDescriptor.Dacl(security.GetSecurityDescriptorBinaryForm(), "local");
    }

    private static uint Granted(SafeAccessTokenHandle token, WindowsDacl dacl)
    {
        var (granted, error) = WindowsPermissionService.EvaluateDiscretionaryMask(token, dacl, TestContext.Current.CancellationToken);
        Assert.True(granted.HasValue, $"AuthZ did not evaluate the DACL (error {error}).");
        return granted.Value;
    }

    // 0 when the token opens the file with exactly this access, otherwise the Win32 error. Runs no managed code that could
    // load assemblies while a restricted token is impersonated.
    private static int OpenError(SafeAccessTokenHandle token, string path, uint access) => WindowsIdentity.RunImpersonated(token, () =>
    {
        using var handle = CreateFile(path, access, 7, 0, 3 /* OPEN_EXISTING */, 0, 0);
        return handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
    });

    private static SafeAccessTokenHandle Restrict(SafeAccessTokenHandle token, SecurityIdentifier[] disable, SecurityIdentifier[] restrict)
    {
        var memory = new List<nint>();
        try
        {
            var disabled = Entries(disable);
            var restricting = Entries(restrict);
            if (!CreateRestrictedToken(token, 0, (uint)disabled.Length, disabled, 0, 0, (uint)restricting.Length, restricting, out var restricted))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return restricted;
        }
        finally { foreach (nint block in memory) Marshal.FreeHGlobal(block); }

        SidAndAttributes[] Entries(SecurityIdentifier[] sids) => sids.Select(sid =>
        {
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            nint block = Marshal.AllocHGlobal(bytes.Length);
            memory.Add(block);
            Marshal.Copy(bytes, 0, block, bytes.Length);
            return new SidAndAttributes { Sid = block, Attributes = 0 };
        }).ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateRestrictedToken(SafeAccessTokenHandle existing, uint flags, uint disableSidCount,
        [In] SidAndAttributes[] sidsToDisable, uint deletePrivilegeCount, nint privilegesToDelete, uint restrictedSidCount,
        [In] SidAndAttributes[] sidsToRestrict, out SafeAccessTokenHandle newToken);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsTokenRestricted(SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", EntryPoint = "SetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileSecurity(string path, uint information, nint descriptor);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
}
