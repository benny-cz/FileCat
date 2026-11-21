using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests(ITestOutputHelper output)
{
    private const uint ReadControl = 0x20000, WriteDac = 0x40000, AccessSystemSecurity = 0x1000000;
    private const uint OwnerGroupDacl = 0x7, DaclInformation = 0x4;
    private static readonly PermissionAuthority Authority = new(PermissionAuthorityKind.Local, "local", 0);
    private static readonly Principal WitnessAccount = new(PrincipalKind.WindowsSid, "S-1-5-32-546", Authority.Id);
    private const uint WitnessRight = (uint)FileSystemRights.ReadAttributes;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_propagation_updates_inheriting_children_and_leaves_protected_branches(bool protectRoot)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-propagation-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            if (protectRoot) MakePrivate(selected);
            var nested = selected.CreateSubdirectory("nested");
            var protectedBranch = selected.CreateSubdirectory("protected");
            MakePrivate(protectedBranch);
            string direct = CreateFile(selected, "direct.txt");
            string concurrentlyOpen = CreateFile(nested, "open.txt");
            string behindProtection = CreateFile(protectedBranch, "private.txt");
            string sibling = CreateFile(fixture, "sibling.txt");
            var protectedFile = new FileInfo(CreateFile(selected, "protected-file.txt"));
            var fileAcl = protectedFile.GetAccessControl(AccessControlSections.Access);
            fileAcl.SetAccessRuleProtection(true, true);
            protectedFile.SetAccessControl(fileAcl);

            var selectedBefore = ReadDescriptor(selected.FullName);
            Assert.Equal(protectRoot, WindowsPermissionDescriptor.Control(selectedBefore).IsProtected);
            var protectedBefore = ReadDescriptor(protectedBranch.FullName);
            var protectedFileBefore = ReadDescriptor(protectedFile.FullName);
            var privateBefore = ReadDescriptor(behindProtection);
            var siblingBefore = ReadDescriptor(sibling);
            Assert.False(HasWitness(ReadDescriptor(direct), inherited: true));
            using var openChild = File.Open(concurrentlyOpen, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var selectedHandle = Open(selected.FullName, ReadControl | WriteDac);
            var projected = AddInheritableWitness(selectedBefore);

            var result = await PropagateThroughJob(selected);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal(PermissionVerification.SameObject, Assert.Single(result.Components).Verification);
            uint error = (uint)(Assert.Single(result.Components).NativeError ?? 0);
            var selectedAfter = ReadDescriptor(selectedHandle);
            output.WriteLine("SetSecurityInfo returned {0}. Parent control {1:X4} -> {2:X4}. Direct child inherited {3}. Open nested child inherited {4}.",
                error, WindowsPermissionDescriptor.Control(selectedBefore).Flags, WindowsPermissionDescriptor.Control(selectedAfter).Flags,
                HasWitness(ReadDescriptor(direct), inherited: true), HasWitness(ReadDescriptor(concurrentlyOpen), inherited: true));
            Assert.Equal(0u, error);
            Assert.True(HasWitness(selectedAfter, inherited: false));
            Assert.Equal(projected, DaclBytes(selectedAfter));
            Assert.Equal(WindowsPermissionDescriptor.Control(selectedBefore), WindowsPermissionDescriptor.Control(selectedAfter));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(selectedBefore, 4, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(selectedAfter, 4, Authority.Id));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(selectedBefore, 8, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(selectedAfter, 8, Authority.Id));
            Assert.True(HasWitness(ReadDescriptor(direct), inherited: true));
            Assert.True(HasWitness(ReadDescriptor(nested.FullName), inherited: true));
            Assert.True(HasWitness(ReadDescriptor(concurrentlyOpen), inherited: true));
            Assert.Equal(protectedBefore, ReadDescriptor(protectedBranch.FullName));
            Assert.Equal(protectedFileBefore, ReadDescriptor(protectedFile.FullName));
            Assert.Equal(privateBefore, ReadDescriptor(behindProtection));
            Assert.Equal(siblingBefore, ReadDescriptor(sibling));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void Native_propagation_can_leave_a_child_that_denies_write_DACL_unchanged()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-denied-child-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            var locked = new FileInfo(CreateFile(selected, "denied.txt"));
            string sibling = CreateFile(selected, "sibling.txt");
            var access = locked.GetAccessControl(AccessControlSections.Access);
            access.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-3-4"), FileSystemRights.ChangePermissions, AccessControlType.Deny));
            locked.SetAccessControl(access);
            using var denied = OpenFile(locked.FullName, ReadControl | WriteDac);
            int deniedError = Marshal.GetLastPInvokeError();
            Assert.True(denied.IsInvalid);
            Assert.Equal(5, deniedError);
            var deniedBefore = ReadDescriptor(locked.FullName);
            var source = ItemRef.ForFileSystemPath(locked.FullName, EntryKind.File);
            CancellationToken ct = TestContext.Current.CancellationToken;
            using var inspection = new WindowsPermissionService().Inspect(FileSubject(source), PermissionSections.Ordinary, ct);
            Assert.Null(WindowsPermissionService.EditRefusal(inspection.Snapshot));
            Assert.Throws<UnauthorizedAccessException>(() => WindowsPermissionService.Prepare(inspection, source, WitnessDraft(), ct));
            Assert.Equal(deniedBefore, ReadDescriptor(locked.FullName));
            var selectedBefore = ReadDescriptor(selected.FullName);
            using var selectedHandle = Open(selected.FullName, ReadControl | WriteDac);
            uint error = SetDacl(selectedHandle, AddInheritableWitness(selectedBefore));
            bool changedRoot = HasWitness(ReadDescriptor(selectedHandle), inherited: false);
            bool changedSibling = HasWitness(ReadDescriptor(sibling), inherited: true);
            bool changedDenied = !deniedBefore.AsSpan().SequenceEqual(ReadDescriptor(locked.FullName));
            output.WriteLine("SetSecurityInfo returned {0}. Parent changed {1}. Sibling changed {2}. Denied child descriptor changed {3}.",
                error, changedRoot, changedSibling, changedDenied);
            Assert.True(error is 0 or 5, new Win32Exception((int)error).Message);
            Assert.False(changedDenied);
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Ordinary_Windows_DACL_job_edits_a_file_whose_seeded_SACL_it_cannot_read()
    {
        var fixture = RequireFixture();
        string path = Path.Combine(fixture, "audit-witness.txt");
        Assert.True(File.Exists(path));
        Assert.Equal(0, (int)(File.GetAttributes(path) & FileAttributes.ReparsePoint));
        using var inaccessible = OpenFile(path, ReadControl | AccessSystemSecurity);
        int denial = Marshal.GetLastPInvokeError();
        Assert.True(inaccessible.IsInvalid, "The ordinary test runner obtained an audit handle.");
        Assert.True(denial is 5 or 1314, new Win32Exception(denial).Message);
        output.WriteLine("Ordinary open for ACCESS_SYSTEM_SECURITY was denied with system error {0}.", denial);
        var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new WindowsPermissionService();
        using var inspection = service.Inspect(FileSubject(source), PermissionSections.Ordinary | PermissionSections.Audit, ct);
        Assert.Equal(PermissionReadStatus.Unsupported, inspection.Snapshot.Audit.Status);
        Assert.Null(WindowsPermissionService.EditRefusal(inspection.Snapshot));
        using var review = WindowsPermissionService.Prepare(inspection, source, WitnessDraft(), ct).Review;
        var result = await RunJob(review, ct);
        Assert.Equal(PermissionVerdict.Verified, result.Verdict);
        Assert.True(HasWitness(ReadDescriptor(path), inherited: false));
    }

    [Fact]
    public async Task Ordinary_Windows_owner_job_changes_a_distinct_reviewed_owner_and_preserves_DACL()
    {
        string path = Path.Combine(RequireFixture(), "owner-witness.txt");
        Assert.True(File.Exists(path));
        Assert.Equal(0, (int)(File.GetAttributes(path) & FileAttributes.ReparsePoint));
        var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new WindowsPermissionService();
        using var inspection = service.Inspect(FileSubject(source), PermissionSections.Ordinary, ct);
        var assignableUser = WindowsPermissionService.OwnerChoices(Authority)[0];
        Assert.Equal("S-1-5-32-544", inspection.Snapshot.Owner.Value?.Id);
        Assert.NotEqual(inspection.Snapshot.Owner.Value, assignableUser);
        var before = ReadDescriptor(path);
        var draft = new PermissionDraft(PermissionScope.Objects, [new AssignPermissionPrincipal(true, assignableUser)]);
        using var review = WindowsPermissionService.Prepare(inspection, source, draft, ct).Review;
        var result = await RunJob(review, ct);
        Assert.Equal(PermissionVerdict.Verified, result.Verdict);
        var after = ReadDescriptor(path);
        Assert.Equal(assignableUser, WindowsPermissionDescriptor.PrincipalAt(after, 4, Authority.Id));
        Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(before, 8, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 8, Authority.Id));
        Assert.Equal(DaclBytes(before), DaclBytes(after));
        Assert.Equal(WindowsPermissionDescriptor.Control(before).IsProtected, WindowsPermissionDescriptor.Control(after).IsProtected);
    }

    [Fact]
    public async Task Ordinary_Windows_job_refuses_external_DACL_drift_on_the_same_file()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-dacl-drift-");
        try
        {
            MakePrivate(fixture);
            string path = CreateFile(fixture, "selected.txt");
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            CancellationToken ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService();
            using var inspection = service.Inspect(FileSubject(source), PermissionSections.Ordinary, ct);
            using var review = WindowsPermissionService.Prepare(inspection, source, WitnessDraft(), ct).Review;
            var file = new FileInfo(path);
            var external = file.GetAccessControl(AccessControlSections.Access);
            external.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"),
                FileSystemRights.ReadExtendedAttributes, AccessControlType.Allow));
            file.SetAccessControl(external);
            var observedExternal = ReadDescriptor(path);

            var result = await RunJob(review, ct);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(observedExternal, ReadDescriptor(path));
            Assert.False(HasWitness(observedExternal, inherited: false));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static PermissionSubject FileSubject(ItemRef source) =>
        new(source.Parent, source.FileSystemPath!, source.FileSystemPath!, Authority, PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);

    private static PermissionDraft WitnessDraft() => new(PermissionScope.Objects,
        [new AddWindowsAce(WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, WitnessAccount, WitnessRight))]);

    private static async Task<PermissionResult> PropagateThroughJob(DirectoryInfo directory, Action? afterReview = null)
    {
        const PermissionSections sections = WindowsPermissionService.DiscretionarySections;
        var ct = TestContext.Current.CancellationToken;
        var source = ItemRef.ForFileSystemPath(directory.FullName, EntryKind.Directory);
        var subject = new PermissionSubject(source.Parent, directory.FullName, directory.FullName, Authority,
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var service = new WindowsPermissionService(qualifyNativePropagation: true);
        using var inspection = service.Inspect(subject, sections, ct);
        var original = inspection.Snapshot;
        Assert.True(service.CanReviewNativePropagation(original), original.BindingProblem ?? original.Access.Reason);
        Assert.False(new WindowsPermissionService().CanReviewNativePropagation(original));
        var boundary = WindowsPermissionService.CapturePropagationBoundary(original, ct);
        var change = new AddWindowsAce(WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, WitnessAccount, WitnessRight, flags: 3));
        using var operation = service.PrepareNativePropagation(inspection, source,
            new PermissionDraft(PermissionScope.NativePropagation, [change]), boundary, ct).Review;
        afterReview?.Invoke();
        return await RunJob(operation, ct);
    }

    private static async Task<PermissionResult> RunJob(ReviewedPermissionOperation review, CancellationToken ct)
    {
        var directory = Directory.CreateTempSubdirectory("filecat-permission-scope-journal-");
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, directory.FullName, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(directory.FullName, leaf, cancellation));
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var job = jobs.SubmitPermissions(review);
            Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), ct));
            Assert.Equal(review.Scope, job.PermissionReviewScope);
            return Assert.Single(job.PermissionResults);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static string RequireFixture()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_WINDOWS_SCOPE") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires the ordinary Windows permissions scope fixture.");
        string path = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        Assert.Equal(Path.Combine(drive.RootDirectory.FullName, "acl-fixtures"), path, ignoreCase: true);
        Assert.Equal("FileCat ACL Fixture", drive.VolumeLabel);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.False(string.Equals(Environment.GetEnvironmentVariable("SystemDrive"), drive.Name.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        return path;
    }

    private static void MakePrivate(DirectoryInfo directory)
    {
        using var user = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static string CreateFile(DirectoryInfo parent, string name)
    {
        string path = Path.Combine(parent.FullName, name);
        File.WriteAllText(path, "owned ACL sentinel");
        return path;
    }

    private static byte[] AddInheritableWitness(byte[] original)
    {
        var descriptor = new RawSecurityDescriptor(original, 0);
        var dacl = descriptor.DiscretionaryAcl!;
        int firstInherited = 0;
        while (firstInherited < dacl.Count && (dacl[firstInherited].AceFlags & AceFlags.Inherited) == 0) firstInherited++;
        dacl.InsertAce(firstInherited, new CommonAce(AceFlags.ContainerInherit | AceFlags.ObjectInherit,
            AceQualifier.AccessAllowed, (int)WitnessRight, new SecurityIdentifier(WitnessAccount.Id), false, null));
        var bytes = new byte[dacl.BinaryLength];
        dacl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static bool HasWitness(byte[] descriptor, bool inherited) =>
        WindowsPermissionDescriptor.Dacl(descriptor, Authority.Id).Entries.Any(entry =>
            entry is { Kind: WindowsAceKind.Allow, Principal: var principal, Mask: WitnessRight } && principal == WitnessAccount &&
            ((entry.Flags & (byte)AceFlags.Inherited) != 0) == inherited);

    private static byte[] DaclBytes(byte[] descriptor)
    {
        var acl = new RawSecurityDescriptor(descriptor, 0).DiscretionaryAcl!;
        var bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static SafeFileHandle Open(string path, uint access)
    {
        var handle = OpenFile(path, access);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    private static SafeFileHandle OpenFile(string path, uint access) =>
        CreateFile(path, access | 0x80 /* FILE_READ_ATTRIBUTES */, 7 /* all sharing */, 0, 3 /* OPEN_EXISTING */,
            0x02000000 | 0x00200000 | 0x00100000 /* backup semantics, open reparse point, no recall */, 0);

    private static byte[] ReadDescriptor(string path)
    {
        using var handle = Open(path, ReadControl);
        return ReadDescriptor(handle);
    }

    private static byte[] ReadDescriptor(SafeFileHandle handle)
    {
        uint error = GetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, OwnerGroupDacl, 0, 0, 0, 0, out nint descriptor);
        try
        {
            if (error != 0) throw new Win32Exception((int)error);
            var bytes = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { if (descriptor != 0) _ = LocalFree(descriptor); }
    }

    private static unsafe uint SetDacl(SafeFileHandle handle, byte[] bytes)
    {
        fixed (byte* acl = bytes)
            return SetSecurityInfo(handle, 1 /* SE_FILE_OBJECT */, DaclInformation, 0, 0, acl, 0);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(SafeFileHandle handle, int type, uint section, nint owner, nint group, nint dacl, nint sacl, out nint descriptor);
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityDescriptorLength(nint descriptor);
    [LibraryImport("advapi32.dll")]
    private static unsafe partial uint SetSecurityInfo(SafeFileHandle handle, int type, uint section, nint owner, nint group, byte* dacl, nint sacl);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint buffer);
}
