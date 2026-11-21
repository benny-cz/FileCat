using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    [Theory]
    [InlineData("arrival")]
    [InlineData("replacement")]
    [InlineData("rename")]
    public void Boundary_captures_exact_names_and_refuses_subsequent_namespace_drift(string change)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-boundary-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            var nested = selected.CreateSubdirectory("nested");
            string original = CreateFile(nested, "original.txt");
            string closed = CreateFile(selected, "root.txt");
            var witness = ReadDescriptor(closed);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = InspectBoundaryRoot(selected, ct);
            var captured = WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct);
            Assert.Equal(new[] { "", "nested", @"nested\original.txt", "root.txt" }, captured.Members.Select(member => member.RelativePath));
            Assert.All(captured.Members, member => Assert.Equal(captured.Root.Identity.MountInstance, member.Identity.MountInstance));
            WindowsPermissionService.RequireUnchangedPropagationBoundary(inspection.Snapshot, captured, ct);
            if (change == "arrival") CreateFile(nested, "arrival.txt");
            else if (change == "rename") File.Move(original, Path.Combine(nested.FullName, "renamed.txt"));
            else
            {
                File.Move(original, Path.Combine(fixture.FullName, "retained-original.txt"));
                CreateFile(nested, "original.txt");
            }
            Assert.Throws<PermissionBindingConflictException>(() =>
                WindowsPermissionService.RequireUnchangedPropagationBoundary(inspection.Snapshot, captured, ct));
            Assert.Equal(witness, ReadDescriptor(closed));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void Boundary_refuses_an_owned_file_with_another_hard_link_name()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-boundary-link-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            string outside = CreateFile(fixture, "owned-witness.txt");
            byte[] witness = ReadDescriptor(outside);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = InspectBoundaryRoot(selected, ct);
            var reviewed = WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct);
            string alias = Path.Combine(selected.FullName, "another-name.txt");
            bool linked = CreateHardLink(alias, outside, 0);
            Assert.True(linked, $"Could not establish the owned hard-link fixture ({Marshal.GetLastPInvokeError()}).");
            var refused = Assert.Throws<NotSupportedException>(() =>
                WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct));
            Assert.Contains("hard-link name", refused.Message);
            Assert.Throws<PermissionBindingConflictException>(() =>
                WindowsPermissionService.RequireUnchangedPropagationBoundary(inspection.Snapshot, reviewed, ct));
            Assert.Equal(witness, ReadDescriptor(outside));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Boundary_conflict_stops_the_held_job_before_native_dispatch()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-boundary-job-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            var nested = selected.CreateSubdirectory("nested");
            string child = CreateFile(nested, "owned-witness.txt");
            byte[] before = ReadDescriptor(child);
            using var held = Open(selected.FullName, ReadControl | WriteDac);
            byte[] rootBefore = ReadDescriptor(held);
            var result = await PropagateThroughJob(selected,
                () => CreateFile(nested, "late-arrival.txt"));
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(rootBefore, ReadDescriptor(held));
            Assert.Equal(before, ReadDescriptor(child));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void Boundary_refuses_a_junction_before_visiting_its_target()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-boundary-junction-");
        string? link = null;
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            var outside = fixture.CreateSubdirectory("other");
            string target = CreateFile(outside, "owned-witness.txt");
            byte[] witness = ReadDescriptor(target);
            link = Path.Combine(selected.FullName, "linked-folder");
            Junction.Create(link, outside.FullName);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = InspectBoundaryRoot(selected, ct);
            var refused = Assert.Throws<NotSupportedException>(() =>
                WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct));
            Assert.Contains("reparse point", refused.Message);
            Assert.Equal(witness, ReadDescriptor(target));
        }
        finally
        {
            if (link is not null && Directory.Exists(link)) Directory.Delete(link);
            fixture.Delete(recursive: true);
        }
    }

    [Fact]
    public void Boundary_refuses_a_mount_point_to_a_different_disposable_NTFS_volume()
    {
        string fixture = RequireFixture();
        string? target = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_MOUNT_WITNESS");
        if (target is null) Assert.Skip("Requires a second disposable NTFS image mounted below the fixture.");
        var selected = new DirectoryInfo(Path.Combine(fixture, "scope-mount"));
        string mounted = Path.Combine(selected.FullName, "attached");
        Assert.True((File.GetAttributes(mounted) & FileAttributes.ReparsePoint) != 0);
        Assert.Equal("FileCat ACL Boundary Witness", new DriveInfo(Path.GetPathRoot(target)!).VolumeLabel);
        using var witness = Open(target, ReadControl);
        using var primary = Open(selected.FullName, ReadControl);
        Assert.NotEqual(ProtectedHexFile.Identity(primary).VolumeSerial, ProtectedHexFile.Identity(witness).VolumeSerial);
        byte[] before = ReadDescriptor(witness);
        var ct = TestContext.Current.CancellationToken;
        using var inspection = InspectBoundaryRoot(selected, ct);
        var refused = Assert.Throws<NotSupportedException>(() =>
            WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct));
        Assert.Contains("mounted folder", refused.Message);
        Assert.Equal(before, ReadDescriptor(witness));
    }

    [Fact]
    public void Boundary_refuses_a_branch_that_cannot_be_listed()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-native-boundary-denied-");
        DirectoryInfo? denied = null;
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            denied = selected.CreateSubdirectory("denied");
            CreateFile(denied, "owned-witness.txt");
            using var user = WindowsIdentity.GetCurrent();
            var security = denied.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
            denied.SetAccessControl(security);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = InspectBoundaryRoot(selected, ct);
            Assert.Throws<UnauthorizedAccessException>(() =>
                WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct));
        }
        finally
        {
            if (denied is not null) MakePrivate(denied);
            fixture.Delete(recursive: true);
        }
    }

    private static PermissionInspection InspectBoundaryRoot(DirectoryInfo directory, CancellationToken ct)
    {
        var subject = new PermissionSubject(Location.FileSystem(directory.Parent!.FullName), directory.FullName, directory.FullName,
            Authority, PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        return new WindowsPermissionService().Inspect(subject, PermissionSections.None, ct);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string alias, string target, nint security);
}
