using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageEvidenceAccountingTests
{
    private static string Header(string directory, Guid id, bool clean = false)
    {
        var observation = new StorageObservation(StorageObservationState.Enabled, StorageObservationState.Disabled, new(1, id.ToString("N")), false, null);
        var header = new StorageJournalHeader(id, DateTime.UnixEpoch, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(),
            [new(0, 0, @"C:\source\file", observation)]);
        string path = Path.Join(directory, StorageJournalPolicy.FileName(DateTime.UnixEpoch, id));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        using var writer = new StorageTransformJournalWriter(stream, () => stream.Flush(true), header);
        if (clean) writer.Finish(new(header.Action, header.Scope, header.ReviewRevision, new(AlreadySatisfied: 1))
        { Discovery = StorageCompleteness.Complete, Roots = [new(0, observation.Identity, StorageCompleteness.Complete, false)] });
        return path;
    }

    [Fact]
    public void Restart_reserves_review_capacity_for_pending_and_damaged_evidence_but_not_clean_history()
    {
        using var temp = new TempDir();
        Header(temp.Path, Guid.NewGuid()); Header(temp.Path, Guid.NewGuid(), clean: true);
        string damaged = Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch, Guid.NewGuid()));
        File.WriteAllText(damaged, "torn record");
        var account = JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        Assert.True(account.Complete, account.Problem); Assert.Equal(3, account.Journals); Assert.Equal(2, account.PendingHistories);
        Assert.Equal(2 * StorageJournalPolicy.ReviewWriteBytes, account.ReservedReviewBytes);
        Assert.Equal(Directory.EnumerateFiles(temp.Path).Sum(p => new FileInfo(p).Length), account.EvidenceBytes);
    }

    [Fact]
    public void Associated_crash_artifacts_count_once_against_physical_bytes_and_remaining_review_reservation()
    {
        using var temp = new TempDir(); Guid id = Guid.NewGuid(); Header(temp.Path, id);
        string reviewed = Path.Join(temp.Path, "reviewed"); Directory.CreateDirectory(reviewed);
        string artifact = Path.Join(reviewed, $".storage-{id:N}-report-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(artifact, new byte[1007]);
        var account = JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        Assert.True(account.Complete, account.Problem); Assert.Equal(1, account.Artifacts);
        Assert.Equal(StorageJournalPolicy.ReviewWriteBytes - 1007, account.ReservedReviewBytes);
        Assert.Equal(new FileInfo(Directory.EnumerateFiles(temp.Path).Single()).Length + StorageJournalPolicy.ReviewWriteBytes, account.ChargedBytes);
        File.WriteAllBytes(Path.Join(reviewed, ".storage-unrecognized.tmp"), [1, 2, 3]);
        account = JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        Assert.False(account.Complete); Assert.Equal(2, account.Artifacts); Assert.Contains("Unassociated", account.Problem);
    }

    [Fact]
    public void A_crash_leftover_larger_than_the_review_reserve_is_charged_in_full_once_without_a_negative_reservation()
    {
        using var temp = new TempDir(); Guid pending = Guid.NewGuid(), finished = Guid.NewGuid();
        Header(temp.Path, pending); Header(temp.Path, finished, clean: true);
        string reviewed = Directory.CreateDirectory(Path.Join(temp.Path, "reviewed")).FullName;
        long large = StorageJournalPolicy.ReviewWriteBytes + 4096;
        using (var file = File.Create(Path.Join(reviewed, StorageReviewNames.Temporary(pending, "report", Guid.NewGuid())))) file.SetLength(large);
        File.WriteAllBytes(Path.Join(reviewed, StorageReviewNames.Temporary(finished, "ack", Guid.NewGuid())), new byte[512]);
        var account = JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        Assert.True(account.Complete, account.Problem); Assert.Equal(2, account.Journals); Assert.Equal(1, account.PendingHistories); Assert.Equal(2, account.Artifacts);
        Assert.Equal(0, account.ReservedReviewBytes); // the pending history's leftover already covers its whole review allowance
        long journals = Directory.EnumerateFiles(temp.Path).Sum(p => new FileInfo(p).Length);
        Assert.Equal(journals + large + 512, account.EvidenceBytes); Assert.Equal(account.EvidenceBytes, account.ChargedBytes);
    }

    [Fact]
    public void Duplicate_job_names_and_orphan_artifacts_cannot_make_accounting_eligible_for_admission()
    {
        using var temp = new TempDir(); Guid id = Guid.NewGuid(); string original = Header(temp.Path, id);
        string duplicate = Path.Join(temp.Path, StorageJournalPolicy.FileName(DateTime.UnixEpoch.AddSeconds(1), id));
        File.Copy(original, duplicate);
        Assert.False(JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken).Complete);
        Assert.False(StorageEvidenceAccounting.Census(temp.Path, new HashSet<Guid> { id }, TestContext.Current.CancellationToken).Complete);
        File.Delete(duplicate);
        string reviewed = Path.Join(temp.Path, "reviewed"); Directory.CreateDirectory(reviewed);
        string orphan = Path.Join(reviewed, $"storage-{Guid.NewGuid():N}-report-{Guid.NewGuid():N}.json");
        File.WriteAllText(orphan, "retained orphan");
        var account = JournalRecovery.AccountStorageHistory(temp.Path, TestContext.Current.CancellationToken);
        Assert.False(account.Complete); Assert.Equal(1, account.Artifacts); Assert.True(File.Exists(orphan));
    }

    [Fact]
    public void A_live_owner_is_replaced_by_its_full_potential_instead_of_double_charging_its_partial_file()
    {
        using var temp = new TempDir(); Guid id = Guid.NewGuid(); Header(temp.Path, id);
        var account = StorageEvidenceAccounting.Census(temp.Path, new HashSet<Guid> { id }, TestContext.Current.CancellationToken);
        Assert.True(account.Complete); Assert.Equal(0, account.ChargedBytes); Assert.Equal(1, account.Journals);
        int checks = 0;
        var budget = new StorageEvidenceBudget(100, 40, (_, live, _) =>
        { checks++; return new(live.Count == 0 ? 10 : 15, 0, 1, 0, 0, true, null); });
        using var first = budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var second = budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Throws<StorageEvidenceLimitException>(() => budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken));
        first.Dispose(); first.Dispose();
        using var replacement = budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(4, checks);
    }

    [Fact]
    public void Incomplete_census_and_another_observed_directory_are_charged_before_any_reservation()
    {
        using var temp = new TempDir(); bool complete = false;
        var budget = new StorageEvidenceBudget(100, 40, (_, _, _) => new(25, 0, 1, 0, 0, complete, "fixture census incomplete"));
        Assert.Throws<IOException>(() => budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken));
        complete = true;
        using (budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken)) { }
        // A second observed location contributes 25 as well, even though the prior worker has retired.
        using var other = budget.Reserve(Path.Join(temp.Path, "other"), Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Throws<StorageEvidenceLimitException>(() => budget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retirement_during_census_never_waits_for_evidence_and_forces_a_fresh_account()
    {
        using var temp = new TempDir(); int checks = 0; bool block = false;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        // The first admission uses an initially empty inventory.
        var emptyBudget = new StorageEvidenceBudget(100, 40, (_, live, ct) =>
        {
            if (!block) return new(0, 0, 0, 0, 0, true, null);
            Interlocked.Increment(ref checks);
            if (live.Count != 0) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), ct)); }
            return new(live.Count == 0 ? 65 : 0, 0, 1, 0, 0, true, null);
        });
        using var first = emptyBudget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken);
        block = true;
        var next = Task.Run(() => Assert.Throws<StorageEvidenceLimitException>(() =>
            emptyBudget.Reserve(temp.Path, Guid.NewGuid(), TestContext.Current.CancellationToken)), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            first.Dispose(); // does not enter the census lane, including when its I/O is blocked
        }
        finally { release.Set(); }
        await next; Assert.Equal(2, checks);
    }
}
