using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupedUniqueIdentityUncertaintySortsAfterMeasuredZeroInBothDirections(bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Add(new(7, "r", 2, "zero.bin", AnalysisKind.File, "ZERO")).Add(new(8, "r", 2, "weak.bin", AnalysisKind.File, "Z")),
            Objects = snapshot.Objects.SetItem(1, snapshot.Objects[1] with { VerifiedIdentity = null }).Add(new("ZERO", "device:ZERO", AnalysisMeasurement.Known(0), AnalysisMeasurement.Known(1)))
        };
        await using (var dataset = await Build(snapshot, io))
        await using (var lease = await dataset.AcquireAsync(Cancellation))
        await using (var groups = await lease.BuildGroupingAsync(AnalysisMetric.UniqueFileSize, AnalysisGroupingDimension.Extension, cancellation: Cancellation))
        {
            var group = Assert.Single((await groups.ReadGroupsAsync(cancellation: Cancellation)).Rows);
            var rows = await groups.ReadMemberRowsAsync(group.Definition.Key, descending: descending, cancellation: Cancellation);
            Assert.Equal(7UL, rows.Rows[0].Member.Path.Id);
            Assert.Equal((UInt128)0, rows.Rows[0].Assigned.Known); Assert.Equal((UInt128)0, rows.Rows[0].Assigned.IdentityUnverified);
            Assert.All(rows.Rows.Where(row => row.Member.Path.Id is 6 or 8), row =>
            { Assert.True(AnalysisGroupedChart.MemberItem(row, group.Definition.Key, groups.Metric).Unknown); Assert.Equal((UInt128)20, row.Assigned.IdentityUnverified); });
        }
        snapshot = snapshot with { Nodes = snapshot.Nodes.Select(node => node.Id switch
        { 4 => node with { Name = "x.cs" }, 5 => node with { Name = "y.jpg" }, 6 or 8 => node with { Name = node.Id + ".unknown" }, 7 => node with { Name = "zero.zero" }, _ => node }).ToImmutableArray() };
        await using (var dataset = await Build(snapshot, io))
        await using (var lease = await dataset.AcquireAsync(Cancellation))
        await using (var groups = await lease.BuildGroupingAsync(AnalysisMetric.UniqueFileSize, AnalysisGroupingDimension.Extension, cancellation: Cancellation))
        {
            var rows = await groups.ReadGroupsAsync(cancellation: Cancellation, descending: descending);
            Assert.Equal("extension:value:unknown", rows.Rows[^1].Definition.Value);
            Assert.Equal((UInt128)40, rows.Rows[^1].Totals.IdentityUnverified);
            Assert.Contains(rows.Rows.Take(rows.Rows.Length - 1), group => group.Definition.Value == "extension:value:zero" && group.Totals.Known == 0);
        }
    }

    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public async Task FilteredGroupedPagesAndDirectLookupMatchTheFullCapturedAliasAccounting(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        var projection = AnalysisAggregate.Project(snapshot, metric, (node, _) => node.Id == 4, Cancellation, _ => false);
        var expected = AnalysisGrouping.Project(snapshot, projection, dimension, Cancellation);
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, dimension, new("name:x.cs"), Cancellation);
        Assert.Equal("name:x.cs", groups.Filter!.Request.Text); Assert.Equal(1UL, groups.Filter.DefinitePaths);
        var all = await AllGroups(groups); Assert.Equal(expected.Groups.Length, all.Count); Equal(projection.Totals, groups.Totals);
        foreach (var wanted in expected.Groups)
        {
            var actual = await groups.ReadGroupAsync(wanted.Definition.Key, Cancellation);
            Assert.Equal(wanted.Definition, actual.Definition); Equal(wanted.Totals, actual.Totals);
            var rows = await groups.ReadMemberRowsAsync(actual.Definition.Key, cancellation: Cancellation);
            Assert.Equal(wanted.Members, rows.Rows.Select(row => row.Member.Path.Id).Order());
            var chart = AnalysisGroupedChart.Members(groups.Descriptor.Identity, dimension, metric, actual, rows);
            Assert.Equal(AnalysisGroupedChart.Area(actual.Totals, metric), chart.ChildrenOf(chart.Scope).Aggregate((UInt128)0, (sum, row) => checked(sum + row.Value)));
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => groups.ReadGroupAsync("n:4", Cancellation));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => groups.ReadGroupAsync("group:1:" + new string('0', 64), Cancellation));
    }
}
