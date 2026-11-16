using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public async Task NativeIndexedComparisonPathsRetainBothCapturedStoresAndExactBidirectionalPairs()
    {
        OrdinaryPreparation(); var token = TestContext.Current.CancellationToken;
        await using var previous = await IndexedFixture.CreateAsync(sharedAlias: true);
        await using var current = await IndexedFixture.CreateAsync(sharedAlias: true);
        Assert.Equal(previous.Lease!.Descriptor.Paths, current.Lease!.Descriptor.Paths);
        var mapping = new Dictionary<string, string>
        { [previous.Lease.Descriptor.Roots.Single().Id] = current.Lease.Descriptor.Roots.Single().Id };
        await using var paths = await current.Lease.MatchPathsAsync(previous.Lease, mapping, token);
        Assert.Equal(306UL, paths.Current.Paths);
        await previous.ReleaseInitialLeaseAsync(); await current.ReleaseInitialLeaseAsync();
        foreach (bool old in new[] { false, true })
        {
            ulong offset = 0;
            while (true)
            {
                var page = old ? await paths.ReadPreviousAsync(offset, 31, token) : await paths.ReadCurrentAsync(offset, 31, token);
                Assert.InRange(page.Rows.Length, 1, 31); Assert.Equal(306UL, page.Total);
                foreach (var pair in page.Rows)
                {
                    Assert.True(pair.Mapped); Assert.NotNull(pair.Previous); Assert.NotNull(pair.Current);
                    Assert.Equal(pair.Previous.NameEncoding, pair.Current.NameEncoding);
                    Assert.Equal(pair.Previous.RawName.ToArray(), pair.Current.RawName.ToArray());
                    Assert.Equal(pair.Previous.Name, pair.Current.Name); Assert.Equal(pair.Previous.Kind, pair.Current.Kind);
                }
                Assert.True(page.NextOffset > offset); offset = page.NextOffset;
                if (!page.HasMore) break;
            }
            Assert.Equal(306UL, offset);
        }
        Assert.Equal(previous.Payload, File.ReadAllBytes(previous.PayloadPath));
        Assert.Equal(current.Payload, File.ReadAllBytes(current.PayloadPath));
    }
}
