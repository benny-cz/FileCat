using FileCat.Core.Analysis;
using FileCat.Core.Commands;

namespace FileCat.Core.Tests;

public sealed class AnalysisTreeTests
{
    [Fact]
    public void LogicalUncertaintyIsIndependentOfAllocationAndKnownZero()
    {
        var s = AnalysisAccountingTests.Golden(); var token = TestContext.Current.CancellationToken;
        s = s with { Objects = s.Objects.SetItem(1, s.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }) };
        var disk = AnalysisTree.Build(s, AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: token), false, token);
        Assert.True(disk.Items["n:6"].Unknown); Assert.False(disk.Items["n:6"].LogicalUnknown);
        Assert.Equal((UInt128)0, disk.Items["n:6"].LogicalSize); Assert.False(disk.Items[AnalysisTree.RootKey].LogicalUnknown);
        s = s with { Objects = s.Objects.SetItem(1, s.Objects[1] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Failed) }) };
        var logical = AnalysisTree.Build(s, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: token), false, token);
        Assert.True(logical.Items["n:6"].LogicalUnknown); Assert.True(logical.Items["n:1"].LogicalUnknown);
        Assert.True(logical.Items[AnalysisTree.RootKey].LogicalUnknown); Assert.Equal((UInt128)200, logical.Items[AnalysisTree.RootKey].LogicalSize);
    }
    [Fact]
    public void FilteredGroupCaptureDoesNotStageContextFoldersOrHiddenDescendants()
    {
        var s = AnalysisAccountingTests.Golden(); var token = TestContext.Current.CancellationToken;
        var tree = AnalysisTree.Build(s, AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, (n, _) => n.Name == "x.bin", token), true, token);
        Assert.Equal(new ulong[] { 4 }, tree.CaptureMembers("n:1"));
        Assert.Equal(new ulong[] { 4 }, tree.CaptureMembers("n:2"));
        Assert.Empty(tree.CaptureMembers("n:3"));
    }

    [Fact]
    public void OversizedCaptureIsBoundedAndCannotPartlyChangeMarksOrBasket()
    {
        var selection = new AnalysisSelectionState(); selection.ToggleMark(7); selection.StageCaptured([8]);
        var ids = Enumerable.Range(1, AnalysisLimits.ReviewPaths + 1).Select(i => (ulong)i);
        Assert.Throws<InvalidOperationException>(() => selection.MarkCaptured(ids));
        Assert.Throws<InvalidOperationException>(() => selection.StageCaptured(ids));
        Assert.Equal(new ulong[] { 7 }, selection.Marks); Assert.Equal(new ulong[] { 8 }, selection.Basket);
        Assert.Throws<ArgumentException>(() => selection.ToggleMark(0));
    }
    [Fact]
    public void IdentityUnverifiedAllocationStaysDistinctAndNeverBecomesUniqueArea()
    {
        var s = AnalysisAccountingTests.Golden();
        s = s with { Objects = [s.Objects[0], s.Objects[1] with { VerifiedIdentity = null }], Facts = [s.Facts[0], s.Facts[1] with { Measurement = AnalysisMeasurement.Known(42) }] };
        var token = TestContext.Current.CancellationToken;
        var disk = AnalysisTree.Build(s, AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: token), false, token);
        Assert.Equal((UInt128)122, disk.ScopeValue(AnalysisTree.RootKey));
        Assert.Equal((UInt128)42, disk.Items[AnalysisTree.RootKey].IdentityUnverified);
        Assert.Equal((UInt128)42, disk.Items["n:6"].IdentityUnverified);
        var unique = AnalysisTree.Build(s, AnalysisAggregate.Project(s, AnalysisMetric.UniqueFileSize, cancellation: token), false, token);
        Assert.Equal((UInt128)100, unique.ScopeValue(AnalysisTree.RootKey));
        Assert.Equal((UInt128)20, unique.Projection.Totals.IdentityUnverified);
        Assert.Equal((UInt128)0, unique.Items["n:6"].Value); Assert.True(unique.Items["n:6"].Unknown);
    }
    [Fact]
    public void DisplayAggregationOpensExactlyItsOmittedMembersAndKeepsExactTotals()
    {
        var s = AnalysisAccountingTests.Golden();
        var tree = AnalysisTree.Build(s, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken), false, TestContext.Current.CancellationToken);
        string other = AnalysisTree.OtherKey("n:2", 1);
        Assert.Equal("n:2", tree.ParentOf(other));
        Assert.Equal("z.bin", Assert.Single(tree.ChildrenOf(other)).Name);
        Assert.Equal((UInt128)20, tree.ScopeValue(other));
        Assert.Equal(new ulong[] { 6 }, tree.CaptureMembers(other));
        Assert.Empty(tree.ChildrenOf(AnalysisTree.OtherKey(other, 1)));
    }

    [Fact]
    public void AnalysisBindingsStaySeparateFromPanelCommandsAndCanBeReassigned()
    {
        var registry = CommandRegistry.CreateDefault();
        var map = new Keymap(registry, new Dictionary<string, string[]> { [CommandIds.DiskSpaceReview] = ["Ctrl+R"] });
        Assert.Empty(map.Conflicts);
        Assert.Equal(CommandIds.Delete, map.Resolve(KeyChord.Parse("F8"), CommandContext.Panel));
        Assert.Null(map.Resolve(KeyChord.Parse("F8"), CommandContext.DiskSpace));
        Assert.Equal(CommandIds.DiskSpaceReview, map.Resolve(KeyChord.Parse("Ctrl+R"), CommandContext.DiskSpace));
        Assert.Equal(CommandIds.DiskSpaceReviewCancel, map.Resolve(KeyChord.Parse("Escape"), CommandContext.DiskSpaceReview));
        Assert.Null(map.Resolve(KeyChord.Parse("F4"), CommandContext.DiskSpace));
        Assert.Null(map.Resolve(KeyChord.Parse("F7"), CommandContext.DiskSpace));
    }
    [Fact]
    public void TreePreservesSharedChargeAndFilteredAliasesAcrossViews()
    {
        var s = AnalysisAccountingTests.Golden();
        var p = AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, (n, _) => n.Name == "x.bin", TestContext.Current.CancellationToken);
        var tree = AnalysisTree.Build(s, p, true, TestContext.Current.CancellationToken);
        Assert.Equal((UInt128)80, tree.ScopeValue(AnalysisTree.RootKey));
        Assert.Equal((UInt128)80, tree.Items["shared:n:1"].Value);
        Assert.Equal((UInt128)0, tree.Items["n:2"].Value);
        Assert.True(tree.Items["n:4"].Visible); Assert.False(tree.Items["n:5"].Visible);
        Assert.True(tree.Items["n:4"].SharedElsewhere);
        Assert.Equal(new ulong[] { 4 }, tree.CaptureMembers("shared:n:1"));
        Assert.Equal(new ulong[] { 4, 5 }, tree.CaptureMembers("shared:n:1", false));
    }

    [Fact]
    public void MarksBasketFocusAndNavigationDoNotRetargetOneAnother()
    {
        var state = new AnalysisSelectionState();
        state.ToggleMark(4); state.SetFocus("n:5"); state.StageCaptured([6]); state.Navigate("n:2");
        Assert.Equal(new ulong[] { 4 }, state.Marks); Assert.Equal(new ulong[] { 6 }, state.Basket);
        Assert.Equal("n:5", state.Focus); state.Back(); Assert.Equal(AnalysisTree.RootKey, state.Scope);
        state.Forward(); Assert.Equal("n:2", state.Scope);
        state.ClearMarks(); Assert.Single(state.Basket);
    }

    [Fact]
    public void KnownPathSizesAndCountsFoldExactlyWithoutUsingGeometry()
    {
        var s = AnalysisAccountingTests.Golden();
        var p = AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken);
        var tree = AnalysisTree.Build(s, p, false, TestContext.Current.CancellationToken);
        Assert.Equal((UInt128)220, tree.ScopeValue(AnalysisTree.RootKey));
        Assert.Equal((UInt128)120, tree.Items["n:2"].Value);
        Assert.Equal(3ul, tree.Items[AnalysisTree.RootKey].Files);
        Assert.Equal((UInt128)220, tree.Items[AnalysisTree.RootKey].LogicalSize);
    }
}
