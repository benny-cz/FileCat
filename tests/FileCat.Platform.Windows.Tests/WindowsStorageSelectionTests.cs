using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsStorageSelectionTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static void RequireNormal()
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "Selection review requires the normal user process.");
    }
    private static StorageSelectionReview Review(string path) => new(new WindowsStorageTransforms(), [ItemRef.ForFileSystemPath(path, EntryKind.File)]);
    private static void Inspect(StorageSelectionReview review, long revision)
    { using var native = StorageReviewResources.EnterNative(Stop); review.InspectOwned(revision, Stop); }

    [Fact]
    public void Cached_actions_and_explicit_Retry_use_one_original_pin_and_never_retarget_a_replacement()
    {
        RequireNormal(); string root = Directory.CreateTempSubdirectory("filecat-selection-").FullName;
        string path = Path.Join(root, "selected 名前.txt"), moved = Path.Join(root, "retained.txt");
        try
        {
            File.WriteAllText(path, "original fixture"); File.SetAttributes(path, FileAttributes.ReadOnly);
            using var review = Review(path); long revision = review.Request(null, StorageScope.SelectedItems); Inspect(review, revision);
            var original = Assert.IsType<StorageObservation>(review.State.Roots[0].Observation).Identity;
            Assert.False(review.State.CanApprove); Assert.Null(StorageNativeAllowance.Current);
            revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems);
            Assert.False(review.State.NeedsInspection); Assert.Equal("read-only-file", review.State.Roots[0].Observation!.Ineligible!.Code);
            revision = review.Request(StorageAction.RemoveCompression, StorageScope.ExistingDescendants);
            Assert.True(review.State.CanApprove); Assert.False(review.State.NeedsInspection); Assert.Equal(original, review.State.Roots[0].Observation!.Identity);
            File.SetAttributes(path, FileAttributes.Normal);
            revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems, retry: true); Inspect(review, revision);
            Assert.True(review.State.CanApprove); Assert.Equal(original, review.State.Roots[0].Observation!.Identity);
            File.Move(path, moved); File.WriteAllText(path, "replacement fixture");
            revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems, retry: true); Inspect(review, revision);
            Assert.True(review.State.Roots[0].Stale); Assert.False(review.State.CanApprove);
            Assert.Equal(original, review.State.Roots[0].Observation!.Identity);
            Assert.Equal("replacement fixture", File.ReadAllText(path)); Assert.Equal("original fixture", File.ReadAllText(moved));
            Assert.Throws<InvalidOperationException>(() => review.Freeze(revision));
        }
        finally { if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal); Directory.Delete(root, true); }
    }

    [Fact]
    public void First_pin_failure_does_not_discard_sibling_identity_or_require_a_subset_selection()
    {
        RequireNormal(); string root = Directory.CreateTempSubdirectory("filecat-selection-mixed-").FullName;
        try
        {
            string existing = Path.Join(root, "existing.txt"), missing = Path.Join(root, "missing.txt"); File.WriteAllText(existing, "fixture");
            using var review = new StorageSelectionReview(new WindowsStorageTransforms(),
                [ItemRef.ForFileSystemPath(existing, EntryKind.File), ItemRef.ForFileSystemPath(missing, EntryKind.File)]);
            long revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems); Inspect(review, revision);
            Assert.True(review.State.Roots[0].Pinned); Assert.False(review.State.Roots[1].Pinned); Assert.NotNull(review.State.Roots[1].Problem);
            var identity = review.State.Roots[0].Observation!.Identity; Assert.False(review.State.CanApprove);
            File.WriteAllText(missing, "new first-pin fixture"); revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems, retry: true); Inspect(review, revision);
            Assert.True(review.State.CanApprove); Assert.Equal(identity, review.State.Roots[0].Observation!.Identity);
            using var frozen = review.Freeze(revision); Assert.Equal(2, frozen.Roots.Length); Assert.Equal(2, frozen.Sources.Length);
            _ = frozen.Approve(StorageAction.Compress, StorageScope.SelectedItems);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Efs_conflict_is_derived_from_cached_flags_without_a_key_or_conversion_service()
    {
        RequireNormal(); Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Opt-in EFS metadata fixture on the disposable VM.");
        string root = Directory.CreateTempSubdirectory("filecat-selection-efs-").FullName; string path = Path.Join(root, "encrypted.txt");
        try
        {
            File.WriteAllText(path, "synthetic encrypted fixture"); File.Encrypt(path);
            using var review = Review(path); long revision = review.Request(null, StorageScope.SelectedItems); Inspect(review, revision);
            Assert.Equal(StorageObservationState.Enabled, review.State.Roots[0].Observation!.Efs);
            revision = review.Request(StorageAction.Compress, StorageScope.SelectedItems);
            Assert.False(review.State.NeedsInspection); Assert.False(review.State.CanApprove); Assert.Equal("efs-conflict", review.State.Roots[0].Observation!.Ineligible!.Code);
            revision = review.Request(StorageAction.RemoveCompression, StorageScope.SelectedItems);
            Assert.False(review.State.NeedsInspection); Assert.True(review.State.CanApprove);
            Assert.True((File.GetAttributes(path) & FileAttributes.Encrypted) != 0); Assert.Equal("synthetic encrypted fixture", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Decrypt(path); Directory.Delete(root, true); }
    }
}
