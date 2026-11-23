using System.Security.AccessControl;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    private static readonly Principal RemovedRuleAccount = new(PrincipalKind.WindowsSid, "S-1-5-32-545", Authority.Id);

    [Fact]
    public async Task Windows_reset_replaces_file_and_directory_rules_children_first_and_limits_the_scope()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-windows-reset-");
        try
        {
            MakePrivate(fixture);
            var source = fixture.CreateSubdirectory("source");
            using (var handle = Open(source.FullName, ReadControl | WriteDac))
                Assert.Equal(0u, SetDacl(handle, AddInheritableWitness(ReadDescriptor(handle))));
            byte[] template = ReadDescriptor(source.FullName);
            string matching = CreateFile(source, "matching.txt");
            var nested = source.CreateSubdirectory("nested");
            AddResetWitness(nested.FullName, flags: 3);
            string file = CreateFile(nested, "captured.txt");
            AddResetWitness(file, flags: 0);
            var protectedBranch = source.CreateSubdirectory("protected");
            MakePrivate(protectedBranch);
            string protectedFile = CreateFile(protectedBranch, "untouched.txt");
            var protectedLeaf = new FileInfo(CreateFile(source, "protected.txt"));
            var leafAccess = protectedLeaf.GetAccessControl(AccessControlSections.Access);
            leafAccess.SetAccessRuleProtection(true, true);
            protectedLeaf.SetAccessControl(leafAccess);
            byte[] leafAcl = ReadDescriptor(protectedLeaf.FullName);
            byte[] protectedAcl = ReadDescriptor(protectedFile);
            byte[] directoryAcl = ReadDescriptor(protectedBranch.FullName);
            var ct = TestContext.Current.CancellationToken;
            var prepared = ReviewWindowsReset(source, ct);
            using var review = prepared.Operation;
            var fileSource = ItemRef.ForFileSystemPath(file, EntryKind.File);
            using (var direct = new WindowsPermissionService().Inspect(FileSubject(fileSource), WindowsPermissionService.DiscretionarySections, ct))
                Assert.Throws<NotSupportedException>(() => WindowsPermissionService.Prepare(direct, fileSource,
                    new(PermissionScope.Objects, [review.Targets[0].Components[0].Change]), ct));
            Assert.Equal(source.FullName, review.TreeSource!.Subject.RawLocator);
            Assert.Equal(2, review.Tree!.ProtectedEntries);
            Assert.Equal([file, matching, nested.FullName], review.Targets.Select(target => target.Subject.RawLocator));
            Assert.Contains(prepared.Rows[0].Before, entry => entry.Principal == RemovedRuleAccount && (entry.Flags & 0x10) == 0);
            Assert.All(prepared.Rows[0].After, entry => Assert.NotEqual(0, entry.Flags & 0x10));
            Assert.DoesNotContain(prepared.Rows[0].After, entry => entry.Principal == RemovedRuleAccount);
            string late = CreateFile(nested, "late.txt");
            byte[] lateBefore = ReadDescriptor(late);

            var results = await RunJobResults(review, ct);
            Assert.Equal([PermissionVerdict.Verified, PermissionVerdict.AlreadyMatching, PermissionVerdict.Verified],
                results.Select(result => result.Verdict));
            Assert.Equal("Windows NtSetSecurityObject reset DACL", Assert.Single(results[0].Components).NativeCall);
            Assert.Null(Assert.Single(results[1].Components).NativeCall);
            foreach (var row in prepared.Rows.Where(row => row.Changed))
            {
                var actual = WindowsPermissionDescriptor.Dacl(ReadDescriptor(row.Path), Authority.Id);
                Assert.Equal(row.After.Select(rule => Convert.ToHexString(rule.Bytes.AsSpan())),
                    actual.Entries.Select(rule => Convert.ToHexString(rule.Bytes.AsSpan())));
            }
            string future = CreateFile(nested, "future.txt");
            Assert.Equal(DaclBytes(ReadDescriptor(future)), DaclBytes(ReadDescriptor(file)));
            Assert.Equal(template, ReadDescriptor(source.FullName));
            Assert.Equal(lateBefore, ReadDescriptor(late));
            Assert.Equal(protectedAcl, ReadDescriptor(protectedFile));
            Assert.Equal(directoryAcl, ReadDescriptor(protectedBranch.FullName));
            Assert.Equal(leafAcl, ReadDescriptor(protectedLeaf.FullName));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Windows_reset_refuses_a_new_hard_link_before_dispatch()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-windows-reset-link-");
        try
        {
            MakePrivate(fixture);
            var source = fixture.CreateSubdirectory("source");
            string file = CreateFile(source, "captured.txt");
            AddResetWitness(file, flags: 0);
            byte[] original = ReadDescriptor(file);
            var ct = TestContext.Current.CancellationToken;
            using var review = ReviewWindowsReset(source, ct).Operation;
            string alias = Path.Combine(fixture.FullName, "outside.txt");
            Assert.True(CreateHardLink(alias, file, 0), $"Create hard link returned {Marshal.GetLastPInvokeError()}.");
            var result = await RunJob(review, ct);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(original, ReadDescriptor(alias));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Windows_reset_refuses_when_the_source_or_an_intermediate_ancestor_changes(bool changeTemplate)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-windows-reset-drift-");
        try
        {
            MakePrivate(fixture);
            var source = fixture.CreateSubdirectory("source");
            var first = source.CreateSubdirectory("first");
            var second = first.CreateSubdirectory("second");
            AddResetWitness(first.FullName, flags: 3);
            string file = CreateFile(second, "captured.txt");
            var ct = TestContext.Current.CancellationToken;
            using var reset = ReviewWindowsReset(source, ct).Operation;
            byte[] baseline = ReadDescriptor(file);

            var external = new AddWindowsAce(WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, WitnessAccount, WitnessRight));
            using var outside = ReviewFolderOnly(changeTemplate ? source : first, external, ct).Review;
            Assert.Equal(PermissionVerdict.Verified, (await RunJob(outside, ct)).Verdict);
            Assert.Equal(baseline, ReadDescriptor(file));

            var results = await RunJobResults(reset, ct);
            Assert.Equal(3, results.Count);
            Assert.All(results, result =>
            {
                Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
                Assert.Null(Assert.Single(result.Components).NativeCall);
            });
            Assert.Equal(baseline, ReadDescriptor(file));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static WindowsPermissionService.AclResetReview ReviewWindowsReset(DirectoryInfo source, CancellationToken ct)
    {
        var service = new WindowsPermissionService(qualifyAclResets: true);
        var item = ItemRef.ForFileSystemPath(source.FullName, EntryKind.Directory);
        using var inspection = service.Inspect(FolderOnlySubject(item), WindowsPermissionService.DiscretionarySections, ct);
        Assert.True(service.CanReviewAclReset(inspection.Snapshot), inspection.Snapshot.BindingProblem ?? inspection.Snapshot.Access.Reason);
        Assert.False(new WindowsPermissionService().CanReviewAclReset(inspection.Snapshot));
        return service.PrepareAclReset(inspection, item, ct);
    }

    private static void AddResetWitness(string path, byte flags)
    {
        using var handle = Open(path, ReadControl | WriteDac);
        var acl = WindowsPermissionDescriptor.Dacl(ReadDescriptor(handle), Authority.Id);
        var entry = WindowsPermissionAclPatch.SimpleEntry(WindowsAceKind.Allow, RemovedRuleAccount,
            (uint)FileSystemRights.ReadExtendedAttributes, flags);
        Assert.Equal(0u, SetDacl(handle, WindowsPermissionAclPatch.InsertCanonical(acl, entry, Authority.Id, directory: flags != 0).ToArray()));
    }
}
