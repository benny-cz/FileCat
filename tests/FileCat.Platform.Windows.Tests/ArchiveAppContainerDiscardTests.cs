using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    // Plan §7.1/§10.3 on NTFS with the real confined workers: cancelling during the rebuild leaves the original unchanged and,
    // through the exact discard of every artifact by its recorded identity, nothing beside the archive and no journal for review.
    [Theory, InlineData(false), InlineData(true)]
    public async Task Cancelling_during_the_rebuild_discards_every_artifact_beside_the_archive(bool update)
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        string root = Directory.CreateTempSubdirectory("filecat-native-discard-").FullName; var ct = TestContext.Current.CancellationToken;
        try
        {
            string folder = Path.Combine(root, "archives"); Directory.CreateDirectory(folder);
            string small = Path.Combine(root, "small.txt"); File.WriteAllText(small, "small"); File.SetLastWriteTimeUtc(small, DateTime.UnixEpoch.AddSeconds(1700000000));
            // Incompressible input keeps LZMA busy long enough to cancel inside the rebuild.
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[24 << 20]; new Random(811).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = ArchiveNativeJobCases.Profile(ArchiveWriteFormat.SevenZip, 0);
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals, new([WindowsArchiveJobBackend.Create(profile.Format, executable)]));
            string target = Path.Combine(folder, update ? "kept.7z" : "new.7z");
            byte[]? original = null; ArchivePlan plan;
            if (update)
            {
                await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "small.txt", small)], Profile: profile), null));
                original = File.ReadAllBytes(target);
                plan = (await manager.ReviewArchiveUpdateAsync(target, profile, null, ct)).CreatePlan([new(ArchiveChangeKind.AddFile, "big.bin", big)]);
            }
            else plan = new(target, null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile);
            var job = manager.SubmitArchive(plan, null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(180));
                while (job.ArchivePhase != ArchiveJobPhase.Rebuilding && !job.State.IsFinished()) await Task.Delay(2, deadline.Token);
                job.Cancel();
                while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            }
            string issues = string.Join(" | ", job.Issues.Select(issue => issue.Message));
            Assert.True(job.State == JobState.Canceled, job.State + ": " + issues);
            Assert.DoesNotContain(job.Issues, issue => issue.Message.Contains("kept them for review", StringComparison.Ordinal));
            if (update) Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Equal(update ? new[] { "kept.7z" } : [], Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).ToArray());
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // The recovery route on NTFS: a cancelled job that kept its files (retained-artifact mode) leaves an unpublished
    // transaction; a fresh recovery lease reopens each artifact by its recorded file ID, deletes it through that handle and
    // closes the journal. Nothing else beside the archive is touched.
    [Fact]
    public async Task Recovery_discards_a_kept_unpublished_transaction_by_recorded_identity()
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        string root = Directory.CreateTempSubdirectory("filecat-native-discard-recovery-").FullName; var ct = TestContext.Current.CancellationToken;
        try
        {
            string folder = Path.Combine(root, "archives"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "neighbour.txt"), "unrelated");
            string big = Path.Combine(root, "big.bin"); var bytes = new byte[24 << 20]; new Random(812).NextBytes(bytes); File.WriteAllBytes(big, bytes);
            File.SetLastWriteTimeUtc(big, DateTime.UnixEpoch.AddSeconds(1700000001));
            var profile = ArchiveNativeJobCases.Profile(ArchiveWriteFormat.SevenZip, 0);
            string journals = Path.Combine(root, "journals");
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), journals,
                new([WindowsArchiveJobBackend.Create(profile.Format, executable, retainArtifacts: true)]));
            var job = manager.SubmitArchive(new(Path.Combine(folder, "new.7z"), null, [new(ArchiveChangeKind.AddFile, "big.bin", big)], Profile: profile), null);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(180));
                while (job.ArchivePhase != ArchiveJobPhase.Rebuilding && !job.State.IsFinished()) await Task.Delay(2, deadline.Token);
                job.Cancel();
                while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
            }
            Assert.True(job.State == JobState.Canceled, job.State + ": " + string.Join(" | ", job.Issues.Select(issue => issue.Message)));
            // Kept for review and reported with a location; the journal stays open.
            Assert.Contains(job.Issues, issue => issue.Message.Contains("kept them for review", StringComparison.Ordinal) && issue.TargetPath is not null);
            var interrupted = Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
            var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(interrupted.JournalPath).Transactions);
            Assert.True(ArchiveDiscardFacts.IsDiscardable(facts));
            Assert.True(File.Exists(facts.StagePath));
            Assert.Equal(1 + facts.Sidecars.Count(sidecar => sidecar.Identity is not null), Directory.GetFiles(folder).Length - 1);

            using (var discard = new ArchiveTransactionDiscard(WindowsArchiveDiscard.OpenRecovery(interrupted.JournalPath, facts.Nonce, ct)))
            {
                Assert.Empty(discard.Observe().Absent);
                Assert.True(discard.Execute(ct).IsResolved);
                discard.Finish("Recovery removed the unfinished files of a cancelled creation.");
            }
            Assert.Equal(new[] { "neighbour.txt" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).ToArray());
            Assert.Equal("unrelated", File.ReadAllText(Path.Combine(folder, "neighbour.txt")));
            Assert.Empty(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
