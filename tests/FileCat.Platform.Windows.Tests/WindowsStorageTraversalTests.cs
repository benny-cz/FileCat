using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsStorageTraversalTests
{
    [Fact]
    public void A_deleted_child_returns_a_failure_entry_without_losing_remaining_cursor_siblings()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-entry-failure-").FullName;
        try
        {
            foreach (string name in new[] { "a", "b", "c" }) File.WriteAllText(Path.Join(root, name), "preserved bytes");
            var adapter = new WindowsStorageTransforms(); var reviewed = Review(adapter, root, EntryKind.Directory, StorageAction.RemoveCompression);
            using var review = reviewed.Review; using var owner = reviewed.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.RemoveCompression, TestContext.Current.CancellationToken);
            using var entries = traversal.Entries(traversal.Root, TestContext.Current.CancellationToken).GetEnumerator();
            Assert.True(entries.MoveNext()); var first = entries.Current; Assert.NotNull(first.Object);
            string victim = Directory.EnumerateFiles(root).First(path => path != first.Path);
            File.Delete(victim); first.Object.Dispose();
            int failed = 0, admitted = 1;
            while (entries.MoveNext())
            {
                var entry = entries.Current;
                if (entry.Object is { } held) { admitted++; held.Dispose(); Assert.Null(entry.Problem); }
                else { failed++; Assert.Equal(victim, entry.Path); Assert.NotNull(entry.Problem); Assert.Equal(StorageReasonKind.IdentityChanged, entry.Problem.Kind); }
            }
            Assert.Equal(1, failed); Assert.Equal(2, admitted);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Overlap_planning_uses_held_native_ancestry_and_original_source_mappings_without_mutating_defaults()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-overlap-").FullName;
        try
        {
            string parent = Directory.CreateDirectory(Path.Join(root, "parent")).FullName;
            string child = Path.Join(parent, "child"); File.WriteAllText(child, "preserved bytes");
            var adapter = new WindowsStorageTransforms();
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(root, "unused-journal"));
            using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(child, EntryKind.File),
                ItemRef.ForFileSystemPath(parent, EntryKind.Directory), ItemRef.ForFileSystemPath(parent, EntryKind.Directory)], StorageAction.Compress, TestContext.Current.CancellationToken);
            var approval = review.Approve(StorageAction.Compress, StorageScope.ExistingDescendants);
            using var owner = review.Transfer(adapter, approval);
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            var plan = StorageExecutionPlan.Build(owner, new(approval.Action, approval.Scope, approval.ReviewRevision) { Roots = review.Roots },
                pin => adapter.OpenReviewedRoot(pin, approval.Action, TestContext.Current.CancellationToken), () => { }, TestContext.Current.CancellationToken);
            Assert.Equal(2, plan.Roots.Length); Assert.Null(plan.Roots[0].Problem); Assert.Null(plan.Roots[1].Problem);
            Assert.Equal(1, plan.Roots[0].ParentPin); Assert.Null(plan.Roots[1].ParentPin);
            Assert.Equal(6UL, plan.Roots[1].Sources); Assert.Equal(7UL, plan.AncestorSources(0));
            Assert.True(owner.IsAlive); Assert.Equal("preserved bytes", File.ReadAllText(child));
            Assert.Equal((FileAttributes)0, File.GetAttributes(parent) & FileAttributes.Compressed);
            Assert.Equal((FileAttributes)0, File.GetAttributes(child) & FileAttributes.Compressed);
        }
        finally { Directory.Delete(root, true); }
    }
    private static (ReviewedStorageRoots Review, StorageReviewOwner Owner) Review(WindowsStorageTransforms adapter, string path, EntryKind kind, StorageAction action)
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(Path.GetDirectoryName(path)!, "unused-journal"));
        var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(path, kind)], action, TestContext.Current.CancellationToken);
        try { return (review, review.Transfer(adapter, review.Approve(action, StorageScope.ExistingDescendants))); }
        catch { review.Dispose(); throw; }
    }
    private static StorageMutationResult Apply(IStorageObjectLease item, StorageAction action)
    {
        var prepared = item.PrepareMutation(action, TestContext.Current.CancellationToken);
        Assert.Null(prepared.Ineligible);
        var result = item.ApplyAdmitted(action); // native protocol fixture; product job execution remains disabled
        Assert.True(result.Effect is StorageEffect.Changed or StorageEffect.AlreadySatisfied, result.Problem?.Message);
        Assert.Equal(result.Effect == StorageEffect.Changed, result.NativeCallAttempted);
        return result;
    }
    private static void Walk(IStorageTraversal traversal, IStorageObjectLease directory, StorageAction action, List<StorageMutationResult> results)
    {
        foreach (var child in traversal.Children(directory, TestContext.Current.CancellationToken))
        {
            using (child)
            {
                results.Add(Apply(child, action));
                if (child.Observe(TestContext.Current.CancellationToken).IsDirectory) Walk(traversal, child, action, results);
            }
        }
    }

    [Fact]
    public void Preorder_directory_mutation_and_recursion_reuse_the_mutation_handles_with_live_review_pins()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-held-traversal-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "folder")).FullName;
            string nested = Directory.CreateDirectory(Path.Join(folder, "nested")).FullName;
            File.WriteAllText(Path.Join(folder, "top"), "top payload"); File.WriteAllText(Path.Join(nested, "bottom"), "bottom payload");
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            var adapter = new WindowsStorageTransforms();
            var compress = Review(adapter, folder, EntryKind.Directory, StorageAction.Compress);
            var remove = Review(adapter, folder, EntryKind.Directory, StorageAction.RemoveCompression);
            using var compressReview = compress.Review; using var compressOwner = compress.Owner;
            using var removeReview = remove.Review; using var removeOwner = remove.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using (var traversal = adapter.OpenReviewedRoot(compressOwner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken))
            {
                Assert.Throws<InvalidOperationException>(() => traversal.Root.ApplyAdmitted(StorageAction.Compress));
                Assert.Equal(StorageEffect.Changed, Apply(traversal.Root, StorageAction.Compress).Effect);
                var results = new List<StorageMutationResult>(); Walk(traversal, traversal.Root, StorageAction.Compress, results);
                Assert.Equal(3, results.Count); Assert.All(results, r => Assert.Equal(StorageEffect.Changed, r.Effect));
            }
            using (var repeat = adapter.OpenReviewedRoot(compressOwner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken))
            {
                Assert.Equal(StorageEffect.AlreadySatisfied, Apply(repeat.Root, StorageAction.Compress).Effect);
                using var cursor = repeat.Children(repeat.Root, TestContext.Current.CancellationToken).GetEnumerator();
                Assert.True(cursor.MoveNext()); using var child = cursor.Current;
                // An already-satisfied directory acquired only listing rights and shares writes, with no duplicate enumeration open.
                using var writer = NtFile.OpenOrThrow(null, @"\??\" + folder, NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
                    NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Probe the no-op listing sharing");
            }
            using (var traversal = adapter.OpenReviewedRoot(removeOwner.Leases[0], StorageAction.RemoveCompression, TestContext.Current.CancellationToken))
            {
                Assert.Equal(StorageEffect.Changed, Apply(traversal.Root, StorageAction.RemoveCompression).Effect);
                var results = new List<StorageMutationResult>(); Walk(traversal, traversal.Root, StorageAction.RemoveCompression, results);
                Assert.Equal(3, results.Count); Assert.All(results, r => Assert.Equal(StorageEffect.Changed, r.Effect));
            }
            Assert.NotEqual((FileAttributes)0, File.GetAttributes(folder) & FileAttributes.ReadOnly);
            Assert.Equal("top payload", File.ReadAllText(Path.Join(folder, "top"))); Assert.Equal("bottom payload", File.ReadAllText(Path.Join(nested, "bottom")));
        }
        finally
        {
            string folder = Path.Join(root, "folder");
            if (Directory.Exists(folder)) File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Noop_ReadOnly_file_needs_no_write_handle_even_with_an_existing_writer()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string path = Path.GetTempFileName();
        try
        {
            using (var setup = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(WindowsCompression.InvokeAdmitted(setup, StorageAction.Compress).NativeSucceeded);
            using var writer = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            var adapter = new WindowsStorageTransforms(); var prepared = Review(adapter, path, EntryKind.File, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            var result = Apply(traversal.Root, StorageAction.Compress);
            Assert.Equal(StorageEffect.AlreadySatisfied, result.Effect); Assert.False(result.NativeCallAttempted);
            Assert.NotEqual((FileAttributes)0, File.GetAttributes(path) & FileAttributes.ReadOnly);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
    }

    [Fact]
    public void Renamed_review_path_replaced_by_another_object_never_chases_the_original_or_changes_the_replacement()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-stale-storage-").FullName;
        try
        {
            string path = Path.Join(root, "captured"), moved = Path.Join(root, "moved"); File.WriteAllText(path, "original");
            var adapter = new WindowsStorageTransforms(); var prepared = Review(adapter, path, EntryKind.File, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            File.Move(path, moved); File.WriteAllText(path, "replacement");
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            var error = Assert.Throws<StorageInspectionException>(() => adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken));
            Assert.Equal("review-stale", error.Problem.Code);
            Assert.Equal("original", File.ReadAllText(moved)); Assert.Equal("replacement", File.ReadAllText(path));
            Assert.Equal((FileAttributes)0, File.GetAttributes(path) & FileAttributes.Compressed); Assert.Equal((FileAttributes)0, File.GetAttributes(moved) & FileAttributes.Compressed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Conflicting_writer_refuses_before_call_then_fresh_preparation_can_change_the_same_held_object()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string path = Path.GetTempFileName();
        try
        {
            var adapter = new WindowsStorageTransforms(); var prepared = Review(adapter, path, EntryKind.File, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            using (var writer = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.Throws<IOException>(() => traversal.Root.PrepareMutation(StorageAction.Compress, TestContext.Current.CancellationToken));
                Assert.Throws<InvalidOperationException>(() => traversal.Root.ApplyAdmitted(StorageAction.Compress));
                Assert.Equal((FileAttributes)0, File.GetAttributes(path) & FileAttributes.Compressed);
            }
            Assert.Equal(StorageEffect.Changed, Apply(traversal.Root, StorageAction.Compress).Effect);
            Assert.Throws<InvalidOperationException>(() => traversal.Root.ApplyAdmitted(StorageAction.Compress));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public unsafe void Case_sensitive_siblings_keep_distinct_IDs_and_drive_extended_and_volume_paths_keep_one_physical_pin()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS case-sensitive fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-case-").FullName;
        try
        {
            using (var directory = NtFile.OpenOrThrow(null, @"\??\" + root, NtFile.WriteAttributes | NtFile.ReadAttributes | NtFile.Synchronize,
                NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Set the disposable directory case flag"))
            {
                uint mode = 1;
                Assert.True(SetInfo(directory, 23, &mode, 4), "Set case flag: " + Marshal.GetLastPInvokeError());
                foreach (string name in new[] { "a", "A" })
                {
                    int status = NtFile.TryOpen(directory, name, NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
                        NtFile.ShareAll, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, out var created, caseSensitive: true);
                    using (created) Assert.True(status >= 0, NtFile.Error(status, "Create case sibling").Message);
                }
            }
            var adapter = new WindowsStorageTransforms();
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(root, "unused-journal"));
            using var siblings = manager.PrepareStorageReview(new[] { "a", "A" }.Select(name => new ItemRef(Location.FileSystem(root), name, EntryKind.File)).ToArray(),
                StorageAction.Compress, TestContext.Current.CancellationToken);
            Assert.NotEqual(siblings.Roots[0].Observation.Identity, siblings.Roots[1].Observation.Identity);
            Assert.Equal(1, siblings.Roots[1].PinIndex);
            string plain = Path.Join(root, "a"), extended = @"\\?\" + plain;
            string volume = @"\\?\Volume{" + siblings.Roots[0].Observation.Identity.VolumeBinding + "}" + plain[2..];
            using var aliases = manager.PrepareStorageReview(new[] { plain, extended, volume }.Select(p => ItemRef.ForFileSystemPath(p, EntryKind.File)).ToArray(),
                StorageAction.Compress, TestContext.Current.CancellationToken);
            Assert.All(aliases.Roots, r => Assert.Equal(0, r.PinIndex)); Assert.Equal(3, aliases.Sources.Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Discovered_reparse_entry_is_policy_skipped_without_mutating_its_target()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-reparse-child-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "selected")).FullName;
            string outside = Path.Join(root, "outside"); File.WriteAllText(outside, "outside payload");
            File.CreateSymbolicLink(Path.Join(folder, "redirect"), outside);
            var adapter = new WindowsStorageTransforms(); var prepared = Review(adapter, folder, EntryKind.Directory, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            Apply(traversal.Root, StorageAction.Compress);
            foreach (var child in traversal.Children(traversal.Root, TestContext.Current.CancellationToken))
            {
                using (child)
                {
                    Assert.Equal("reparse-entry", child.PrepareMutation(StorageAction.Compress, TestContext.Current.CancellationToken).Ineligible?.Code);
                    var result = child.ApplyAdmitted(StorageAction.Compress);
                    Assert.Equal(StorageEffect.Skipped, result.Effect); Assert.False(result.NativeCallAttempted); Assert.False(result.Problem!.IsError);
                }
            }
            Assert.Equal("outside payload", File.ReadAllText(outside)); Assert.Equal((FileAttributes)0, File.GetAttributes(outside) & FileAttributes.Compressed);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Canceled_preparation_is_not_authority_for_a_call_and_admitted_call_has_no_cancellation_gap()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string path = Path.GetTempFileName();
        try
        {
            var adapter = new WindowsStorageTransforms(); var prepared = Review(adapter, path, EntryKind.File, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            using var stop = new CancellationTokenSource(); stop.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => traversal.Root.PrepareMutation(StorageAction.Compress, stop.Token));
            Assert.Throws<InvalidOperationException>(() => traversal.Root.ApplyAdmitted(StorageAction.Compress));
            Assert.Equal((FileAttributes)0, File.GetAttributes(path) & FileAttributes.Compressed);
            using var afterPreparation = new CancellationTokenSource();
            Assert.Null(traversal.Root.PrepareMutation(StorageAction.Compress, afterPreparation.Token).Ineligible);
            afterPreparation.Cancel();
            var result = traversal.Root.ApplyAdmitted(StorageAction.Compress);
            Assert.True(result.NativeCallAttempted); Assert.Equal(StorageEffect.Changed, result.Effect);
            Assert.Equal(StorageObservationState.Enabled, result.After?.Compression);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Depth_limit_includes_ancestors_above_the_selected_root_and_refuses_further_descent()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-depth-").FullName;
        try
        {
            string deepest = root;
            while (deepest.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 64)
                deepest = Directory.CreateDirectory(Path.Join(deepest, "d")).FullName;
            string over = Directory.CreateDirectory(Path.Join(deepest, "beyond")).FullName;
            var adapter = new WindowsStorageTransforms();
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), adapter, Path.Join(root, "unused-journal"));
            var limit = Assert.Throws<StorageInspectionException>(() => manager.PrepareStorageReview([ItemRef.ForFileSystemPath(over, EntryKind.Directory)],
                StorageAction.Compress, TestContext.Current.CancellationToken));
            Assert.Equal("depth-limit", limit.Problem.Code);
            var prepared = Review(adapter, deepest, EntryKind.Directory, StorageAction.Compress);
            using var review = prepared.Review; using var owner = prepared.Owner;
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            Apply(traversal.Root, StorageAction.Compress);
            var descent = Assert.Throws<StorageInspectionException>(() => traversal.Children(traversal.Root, TestContext.Current.CancellationToken).ToArray());
            Assert.Equal("depth-limit", descent.Problem.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool SetInfo(SafeFileHandle file, int infoClass, void* input, uint length);
}
