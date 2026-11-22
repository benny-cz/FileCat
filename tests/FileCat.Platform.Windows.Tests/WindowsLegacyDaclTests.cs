using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// DACLs stored without SE_DACL_AUTO_INHERITED. Format leaves volume roots that way, and files and folders created below
/// such a DACL inherit the form without inheritance marks, so whole data volumes hold them. GetSecurityInfo reads them
/// differently from how they are stored, and SetSecurityInfo rewrites them; FileCat reads and keeps the stored form.
/// </summary>
public sealed partial class WindowsLegacyDaclTests
{
    private const string AuthorityId = "local";
    private const string Guests = "S-1-5-32-546", Users = "S-1-5-32-545", Interactive = "S-1-5-4";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_NTFS_legacy_file_DACL_reads_as_stored_and_an_edit_keeps_its_form(bool protect)
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-");
        try
        {
            string me = CurrentSid();
            string path = CreateLegacyChild(directory, me, protect);
            string prefix = protect ? "P" : "";
            string control = protect ? "0x9004" : "0x8004";
            Assert.Equal($"{control} D:{prefix}(A;;FA;;;{me})(A;;FR;;;BG)(A;;FR;;;BU)", StoredSddl(path));

            var ct = TestContext.Current.CancellationToken;
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            using var inspection = new WindowsPermissionService().Inspect(Subject(source), PermissionSections.Ordinary, ct);
            var snapshot = inspection.Snapshot;
            // GetSecurityInfo would list BU first and mark the two entries matching the parent's rules inherited.
            var dacl = Assert.IsType<WindowsDacl>(snapshot.Access.Value?.Acl);
            Assert.Equal([me, Guests, Users], dacl.Entries.Select(entry => entry.Principal?.Id));
            Assert.All(dacl.Entries, entry => Assert.Equal(0, entry.Flags & 0x10));
            var inheritance = snapshot.Inheritance.Value!.Value;
            Assert.False(inheritance.IsAutoInherited);
            Assert.Equal(protect, inheritance.IsProtected);
            Assert.Null(WindowsPermissionService.EditRefusal(snapshot));

            var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, Interactive, AuthorityId),
                (uint)(FileSystemRights.Read | FileSystemRights.Synchronize));
            var prepared = WindowsPermissionService.Prepare(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct);
            using var operation = prepared.Review;
            var result = await RunJob(operation, ct);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            Assert.Equal("Windows NtSetSecurityObject legacy DACL", Assert.Single(result.Components).NativeCall);
            // Only the reviewed entry changed: no automatic-inheritance mark, no inherited marks, the stored order kept.
            Assert.Equal($"{control} D:{prefix}(A;;FA;;;{me})(A;;FR;;;BG)(A;;FR;;;BU)(A;;FR;;;IU)", StoredSddl(path));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Native_NTFS_parent_change_leaves_a_legacy_child_reading_the_same()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-parent-");
        try
        {
            string me = CurrentSid();
            string path = CreateLegacyChild(directory, me, protect: false);
            var ct = TestContext.Current.CancellationToken;
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            var service = new WindowsPermissionService();
            PermissionRevision? before;
            using (var first = service.Inspect(Subject(source), WindowsPermissionService.DiscretionarySections, ct))
                before = PermissionRevision.Read(first.Snapshot, WindowsPermissionService.DiscretionarySections);
            Assert.NotNull(before);

            // A parent change that does not propagate leaves the child's stored descriptor as it was. GetSecurityInfo would
            // now report the child as protected, because its entries no longer match the parent's inheritable rules.
            SetStoredDacl(directory.FullName, $"D:P(A;OICI;FA;;;{me})(A;OICI;0x1200a9;;;BG)");
            using var second = service.Inspect(Subject(source), WindowsPermissionService.DiscretionarySections, ct);
            Assert.Equal(before, PermissionRevision.Read(second.Snapshot, WindowsPermissionService.DiscretionarySections));
            Assert.False(second.Snapshot.Inheritance.Value!.Value.IsProtected);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Native_NTFS_legacy_folder_offers_no_native_propagation_or_inheritance_change()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-folder-");
        try
        {
            string me = CurrentSid();
            var automatic = directory.CreateSubdirectory("automatic");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(me), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            automatic.SetAccessControl(security); // SetSecurityInfo stores the automatic form.
            var legacy = directory.CreateSubdirectory("legacy");
            SetStoredDacl(legacy.FullName, $"D:P(A;OICI;FA;;;{me})");

            var ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService(qualifyNativePropagation: true, qualifyInheritanceChanges: true);
            using var automaticInspection = service.Inspect(FolderSubject(automatic), WindowsPermissionService.DiscretionarySections, ct);
            using var legacyInspection = service.Inspect(FolderSubject(legacy), WindowsPermissionService.DiscretionarySections, ct);
            Assert.True(automaticInspection.Snapshot.Inheritance.Value!.Value.IsAutoInherited);
            Assert.True(service.CanReviewNativePropagation(automaticInspection.Snapshot),
                automaticInspection.Snapshot.BindingProblem ?? automaticInspection.Snapshot.Access.Reason);
            Assert.False(legacyInspection.Snapshot.Inheritance.Value!.Value.IsAutoInherited);
            Assert.False(service.CanReviewNativePropagation(legacyInspection.Snapshot));
            Assert.False(service.CanReviewInheritanceChange(legacyInspection.Snapshot));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Native_NTFS_folder_only_edit_keeps_a_legacy_folder_DACL_and_leaves_its_children()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-folder-only-");
        try
        {
            string me = CurrentSid();
            var folder = directory.CreateSubdirectory("legacy");
            SetStoredDacl(folder.FullName, $"D:(A;OICI;FA;;;{me})(A;OICI;FR;;;BG)");
            string child = Path.Combine(folder.FullName, "child.txt");
            File.WriteAllText(child, "fixture");
            string childBefore = StoredSddl(child);
            Assert.StartsWith("0x8004 ", childBefore, StringComparison.Ordinal); // Created below a legacy DACL: legacy itself.

            var ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService(qualifyFolderOnly: true);
            var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
            using var inspection = service.Inspect(FolderSubject(folder), WindowsPermissionService.DiscretionarySections, ct);
            Assert.True(service.CanReviewFolderOnly(inspection.Snapshot), inspection.Snapshot.BindingProblem ?? inspection.Snapshot.Access.Reason);
            var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, Interactive, AuthorityId),
                (uint)(FileSystemRights.Read | FileSystemRights.Synchronize), flags: 3 /* OBJECT_INHERIT | CONTAINER_INHERIT */);
            var prepared = service.PrepareFolderOnly(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct);
            using var operation = prepared.Review;
            var result = await RunJob(operation, ct);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            Assert.Equal("Windows NtSetSecurityObject folder DACL", Assert.Single(result.Components).NativeCall);
            Assert.Equal($"0x8004 D:(A;OICI;FA;;;{me})(A;OICI;FR;;;BG)(A;OICI;FR;;;IU)", StoredSddl(folder.FullName));
            Assert.Equal(childBefore, StoredSddl(child));
        }
        finally { directory.Delete(recursive: true); }
    }

    // A DACL stored without SE_DACL_AUTO_INHERITED whose trailing entries carry inheritance marks, as a tool that copies
    // marked entries without the control bit leaves it: the folder-only route adds the reviewed rule before the marked
    // entries and keeps the marks, the legacy control and the children.
    [Fact]
    public async Task Native_NTFS_folder_only_edit_keeps_a_legacy_folder_DACL_with_inheritance_marks()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-marked-");
        try
        {
            string me = CurrentSid();
            var folder = directory.CreateSubdirectory("marked");
            SetStoredDacl(folder.FullName, $"D:(A;OICI;FA;;;{me})(A;OICIID;FR;;;BG)");
            Assert.Equal($"0x8004 D:(A;OICI;FA;;;{me})(A;OICIID;FR;;;BG)", StoredSddl(folder.FullName));
            string child = Path.Combine(folder.FullName, "child.txt");
            File.WriteAllText(child, "fixture");
            string childBefore = StoredSddl(child);

            var ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService(qualifyFolderOnly: true);
            var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
            using var inspection = service.Inspect(FolderSubject(folder), WindowsPermissionService.DiscretionarySections, ct);
            Assert.True(service.CanReviewFolderOnly(inspection.Snapshot), inspection.Snapshot.BindingProblem ?? inspection.Snapshot.Access.Reason);
            var grant = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, new(PrincipalKind.WindowsSid, Interactive, AuthorityId),
                (uint)(FileSystemRights.Read | FileSystemRights.Synchronize), flags: 3 /* OBJECT_INHERIT | CONTAINER_INHERIT */);
            var prepared = service.PrepareFolderOnly(inspection, source, new(PermissionScope.Objects, [new AddWindowsAce(grant)]), ct);
            using var operation = prepared.Review;
            var result = await RunJob(operation, ct);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            Assert.Equal("Windows NtSetSecurityObject folder DACL", Assert.Single(result.Components).NativeCall);
            Assert.Equal($"0x8004 D:(A;OICI;FA;;;{me})(A;OICI;FR;;;IU)(A;OICIID;FR;;;BG)", StoredSddl(folder.FullName));
            Assert.Equal(childBefore, StoredSddl(child));
        }
        finally { directory.Delete(recursive: true); }
    }

    // The reading FileCat shows beside a legacy DACL is what Windows tools show: GetNamedSecurityInfo's DACL and protection.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_NTFS_legacy_reading_matches_what_Windows_tools_show(bool parentChanged)
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-reading-");
        try
        {
            string me = CurrentSid();
            string path = CreateLegacyChild(directory, me, protect: false);
            // A parent change that does not propagate: the entries that match the new inheritable rules now read as inherited.
            if (parentChanged) SetStoredDacl(directory.FullName, $"D:P(A;OICI;FA;;;{me})(A;OICI;FR;;;BU)");
            var ct = TestContext.Current.CancellationToken;
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            using var inspection = new WindowsPermissionService().Inspect(Subject(source), WindowsPermissionService.DiscretionarySections, ct);
            var reading = Assert.IsType<WindowsInheritedReading>(inspection.Snapshot.WindowsReading);
            var tools = new RawSecurityDescriptor(NamedDescriptor(path), 0);
            var shown = new RawSecurityDescriptor(reading.Acl.SecurityDescriptor.ToArray(), 0);
            // The same entries in the same order and the same protection. GetNamedSecurityInfo returns them without
            // SE_DACL_AUTO_INHERITED; the conversion FileCat would store adds it.
            Assert.Equal(Entries(tools), Entries(shown));
            Assert.Equal((tools.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0, reading.Control.IsProtected);
            Assert.True(reading.Control.IsAutoInherited);
            if (!parentChanged) Assert.Equal($"(A;;FR;;;BU)(A;ID;FA;;;{me})(A;ID;FR;;;BG)", Entries(shown));
            else Assert.Equal($"(A;;FR;;;BG)(A;ID;FA;;;{me})(A;ID;FR;;;BU)", Entries(shown));
            Assert.StartsWith("0x8004 ", StoredSddl(path), StringComparison.Ordinal); // Reading never writes.
        }
        finally { directory.Delete(recursive: true); }
    }

    // The explicit conversion stores the reviewed reading in automatic form, leaves the children alone and is the way
    // to native propagation on a legacy folder; a file converts through the ordinary writer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_NTFS_conversion_stores_the_reviewed_reading_in_automatic_form(bool folder)
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-convert-");
        try
        {
            string me = CurrentSid();
            SetStoredDacl(directory.FullName, $"D:P(A;OICI;FA;;;{me})(A;OICI;FR;;;BG)");
            string path;
            string? child = null, childBefore = null;
            if (folder)
            {
                path = directory.CreateSubdirectory("legacy").FullName;
                SetStoredDacl(path, $"D:(A;;FR;;;BU)(A;OICI;FA;;;{me})(A;OICI;FR;;;BG)");
                child = Path.Combine(path, "child.txt");
                File.WriteAllText(child, "fixture");
                childBefore = StoredSddl(child);
            }
            else path = CreateLegacyChild(directory, me, protect: false);
            var ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService(qualifyFolderOnly: true, qualifyNativePropagation: true);
            var source = ItemRef.ForFileSystemPath(path, folder ? EntryKind.Directory : EntryKind.File);
            var subject = folder ? FolderSubject(new DirectoryInfo(path)) : Subject(source);
            using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
            var reading = Assert.IsType<WindowsInheritedReading>(inspection.Snapshot.WindowsReading);
            Assert.False(service.CanReviewNativePropagation(inspection.Snapshot));
            Assert.True(service.CanReviewInheritanceConversion(inspection.Snapshot));
            string readingSddl = new RawSecurityDescriptor(reading.Acl.SecurityDescriptor.ToArray(), 0).GetSddlForm(AccessControlSections.Access);
            var prepared = service.PrepareInheritanceConversion(inspection, source, ct);
            using var operation = prepared.Review;
            var result = await RunJob(operation, ct);
            Assert.True(result.Verdict == PermissionVerdict.Verified, $"{result.Verdict}: {result.Detail}");
            Assert.Equal("Windows NtSetSecurityObject inheritance conversion", Assert.Single(result.Components).NativeCall);
            Assert.Equal("0x8404 " + readingSddl, StoredSddl(path));
            Assert.Contains("ID;FA;;;" + me + ")", readingSddl); // The parent's rule now reads as inherited.
            if (child is not null) Assert.Equal(childBefore, StoredSddl(child));
            using var after = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
            Assert.Null(after.Snapshot.WindowsReading);
            Assert.Equal(folder, service.CanReviewNativePropagation(after.Snapshot));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Native_NTFS_conversion_refuses_after_the_parent_changes()
    {
        RequireOrdinaryNtfs();
        var directory = Directory.CreateTempSubdirectory("filecat-acl-legacy-convert-stale-");
        try
        {
            string me = CurrentSid();
            string path = CreateLegacyChild(directory, me, protect: false);
            var ct = TestContext.Current.CancellationToken;
            var service = new WindowsPermissionService();
            var source = ItemRef.ForFileSystemPath(path, EntryKind.File);
            using var inspection = service.Inspect(Subject(source), WindowsPermissionService.DiscretionarySections, ct);
            Assert.NotNull(inspection.Snapshot.WindowsReading);
            SetStoredDacl(directory.FullName, $"D:P(A;OICI;FA;;;{me})(A;OICI;FR;;;BU)");
            Assert.Throws<PermissionBindingConflictException>(() => service.PrepareInheritanceConversion(inspection, source, ct));
            Assert.StartsWith("0x8004 ", StoredSddl(path), StringComparison.Ordinal);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static string Entries(RawSecurityDescriptor descriptor)
    {
        string sddl = descriptor.GetSddlForm(AccessControlSections.Access);
        return sddl[sddl.IndexOf('(')..];
    }

    // Windows tools' reading: GetNamedSecurityInfo's DACL.
    private static byte[] NamedDescriptor(string path)
    {
        uint error = GetNamedSecurityInfo(path, 1 /* SE_FILE_OBJECT */, 4 /* DACL */, out _, out _, out _, out _, out nint descriptor);
        Assert.Equal(0u, error);
        try
        {
            byte[] bytes = new byte[GetSecurityDescriptorLength(descriptor)];
            Marshal.Copy(descriptor, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { LocalFree(descriptor); }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetNamedSecurityInfo(string path, int objectType, uint information, out nint owner, out nint group,
        out nint dacl, out nint sacl, out nint descriptor);

    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityDescriptorLength(nint descriptor);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    // A folder whose inheritable rules the child copies as plain entries, then a child that also lists Users after them,
    // both stored the legacy way.
    private static string CreateLegacyChild(DirectoryInfo directory, string me, bool protect)
    {
        SetStoredDacl(directory.FullName, $"D:P(A;OICI;FA;;;{me})(A;OICI;FR;;;BG)");
        string path = Path.Combine(directory.FullName, "legacy.txt");
        File.WriteAllText(path, "fixture");
        SetStoredDacl(path, $"D:{(protect ? "P" : "")}(A;;FA;;;{me})(A;;FR;;;BG)(A;;FR;;;BU)");
        return path;
    }

    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static PermissionSubject Subject(ItemRef source) => new(source.Parent, source.FileSystemPath!, source.FileSystemPath!,
        new(PermissionAuthorityKind.Local, AuthorityId, 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);

    private static PermissionSubject FolderSubject(DirectoryInfo folder)
    {
        var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
        return new(source.Parent, folder.FullName, folder.FullName, new(PermissionAuthorityKind.Local, AuthorityId, 0),
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
    }

    private static async Task<PermissionResult> RunJob(ReviewedPermissionOperation review, CancellationToken ct)
    {
        var journal = Directory.CreateTempSubdirectory("filecat-acl-legacy-journal-");
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, journal.FullName, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) => WindowsAnalysisPrivateDirectory.CreatePermissionJournal(journal.FullName, leaf, cancellation));
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            jobs.JobFinished += job => finished.TrySetResult(job);
            var job = jobs.SubmitPermissions(review);
            Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(30), ct));
            return Assert.Single(job.PermissionResults);
        }
        finally { journal.Delete(recursive: true); }
    }

    // SetFileSecurityW stores the DACL without SE_DACL_AUTO_INHERITED and without propagating it.
    private static void SetStoredDacl(string path, string sddl)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        byte[] bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        Assert.True(SetFileSecurity(path, 4 /* DACL_SECURITY_INFORMATION */, bytes), $"SetFileSecurityW failed: {Marshal.GetLastPInvokeError()}");
    }

    private static string StoredSddl(string path)
    {
        _ = GetFileSecurity(path, 4, null, 0, out uint length);
        byte[] descriptor = new byte[length];
        Assert.True(GetFileSecurity(path, 4, descriptor, length, out _), $"GetFileSecurityW failed: {Marshal.GetLastPInvokeError()}");
        var raw = new RawSecurityDescriptor(descriptor, 0);
        return $"0x{(int)raw.ControlFlags:X4} {raw.GetSddlForm(AccessControlSections.Access)}";
    }

    private static void RequireOrdinaryNtfs()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_NTFS") != "1" ||
            ReviewedPermissionOperation.ProcessRefusal() is not null)
            Assert.Skip("Requires an explicitly enabled ordinary Windows x64 NTFS test volume.");
    }

    [LibraryImport("advapi32.dll", EntryPoint = "SetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileSecurity(string path, uint information, byte[] descriptor);

    [LibraryImport("advapi32.dll", EntryPoint = "GetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileSecurity(string path, uint information, byte[]? descriptor, uint length, out uint needed);
}
