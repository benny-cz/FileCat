using System.IO.Compression;
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
