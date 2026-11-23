using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    [Theory]
    [InlineData("copy")]
    [InlineData("remove")]
    [InlineData("enable")]
    public async Task Native_inheritance_conversion_preserves_order_and_protected_descendants(string conversion)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-inheritance-conversion-");
        try
        {
            var selected = CreateInheritanceConversionFixture(fixture, conversion == "enable", explicitOwner: conversion == "remove");
            string ordinaryChild = CreateFile(selected, "child.txt");
            var protectedChild = selected.CreateSubdirectory("protected");
            MakePrivate(protectedChild);
            var protectionWitness = ReadDescriptor(protectedChild.FullName);

            var service = new WindowsPermissionService(qualifyNativePropagation: true, qualifyInheritanceChanges: true);
            var source = ItemRef.ForFileSystemPath(selected.FullName, EntryKind.Directory);
            var subject = new PermissionSubject(source.Parent, selected.FullName, selected.FullName, Authority,
                PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
            Assert.True(service.CanReviewInheritanceChange(inspection.Snapshot), inspection.Snapshot.BindingProblem);
            var boundary = WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct);
            var action = conversion switch
            {
                "copy" => WindowsInheritanceAction.ProtectCopy,
                "remove" => WindowsInheritanceAction.ProtectRemove,
                _ => WindowsInheritanceAction.Enable
            };
            var parent = action == WindowsInheritanceAction.Enable
                ? WindowsPermissionService.CaptureInheritanceParent(inspection.Snapshot, boundary, ct) : null;
            byte[] before = ReadDescriptor(selected.FullName);
            Assert.Equal(action == WindowsInheritanceAction.Enable, WindowsPermissionDescriptor.Control(before).IsProtected);
            var change = new ChangeWindowsInheritance(action);
            var prepared = service.PrepareNativePropagation(inspection, source, new(PermissionScope.NativePropagation, [change]), boundary, ct, parent);
            using var review = prepared.Review;

            var result = await RunJob(review, ct);
            byte[] after = ReadDescriptor(selected.FullName);
            var expected = Assert.IsType<WindowsDacl>(prepared.Expected.Access.Value?.Acl);
            output.WriteLine("Conversion {0}. Control {1:X4} → {2:X4} (expected {3:X4}). Result {4}.",
                conversion, WindowsPermissionDescriptor.Control(before).Flags, WindowsPermissionDescriptor.Control(after).Flags,
                prepared.Expected.Inheritance.Value!.Value.Flags, result.Verdict);
            if (result.Verdict != PermissionVerdict.Verified)
            {
                var component = Assert.Single(result.Components);
                output.WriteLine($"Native error {component.NativeError}. {component.Detail}");
                output.WriteLine("Original DACL: {0}", Convert.ToHexString(DaclBytes(before)));
                output.WriteLine("Projected DACL: {0}", Convert.ToHexString(DaclBytes(expected.SecurityDescriptor.ToArray())));
                output.WriteLine("Observed DACL: {0}", Convert.ToHexString(DaclBytes(after)));
            }
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal(PermissionVerification.SameObject, Assert.Single(result.Components).Verification);
            Assert.Equal(action != WindowsInheritanceAction.Enable, WindowsPermissionDescriptor.Control(after).IsProtected);
            Assert.Equal(DaclBytes(expected.SecurityDescriptor.ToArray()), DaclBytes(after));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(before, 4, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 4, Authority.Id));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(before, 8, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 8, Authority.Id));
            Assert.Equal(action == WindowsInheritanceAction.ProtectCopy, HasWitness(after, inherited: false));
            Assert.Equal(action == WindowsInheritanceAction.Enable, HasWitness(after, inherited: true));
            Assert.Equal(action != WindowsInheritanceAction.ProtectRemove, HasWitness(ReadDescriptor(ordinaryChild), inherited: true));
            Assert.Equal(protectionWitness, ReadDescriptor(protectedChild.FullName));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void Protect_and_copy_refuses_rule_occurrences_that_Windows_can_merge()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-inheritance-copy-repeat-");
        try
        {
            var selected = CreateInheritanceConversionFixture(fixture, protectedChild: false, explicitOwner: true);
            var service = new WindowsPermissionService(qualifyNativePropagation: true, qualifyInheritanceChanges: true);
            var source = ItemRef.ForFileSystemPath(selected.FullName, EntryKind.Directory);
            var subject = new PermissionSubject(source.Parent, selected.FullName, selected.FullName, Authority,
                PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
            var boundary = WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct);
            var change = new ChangeWindowsInheritance(WindowsInheritanceAction.ProtectCopy);
            byte[] before = ReadDescriptor(selected.FullName);

            Assert.Contains("merge repeated access rules", Assert.Throws<NotSupportedException>(() =>
                WindowsPermissionService.PreviewPropagation(inspection.Snapshot, change)).Message);
            Assert.Throws<NotSupportedException>(() =>
                service.PrepareNativePropagation(inspection, source, new(PermissionScope.NativePropagation, [change]), boundary, ct));
            Assert.Equal(before, ReadDescriptor(selected.FullName));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Enabling_inheritance_refuses_parent_DACL_changes_after_the_review()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-inheritance-parent-drift-");
        try
        {
            var selected = CreateInheritanceConversionFixture(fixture, protectedChild: true);
            var service = new WindowsPermissionService(qualifyNativePropagation: true, qualifyInheritanceChanges: true);
            var source = ItemRef.ForFileSystemPath(selected.FullName, EntryKind.Directory);
            var subject = new PermissionSubject(source.Parent, selected.FullName, selected.FullName, Authority,
                PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
            var ct = TestContext.Current.CancellationToken;
            using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
            var boundary = WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct);
            var parent = WindowsPermissionService.CaptureInheritanceParent(inspection.Snapshot, boundary, ct);
            var change = new ChangeWindowsInheritance(WindowsInheritanceAction.Enable);
            using var operation = service.PrepareNativePropagation(inspection, source, new(PermissionScope.NativePropagation, [change]),
                boundary, ct, parent).Review;

            byte[] before = ReadDescriptor(selected.FullName);
            var external = fixture.GetAccessControl(AccessControlSections.Access);
            external.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadExtendedAttributes,
                InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
            fixture.SetAccessControl(external);
            Assert.Equal(before, ReadDescriptor(selected.FullName));

            var result = await RunJob(operation, ct);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(before, ReadDescriptor(selected.FullName));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public void Enabling_inheritance_refuses_an_unreadable_parent_DACL()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-inheritance-parent-denied-");
        try
        {
            var selected = CreateInheritanceConversionFixture(fixture, protectedChild: true);
            using var restoration = Open(fixture.FullName, ReadControl | WriteDac);
            byte[] original = ReadDescriptor(restoration);
            var raw = new RawSecurityDescriptor(original, 0).DiscretionaryAcl!;
            raw.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessDenied, (int)ReadControl,
                new SecurityIdentifier("S-1-3-4"), isCallback: false, opaque: null));
            var deniedAcl = new byte[raw.BinaryLength];
            raw.GetBinaryForm(deniedAcl, 0);
            Assert.Equal(0u, SetDacl(restoration, deniedAcl));
            try
            {
                using var denied = OpenFile(fixture.FullName, ReadControl);
                Assert.True(denied.IsInvalid, "The parent DACL is still readable to the ordinary actor.");
                var ct = TestContext.Current.CancellationToken;
                var service = new WindowsPermissionService(qualifyNativePropagation: true, qualifyInheritanceChanges: true);
                var source = ItemRef.ForFileSystemPath(selected.FullName, EntryKind.Directory);
                var subject = new PermissionSubject(source.Parent, selected.FullName, selected.FullName, Authority,
                    PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
                using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
                var boundary = WindowsPermissionService.CapturePropagationBoundary(inspection.Snapshot, ct);
                Assert.Throws<UnauthorizedAccessException>(() =>
                    WindowsPermissionService.CaptureInheritanceParent(inspection.Snapshot, boundary, ct));
            }
            finally { Assert.Equal(0u, SetDacl(restoration, DaclBytes(original))); }
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static DirectoryInfo CreateInheritanceConversionFixture(DirectoryInfo parent, bool protectedChild, bool explicitOwner = false)
    {
        MakePrivate(parent);
        using var owner = WindowsIdentity.GetCurrent();
        using (var handle = Open(parent.FullName, ReadControl | WriteDac))
            Assert.Equal(0u, SetDacl(handle, AddInheritableWitness(ReadDescriptor(handle))));
        var selected = parent.CreateSubdirectory("selected");
        if (protectedChild) MakePrivate(selected);
        else
        {
            var selectedAcl = selected.GetAccessControl(AccessControlSections.Access);
            selectedAcl.AddAccessRule(explicitOwner
                ? new FileSystemAccessRule(owner.User!, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow)
                : new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadExtendedAttributes, AccessControlType.Allow));
            selected.SetAccessControl(selectedAcl);
        }
        return selected;
    }
}
