using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task NativeIndexedComparisonKeepsBothInputsAndExactMetricRowsAfterTheirSourceOwnersClose(AnalysisMetric metric)
    {
        OrdinaryPreparation(); var token = TestContext.Current.CancellationToken;
        await using var a = await IndexedFixture.CreateAsync(sharedAlias: true); await using var b = await IndexedFixture.CreateAsync(sharedAlias: true);
        var mapping = new Dictionary<string, string> { [a.Lease!.Descriptor.Roots.Single().Id] = b.Lease!.Descriptor.Roots.Single().Id };
        var expectedA = await a.Lease.SummarizeAsync(metric, token); var expectedB = await b.Lease.SummarizeAsync(metric, token);
        await using var result = await b.Lease.CompareIndexedAsync(a.Lease, metric, mapping, token);
        await a.ReleaseInitialLeaseAsync(); await b.ReleaseInitialLeaseAsync();
        Assert.False(result.Compatibility.VerifiedIdentityContext); Assert.True(result.Compatibility.SameScope); Assert.True(result.Compatibility.SamePolicy);
        Assert.True(result.Compatibility.PreviousComplete); Assert.True(result.Compatibility.CurrentComplete);
        Assert.Equal(expectedA.Totals.Known, result.PreviousTotals.Known); Assert.Equal(expectedB.Totals.Known, result.CurrentTotals.Known);
        Assert.Equal(metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk ? 303UL : 304UL, result.Count);
        ulong offset = 0, previousPaths = 0, currentPaths = 0;
        while (true)
        {
            var page = await result.ReadAsync(offset, 41, token); Assert.InRange(page.Rows.Length, 1, 41);
            foreach (var row in page.Rows)
            {
                Assert.NotNull(row.Previous); Assert.NotNull(row.Current); Assert.Equal(AnalysisComparisonEvidence.Structural, row.Evidence);
                previousPaths += row.Previous.ObservedPaths; currentPaths += row.Current.ObservedPaths;
                Assert.Equal(row.Previous.Value, row.Current.Value);
            }
            if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
        }
        Assert.Equal(304UL, previousPaths); Assert.Equal(304UL, currentPaths);
        Assert.Equal(a.Payload, File.ReadAllBytes(a.PayloadPath)); Assert.Equal(b.Payload, File.ReadAllBytes(b.PayloadPath));
    }
}
