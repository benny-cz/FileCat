using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsStorageInspectionTests
{
    [Fact]
    public void Unqualified_hosts_refuse_storage_before_opening_any_path_and_do_not_offer_private_history()
    {
        var adapter = new WindowsStorageTransforms();
        Assert.Equal(!WindowsStorageTransforms.IsQualifiedHost,
            adapter.Unavailable(StorageAction.Compress)?.Code == "target-unqualified");
        Assert.Equal("efs-unqualified", adapter.Unavailable(StorageAction.EncryptEfs)?.Code);
        if (WindowsStorageTransforms.IsQualifiedHost) return;
        Assert.False(WindowsStorageReviewEvidence.IsSupportedHost);
        Assert.Throws<UnauthorizedAccessException>(() => WindowsStorageReviewEvidence.Backend.OpenCursor(
            Path.Join(Path.GetTempPath(), "filecat-must-not-create-" + Guid.NewGuid().ToString("N")), false, TestContext.Current.CancellationToken));
    }

    private static JobManager Manager(string root, Func<string>? windows = null) => new(new PortableFileOperations(), new ProviderRegistry(),
        new WindowsStorageTransforms(windows), Path.Join(root, "journal"));
    private static ReviewedStorageRoots Inspect(JobManager manager, string path, StorageAction action, EntryKind kind = EntryKind.File) =>
        manager.PrepareStorageReview([ItemRef.ForFileSystemPath(path, kind)], action, TestContext.Current.CancellationToken);

    [Fact]
    public void Ordinary_objects_have_bound_volume_IDs_and_ReadOnly_files_only_refuse_when_a_change_is_needed()
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-inspection-").FullName;
        try
        {
            string path = Path.Join(root, "ordinary 名前.txt"); File.WriteAllText(path, "payload unchanged");
            var manager = Manager(root);
            using var ordinary = Inspect(manager, path, StorageAction.Compress);
            var fact = Assert.Single(ordinary.Roots).Observation;
            Assert.Null(fact.Ineligible); Assert.Equal(StorageObservationState.Disabled, fact.Compression);
            Assert.Equal(StorageObservationState.Disabled, fact.Efs); Assert.False(fact.IsDirectory);
            Assert.True(Guid.TryParse(fact.Identity.VolumeBinding, out _)); Assert.Equal(32, fact.Identity.ObjectId.Length);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            using var refused = Inspect(manager, path, StorageAction.Compress);
            Assert.Equal("read-only-file", Assert.Single(refused.Roots).Observation.Ineligible?.Code);
            using var noop = Inspect(manager, path, StorageAction.RemoveCompression);
            Assert.Null(Assert.Single(noop.Roots).Observation.Ineligible);
            using var folder = Inspect(manager, root, StorageAction.Compress, EntryKind.Directory);
            Assert.Null(Assert.Single(folder.Roots).Observation.Ineligible);
            Assert.Equal("payload unchanged", File.ReadAllText(path));
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
        finally
        {
            foreach (string path in Directory.EnumerateFiles(root)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Protected_tree_uses_live_ancestry_while_a_lookalike_name_and_System_ancestor_do_not_create_a_denylist()
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-policy-").FullName;
        try
        {
            string active = Directory.CreateDirectory(Path.Join(root, "active-system")).FullName;
            string protectedFile = Path.Join(active, "protected"); File.WriteAllText(protectedFile, "protected");
            string lookalike = Directory.CreateDirectory(Path.Join(root, "Windows")).FullName;
            string allowedFile = Path.Join(lookalike, "allowed"); File.WriteAllText(allowedFile, "allowed");
            var manager = Manager(root, () => active);
            using var denied = Inspect(manager, protectedFile, StorageAction.Compress);
            Assert.Equal("windows-tree", denied.Roots[0].Observation.Ineligible?.Code);
            File.SetAttributes(lookalike, File.GetAttributes(lookalike) | FileAttributes.System);
            using var allowed = Inspect(manager, allowedFile, StorageAction.Compress);
            Assert.Null(allowed.Roots[0].Observation.Ineligible);
            using var system = Inspect(manager, lookalike, StorageAction.Compress, EntryKind.Directory);
            Assert.Equal("system-entry", system.Roots[0].Observation.Ineligible?.Code);
            File.SetAttributes(lookalike, File.GetAttributes(lookalike) & ~FileAttributes.System);
            var unknown = Manager(root, () => Path.Join(root, "missing-active-directory"));
            Assert.Equal("protected-tree-unknown", Assert.Throws<StorageInspectionException>(() => Inspect(unknown, allowedFile, StorageAction.Compress)).Problem.Code);
            string redirectedAnchor = Path.Join(root, "redirected-active"); Directory.CreateSymbolicLink(redirectedAnchor, active);
            var redirected = Manager(root, () => redirectedAnchor);
            Assert.Equal("protected-tree-unknown", Assert.Throws<StorageInspectionException>(() => Inspect(redirected, allowedFile, StorageAction.Compress)).Problem.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public unsafe void Reparse_ancestors_and_leaves_sparse_and_multiple_names_are_refused_without_following_targets()
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-exclusions-").FullName;
        try
        {
            string real = Directory.CreateDirectory(Path.Join(root, "real")).FullName;
            string path = Path.Join(real, "payload"); File.WriteAllText(path, "kept");
            string link = Path.Join(root, "link"); File.CreateSymbolicLink(link, path);
            string junction = Path.Join(root, "redirect"); Directory.CreateSymbolicLink(junction, real);
            var manager = Manager(root);
            using var excluded = Inspect(manager, link, StorageAction.Compress);
            Assert.Equal("reparse-entry", excluded.Roots[0].Observation.Ineligible?.Code);
            Assert.False(excluded.Roots[0].Observation.Ineligible!.IsError);
            Assert.Equal("ancestor-excluded", Assert.Throws<StorageInspectionException>(() => Inspect(manager, Path.Join(junction, "payload"), StorageAction.Compress)).Problem.Code);
            string alias = Path.Join(root, "alias");
            Assert.True(CreateHardLink(alias, path, 0), new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            using var multiple = Inspect(manager, path, StorageAction.Compress);
            Assert.Equal("multiple-names", multiple.Roots[0].Observation.Ineligible?.Code);
            Assert.False(multiple.Roots[0].Observation.Ineligible!.IsError);
            File.Delete(alias);
            using (var data = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(Control(data, 0x900C4, null, 0, null, 0, out _, 0));
            using var sparse = Inspect(manager, path, StorageAction.Compress);
            Assert.Equal("sparse-entry", sparse.Roots[0].Observation.Ineligible?.Code);
            Assert.Equal("kept", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Bounded_cursor_reuses_a_held_directory_across_batches_and_returns_native_child_IDs()
    {
        Assert.SkipWhen(!WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-cursor-").FullName;
        try
        {
            for (int i = 0; i < 1100; i++) File.WriteAllText(Path.Join(root, $"item-{i:D4}"), "x");
            using var directory = NtFile.OpenOrThrow(null, @"\??\" + root, NtFile.ListDirectory | NtFile.ReadAttributes | NtFile.Synchronize,
                NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Open disposable cursor directory");
            using var cursor = new WindowsStorageDirectoryCursor(directory);
            var names = new HashSet<string>(StringComparer.Ordinal); int batches = 0;
            while (!cursor.IsComplete)
            {
                var entries = cursor.ReadBatch(TestContext.Current.CancellationToken); batches++;
                foreach (var entry in entries)
                {
                    Assert.True(names.Add(entry.Name));
                    using var file = NtFile.OpenOrThrow(directory, entry.Name, NtFile.ReadAttributes | NtFile.Synchronize,
                        NtFile.ShareAll, NtFile.Open, NtFile.OpenReparsePoint, "Verify the cursor's held-relative child");
                    Assert.Equal(ProtectedHexFile.Identity(file).FileId, entry.ObjectId);
                }
            }
            Assert.Equal(1100, names.Count); Assert.True(batches >= 3);
            Assert.Empty(cursor.ReadBatch(TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CreateHardLink(string name, string existing, nint security);
    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool Control(SafeFileHandle file, uint code, void* input, uint inputBytes, void* output, uint outputBytes, out uint returned, nint overlapped);
}
