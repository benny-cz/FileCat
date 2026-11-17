using FileCat.Core.HiddenData;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class HiddenInventoryRefreshTests
{
    private sealed class Split : ResourceProvider, IDisposable
    {
        private int _calls;
        public bool RemoveSelected;
        public readonly ManualResetEventSlim Finish = new();
        public override string Scheme => Schemes.HiddenData;
        public override string GetDisplayPath(Location location) => "fixture";
        public override Location? GetParent(Location location) => location.Container;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override string GetDeviceKey(Location location) => "split-inventory";
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override ItemRef GetItemRef(Location listing, in EntryData entry) =>
            new(listing, entry.Name, entry.Kind) { Ordinal = HiddenIdentities.Ordinal(((HiddenEntryTag)entry.Tag!).Item.Kind) };
        private static EntryData Row(HiddenKind kind) => new("same", EntryKind.File, 5) { Tag = new HiddenEntryTag(new("same", kind, 5), "Not read", []) };
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                sink.AddBatch([Row(HiddenKind.NtfsAttribute)]);
                Finish.Wait(ct); // initial loading publishes the selected category before restart
            }
            else
            {
                sink.AddBatch([Row(HiddenKind.Stream)]);
                Finish.Wait(ct); // restarted loading publishes the other category first
                if (!RemoveSelected) sink.AddBatch([Row(HiddenKind.NtfsAttribute)]);
            }
            return Task.CompletedTask;
        }
        public void Dispose() { Finish.Set(); Finish.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loading_restart_keeps_full_category_identity_through_split_reversed_arrival(bool missing)
    {
        using var ui = new TestDispatcher(); using var io = new DeviceIoScheduler(); using var provider = new Split { RemoveSelected = missing };
        var registry = new ProviderRegistry(); registry.Register(provider);
        var model = await ui.InvokeAsync(() => new ListingModel(registry, io, ui));
        try
        {
            await ui.InvokeAsync(() => model.Load(HiddenDataProvider.Of(Path.Combine(Path.GetTempPath(), "subject"))));
            await ui.WaitUntilAsync(() => model.VisibleCount == 2);
            await ui.InvokeAsync(() => { model.SetFocus(1); model.SetMark(1, true); model.Refresh(); });
            await ui.WaitUntilAsync(() => model.VisibleCount == 2 && model.GetVisible(1).Tag is HiddenEntryTag { Item.Kind: HiddenKind.Stream });
            await ui.InvokeAsync(() => Assert.False(model.IsVisibleMarked(1)));
            provider.Finish.Set();
            await ui.WaitUntilAsync(() => model.State == ListingState.Complete);
            await ui.InvokeAsync(() =>
            {
                if (missing)
                {
                    Assert.Equal(0, model.MarkedCount);
                    Assert.True(model.TryGetFocused(out var focused)); Assert.Equal(EntryKind.Parent, focused.Kind);
                }
                else
                {
                    Assert.Equal(3, Assert.Single(model.GetSelection()).Ordinal);
                    Assert.Equal(3, model.GetItemRef(model.FocusedStoreIndex).Ordinal);
                }
            });
            if (missing)
            {
                await ui.InvokeAsync(model.Refresh);
                await ui.WaitUntilAsync(() => model.State == ListingState.Complete && !model.IsRefreshing);
                await ui.InvokeAsync(() =>
                {
                    Assert.True(model.TryGetFocused(out var focused)); Assert.Equal(EntryKind.Parent, focused.Kind);
                    Assert.Equal(0, model.MarkedCount);
                });
            }
        }
        finally { provider.Finish.Set(); await ui.InvokeAsync(model.Dispose); }
    }
}
