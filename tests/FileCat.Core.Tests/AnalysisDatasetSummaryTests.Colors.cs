using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task IndexedAndMemoryColorsUseTheSameCapturedTimesIncludingLegacyObjectFallbackAndChargeBuckets()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        s = s with { Identity = s.Identity with { StartedUtc = now, ObservedUtc = now },
            Nodes = s.Nodes.SetItem(1, s.Nodes[1] with { Times = new(now, now.AddDays(-14), 1) })
                .SetItem(5, s.Nodes[5] with { Times = new(now, null, 1) }),
            Objects = s.Objects.SetItem(0, s.Objects[0] with { ModifiedUtc = now.AddDays(-40) })
                .SetItem(1, s.Objects[1] with { ModifiedUtc = now.AddDays(-1) }),
            Facts = s.Facts.Select(f => f with { ObservedUtc = now }).Append(new("dir", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", now, [])).ToImmutableArray() };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        foreach (var metric in new[] { AnalysisMetric.Files, AnalysisMetric.OnDisk })
        {
            await using var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, metric, new(""), Cancellation);
            Assert.True(await explorer.LoadChartAsync(cancellation: Cancellation)); var chart = explorer.Chart!;
            var memory = AnalysisTree.Build(s, AnalysisAggregate.Project(s, metric, cancellation: Cancellation), false, Cancellation);
            Assert.Equal(memory.ObservedUtc, chart.ObservedUtc); Assert.Equal(memory.ScopeValue(AnalysisTree.RootKey), chart.ScopeValue(AnalysisTree.RootKey));
            foreach (var item in chart.Items.Values.Where(i => i.NodeId is not null))
            foreach (var mode in Enum.GetValues<AnalysisColorDimension>())
                Assert.Equal(AnalysisChartColors.Category(memory, memory.Items[item.Key], mode), AnalysisChartColors.Category(chart, item, mode));
            if (metric == AnalysisMetric.Files)
            {
                Assert.Equal("age-180", AnalysisChartColors.Category(chart, chart.Items["n:4"], AnalysisColorDimension.ModifiedAge).Key);
                Assert.Equal("unknown", AnalysisChartColors.Category(chart, chart.Items["n:6"], AnalysisColorDimension.ModifiedAge).Key);
            }
            else
            {
                Assert.Contains(chart.Items.Values, i => i.Family == "directory-allocation");
                Assert.Contains(chart.Items.Values, i => i.Family == "shared");
            }
        }
    }
}
