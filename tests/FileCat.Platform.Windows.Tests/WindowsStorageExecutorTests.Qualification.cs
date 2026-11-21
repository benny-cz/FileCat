using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// N-C2 and N-ID.C native fixtures for the compression release: aliases, mounts, ancestor and in-place reparse
/// adversaries, long and case-distinct names, denial, sparse read-only files, WOF and small allocation units.
/// Every fixture is disposable and owned by the test; a refusal must leave the reviewed and the competing objects intact.
/// </summary>
public sealed partial class WindowsStorageExecutorTests
{
    private static ITestOutputHelper? Output => TestContext.Current.TestOutputHelper;

    [Fact]
    public void Drive_and_volume_GUID_roots_are_refused_at_capture_before_any_native_open()
    {
        RequireNormal(WindowsStorageAbiTests.QualifyNativeArm64Jobs);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-volume-root-").FullName;
        try
        {
            var manager = Manager(temp, qualifyNativeArm64: WindowsStorageAbiTests.QualifyNativeArm64Jobs);
            string drive = Path.GetPathRoot(temp)!;
            string volume = @"\\?\Volume{" + (WindowsStorageSyncRoots.VolumeOf(drive) ?? throw new InvalidOperationException("No volume GUID.")) + @"}\";
            foreach (string root in new[] { drive, @"\\?\" + drive, volume })
            {
                var item = ItemRef.ForFileSystemPath(root, EntryKind.Directory);
                Assert.Equal("source-path", StoragePaths.SourceProblem(item)?.Code);
                foreach (var action in new[] { StorageAction.Compress, StorageAction.RemoveCompression })
                    Assert.Throws<ArgumentException>(() => manager.PrepareStorageReview([item], action, Stop));
            }
            Assert.Empty(manager.Jobs);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Join(temp, "journals")));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void A_drive_letter_alias_of_a_folder_is_refused_as_a_volume_binding_before_a_job()
    {
        RequireNormal(WindowsStorageAbiTests.QualifyNativeArm64Jobs);
        char? free = null;
        for (char letter = 'Y'; letter >= 'M' && free is null; letter--)
            if (!Directory.Exists(letter + @":\") && !DriveInfo.GetDrives().Any(d => d.Name[0] == letter)) free = letter;
        Assert.SkipWhen(free is null, "No free drive letter for the alias.");
        string temp = Directory.CreateTempSubdirectory("filecat-storage-subst-").FullName;
        string device = free + ":";
        bool defined = false;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "aliased")).FullName;
            string file = Path.Join(folder, "file.bin"); File.WriteAllText(file, "alias payload");
            Assert.True(QDefineDosDevice(0, device, folder), "DefineDosDevice: " + Marshal.GetLastPInvokeError());
            defined = true;
            string aliased = device + @"\file.bin";
            Assert.Equal("alias payload", File.ReadAllText(aliased));
            var manager = Manager(temp, qualifyNativeArm64: WindowsStorageAbiTests.QualifyNativeArm64Jobs);
            foreach (var action in new[] { StorageAction.Compress, StorageAction.RemoveCompression })
            {
                var refused = Assert.Throws<StorageInspectionException>(() =>
                    manager.PrepareStorageReview([ItemRef.ForFileSystemPath(aliased, EntryKind.File)], action, Stop));
                Assert.Equal("volume-binding", refused.Problem.Code);
            }
            Assert.Empty(manager.Jobs); Assert.False(Compressed(file)); Assert.Equal("alias payload", File.ReadAllText(file));
        }
        finally
        {
            if (defined) QDefineDosDevice(2 /* DDD_REMOVE_DEFINITION */ | 4 /* DDD_EXACT_MATCH_ON_REMOVE */, device, Path.Join(temp, "aliased"));
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Junctions_are_refused_as_targets_and_ancestors_and_skipped_without_following_as_discovered_children()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-junction-").FullName;
        string folder = Path.Join(temp, "folder"), junction = Path.Join(folder, "junction");
        try
        {
            string outside = Directory.CreateDirectory(Path.Join(temp, "outside")).FullName;
            string sentinel = Path.Join(outside, "sentinel"); File.WriteAllText(sentinel, "outside scope");
            Directory.CreateDirectory(folder);
            string ordinary = Path.Join(folder, "ordinary"); File.WriteAllText(ordinary, "ordinary bytes");
            MakeJunction(junction, outside);
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            using (var selected = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(junction, EntryKind.Directory)], StorageAction.Compress, Stop))
                Assert.Equal("reparse-entry", Assert.Single(selected.Roots).Observation.Ineligible?.Code);
            var through = Assert.Throws<StorageInspectionException>(() => manager.PrepareStorageReview(
                [ItemRef.ForFileSystemPath(Path.Join(junction, "sentinel"), EntryKind.File)], StorageAction.Compress, Stop));
            Assert.Equal("ancestor-excluded", through.Problem.Code);

            var job = Run(manager, StorageScope.ExistingDescendants, ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal((2L, 1L), (job.StorageResult!.Counts.Changed, job.StorageResult.Counts.Skipped));
            Assert.True(Compressed(folder)); Assert.True(Compressed(ordinary));
            Assert.False(Compressed(outside)); Assert.False(Compressed(sentinel)); Assert.Equal("outside scope", File.ReadAllText(sentinel));
            Assert.True((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0);
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// FILECAT_STORAGE_NATIVE_MOUNT_FOLDER names an empty folder where the runner mounted an owned fixed NTFS volume that
    /// holds "mounted.bin". The mount point is never crossed; the same file is admitted through its own volume GUID path.
    /// </summary>
    [Fact]
    public void Volume_mount_points_are_never_crossed_and_the_mounted_volume_is_changed_through_its_own_path()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string? mount = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_MOUNT_FOLDER");
        if (string.IsNullOrWhiteSpace(mount)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_MOUNT_FOLDER to an owned volume mount point.");
        Assert.True((File.GetAttributes(mount) & FileAttributes.ReparsePoint) != 0, "The fixture folder must be a mount point.");
        string mountedGuid = WindowsStorageSyncRoots.VolumeOf(mount.TrimEnd('\\') + @"\")!;
        string outerGuid = WindowsStorageSyncRoots.VolumeOf(Path.GetPathRoot(mount)!)!;
        Assert.NotEqual(outerGuid, mountedGuid);
        string direct = @"\\?\Volume{" + mountedGuid + @"}\mounted.bin";
        byte[] payload = File.ReadAllBytes(direct);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-mount-").FullName;
        try
        {
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            using (var selected = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(mount, EntryKind.Directory)], StorageAction.Compress, Stop))
                Assert.Equal("reparse-entry", Assert.Single(selected.Roots).Observation.Ineligible?.Code);
            var crossed = Assert.Throws<StorageInspectionException>(() => manager.PrepareStorageReview(
                [ItemRef.ForFileSystemPath(Path.Join(mount, "mounted.bin"), EntryKind.File)], StorageAction.Compress, Stop));
            Assert.Equal("ancestor-excluded", crossed.Problem.Code);
            using (var own = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(direct, EntryKind.File)], StorageAction.Compress, Stop))
            {
                var observation = Assert.Single(own.Roots).Observation;
                Assert.Null(observation.Ineligible);
                Assert.Equal(mountedGuid, observation.Identity.VolumeBinding, ignoreCase: true);
            }
            var compressed = RunAction(manager, StorageAction.Compress, StorageScope.SelectedItems, ItemRef.ForFileSystemPath(direct, EntryKind.File));
            Assert.Equal((JobState.Completed, 1L), (compressed.State, compressed.StorageResult!.Counts.Changed));
            Assert.True(Compressed(direct)); Assert.False(Compressed(mount)); Assert.Equal(payload, File.ReadAllBytes(direct));
            var removed = RunAction(manager, StorageAction.RemoveCompression, StorageScope.SelectedItems, ItemRef.ForFileSystemPath(direct, EntryKind.File));
            Assert.Equal((JobState.Completed, 1L), (removed.State, removed.StorageResult!.Counts.Changed));
            Assert.False(Compressed(direct)); Assert.Equal(payload, File.ReadAllBytes(direct));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void An_ancestor_replaced_after_review_never_redirects_the_job_to_the_replacement()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-ancestor-swap-").FullName;
        try
        {
            string parent = Directory.CreateDirectory(Path.Join(temp, "parent")).FullName;
            string reviewed = Path.Join(parent, "file"); File.WriteAllText(reviewed, "reviewed object");
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            var source = ItemRef.ForFileSystemPath(reviewed, EntryKind.File);
            using var review = manager.PrepareStorageReview([source], StorageAction.Compress, Stop);
            Assert.Null(Assert.Single(review.Roots).Observation.Ineligible);
            string moved = Path.Join(temp, "parent-moved");
            bool swapped;
            try { Directory.Move(parent, moved); swapped = true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { swapped = false; }
            if (swapped) { Directory.CreateDirectory(parent); File.WriteAllText(reviewed, "replacement object"); }
            var job = Submit(manager, review, StorageAction.Compress, StorageScope.SelectedItems);
            var result = job.StorageResult!;
            Output?.WriteLine($"Ancestor rename with the review pin held: {(swapped ? "allowed" : "blocked")}; job {job.State}, changed " +
                $"{result.Counts.Changed}, failed {result.Counts.Failed}; reasons {string.Join(',', result.Reasons.Select(r => r.Problem.Code))}");
            string original = swapped ? Path.Join(moved, "file") : reviewed;
            if (swapped) { Assert.False(Compressed(reviewed)); Assert.Equal("replacement object", File.ReadAllText(reviewed)); }
            else Assert.Equal((JobState.Completed, 1L), (job.State, result.Counts.Changed));
            Assert.Equal("reviewed object", File.ReadAllText(original));
            Assert.Equal(result.Counts.Changed == 1, Compressed(original));
            Assert.Equal(1L, result.Counts.Changed + result.Counts.Failed + result.Counts.Skipped);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Renaming_an_ancestor_during_a_recursive_job_keeps_traversal_on_the_held_objects()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-ancestor-held-").FullName;
        string above = Path.Join(temp, "above"), moved = Path.Join(temp, "above-moved");
        string? competitor = null; int calls = 0;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(above, "folder")).FullName;
            for (int i = 0; i < 3; i++) File.WriteAllText(Path.Join(folder, "child" + i), "held child " + i);
            var adapter = new WindowsStorageTransforms(qualifyNativeArm64: arm64, compressionSetReturnedForQualification: succeeded =>
            {
                if (Interlocked.Increment(ref calls) != 1) return;
                try
                {
                    Directory.Move(above, moved);
                    Directory.CreateDirectory(Path.Join(above, "folder"));
                    File.WriteAllText(Path.Join(above, "folder", "child0"), "decoy at the old path");
                    competitor = "renamed";
                }
                catch (IOException ex) { competitor = "blocked: " + ex.HResult.ToString("X8"); }
                catch (UnauthorizedAccessException ex) { competitor = "denied: " + ex.HResult.ToString("X8"); }
            });
            var job = Run(Manager(temp, adapter, arm64), StorageScope.ExistingDescendants, ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            var result = job.StorageResult!;
            Output?.WriteLine("Ancestor rename while held: " + competitor + "; job " + job.State + " changed " + result.Counts.Changed);
            Assert.NotNull(competitor);
            string held = competitor == "renamed" ? Path.Join(moved, "folder") : folder;
            Assert.Equal((JobState.Completed, 4L), (job.State, result.Counts.Changed));
            Assert.True(Compressed(held));
            for (int i = 0; i < 3; i++)
            {
                Assert.True(Compressed(Path.Join(held, "child" + i)));
                Assert.Equal("held child " + i, File.ReadAllText(Path.Join(held, "child" + i)));
            }
            if (competitor == "renamed")
            {
                string decoy = Path.Join(above, "folder", "child0");
                Assert.False(Compressed(decoy)); Assert.Equal("decoy at the old path", File.ReadAllText(decoy));
            }
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public unsafe void An_attribute_only_junction_on_an_empty_reviewed_directory_is_refused_without_following_it()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-inplace-junction-").FullName;
        string reviewed = Path.Join(temp, "reviewed");
        try
        {
            Directory.CreateDirectory(reviewed);
            string other = Directory.CreateDirectory(Path.Join(temp, "unselected")).FullName;
            string sentinel = Path.Join(other, "sentinel"); File.WriteAllText(sentinel, "unselected bytes");
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            var source = ItemRef.ForFileSystemPath(reviewed, EntryKind.Directory);
            using var review = manager.PrepareStorageReview([source], StorageAction.Compress, Stop);
            var identity = Assert.Single(review.Roots).Observation.Identity;
            SetJunction(reviewed, other); // same object, now a reparse point
            var job = Submit(manager, review, StorageAction.Compress, StorageScope.ExistingDescendants);
            var result = job.StorageResult!;
            Output?.WriteLine($"In-place junction outcome: {job.State}; reasons {string.Join(',', result.Reasons.Select(r => r.Problem.Code))}");
            Assert.Equal(0L, result.Counts.Changed);
            Assert.False(Compressed(other)); Assert.False(Compressed(sentinel)); Assert.Equal("unselected bytes", File.ReadAllText(sentinel));
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.False(string.IsNullOrEmpty(identity.ObjectId));
        }
        finally
        {
            if (Directory.Exists(reviewed)) Directory.Delete(reviewed);
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Paths_longer_than_MAX_PATH_run_through_a_reviewed_recursive_job()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-long-").FullName;
        try
        {
            string deep = temp;
            for (int i = 0; i < 4; i++) deep = Path.Join(deep, new string((char)('a' + i), 90) + " ü");
            Directory.CreateDirectory(deep);
            string file = Path.Join(deep, "long file " + new string('z', 40) + ".bin");
            File.WriteAllText(file, "long path payload");
            Assert.True(file.Length > 400, "The fixture path must exceed MAX_PATH by a wide margin.");
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            var top = Path.Join(temp, new string('a', 90) + " ü");
            var job = Run(manager, StorageScope.ExistingDescendants, ItemRef.ForFileSystemPath(top, EntryKind.Directory));
            Assert.Equal((JobState.Completed, 5L), (job.State, job.StorageResult!.Counts.Changed));
            Assert.True(Compressed(file)); Assert.Equal("long path payload", File.ReadAllText(file));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public unsafe void Only_the_selected_case_sibling_changes_in_a_case_sensitive_directory()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-case-job-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "case")).FullName;
            using (var directory = NtFile.OpenOrThrow(null, @"\??\" + folder, NtFile.WriteAttributes | NtFile.ReadAttributes | NtFile.Synchronize,
                NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Set the disposable directory case flag"))
            {
                uint mode = 1;
                Assert.True(QSetInfo(directory, 23 /* FileCaseSensitiveInfo */, &mode, 4), "Set case flag: " + Marshal.GetLastPInvokeError());
                foreach (string name in new[] { "a", "A" })
                {
                    int status = NtFile.TryOpen(directory, name, NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
                        NtFile.ShareAll, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, out var created, caseSensitive: true);
                    using (created)
                    {
                        Assert.True(status >= 0, NtFile.Error(status, "Create case sibling").Message);
                        byte[] bytes = Encoding.ASCII.GetBytes("sibling " + name);
                        RandomAccess.Write(created, bytes, 0);
                    }
                }
            }
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            var job = Run(manager, StorageScope.SelectedItems, new ItemRef(Location.FileSystem(folder), "a", EntryKind.File));
            Assert.Equal((JobState.Completed, 1L), (job.State, job.StorageResult!.Counts.Changed));
            Assert.Equal((true, false), (CaseCompressed(folder, "a"), CaseCompressed(folder, "A")));
        }
        finally { Directory.Delete(temp, true); }

        static bool CaseCompressed(string folder, string name)
        {
            using var directory = NtFile.OpenOrThrow(null, @"\??\" + folder, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open,
                NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Open the case fixture");
            int status = NtFile.TryOpen(directory, name, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open,
                NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, out var file, caseSensitive: true);
            using (file)
            {
                Assert.True(status >= 0, NtFile.Error(status, "Open case sibling").Message);
                return (NtFile.GetBasic(file).FileAttributes & (uint)FileAttributes.Compressed) != 0;
            }
        }
    }

    [Fact]
    public void Compressing_a_file_with_1025_named_streams_needs_no_stream_inventory_and_keeps_every_stream()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-many-streams-").FullName;
        try
        {
            string file = Path.Join(temp, "streams.bin"); File.WriteAllText(file, "main data");
            for (int i = 0; i < 1025; i++) File.WriteAllText(file + ":s" + i, "stream " + i);
            var job = Run(Manager(temp, qualifyNativeArm64: arm64), StorageScope.SelectedItems, ItemRef.ForFileSystemPath(file, EntryKind.File));
            Assert.Equal((JobState.Completed, 1L), (job.State, job.StorageResult!.Counts.Changed));
            Assert.True(Compressed(file)); Assert.Equal("main data", File.ReadAllText(file));
            foreach (int i in new[] { 0, 511, 1024 }) Assert.Equal("stream " + i, File.ReadAllText(file + ":s" + i));
        }
        finally { Directory.Delete(temp, true); }
    }

    /// <summary>FILECAT_STORAGE_NATIVE_WOF_TARGET names an owned WOF-backed file (for example from compact /c /exe:xpress4k).</summary>
    [Fact]
    public void Wof_backed_files_are_refused_for_compression_changes_without_touching_their_backing()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string? target = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_WOF_TARGET");
        if (string.IsNullOrWhiteSpace(target)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_WOF_TARGET to an owned WOF-backed file.");
        byte[] payload = File.ReadAllBytes(target);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-wof-").FullName;
        try
        {
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            foreach (var action in new[] { StorageAction.Compress, StorageAction.RemoveCompression })
            {
                using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(target, EntryKind.File)], action, Stop);
                string? code = Assert.Single(review.Roots).Observation.Ineligible?.Code;
                Assert.True(code is "reparse-entry" or "external-backing", "WOF refusal: " + code);
            }
            Assert.Empty(manager.Jobs);
            using (var handle = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(WindowsStorageBacking.Query(handle, Stop).External);
            Assert.Equal(payload, File.ReadAllBytes(target));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public unsafe void A_sparse_ReadOnly_file_refuses_compression_and_its_clear_removal_is_a_no_op()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-sparse-readonly-").FullName;
        string file = Path.Join(temp, "sparse.bin");
        try
        {
            using (var handle = File.OpenHandle(file, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                Assert.True(QControl(handle, 0x000900C4 /* FSCTL_SET_SPARSE */, null, 0, null, 0, out _, 0), "Set sparse: " + Marshal.GetLastPInvokeError());
                RandomAccess.SetLength(handle, 64L << 20);
                RandomAccess.Write(handle, Encoding.ASCII.GetBytes("head"), 0);
                RandomAccess.Write(handle, Encoding.ASCII.GetBytes("tail"), (64L << 20) - 4);
            }
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            using (var compress = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(file, EntryKind.File)], StorageAction.Compress, Stop))
                Assert.Equal("read-only-file", Assert.Single(compress.Roots).Observation.Ineligible?.Code);
            var removal = RunAction(manager, StorageAction.RemoveCompression, StorageScope.SelectedItems, ItemRef.ForFileSystemPath(file, EntryKind.File));
            Assert.Equal((JobState.Completed, 1L, 0L), (removal.State, removal.StorageResult!.Counts.AlreadySatisfied, removal.StorageResult.Counts.Changed));
            var attributes = File.GetAttributes(file);
            Assert.Equal((true, true, false), ((attributes & FileAttributes.ReadOnly) != 0, (attributes & FileAttributes.SparseFile) != 0,
                (attributes & FileAttributes.Compressed) != 0));
            using var read = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] head = new byte[4], tail = new byte[4];
            RandomAccess.Read(read, head, 0); RandomAccess.Read(read, tail, (64L << 20) - 4);
            Assert.Equal(("head", "tail"), (Encoding.ASCII.GetString(head), Encoding.ASCII.GetString(tail)));
        }
        finally
        {
            if (File.Exists(file)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void A_folder_without_listing_access_leaves_its_scope_incomplete_and_a_denied_selected_folder_is_refused()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-folder-denial-").FullName;
        var restore = new List<(DirectoryInfo Folder, DirectorySecurity Original)>();
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "folder")).FullName;
            string allowed = Path.Join(folder, "allowed"); File.WriteAllText(allowed, "allowed bytes");
            string unlisted = Directory.CreateDirectory(Path.Join(folder, "unlisted")).FullName;
            string hidden = Path.Join(unlisted, "inside"); File.WriteAllText(hidden, "inside bytes");
            string locked = Directory.CreateDirectory(Path.Join(temp, "locked")).FullName;
            Deny(unlisted, FileSystemRights.ListDirectory);
            Deny(locked, FileSystemRights.FullControl);
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            var refused = Assert.Throws<StorageInspectionException>(() => manager.PrepareStorageReview(
                [ItemRef.ForFileSystemPath(locked, EntryKind.Directory)], StorageAction.Compress, Stop));
            Assert.Equal(StorageReasonKind.PermissionOrPolicy, refused.Problem.Kind);
            var job = Run(manager, StorageScope.ExistingDescendants, ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            var result = job.StorageResult!;
            Output?.WriteLine($"Unlisted folder outcome: {job.State}; changed {result.Counts.Changed}, failed {result.Counts.Failed}, " +
                $"discovery {result.Discovery}; reasons {string.Join(',', result.Reasons.Select(r => r.Problem.Code))}");
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.NotEqual(StorageCompleteness.Complete, result.Discovery);
            Assert.True(Compressed(folder)); Assert.True(Compressed(allowed));
            foreach (var (info, original) in restore) info.SetAccessControl(original);
            restore.Clear();
            Assert.False(Compressed(hidden)); Assert.Equal("inside bytes", File.ReadAllText(hidden));
            Assert.False(Compressed(locked));
        }
        finally
        {
            foreach (var (info, original) in restore) info.SetAccessControl(original);
            Directory.Delete(temp, true);
        }

        void Deny(string path, FileSystemRights rights)
        {
            var info = new DirectoryInfo(path);
            var original = info.GetAccessControl();
            var security = info.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, rights, AccessControlType.Deny));
            info.SetAccessControl(security);
            var copy = new DirectorySecurity();
            copy.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            restore.Add((info, copy));
        }
    }

    /// <summary>
    /// FILECAT_STORAGE_NATIVE_SMALL_UNIT_ROOTS lists owned folders, separated by semicolons, on fixed NTFS volumes with
    /// 512-, 1,024- and 2,048-byte allocation units. Main data changes; the named stream keeps its bytes and its own state.
    /// </summary>
    [Fact]
    public void Allocation_units_below_4096_bytes_round_trip_main_data_compression()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string? roots = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_SMALL_UNIT_ROOTS");
        if (string.IsNullOrWhiteSpace(roots)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_SMALL_UNIT_ROOTS to owned folders on small-cluster NTFS volumes.");
        string temp = Directory.CreateTempSubdirectory("filecat-storage-small-units-").FullName;
        var units = new List<uint>();
        try
        {
            var manager = Manager(temp, qualifyNativeArm64: arm64);
            foreach (string root in roots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Assert.True(QFreeSpace(Path.GetPathRoot(root)!, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _));
                uint unit = sectorsPerCluster * bytesPerSector;
                Assert.True(unit < 4096, "The fixture volume must use an allocation unit below 4 KiB.");
                units.Add(unit);
                string folder = Directory.CreateDirectory(Path.Join(root, "units-" + Guid.NewGuid().ToString("N"))).FullName;
                string file = Path.Join(folder, "payload.bin");
                byte[] main = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 13)).ToArray();
                File.WriteAllBytes(file, main); File.WriteAllText(file + ":aux", "named stream " + unit);
                var source = ItemRef.ForFileSystemPath(file, EntryKind.File);
                var compressed = RunAction(manager, StorageAction.Compress, StorageScope.SelectedItems, source);
                Assert.Equal((JobState.Completed, 1L), (compressed.State, compressed.StorageResult!.Counts.Changed));
                Assert.True(Compressed(file)); Assert.Equal(main, File.ReadAllBytes(file));
                Assert.Equal("named stream " + unit, File.ReadAllText(file + ":aux"));
                var removed = RunAction(manager, StorageAction.RemoveCompression, StorageScope.SelectedItems, source);
                Assert.Equal((JobState.Completed, 1L), (removed.State, removed.StorageResult!.Counts.Changed));
                Assert.False(Compressed(file)); Assert.Equal(main, File.ReadAllBytes(file));
                Directory.Delete(folder, recursive: true);
            }
            Output?.WriteLine("Allocation units exercised: " + string.Join(", ", units));
            Assert.NotEmpty(units);
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Protected_tree_descendants_are_skipped_in_a_recursive_job_while_name_lookalikes_stay_ordinary()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-protected-job-").FullName;
        string? otherVolumeAnchor = null;
        try
        {
            string scope = Directory.CreateDirectory(Path.Join(temp, "scope")).FullName;
            string active = Directory.CreateDirectory(Path.Join(scope, "active-windows")).FullName;
            string nested = Directory.CreateDirectory(Path.Join(active, "System32")).FullName;
            string inside = Path.Join(nested, "inside.dll"); File.WriteAllText(inside, "protected bytes");
            var lookalikes = new[] { "Windows", "Program Files", "ProgramData" }.Select(name =>
            {
                string folder = Directory.CreateDirectory(Path.Join(scope, name)).FullName;
                string file = Path.Join(folder, "ordinary.bin"); File.WriteAllText(file, name + " bytes");
                return (folder, file);
            }).ToArray();
            var adapter = new WindowsStorageTransforms(windowsDirectory: () => active, qualifyNativeArm64: arm64);
            var manager = Manager(temp, adapter, arm64);
            using (var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(inside, EntryKind.File)], StorageAction.Compress, Stop))
            {
                var refusal = Assert.Single(review.Roots).Observation.Ineligible;
                Assert.Equal(("windows-tree", StorageReasonKind.FileCatBoundary, false), (refusal?.Code, refusal!.Kind, refusal.IsError));
            }
            var job = Run(manager, StorageScope.ExistingDescendants, ItemRef.ForFileSystemPath(scope, EntryKind.Directory));
            var result = job.StorageResult!;
            Output?.WriteLine($"Protected tree job: {job.State}; changed {result.Counts.Changed}, skipped {result.Counts.Skipped}; " +
                $"reasons {string.Join(',', result.Reasons.Select(r => r.Problem.Code + "×" + r.Count))}");
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal((7L, 1L), (result.Counts.Changed, result.Counts.Skipped));
            Assert.Contains(result.Reasons, r => r.Problem.Code == "windows-tree");
            Assert.True(Compressed(scope));
            Assert.All(lookalikes, l => { Assert.True(Compressed(l.folder)); Assert.True(Compressed(l.file)); });
            Assert.Equal((false, false, false), (Compressed(active), Compressed(nested), Compressed(inside)));
            Assert.Equal("protected bytes", File.ReadAllText(inside));

