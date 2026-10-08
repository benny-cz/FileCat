using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StagedRecoveryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65536)]
    [InlineData(65537)]
    public void Exact_bounded_staged_bytes_can_be_reviewed_and_deleted(int length)
    {
        using var f = new Fixture(); File.WriteAllBytes(f.Path, Enumerable.Range(0, length).Select(i => (byte)i).ToArray());
        var review = Assert.IsType<JournalRecovery.StagedFileReview>(JournalRecovery.ReviewStagedFile(f.Path, new LocalFileSystemProvider()));
        Assert.Equal(length, review.Length); Assert.True(JournalRecovery.DeleteReviewedStagedFile(review, new LocalFileSystemProvider())); Assert.False(File.Exists(f.Path));
    }
    [Fact]
    public void A_staged_file_larger_than_the_remaining_budget_is_kept()
    {
        using var f = new Fixture(); File.WriteAllText(f.Path, "owned bytes");
        Assert.Null(JournalRecovery.ReviewStagedFile(f.Path, new LocalFileSystemProvider(), byteLimit: 3)); Assert.Equal("owned bytes", File.ReadAllText(f.Path));
    }
    [Fact]
    public void Cancellation_before_deletion_preserves_the_reviewed_file()
    {
        using var f = new Fixture(); File.WriteAllText(f.Path, "owned bytes"); var provider = new LocalFileSystemProvider();
        var review = JournalRecovery.ReviewStagedFile(f.Path, provider)!;
        Assert.Throws<OperationCanceledException>(() => JournalRecovery.DeleteReviewedStagedFile(review, provider, () => throw new OperationCanceledException())); Assert.Equal("owned bytes", File.ReadAllText(f.Path));
    }
    [Fact]
    public void Potential_staged_path_enumeration_is_bounded_and_deduplicated()
    {
        using var f = new Fixture(); for (int i = 0; i < 12; i++) File.WriteAllText(System.IO.Path.Join(f.Root, ".fc-abcdef12-" + i + ".tmp"), "bytes");
        var job = new InterruptedJob(System.IO.Path.Join(f.Root, "job-abcdef12.fcj"), "Copy", "owned", DateTime.UtcNow, [], null, [], [f.Root, f.Root], 0, 0);
        var paths = JournalRecovery.FindStagedLeftovers(job, 5); Assert.Equal(5, paths.Count); Assert.Equal(5, paths.Distinct().Count());
        Assert.Equal(12, Directory.GetFiles(f.Root, ".fc-*").Length);
    }
    [Fact]
    public void An_ended_journal_cannot_be_reconciled_as_a_live_interruption()
    {
        using var f = new Fixture(); var job = new Job(new JobRequest { Kind = JobKind.Copy, Sources = [], Destination = Location.FileSystem(f.Root) }, "owned", "owned", [], []);
        string path; using (var writer = JobJournal.Create(f.Root, job)) path = writer.Path;
        var interrupted = Assert.Single(JournalRecovery.Scan(f.Root)); Assert.True(JournalRecovery.IsInterrupted(interrupted)); Assert.True(JournalRecovery.TryClose(interrupted, "owned review")); Assert.False(JournalRecovery.IsInterrupted(interrupted));
        Assert.Empty(JournalRecovery.Scan(f.Root)); Assert.True(File.Exists(path));
    }
    private sealed class Fixture : IDisposable
    {
        public string Root = Directory.CreateDirectory(System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-staged-tests", Guid.NewGuid().ToString("N"))).FullName;
        public string Path => System.IO.Path.Join(Root, "owned.tmp");
        public void Dispose() => Directory.Delete(Root, true);
    }
}
