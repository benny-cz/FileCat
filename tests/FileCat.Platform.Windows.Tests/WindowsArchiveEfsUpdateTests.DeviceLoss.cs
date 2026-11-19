using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveEfsUpdateTests
{
    [Fact]
    public void Legacy_zip_keeps_review_evidence_when_the_volume_detaches_during_cleanup()
    {
        string? control = Environment.GetEnvironmentVariable("FILECAT_TEST_LEGACY_ZIP_CLEANUP_DEVICE_LOSS");
        if (string.IsNullOrWhiteSpace(control)) Assert.Skip("Requires the isolated NTFS virtual disk device-loss fixture.");
        RequireLocalNtfs();
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(90));

        string canary = File.ReadAllText(Path.Join(control, "target.txt")).Trim();
        var drive = new DriveInfo(Path.GetPathRoot(canary)!);
        Assert.Equal("FCDeviceLoss", drive.VolumeLabel);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.InRange(drive.TotalSize, 32L << 20, 320L << 20);
        string folder = Path.GetDirectoryName(canary)!;
        string target = Path.Join(folder, "owned-archive.zip");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
        RequireZipPublicationVolume(target);
        File.WriteAllText(target + ":Zone.Identifier", mark, Encoding.Unicode);
        File.WriteAllText(target + ":fixture", "original named stream");
        byte[] original = File.ReadAllBytes(target);
        byte[] originalMark = File.ReadAllBytes(target + ":Zone.Identifier");

        string evidence = Directory.CreateTempSubdirectory("filecat-zip-cleanup-loss-").FullName;
        try
        {
            var files = new CleanupBoundaryFileOperations(() =>
            {
                File.WriteAllText(Path.Join(control, "prepared"), "");
                WaitForFixture("detached");
            });
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "ZIP cleanup device-loss fixture", "", [], []);
            string journalPath;
            using (var journal = JobJournal.Create(evidence, job))
            {
                new ZipUpdateExecutor(job, files, journal).Execute();
                Assert.Equal(1, files.CleanupBoundariesReached);
                Assert.True(journal.NeedsRecovery);
                Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed &&
                    issue.Severity == IssueSeverity.Warning && issue.Message.Contains("ZIP update was published", StringComparison.Ordinal));
                journalPath = journal.Path;
            }

            var pending = Assert.Single(JournalRecovery.Scan(evidence));
            Assert.True(pending.HasLegacyZipRecovery);
            var intent = Assert.Single(pending.OpenIntents);
            Assert.Equal(target, intent.Path);
            string stage = Assert.IsType<string>(intent.Staged);
            string snapshot = Assert.IsType<string>(intent.RecoveryPath);
            Assert.Equal(folder, Path.GetDirectoryName(stage));
            Assert.Equal(folder, Path.GetDirectoryName(snapshot));
            Assert.Empty(JournalRecovery.FindStagedLeftovers(pending));
            File.Copy(journalPath, Path.Join(control, "interrupted.journal"));
            File.WriteAllText(Path.Join(control, "retired"), "");
            WaitForFixture("reattached");

            Assert.Equal(identity, files.GetFileIdentity(target));
            byte[] published = File.ReadAllBytes(target);
            Assert.NotEqual(original, published);
            Assert.Equal(published, File.ReadAllBytes(stage));
            Assert.Equal(original, File.ReadAllBytes(snapshot));
            Assert.Equal(originalMark, File.ReadAllBytes(target + ":Zone.Identifier"));
            Assert.Equal(Encoding.UTF8.GetBytes(mark), File.ReadAllBytes(stage + ":Zone.Identifier"));
            Assert.Equal(originalMark, File.ReadAllBytes(snapshot + ":Zone.Identifier"));
            Assert.Equal("original named stream", File.ReadAllText(target + ":fixture"));
            Assert.All(new[] { target, stage, snapshot }, path => Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted)));
            using var archive = ZipFile.OpenRead(target);
            Assert.NotNull(archive.GetEntry("original.txt"));
            Assert.NotNull(archive.GetEntry("new-folder/"));
            Assert.True(Assert.Single(JournalRecovery.Scan(evidence)).HasLegacyZipRecovery);
        }
        finally { Directory.Delete(evidence, recursive: true); }

        void WaitForFixture(string marker)
        {
            while (!File.Exists(Path.Join(control, marker)))
            {
                stop.Token.ThrowIfCancellationRequested();
                if (File.Exists(Path.Join(control, "aborted"))) throw new IOException("The isolated device-loss fixture could not proceed.");
                stop.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private sealed class CleanupBoundaryFileOperations(Action beforeCleanupCommit) : WindowsFileOperations, ILegacyZipInPlacePublisher
    {
        private readonly ILegacyZipInPlacePublisher _native = new WindowsFileOperations();
        internal int CleanupBoundariesReached { get; private set; }

        public SafeFileHandle? PinLegacyZipForPublication(SafeFileHandle file) => _native.PinLegacyZipForPublication(file);

        public LegacyZipInPlaceResult? TryPublishLegacyZipInPlace(string archive, string stage, string backup,
            LegacyZipRollbackFile original, SafeFileHandle originalPin, LegacyZipRollbackFile rebuilt, SafeFileHandle stagePin, Action checkpoint) =>
            PublishLegacyZipInPlaceCore(archive, stage, backup, original, originalPin, rebuilt, stagePin, checkpoint,
                beforeCommit: null, beforeCleanupCommit: () =>
                {
                    CleanupBoundariesReached++;
                    beforeCleanupCommit();
                });
    }
}