            // An anchor on another volume protects only its own tree there.
            string? owned = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_NOFILTER_ROOT");
            if (!string.IsNullOrWhiteSpace(owned))
            {
                otherVolumeAnchor = Directory.CreateDirectory(Path.Join(owned, "anchor-" + Guid.NewGuid().ToString("N"))).FullName;
                string remote = Path.Join(otherVolumeAnchor, "inside.bin"); File.WriteAllText(remote, "other volume");
                var elsewhere = Manager(temp, new WindowsStorageTransforms(windowsDirectory: () => otherVolumeAnchor, qualifyNativeArm64: arm64), arm64);
                using (var there = elsewhere.PrepareStorageReview([ItemRef.ForFileSystemPath(remote, EntryKind.File)], StorageAction.Compress, Stop))
                    Assert.Equal("windows-tree", Assert.Single(there.Roots).Observation.Ineligible?.Code);
                using (var here = elsewhere.PrepareStorageReview([ItemRef.ForFileSystemPath(inside, EntryKind.File)], StorageAction.Compress, Stop))
                    Assert.Null(Assert.Single(here.Roots).Observation.Ineligible);
            }
        }
        finally
        {
            if (otherVolumeAnchor is not null) Directory.Delete(otherVolumeAnchor, recursive: true);
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Stop_after_a_folder_default_changes_records_that_call_and_leaves_its_contents_unprocessed()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs; RequireNormal(arm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-stop-before-descent-").FullName;
        using var returned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "folder")).FullName;
            var children = new[] { "first", "second" }.Select(n => Path.Join(folder, n)).ToArray();
            foreach (string child in children) File.WriteAllText(child, "unprocessed " + child.Length);
            var adapter = new WindowsStorageTransforms(qualifyNativeArm64: arm64, compressionSetReturnedForQualification: _ =>
            {
                if (Interlocked.Increment(ref calls) != 1) return;
                returned.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "The test did not release the held folder call.");
            });
            var manager = Manager(temp, adapter, arm64);
            using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(folder, EntryKind.Directory)], StorageAction.Compress, Stop);
            using var finished = new ManualResetEventSlim();
            manager.JobFinished += _ => finished.Set();
            var job = manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.ExistingDescendants), _ => { });
            Assert.True(returned.Wait(TimeSpan.FromSeconds(30), Stop), "The folder's SET did not return.");
            job.Cancel(); // Stop after the folder's own call returned, before its enumeration
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(60), Stop));
            var result = job.StorageResult!;
            Output?.WriteLine($"Stop before descent: {job.State}; changed {result.Counts.Changed}, not processed {result.Counts.NotProcessed}, " +
                $"discovery {result.Discovery}, canceled {result.Canceled}, review pending {result.ReviewPending}");
            Assert.Equal(1, calls);
            Assert.Equal(1L, result.Counts.Changed);
            Assert.True(result.Canceled);
            Assert.False(result.ReviewPending);
            Assert.NotEqual(StorageCompleteness.Complete, result.Discovery);
            Assert.True(Compressed(folder));
            Assert.All(children, child => { Assert.False(Compressed(child)); Assert.Equal("unprocessed " + child.Length, File.ReadAllText(child)); });
            var status = manager.ReadStorageReview(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), Stop);
            Assert.False(status.ReviewPending);
            Assert.Equal(1L, status.Current.History.LastCall!.Call);
        }
        finally { release.Set(); Directory.Delete(temp, true); }
    }

    private static Job Submit(JobManager manager, ReviewedStorageRoots review, StorageAction action, StorageScope scope)
    {
        using var finished = new ManualResetEventSlim();
        void Finished(Job _) => finished.Set();
        manager.JobFinished += Finished;
        try
        {
            var job = manager.SubmitStorage(review, review.Approve(action, scope), _ => { });
            Assert.True(finished.Wait(TimeSpan.FromSeconds(60), Stop));
            return job;
        }
        finally { manager.JobFinished -= Finished; }
    }

    private static void MakeJunction(string path, string target)
    {
        Directory.CreateDirectory(path);
        SetJunction(path, target);
    }

    private static unsafe void SetJunction(string path, string target)
    {
        using var directory = NtFile.OpenOrThrow(null, @"\??\" + path, NtFile.WriteAttributes | NtFile.Synchronize,
            NtFile.ShareAll, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, "Disposable junction fixture");
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target), print = Encoding.Unicode.GetBytes(target);
        byte[] data = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0xA0000003);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), checked((ushort)(data.Length - 8)));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(data, 16); print.CopyTo(data, 18 + substitute.Length);
        fixed (byte* buffer = data)
            Assert.True(QControl(directory, 0x000900A4 /* FSCTL_SET_REPARSE_POINT */, buffer, (uint)data.Length, null, 0, out _, 0),
                "Set junction: " + Marshal.GetLastPInvokeError());
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DefineDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool QDefineDosDevice(uint flags, string device, string? target);
    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QControl(SafeFileHandle file, uint code, void* input, uint inputLength, void* output, uint outputLength, out uint returned, nint overlapped);
    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool QSetInfo(SafeFileHandle file, int infoClass, void* input, uint length);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
