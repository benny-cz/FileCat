using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    [Theory]
    [InlineData(WindowsAceKind.Allow)]
    [InlineData(WindowsAceKind.Deny)]
    public async Task Folder_only_rule_preserves_existing_children_and_sets_future_inheritance(WindowsAceKind kind)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-folder-only-acl-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var access = selected.GetAccessControl(AccessControlSections.Access);
                access.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
                selected.SetAccessControl(access);
            }
            var nested = selected.CreateSubdirectory("nested");
            var protectedBranch = selected.CreateSubdirectory("protected");
            MakePrivate(protectedBranch);
            string openChild = CreateFile(nested, "open.txt");
            string direct = CreateFile(selected, "direct.txt");
            string protectedChild = CreateFile(protectedBranch, "private.txt");
            string sibling = CreateFile(fixture, "sibling.txt");
            string[] sentinels = [nested.FullName, protectedBranch.FullName, openChild, direct, protectedChild, sibling];
            var unchanged = sentinels.Select(ReadDescriptor).ToArray();
            var before = ReadDescriptor(selected.FullName);
            Assert.False(WindowsPermissionDescriptor.Control(before).IsProtected);
            var baseline = WindowsPermissionDescriptor.Dacl(before, Authority.Id);
            Assert.Contains(baseline.Entries, entry => (entry.Flags & (byte)AceFlags.Inherited) == 0);
            using var handle = File.Open(openChild, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var rule = WindowsPermissionAclPatch.SimpleEntry(kind, WitnessAccount, WitnessRight, flags: 3);
            var ct = TestContext.Current.CancellationToken;
            var prepared = ReviewFolderOnly(selected, new AddWindowsAce(rule), ct);
            using var review = prepared.Review;

            var result = await RunJob(review, ct);
            var after = ReadDescriptor(selected.FullName);
            var expected = Assert.IsType<WindowsDacl>(prepared.Expected.Access.Value?.Acl);
            output.WriteLine($"Folder-only {kind}. Control {WindowsPermissionDescriptor.Control(before).Flags:X4} → " +
                $"{WindowsPermissionDescriptor.Control(after).Flags:X4}. DACL match {DaclBytes(after).AsSpan().SequenceEqual(DaclBytes(expected.SecurityDescriptor.ToArray()))}. Result {result.Verdict}.");
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            var component = Assert.Single(result.Components);
            Assert.Equal(PermissionVerification.SameObject, component.Verification);
            Assert.Equal("Windows NtSetSecurityObject folder DACL", component.NativeCall);
            Assert.Equal(DaclBytes(expected.SecurityDescriptor.ToArray()), DaclBytes(after));
            Assert.Equal(WindowsPermissionDescriptor.Control(before), WindowsPermissionDescriptor.Control(after));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(before, 4, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 4, Authority.Id));
            Assert.Equal(WindowsPermissionDescriptor.PrincipalAt(before, 8, Authority.Id), WindowsPermissionDescriptor.PrincipalAt(after, 8, Authority.Id));
            for (int index = 0; index < sentinels.Length; index++) Assert.Equal(unchanged[index], ReadDescriptor(sentinels[index]));

            string newFile = CreateFile(selected, "future.txt");
            var newFolder = selected.CreateSubdirectory("future");
            foreach (string path in new[] { newFile, newFolder.FullName })
            {
                var observed = WindowsPermissionDescriptor.Dacl(ReadDescriptor(path), Authority.Id);
                Assert.Contains(observed.Entries, entry => entry.Kind == kind && entry.Principal == WitnessAccount &&
                    entry.Mask == WitnessRight && (entry.Flags & (byte)AceFlags.Inherited) != 0);
            }
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Folder_only_edits_one_existing_explicit_occurrence(bool remove)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-folder-only-existing-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            using (var root = Open(selected.FullName, ReadControl | WriteDac))
                Assert.Equal(0u, SetDacl(root, AddInheritableWitness(ReadDescriptor(root))));
            string child = CreateFile(selected, "existing.txt");
            var childBefore = ReadDescriptor(child);
            var baseline = WindowsPermissionDescriptor.Dacl(ReadDescriptor(selected.FullName), Authority.Id);
            var entry = Assert.Single(baseline.Entries, rule => rule.Principal == WitnessAccount);
            var replacement = WindowsPermissionAclPatch.SimpleEntry(entry.Kind, WitnessAccount, (uint)FileSystemRights.ReadExtendedAttributes, entry.Flags);
            var change = new ChangeWindowsAce(baseline.Entries.IndexOf(entry), entry, remove ? null : replacement);
            var ct = TestContext.Current.CancellationToken;
            var prepared = ReviewFolderOnly(selected, change, ct);
            using var review = prepared.Review;

            Assert.Equal(PermissionVerdict.Verified, (await RunJob(review, ct)).Verdict);
            var after = WindowsPermissionDescriptor.Dacl(ReadDescriptor(selected.FullName), Authority.Id);
            var expected = Assert.IsType<WindowsDacl>(prepared.Expected.Access.Value?.Acl);
            Assert.Equal(DaclBytes(expected.SecurityDescriptor.ToArray()), DaclBytes(after.SecurityDescriptor.ToArray()));
            Assert.Equal(remove ? 0 : 1, after.Entries.Count(rule => rule.Principal == WitnessAccount));
            Assert.Equal(childBefore, ReadDescriptor(child));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Folder_only_refuses_protected_baseline_and_protection_drift()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-folder-only-drift-");
        try
        {
            MakePrivate(fixture);
            var selected = fixture.CreateSubdirectory("selected");
            var ct = TestContext.Current.CancellationToken;
            var change = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            using var review = ReviewFolderOnly(selected, change, ct).Review;
            MakePrivate(selected);
            byte[] protectedDescriptor = ReadDescriptor(selected.FullName);
            var service = new WindowsPermissionService(qualifyFolderOnly: true);
            var source = ItemRef.ForFileSystemPath(selected.FullName, EntryKind.Directory);
            using var inspection = service.Inspect(FolderOnlySubject(source), WindowsPermissionService.DiscretionarySections, ct);
            Assert.False(service.CanReviewFolderOnly(inspection.Snapshot));
            Assert.Throws<NotSupportedException>(() => service.PrepareFolderOnly(inspection, source, new(PermissionScope.Objects, [change]), ct));

            var result = await RunJob(review, ct);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(protectedDescriptor, ReadDescriptor(selected.FullName));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static (ReviewedPermissionOperation Review, PermissionSnapshot Expected) ReviewFolderOnly(DirectoryInfo folder,
        PermissionDelta change, CancellationToken ct)
    {
        var service = new WindowsPermissionService(qualifyFolderOnly: true);
        var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
        using var inspection = service.Inspect(FolderOnlySubject(source), WindowsPermissionService.DiscretionarySections, ct);
        Assert.True(service.CanReviewFolderOnly(inspection.Snapshot), inspection.Snapshot.BindingProblem ?? inspection.Snapshot.Access.Reason);
        Assert.False(new WindowsPermissionService().CanReviewFolderOnly(inspection.Snapshot));
        return service.PrepareFolderOnly(inspection, source, new(PermissionScope.Objects, [change]), ct);
    }

    private static PermissionSubject FolderOnlySubject(ItemRef source) => new(source.Parent, source.FileSystemPath!, source.FileSystemPath!,
        Authority, PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
}
