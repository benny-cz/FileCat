using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisChartColorsTests
{
    private static readonly DateTimeOffset Captured = DateTimeOffset.Parse("2026-11-04T12:34:56Z");
    [Theory]
    [InlineData(7, "age-7", "age-30")]
    [InlineData(30, "age-30", "age-180")]
    [InlineData(180, "age-180", "age-365")]
    [InlineData(365, "age-365", "age-old")]
    public void AgeBandsHonorExactCapturedUtcBoundariesAndTimestampUncertainty(int days, string younger, string older)
    {
        var boundary = Captured.AddDays(-days);
        Assert.Equal(younger, AnalysisChartColors.Age(boundary, 1, Captured));
        Assert.Equal(older, AnalysisChartColors.Age(boundary.AddTicks(-1), 1, Captured));
        Assert.Equal("unknown", AnalysisChartColors.Age(boundary.AddTicks(-1), 2, Captured));
        Assert.Equal(younger, AnalysisChartColors.Age(boundary.ToOffset(TimeSpan.FromHours(9)), 1, Captured));
    }
    [Fact]
    public void MissingOverflowingAndFutureIntervalsAreExplicitAndNeverDependOnTheCurrentClock()
    {
        Assert.Equal("unknown", AnalysisChartColors.Age(null, 1, Captured));
        Assert.Equal("unknown", AnalysisChartColors.Age(Captured, 0, Captured));
        Assert.Equal("unknown", AnalysisChartColors.Age(DateTimeOffset.MaxValue, 2, Captured));
        Assert.Equal("unknown", AnalysisChartColors.Age(Captured.AddTicks(-1), 3, Captured));
        Assert.Equal("age-future", AnalysisChartColors.Age(Captured.AddTicks(1), 1, Captured));
        Assert.Equal("age-old", AnalysisChartColors.Age(DateTimeOffset.UnixEpoch, 1, Captured));
    }
    [Fact]
    public void TypeDepthAgeAndSyntheticCategoriesShareAnExplicitLegendWithoutChangingAccounting()
    {
        var source = AnalysisAccountingTests.Golden(); var cancel = TestContext.Current.CancellationToken;
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, AnalysisMetric.OnDisk, cancellation: cancel), false, cancel);
        var file = tree.Items["n:6"] with { ModifiedUtc = Captured.AddDays(-14), ModifiedPrecisionTicks = 1 };
        Assert.Equal("other-type", Category(file, AnalysisColorDimension.FileType));
        Assert.Equal("folder", Category(tree.Items["n:2"], AnalysisColorDimension.FileType));
        Assert.Equal("depth-3", Category(file, AnalysisColorDimension.HierarchyDepth));
        foreach (string family in new[] { "text", "code", "image", "audio", "video", "archive", "executable", "document", "unknown" })
            Assert.Equal(family, Category(file with { Family = family }, AnalysisColorDimension.FileType));
        foreach (var mode in Enum.GetValues<AnalysisColorDimension>())
        {
            Assert.Equal("shared", Category(file with { Family = "shared", Synthetic = true }, mode));
            Assert.Equal("directory-allocation", Category(file with { Family = "directory-allocation", Synthetic = true }, mode));
            Assert.Equal("other", Category(file with { Family = "other", Synthetic = true }, mode));
            Assert.Equal(AnalysisChartColors.Legend(mode).Length, AnalysisChartColors.Legend(mode).Select(c => c.Key).Distinct().Count());
        }
        Assert.Equal("unknown", Category(file with { MetadataState = MeasurementState.Changed }, AnalysisColorDimension.ModifiedAge));
        Assert.Equal("unknown", Category(file with { IncludedByPolicy = false }, AnalysisColorDimension.ModifiedAge));
        Assert.Equal("unknown", Category(file with { Kind = AnalysisKind.Link }, AnalysisColorDimension.FileType));
        Assert.Equal("unknown", Category(file with { ParentKey = "absent" }, AnalysisColorDimension.HierarchyDepth));
        Assert.Equal((UInt128)80, tree.ScopeValue(AnalysisTree.RootKey));
        string Category(AnalysisTreeItem item, AnalysisColorDimension mode) => AnalysisChartColors.Category(tree, item, mode).Key;
    }
    [Fact]
    public void DepthIsAbsoluteAndMalformedCyclesReturnUnknownWithinTheCapturedDepthBound()
    {
        var rows = ImmutableDictionary.CreateBuilder<string, AnalysisTreeItem>();
        for (int i = 0; i <= 12; i++)
        {
            string key = i == 0 ? AnalysisTree.RootKey : "n:" + i;
            rows.Add(key, new(key, i <= 1 ? AnalysisTree.RootKey : "n:" + (i - 1), key, null, AnalysisKind.Directory, false, true, 1, 1, 1, false, false, "folder", [], []));
        }
        var data = new Chart(rows.ToImmutable());
        Assert.Equal(new[] { "depth-0", "depth-1", "depth-1", "depth-3", "depth-3", "depth-3", "depth-6", "depth-6", "depth-6", "depth-6", "depth-6", "depth-11", "depth-11" },
            Enumerable.Range(0, 13).Select(i => AnalysisChartColors.Category(data, data.Items[i == 0 ? AnalysisTree.RootKey : "n:" + i], AnalysisColorDimension.HierarchyDepth).Key));
        var cycle = new Chart(data.Items.SetItem("n:1", data.Items["n:1"] with { ParentKey = "n:2" }));
        Assert.Equal("unknown", AnalysisChartColors.Category(cycle, cycle.Items["n:2"], AnalysisColorDimension.HierarchyDepth).Key);
    }
    private sealed class Chart(ImmutableDictionary<string, AnalysisTreeItem> items) : IAnalysisChartData
    {
        public AnalysisMetric Metric => AnalysisMetric.Files;
        public DateTimeOffset ObservedUtc => Captured;
        public ImmutableDictionary<string, AnalysisTreeItem> Items => items;
        public string ParentOf(string key) => Items[key].ParentKey;
        public string NameOf(string key) => Items[key].Name;
        public UInt128 ScopeValue(string key) => Items[key].Value;
        public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key) => [];
    }
}
