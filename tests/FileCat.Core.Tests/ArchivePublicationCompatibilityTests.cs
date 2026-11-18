using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using System.IO.Compression;

namespace FileCat.Core.Tests;

public sealed class ArchivePublicationCompatibilityTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Zip)]
    [InlineData(ArchiveWriteFormat.Tar)]
    public void An_explicit_profile_cannot_enter_legacy_publication_or_create_a_legacy_transaction_intent(ArchiveWriteFormat format)
    {
        using var fixture = new TempDir();
        string archive = Path.Combine(fixture.Path, "owned.archive");
        var plan = new ArchivePlan(archive, null, [], Profile: new(format));
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "owned publication guard", "owned", [], []);
        using var journal = JobJournal.Create(fixture.Path, job);
        long before = new FileInfo(journal.Path).Length;
        var failure = Assert.Throws<ArchiveWriteException>(() =>
            new LegacyZipPublicationTransaction(job, new PortableFileOperations(), journal, plan, archive, fixture.Path));
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, failure.Reason);
        Assert.Equal(before, new FileInfo(journal.Path).Length);
        Assert.Empty(Directory.EnumerateFiles(fixture.Path, ".filecat-zip-*"));
        Assert.False(File.Exists(archive));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_replacement_records_the_backup_before_invocation_and_retains_partial_failures(bool partial)
    {
        using var fixture = new TempDir();
        string archive = Path.Combine(fixture.Path, "owned.zip");
        using (var original = ZipFile.Open(archive, ZipArchiveMode.Create))
            original.CreateEntry("original.txt");
        var originalBytes = File.ReadAllBytes(archive);
        string journals = fixture.Dir("journals");
        var plan = new ArchivePlan(archive, ArchiveBaseline.Of(archive), []);
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "Owned legacy ZIP", "", [], []);
        string? expectedBackup = null, expectedStage = null;
        using (var journal = JobJournal.Create(journals, job))
        {
            var update = new LegacyZipPublicationTransaction(job, new PortableFileOperations(), journal, plan,
                archive, fixture.Path, replaceFile: (stage, target, retained) =>
                {
                    expectedStage = stage;
                    expectedBackup = retained;
                    var reservation = Assert.Single(JobJournal.ReadRecords(journal.Path), row => row.Type == "replacement-recovery");
                    Assert.Equal(retained, reservation.Get("path"));
                    if (partial) File.Move(target, retained, overwrite: false);
                    throw new IOException(partial ? "Simulated partial replacement" : "Simulated sharing refusal",
                        partial ? unchecked((int)0x80070499) : unchecked((int)0x80070020));
                });
            using (var source = new ArchiveMutationReadStream(archive)) update.CaptureOpenedSource(source);
            using (var output = update.CreateStage())
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                zip.CreateEntry("rebuilt.txt");
            using (var stage = update.OpenStageRead()) update.CaptureVerifiedStage(stage);
            update.Prepare();

            var failure = Assert.Throws<IOException>(() => update.Publish(() => throw new InvalidOperationException("Publication was not expected.")));
            update.RecordFailure(failure);
            update.CleanupUnpublishedStage();
            Assert.Equal(partial, journal.NeedsRecovery);
            if (!partial) journal.Finish(JobState.Failed, failure.Message);
        }

        string stagePath = Assert.IsType<string>(expectedStage);
        string backupPath = Assert.IsType<string>(expectedBackup);
        if (!partial)
        {
            Assert.Equal(originalBytes, File.ReadAllBytes(archive));
            Assert.False(File.Exists(stagePath));
            Assert.False(File.Exists(backupPath));
            Assert.Empty(JournalRecovery.Scan(journals));
            return;
        }

        var interrupted = Assert.Single(JournalRecovery.Scan(journals));
        Assert.True(interrupted.HasLegacyZipRecovery);
        var pending = Assert.Single(interrupted.OpenIntents);
        Assert.Equal(archive, pending.Path);
        Assert.Equal(backupPath, pending.RecoveryPath);
        Assert.Equal(stagePath, pending.Staged);
        Assert.Empty(JournalRecovery.FindStagedLeftovers(interrupted));
        Assert.False(File.Exists(archive));
        Assert.Equal(originalBytes, File.ReadAllBytes(backupPath));
        Assert.True(File.Exists(stagePath));
    }
}
