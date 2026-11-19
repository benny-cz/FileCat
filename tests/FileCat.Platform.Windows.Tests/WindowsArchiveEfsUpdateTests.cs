using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveEfsUpdateTests
{
    [Theory]
    [InlineData(false, FileAttributes.Normal)]
    [InlineData(false, FileAttributes.Compressed)]
    [InlineData(false, FileAttributes.SparseFile)]
    [InlineData(true, FileAttributes.Normal)]
    public void Legacy_zip_publication_preserves_the_original_object_or_leaves_its_hard_link_unchanged(bool hardLinked, FileAttributes storage)
    {
        RequireLocalNtfs();
        string root = Directory.CreateTempSubdirectory("filecat-zip-publication-").FullName;
        string target = Path.Join(root, "original.zip"), alias = Path.Join(root, "linked.zip");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        try
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            RequireZipPublicationVolume(target, storage);
            File.WriteAllText(target + ":filecat-fixture", "other stream");
            File.WriteAllText(target + ":Zone.Identifier", mark, Encoding.Unicode);
            if (storage != FileAttributes.Normal) ConfigureZipStorage(target, storage);
            if (storage == FileAttributes.Compressed) ConfigureZipStorage(target + ":filecat-fixture", storage);
            var originalMark = File.ReadAllBytes(target + ":Zone.Identifier");
            var original = File.ReadAllBytes(target);
            var creation = File.GetCreationTimeUtc(target);
            byte[]? originalEas = null;
            if (hardLinked)
            {
                WriteZipTestEa(target);
                originalEas = ReadZipTestEas(target);
                Assert.NotEmpty(originalEas);
                Assert.True(CreateHardLink(alias, target, 0));
            }
            var files = new WindowsFileOperations();
            string id = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "ZIP publication fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);

            new ZipUpdateExecutor(job, files, journal).Execute();

            using var output = ZipFile.OpenRead(target);
            Assert.NotNull(output.GetEntry("original.txt"));
            Assert.NotNull(output.GetEntry("new-folder/"));
            Assert.Equal(mark, files.ReadOriginMark(target));
            if (hardLinked)
            {
                Assert.NotEqual(id, files.GetFileIdentity(target));
                Assert.Equal(id, files.GetFileIdentity(alias));
                Assert.Equal(original, File.ReadAllBytes(alias));
                Assert.Equal(originalMark, File.ReadAllBytes(alias + ":Zone.Identifier"));
                Assert.Equal(originalMark, File.ReadAllBytes(target + ":Zone.Identifier"));
                Assert.Equal(creation, File.GetCreationTimeUtc(target));
                Assert.Equal("other stream", File.ReadAllText(target + ":filecat-fixture"));
                Assert.Equal(originalEas, ReadZipTestEas(target));
                Assert.Equal(originalEas, ReadZipTestEas(alias));
                string backup = Assert.Single(Directory.GetFiles(root, "*.bak"));
                Assert.Equal(id, files.GetFileIdentity(backup));
                Assert.True(journal.NeedsRecovery);
                return;
            }
            Assert.Equal(id, files.GetFileIdentity(target));
            Assert.Equal(creation, File.GetCreationTimeUtc(target));
            Assert.Equal(originalMark, File.ReadAllBytes(target + ":Zone.Identifier"));
            Assert.Equal("other stream", File.ReadAllText(target + ":filecat-fixture"));
            const FileAttributes storageMask = FileAttributes.Compressed | FileAttributes.SparseFile;
            Assert.Equal(storage & storageMask, File.GetAttributes(target) & storageMask);
            if (storage == FileAttributes.Compressed)
            {
                Assert.Equal(2, ReadZipCompression(target));
                Assert.Equal(2, ReadZipCompression(target + ":filecat-fixture"));
                Assert.Equal(0, ReadZipCompression(target + ":Zone.Identifier"));
            }
            Assert.Equal([target], Directory.GetFiles(root));
            Assert.False(journal.NeedsRecovery);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string name, string original, nint security);

    private static void WriteZipTestEa(string path)
    {
        ReadOnlySpan<byte> name = "owned.test"u8;
        // A single FILE_FULL_EA_INFORMATION entry with one value byte.
        byte[] packet = new byte[(10 + name.Length + 3) & ~3];
        packet[5] = (byte)name.Length;
        packet[6] = 1;
        name.CopyTo(packet.AsSpan(8));
        packet[9 + name.Length] = 123;
        using var file = File.OpenHandle(path, access: FileAccess.ReadWrite);
        WindowsHiddenData.WriteArchiveCopyEas(file, packet);
    }

    private static byte[] ReadZipTestEas(string path)
    {
        using var file = File.OpenHandle(path);
        return Assert.IsType<byte[]>(WindowsHiddenData.ReadArchiveCopyEas(file));
    }

    private static void RequireZipPublicationVolume(string path, FileAttributes storage = FileAttributes.Normal)
    {
        using var file = File.OpenHandle(path);
        uint flags = WindowsArchiveNative.Volume(file, requirePrivateNtfs: false).Flags;
        Assert.SkipUnless((flags & 0x00200000 /* FILE_SUPPORTS_TRANSACTIONS */) != 0, "Requires NTFS transaction support.");
        if (storage == FileAttributes.Compressed)
            Assert.SkipUnless((flags & 0x10 /* FILE_FILE_COMPRESSION */) != 0, "Requires NTFS per-file compression support.");
        if (storage == FileAttributes.SparseFile)
            Assert.SkipUnless((flags & 0x40 /* FILE_SUPPORTS_SPARSE_FILES */) != 0, "Requires NTFS sparse file support.");
    }

    private static unsafe void ConfigureZipStorage(string path, FileAttributes storage)
    {
        using var file = File.OpenHandle(path, access: FileAccess.ReadWrite);
        ushort lznt1 = 2;
        bool configured = storage == FileAttributes.Compressed
            ? ZipStorageControl(file, 0x0009C040 /* FSCTL_SET_COMPRESSION */, &lznt1, 2, null, 0, out _, 0)
            : ZipStorageControl(file, 0x000900C4 /* FSCTL_SET_SPARSE */, null, 0, null, 0, out _, 0);
        Assert.True(configured, $"Could not set {storage} on the owned fixture ({Marshal.GetLastPInvokeError()}).");
    }

    private static unsafe ushort ReadZipCompression(string path)
    {
        using var file = File.OpenHandle(path);
        ushort format;
        Assert.True(ZipStorageControl(file, 0x0009003C /* FSCTL_GET_COMPRESSION */, null, 0, &format, 2, out uint returned, 0),
            $"Could not read compression on the owned fixture ({Marshal.GetLastPInvokeError()}).");
        Assert.Equal(2u, returned);
        return format;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ZipStorageControl(SafeFileHandle file, uint code, void* input, uint inputLength,
        void* output, uint outputLength, out uint returned, nint overlapped);

    private static void LegacyReplace(string stage, string target, string backup) =>
        File.Replace(stage, target, backup, ignoreMetadataErrors: true);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Legacy_zip_publication_is_private_until_commit_and_retains_the_snapshot_on_cancellation(bool cancel, bool hardLinked)
    {
        RequireLocalNtfs();
        string root = Directory.CreateTempSubdirectory("filecat-zip-publication-commit-").FullName;
        string target = Path.Join(root, "original.zip"), stage = Path.Join(root, "stage.tmp");
        string backup = Path.Join(root, "snapshot.bak"), alias = Path.Join(root, "alias.zip");
        string existingAlias = Path.Join(root, "existing-alias.zip");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        try
        {
            var files = new WindowsFileOperations();
            ILegacyZipInPlacePublisher publisher = files;
            var original = WriteReviewedZip(target, "original.txt", null);
            RequireZipPublicationVolume(target);
            var rebuilt = WriteReviewedZip(stage, "replacement.txt", mark);
            if (hardLinked) Assert.True(CreateHardLink(existingAlias, target, 0));
            var oldBytes = File.ReadAllBytes(target);
            var newBytes = File.ReadAllBytes(stage);
            using var originalPin = Pin(target);
            using var stagePin = Pin(stage);
            bool reachedCommit = false;

            var result = WindowsFileOperations.PublishLegacyZipInPlaceCore(target, stage, backup, original, originalPin,
                rebuilt, stagePin, checkpoint: () => { }, beforeCommit: () =>
                {
                    reachedCommit = true;
                    RequirePrivateUntilCommit(() => Assert.Equal(oldBytes, File.ReadAllBytes(target)));
                    RequirePrivateUntilCommit(() => Assert.Null(files.ReadOriginMark(target)));
                    if (hardLinked)
                    {
                        Assert.False(File.Exists(backup));
                        RequirePrivateUntilCommit(() => Assert.Equal(oldBytes, File.ReadAllBytes(existingAlias)));
                    }
                    else Assert.Equal(oldBytes, File.ReadAllBytes(backup));
                    Assert.False(CreateHardLink(alias, target, 0));
                    if (cancel) throw new OperationCanceledException("Owned fixture stopped before publication.");
                });

            Assert.NotNull(result);
            Assert.True(reachedCommit, result.Failure?.ToString());
            Assert.True(result.Published == !cancel, result.Failure?.ToString());
            Assert.Equal(hardLinked && !cancel ? rebuilt.Version.Identity : original.Version.Identity, files.GetFileIdentity(target));
            Assert.Equal(cancel ? oldBytes : newBytes, File.ReadAllBytes(target));
            Assert.Equal(cancel ? null : mark, files.ReadOriginMark(target));
            Assert.Equal(hardLinked ? !cancel : cancel, File.Exists(backup));
            Assert.Equal(cancel, File.Exists(stage));
            if (hardLinked)
            {
                Assert.Equal(original.Version.Identity, files.GetFileIdentity(existingAlias));
                Assert.Equal(oldBytes, File.ReadAllBytes(existingAlias));
                if (!cancel) Assert.Equal(original.Version.Identity, files.GetFileIdentity(backup));
            }
            if (cancel)
            {
                Assert.True(result.Failure is OperationCanceledException, result.Failure?.ToString());
                Assert.NotNull(result.ReviewReason);
            }
            else
            {
                Assert.Null(result.Failure);
                if (hardLinked) Assert.NotNull(result.ReviewReason);
                else Assert.Null(result.ReviewReason);
            }

            LegacyZipRollbackFile WriteReviewedZip(string path, string member, string? origin)
            {
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry(member);
                if (origin is not null) Assert.True(files.WriteOriginMark(path, origin));
                var baseline = ArchiveBaseline.Of(path);
                return new(new(Assert.IsType<string>(files.GetFileIdentity(path)), baseline.Length, baseline.Sha256Hex),
                    origin, Encrypted: false);
            }

            SafeFileHandle Pin(string path)
            {
                using var file = File.OpenHandle(path, share: FileShare.ReadWrite | FileShare.Delete);
                return Assert.IsType<SafeFileHandle>(publisher.PinLegacyZipForPublication(file));
            }

            static void RequirePrivateUntilCommit(Action read)
            {
                // An outside reader may see the committed version or be blocked while NTFS holds the new stream.
                const int sharingViolation = unchecked((int)0x80070020), transactionalConflict = unchecked((int)0x80071A90);
                var error = Record.Exception(read);
                Assert.True(error is null || error is IOException io &&
                    (io.InnerException ?? io).HResult is sharingViolation or transactionalConflict, error?.ToString());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Legacy_revision_does_not_hash_an_archive_encrypted_after_the_reader_opens()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-efs-revision-").FullName;
        string target = Path.Join(root, "original.zip");
        try
        {
            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("original.txt").Open()))
                writer.Write("Original archive member");
            byte[] bytes = File.ReadAllBytes(target);
            int checkpoints = 0;
            var refusal = Assert.Throws<ArchiveWriteException>(() => ArchiveBaseline.OfForMutation(target, () =>
            {
                if (++checkpoints == 1) File.Encrypt(target);
            }));
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, refusal.Reason);
            Assert.Equal(ArchiveOuterEfs.UpdateUnavailable, refusal.Message);
            Assert.Equal(1, checkpoints);
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(bytes, File.ReadAllBytes(target));
            Assert.Equal([target], Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Archive_update_refuses_a_target_encrypted_after_review_before_staging(bool native)
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-archive-efs-update-").FullName;
        string target = Path.Join(root, "original.zip");
        try
        {
            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("original.txt").Open()))
                writer.Write("Original archive member");
            byte[] original = File.ReadAllBytes(target);
            var baseline = ArchiveBaseline.Of(target);
            ArchiveFileObservation? nativeBaseline;
            using (var directory = WindowsArchiveDirectory.Open(root))
            using (var file = directory.OpenSource(Path.GetFileName(target)))
                nativeBaseline = ((IArchiveRetainedFile)file).Observe(TestContext.Current.CancellationToken);

            File.Encrypt(target);
            File.SetLastWriteTimeUtc(target, new DateTime(baseline.LastWriteUtcTicks, DateTimeKind.Utc));
            Assert.True(baseline.Matches(target));
            Assert.Equal(ArchiveOuterEfs.UpdateUnavailable, WindowsArchiveJobBackend.TargetProblem(target));

            var profile = native ? new ArchiveWriteProfile(ArchiveWriteFormat.Zip) : null;
            var plan = new ArchivePlan(target, baseline, [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")], Profile: profile)
            { NativeBaseline = native ? nativeBaseline : null };
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan },
                "EFS archive update fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            var files = new WindowsFileOperations();
            ArchiveWriteException problem;
            if (native)
            {
                // The backend must stop while opening the update scope, before trying to launch a worker.
                var backend = WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Zip, Path.Join(root, "unused-worker.exe"));
                problem = Assert.Throws<ArchiveWriteException>(() => backend.Execute(new(job, plan, null, files, journal)));
            }
            else
            {
                problem = Assert.Throws<ArchiveWriteException>(() => new ZipUpdateExecutor(job, files, journal).Execute());
            }
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, problem.Reason);
            Assert.Equal(ArchiveOuterEfs.UpdateUnavailable, problem.Message);
            Assert.Equal([target], Directory.GetFiles(root));
            Assert.True((File.GetAttributes(target) & FileAttributes.Encrypted) != 0);
            Assert.Equal(original, File.ReadAllBytes(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Native_transaction_refuses_an_encrypted_snapshot_before_copying_original_bytes()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-native-efs-snapshot-").FullName;
        string archive = Path.Join(root, "original.tar");
        byte[] originalBytes = "owned transactional archive bytes"u8.ToArray();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            File.WriteAllBytes(archive, originalBytes);
            using var directory = WindowsArchiveDirectory.Open(root);
            using var original = directory.OpenReviewedTarget("original.tar");
            var baseline = ((IArchiveRetainedFile)original).Observe(ct);
            var nonce = Guid.NewGuid();
            using var snapshot = directory.CreateArtifact(nonce, WindowsArchiveArtifactKind.Stage, originalBytes.Length);
            File.Encrypt(snapshot.DisplayPath);
            Assert.True(File.GetAttributes(snapshot.DisplayPath).HasFlag(FileAttributes.Encrypted));

            using (var transaction = original.BeginContentTransaction(baseline, nonce, TimeSpan.FromSeconds(30), ct))
            {
                var refusal = Assert.Throws<ArchiveWriteException>(() => transaction.RetainOriginal(snapshot, ct));
                Assert.Equal(ArchiveWriteReason.PublicationUnavailable, refusal.Reason);
                Assert.Equal(ArchiveOuterEfs.NativeUpdateArtifactUnavailable, refusal.Message);
                Assert.Equal(0, snapshot.Length);
                transaction.Rollback();
            }

            Assert.Equal(originalBytes, File.ReadAllBytes(archive));
            Assert.True(File.GetAttributes(snapshot.DisplayPath).HasFlag(FileAttributes.Encrypted));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Native_update_read_view_refuses_protection_drift_on_the_held_original()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-native-update-efs-drift-").FullName;
        string target = Path.Join(root, "original.zip");
        byte[] bytes = [1, 2, 3, 4];
        try
        {
            File.WriteAllBytes(target, bytes);
            using var directory = WindowsArchiveDirectory.Open(root);
            using var file = directory.OpenSource(Path.GetFileName(target));
            file.BindUpdateSource();
            var retained = (IArchiveRetainedFile)file;
            var ct = TestContext.Current.CancellationToken;
            var original = retained.Observe(ct);
            using var read = retained.OpenReadView(original, ct);
            Assert.Equal(1, read.ReadByte());

            File.Encrypt(target);
            byte[] next = [0];
            var refusal = Assert.Throws<ArchiveWriteException>(() => read.Read(next));
            Assert.Equal(ArchiveOuterEfs.UpdateUnavailable, refusal.Message);
            Assert.Equal((byte)0, next[0]);
            Assert.Equal(1, read.Position);
            Assert.Equal(ArchiveOuterEfs.UpdateUnavailable, Assert.Throws<ArchiveWriteException>(() => retained.Observe(ct)).Message);
            Assert.Equal([target], Directory.GetFiles(root));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(bytes, File.ReadAllBytes(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_update_preserves_newly_encrypted_original_during_preparation_or_backup_review(bool encryptBackup)
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-update-efs-drift-").FullName;
        string target = Path.Join(root, "original.zip");
        try
        {
            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("original.txt").Open()))
                writer.Write("Original archive member");
            byte[] original = File.ReadAllBytes(target);
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "EFS drift fixture", "", [], []);
            var files = new EncryptDuringMarkRead(target, encryptBackup);
            string originalIdentity = files.GetFileIdentity(target)!;
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);

            var refusal = Assert.Throws<ArchiveWriteException>(() => new ZipUpdateExecutor(job, files, journal, replaceFile: LegacyReplace).Execute());
            Assert.True(files.Encrypted);
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, refusal.Reason);
            Assert.Equal(encryptBackup ? "The original archive became EFS-encrypted during the update. FileCat restored the encrypted original."
                : ArchiveOuterEfs.UpdateUnavailable, refusal.Message);
            Assert.Equal(originalIdentity, files.GetFileIdentity(target));
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal([target], Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_update_refuses_changed_or_encrypted_stage_after_verification(bool encrypt)
    {
        RequireLocalNtfs();
        if (encrypt) RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-stage-drift-").FullName;
        string target = Path.Join(root, "original.zip");
        try
        {
            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
                archive.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(target);
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Legacy ZIP stage drift", "", [], []);
            var files = new ChangeStageDuringPreparation(target, encrypt);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);

            var error = Record.Exception(() => new ZipUpdateExecutor(job, files, journal, replaceFile: LegacyReplace).Execute());
            string stage = Assert.IsType<string>(files.Stage);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Empty(Directory.GetFiles(root, "*.bak"));
            Assert.True(journal.NeedsRecovery);
            Assert.Equal(files.StageBytes, File.ReadAllBytes(stage));
            if (encrypt)
            {
                var refusal = Assert.IsType<ArchiveWriteException>(error);
                Assert.Equal(ArchiveWriteReason.PublicationUnavailable, refusal.Reason);
                Assert.Equal(ArchiveOuterEfs.UpdateStageUnavailable, refusal.Message);
                Assert.True(File.GetAttributes(stage).HasFlag(FileAttributes.Encrypted));
                Assert.Contains(job.Issues, issue => issue.Path == stage && issue.Message.Contains("kept", StringComparison.Ordinal));
                var cleanup = files.DeleteClearOwnedFile(stage, files.GetFileIdentity(stage)!);
                Assert.False(cleanup!.Deleted);
                Assert.True(File.Exists(stage));
            }
            else
            {
                Assert.Contains("changed after verification", Assert.IsType<IOException>(error).Message, StringComparison.Ordinal);
                Assert.Contains(job.Issues, issue => issue.Path == stage && issue.Outcome == StepOutcome.Uncertain);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_update_keeps_both_files_if_the_replacement_or_unverified_backup_becomes_EFS_protected(bool encryptOutput)
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-publication-efs-drift-").FullName;
        string target = Path.Join(root, "original.zip");
        try
        {
            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
                archive.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(target);
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Legacy ZIP output drift", "", [], []);
            var files = new EncryptAfterPublication(target, encryptOutput);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);

            Assert.Throws<IOException>(() => new ZipUpdateExecutor(job, files, journal, replaceFile: LegacyReplace).Execute());
            string backup = Assert.Single(Directory.GetFiles(root, "*.bak"));
            Assert.True(files.Encrypted);
            Assert.Equal(original, File.ReadAllBytes(backup));
            Assert.Equal(!encryptOutput, File.GetAttributes(backup).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(encryptOutput, File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            using var published = ZipFile.OpenRead(target);
            Assert.NotNull(published.GetEntry("new-folder/"));
            string expected = encryptOutput ? ArchiveOuterEfs.UpdateOutputUnavailable : ArchiveOuterEfs.UpdateUnavailable;
            Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Uncertain &&
                issue.Message.Contains(backup, StringComparison.Ordinal) && issue.Message.Contains(expected, StringComparison.Ordinal));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Legacy_partial_replace_restores_only_the_exact_encrypted_original_to_a_vacant_name(bool occupied, bool changedBackup)
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-partial-efs-").FullName;
        string target = Path.Join(root, "original.zip");
        string journals = Path.Join(root, "journals");
        string movedOriginal = Path.Join(root, "original-moved-again.zip");
        string? backup = null, stage = null;
        try
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(target);
            File.WriteAllText(target + ":fixture", "original named stream");
            var files = new WindowsFileOperations();
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), []);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Legacy partial ZIP update", "", [], []);
            using (var journal = JobJournal.Create(journals, job))
            {
                var update = new LegacyZipPublicationTransaction(job, files, journal, plan, target, root,
                    replaceFile: (incoming, archive, retained) =>
                    {
                        stage = incoming;
                        backup = retained;
                        File.Move(archive, retained, overwrite: false);
                        File.Encrypt(retained);
                        // Model Windows reporting the partially applied result with metadata on the replacement.
                        File.Encrypt(incoming);
                        if (changedBackup)
                        {
                            File.Move(retained, movedOriginal, overwrite: false);
                            File.WriteAllText(retained, "another file occupies the backup name");
                        }
                        if (occupied) File.WriteAllText(archive, "another file occupies the original name");
                        throw new IOException("Simulated ERROR_UNABLE_TO_MOVE_REPLACEMENT_2", unchecked((int)0x80070499));
                    });
                using (var source = new ArchiveMutationReadStream(target)) update.CaptureOpenedSource(source);
                using (var output = update.CreateStage())
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                    zip.CreateEntry("rebuilt.txt");
                using (var read = update.OpenStageRead()) update.CaptureVerifiedStage(read);
                update.Prepare();

                var failure = Assert.Throws<IOException>(() => update.Publish(() => throw new InvalidOperationException("Publication was not expected.")));
                update.RecordFailure(failure);
                update.CleanupUnpublishedStage();
                Assert.True(journal.NeedsRecovery);
            }

            string stagePath = Assert.IsType<string>(stage), backupPath = Assert.IsType<string>(backup);
            string originalPath = changedBackup ? movedOriginal : occupied ? backupPath : target;
            Assert.Equal(identity, files.GetFileIdentity(originalPath));
            Assert.Equal(original, File.ReadAllBytes(originalPath));
            Assert.True(File.GetAttributes(originalPath).HasFlag(FileAttributes.Encrypted));
            Assert.Equal("original named stream", File.ReadAllText(originalPath + ":fixture"));
            Assert.True(File.GetAttributes(stagePath).HasFlag(FileAttributes.Encrypted));
            if (occupied) Assert.Equal("another file occupies the original name", File.ReadAllText(target));
            if (changedBackup)
            {
                Assert.False(File.Exists(target));
                Assert.Equal("another file occupies the backup name", File.ReadAllText(backupPath));
            }
            if (!occupied && !changedBackup) Assert.False(File.Exists(backupPath));
            var pending = Assert.Single(Assert.Single(JournalRecovery.Scan(journals)).OpenIntents);
            Assert.Equal(stagePath, pending.Staged);
            Assert.Equal(backupPath, pending.RecoveryPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Legacy_download_mark_loss_rolls_back_through_the_real_Windows_archive_path()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-mark-rollback-").FullName;
        string target = Path.Join(root, "original.zip");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        try
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(target);
            var files = new RemovePublishedMark(target);
            Assert.True(files.WriteOriginMark(target, mark));
            string id = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Legacy ZIP mark loss", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);

            var failure = Assert.Throws<IOException>(() => new ZipUpdateExecutor(job, files, journal, replaceFile: LegacyReplace).Execute());
            Assert.Contains("original was restored", failure.Message, StringComparison.Ordinal);
            Assert.True(files.Removed);
            Assert.Equal(id, files.GetFileIdentity(target));
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(mark, files.ReadOriginMark(target));
            Assert.False(journal.NeedsRecovery);
            Assert.Equal([target], Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Held_ZIP_rollback_preserves_a_new_archive_occupant_or_a_new_stream_on_the_rebuild(bool occupyArchive)
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-held-zip-rollback-").FullName;
        string target = Path.Join(root, "original.zip");
        string backup = Path.Join(root, ".filecat-zip-owned.bak");
        string stage = Path.Join(root, ".filecat-zip-owned.tmp");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        try
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("rebuilt.txt");
            using (var zip = ZipFile.Open(backup, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            byte[] rebuilt = File.ReadAllBytes(target), original = File.ReadAllBytes(backup);
            var files = new WindowsFileOperations();
            string rebuildId = Assert.IsType<string>(files.GetFileIdentity(target));
            string originalId = Assert.IsType<string>(files.GetFileIdentity(backup));
            Assert.True(files.WriteOriginMark(backup, mark));
            File.Encrypt(backup);
            var current = new LegacyZipRollbackFile(new(rebuildId, rebuilt.Length, Convert.ToHexString(SHA256.HashData(rebuilt))), null, Encrypted: false);
            var retained = new LegacyZipRollbackFile(new(originalId, original.Length, Convert.ToHexString(SHA256.HashData(original))), mark, Encrypted: true);

            var result = WindowsFileOperations.RollbackLegacyZipCore(target, backup, stage, current, retained, afterRetire: () =>
            {
                if (occupyArchive)
                    File.WriteAllText(target, "independent archive-name occupant");
                else
                {
                    using var stream = new FileStream(stage + ":fixture", FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
                    using var writer = new StreamWriter(stream);
                    writer.Write("independent named-stream data");
                }
            });

            Assert.Equal(!occupyArchive, result.Restored);
            Assert.True(result.RebuildRetained);
            Assert.Equal(rebuildId, files.GetFileIdentity(stage));
            Assert.Equal(rebuilt, File.ReadAllBytes(stage));
            string originalPath = occupyArchive ? backup : target;
            Assert.Equal(originalId, files.GetFileIdentity(originalPath));
            Assert.Equal(original, File.ReadAllBytes(originalPath));
            Assert.True(File.GetAttributes(originalPath).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(mark, files.ReadOriginMark(originalPath));
            if (occupyArchive) Assert.Equal("independent archive-name occupant", File.ReadAllText(target));
            else
            {
                Assert.False(File.Exists(backup));
                Assert.Equal("independent named-stream data", File.ReadAllText(stage + ":fixture"));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("encrypted backup")]
    [InlineData("new stream")]
    [InlineData("new mark")]
    [InlineData("existing mark writer")]
    [InlineData("existing attribute writer")]
    [InlineData("substituted backup")]
    [InlineData("substituted publication")]
    public void Legacy_cleanup_removes_only_the_reviewed_backup_and_records_retained_evidence(string change)
    {
        RequireLocalNtfs();
        if (change == "encrypted backup") RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-zip-cleanup-").FullName;
        string target = Path.Join(root, "original.zip");
        string journals = Path.Join(root, "journals");
        string moved = Path.Join(root, "moved-during-review.zip");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        const string lateMark = "[ZoneTransfer]\r\nZoneId=4\r\n";
        const string lateStream = "new named-stream data";
        IDisposable? metadataWriter = null;
        try
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(target);
            var files = new ChangeBeforeBackupCleanup((published, backup, fs) =>
            {
                switch (change)
                {
                    case "encrypted backup": File.Encrypt(backup); break;
                    case "new stream":
                        using (var stream = new FileStream(backup + ":fixture", FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (var writer = new StreamWriter(stream)) writer.Write(lateStream);
                        break;
                    case "new mark": Assert.True(fs.WriteOriginMark(backup, lateMark)); break;
                    case "existing mark writer":
                        metadataWriter = new FileStream(backup + ":Zone.Identifier", FileMode.Open, FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        break;
                    case "existing attribute writer":
                        metadataWriter = NtFile.OpenOrThrow(null, @"\??\" + backup, NtFile.ReadAttributes | NtFile.Synchronize | 0x10 /* FILE_WRITE_EA */,
                            NtFile.ShareAll, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, "Open owned ZIP for extended-attribute writes");
                        break;
                    case "substituted backup":
                    case "substituted publication":
                        string path = change == "substituted backup" ? backup : published;
                        File.Move(path, moved, overwrite: false);
                        File.Copy(moved, path, overwrite: false);
                        Assert.True(fs.WriteOriginMark(path, mark));
                        Assert.NotEqual(fs.GetFileIdentity(moved), fs.GetFileIdentity(path));
                        break;
                }
            });
            Assert.True(files.WriteOriginMark(target, mark));
            string id = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Legacy ZIP backup cleanup", "", [], []);
            bool kept = change != "unchanged";
            using (var journal = JobJournal.Create(journals, job))
            {
                new ZipUpdateExecutor(job, files, journal, replaceFile: LegacyReplace).Execute();
                metadataWriter?.Dispose();
                Assert.Equal(kept, journal.NeedsRecovery);
                if (!kept) journal.Finish(JobState.Completed, "Published owned ZIP and removed reviewed backup.");
            }

            string retained = Assert.IsType<string>(files.Backup);
            using var output = ZipFile.OpenRead(target);
            Assert.NotNull(output.GetEntry("new-folder/"));
            Assert.Equal(mark, files.ReadOriginMark(target));
            if (!kept)
            {
                Assert.False(File.Exists(retained));
                Assert.Empty(JournalRecovery.Scan(journals));
                return;
            }

            Assert.Equal(original, File.ReadAllBytes(retained));
            Assert.Equal(id, files.GetFileIdentity(change == "substituted backup" ? moved : retained));
            Assert.Equal(change == "encrypted backup", File.GetAttributes(retained).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(change == "new mark" ? lateMark : mark, files.ReadOriginMark(retained));
            if (change == "new stream") Assert.Equal(lateStream, File.ReadAllText(retained + ":fixture"));
            Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed && issue.Severity == IssueSeverity.Warning &&
                issue.Message.Contains(retained, StringComparison.Ordinal));
            var interrupted = Assert.Single(JournalRecovery.Scan(journals));
            Assert.True(interrupted.HasLegacyZipRecovery);
            var pending = Assert.Single(interrupted.OpenIntents);
            Assert.Equal(target, pending.Path);
            Assert.Equal(retained, pending.RecoveryPath);
            Assert.Empty(JournalRecovery.FindStagedLeftovers(interrupted));
        }
        finally { metadataWriter?.Dispose(); Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zip_cleanup_holds_both_files_against_protection_changes_through_the_commit_boundary(bool rollback)
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-zip-cleanup-commit-").FullName;
        string archive = Path.Join(root, "published.zip"), backup = Path.Join(root, "retained.bak");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        try
        {
            var fs = new WindowsFileOperations();
            var published = WriteReviewedZip(archive, "published fixture");
            var original = WriteReviewedZip(backup, "backup fixture");
            bool reachedCommit = false;

            string? reason = WindowsFileOperations.CleanupLegacyZipBackupCore(archive, backup, published, original, () =>
            {
                reachedCommit = true;
                foreach (string path in new[] { archive, backup })
                {
                    var encryption = Record.Exception(() => File.Encrypt(path));
                    Assert.True(encryption is IOException or UnauthorizedAccessException, encryption?.Message);
                    var streamCreation = Record.Exception(() =>
                    {
                        using var stream = new FileStream(path + ":arrived", FileMode.CreateNew, FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        stream.WriteByte(1);
                    });
                    Assert.True(streamCreation is IOException or UnauthorizedAccessException, streamCreation?.Message);
                }
                if (rollback) throw new IOException("Owned fixture stopped before commit.");
            });

            Assert.True(reachedCommit, reason);
            Assert.Equal(rollback, reason is not null);
            Assert.Equal(rollback, File.Exists(backup));
            Assert.Equal("published fixture", File.ReadAllText(archive));
            Assert.Equal(mark, fs.ReadOriginMark(archive));
            Assert.False(File.GetAttributes(archive).HasFlag(FileAttributes.Encrypted));
            Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(archive + ":arrived"));
            if (rollback)
            {
                Assert.Equal("backup fixture", File.ReadAllText(backup));
                Assert.Equal(mark, fs.ReadOriginMark(backup));
                Assert.False(File.GetAttributes(backup).HasFlag(FileAttributes.Encrypted));
                Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(backup + ":arrived"));
            }

            LegacyZipRollbackFile WriteReviewedZip(string path, string content)
            {
                File.WriteAllText(path, content);
                Assert.True(fs.WriteOriginMark(path, mark));
                byte[] bytes = File.ReadAllBytes(path);
                string id = Assert.IsType<string>(fs.GetFileIdentity(path));
                return new(new(id, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))), mark, Encrypted: false);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_update_checks_a_same_byte_archive_replacement_at_the_publication_boundary(bool insideCall)
    {
        RequireLocalNtfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-zip-publish-identity-").FullName;
        string target = Path.Join(root, "original.zip"), moved = Path.Join(root, "moved.zip");
        try
        {
            using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(target);
            var files = new WindowsFileOperations();
            string id = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), []);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "ZIP file identity", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            string? backup = null;
            void Substitute()
            {
                File.Move(target, moved, overwrite: false);
                File.Copy(moved, target, overwrite: false);
                Assert.NotEqual(id, files.GetFileIdentity(target));
                Assert.Equal(original, File.ReadAllBytes(target));
            }
            var publication = new LegacyZipPublicationTransaction(job, files, journal, plan, target, root,
                replaceFile: (stage, path, retained) =>
                {
                    Assert.True(insideCall);
                    Substitute();
                    File.Replace(stage, path, retained);
                    backup = retained;
                });
            using (var reader = new ArchiveMutationReadStream(target)) publication.CaptureOpenedSource(reader);
            using (var stage = publication.CreateStage())
            using (var zip = new ZipArchive(stage, ZipArchiveMode.Create, leaveOpen: true)) zip.CreateEntry("rebuilt.txt");
            using (var stage = publication.OpenStageRead()) publication.CaptureVerifiedStage(stage);
            publication.Prepare();
            if (!insideCall) Substitute();

            var failure = Assert.Throws<IOException>(() => publication.Publish(() => throw new InvalidOperationException("Unexpected commit.")));
            publication.RecordFailure(failure);
            publication.CleanupUnpublishedStage();
            Assert.Equal(insideCall, journal.NeedsRecovery);
            Assert.Equal(id, files.GetFileIdentity(moved));
            Assert.Equal(original, File.ReadAllBytes(moved));
            if (insideCall)
            {
                string retained = Assert.IsType<string>(backup);
                Assert.Equal(original, File.ReadAllBytes(retained));
                Assert.Contains(job.Issues, issue => issue.Message.Contains("a different file", StringComparison.Ordinal));
            }
            else
            {
                Assert.Null(backup);
                Assert.Contains("before replacement", failure.Message, StringComparison.Ordinal);
                Assert.Equal(original, File.ReadAllBytes(target));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("canceled before verification")]
    [InlineData("changed bytes")]
    [InlineData("new stream")]
    [InlineData("encrypted stage")]
    public void Legacy_abort_confirms_stage_cleanup_before_closing_recovery(string change)
    {
        RequireLocalNtfs();
        if (change == "encrypted stage") RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-legacy-stage-cleanup-").FullName;
        string archive = Path.Join(root, "original.zip"), journals = Path.Join(root, "journals");
        try
        {
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
            byte[] original = File.ReadAllBytes(archive);
            var plan = new ArchivePlan(archive, ArchiveBaseline.Of(archive), []);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Legacy ZIP aborted stage", "", [], []);
            bool kept = change is "changed bytes" or "new stream" or "encrypted stage";
            string stage;
            using (var journal = JobJournal.Create(journals, job))
            {
                var update = new LegacyZipPublicationTransaction(job, new WindowsFileOperations(), journal, plan, archive, root,
                    replaceFile: (prepared, _, _) =>
                    {
                        if (change == "changed bytes")
                        {
                            byte[] changed = File.ReadAllBytes(prepared);
                            changed[0] ^= 1;
                            File.WriteAllBytes(prepared, changed);
                        }
                        else if (change == "new stream") File.WriteAllText(prepared + ":late", "independent stream");
                        else if (change == "encrypted stage") File.Encrypt(prepared);
                        throw new IOException("Owned sharing refusal before replacement", unchecked((int)0x80070020));
                    });
                using (var source = new ArchiveMutationReadStream(archive)) update.CaptureOpenedSource(source);
                using (var target = update.CreateStage())
                using (var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true)) zip.CreateEntry("rebuilt.txt");
                stage = Assert.Single(Directory.GetFiles(root, ".filecat-zip-*.tmp"));
                Exception failure;
                if (change == "canceled before verification") failure = new OperationCanceledException("Owned cancellation during rebuild");
                else
                {
                    using (var verify = update.OpenStageRead()) update.CaptureVerifiedStage(verify);
                    update.Prepare();
                    failure = Assert.Throws<IOException>(() => update.Publish(() => throw new InvalidOperationException("Unexpected commit.")));
                }
                update.RecordFailure(failure);
                update.CleanupUnpublishedStage();
                Assert.Equal(kept, journal.NeedsRecovery);
                if (!kept) journal.Finish(JobState.Failed, "Owned stage cleaned after abort.");
            }

            Assert.Equal(original, File.ReadAllBytes(archive));
            Assert.Equal(kept, File.Exists(stage));
            if (change == "encrypted stage") Assert.True(File.GetAttributes(stage).HasFlag(FileAttributes.Encrypted));
            if (change == "new stream") Assert.Equal("independent stream", File.ReadAllText(stage + ":late"));
            if (kept)
            {
                var interrupted = Assert.Single(JournalRecovery.Scan(journals));
                Assert.True(interrupted.HasLegacyZipRecovery);
                Assert.Equal(stage, Assert.Single(interrupted.OpenIntents).Staged);
                Assert.Empty(JournalRecovery.FindStagedLeftovers(interrupted));
            }
            else Assert.Empty(JournalRecovery.Scan(journals));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ChangeBeforeBackupCleanup(Action<string, string, WindowsFileOperations> change) : WindowsFileOperations, ILegacyZipBackupRestorer
    {
        private readonly ILegacyZipBackupRestorer native = new WindowsFileOperations();
        public string? Backup { get; private set; }

        string? ILegacyZipBackupRestorer.CleanupLegacyZipBackup(string archive, string backup,
            LegacyZipRollbackFile published, LegacyZipRollbackFile original)
        {
            Backup = backup;
            change(archive, backup, this);
            return native.CleanupLegacyZipBackup(archive, backup, published, original);
        }
    }

    private sealed class RemovePublishedMark(string target) : WindowsFileOperations
    {
        public bool Removed { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            if (!Removed && path == target && Directory.GetFiles(Path.GetDirectoryName(path)!, ".filecat-zip-*.bak").Length == 1)
            {
                File.Delete(path + ":Zone.Identifier");
                Removed = true;
            }
            return base.ReadOriginMark(path);
        }
    }

    private sealed class ChangeStageDuringPreparation(string target, bool encrypt) : WindowsFileOperations
    {
        public string? Stage { get; private set; }
        public byte[]? StageBytes { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            if (Stage is null && path == target)
            {
                Stage = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, ".filecat-zip-*.tmp"));
                StageBytes = File.ReadAllBytes(Stage);
                if (encrypt) File.Encrypt(Stage);
                else
                {
                    StageBytes[0] ^= 1;
                    File.WriteAllBytes(Stage, StageBytes);
                }
            }
            return base.ReadOriginMark(path);
        }
    }

    private sealed class EncryptAfterPublication(string target, bool encryptOutput) : WindowsFileOperations
    {
        public bool Encrypted { get; private set; }

        public override string? GetFileIdentity(string path)
        {
            if (!Encrypted && path == target &&
                Directory.GetFiles(Path.GetDirectoryName(path)!, ".filecat-zip-*.bak").SingleOrDefault() is { } backup)
            {
                File.Encrypt(encryptOutput ? path : backup);
                Encrypted = true;
            }
            return base.GetFileIdentity(path);
        }
    }

    private sealed class EncryptDuringMarkRead(string target, bool encryptBackup) : WindowsFileOperations
    {
        public bool Encrypted { get; private set; }

        public override string? ReadOriginMark(string path)
        {
            bool matches = encryptBackup ? Path.GetExtension(path) == ".bak" : path == target;
            if (!Encrypted && matches)
            {
                File.Encrypt(path);
                Encrypted = true;
            }
            return base.ReadOriginMark(path);
        }
    }

    private static void RequireLocalNtfs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows native ZIP publication.");
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetTempPath())!);
        Assert.SkipUnless(drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.DriveFormat == "NTFS",
            "Requires a local NTFS test directory.");
    }

    private static void RequireEfs()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }
}
