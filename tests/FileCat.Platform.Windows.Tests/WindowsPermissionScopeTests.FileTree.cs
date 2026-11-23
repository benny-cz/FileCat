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

    private static WindowsPermissionService.AccessTreeReview ReviewCapturedFiles(DirectoryInfo folder, AddWindowsAce change, CancellationToken ct)
    {
        var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
        var subject = new PermissionSubject(source.Parent, folder.FullName, folder.FullName, Authority,
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var service = new WindowsPermissionService(qualifyFileTrees: true);
        using var inspection = service.Inspect(subject, WindowsPermissionService.DiscretionarySections, ct);
        Assert.True(service.CanReviewAccessTree(inspection.Snapshot, PermissionObjectKind.File), inspection.Snapshot.BindingProblem);
        Assert.False(new WindowsPermissionService().CanReviewAccessTree(inspection.Snapshot, PermissionObjectKind.File));
        return service.PrepareAccessTree(inspection.Snapshot, source, change, PermissionObjectKind.File, ct);
    }

    [Fact]
    public async Task Captured_folder_rule_changes_reviewed_directories_children_first()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-captured-folders-");
        try
        {
            MakePrivate(fixture);
            var root = fixture.CreateSubdirectory("selected");
            var parent = root.CreateSubdirectory("parent");
            var nested = parent.CreateSubdirectory("nested");
            var matching = nested.CreateSubdirectory("matching");
            string file = CreateFile(parent, "existing.txt");
            string sibling = CreateFile(fixture, "outside.txt");
            var witness = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            var ct = TestContext.Current.CancellationToken;
            using (var seed = ReviewFolderOnly(matching, witness, ct).Review)
                Assert.Equal(PermissionVerdict.Verified, (await RunJob(seed, ct)).Verdict);
            var review = ReviewCapturedFolders(root, witness, ct);
            using var operation = review.Operation;
            Assert.Equal([matching.FullName, nested.FullName, parent.FullName], review.Rows.Select(row => row.Path));
            Assert.Equal([true, false, false], review.Rows.Select(row => row.AlreadyPresent));
            var late = parent.CreateSubdirectory("late");
            string[] sentinels = [root.FullName, matching.FullName, file, sibling, late.FullName];
            var unchanged = sentinels.Select(ReadDescriptor).ToArray();
            var results = await RunJobResults(operation, ct);
            Assert.Equal([PermissionVerdict.AlreadyMatching, PermissionVerdict.Verified, PermissionVerdict.Verified],
                results.Select(result => result.Verdict));
            Assert.Null(Assert.Single(results[0].Components).NativeCall);
            Assert.All(results.Skip(1), result => Assert.Equal("Windows NtSetSecurityObject folder DACL",
                Assert.Single(result.Components).NativeCall));
            Assert.True(HasWitness(ReadDescriptor(parent.FullName), inherited: false));
            Assert.True(HasWitness(ReadDescriptor(nested.FullName), inherited: false));
            for (int index = 0; index < sentinels.Length; index++) Assert.Equal(unchanged[index], ReadDescriptor(sentinels[index]));
            Assert.False(HasWitness(ReadDescriptor(parent.CreateSubdirectory("future").FullName), inherited: true));
        }
        finally { fixture.Delete(recursive: true); }
    }

    [Fact]
    public async Task Captured_folder_rule_refuses_protection_and_same_name_replacement()
    {
        RequireFixture();
        var fixture = Directory.CreateTempSubdirectory("filecat-captured-folder-drift-");
        try
        {
            MakePrivate(fixture);
            var root = fixture.CreateSubdirectory("selected");
            var child = root.CreateSubdirectory("child");
            var witness = Assert.IsType<AddWindowsAce>(Assert.Single(WitnessDraft().Changes));
            var ct = TestContext.Current.CancellationToken;
            using var operation = ReviewCapturedFolders(root, witness, ct).Operation;
            byte[] original = ReadDescriptor(child.FullName);
            child.MoveTo(Path.Combine(fixture.FullName, "moved"));
            var replacement = root.CreateSubdirectory("child");
            MakePrivate(replacement);
            byte[] current = ReadDescriptor(replacement.FullName);
            Assert.Throws<NotSupportedException>(() => ReviewCapturedFolders(root, witness, ct));
            var result = await RunJob(operation, ct);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            Assert.Null(Assert.Single(result.Components).NativeCall);
            Assert.Equal(original, ReadDescriptor(child.FullName));
            Assert.Equal(current, ReadDescriptor(replacement.FullName));
        }
        finally { fixture.Delete(recursive: true); }
    }

    private static WindowsPermissionService.AccessTreeReview ReviewCapturedFolders(DirectoryInfo folder, AddWindowsAce change, CancellationToken ct)
    {
        var service = new WindowsPermissionService(qualifyFolderTrees: true);
        var source = ItemRef.ForFileSystemPath(folder.FullName, EntryKind.Directory);
        using var inspection = service.Inspect(FolderOnlySubject(source), WindowsPermissionService.DiscretionarySections, ct);
        Assert.True(service.CanReviewAccessTree(inspection.Snapshot, PermissionObjectKind.Directory), inspection.Snapshot.BindingProblem);
        Assert.False(service.CanReviewAccessTree(inspection.Snapshot, PermissionObjectKind.File));
        Assert.False(new WindowsPermissionService(qualifyFileTrees: true)
            .CanReviewAccessTree(inspection.Snapshot, PermissionObjectKind.Directory));
        return service.PrepareAccessTree(inspection.Snapshot, source, change, PermissionObjectKind.Directory, ct);
    }
}
