using System.Collections.Immutable;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageHistoryDiscoveryTests
{
    [Fact]
    public void Detached_pages_resume_after_the_exact_anchor_without_retaining_a_cursor_or_old_headers()
    {
        using var temp = new TempDir(); var ct = TestContext.Current.CancellationToken;
        var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(7, "source"), false, null);
        for (int i = 0; i < 130; i++)
        {
            Guid id = Guid.NewGuid(); var header = new StorageJournalHeader(id, DateTime.UnixEpoch, StorageAction.Compress,
                StorageScope.SelectedItems, Guid.NewGuid(), [new(0, 0, @"C:\source\file", before)]);
            File.WriteAllBytes(Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, id)),
                StorageTransformJournalWriter.Encode("storage-begin", 1, id, header));
        }
        var first = JournalRecovery.ReadStorageHistoryPage(temp.Path, ct: ct);
        Assert.Equal(64, first.Rows.Length); Assert.NotNull(first.Continuation); Assert.True(first.More);
        // Acquiring the sole lane here proves that the page returned after native/managed cursor retirement.
        using (StorageEvidenceLanes.EnterCursor(ct)) { }
        var second = JournalRecovery.ReadStorageHistoryPage(temp.Path, first.Continuation, ct: ct);
        Assert.Equal(64, second.Rows.Length); Assert.InRange(second.HeaderBytes, 1, 1024 * 1024); Assert.Equal(64, second.ReplayedEntries);
        var third = JournalRecovery.ReadStorageHistoryPage(temp.Path, second.Continuation, ct: ct);
        Assert.Equal(2, third.Rows.Length); Assert.False(third.More); Assert.Equal(128, third.ReplayedEntries);
        Assert.Equal(130, first.Rows.Concat(second.Rows).Concat(third.Rows).Select(r => r.FileName).Distinct().Count());
        File.Delete(Path.Join(temp.Path, first.Continuation.FileName));
        Assert.Throws<IOException>(() => JournalRecovery.ReadStorageHistoryPage(temp.Path, first.Continuation, ct: ct));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => JournalRecovery.ReadStorageHistoryPage(temp.Path, second.Continuation, ct: stop.Token));
    }

    [Fact]
    public void Retained_page_descriptors_do_not_read_reports_and_ignore_temporary_or_non_ack_artifacts()
    {
        using var temp = new TempDir(); string directory = Directory.CreateDirectory(Path.Join(temp.Path, "reviewed")).FullName;
        Guid id = Guid.NewGuid(); string ack = StorageReviewNames.Published(id, "ack", Guid.NewGuid());
        foreach (var leaf in new[] { ack, StorageReviewNames.Published(id, "report", Guid.NewGuid()), StorageReviewNames.Temporary(id, "ack", Guid.NewGuid()) })
            File.WriteAllText(Path.Join(directory, leaf), "Unread arbitrary bytes; selection must verify the actual pair later.");
        var page = JournalRecovery.ReadStorageHistoryPage(temp.Path, retainedReviews: true, ct: TestContext.Current.CancellationToken);
        var row = Assert.Single(page.Rows); Assert.Equal(ack, row.FileName); Assert.True(row.RetainedReview);
        Assert.Equal(0, page.HeaderBytes); Assert.Contains("verification required", row.Status); Assert.False(page.More);
    }
    [Fact]
    public void Unrecognized_names_directories_and_mismatched_job_identity_remain_visible_as_damaged()
    {
        using var temp = new TempDir();
        var observation = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(7, "sample"), false, null);
        Guid recorded = Guid.NewGuid();
        var header = new StorageJournalHeader(recorded, DateTime.UnixEpoch, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(),
            [new(0, 0, @"C:\source\file", observation)]);
        File.WriteAllBytes(Path.Join(temp.Path, "job-Storage-unknown.tmp"), StorageTransformJournalWriter.Encode("storage-begin", 1, recorded, header));
        string mismatch = Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, Guid.NewGuid()));
        File.WriteAllBytes(mismatch, StorageTransformJournalWriter.Encode("storage-begin", 1, recorded, header));
        Directory.CreateDirectory(Path.Join(temp.Path, "job-storage-unprovable-directory"));
        using var discovery = JournalRecovery.OpenStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        var page = discovery.ReadPage(TestContext.Current.CancellationToken);
        Assert.Equal(3, page.Rows.Length); Assert.All(page.Rows, row => Assert.Contains("damaged", row.Status));
        Assert.True(JournalRecovery.ReadStorageHistory(mismatch, ct: TestContext.Current.CancellationToken).Damaged);
        discovery.Dispose(); // the independent census owns the one cursor only after discovery has yielded it
        Assert.False(JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken).Complete);
        _ = JournalRecovery.Scan(temp.Path, keepFinished: 0, maxAge: TimeSpan.Zero);
        Assert.True(File.Exists(Path.Join(temp.Path, "job-Storage-unknown.tmp")));
    }

    [Fact]
    public void History_is_incremental_unsorted_and_header_damage_remains_a_visible_descriptor()
    {
        using var temp = new TempDir();
        var observation = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(7, "sample"), false, null);
        for (int i = 0; i < 130; i++)
        {
            Guid id = Guid.NewGuid();
            var header = new StorageJournalHeader(id, DateTime.UnixEpoch, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(),
                [new(0, 0, @"C:\source\file", observation)]);
            File.WriteAllBytes(Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, id)),
                i == 0 ? [] : StorageTransformJournalWriter.Encode("storage-begin", 1, id, header));
        }
        using var discovery = JournalRecovery.OpenStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        var names = new HashSet<string>(StringComparer.Ordinal); bool sawDamaged = false; int pages = 0;
        while (true)
        {
            var page = discovery.ReadPage(TestContext.Current.CancellationToken); pages++;
            Assert.InRange(page.Examined, 0, 64); Assert.InRange(page.HeaderBytes, 0, 1024 * 1024); Assert.InRange(page.Rows.Length, 0, 64);
            foreach (var row in page.Rows)
            {
                Assert.True(names.Add(row.FileName)); Assert.True(row.FileName.Length < 256); Assert.DoesNotContain(@"C:\source", row.Status);
                if (row.JobId is null) { sawDamaged = true; Assert.Contains("damaged", row.Status); }
                else Assert.Contains("verification", row.Status);
            }
            if (!page.More) break;
        }
        Assert.Equal(130, names.Count); Assert.True(sawDamaged); Assert.True(pages >= 3);
        _ = JournalRecovery.Scan(temp.Path, keepFinished: 0, maxAge: TimeSpan.Zero);
        Assert.Equal(130, Directory.EnumerateFiles(temp.Path).Count());
    }

    private static byte[] Begin(Guid id, int pathLength = 0)
    {
        var observation = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(7, "sample"), false, null);
        ImmutableArray<StorageJournalRoot> roots = pathLength == 0 ? [new StorageJournalRoot(0, 0, @"C:\source\file", observation)]
            : Enumerable.Range(0, 64).Select(i => new StorageJournalRoot(i, i, @"C:\" + new string('a', pathLength) + @"\file" + i,
                observation with { Identity = new(7, "large-" + i) })).ToImmutableArray();
        return StorageTransformJournalWriter.Encode("storage-begin", 1, id, new StorageJournalHeader(id, DateTime.UnixEpoch, StorageAction.Compress,
            StorageScope.SelectedItems, Guid.NewGuid(), roots));
    }

    [Fact]
    public void The_header_byte_slice_ends_a_page_before_64_candidates_and_later_pages_still_discover_every_history()
    {
        using var temp = new TempDir(); var ct = TestContext.Current.CancellationToken; int size = 0;
        for (int i = 0; i < 10; i++)
        {
            Guid id = Guid.NewGuid(); byte[] begin = Begin(id, pathLength: 3300); size = begin.Length;
            File.WriteAllBytes(Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, id)), begin);
        }
        // Each header is individually valid, but four of them exceed the slice's remaining header-byte allowance.
        Assert.InRange(size, (1024 * 1024 - StorageJournalPolicy.RecordBytes) / 4 + 1, StorageJournalPolicy.RecordBytes - 1);
        using var discovery = JournalRecovery.OpenStorageHistory(temp.Path, ct);
        var names = new HashSet<string>(StringComparer.Ordinal); int pages = 0;
        while (true)
        {
            var page = discovery.ReadPage(ct); pages++;
            Assert.InRange(page.HeaderBytes, 0, 1024 * 1024); Assert.Equal(page.Examined, page.Rows.Length);
            if (page.More)
            {
                // The 1 MiB header slice, not the 64-candidate bound, ended this page.
                Assert.InRange(page.Examined, 1, 63); Assert.True(page.HeaderBytes > 1024 * 1024 - StorageJournalPolicy.RecordBytes);
            }
            foreach (var row in page.Rows)
            {
                Assert.True(names.Add(row.FileName)); Assert.NotNull(row.JobId); Assert.Equal(64, row.RootCount);
                Assert.Contains("verification", row.Status); Assert.DoesNotContain(@"C:\", row.Status);
            }
            if (!page.More) break;
        }
        Assert.Equal(10, names.Count); Assert.Equal(3, pages);
    }

    [Fact]
    public void An_oversized_first_record_is_a_bounded_damaged_row_and_stays_charged_pending_history()
    {
        using var temp = new TempDir(); var ct = TestContext.Current.CancellationToken;
        string oversized = Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, Guid.NewGuid()));
        byte[] bytes = new byte[StorageJournalPolicy.RecordBytes + 64 * 1024];
        Array.Fill(bytes, (byte)'x'); Encoding.ASCII.GetBytes("00000000 {\"t\":\"storage-begin\",\"data\":\"").CopyTo(bytes, 0); bytes[^1] = (byte)'\n';
        File.WriteAllBytes(oversized, bytes);
        for (int i = 0; i < 2; i++)
        {
            Guid id = Guid.NewGuid(); File.WriteAllBytes(Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, id)), Begin(id));
        }
        using (var discovery = JournalRecovery.OpenStorageHistory(temp.Path, ct))
        {
            var page = discovery.ReadPage(ct);
            Assert.False(page.More); Assert.Equal(3, page.Rows.Length);
            var row = Assert.Single(page.Rows, r => r.FileName == Path.GetFileName(oversized));
            Assert.Null(row.JobId); Assert.Null(row.RootCount); Assert.Contains("damaged", row.Status);
            Assert.Equal(2, page.Rows.Count(r => r.JobId is not null));
            // Only the 256 KiB record bound was read from the oversized history, not its whole first line.
            Assert.InRange(page.HeaderBytes, StorageJournalPolicy.RecordBytes, StorageJournalPolicy.RecordBytes + 8 * 1024);
        }
        var history = JournalRecovery.ReadStorageHistory(oversized, ct: ct);
        Assert.True(history.Damaged); Assert.True(history.LimitReached); Assert.True(history.ReviewPending);
        var account = JournalRecovery.AccountStorageHistory(temp.Path, ct);
        Assert.True(account.Complete, account.Problem); Assert.Equal(3, account.Journals); Assert.Equal(3, account.PendingHistories);
        Assert.Equal(Directory.EnumerateFiles(temp.Path).Sum(p => new FileInfo(p).Length), account.EvidenceBytes);
        Assert.Equal(3 * StorageJournalPolicy.ReviewWriteBytes, account.ReservedReviewBytes);
        _ = JournalRecovery.Scan(temp.Path, keepFinished: 0, maxAge: TimeSpan.Zero);
        Assert.Equal(bytes, File.ReadAllBytes(oversized));
    }
}
