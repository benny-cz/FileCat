using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsStorageReviewEvidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bound_export_publishes_exact_bytes_for_live_prefix_and_inactive_raw_review(bool inactive)
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-export-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Join(root, "private")).FullName, leaf = CreateJournal(source);
            string target = Path.Join(Directory.CreateDirectory(Path.Join(root, "out")).FullName, "evidence.fcj");
            StorageReviewedJournal reviewed;
            using (var owner = WindowsStorageReviewEvidence.Open(source, leaf, Stop)) reviewed = JournalRecovery.ReadStorageReview(owner, Stop);
            StorageExportResult result;
            if (inactive)
            {
                using var owner = WindowsStorageReviewEvidence.Open(source, leaf, Stop);
                result = JournalRecovery.ExportStorageReview(owner, reviewed, target, Stop);
            }
            else result = JournalRecovery.ExportStorageCalls(Path.Join(source, leaf), reviewed.History.Prefix, target, WindowsStorageReviewEvidence.Backend, Stop);
            Assert.Equal(target, result.Destination); Assert.Equal(File.ReadAllBytes(Path.Join(source, leaf)), File.ReadAllBytes(target));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(target)!, ".filecat-storage-export-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Export_refuses_existing_read_only_file_and_private_namespace_without_creating_staging()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-export-refuse-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Join(root, "private")).FullName, leaf = CreateJournal(source);
            string folder = Directory.CreateDirectory(Path.Join(root, "out")).FullName, target = Path.Join(folder, "existing.fcj");
            File.WriteAllText(target, "unrelated existing data"); File.SetAttributes(target, FileAttributes.ReadOnly);
            try
            {
                using var history = WindowsStorageReviewEvidence.Backend.OpenHistory!(source, leaf, Stop);
                Assert.Throws<IOException>(() => history.CreateExport(target, Stop));
                Assert.Equal("unrelated existing data", File.ReadAllText(target)); Assert.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
                Assert.Throws<IOException>(() => history.CreateExport(Path.Join(source, "unwanted.fcj"), Stop));
                Assert.False(File.Exists(Path.Join(source, "unwanted.fcj")));
                Assert.Empty(Directory.EnumerateFiles(folder, ".filecat-storage-export-*.tmp"));
            }
            finally { File.SetAttributes(target, FileAttributes.Normal); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Publication_collision_preserves_the_competitor_and_removes_only_its_held_staging_file()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-export-collision-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Join(root, "private")).FullName, leaf = CreateJournal(source);
            string folder = Directory.CreateDirectory(Path.Join(root, "out")).FullName, target = Path.Join(folder, "winner.fcj");
            using var history = WindowsStorageReviewEvidence.Backend.OpenHistory!(source, leaf, Stop);
            using (var output = history.CreateExport(target, Stop))
            {
                output.Stream.Write("captured metadata"u8);
                string temporary = Assert.Single(Directory.EnumerateFiles(folder));
                Assert.ThrowsAny<IOException>(() => File.Move(temporary, Path.Join(folder, "substituted.tmp")));
                File.WriteAllText(target, "competing data");
                Assert.ThrowsAny<IOException>(() => output.Publish(Stop));
            }
            Assert.Equal("competing data", File.ReadAllText(target)); Assert.Equal(new[] { target }, Directory.EnumerateFiles(folder));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Cancel_before_publication_cleans_the_owned_object_and_releases_every_evidence_slot()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-export-cancel-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Join(root, "private")).FullName, leaf = CreateJournal(source);
            string folder = Directory.CreateDirectory(Path.Join(root, "out")).FullName, target = Path.Join(folder, "canceled.fcj");
            using (var history = WindowsStorageReviewEvidence.Backend.OpenHistory!(source, leaf, Stop))
            using (var output = history.CreateExport(target, Stop))
            {
                output.Stream.Write("partial captured metadata"u8);
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => output.Publish(canceled.Token));
            }
            Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Stop); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using (StorageEvidenceLanes.EnterEnvelope(deadline.Token)) { }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Export_does_not_follow_a_junction_into_the_private_evidence_directory()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-export-link-").FullName;
        string alias = Path.Join(root, "alias");
        try
        {
            string source = Directory.CreateDirectory(Path.Join(root, "private")).FullName, leaf = CreateJournal(source);
            new WindowsFileOperations().CreateLink(alias, source, LinkKind.Junction, true);
            using var history = WindowsStorageReviewEvidence.Backend.OpenHistory!(source, leaf, Stop);
            Assert.Throws<IOException>(() => history.CreateExport(Path.Join(alias, "unwanted.fcj"), Stop));
            Assert.Equal(new[] { Path.Join(source, leaf) }, Directory.EnumerateFiles(source));
        }
        finally { if (Directory.Exists(alias)) Directory.Delete(alias); Directory.Delete(root, true); }
    }

    [Fact]
    public void Export_can_publish_to_the_explicitly_prepared_FAT_or_exFAT_fixture()
    {
        RequireNormalWindows(); string? prepared = Environment.GetEnvironmentVariable("FILECAT_STORAGE_EXPORT_TEST_ROOT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(prepared), "Prepare a disposable writable FAT/exFAT fixture and set FILECAT_STORAGE_EXPORT_TEST_ROOT.");
        string root = Directory.CreateTempSubdirectory("filecat-export-fat-source-").FullName;
        string outputFolder = Directory.CreateDirectory(Path.Join(prepared!, "filecat-export-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            Assert.Contains(new DriveInfo(Path.GetPathRoot(outputFolder)!).DriveFormat, new[] { "FAT", "FAT32", "exFAT" });
            string leaf = CreateJournal(root), target = Path.Join(outputFolder, "metadata.fcj"); StorageReviewedJournal reviewed;
            using (var owner = WindowsStorageReviewEvidence.Open(root, leaf, Stop)) reviewed = JournalRecovery.ReadStorageReview(owner, Stop);
            var exported = JournalRecovery.ExportStorageCalls(Path.Join(root, leaf), reviewed.History.Prefix, target, WindowsStorageReviewEvidence.Backend, Stop);
            Assert.Equal(target, exported.Destination); Assert.Equal(File.ReadAllBytes(Path.Join(root, leaf)), File.ReadAllBytes(target));
            Assert.Equal(new[] { target }, Directory.EnumerateFiles(outputFolder));
        }
        finally { Directory.Delete(outputFolder, true); Directory.Delete(root, true); }
    }
}
