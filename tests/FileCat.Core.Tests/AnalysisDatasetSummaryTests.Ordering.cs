using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndexedNameOrderAgreesWithOrdinalIgnoreCaseForNativeUnicodeAndPrefixNames(bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        string[] names = ["A", "a", "aa", "alpha", "B", "i", "I", "ı", "İ", "ſ", "K", "ß", "ẞ", "σ", "Σ", "ς", "µ", "Μ", "μ", "𐐨", "𐐀", "\uD800"];
        var nodes = snapshot.Nodes;
        for (int at = 0; at < names.Length; at++) nodes = nodes.Add(new AnalysisNode((ulong)at + 7, "r", 2, names[at], AnalysisKind.Link) { NameEncoding = "utf16le" });
        snapshot = snapshot with { Nodes = nodes };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(2, AnalysisHierarchySort.Name, cancellation: Cancellation, descending: descending);
        var children = nodes.Where(node => node.ParentId == 2);
        var expected = descending ? children.OrderByDescending(node => node.Name, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Id) : children.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(node => node.Id);
        Assert.Equal(expected.Select(node => node.Id), rows.Rows.Select(row => row.Path.Id));
        var page = await hierarchy.ReadChildrenAsync(2, AnalysisHierarchySort.Name, offset: 3, maximum: 4, cancellation: Cancellation, descending: descending);
        Assert.Equal(expected.Skip(3).Take(4).Select(node => node.Id), page.Rows.Select(row => row.Path.Id));
        Assert.Equal(AnalysisNames.DisplayName(nodes[^1]), AnalysisNames.DisplayName(rows.Rows.Single(row => row.Path.Id == nodes[^1].Id).Path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IndexedMetadataStateOrderKeepsKnownBeforeAllUnavailableStatesInBothDirections(bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        var nodes = snapshot.Nodes; ulong id = 7;
        foreach (var state in Enum.GetValues<MeasurementState>().Where(state => state != MeasurementState.Known))
            nodes = nodes.Add(new AnalysisNode(id++, "r", 2, state.ToString(), AnalysisKind.Link) { MetadataState = state, IncludedByPolicy = state != MeasurementState.Excluded });
        snapshot = snapshot with { Nodes = nodes };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(2, AnalysisHierarchySort.MetadataState, cancellation: Cancellation, descending: descending);
        Assert.All(rows.Rows.Take(2), row => Assert.Equal(MeasurementState.Known, row.Path.MetadataState));
        var missing = rows.Rows.Skip(2).Select(row => row.Path.MetadataState);
        var expected = Enum.GetValues<MeasurementState>().Where(state => state != MeasurementState.Known);
        Assert.Equal(descending ? expected.OrderDescending() : expected.Order(), missing);
        Assert.Equal(3UL, hierarchy.LogicalTotals.Files); // Namespace evidence never becomes a matching file.
    }

    [Fact]
    public async Task IndexedNameKeyCanExceed64KiBWithoutTruncatingItsCapturedName()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden(); string name = new('a', AnalysisLimits.RawNameBytes);
        snapshot = snapshot with { Nodes = snapshot.Nodes.SetItem(3, snapshot.Nodes[3] with { Name = name }) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(2, AnalysisHierarchySort.Name, cancellation: Cancellation, descending: false);
        Assert.Equal(name, rows.Rows[0].Path.Name); Assert.Equal(4UL, rows.Rows[0].Path.Id);
        Assert.Equal("z.bin", rows.Rows[1].Path.Name);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedFileSizeOrderUsesTheSameLogicalColumnAcrossMetricsAndPagedDirections(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, metric, cancellation: Cancellation);
        Assert.True(await explorer.LoadPageAsync(1, AnalysisHierarchySort.FileSize, maximum: 1, cancellation: Cancellation));
        Assert.True(explorer.Page!.Descending); Assert.Equal(2UL, Assert.Single(explorer.Page.Children.Rows).Path.Id);
        Assert.True(await explorer.LoadPageAsync(1, AnalysisHierarchySort.FileSize, maximum: 1, cancellation: Cancellation, descending: false));
        Assert.False(explorer.Page!.Descending); Assert.Equal(3UL, Assert.Single(explorer.Page.Children.Rows).Path.Id);
        Assert.Equal((UInt128)100, explorer.Page.Children.Rows[0].Logical.KnownBytes);
        Assert.True(await explorer.LoadPageAsync(1, AnalysisHierarchySort.FileSize, offset: 1, maximum: 1, cancellation: Cancellation, descending: false));
        Assert.Equal(2UL, Assert.Single(explorer.Page!.Children.Rows).Path.Id);
        Assert.Equal((UInt128)220, explorer.LogicalTotals.KnownBytes);
    }

    [Theory]
    [InlineData(AnalysisHierarchySort.Metric, true)]
    [InlineData(AnalysisHierarchySort.Metric, false)]
    [InlineData(AnalysisHierarchySort.FileSize, true)]
    [InlineData(AnalysisHierarchySort.FileSize, false)]
    public async Task IndexedNumericOrdersKeepKnownZeroBeforeMissingAndNamespaceLeaves(AnalysisHierarchySort sort, bool descending)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Objects = [snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }, snapshot.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }],
            Nodes = snapshot.Nodes.Add(new(7, "r", 2, "link", AnalysisKind.Link))
        };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(2, sort, cancellation: Cancellation, descending: descending);
        Assert.Equal(new ulong[] { 6, 4, 7 }, rows.Rows.Select(row => row.Path.Id));
        var tail = await hierarchy.ReadChildrenAsync(2, sort, offset: 1, maximum: 1, cancellation: Cancellation, descending: descending);
        Assert.Equal(4UL, Assert.Single(tail.Rows).Path.Id);
    }

    [Fact]
    public async Task IndexedLogicalOrderRetainsFull128BitPrecisionAndFilteredMembership()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Known((UInt128)1 << 100) }) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:x.bin"), Cancellation);
        var rows = await hierarchy.ReadChildrenAsync(1, AnalysisHierarchySort.FileSize, cancellation: Cancellation, descending: false);
        Assert.Equal(new ulong[] { 3, 2 }, rows.Rows.Select(row => row.Path.Id));
        Assert.Equal((UInt128)0, rows.Rows[0].Logical.KnownBytes); Assert.Equal((UInt128)1 << 100, rows.Rows[1].Logical.KnownBytes);
        Assert.False(rows.Rows[0].IsMatch); Assert.Equal(1UL, hierarchy.LogicalTotals.Files);
    }
}
