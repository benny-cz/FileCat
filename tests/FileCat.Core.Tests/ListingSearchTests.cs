using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class ListingSearchTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestDispatcher _ui = new();
    private readonly DeviceIoScheduler _io = new();
    private readonly TempDir _scratch = new();
    private readonly List<ListingModel> _models = [];

    private async Task<ListingModel> Load(bool spill, bool external)
    {
        var providers = new ProviderRegistry();
        providers.Register(new Names());
        var model = await _ui.InvokeAsync(() => new ListingModel(providers, _io, _ui, _scratch.Path,
            listingMemoryBudgetBytes: spill ? 1024 : 96L << 20,
            indexBudget: new IndexMemoryBudget(external ? 512 : 512L << 20)));
        _models.Add(model);
        await _ui.InvokeAsync(() => model.Load(Location.FileSystem(Path.Combine(_scratch.Path, "root"))));
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        Assert.Equal(spill, await _ui.InvokeAsync(() => model.Store.IsSpilled));
        Assert.Equal(external, await _ui.InvokeAsync(() => model.HasExternalIndex));
        return model;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Background_search_uses_display_order_wraps_and_never_changes_marks_or_focus(bool spill, bool external)
    {
        var model = await Load(spill, external);
        await _ui.InvokeAsync(() => { model.SetFocus(4); model.SetMark(8, true); });
        foreach (var (start, forward, expected) in new[] { (0, true, 8), (8, true, 8), (9, true, 18),
                     (7, false, 598), (-1, true, 8), (601, false, 598) })
        {
            var task = await _ui.InvokeAsync(() => model.FindVisibleAsync(start, forward,
                n => n.EndsWith("7.txt", StringComparison.Ordinal)));
            var result = await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await _ui.InvokeAsync(() =>
            {
                Assert.True(result.IsCurrent);
                Assert.Equal(expected, result.Row);
                Assert.Equal(4, model.FocusedIndex);
                Assert.Equal(1, model.MarkedCount);
            });
        }
        var parent = await (await _ui.InvokeAsync(() => model.FindVisibleAsync(0, true, _ => true)));
        Assert.Equal(1, parent.Row); // ".." must never match, even when every name matches.
        var miss = await (await _ui.InvokeAsync(() => model.FindVisibleAsync(0, false, _ => false)));
        Assert.Equal(-1, miss.Row);
    }

    [Fact]
    public async Task A_held_name_scan_leaves_the_UI_free_and_cancellation_stops_at_a_bounded_batch()
    {
        var model = await Load(spill: true, external: true);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource();
        int visited = 0, worker = 0;
        var task = await _ui.InvokeAsync(() => model.FindVisibleAsync(0, true, _ =>
        {
            Assert.False(_ui.CheckAccess());
            worker = Environment.CurrentManagedThreadId;
            if (Interlocked.Increment(ref visited) == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
            return false;
        }, cancel.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            int uiThread = await _ui.InvokeAsync(() =>
            {
                cancel.Cancel();
                model.SetFocus(20);
                return Environment.CurrentManagedThreadId;
            }).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.NotEqual(uiThread, worker);
            Assert.False(task.IsCompleted); // the worker's predicate is still held; UI acknowledgement is independent.
            output.WriteLine($"UI thread {uiThread}; held worker {worker}; UI cancellation acknowledged before release.");
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.InRange(visited, 1, 128);
        Assert.Equal(20, await _ui.InvokeAsync(() => model.FocusedIndex));
        output.WriteLine($"Visited {visited} names before worker cancellation.");
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("filter")]
    [InlineData("refresh")]
    [InlineData("navigate")]
    [InlineData("dispose")]
    public async Task Retired_spilled_rows_and_external_indexes_survive_the_worker_but_never_supply_a_current_answer(string change)
    {
        var model = await Load(spill: true, external: true);
        string[] original = Directory.GetFiles(_scratch.Path, "listing-*");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int visited = 0;
        var task = await _ui.InvokeAsync(() => model.FindVisibleAsync(0, true, _ =>
        {
            if (Interlocked.Increment(ref visited) == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }
            return false;
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            await _ui.InvokeAsync(() =>
            {
                switch (change)
                {
                    case "sort": model.Sort = model.Sort with { Descending = true }; break;
                    case "filter": model.Filter = Mask.Parse("*7.txt"); break;
                    case "refresh": model.Refresh(); break;
                    case "navigate": model.Load(Location.FileSystem(Path.Combine(_scratch.Path, "next"))); break;
                    case "dispose": model.Dispose(); break;
                }
            }).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            if (change == "refresh") await _ui.WaitUntilAsync(() => !model.IsRefreshing);
            if (change is "navigate" or "dispose")
                Assert.Contains(original, File.Exists); // search leases still retain the old captured bytes/indexes.
        }
        finally { release.Set(); }
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(-1, result.Row);
        Assert.Equal(600, visited);
        Assert.False(await _ui.InvokeAsync(() => result.IsCurrent));
        await _ui.InvokeAsync(model.Dispose);
        await _ui.WaitUntilAsync(() => !Directory.EnumerateFiles(_scratch.Path, "listing-*").Any());
        output.WriteLine($"{change}: all 600 captured names remained readable; answer stale; spill/index files cleaned.");
    }

    public void Dispose()
    {
        foreach (var model in _models) _ui.InvokeAsync(model.Dispose).GetAwaiter().GetResult();
        _io.Dispose();
        _ui.Dispose();
        _scratch.Dispose();
    }

    private sealed class Names : ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => location.WithPath("parent");
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch(Enumerable.Range(0, 600).Select(i => new EntryData($"n{i:0000}.txt", EntryKind.File)).ToArray());
            return Task.CompletedTask;
        }
    }
}
