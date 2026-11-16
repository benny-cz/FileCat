using Avalonia.Headless.XUnit;
using FileCat.Core.Metadata;
using FileCat.Core.Verification;

namespace FileCat.App.Tests;

public sealed partial class OwnerColumnTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verification_report_graphs_keep_their_scalar_order_without_retaining_the_report(bool analyze)
    {
        using var f = new OwnerFixture(cacheSize: 8);
        int reads = 0;
        f.Services.Metadata.Register(BuiltInFields.Verified with
        {
            Cost = MetadataCost.Analysis,
            Produce = (path, _) =>
            {
                Interlocked.Increment(ref reads);
                var state = Sequence(path) % 2 == 0 ? VerificationState.Differs : VerificationState.Matches;
                return new VerificationResult(state, state.ToString(), new List<string> { "report detail" });
            },
        });
        await f.Load();
        if (analyze)
        {
            await f.Tab.AnalyzeAsync("verified");
            Assert.Contains("120 current values", f.Tab.Banner);
            Assert.Contains("retains all 120", f.Tab.Banner);
            Assert.Equal(Enumerable.Range(1, 120).OrderBy(i => i % 2),
                Enumerable.Range(0, 120).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
        }
        else
        {
            for (int i = 0; i < f.Tab.Listing.Store.Count; i++)
            {
                var entry = f.Tab.Listing.Store[i];
                if (Sequence(entry.Name) <= 2)
                    f.Services.Metadata.Compute("verified", Path.Combine(f.Tab.Location!.Path, entry.Name), entry, default);
            }
            f.Tab.SortByMetadata("verified");
            await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
            Assert.Equal(new[] { 2, 1, 3 }, Enumerable.Range(0, 3).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
        }

        int capturedReads = reads;
        f.Tab.SortByMetadata("verified");
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
        if (analyze)
            Assert.Equal(Enumerable.Range(1, 120).OrderByDescending(i => i % 2),
                Enumerable.Range(0, 120).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
        else Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(0, 3).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
        Assert.Equal(capturedReads, reads);
        Assert.Empty(f.Source.ReadPaths);
    }
}
