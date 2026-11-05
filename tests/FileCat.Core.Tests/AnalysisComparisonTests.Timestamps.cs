using System.Globalization;
using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisComparisonTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize, false)]
    [InlineData(AnalysisMetric.UniqueFileSize, false)]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.Files, false)]
    [InlineData(AnalysisMetric.FileSize, true)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    [InlineData(AnalysisMetric.Files, true)]
    public void InventoryRoundTripPreservesMetadataAcrossTimestampOffsets(AnalysisMetric metric, bool structural)
    {
        var instant = DateTimeOffset.Parse("2026-11-05T09:12:00.1234567+05:45", CultureInfo.InvariantCulture);
        var source = Sample(("same", "k", 10, 8, "v"));
        source = source with
        {
            Identity = source.Identity with { StartedUtc = instant, ObservedUtc = instant },
            Facts = source.Facts.Select(f => f with { ObservedUtc = instant }).ToImmutableArray(),
            Objects = [source.Objects[0] with { ModifiedUtc = instant }],
            Nodes = source.Nodes.SetItem(1, source.Nodes[1] with
            { Times = new(instant, instant, 1, instant.AddDays(-1), 1, instant.AddSeconds(-2), 1) })
        };
        using var stream = new MemoryStream(); AnalysisInventoryFormat.Write(stream, source, Guid.NewGuid(), Cancellation); stream.Position = 0;
        using var previous = AnalysisInventoryFormat.Read(stream, Cancellation);
        Assert.Equal(TimeSpan.Zero, previous.Snapshot.Objects[0].ModifiedUtc!.Value.Offset);
        Assert.NotEqual(TimeSpan.Zero, source.Objects[0].ModifiedUtc!.Value.Offset);
        using var comparison = AnalysisComparison.Build(previous.Snapshot, source, metric,
            structural ? new Dictionary<string, string> { [previous.Snapshot.Roots[0].Id] = source.Roots[0].Id } : null, Cancellation);
        Assert.Equal(new AnalysisDelta(false, 0), comparison.NetChange);
        Assert.Equal(AnalysisChange.NoObservedMetadataChange, Assert.Single(comparison.Rows).Change);
    }
}
