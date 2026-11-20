using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static TheoryData<AnalysisTableSort, bool> GroupOrders
    {
        get { var data = new TheoryData<AnalysisTableSort, bool>(); foreach (var sort in Enum.GetValues<AnalysisTableSort>()) { data.Add(sort, false); data.Add(sort, true); } return data; }
    }
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task GroupDisplayRowsKeepAssignedAmountsSharedReferencesAndLogicalLengthsDistinct(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        var all = await AllGroups(groups);
        var code = Assert.Single(all, g => g.Definition.Value == "type:code");
        var row = Assert.Single((await groups.ReadMemberRowsAsync(code.Definition.Key, cancellation: Cancellation)).Rows);
        Assert.Equal(4UL, row.Member.Path.Id); Assert.Equal(AnalysisMeasurement.Known(100), row.Member.LogicalLength);
        Assert.Equal(metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk, row.SharedElsewhere);
        Assert.Equal(metric == AnalysisMetric.Files ? (UInt128)1 : metric == AnalysisMetric.FileSize ? 100U : 0U, row.Assigned.Known);
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            var shared = Assert.Single(all, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Equal(shared.Totals, shared.Shared);
            var reference = await groups.ReadMemberRowsAsync(shared.Definition.Key, sort: AnalysisTableSort.FileSize, descending: false, cancellation: Cancellation);
            Assert.Equal(2, reference.Rows.Length); Assert.All(reference.Rows, r => { Assert.True(r.SharedElsewhere); Assert.True(r.Member.AccountingReference); Assert.Equal((UInt128)0, r.Assigned.Known); Assert.Equal(AnalysisMeasurement.Known(100), r.Member.LogicalLength); });
        }
        if (metric == AnalysisMetric.OnDisk)
        {
            var directory = Assert.Single(all, g => g.Kind == AnalysisGroupKind.DirectoryAllocation);
            var own = Assert.Single((await groups.ReadMemberRowsAsync(directory.Definition.Key, cancellation: Cancellation)).Rows);
            Assert.False(own.SharedElsewhere); Assert.Equal((UInt128)32, own.Assigned.IdentityUnverified); Assert.Equal((UInt128)0, directory.Shared.IdentityUnverified);
        }
    }
    [Theory]
    [MemberData(nameof(GroupOrders))]
    public async Task GroupSummaryOrdersPageExactFieldsAndKeepUnknownNumericValuesLast(AnalysisTableSort field, bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        var rows = new List<AnalysisDatasetGroup>(); ulong at = 0;
        while (true)
        {
            var page = await groups.ReadGroupsAsync(at, maximum: 1, cancellation: Cancellation, sort: field, descending: descending); rows.AddRange(page.Rows);
            if (!page.HasMore) break; at = page.NextOffset;
        }
        Assert.Equal(groups.Count, (ulong)rows.Count); Assert.Equal(rows.Count, rows.Select(r => r.Definition.Key).Distinct().Count());
        bool Missing(AnalysisDatasetGroup group) => field == AnalysisTableSort.ActiveMetric ? group.Totals.Missing.Count != 0 && group.Totals.Known + group.Totals.IdentityUnverified == 0 :
            field == AnalysisTableSort.FileSize ? group.Logical.UnknownFiles != 0 && group.Logical.KnownBytes == 0 : field == AnalysisTableSort.MeasurementState && group.Totals.Missing.Count != 0;
        var known = rows.TakeWhile(r => !Missing(r)).ToArray(); Assert.All(rows.Skip(known.Length), r => Assert.True(Missing(r)));
        for (int i = 1; i < known.Length; i++)
        {
            int compared = field switch
            {
                AnalysisTableSort.Name => StringComparer.OrdinalIgnoreCase.Compare(known[i - 1].Definition.Label, known[i].Definition.Label),
                AnalysisTableSort.FileSize => known[i - 1].Logical.KnownBytes.CompareTo(known[i].Logical.KnownBytes),
                AnalysisTableSort.FilePaths => known[i - 1].Totals.MatchingPaths.CompareTo(known[i].Totals.MatchingPaths),
                AnalysisTableSort.MeasurementState => 0,
                _ => (known[i - 1].Totals.Known + known[i - 1].Totals.IdentityUnverified).CompareTo(known[i].Totals.Known + known[i].Totals.IdentityUnverified)
            };
            Assert.True(descending ? compared >= 0 : compared <= 0);
        }
    }
    [Theory]
    [MemberData(nameof(GroupOrders))]
    public async Task GroupMemberOrdersUseTheirOwnMetricAndLogicalAmountsThroughBoundedPages(AnalysisTableSort field, bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture();
        string[] names = ["z.cs", "A.cs", "m.cs", "n.cs", "À.cs", "a😀.cs"];
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(s.Nodes[0]); var objects = ImmutableArray.CreateBuilder<AnalysisObject>(); var facts = ImmutableArray.CreateBuilder<StorageFact>();
        for (int i = 0; i < names.Length; i++)
        {
            string id = "object-" + i; nodes.Add(new((ulong)i + 2, "r", 1, names[i], AnalysisKind.File, id));
            objects.Add(new(id, id, i == 4 ? AnalysisMeasurement.Missing(MeasurementState.Failed) : AnalysisMeasurement.Known((uint)(i * 7)), AnalysisMeasurement.Known(1)));
            facts.Add(new(id, id, null, "primary", "fixture-v1", i == 3 ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known((uint)(100 - i * 13)), "fixture", s.Identity.ObservedUtc, []));
        }
        s = s with { Nodes = nodes.ToImmutable(), Objects = objects.ToImmutable(), Facts = facts.ToImmutable(), Roots = [s.Roots[0]] };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        var code = Assert.Single(await AllGroups(groups)); var rows = new List<AnalysisDatasetGroupRow>(); ulong at = 0;
        while (true)
        {
            var page = await groups.ReadMemberRowsAsync(code.Definition.Key, at, 2, field, descending, Cancellation); rows.AddRange(page.Rows);
            if (!page.HasMore) break; at = page.NextOffset;
        }
        Assert.Equal(names.Length, rows.Count); Assert.Equal(rows.Count, rows.Select(r => r.Member.Path.Id).Distinct().Count());
        bool Missing(AnalysisDatasetGroupRow row) => field == AnalysisTableSort.ActiveMetric ? row.Assigned.Missing.Count != 0 && row.Assigned.Known == 0 :
            field == AnalysisTableSort.FileSize ? !row.Member.LogicalLength.IsKnown : field == AnalysisTableSort.MeasurementState && row.Assigned.Missing.Count != 0;
        var known = rows.TakeWhile(r => !Missing(r)).ToArray(); Assert.All(rows.Skip(known.Length), row => Assert.True(Missing(row)));
        for (int i = 1; i < known.Length; i++)
        {
            int compared = field switch
            {
                AnalysisTableSort.Name => StringComparer.OrdinalIgnoreCase.Compare(known[i - 1].Member.Path.Name, known[i].Member.Path.Name),
                AnalysisTableSort.FileSize => known[i - 1].Member.LogicalLength.Value!.Value.CompareTo(known[i].Member.LogicalLength.Value!.Value),
                AnalysisTableSort.ActiveMetric => known[i - 1].Assigned.Known.CompareTo(known[i].Assigned.Known),
                _ => 0
            };
            Assert.True(descending ? compared >= 0 : compared <= 0);
        }
        Assert.Equal(s.Facts.Where(f => f.Measurement.IsKnown).Aggregate((UInt128)0, (sum, f) => sum + f.Measurement.Value!.Value), rows.Aggregate((UInt128)0, (sum, r) => sum + r.Assigned.Known));
    }
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task GroupedChartsPreserveScopeAreaAndSeparateSharedChargesFromPagedMemberReferences(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(metric, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        var page = await groups.ReadGroupsAsync(maximum: 1, cancellation: Cancellation);
        var overview = AnalysisGroupedChart.Overview(s.Identity, groups.Dimension, metric, groups.Totals, groups.LogicalTotals, page);
        Assert.Equal(AnalysisGroupedChart.Area(groups.Totals, metric), overview.ChildrenOf(overview.Scope).Aggregate((UInt128)0, (sum, item) => sum + item.Value));
        Assert.NotNull(overview.RemainderKey); Assert.Equal(page.NextOffset, overview.RemainderOffset);
        foreach (var group in await AllGroups(groups))
        {
            var members = await groups.ReadMemberRowsAsync(group.Definition.Key, maximum: 1, cancellation: Cancellation);
            var chart = AnalysisGroupedChart.Members(s.Identity, groups.Dimension, metric, group, members);
            Assert.Equal(AnalysisGroupedChart.Area(group.Totals, metric), chart.ChildrenOf(chart.Scope).Aggregate((UInt128)0, (sum, item) => sum + item.Value));
            var shared = chart.ChildrenOf(chart.Scope).Where(item => item.Family == "shared").ToArray();
            if (group.Shared.Known != 0 || group.Shared.IdentityUnverified != 0 || group.Shared.Missing.Count != 0)
                Assert.Equal(AnalysisGroupedChart.Area(group.Shared, metric), Assert.Single(shared).Value);
            else Assert.Empty(shared);
        }
    }
    [Fact]
    public async Task GroupedChartColorMeaningUsesItsGroupingAndCapturedSourceDepth()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisGroupingTests.Fixture();
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.FileType, cancellation: Cancellation);
        var root = await groups.ReadGroupsAsync(cancellation: Cancellation); var chart = AnalysisGroupedChart.Overview(s.Identity, groups.Dimension, groups.Metric, groups.Totals, groups.LogicalTotals, root);
        var code = Assert.Single(root.Rows, group => group.Definition.Value == "type:code");
        Assert.Equal("code", AnalysisChartColors.Category(chart, chart.Items[code.Definition.Key], AnalysisColorDimension.FileType).Key);
        var other = Assert.Single(root.Rows, group => group.Definition.Value == "type:other");
        Assert.Equal("other-type", AnalysisChartColors.Category(chart, chart.Items[other.Definition.Key], AnalysisColorDimension.FileType).Key);
        var members = await groups.ReadMemberRowsAsync(code.Definition.Key, cancellation: Cancellation);
        var child = AnalysisGroupedChart.Members(s.Identity, groups.Dimension, groups.Metric, code, members);
        Assert.Equal(3, child.Items["n:4"].CapturedDepth);
        Assert.Equal("depth-3", AnalysisChartColors.Category(child, child.Items["n:4"], AnalysisColorDimension.HierarchyDepth).Key);
        Assert.Throws<ArgumentException>(() => AnalysisGroupedChart.Overview(s.Identity, groups.Dimension, groups.Metric, groups.Totals, groups.LogicalTotals,
            root with { Offset = 1 }));
    }
}
