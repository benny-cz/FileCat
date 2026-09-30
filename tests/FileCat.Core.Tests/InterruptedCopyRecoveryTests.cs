using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Release campaign V03-PARTIAL (release plan §8.3 DPI P03): after an interrupted copy, "Delete partial files" and
/// "Run again" may delete only files the job provably left incomplete. A real copy job runs, its journal loses the
/// end record (the crash), and recovery reads it back exactly as FileCat does at the next start.
/// </summary>
public sealed class InterruptedCopyRecoveryTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Copies <paramref name="files"/> into <paramref name="dest"/>, then removes the journal's end record.</summary>
    private async Task<InterruptedJob> CopyThenCrashAsync(IEnumerable<string> files, string dest)
    {
        var journals = _dir.Dir("journal-" + Guid.NewGuid().ToString("N")[..8]);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(new PortableFileOperations(), providers, journals);
        jobs.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.CancelJob));
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = files.Select(f => ItemRef.ForFileSystemPath(f, EntryKind.File)).ToList(),
            Destination = Location.FileSystem(dest),
        });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(JobState.Completed, job.State);
        var journal = Assert.Single(Directory.GetFiles(journals, "job-*.fcj"));
        var lines = File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)).ToArray();
        File.WriteAllLines(journal, lines);
        return Assert.Single(JournalRecovery.Scan(journals));
    }

    [Fact]
    public async Task A_complete_copy_from_a_second_source_folder_is_never_taken_for_a_partial_one()
    {
        // Two folders feed one destination; each also holds an unselected namesake of the other's selected file
        // (README.md, index.html, and the like are common in many folders).
        var a = _dir.Dir("a");
        var b = _dir.Dir("b");
        var dest = _dir.Dir("dest");
        File.WriteAllText(Path.Combine(a, "first.txt"), "first from a");
        File.WriteAllText(Path.Combine(a, "second.txt"), "an unrelated second.txt in a");
        File.WriteAllText(Path.Combine(b, "second.txt"), "second from b");
        File.WriteAllText(Path.Combine(b, "first.txt"), "an unrelated first.txt in b");
        var crashed = await CopyThenCrashAsync([Path.Combine(a, "first.txt"), Path.Combine(b, "second.txt")], dest);

        // Both copies are complete and equal to the files they were copied from: nothing may be offered for deletion.
        Assert.Equal("first from a", File.ReadAllText(Path.Combine(dest, "first.txt")));
        Assert.Equal("second from b", File.ReadAllText(Path.Combine(dest, "second.txt")));
        Assert.Empty(JournalRecovery.FindIncompleteCopies(crashed));
    }

    [Fact]
    public async Task A_partial_copy_from_any_source_folder_is_found()
    {
        var a = _dir.Dir("a");
        var b = _dir.Dir("b");
        var dest = _dir.Dir("dest");
        File.WriteAllText(Path.Combine(a, "one.txt"), "11111111");
        File.WriteAllText(Path.Combine(b, "two.txt"), "22222222");
        var crashed = await CopyThenCrashAsync([Path.Combine(a, "one.txt"), Path.Combine(b, "two.txt")], dest);
        // The "crash" cut both copies short.
        File.WriteAllText(Path.Combine(dest, "one.txt"), "111");
        File.WriteAllText(Path.Combine(dest, "two.txt"), "22");

        var found = JournalRecovery.FindIncompleteCopies(crashed).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["one.txt", "two.txt"], found);
    }

    [Fact]
    public async Task A_copy_changed_after_the_interruption_is_not_offered_for_deletion()
    {
        var src = _dir.Dir("src");
        var dest = _dir.Dir("dest");
        File.WriteAllText(Path.Combine(src, "notes.txt"), "the copied text");
        var crashed = await CopyThenCrashAsync([Path.Combine(src, "notes.txt")], dest);
        // The user opened the copy after the crash and saved an edit before reviewing the interrupted operation.
        File.WriteAllText(Path.Combine(dest, "notes.txt"), "THE USER'S OWN EDIT");

        Assert.Empty(JournalRecovery.FindIncompleteCopies(crashed));
        // It is named, so "Run again" can say it leaves the file as it is.
        Assert.Equal([Path.Combine(dest, "notes.txt")], JournalRecovery.ReviewCopies(crashed).Differing);
    }

    [Fact]
    public async Task A_complete_copy_whose_source_changed_since_is_not_offered_for_deletion()
    {
        var src = _dir.Dir("src");
        var dest = _dir.Dir("dest");
        File.WriteAllText(Path.Combine(src, "data.txt"), "version one");
        var crashed = await CopyThenCrashAsync([Path.Combine(src, "data.txt")], dest);
        // The source moved on after the crash; the copy (a backup of version one, say) is complete and the only copy of it.
        File.WriteAllText(Path.Combine(src, "data.txt"), "version two, rewritten");

        Assert.Empty(JournalRecovery.FindIncompleteCopies(crashed));
        Assert.Equal([Path.Combine(dest, "data.txt")], JournalRecovery.ReviewCopies(crashed).Differing);
    }

    [Fact]
    public async Task A_copy_whose_bytes_are_complete_but_whose_time_was_not_set_yet_is_left_alone()
    {
        var src = _dir.Dir("src");
        var dest = _dir.Dir("dest");
        File.WriteAllText(Path.Combine(src, "done.txt"), "all of it");
        var crashed = await CopyThenCrashAsync([Path.Combine(src, "done.txt")], dest);
        File.SetLastWriteTimeUtc(Path.Combine(dest, "done.txt"), DateTime.UtcNow.AddMinutes(5)); // the crash came before the time

        var review = JournalRecovery.ReviewCopies(crashed);
        Assert.Empty(review.Incomplete);
        Assert.Empty(review.Differing);
    }

    [Fact]
    public async Task Deleting_checks_each_file_again_and_keeps_one_that_changed_since_the_review()
    {
        var src = _dir.Dir("src");
        var dest = _dir.Dir("dest");
        File.WriteAllText(Path.Combine(src, "a.txt"), "aaaaaaaa");
        File.WriteAllText(Path.Combine(src, "b.txt"), "bbbbbbbb");
        var crashed = await CopyThenCrashAsync([Path.Combine(src, "a.txt"), Path.Combine(src, "b.txt")], dest);
        File.WriteAllText(Path.Combine(dest, "a.txt"), "aa");
        File.WriteAllText(Path.Combine(dest, "b.txt"), "bb");
        var review = JournalRecovery.ReviewCopies(crashed);
        Assert.Equal(2, review.Incomplete.Count);

        // Between the review and the confirmation, something wrote to b.txt.
        File.WriteAllText(Path.Combine(dest, "b.txt"), "bb and more");
        int deleted = JournalRecovery.DeleteIncompleteCopies(review.Incomplete, out var kept);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
        Assert.Equal([Path.Combine(dest, "b.txt")], kept);
        Assert.Equal("bb and more", File.ReadAllText(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public async Task A_truncated_file_that_became_a_link_is_never_deleted()
    {
        var src = _dir.Dir("src");
        var dest = _dir.Dir("dest");
        var elsewhere = _dir.File("elsewhere/precious.txt", "p");
        File.WriteAllText(Path.Combine(src, "p.txt"), "pppppp");
        var crashed = await CopyThenCrashAsync([Path.Combine(src, "p.txt")], dest);
        var copy = Path.Combine(dest, "p.txt");
        File.WriteAllText(copy, "p");
        var review = JournalRecovery.ReviewCopies(crashed);
        Assert.Single(review.Incomplete);

        // The name now leads somewhere else (a link made after the review): recovery neither deletes it nor its target.
        File.Delete(copy);
        try { File.CreateSymbolicLink(copy, elsewhere); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Symbolic links cannot be made here: " + ex.Message); }
        Assert.Equal(0, JournalRecovery.DeleteIncompleteCopies(review.Incomplete, out _));
        Assert.True(File.Exists(copy));
        Assert.Equal("p", File.ReadAllText(elsewhere));
        Assert.Empty(JournalRecovery.ReviewCopies(crashed).Incomplete);
    }
}
