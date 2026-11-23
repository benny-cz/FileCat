using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// N-ID.C held-directory sequence: a no-op directory whose default changes before descent needs a fresh prepared call,
/// and a directory that changes after its enumeration began is never promoted under the open cursor.
/// </summary>
public sealed partial class WindowsStorageTraversalTests
{
    private static void SetDirectoryCompression(string path, StorageAction action)
    {
        using var writer = NtFile.OpenOrThrow(null, @"\??\" + path, NtFile.ReadData | NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
            NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Change the disposable directory default");
        Assert.True(WindowsCompression.InvokeAdmitted(writer, action).NativeSucceeded);
    }

    [Fact]
    public void A_no_op_directory_whose_default_changes_before_descent_needs_a_fresh_prepared_call()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(arm64), "Qualified Windows client NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-needs-preparation-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "folder")).FullName;
            string child = Path.Join(folder, "child"); File.WriteAllText(child, "child payload");
            SetDirectoryCompression(folder, StorageAction.Compress);
            var adapter = new WindowsStorageTransforms(qualifyNativeArm64: arm64);
            var prepared = Review(adapter, folder, EntryKind.Directory, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            Assert.Equal(StorageEffect.AlreadySatisfied, Apply(traversal.Root, StorageAction.Compress).Effect);
            SetDirectoryCompression(folder, StorageAction.RemoveCompression); // a competitor clears the default before descent
            var refused = Assert.Throws<StorageInspectionException>(() =>
            {
                using var cursor = traversal.Children(traversal.Root, TestContext.Current.CancellationToken).GetEnumerator();
                cursor.MoveNext();
            });
            Assert.Equal(("directory-needs-preparation", StorageReasonKind.IdentityChanged), (refused.Problem.Code, refused.Problem.Kind));
            Assert.Equal((FileAttributes)0, File.GetAttributes(folder) & FileAttributes.Compressed);
            Assert.Equal((FileAttributes)0, File.GetAttributes(child) & FileAttributes.Compressed);
            Assert.Equal("child payload", File.ReadAllText(child));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void A_directory_that_changes_after_its_enumeration_began_is_not_promoted_under_the_open_cursor()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(arm64), "Qualified Windows client NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-cursor-active-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "folder")).FullName;
            foreach (string name in new[] { "first", "second" }) File.WriteAllText(Path.Join(folder, name), name + " payload");
            SetDirectoryCompression(folder, StorageAction.Compress);
            var adapter = new WindowsStorageTransforms(qualifyNativeArm64: arm64);
            var prepared = Review(adapter, folder, EntryKind.Directory, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            Assert.Equal(StorageEffect.AlreadySatisfied, Apply(traversal.Root, StorageAction.Compress).Effect);
            using (var cursor = traversal.Children(traversal.Root, TestContext.Current.CancellationToken).GetEnumerator())
            {
                Assert.True(cursor.MoveNext());
                cursor.Current.Dispose();
            }
            SetDirectoryCompression(folder, StorageAction.RemoveCompression);
            var refused = Assert.Throws<StorageInspectionException>(() =>
            {
                using var again = traversal.Children(traversal.Root, TestContext.Current.CancellationToken).GetEnumerator();
                again.MoveNext();
            });
            Assert.Equal(("cursor-active", StorageReasonKind.IdentityChanged), (refused.Problem.Code, refused.Problem.Kind));
            foreach (string name in new[] { "first", "second" })
            {
                Assert.Equal((FileAttributes)0, File.GetAttributes(Path.Join(folder, name)) & FileAttributes.Compressed);
                Assert.Equal(name + " payload", File.ReadAllText(Path.Join(folder, name)));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
