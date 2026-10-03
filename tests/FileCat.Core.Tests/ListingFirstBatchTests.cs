using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>V12/V16: even a single delayed first entry becomes visible before the slow enumeration ends.</summary>
public sealed class ListingFirstBatchTests
{
    [Theory]
    [InlineData(false, false, "alpha.txt")]
    [InlineData(false, true, "zeta.txt")]
    [InlineData(true, false, "..")]
    [InlineData(true, true, "zeta.txt")]
    public async Task First_real_row_after_an_empty_or_parent_only_view_is_shown_while_loading(
        bool parent, bool moveFocus, string expectedFocus)
    {
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        using var scratch = new TempDir();
        using var firstPublished = new ManualResetEventSlim();
        using var provider = new Delayed(parent);
        var providers = new ProviderRegistry();
        providers.Register(provider);
        var model = await ui.InvokeAsync(() => new ListingModel(providers, io, ui));
        int initialCount = parent ? 1 : 0;
        await ui.InvokeAsync(() =>
        {
            model.Changed += (_, change) =>
            {
                // Reset alone is insufficient: wait until the sorter has actually published its empty view.
                if ((change & ListingChange.Rows) != 0 && model.VisibleCount == initialCount)
                    firstPublished.Set();
            };
            model.Load(Location.FileSystem(scratch.Path));
        });
        try
        {
            try
            {
                Assert.True(firstPublished.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken),
                    "The initial empty/parent-only sorted view was not observed.");
                provider.First.Set();
                await ui.WaitUntilAsync(() => model.VisibleCount == initialCount + 1, timeoutMs: 2000);
                await ui.InvokeAsync(() =>
                {
                    Assert.Equal(ListingState.Loading, model.State);
                    Assert.Equal("zeta.txt", model.GetVisible(initialCount).Name);
                    if (moveFocus) model.SetFocus(initialCount);
                });
            }
            finally { provider.First.Set(); provider.Finish.Set(); }
            await ui.WaitUntilAsync(() => model.State == ListingState.Complete);
            await ui.InvokeAsync(() =>
            {
                Assert.Equal(parent ? new[] { "..", "alpha.txt", "zeta.txt" } : new[] { "alpha.txt", "zeta.txt" },
                    Enumerable.Range(0, model.VisibleCount).Select(i => model.GetVisible(i).Name));
                Assert.True(model.TryGetFocused(out var focused));
                Assert.Equal(expectedFocus, focused.Name);
            });
        }
        finally
        {
            provider.First.Set();
            provider.Finish.Set();
            await ui.InvokeAsync(model.Dispose);
        }
    }

    private sealed class Delayed(bool parent) : ResourceProvider, IDisposable
    {
        public readonly ManualResetEventSlim First = new();
        public readonly ManualResetEventSlim Finish = new();
        public override string Scheme => Schemes.FileSystem;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => parent ? location.WithPath("parent") : null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parentLocation, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            First.Wait(ct);
            sink.AddBatch([new EntryData("zeta.txt", EntryKind.File)]);
            Finish.Wait(ct);
            sink.AddBatch([new EntryData("alpha.txt", EntryKind.File)]);
            return Task.CompletedTask;
        }
        public void Dispose() { First.Dispose(); Finish.Dispose(); }
    }
}
