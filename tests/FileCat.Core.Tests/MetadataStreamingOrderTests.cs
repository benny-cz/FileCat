using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataStreamingOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streaming_appends_and_filters_keep_one_snapshot_in_memory_and_external_merges(bool external)
    {
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        using var scratch = new TempDir();
        using var provider = new Batches();
        var providers = new ProviderRegistry(); providers.Register(provider);
        var budget = new IndexMemoryBudget(external ? 8000 : 1_000_000);
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path,
            listingMemoryBudgetBytes: external ? 1 : long.MaxValue, indexBudget: budget));
        int calls = 0;
        bool changed = false;
        try
        {
            await ui.InvokeAsync(() => listing.Load(new Location("snapshot-batches", "root")));
            await ui.WaitUntilAsync(() => listing.VisibleCount == 4);
            await ui.InvokeAsync(() =>
            {
                listing.MetadataKeys = (_, index, _) => { Interlocked.Increment(ref calls); return changed ? -index : 4 - index; };
                listing.Sort = new SortSpec(SortField.Metadata) { MetadataId = "owner" };
            });
            await ui.WaitUntilAsync(() => listing.AppliedViewSpecVersion == listing.ViewSpecVersion);
            Assert.Equal(4, calls);
            changed = true; // Represents arbitrary later owner/name publication.
            await ui.InvokeAsync(() => listing.Filter = Mask.Parse("row*"));
            await ui.WaitUntilAsync(() => listing.AppliedViewSpecVersion == listing.ViewSpecVersion);
            Assert.Equal(4, calls);
            provider.Finish.Set();
            await ui.WaitUntilAsync(() => listing.State == ListingState.Complete && listing.VisibleCount == 304);
            await ui.InvokeAsync(() =>
            {
                Assert.Equal(new[] { "row0003", "row0002", "row0001", "row0000", "row0004" },
                    Enumerable.Range(0, 5).Select(i => listing.GetVisible(i).Name));
                Assert.Equal(external, listing.HasExternalIndex);
                Assert.Equal(4, calls);
                listing.Sort = listing.Sort with { Descending = true };
            });
            await ui.WaitUntilAsync(() => listing.AppliedViewSpecVersion == listing.ViewSpecVersion);
            Assert.True(calls > 4); // A direction change starts a bounded new capture, never a live comparison.
            await ui.InvokeAsync(listing.Dispose);
            await ui.WaitUntilAsync(() => budget.ReservedBytes == 0);
        }
        finally { provider.Finish.Set(); await ui.InvokeAsync(listing.Dispose); }
    }

    private sealed class Batches : ResourceProvider, IDisposable
    {
        public ManualResetEventSlim Finish { get; } = new();
        public override string Scheme => "snapshot-batches";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location location, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch(Enumerable.Range(0, 4).Select(i => new EntryData($"row{i:0000}", EntryKind.File)).ToArray());
            Finish.Wait(ct);
            sink.AddBatch(Enumerable.Range(4, 300).Select(i => new EntryData($"row{i:0000}", EntryKind.File)).ToArray());
            return Task.CompletedTask;
        }
        public void Dispose() => Finish.Dispose();
    }
}
