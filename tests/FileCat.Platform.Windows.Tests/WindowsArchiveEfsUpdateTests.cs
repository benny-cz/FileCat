using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveEfsUpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Archive_update_refuses_a_target_encrypted_after_review_before_staging(bool native)
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
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
}
