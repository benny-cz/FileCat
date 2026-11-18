using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveEfsUpdateTests
{
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

            var refusal = Assert.Throws<ArchiveWriteException>(() => new ZipUpdateExecutor(job, files, journal).Execute());
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
        RequireEfs();
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

            var error = Record.Exception(() => new ZipUpdateExecutor(job, files, journal).Execute());
            string stage = Assert.IsType<string>(files.Stage);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Empty(Directory.GetFiles(root, "*.bak"));
            if (encrypt)
            {
                var refusal = Assert.IsType<ArchiveWriteException>(error);
                Assert.Equal(ArchiveWriteReason.PublicationUnavailable, refusal.Reason);
                Assert.Equal(ArchiveOuterEfs.UpdateStageUnavailable, refusal.Message);
                Assert.True(File.GetAttributes(stage).HasFlag(FileAttributes.Encrypted));
                Assert.Equal(files.StageBytes, File.ReadAllBytes(stage));
                Assert.Contains(job.Issues, issue => issue.Path == stage && issue.Message.Contains("kept", StringComparison.Ordinal));
                var cleanup = files.DeleteClearOwnedFile(stage, files.GetFileIdentity(stage)!);
                Assert.False(cleanup!.Deleted);
                Assert.True(File.Exists(stage));
            }
            else
            {
                Assert.Contains("changed after verification", Assert.IsType<IOException>(error).Message, StringComparison.Ordinal);
                Assert.False(File.Exists(stage));
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

            Assert.Throws<IOException>(() => new ZipUpdateExecutor(job, files, journal).Execute());
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

            var failure = Assert.Throws<IOException>(() => new ZipUpdateExecutor(job, files, journal).Execute());
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

    private static void RequireEfs()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }
}
