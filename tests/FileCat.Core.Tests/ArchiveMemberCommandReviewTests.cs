using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Member_commands_count_exact_physical_scope_and_keep_duplicate_occurrences_distinct(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        var source = WorkflowSource(WorkflowProfile(format, mode), duplicates: true);
        var review = new ArchiveMemberCommandReview(source, Ct);
        var folder = review.Select("tree", true);
        Assert.Equal(2, folder.Files); Assert.Equal(1, folder.DirectoryRecords); Assert.Equal(3, folder.PhysicalEntries);
        Assert.Equal(2, folder.LogicalBytes); Assert.Equal("tree/", folder.Path);
        Assert.Null(folder.Change(ArchiveChangeKind.Delete).Ordinal);
        var second = review.Select("tree/old", false, 1);
        Assert.Equal(2, second.Copies); Assert.Equal(1, second.Change(ArchiveChangeKind.Delete).Ordinal);
        Assert.Equal(0, review.Select("Case", false).Change(ArchiveChangeKind.Delete).Ordinal);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => review.Select("tree/old", false, 2)).Reason);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => review.Select("tre", true)).Reason);
        Assert.Null(review.RenameProblem(review.Select("Case", false), "CASE"));
        Assert.NotNull(review.RenameProblem(review.Select("Case", false), "case"));
        Assert.NotNull(review.RenameProblem(review.Select("é", false), "é"));
        Assert.True(review.DirectoryExists("tree")); Assert.True(review.DirectoryExists("treehouse"));
        Assert.NotNull(review.FolderProblem("tree")); Assert.NotNull(review.FolderProblem("Case/nested"));
        Assert.Null(review.FolderProblem("TREE/new"));
        Assert.NotNull(review.DestinationFolderProblem("Case/nested")); Assert.Null(review.DestinationFolderProblem("missing/nested"));
    }

    [Fact]
    public void Member_command_scope_includes_implicit_folders_and_cancellation_precedes_index_allocation()
    {
        var source = WorkflowSource(new(ArchiveWriteFormat.Tar), implicitFolder: true);
        var review = new ArchiveMemberCommandReview(source, Ct);
        Assert.Equal(1, review.Select("tree", true).PhysicalEntries); Assert.Equal(0, review.Select("tree", true).DirectoryRecords);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new ArchiveMemberCommandReview(source, stop.Token));
    }
}
