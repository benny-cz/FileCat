using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisTreeGroupingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public void GroupedMemoryTreeKeepsEveryMetricGroupAreaAndCapturedMembershipExact(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        var snapshot = AnalysisGroupingTests.Fixture(); var projection = AnalysisAggregate.Project(snapshot, metric, cancellation: Token);
        var expected = AnalysisGrouping.Project(snapshot, projection, dimension, Token);
        var tree = AnalysisTree.BuildGrouped(snapshot, projection, dimension, false, Token);
        Assert.Same(snapshot, tree.Snapshot); Assert.Same(projection, tree.Projection); Assert.Equal(dimension, tree.GroupingDimension);
        Assert.Equal(AnalysisGroupedChart.Area(projection.Totals, metric), tree.ScopeValue(AnalysisTree.RootKey));
        Assert.Equal(tree.ScopeValue(AnalysisTree.RootKey), tree.ChildrenOf(AnalysisTree.RootKey).Aggregate((UInt128)0, (sum, row) => checked(sum + row.Value)));
        Assert.Equal(projection.Totals.MatchingPaths, tree.Items[AnalysisTree.RootKey].Files);
        foreach (var group in expected.Groups)
        {
            var item = tree.Items[group.Definition.Key]; Assert.Equal(group.ChartArea(metric), item.Value); Assert.True(tree.CanExplore(item.Key));
            Assert.Equal(item.Value, tree.ChildrenOf(item.Key).Aggregate((UInt128)0, (sum, row) => checked(sum + row.Value)));
            Assert.Equal(group.Members, tree.CaptureMembers(item.Key));
            Assert.Equal(group.Members, tree.ChildrenOf(item.Key).Where(row => row.NodeId.HasValue).Select(row => row.NodeId!.Value).Order());
            foreach (var row in tree.ChildrenOf(item.Key).Where(row => row.NodeId.HasValue))
            {
                Assert.False(tree.CanExplore(row.Key)); Assert.Equal(group.Definition.Key, row.ParentKey);
                Assert.Equal(row.NodeId == 4 || row.NodeId == 6 ? 3 : row.NodeId == 5 ? 2 : 2, row.CapturedDepth);
            }
        }
        var wanted = expected.Groups.SelectMany(group => group.Members).Distinct().Order();
        Assert.Equal(wanted, tree.CaptureMembers(AnalysisTree.RootKey).Order());
        Assert.Equal(2UL, snapshot.Nodes.Single(node => node.Id == 4).ParentId);
    }

    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public void GroupedMemoryFilterCannotCaptureANonmatchingAliasOrRedistributeItsSharedCharge(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        var snapshot = AnalysisGroupingTests.Fixture(); var projection = AnalysisAggregate.Project(snapshot, metric, (node, _) => node.Id == 4, Token, _ => false);
        var tree = AnalysisTree.BuildGrouped(snapshot, projection, dimension, true, Token);
        Assert.Equal(new ulong[] { 4 }, tree.CaptureMembers(AnalysisTree.RootKey));
        Assert.False(tree.Items[AnalysisTree.Key(5)].Visible);
        foreach (var group in tree.ChildrenOf(AnalysisTree.RootKey)) Assert.Equal(new ulong[] { 4 }, tree.CaptureMembers(group.Key));
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            var shared = Assert.Single(tree.Groups.Values, group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
            var reference = Assert.Single(tree.ChildrenOf(shared.Definition.Key), row => row.NodeId.HasValue);
            Assert.Equal(4UL, reference.NodeId); Assert.Equal((UInt128)0, reference.Value); Assert.Equal((UInt128)100, reference.LogicalSize);
            Assert.True(reference.SharedElsewhere); Assert.NotEqual(AnalysisTree.Key(4), reference.Key);
            var bucket = Assert.Single(tree.ChildrenOf(shared.Definition.Key), row => row.Synthetic);
            Assert.False(tree.CanExplore(bucket.Key)); Assert.Empty(tree.ChildrenOf(bucket.Key)); Assert.Equal(new ulong[] { 4 }, tree.CaptureMembers(bucket.Key));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedSubtotalCapturesOnlyItsOwnAliasesAndKeepsMissingEvidenceSeparate(bool missing)
    {
        var snapshot = AnalysisAccountingTests.Golden();
        if (missing) snapshot = snapshot with { Facts = snapshot.Facts.SetItem(0, snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Failed) }) };
        var projection = AnalysisAggregate.Project(snapshot, missing ? AnalysisMetric.OnDisk : AnalysisMetric.UniqueFileSize, cancellation: Token);
        var tree = AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.Extension, false, Token);
        var group = Assert.Single(tree.ChildrenOf(AnalysisTree.RootKey)); var bucket = Assert.Single(tree.ChildrenOf(group.Key), row => row.Synthetic);
        Assert.Equal(new ulong[] { 4, 5 }, tree.CaptureMembers(bucket.Key)); Assert.DoesNotContain(6UL, bucket.Members);
        Assert.Equal(new ulong[] { 4, 5, 6 }, tree.CaptureMembers(group.Key).Order());
        Assert.Equal(group.Value, tree.ChildrenOf(group.Key).Aggregate((UInt128)0, (sum, row) => sum + row.Value));
        if (missing) { Assert.True(bucket.Unknown); Assert.Equal((UInt128)0, bucket.Value); Assert.Equal(1UL, tree.Groups[group.Key].Totals.Missing[MeasurementState.Failed]); }
        else { Assert.Equal((UInt128)100, bucket.Value); Assert.Equal((UInt128)20, tree.Items[AnalysisTree.Key(6)].Value); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupedMemoryUnknownUniqueAmountsAndSharedReferencesFollowMeasuredZero(bool descending)
    {
        var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Objects = snapshot.Objects.SetItem(1, snapshot.Objects[1] with { VerifiedIdentity = null }).Add(new("ZERO", "device:ZERO", AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(1))),
            Nodes = snapshot.Nodes.Add(new(7, "r", 2, "zero.bin", AnalysisKind.File, "ZERO"))
        };
        var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: Token);
        var tree = AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.Extension, false, Token);
        var group = Assert.Single(tree.ChildrenOf(AnalysisTree.RootKey));
        using var ordered = AnalysisTableOrder.Apply(tree.ChildrenOf(group.Key), AnalysisTableSort.ActiveMetric, descending, Token);
        int zero = ordered.Rows.IndexOf(tree.Items[AnalysisTree.Key(7)]), unknown = ordered.Rows.IndexOf(tree.Items[AnalysisTree.Key(6)]);
        Assert.True(zero < unknown); Assert.True(tree.Items[AnalysisTree.Key(6)].Unknown); Assert.Equal((UInt128)0, tree.Items[AnalysisTree.Key(6)].Value);
        Assert.Equal((UInt128)20, tree.Items[AnalysisTree.Key(6)].IdentityUnverified);
    }

    [Fact]
    public void GroupedMemoryRefusesOtherRevisionsAndCanceledWorkAndNeverInventsIncompatibleArea()
    {
        var snapshot = AnalysisGroupingTests.Fixture(); var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: Token);
        Assert.Throws<InvalidOperationException>(() => AnalysisTree.BuildGrouped(snapshot with { Identity = snapshot.Identity with { Revision = 2 } }, projection, AnalysisGroupingDimension.FileType, false, Token));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.FileType, false, canceled.Token));
        snapshot = snapshot with { Facts = snapshot.Facts.Select(fact => fact with { BasisId = fact.DirectoryId is not null ? "other-basis" : fact.BasisId }).ToImmutableArray() };
        projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: Token); Assert.False(projection.Totals.CompatibleBasis);
        var tree = AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.CapturedLocation, false, Token);
        Assert.Equal((UInt128)0, tree.ScopeValue(AnalysisTree.RootKey)); Assert.All(tree.Items.Values, row => Assert.Equal((UInt128)0, row.Value));
    }
}
