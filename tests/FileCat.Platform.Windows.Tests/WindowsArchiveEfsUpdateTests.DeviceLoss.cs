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
    public void Legacy_zip_keeps_review_evidence_when_the_volume_detaches_during_cleanup() =>
        RunZipDeviceLoss("FILECAT_TEST_LEGACY_ZIP_CLEANUP_DEVICE_LOSS", ZipDeviceLoss.Cleanup);

    [Fact]
    public void Hard_linked_ZIP_recovers_both_original_names_after_detachment_before_commit() =>
        RunZipDeviceLoss("FILECAT_TEST_HARD_LINKED_ZIP_DEVICE_LOSS_BEFORE", ZipDeviceLoss.BeforeCommit);

    [Fact]
    public void Hard_linked_ZIP_preserves_the_update_after_detachment_after_commit() =>
        RunZipDeviceLoss("FILECAT_TEST_HARD_LINKED_ZIP_DEVICE_LOSS_AFTER", ZipDeviceLoss.AfterCommit);

    private enum ZipDeviceLoss { Cleanup, BeforeCommit, AfterCommit }

    private static void RunZipDeviceLoss(string fixtureVariable, ZipDeviceLoss phase)
    {
        string? control = Environment.GetEnvironmentVariable(fixtureVariable);
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
        bool hardLinked = phase != ZipDeviceLoss.Cleanup;
        string target = Path.Join(folder, "owned.zip"), alias = Path.Join(folder, "alias.zip");
        const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create)) zip.CreateEntry("original.txt");
        RequireZipPublicationVolume(target);
        File.WriteAllText(target + ":Zone.Identifier", mark, Encoding.Unicode);
        File.WriteAllText(target + ":fixture", "original named stream");
        byte[] original = File.ReadAllBytes(target);
        byte[] originalMark = File.ReadAllBytes(target + ":Zone.Identifier");
        byte[] originalEas = [];
        if (hardLinked)
        {
            WriteZipTestEa(target);
            originalEas = ReadZipTestEas(target);
            Assert.NotEmpty(originalEas);
            Assert.True(CreateHardLink(alias, target, 0));
        }

        string evidence = Directory.CreateTempSubdirectory("filecat-zip-cleanup-loss-").FullName;
        try
        {
            int boundariesReached = 0;
            void Detach()
            {
                boundariesReached++;
                File.WriteAllText(Path.Join(control, "prepared"), "");
                WaitForFixture("detached");
                Assert.False(File.Exists(canary));
            }
            var files = new ZipDeviceLossFileOperations(phase, Detach);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            var plan = new ArchivePlan(target, ArchiveBaseline.Of(target), [new(ArchiveChangeKind.CreateFolderEntry, "new-folder")]);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "ZIP device-loss fixture", "", [], []);
            string journalPath;
            using (var journal = JobJournal.Create(evidence, job))
            {
                var executor = new ZipUpdateExecutor(job, files, journal);
                if (hardLinked)
                {
                    Assert.ThrowsAny<IOException>(executor.Execute);
                    Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Uncertain && issue.Severity == IssueSeverity.Error &&
                        issue.Message.Contains("could not verify the hard-linked ZIP publication", StringComparison.Ordinal));
                }
                else
                {
                    executor.Execute();
                    Assert.Contains(job.Issues, issue => issue.Outcome == StepOutcome.Committed &&
                        issue.Severity == IssueSeverity.Warning && issue.Message.Contains("ZIP update was published", StringComparison.Ordinal));
                }
                Assert.Equal(1, boundariesReached);
                Assert.True(journal.NeedsRecovery);
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

            var rebuilt = Assert.IsType<LegacyZipRollbackFile>(files.Rebuilt);
            string rebuiltPath = phase == ZipDeviceLoss.BeforeCommit ? stage : target;
            Assert.Equal(rebuilt.Version.Sha256, ArchiveBaseline.Of(rebuiltPath).Sha256Hex);
            Assert.Equal(originalMark, File.ReadAllBytes(rebuiltPath + ":Zone.Identifier"));
            Assert.Equal("original named stream", File.ReadAllText(rebuiltPath + ":fixture"));
            Assert.False(File.GetAttributes(rebuiltPath).HasFlag(FileAttributes.Encrypted));
            if (hardLinked)
            {
                Assert.Equal(rebuilt.Version.Identity, files.GetFileIdentity(rebuiltPath));
                Assert.Equal(originalEas, ReadZipTestEas(rebuiltPath));
                CheckOriginal(alias);
                Assert.Equal(originalEas, ReadZipTestEas(alias));
            }
            if (phase == ZipDeviceLoss.BeforeCommit)
            {
                CheckOriginal(target);
                Assert.False(File.Exists(snapshot));
            }
            else if (phase == ZipDeviceLoss.AfterCommit)
            {
                CheckOriginal(snapshot);
                Assert.False(File.Exists(stage));
            }
            else
            {
                Assert.Equal(identity, files.GetFileIdentity(target));
                Assert.Equal(File.ReadAllBytes(target), File.ReadAllBytes(stage));
                Assert.Equal(original, File.ReadAllBytes(snapshot));
                Assert.Equal(Encoding.UTF8.GetBytes(mark), File.ReadAllBytes(stage + ":Zone.Identifier"));
                Assert.Equal(originalMark, File.ReadAllBytes(snapshot + ":Zone.Identifier"));
                Assert.False(File.GetAttributes(stage).HasFlag(FileAttributes.Encrypted));
                Assert.False(File.GetAttributes(snapshot).HasFlag(FileAttributes.Encrypted));
            }
            using var archive = ZipFile.OpenRead(rebuiltPath);
            Assert.NotNull(archive.GetEntry("original.txt"));
            Assert.NotNull(archive.GetEntry("new-folder/"));
            var afterRemount = Assert.Single(JournalRecovery.Scan(evidence));
            Assert.True(afterRemount.HasLegacyZipRecovery);
            Assert.Empty(JournalRecovery.FindStagedLeftovers(afterRemount));

            void CheckOriginal(string path)
            {
                Assert.Equal(identity, files.GetFileIdentity(path));
                Assert.Equal(original, File.ReadAllBytes(path));
                Assert.Equal(originalMark, File.ReadAllBytes(path + ":Zone.Identifier"));
                Assert.Equal("original named stream", File.ReadAllText(path + ":fixture"));
                Assert.False(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
            }
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

    private sealed class ZipDeviceLossFileOperations(ZipDeviceLoss phase, Action detach) : WindowsFileOperations, ILegacyZipInPlacePublisher
    {
        private readonly ILegacyZipInPlacePublisher _native = new WindowsFileOperations();
        internal LegacyZipRollbackFile? Rebuilt { get; private set; }

        public SafeFileHandle? PinLegacyZipForPublication(SafeFileHandle file) => _native.PinLegacyZipForPublication(file);

        public LegacyZipInPlaceResult? TryPublishLegacyZipInPlace(string archive, string stage, string backup,
            LegacyZipRollbackFile original, SafeFileHandle originalPin, LegacyZipRollbackFile rebuilt, SafeFileHandle stagePin, Action checkpoint)
        {
            Rebuilt = rebuilt;
            return PublishLegacyZipInPlaceCore(archive, stage, backup, original, originalPin, rebuilt, stagePin, checkpoint,
                beforeCommit: phase == ZipDeviceLoss.BeforeCommit ? detach : null,
                beforeCleanupCommit: phase == ZipDeviceLoss.Cleanup ? detach : null,
                afterCommit: phase == ZipDeviceLoss.AfterCommit ? detach : null);
        }
    }
}
