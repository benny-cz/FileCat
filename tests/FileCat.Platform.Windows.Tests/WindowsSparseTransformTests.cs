using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Disposable NTFS behavior fixtures. These operations do not exercise FileCat release admission.</summary>
[Collection("Windows storage job evidence")]
public sealed partial class WindowsSparseTransformTests
{
    private const int Length = 4 * 1024 * 1024;
    private const string NamedStream = ":fixture";
    private const uint SetSparse = 0x000900C4;
    private const uint QueryAllocatedRanges = 0x000940CF;

    [Fact]
    public void Reviewed_hex_save_as_keeps_sparse_ranges_in_an_approved_clear_copy()
    {
        RequireFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Requires the explicit EFS fixture opt-in.");
        Run("main-holes", (path, before) =>
        {
            File.Encrypt(path);
            byte[] expected = File.ReadAllBytes(path);
            expected[64 * 1024] = 0x6d;
            string destination = Path.Join(Path.GetDirectoryName(path)!, "copy.bin");
            using var source = new ProtectedHexFile(path);
            using var overlay = new HexPatchOverlay(source);
            overlay.Write(64 * 1024, [0x6d]);
            using var review = HexSaveAs.Prepare(source, overlay, destination);
            Assert.True(review.NeedsEfsConsent);
            review.ApproveEfsExport();
            var result = HexSaveAs.CreateNew(review, TestContext.Current.CancellationToken);
            Assert.True(result.Sparse);
            Assert.False(result.ExpandedSparseHoles);
            Assert.InRange(result.BytesWritten, 1, Length / 2);
            using var copy = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            var observed = ObserveStream(copy);
            Assert.Equal(Length, observed.Length);
            Assert.Equal(before.Main.Ranges, observed.Ranges);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), observed.Digest);
            Assert.False(observed.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hex_save_as_reports_expanded_holes_and_preserves_bytes_on_an_owned_FAT32_volume(bool encrypted)
    {
        RequireFixture();
        string root = Fat32OutputRoot();
        if (encrypted) Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Requires the explicit EFS fixture opt-in.");
        string destination = Path.Join(root, (encrypted ? "efs-" : "clear-") + Guid.NewGuid().ToString("N") + ".bin");

        Run("main-holes", (path, before) =>
        {
            if (encrypted) File.Encrypt(path);
            byte[] expected = File.ReadAllBytes(path);
            const int editedHole = 1024 * 1024;
            Assert.Equal(0, expected[editedHole]);
            expected[editedHole] = 0x6d;
            var ct = TestContext.Current.CancellationToken;
            using (var source = new ProtectedHexFile(path))
            using (var overlay = new HexPatchOverlay(source))
            {
                overlay.Write(editedHole, [0x6d]);
                if (encrypted)
                {
                    Assert.Contains("approval", Assert.Throws<IOException>(() => HexSaveAs.CreateNew(source, overlay, destination, ct)).Message);
                    Assert.Empty(Directory.EnumerateFileSystemEntries(root));
                }

                using var review = HexSaveAs.Prepare(source, overlay, destination);
                Assert.Equal(encrypted, review.NeedsEfsConsent);
                if (encrypted) review.ApproveEfsExport();
                var result = HexSaveAs.CreateNew(review, ct);
                Assert.False(result.Sparse);
                Assert.True(result.ExpandedSparseHoles);
                Assert.Equal(Length, result.BytesWritten);
                Assert.Equal(HexOriginMark.None, result.OriginMark);
            }

            Assert.Equal(expected, File.ReadAllBytes(destination));
            Assert.Equal((FileAttributes)0, File.GetAttributes(destination) & (FileAttributes.SparseFile | FileAttributes.Encrypted));
            Assert.Equal(new[] { destination }, Directory.EnumerateFileSystemEntries(root));
            Assert.Equal(encrypted, File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(before.Main.Ranges, Observe(path).Main.Ranges);
            File.Delete(destination);
        });
    }

    [Theory]
    [InlineData("NTFS", false, false)]
    [InlineData("NTFS", true, false)]
    [InlineData("FAT32", false, false)]
    [InlineData("FAT32", true, true)]
    [InlineData("SMB", true, true)]
    public async Task Native_copy_reports_loss_of_sparse_attributes_after_publication(string targetVolume, bool encrypted, bool keepBoth)
    {
        RequireFixture();
        if (encrypted) Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Requires the explicit EFS fixture opt-in.");
        bool clearExport = targetVolume == "SMB";
        string? external = targetVolume == "FAT32" ? Fat32OutputRoot() : clearExport
            ? Environment.GetEnvironmentVariable("FILECAT_STORAGE_EFS_CLEAR_TEST_ROOT") : null;
        Assert.SkipUnless(!clearExport || !string.IsNullOrWhiteSpace(external) && Directory.Exists(external), "Requires the prepared SMB clear-copy fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-sparse-copy-").FullName;
        string destination = Path.Join(external ?? root, "copy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(destination);
            string source = Path.Join(root, "source.bin");
            WriteStream(source, sparse: true, holes: true, allHole: false, seed: 17);
            if (encrypted) File.Encrypt(source);
            byte[] expected = File.ReadAllBytes(source);
            StreamObservation before;
            using (var file = File.OpenRead(source)) before = ObserveStream(file);
            Assert.InRange(before.Allocation, 1, Length / 2);

            string existing = Path.Join(destination, Path.GetFileName(source));
            if (keepBoth) File.WriteAllBytes(existing, [0xab]);
            var (job, questions) = await WindowsEfsCopyTests.Run(root, new WindowsFileOperations(), JobKind.Copy, [source], destination, request =>
            {
                if (!clearExport || request is not ConfirmRequest { ConfirmClass: "decrypt" }) return new(DecisionAction.CancelJob);
                Assert.Equal(new[] { existing }, Directory.GetFiles(destination));
                return new(DecisionAction.Proceed);
            }, new TransferOptions { Conflicts = ConflictPolicy.KeepBothRenameIncoming });
            Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues,
                job.State + " " + JobIssue.Summarize(job.Issues));
            if (clearExport)
            {
                var prompt = Assert.IsType<ConfirmRequest>(Assert.Single(questions));
                Assert.Contains("may allocate space for the entire file", prompt.Message);
                Assert.Contains("Temporary copy", prompt.Message);
            }
            else Assert.Empty(questions);

            string published = keepBoth ? Assert.Single(Directory.EnumerateFiles(destination), file => file != existing)
                : Assert.Single(Directory.EnumerateFiles(destination));
            bool sparse = File.GetAttributes(published).HasFlag(FileAttributes.SparseFile);
            if (!clearExport) Assert.Equal(targetVolume == "NTFS", sparse);
            Assert.Equal(encrypted && !clearExport, File.GetAttributes(published).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(expected, File.ReadAllBytes(published));
            Assert.DoesNotContain(JournalRecovery.StagedPrefix, published);
            if (keepBoth) Assert.Equal(new byte[] { 0xab }, File.ReadAllBytes(existing));
            var notices = job.Issues.Where(issue => issue.Message.Contains("may use more disk space", StringComparison.Ordinal));
            if (sparse) Assert.Empty(notices);
            else
            {
                var notice = Assert.Single(notices);
                Assert.Equal(StepOutcome.Committed, notice.Outcome);
                Assert.Equal(published, notice.TargetPath);
            }

            Assert.Equal(encrypted, File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            using var sourceAgain = File.OpenRead(source);
            Assert.Equal(before.Ranges, ObserveStream(sourceAgain).Ranges);
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Fat32OutputRoot()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_SAVEAS_FAT32_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(root), "Requires the prepared FAT32 output fixture.");
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        Assert.Equal("FAT32", drive.DriveFormat);
        Assert.Equal("FCSPARSEOUT", drive.VolumeLabel);
        Assert.InRange(drive.TotalSize, 128L << 20, 320L << 20);
        return root!;
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Efs_native_actions_preserve_identity_and_logical_bytes_in_bounded_sparse_fixtures(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var encrypted = Report("encrypt", TryEfs(() => File.Encrypt(path)), path, first);
            Assert.True(encrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, encrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, encrypted.Named.Allocation);
            var decrypted = Report("decrypt", TryEfs(() => File.Decrypt(path)), path, first);
            Assert.False(decrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, decrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, decrypted.Named.Allocation);
        });
    }

    [Theory]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Compression_native_actions_preserve_identity_and_logical_bytes_in_bounded_sparse_fixtures(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var compressed = Report("compress-main", InvokeCompression(path, StorageAction.Compress), path, first);
            Assert.Equal(WindowsCompression.Lznt1, compressed.Main.Compression);
            Assert.Equal(first.Named.Compression, compressed.Named.Compression);
            Assert.Equal(first.Named.Allocation, compressed.Named.Allocation);
            var decompressed = Report("remove-main-compression", InvokeCompression(path, StorageAction.RemoveCompression), path, first);
            Assert.Equal(WindowsCompression.None, decompressed.Main.Compression);
            Assert.Equal(first.Main.Allocation, decompressed.Main.Allocation);
            Assert.Equal(first.Named.Compression, decompressed.Named.Compression);
            Assert.Equal(first.Named.Allocation, decompressed.Named.Allocation);
        });
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Reviewed_compression_jobs_preserve_sparse_streams_and_do_not_call_native_for_noops(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var jobs = CreateManager(path);
            RunJob(jobs, path, StorageAction.Compress, changed: true);
            var compressed = Report("reviewed compress", "changed", path, first);
            Assert.Equal(WindowsCompression.Lznt1, compressed.Main.Compression);
            Assert.Equal(first.Named.Allocation, compressed.Named.Allocation);
            Assert.Equal(first.Named.Compression, compressed.Named.Compression);
            RunJob(jobs, path, StorageAction.Compress, changed: false);

            RunJob(jobs, path, StorageAction.RemoveCompression, changed: true);
            var decompressed = Report("reviewed decompress", "changed", path, first);
            Assert.Equal(WindowsCompression.None, decompressed.Main.Compression);
            Assert.Equal(first.Main.Allocation, decompressed.Main.Allocation);
            Assert.Equal(first.Named.Allocation, decompressed.Named.Allocation);
            Assert.Equal(first.Named.Compression, decompressed.Named.Compression);
            RunJob(jobs, path, StorageAction.RemoveCompression, changed: false);
        });
    }

