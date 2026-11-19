using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsStorageReviewEvidenceTests
{
    [Fact]
    public void Near_full_private_journals_preserve_review_and_release_space_only_after_acknowledgment()
    {
        RequireNormalWindows();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_SCALE") == "1",
            "Requires the disposable normal-user Windows NTFS storage scale fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-occupied-evidence-").FullName;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            Assert.Equal("NTFS", drive.DriveFormat);
            Assert.SkipUnless(drive.AvailableFreeSpace >= 400L * 1024 * 1024,
                "Requires room for four near-limit journals and one exact export.");
            string directory = Directory.CreateDirectory(Path.Join(root, "journals")).FullName;
            var timer = Stopwatch.StartNew();
            DateTime created = DateTime.UtcNow;
            var occupied = Enumerable.Range(0, 4).Select(i => SeedOccupiedHistory(directory, created.AddSeconds(i))).ToArray();
            double preparationMs = timer.Elapsed.TotalMilliseconds;
            long expectedBytes = occupied.Sum(fixture => fixture.Prefix.ByteLength);
            var backend = WindowsStorageReviewEvidence.Backend;
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), directory, ArchiveJobBackends.Empty,
                new NoStorageTransforms(), WindowsStorageReviewEvidence.CreateJournal, backend);

            timer.Restart();
            var headerPage = manager.ReadStorageHistoryPage(ct: Stop);
            double pageMs = timer.Elapsed.TotalMilliseconds;
            Assert.False(headerPage.More);
            Assert.Equal(4, headerPage.Examined);
            Assert.Equal(occupied.Select(fixture => fixture.Leaf).Order(), headerPage.Rows.Select(row => row.FileName).Order());
            Assert.InRange(headerPage.HeaderBytes, 1, 1024 * 1024);

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            timer.Restart();
            var full = manager.AccountStorageHistory(Stop);
            double censusMs = timer.Elapsed.TotalMilliseconds;
            long censusAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.True(full.Complete, full.Problem);
            Assert.Equal(4, full.PendingHistories);
            Assert.Equal(4, full.Journals);
            Assert.Equal(expectedBytes, full.EvidenceBytes);
            Assert.Equal(4 * StorageJournalPolicy.ReviewWriteBytes, full.ReservedReviewBytes);

            var budget = new StorageEvidenceBudget();
            timer.Restart();
            Assert.Throws<StorageEvidenceLimitException>(() => budget.Reserve(directory, Guid.NewGuid(), Stop, backend));
            double refusalMs = timer.Elapsed.TotalMilliseconds;
            Assert.All(occupied, fixture => Assert.True(File.Exists(Path.Join(directory, fixture.Leaf))));

            var selected = occupied[0];
            timer.Restart();
            var review = manager.ReadStorageReview(selected.Leaf, Stop);
            Assert.True(review.ReviewPending);
            Assert.True(review.Current.History.EffectUncertain);
            Assert.False(review.Current.History.Damaged);
            Assert.Equal(selected.Prefix, review.Current.History.Prefix);
            var tail = manager.ReadStorageCalls(selected.Leaf, selected.Prefix, selected.PendingCall - 2, Stop);
            Assert.False(tail.More);
            Assert.Equal(2, tail.Calls.Length);
            Assert.Equal(StorageCallDisposition.NotAttempted, tail.Calls[0].Disposition);
            Assert.Equal(selected.PendingCall, tail.Calls[1].Call);
            Assert.Null(tail.Calls[1].Disposition);
            double reviewMs = timer.Elapsed.TotalMilliseconds;

            string export = Path.Join(root, "full-prefix.fcj");
            timer.Restart();
            var copy = manager.ExportStorageCalls(selected.Leaf, selected.Prefix, export, Stop);
            double exportMs = timer.Elapsed.TotalMilliseconds;
            Assert.Equal(export, copy.Destination);
            using (var stream = File.OpenRead(export))
            {
                Assert.Equal(selected.Prefix.ByteLength, stream.Length);
                Assert.Equal(selected.Prefix.Sha256, Convert.ToHexString(SHA256.HashData(stream)));
            }

            timer.Restart();
            using (var owner = backend.OpenJournal(directory, selected.Leaf, Stop))
            {
                var acknowledged = JournalRecovery.AcknowledgeStorageReview(owner, review.Current, "Reviewed the exact occupied fixture.",
                    StorageReviewChoice.AcknowledgeReviewedHistory, Stop);
                Assert.False(acknowledged.ReviewPending);
            }
            double acknowledgmentMs = timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            using (budget.Reserve(directory, Guid.NewGuid(), Stop, backend))
            {
                Assert.False(File.Exists(Path.Join(directory, selected.Leaf)));
                Assert.All(occupied.Skip(1), fixture => Assert.True(File.Exists(Path.Join(directory, fixture.Leaf))));
            }
            double retryMs = timer.Elapsed.TotalMilliseconds;
            var remaining = manager.AccountStorageHistory(Stop);
            Assert.True(remaining.Complete, remaining.Problem);
            Assert.Equal(3, remaining.PendingHistories);
            Assert.Equal(3, remaining.Journals);
            Assert.Equal(2, remaining.Artifacts);
            Assert.Equal(3 * StorageJournalPolicy.ReviewWriteBytes, remaining.ReservedReviewBytes);

            TestContext.Current.TestOutputHelper!.WriteLine(
                $"Four journals used {expectedBytes:N0} bytes plus {full.ReservedReviewBytes:N0} bytes reserved. " +
                $"Seed {preparationMs:F1} ms, headers {headerPage.HeaderBytes:N0} bytes in {pageMs:F1} ms, " +
                $"census {censusMs:F1} ms with {censusAllocated:N0} allocated bytes, admission refused in {refusalMs:F1} ms. " +
                $"Review and exact tail {reviewMs:F1} ms, exact export {exportMs:F1} ms, acknowledgment {acknowledgmentMs:F1} ms, " +
                $"pruning and successful retry {retryMs:F1} ms. Remaining charge {remaining.ChargedBytes:N0} bytes.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static (string Leaf, StorageJournalPrefix Prefix, long PendingCall) SeedOccupiedHistory(string directory, DateTime created)
    {
        const string root = @"C:\storage-fixture";
        const long targetBytes = StorageJournalPolicy.JournalBytes - 3 * 1024 * 1024;
        string parent = root + "\\" + string.Join('\\', Enumerable.Repeat(new string('a', 240), 64));
        var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(1, "fixture-child"), false, null);
        var rootObservation = before with { Identity = new(1, "fixture-root"), IsDirectory = true };
        Guid job = Guid.NewGuid();
        string leaf = StorageJournalPolicy.FileName(created, job);
        using var native = StorageReviewResources.EnterNative(Stop);
        using var file = WindowsStorageReviewEvidence.CreateJournal(directory, leaf, Stop);
        var header = new StorageJournalHeader(job, created, StorageAction.Compress, StorageScope.ExistingDescendants, Guid.NewGuid(),
            [new(0, 0, root, rootObservation)]);
        // This is existing history for startup and retention. All simulated calls settle without a native effect
        // except the final intent. There is one fixture flush before any other reader opens the journal.
        using var writer = new StorageTransformJournalWriter(file.Stream, static () => { }, header);
        int name = 0;
        while (writer.Prefix!.ByteLength < targetBytes)
        {
            Stop.ThrowIfCancellationRequested();
            var call = writer.FlushIntent(0, parent + $"\\{name++:D5}.bin", before);
            writer.FlushNotAttempted(call, StorageNoCallReason.StateChanged);
        }
        var pending = writer.FlushIntent(0, parent + $"\\{name:D5}.bin", before);
        file.Stream.Flush(flushToDisk: true);
        var prefix = writer.Prefix!;
        string path = Path.Join(directory, leaf);
        Assert.Equal(prefix.ByteLength, new FileInfo(path).Length);
        Assert.Equal((FileAttributes)0, File.GetAttributes(path) & (FileAttributes.SparseFile | FileAttributes.Compressed));
        return (leaf, prefix, pending.Intent.Call);
    }
}
