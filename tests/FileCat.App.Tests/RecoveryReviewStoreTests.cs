using System.Text.Json;
using FileCat.App.Services;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class RecoveryReviewStoreTests
{
    [Fact]
    public void Remote_stage_hint_requires_a_well_formed_job_name_and_never_authorizes_deletion()
    {
        var job = new InterruptedJob("/state/job-20261021123456-deadbeef.fcj", "Move", "Upload", DateTime.UtcNow,
            ["/source"], "sftp:test:/target", [new PendingIntent(1, "upload-move", "/source", "/target/file", null)],
            [], 0, 1);
        Assert.Equal(".fc-deadbeef-", InterruptedRecoveryReport.ExpectedRemoteStagePrefix(job));
        string warning = InterruptedRecoveryReport.RemoteUploadReviewWarning(job);
        Assert.Contains("Candidate temporary names begin .fc-deadbeef-", warning);
        Assert.Contains("verify the server item before changing it", warning);
        Assert.Null(InterruptedRecoveryReport.ExpectedRemoteStagePrefix(job with { JournalPath = "/state/job-invalid.fcj" }));
        Assert.Equal(InterruptedRecoveryReport.RemoteUploadWarning,
            InterruptedRecoveryReport.RemoteUploadReviewWarning(job with { JournalPath = "/state/job-invalid.fcj" }));
    }

    [Fact]
    public void Remote_replacement_review_distinguishes_the_old_backup_from_the_incoming_stage()
    {
        var job = new InterruptedJob("/state/job-20261021123456-deadbeef.fcj", "Move", "Replace", DateTime.UtcNow,
            ["/source"], "sftp:test:/owned", [new PendingIntent(2, "remote-replace-backup", "/owned/file",
                "/owned/.fc-deadbeef-2", "/owned/.fc-deadbeef-1")], [], 1, 1);

        Assert.True(InterruptedRecoveryReport.HasUnreviewedRemoteUpload(job));
        Assert.Equal(["/owned/file"], InterruptedRecoveryReport.RemoteUploadTargets(job));
        string warning = InterruptedRecoveryReport.RemoteUploadReviewWarning(job);
        Assert.Contains("previous destination version", warning);
        Assert.Contains("do not delete either", warning);
        string report = InterruptedRecoveryReport.Format(job, "Inspect", [], [], new CopyReview([], [], false), false);
        Assert.Contains("/owned/.fc-deadbeef-2", report);
        Assert.Contains("/owned/.fc-deadbeef-1", report);
    }

    [Fact]
    public void Full_retained_report_preserves_paths_effects_and_unknown_remainder()
    {
        string source = "C:\\root\\name\twith-control";
        string target = "D:\\out\\file.txt";
        var job = new InterruptedJob("C:\\journal\\job-a.fcj", "Move", "Deep move", DateTime.UtcNow,
            [source], target,
            [new PendingIntent(1, JobJournal.RenameViaOp, source, target, null) { Via = "C:\\root\\temporary-name" }],
            [], 4, 3)
        {
            FillDirectories = Enumerable.Range(0, 10_004).Select(i => new FillDirectory($"source-{i}", $"target-{i}")).ToArray(),
            FillDirectoriesCut = true,
        };
        string report = InterruptedRecoveryReport.Format(job, "Reviewed without file changes", ["deleted-file"], ["unreadable-stage"],
            new CopyReview([new IncompleteCopy("incomplete", source, 2, DateTime.UtcNow, DateTime.UtcNow, null)],
                ["differing"], LimitReached: true), stagedLimit: true,
            deleted: ["deleted-file"], kept: ["kept-file"], renameEffects: ["temporary name retained"]);
        Assert.Contains(JsonSerializer.Serialize(source), report);
        Assert.Contains(JsonSerializer.Serialize(target), report);
        Assert.DoesNotContain(source, report); // the hidden tab remains reversible in JSON quoting
        Assert.Contains("temporary rename path", report);
        Assert.Contains("Deleted by cleanup: \"deleted-file\"", report);
        Assert.Contains("Reviewed staged file deleted by cleanup: \"deleted-file\"", report);
        Assert.DoesNotContain("Reviewed staged file kept at this snapshot: \"deleted-file\"", report);
        Assert.Contains("Kept after cleanup: \"kept-file\"", report);
        Assert.Contains("Recorded source roots: 1 retained in this report of 3 declared", report);
        Assert.Contains("omitted by this report's limit: ", report);
        Assert.Contains("Their count and paths are unknown", report);
        Assert.DoesNotContain("target-10003", report); // bounded detail list
        string unknown = InterruptedRecoveryReport.Format(job with { SourceCount = -1, FillDirectories = [] },
            "Inspect", [], [], new CopyReview([], [], false), stagedLimit: false);
        Assert.Contains("an unknown declared total", unknown);
        Assert.Contains("complete source-root count could not be reconstructed", unknown);
        string complete = InterruptedRecoveryReport.Format(job with { FillDirectories = [] }, "Inspect", [], [],
            new CopyReview([], [], false), stagedLimit: false, allSources: [source, "second root", "third root"]);
        Assert.Contains("all declared roots are represented", complete);
        Assert.Contains("Recorded source: \"third root\"", complete);
        Assert.DoesNotContain("source roots are not retained", complete);
    }

    [Fact]
    public void Durable_records_are_atomic_bounded_and_reject_an_oversize_update()
    {
        string root = Directory.CreateTempSubdirectory("fc-reviewed-recovery-").FullName;
        try
        {
            string journal = Path.Combine(root, "job-a.fcj");
            var pending = new RecoveryReviewRecord(journal, "A", "Copy", DateTime.UtcNow, "Review", "exact\ttext", false);
            RecoveryReviewStore.Save(root, pending);
            Assert.Equal(pending, Assert.Single(RecoveryReviewStore.Load(root)));
            Assert.Throws<InvalidDataException>(() => RecoveryReviewStore.Save(root,
                pending with { Report = new string('x', RecoveryReviewStore.MaxReportCharacters + 1) }));
            Assert.Equal(pending, Assert.Single(RecoveryReviewStore.Load(root)));
            var closed = pending with { ClosureConfirmed = true };
            RecoveryReviewStore.Save(root, closed);
            Assert.Equal(closed, Assert.Single(RecoveryReviewStore.Load(root)));
            File.SetLastWriteTimeUtc(Assert.Single(Directory.GetFiles(RecoveryReviewStore.DirectoryOf(root), "review-*.json")), DateTime.UtcNow.AddDays(-1));
            for (int i = 0; i < RecoveryReviewStore.KeepRecords; i++)
                RecoveryReviewStore.Save(root, closed with { JournalPath = Path.Combine(root, $"job-{i:D3}.fcj"), Title = $"{i:D3}" });
            var loaded = RecoveryReviewStore.Load(root);
            Assert.Equal(RecoveryReviewStore.KeepRecords, loaded.Count);
            Assert.DoesNotContain(loaded, r => r.JournalPath == journal);
            Assert.All(loaded, r => Assert.True(r.ClosureConfirmed));
            Assert.Empty(Directory.EnumerateFiles(RecoveryReviewStore.DirectoryOf(root), ".review-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }
}
