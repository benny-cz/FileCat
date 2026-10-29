using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundComparedDeleteTreeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reviewed_tree_deletes_only_the_exact_unchanged_child(bool substitute)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows handle-bound tree deletion required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-reviewed-tree-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
            string child = Path.Combine(tree, "child.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            File.WriteAllBytes(child, a);
            var files = new WindowsFileOperations();
            var review = Assert.IsType<FileTreeReview>(FileTreeReview.Capture(tree, files,
                new LocalFileSystemProvider()));
            var entries = Entries(review);
            if (substitute)
            {
                DateTime stamp = File.GetLastWriteTimeUtc(child);
                DateTime folderStamp = Directory.GetLastWriteTimeUtc(tree);
                File.Move(child, Path.Combine(root, "retired-A.bin"));
                File.WriteAllBytes(child, b);
                File.SetLastWriteTimeUtc(child, stamp);
                Directory.SetLastWriteTimeUtc(tree, folderStamp);
            }

            var result = Assert.IsType<BoundTreeDeletion>(files.DeleteReviewedTree(tree, entries,
                () => { }, TestContext.Current.CancellationToken));
            if (substitute)
            {
                Assert.Equal(0, result.Done);
                Assert.True(result.Failed > 0);
                Assert.Equal(b, File.ReadAllBytes(child));
            }
            else
            {
                Assert.Equal(entries.Length, result.Done);
                Assert.Equal(0, result.Failed);
                Assert.False(Directory.Exists(tree));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Held_reviewed_child_cannot_be_swapped_after_its_final_check()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows handle-bound tree deletion required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-reviewed-tree-window-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
            string child = Path.Combine(tree, "child.bin");
            File.WriteAllText(child, "A");
            var files = new WindowsFileOperations();
            var review = Assert.IsType<FileTreeReview>(FileTreeReview.Capture(tree, files,
                new LocalFileSystemProvider()));
            bool attempted = false, blocked = false;
            var result = Assert.IsType<BoundTreeDeletion>(files.DeleteReviewedTreeCore(tree, Entries(review),
                () => { }, TestContext.Current.CancellationToken, () =>
                {
                    if (attempted) return;
                    attempted = true;
                    try { File.Move(child, Path.Combine(root, "retired-A.bin")); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { blocked = true; }
                }));
            Assert.True(attempted);
            Assert.True(blocked);
            Assert.Equal(0, result.Failed);
            Assert.False(Directory.Exists(tree));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static BoundDeletionEntry[] Entries(FileTreeReview review) => review.Items.Select(i =>
        new BoundDeletionEntry(i.Path, i.Version.Identity, i.Version.Info.IsDirectory,
            i.Version.Info.IsLink, i.Content?.Sha256, i.Content?.Length ?? -1)).ToArray();
}
