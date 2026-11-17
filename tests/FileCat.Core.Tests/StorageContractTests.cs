using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class StorageContractTests
{
    [Theory]
    [InlineData(@"C:\file", true)]
    [InlineData(@"\\?\C:\long\名前", true)]
    [InlineData(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\file", true)]
    [InlineData(@"C:\file:ads", false)]
    [InlineData(@"C:\file:ads:$DATA", false)]
    [InlineData(@"C:\file::$DATA", false)]
    [InlineData(@"C:\folder:stream\child", false)]
    [InlineData(@"C:relative", false)]
    [InlineData(@"\root-relative", false)]
    [InlineData(@"\\server\share\file", false)]
    [InlineData(@"\\?\UNC\server\share\file", false)]
    [InlineData(@"\\.\PhysicalDrive0", false)]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\file", false)]
    [InlineData(@"C:\..\file", false)]
    [InlineData(@"C:\a\\b", false)]
    [InlineData(@"C:\file*", false)]
    public void Ordinary_unnamed_stream_grammar_is_explicit_and_never_strips_a_suffix(string path, bool accepted) =>
        Assert.Equal(accepted, StoragePaths.IsOrdinaryLocalPath(path));

    [Theory]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a:b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a/b", false)]
    [InlineData("case-sensitive 名前", true)]
    public void Relative_child_is_one_exact_component(string name, bool accepted) => Assert.Equal(accepted, StoragePaths.IsChildName(name));

    [Fact]
    public void Outcomes_are_disjoint_and_uncertainty_outranks_stop()
    {
        var counts = new StorageOutcomeCounts().Add(StorageEffect.Changed).Add(StorageEffect.AlreadySatisfied)
            .Add(StorageEffect.Skipped).Add(StorageEffect.Failed).Add(StorageEffect.Uncertain).Add(StorageEffect.NotProcessed);
        Assert.Equal(2, counts.Successful);
        Assert.Equal(5, counts.Processed);
        Assert.Equal(6, counts.Observed);
        var snapshot = new StorageResultSnapshot(StorageAction.Compress, StorageScope.ExistingDescendants, Guid.NewGuid(), counts) { Canceled = true };
        Assert.Equal(JobState.Failed, snapshot.FinalState());
        Assert.Equal(JobState.Canceled, (snapshot with { Counts = counts with { Uncertain = 0 } }).FinalState());
        Assert.Equal(JobState.Failed, (snapshot with { Counts = new(), PersistenceFault = true }).FinalState());
    }

    [Fact]
    public void Reducer_keeps_success_noop_partial_skips_and_no_progress_distinct()
    {
        var snapshot = new StorageResultSnapshot(StorageAction.RemoveCompression, StorageScope.SelectedItems, Guid.NewGuid(), new(AlreadySatisfied: 3))
        {
            Discovery = StorageCompleteness.Complete,
            Roots = [new(0, null, StorageCompleteness.Complete, false)],
        };
        Assert.Equal(JobState.Completed, snapshot.FinalState());
        Assert.Equal(JobState.CompletedWithIssues, (snapshot with { Discovery = StorageCompleteness.UnknownRemainder }).FinalState());
        Assert.Equal(JobState.CompletedWithIssues, (snapshot with { Counts = new(Skipped: 3) }).FinalState());
        Assert.Equal(JobState.Failed, (snapshot with { Counts = new(), ExecutionRefused = true }).FinalState());
        Assert.Equal(JobState.Failed, (snapshot with { ReviewPending = true }).FinalState());
        Assert.Equal(JobState.CompletedWithIssues, (snapshot with { Counts = new(Changed: 1), Roots = [new(0, null, StorageCompleteness.Incomplete, true)] }).FinalState());
    }
}
