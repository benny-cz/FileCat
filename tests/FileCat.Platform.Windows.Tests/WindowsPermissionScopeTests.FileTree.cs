using System.Runtime.InteropServices;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsPermissionScopeTests
{
    [Fact]
    public async Task Captured_file_rule_leaves_folders_matching_files_and_later_arrivals_unchanged()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-captured-acl-");
        try
        {
            MakePrivate(fixture);
            var root = fixture.CreateSubdirectory("selected");
            var nested = root.CreateSubdirectory("nested");
            string file = CreateFile(nested, "change.txt");
            string matching = CreateFile(root, "already.txt");
            string sibling = CreateFile(fixture, "outside.txt");
            var witness = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            using (var held = Open(matching, ReadControl | WriteDac))
            {
                var acl = WindowsPermissionDescriptor.Dacl(ReadDescriptor(held), Authority.Id);
                Assert.Equal(0u, SetDacl(held, WindowsPermissionAclPatch.InsertCanonical(acl, witness.Entry, Authority.Id).ToArray()));
            }
            byte[][] before = [ReadDescriptor(root.FullName), ReadDescriptor(nested.FullName), ReadDescriptor(matching), ReadDescriptor(sibling)];
            var ct = TestContext.Current.CancellationToken;
            var review = ReviewCapturedFiles(root, witness, ct);
            using var operation = review.Operation;
            Assert.Equal(2, review.Directories);
            Assert.Equal(1, operation.Tree!.RootCount);
            Assert.Equal([file, matching], review.Rows.Select(row => row.Path));
            Assert.False(review.Rows[0].AlreadyPresent);
            Assert.True(review.Rows[1].AlreadyPresent);
            string late = CreateFile(nested, "late.txt");
            byte[] lateBefore = ReadDescriptor(late);

            var results = await RunJobResults(operation, ct);
            Assert.Equal([PermissionVerdict.Verified, PermissionVerdict.AlreadyMatching], results.Select(result => result.Verdict));
            Assert.Null(Assert.Single(results[1].Components).NativeCall);
            Assert.True(HasWitness(ReadDescriptor(file), inherited: false));
            Assert.Equal(before[0], ReadDescriptor(root.FullName));
            Assert.Equal(before[1], ReadDescriptor(nested.FullName));
            Assert.Equal(before[2], ReadDescriptor(matching));
            Assert.Equal(before[3], ReadDescriptor(sibling));
            Assert.Equal(lateBefore, ReadDescriptor(late));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Captured_file_rule_refuses_a_new_hard_link_or_replaced_original_name(bool hardlink)
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-captured-acl-drift-");
        try
        {
            MakePrivate(fixture);
            var root = fixture.CreateSubdirectory("selected");
            var nested = root.CreateSubdirectory("nested");
            string path = CreateFile(nested, "entry.txt");
            var witness = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            var ct = TestContext.Current.CancellationToken;
            using var operation = ReviewCapturedFiles(root, witness, ct).Operation;
            byte[] before = ReadDescriptor(path);
            string original = path;
            if (hardlink)
            {
                original = Path.Combine(fixture.FullName, "alias.txt");
                Assert.True(CreateHardLink(original, path, 0), $"Create hard link returned {Marshal.GetLastPInvokeError()}.");
            }
            else
            {
                original = Path.Combine(fixture.FullName, "renamed.txt");
                File.Move(path, original);
                CreateFile(nested, "entry.txt");
            }
            byte[] current = ReadDescriptor(path);
            var result = await RunJob(operation, ct);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(before, ReadDescriptor(original));
            Assert.Equal(current, ReadDescriptor(path));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static WindowsPermissionService.FileTreeReview ReviewCapturedFiles(DirectoryInfo folder, AddWindowsAce change, CancellationToken ct)
    {
        var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
        var subject = new PermissionSubject(source.Parent, folder.FullName, folder.FullName, Authority,
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var service = new WindowsPermissionService(qualifyFileTrees: true);
        using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
        Assert.True(service.CanReviewFileTree(inspection.Snapshot), inspection.Snapshot.BindingProblem);
        Assert.False(new WindowsPermissionService().CanReviewFileTree(inspection.Snapshot));
        return service.PrepareFileTree(inspection.Snapshot, source, change, ct);
    }
}
