using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class ListingDisposeOwnershipTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 16)]
    [InlineData(false, 4096)]
    [InlineData(false, 65536)]
    [InlineData(true, 16)]
    [InlineData(true, 4096)]
    [InlineData(true, 65536)]
    public async Task Closed_listing_retires_owned_rows_marks_sizes_and_remembered_items(bool retire, int count)
    {
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        using var rows = new OwnedRows(count);
        var providers = new ProviderRegistry(); providers.Register(rows);
        var budget = new IndexMemoryBudget();
        var model = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, indexBudget: budget));
        try
        {
            await ui.InvokeAsync(() => model.Load(new Location(rows.Scheme, "owned")));
            await ui.WaitUntilAsync(() => model.State == ListingState.Complete);
            var captured = await ui.InvokeAsync(() => Capture(model, count));
            if (retire) await ui.InvokeAsync(() => { model.Dispose(); model.Dispose(); });
            if (retire) await ui.WaitUntilAsync(() => budget.ReservedBytes == 0);
            await ui.InvokeAsync(() => { });
            await Collect();
            var alive = captured.Values.Select(v => v.IsAlive).ToArray();
            output.WriteLine("LISTING_DISPOSE_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                retire, count, captured.VisibleBytes, captured.PositionBytes, alive,
                disposed = model.IsDisposed, reserved = budget.ReservedBytes,
                visible = model.VisibleCount, total = model.TotalCount,
                modelHeldDuringObservation = true, syntheticRowsOnly = true
            }));
            Assert.All(alive, value => Assert.Equal(!retire, value));
            if (retire)
            {
                Assert.Equal(0, model.VisibleCount); Assert.Equal(0, model.TotalCount);
                Assert.False(model.HasMarks); Assert.False(model.HasLastOperation);
                Assert.False(model.TryGetFocused(out _));
            }
            else await ui.InvokeAsync(() =>
            {
                Assert.Equal(count, model.MarkedCount);
                Assert.Equal(count, model.GetSelection().Count);
                Assert.Equal("owned-00000000", model.GetVisible(0).Name);
                Assert.Equal(42, model.GetVisible(0).Size);
            });
            GC.KeepAlive(model);
        }
        finally { await ui.InvokeAsync(model.Dispose); }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(4096)]
    [InlineData(65536)]
    public async Task Closing_listing_keeps_a_separately_leased_selection_readable(int count)
    {
        using var ui = new TestDispatcher(); using var io = new DeviceIoScheduler();
        using var rows = new OwnedRows(count);
        var providers = new ProviderRegistry(); providers.Register(rows);
        var model = await ui.InvokeAsync(() => new ListingModel(providers, io, ui));
        SelectionSnapshot? snapshot = null;
        try
        {
            await ui.InvokeAsync(() => model.Load(new Location(rows.Scheme, "owned")));
            await ui.WaitUntilAsync(() => model.State == ListingState.Complete);
            snapshot = await ui.InvokeAsync(() =>
            {
                model.MarkAll(true); model.SnapshotThreshold = 0;
                var selection = Assert.IsType<SelectionSnapshot>(model.GetSelection());
                model.RememberOperation(selection); model.Dispose(); return selection;
            });
            await Collect();
            Assert.Equal(count, snapshot.Count);
            Assert.Equal("owned-00000000", snapshot[0].Name);
            Assert.Equal("owned-" + (count - 1).ToString("D8"), snapshot[count - 1].Name);
            Assert.Equal(count, snapshot.Count(s => s.Kind == EntryKind.Directory));
            output.WriteLine("LISTING_DISPOSE_LEASE " + JsonSerializer.Serialize(new
            { count, ownerDisposed = model.IsDisposed, liveLeaseReadable = true, first = snapshot[0].Name, last = snapshot[count - 1].Name }));
            snapshot.Release();
            await ui.WaitUntilAsync(() =>
            {
                try { _ = model.Store[0]; return false; }
                catch (ObjectDisposedException) { return true; }
            });
            GC.KeepAlive(model);
        }
        finally { snapshot?.Release(); await ui.InvokeAsync(model.Dispose); }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(4096)]
    [InlineData(65536)]
    public async Task Closing_streaming_listing_drops_published_rows_and_prevents_late_repopulation(int count)
    {
        using var ui = new TestDispatcher(); using var io = new DeviceIoScheduler();
        using var rows = new OwnedRows(count, hold: true);
        var providers = new ProviderRegistry(); providers.Register(rows);
        var budget = new IndexMemoryBudget();
        var model = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, indexBudget: budget));
        try
        {
            await ui.InvokeAsync(() => model.Load(new Location(rows.Scheme, "owned")));
            await ui.WaitUntilAsync(() => model.VisibleCount > 0);
            Assert.Equal(ListingState.Loading, model.State);
            // Enumeration is deliberately still open. Only the rows already published belong to this view;
            // the pipeline need not publish its final count until enumeration completes.
            var captured = await ui.InvokeAsync(() => Capture(model, model.VisibleCount));
            await ui.InvokeAsync(model.Dispose); rows.Release();
            await ui.WaitUntilAsync(() => budget.ReservedBytes == 0);
            await ui.InvokeAsync(() => { }); await Collect();
            var alive = captured.Values.Select(v => v.IsAlive).ToArray();
            output.WriteLine("LISTING_DISPOSE_STREAM " + JsonSerializer.Serialize(new
            { count, published = captured.VisibleBytes / 4, alive, disposed = model.IsDisposed, reserved = budget.ReservedBytes, visible = model.VisibleCount, total = model.TotalCount }));
            Assert.All(alive, value => Assert.False(value));
            Assert.Equal(0, model.VisibleCount); Assert.Equal(0, model.TotalCount);
            GC.KeepAlive(model);
        }
        finally { rows.Release(); await ui.InvokeAsync(model.Dispose); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Captured Capture(ListingModel model, int count)
    {
        model.MarkAll(true); model.SetComputedSize("owned-00000000", 42, true);
        model.SnapshotThreshold = int.MaxValue;
        model.RememberOperation(model.GetSelection());
        object Field(string name) => typeof(ListingModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
        var visible = (int[])Field("_visible"); var positions = (int[])Field("_positions");
        Assert.Equal(count, visible.Length); Assert.Equal(count, positions.Length);
        Assert.Equal(count, model.MarkedCount);
        return new([new(visible), new(positions), new(Field("_marks")), new(Field("_computedSizes")), new(Field("_lastOperation"))], visible.LongLength * 4, positions.LongLength * 4);
    }

    private static async Task Collect()
    {
        await Task.Delay(100);
        for (int i = 0; i < 3; i++)
        { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(10); }
    }
    private sealed record Captured(WeakReference[] Values, long VisibleBytes, long PositionBytes);

    private sealed class OwnedRows(int count, bool hold = false) : ResourceProvider, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(!hold);
        public override string Scheme => "owned-listing-retirement";
        public override string GetDisplayPath(Location l) => l.Path;
        public override Location? GetParent(Location l) => null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location p, in EntryData e) => null;
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            for (int i = 0; i < count; i += 1024)
            {
                ct.ThrowIfCancellationRequested();
                sink.AddBatch(Enumerable.Range(i, Math.Min(1024, count - i)).Select(n =>
                    new EntryData("owned-" + n.ToString("D8"), EntryKind.Directory, 1)).ToArray());
            }
            _release.Wait(ct); return Task.CompletedTask;
        }
        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }
}