    [Fact]
    public void A_fresh_local_4_KiB_NTFS_volume_supports_classic_compression()
    {
        RequireFixture();
        string? canary = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_FRESH_4K_TARGET");
        if (string.IsNullOrWhiteSpace(canary)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_FRESH_4K_TARGET to a file on the newly formatted 4 KiB NTFS volume.");
        Assert.True(File.Exists(canary));
        string evidenceRoot = Directory.CreateTempSubdirectory("filecat-compression-fresh-volume-").FullName;
        string scratch = Path.Join(Path.GetDirectoryName(canary), "filecat-compress-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            string path = Path.Join(scratch, "selected.bin");
            File.WriteAllText(path, new string('t', 128 * 1024));
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                TestContext.Current.TestOutputHelper?.WriteLine("Initial WOF observation: " + WindowsStorageBacking.Query(handle, TestContext.Current.CancellationToken));
            var jobs = CreateManager(path, evidenceRoot);
            RunJob(jobs, path, StorageAction.Compress, changed: true);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Compressed));
            RunJob(jobs, path, StorageAction.RemoveCompression, changed: true);
            Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Compressed));
            Assert.Equal(new string('t', 128 * 1024), File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public void Large_sparse_removal_is_refused_on_review_and_after_late_growth()
    {
        RequireFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_SIZE") == "1",
            "Requires the separately prepared NTFS large sparse fixture.");
        const long largeLength = 30_000_000_001L;
        const StorageAction remove = StorageAction.RemoveCompression;
        string root = Directory.CreateTempSubdirectory("filecat-large-sparse-removal-").FullName;
        string path = Path.Join(root, "large.bin");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            WriteStream(path, sparse: true, holes: true, allHole: false, seed: 57);
            WriteStream(path + NamedStream, sparse: true, holes: true, allHole: false, seed: 91);
            InvokeCompression(path, StorageAction.Compress);
            var adapter = new WindowsStorageTransforms(qualifyNativeArm64: WindowsStorageAbiTests.QualifyNativeArm64Jobs);
            var jobs = CreateManager(path, adapter: adapter);
            var target = ItemRef.ForFileSystemPath(path, EntryKind.File);
            using var original = jobs.PrepareStorageReview([target], remove, ct);
            Assert.Equal(StorageObservationState.Enabled, Assert.Single(original.Roots).Observation.Compression);
            var approval = original.Approve(remove, StorageScope.SelectedItems);
            using var owner = original.Transfer(adapter, approval);
            using (var data = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                RandomAccess.SetLength(data, largeLength);

            using (var fresh = jobs.PrepareStorageReview([target], remove, ct))
            {
                Assert.Equal("large-sparse-removal", Assert.Single(fresh.Roots).Observation.Ineligible?.Code);
                Assert.Throws<InvalidOperationException>(() => fresh.Approve(remove, StorageScope.SelectedItems));
            }
            using (var native = StorageReviewResources.EnterNative(ct))
            using (var traversal = adapter.OpenReviewedRoot(owner.Leases[0], remove, ct))
            {
                Assert.Equal("large-sparse-removal", traversal.Root.PrepareMutation(remove, ct).Ineligible?.Code);
                var refused = traversal.Root.ApplyAdmitted(remove);
                Assert.Equal(StorageEffect.Skipped, refused.Effect);
                Assert.Equal("large-sparse-removal", refused.Problem?.Code);
                Assert.False(refused.NativeCallAttempted);
            }
            Assert.Empty(jobs.Jobs);
            Assert.Empty(Directory.EnumerateFiles(Path.Join(root, "journal")));
            using (var data = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.True(File.GetAttributes(data).HasFlag(FileAttributes.SparseFile));
                Assert.Equal(WindowsCompression.Lznt1, WindowsCompression.Query(data).Native);
                var layout = Assert.IsType<WindowsStorageTransforms.SparseLayout>(WindowsStorageTransforms.ReadSparseLayout(data, ct));
                Assert.Equal(largeLength, layout.Length);
                Assert.Equal(new[]
                {
                    new WindowsStorageTransforms.AllocatedRange(64 * 1024, 64 * 1024),
                    new WindowsStorageTransforms.AllocatedRange(2 * 1024 * 1024, 64 * 1024)
                }, layout.Ranges);
            }

            string clear = Path.Join(root, "clear.bin");
            WriteStream(clear, sparse: true, holes: true, allHole: false, seed: 57);
            using (var data = File.OpenHandle(clear, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                RandomAccess.SetLength(data, largeLength);
            RunJob(jobs, clear, remove, changed: false);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Reviewed_efs_binding_preserves_sparse_main_and_named_layouts(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var encrypted = ApplyReviewedEfs(path, StorageAction.EncryptEfs);
            Assert.Equal(StorageEffect.Changed, encrypted.Effect);
            Assert.True(encrypted.NativeCallAttempted);
            var enabled = Report("reviewed encrypt", "changed", path, first);
            Assert.True(enabled.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, enabled.Main.Allocation);
            Assert.Equal(first.Named.Allocation, enabled.Named.Allocation);
            var encryptNoop = ApplyReviewedEfs(path, StorageAction.EncryptEfs);
            Assert.Equal(StorageEffect.AlreadySatisfied, encryptNoop.Effect);
            Assert.False(encryptNoop.NativeCallAttempted);

            var decrypted = ApplyReviewedEfs(path, StorageAction.DecryptEfs);
            Assert.Equal(StorageEffect.Changed, decrypted.Effect);
            Assert.True(decrypted.NativeCallAttempted);
            var disabled = Report("reviewed decrypt", "changed", path, first);
            Assert.False(disabled.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, disabled.Main.Allocation);
            Assert.Equal(first.Named.Allocation, disabled.Named.Allocation);
            var decryptNoop = ApplyReviewedEfs(path, StorageAction.DecryptEfs);
            Assert.Equal(StorageEffect.AlreadySatisfied, decryptNoop.Effect);
            Assert.False(decryptNoop.NativeCallAttempted);
        });
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Reviewed_efs_jobs_preserve_sparse_streams_and_record_only_changed_calls(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => Path.GetDirectoryName(path)!,
                qualifyNativeArm64: arm64);
            var candidate = adapter.CreateEfsJobCandidate((_, sid, consent, ct) =>
            {
                var setup = adapter.InspectEfsSetup(sid, consent, true, ct);
                Assert.Null(setup.Problem);
                return StorageEfsRecoveryApproval.DeclareArrangement(consent, setup,
                    Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1, acknowledged: true);
            });
            var jobs = CreateManager(path, adapter: candidate);
            RunEfsJob(jobs, path, StorageAction.EncryptEfs, changed: true);
            var encrypted = Report("reviewed job encrypt", "changed", path, first);
            Assert.True(encrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, encrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, encrypted.Named.Allocation);
            RunEfsJob(jobs, path, StorageAction.EncryptEfs, changed: false);

            RunEfsJob(jobs, path, StorageAction.DecryptEfs, changed: true);
            var decrypted = Report("reviewed job decrypt", "changed", path, first);
            Assert.False(decrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, decrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, decrypted.Named.Allocation);
            RunEfsJob(jobs, path, StorageAction.DecryptEfs, changed: false);
        });
    }

    [Fact]
    public void A_read_only_sparse_file_refuses_efs_review_without_a_job_or_native_call()
    {
        RequireFixture();
        Run("main-holes", (path, first) =>
        {
            File.SetAttributes(path, first.Attributes | FileAttributes.ReadOnly);
            try
            {
                var adapter = new WindowsStorageTransforms(qualifyNativeArm64: WindowsStorageAbiTests.QualifyNativeArm64Jobs);
                var jobs = CreateManager(path, adapter: adapter.CreateEfsJobCandidate());
                using var review = jobs.PrepareStorageReview([ItemRef.ForFileSystemPath(path, EntryKind.File)],
                    StorageAction.EncryptEfs, TestContext.Current.CancellationToken);
                Assert.Equal("read-only-file", Assert.Single(review.Roots).Observation.Ineligible?.Code);
                Assert.Throws<InvalidOperationException>(() => review.ApproveEfs(StorageAction.EncryptEfs,
                    StorageScope.SelectedItems, new(true, true)));
                Assert.Empty(jobs.Jobs);
                Assert.Empty(Directory.EnumerateFiles(Path.Join(Path.GetDirectoryName(path)!, "journal")));
                Assert.False(Report("read-only reviewed encrypt", "refused", path, first).Attributes.HasFlag(FileAttributes.Encrypted));
            }
            finally { File.SetAttributes(path, first.Attributes); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_sparse_layout_growth_before_or_during_the_call_is_never_reported_as_success(bool duringCall)
    {
        RequireFixture();
        Run("named-holes", (path, first) =>
        {
            void GrowNamedStream()
            {
                using var named = new FileStream(path + NamedStream, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                named.Position = 1024 * 1024;
                named.WriteByte(0x5A);
                named.Flush(flushToDisk: true);
            }
            var outcome = ApplyReviewedEfs(path, StorageAction.EncryptEfs,
                afterPrepare: duringCall ? null : GrowNamedStream, afterCall: duringCall ? GrowNamedStream : null);
            Assert.Equal(duringCall ? StorageEffect.Uncertain : StorageEffect.NotProcessed, outcome.Effect);
            Assert.Equal(duringCall ? "efs-sparse-postcondition-changed" : "efs-sparse-preparation-changed", outcome.Problem?.Code);
            Assert.Equal(duringCall, outcome.NativeCallAttempted);
            var after = Observe(path);
            Assert.Equal(duringCall, after.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Digest, after.Main.Digest);
            Assert.NotEqual(first.Named.Digest, after.Named.Digest);
        });
    }

    private static StorageMutationResult ApplyReviewedEfs(string path, StorageAction action, Action? afterPrepare = null,
        Action? afterCall = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var adapter = new WindowsStorageTransforms(qualifyNativeArm64: WindowsStorageAbiTests.QualifyNativeArm64Jobs);
        var target = StoragePaths.Capture([ItemRef.ForFileSystemPath(path, EntryKind.File)])[0];
        using var reserve = StorageReviewResources.ReservePins(1);
        using var native = StorageReviewResources.EnterNative(ct);
        IStorageReviewLease pin;
        using (StorageReviewResources.EnterRootInspection(target))
            pin = adapter.InspectEfsRoot(target, action, ct);
        using (pin)
        {
            var consent = StorageEfsConsent.Capture(Guid.NewGuid(), action, StorageScope.SelectedItems,
                [pin.Observation.Identity], new(true, action == StorageAction.EncryptEfs));
            using var held = adapter.OpenEfsPathForQualification(pin, consent, ct, afterCall is null ? null : (boundPath, observedAction) =>
            {
                Assert.Equal(StorageAction.EncryptEfs, observedAction);
                File.Encrypt(boundPath);
                afterCall();
                return new(true, 0);
            });
            Assert.Null(held.PrepareMutation(action, ct).Ineligible);
            afterPrepare?.Invoke();
            var result = held.ApplyAdmitted(action);
            TestContext.Current.TestOutputHelper!.WriteLine($"reviewed {action} -> {result.Effect} {result.Problem}");
            return result;
        }
    }

    [Fact]
    public unsafe void A_sparse_range_overflow_refuses_before_a_native_call_while_a_noop_requires_no_inventory()
    {
        RequireFixture();
        const int stride = 128 * 1024;
        string root = Directory.CreateTempSubdirectory("filecat-sparse-fragmented-").FullName;
        string path = Path.Join(root, "fragmented.bin");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(Control(stream.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0));
                stream.SetLength((long)(WindowsStorageTransforms.SparseRangeLimit + 1) * stride);
                for (int i = 0; i <= WindowsStorageTransforms.SparseRangeLimit; i++)
                {
                    TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                    stream.Position = (long)i * stride;
                    stream.WriteByte(0x39);
                }
                stream.Flush(flushToDisk: true);
            }
            var jobs = CreateManager(path);
            RunJob(jobs, path, StorageAction.RemoveCompression, changed: false);
            var refused = Submit(jobs, path, StorageAction.Compress);
            Assert.Equal(JobState.Failed, refused.State);
            Assert.Equal(1, refused.StorageResult!.Counts.Failed);
            Assert.Contains(refused.StorageResult.Reasons, r => r.Problem.Code == "sparse-range-limit");
            AssertCallEvidence(jobs, refused, changed: false);
            using (var source = new ProtectedHexFile(path))
            using (var overlay = new HexPatchOverlay(source))
            {
                string destination = Path.Join(root, "copy.bin");
                var failure = Assert.Throws<StorageInspectionException>(() =>
                    HexSaveAs.CreateNew(source, overlay, destination, TestContext.Current.CancellationToken));
                Assert.Equal("sparse-range-limit", failure.Problem.Code);
                Assert.Equal(new[] { path }, Directory.GetFiles(root));
            }
            using var held = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(held).Native);
            Assert.True(File.GetAttributes(held).HasFlag(FileAttributes.SparseFile));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static JobManager CreateManager(string path, string? evidenceRoot = null, IStorageTransforms? adapter = null)
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        return new(new PortableFileOperations(), new ProviderRegistry(), Directory.CreateDirectory(
            Path.Join(evidenceRoot ?? Path.GetDirectoryName(path)!, "journal")).FullName, ArchiveJobBackends.Empty,
            adapter ?? new WindowsStorageTransforms(qualifyNativeArm64: arm64),
            arm64 ? WindowsStorageReviewEvidence.CreateArm64QualificationJournal : WindowsStorageReviewEvidence.CreateJournal,
            arm64 ? WindowsStorageReviewEvidence.Arm64QualificationBackend : WindowsStorageReviewEvidence.Backend);
    }

    private static Job Submit(JobManager jobs, string path, StorageAction action, TimeSpan? timeout = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var review = jobs.PrepareStorageReview([ItemRef.ForFileSystemPath(path, EntryKind.File)], action, ct);
        var selection = review.Approve(action, StorageScope.SelectedItems);
        using var finished = new ManualResetEventSlim();
        void OnFinished(Job _) => finished.Set();
        jobs.JobFinished += OnFinished;
        try
        {
            var job = jobs.SubmitStorage(review, selection, _ => { });
            Assert.True(finished.Wait(timeout ?? TimeSpan.FromSeconds(45), ct));
            return job;
        }
        finally { jobs.JobFinished -= OnFinished; }
    }

    private static void RunJob(JobManager jobs, string path, StorageAction action, bool changed)
    {
        var job = Submit(jobs, path, action);
        Assert.True(job.State == JobState.Completed, $"{job.State}: " + string.Join(", ",
            job.StorageResult!.Reasons.Select(r => r.Problem.Message).Concat(job.Issues.Select(i => i.Message))));
        Assert.Equal(changed ? 1 : 0, job.StorageResult!.Counts.Changed);
        Assert.Equal(changed ? 0 : 1, job.StorageResult.Counts.AlreadySatisfied);
        AssertCallEvidence(jobs, job, changed);
    }

    private static void RunEfsJob(JobManager jobs, string path, StorageAction action, bool changed, TimeSpan? timeout = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var review = jobs.PrepareStorageReview([ItemRef.ForFileSystemPath(path, EntryKind.File)], action, ct);
        Assert.Null(Assert.Single(review.Roots).Observation.Ineligible);
        var selection = review.ApproveEfs(action, StorageScope.SelectedItems,
            new(true, action == StorageAction.EncryptEfs));
        using var finished = new ManualResetEventSlim();
        void OnFinished(Job _) => finished.Set();
        jobs.JobFinished += OnFinished;
        try
        {
            var job = jobs.SubmitStorage(review, selection, _ => { });
            Assert.True(finished.Wait(timeout ?? TimeSpan.FromSeconds(45), ct));
            Assert.True(job.State == JobState.Completed, $"{job.State}: " + string.Join(", ",
                job.StorageResult!.Reasons.Select(r => r.Problem.Message).Concat(job.Issues.Select(i => i.Message))));
            Assert.Equal(changed ? 1 : 0, job.StorageResult!.Counts.Changed);
            Assert.Equal(changed ? 0 : 1, job.StorageResult.Counts.AlreadySatisfied);
            AssertCallEvidence(jobs, job, changed);
        }
        finally { jobs.JobFinished -= OnFinished; }
    }

    private static void AssertCallEvidence(JobManager jobs, Job job, bool changed)
    {
        var report = jobs.ReadStorageReview(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), TestContext.Current.CancellationToken);
        Assert.False(report.ReviewPending);
        Assert.True(report.Current.History.Ended);
        Assert.Equal(changed, report.Current.History.LastCall is not null);
    }

    private static void RequireFixture()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_TRANSFORMS") == "1",
            "Requires an explicitly prepared disposable NTFS volume on Windows 11.");
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.True(WindowsStorageTransforms.SupportsHost(arm64));
        if (arm64) WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    private static void Run(string shape, Action<string, Observation> transform)
    {
        string directory = Directory.CreateTempSubdirectory("filecat-sparse-transforms-").FullName;
        string path = Path.Join(directory, "fixture.bin");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(directory)!);
            Assert.Equal("NTFS", drive.DriveFormat);
            File.WriteAllBytes(path, []);
            WriteStream(path, shape != "named-holes", shape is "all-hole" or "main-holes" or "both-holes",
                allHole: shape == "all-hole", seed: 17);
            WriteStream(path + NamedStream, shape is "named-holes" or "both-holes", shape is "named-holes" or "both-holes",
                allHole: false, seed: 29);
            var first = Observe(path);
            if (shape != "named-holes") Assert.True(first.Attributes.HasFlag(FileAttributes.SparseFile));
            Assert.Equal(Length, first.Main.Length);
            Assert.Equal(Length, first.Named.Length);
            if (shape == "all-hole") Assert.Empty(first.Main.Ranges);
            if (shape is "main-holes" or "both-holes") Assert.True(first.Main.Ranges.Sum(range => range.Length) < Length / 2);
            if (shape is "named-holes" or "both-holes") Assert.True(first.Named.Ranges.Sum(range => range.Length) < Length / 2);
            if (shape == "sparse-no-holes") Assert.Equal(Length, first.Main.Ranges.Sum(range => range.Length));
            Assert.True(GetDiskFreeSpace(drive.RootDirectory.FullName, out uint sectors, out uint bytes, out _, out _));
            TestContext.Current.TestOutputHelper!.WriteLine($"{shape} NTFS {RuntimeInformation.ProcessArchitecture} allocationUnit={checked(sectors * bytes)}");
            Write("before", "observed", first);

            transform(path, first);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static unsafe void WriteStream(string streamPath, bool sparse, bool holes, bool allHole, int seed)
    {
        using var stream = new FileStream(streamPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (sparse) Assert.True(Control(stream.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0),
            $"Setting the fixture sparse failed with {Marshal.GetLastPInvokeError()}.");
        stream.SetLength(Length);
        if (holes)
        {
            if (!allHole)
            {
                stream.Position = 64 * 1024;
                stream.WriteByte((byte)seed);
                stream.Position = 2 * 1024 * 1024;
                stream.WriteByte((byte)(seed + 1));
            }
        }
        else
        {
            byte[] block = new byte[64 * 1024];
            new Random(seed).NextBytes(block);
            while (stream.Position < Length) stream.Write(block);
        }
        stream.Flush(flushToDisk: true);
    }

    private static string TryEfs(Action call)
    {
        try { call(); return "success"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return $"failure 0x{error.HResult:X8} {error.Message}"; }
    }

    private static string InvokeCompression(string path, StorageAction action)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var result = WindowsCompression.InvokeAdmitted(file, action);
        return $"success={result.NativeSucceeded} error={result.NativeError} format={result.After.Native}";
    }

    private static Observation Report(string phase, string result, string path, Observation first)
    {
        var observed = Observe(path);
        Write(phase, result, observed);
        Assert.Equal(first.Identity, observed.Identity);
        Assert.Equal(first.Main.Digest, observed.Main.Digest);
        Assert.Equal(first.Named.Digest, observed.Named.Digest);
        Assert.Equal(first.Main.Length, observed.Main.Length);
        Assert.Equal(first.Named.Length, observed.Named.Length);
        Assert.Equal(first.Main.Ranges, observed.Main.Ranges);
        Assert.Equal(first.Named.Ranges, observed.Named.Ranges);
        Assert.Equal(first.Main.Attributes & FileAttributes.SparseFile, observed.Main.Attributes & FileAttributes.SparseFile);
        Assert.Equal(first.Named.Attributes & FileAttributes.SparseFile, observed.Named.Attributes & FileAttributes.SparseFile);
        return observed;
    }

    private static void Write(string phase, string result, Observation observation)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{phase} {result} attributes={observation.Attributes} id={observation.Identity}");
        WriteStreamObservation("main", observation.Main);
        WriteStreamObservation("named", observation.Named);
    }

    private static void WriteStreamObservation(string name, StreamObservation observation) =>
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"  {name} eof={observation.Length} allocation={observation.Allocation} attributes={observation.Attributes} compression={observation.Compression} " +
            $"ranges={string.Join(",", observation.Ranges.Select(r => $"{r.Offset}+{r.Length}"))} sha256={observation.Digest}");

    private static Observation Observe(string path)
    {
        using var main = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var named = new FileStream(path + NamedStream, FileMode.Open, FileAccess.Read, FileShare.Read);
        var identity = ProtectedHexFile.Identity(main.SafeFileHandle);
        Assert.Equal(identity, ProtectedHexFile.Identity(named.SafeFileHandle));
        return new(identity, File.GetAttributes(main.SafeFileHandle), ObserveStream(main), ObserveStream(named));
    }

    private static unsafe StreamObservation ObserveStream(FileStream stream)
    {
        var input = new Range(0, stream.Length);
        var output = stackalloc Range[32];
        uint rangeSize = (uint)sizeof(Range);
        Assert.True(Control(stream.SafeFileHandle, QueryAllocatedRanges, &input, rangeSize, output, 32 * rangeSize, out uint bytes, 0),
            $"Allocated-range query failed with {Marshal.GetLastPInvokeError()}.");
        Assert.True(bytes <= 32 * sizeof(Range) && bytes % sizeof(Range) == 0);
        var ranges = new ReadOnlySpan<Range>(output, (int)(bytes / sizeof(Range))).ToArray();
        long last = 0;
        foreach (var range in ranges)
        {
            Assert.True(range.Offset >= last && range.Length > 0 && checked(range.Offset + range.Length) <= input.Length);
            last = range.Offset + range.Length;
        }
        Assert.True(GetInfo(stream.SafeFileHandle, 1, out StandardInfo standard, (uint)sizeof(StandardInfo)));
        Assert.Equal(stream.Length, standard.EndOfFile);
        return new(stream.Length, standard.AllocationSize, File.GetAttributes(stream.SafeFileHandle), WindowsCompression.Query(stream.SafeFileHandle).Native,
            ranges, Convert.ToHexString(SHA256.HashData(stream)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Range(long Offset, long Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }

    private sealed record StreamObservation(long Length, long Allocation, FileAttributes Attributes, ushort? Compression, Range[] Ranges, string Digest);
    private sealed record Observation(FileIdentity Identity, FileAttributes Attributes, StreamObservation Main, StreamObservation Named);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Control(SafeFileHandle file, uint code, void* input, uint inputBytes, void* output,
        uint outputBytes, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetInfo(SafeFileHandle file, int informationClass, out StandardInfo result, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector,
        out uint freeClusters, out uint totalClusters);
}
