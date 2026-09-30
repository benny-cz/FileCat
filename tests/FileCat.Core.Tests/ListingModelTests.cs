using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class ListingModelTests : IDisposable
{
    private readonly TestDispatcher _ui = new();
    private readonly DeviceIoScheduler _io = new();
    private readonly ProviderRegistry _providers = new();
    private readonly TempDir _dir = new();

    public ListingModelTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _providers.Register(new ComputerProvider());
    }

    public void Dispose()
    {
        _io.Dispose();
        _ui.Dispose();
        _dir.Dispose();
    }

    private async Task<ListingModel> LoadAsync(string path, string? focus = null)
    {
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => model.Load(Location.FileSystem(path), focus));
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        return model;
    }

    private Task<string[]> VisibleNames(ListingModel m) =>
        _ui.InvokeAsync(() => Enumerable.Range(0, m.VisibleCount).Select(i => m.GetVisible(i).Name).ToArray());

    [Fact]
    public async Task Refresh_preserves_only_the_marked_kind_when_names_overlap()
    {
        _providers.Register(new SameNameProvider());
        var location = new Location("regtest", "root");
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => model.Load(location));
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        await _ui.InvokeAsync(() =>
        {
            int row = Enumerable.Range(0, model.VisibleCount).First(i => model.GetVisible(i).Kind == EntryKind.RegistryValue);
            model.SetFocus(row);
            model.SetMark(row, true);
            model.Refresh();
        });
        await _ui.WaitUntilAsync(() => !model.IsRefreshing && model.State == ListingState.Complete);
        var selected = await _ui.InvokeAsync(() => model.GetSelection());
        Assert.Single(selected);
        Assert.Equal(EntryKind.RegistryValue, selected[0].Kind);
        Assert.Equal(EntryKind.RegistryValue, await _ui.InvokeAsync(() => model.TryGetFocused(out var e) ? e.Kind : EntryKind.Parent));
        await _ui.InvokeAsync(model.Dispose);
    }

    private sealed class SameNameProvider : ResourceProvider
    {
        public override string Scheme => "regtest";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch([new EntryData("same", EntryKind.RegistryKey), new EntryData("same", EntryKind.RegistryValue)]);
            return Task.CompletedTask;
        }
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
    }

    [Fact]
    public async Task Whole_listing_operations_on_a_spilled_listing_match_an_in_memory_one()
    {
        for (int i = 0; i < 400; i++) _dir.File($"n{i:000}.{(i % 3 == 0 ? "log" : "txt")}", new string('x', i % 7 + 1));
        for (int i = 0; i < 20; i++) _dir.Dir($"d{i:00}");
        using var scratch = new TempDir();
        var spilled = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui, scratch.Path, listingMemoryBudgetBytes: 1024) { SnapshotThreshold = 10 });
        var memory = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui) { SnapshotThreshold = 10 });
        foreach (var m in new[] { spilled, memory })
        {
            await _ui.InvokeAsync(() => m.Load(Location.FileSystem(_dir.Path)));
            await _ui.WaitUntilAsync(() => m.State == ListingState.Complete);
        }
        Assert.True(await _ui.InvokeAsync(() => spilled.Store.IsSpilled));
        Assert.False(await _ui.InvokeAsync(() => memory.Store.IsSpilled));

        Task<string[]> Marked(ListingModel m) =>
            _ui.InvokeAsync(() => m.GetSelection().Select(i => i.Name).Order(StringComparer.Ordinal).ToArray());
        async Task Same(Action<ListingModel> act)
        {
            await _ui.InvokeAsync(() =>
            {
                act(spilled);
                act(memory);
            });
            Assert.Equal(await Marked(memory), await Marked(spilled));
            Assert.Equal(await _ui.InvokeAsync(memory.GetMarkStats), await _ui.InvokeAsync(spilled.GetMarkStats));
        }

        await Same(m => m.MarkByMask(Mask.Parse("*1*.txt"), true, includeDirectories: false));
        await Same(m => m.InvertMarks(includeDirectories: true));
        await Same(m => m.MarkNames(new HashSet<string>(["n005.txt", "d03", "missing"], StringComparer.Ordinal), false));
        await Same(m =>
        {
            m.UnmarkEverything();
            m.FocusName("n003.log");
            m.MarkSameExtension(true);
        });
        await Same(m => m.SetMarkRange(0, m.VisibleCount - 1, false));
        Assert.Equal(["n003.log"], await Marked(spilled)); // nothing marked: the focused item

        // Name lookups and quick search agree, in both directions.
        int si = await _ui.InvokeAsync(() => spilled.FindStoreIndex("n124.txt"));
        Assert.Equal("n124.txt", await _ui.InvokeAsync(() => spilled.Store[si].Name));
        Assert.Equal(si, await _ui.InvokeAsync(() => spilled.FindStoreIndex("n124.txt")));
        Assert.Equal(-1, await _ui.InvokeAsync(() => spilled.FindStoreIndex("missing")));
        foreach (bool forward in new[] { true, false })
        {
            int Row(ListingModel m) => m.FindVisible(0, forward, n => n.EndsWith("7.log", StringComparison.Ordinal));
            int row = await _ui.InvokeAsync(() => Row(spilled));
            Assert.Equal(await _ui.InvokeAsync(() => Row(memory)), row);
            Assert.Equal(forward ? "n027.log" : "n387.log", await _ui.InvokeAsync(() => spilled.GetVisible(row).Name));
        }

        // Outcomes still map back once a refresh replaced the store (names first, then identity).
        await _ui.InvokeAsync(() => spilled.MarkByMask(Mask.Parse("n0*"), true, includeDirectories: false));
        var captured = Assert.IsType<SelectionSnapshot>(await _ui.InvokeAsync(() => spilled.GetSelection()));
        await _ui.InvokeAsync(spilled.Refresh);
        await _ui.WaitUntilAsync(() => !spilled.IsRefreshing);
        Assert.NotSame(captured.Store, await _ui.InvokeAsync(() => spilled.Store));
        await _ui.InvokeAsync(() => spilled.MarkItems(captured, [0, 1, 2], false));
        var left = await Marked(spilled);
        Assert.Equal(97, left.Length);
        Assert.DoesNotContain("n000.log", left);
        Assert.Contains("n003.log", left);
        captured.Release();
        await _ui.InvokeAsync(spilled.Dispose);
        await _ui.InvokeAsync(memory.Dispose);
    }

    [Fact]
    public async Task Tabs_share_the_index_reservation_and_release_it_on_close()
    {
        for (int i = 0; i < 200; i++) _dir.File($"f{i:000}.txt");
        var budget = new IndexMemoryBudget(10_000);
        ListingModel NewModel() => new(_providers, _io, _ui, _dir.Path, indexBudget: budget);
        var first = await _ui.InvokeAsync(NewModel);
        await _ui.InvokeAsync(() => first.Load(Location.FileSystem(_dir.Path)));
        await _ui.WaitUntilAsync(() => first.State == ListingState.Complete);
        Assert.False(await _ui.InvokeAsync(() => first.HasExternalIndex));
        var second = await _ui.InvokeAsync(NewModel);
        await _ui.InvokeAsync(() => second.Load(Location.FileSystem(_dir.Path)));
        await _ui.WaitUntilAsync(() => second.State == ListingState.Complete);
        Assert.True(await _ui.InvokeAsync(() => second.HasExternalIndex));
        Assert.InRange(budget.ReservedBytes, 1, budget.LimitBytes);
        await _ui.InvokeAsync(first.Dispose);
        await _ui.InvokeAsync(second.Dispose);
        await _ui.WaitUntilAsync(() => budget.ReservedBytes == 0);
        await _ui.WaitUntilAsync(() => !Directory.EnumerateFiles(_dir.Path, "listing-index-*").Any());
    }
    [Fact]
    public async Task External_index_preserves_sort_filter_focus_and_marks_under_a_shared_budget()
    {
        for (int i = 0; i < 200; i++) _dir.File($"f{i:000}.txt");
        var budget = new IndexMemoryBudget(512);
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui,
            _dir.Path, listingMemoryBudgetBytes: 1024, indexBudget: budget));
        await _ui.InvokeAsync(() => model.Load(Location.FileSystem(_dir.Path)));
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        Assert.True(await _ui.InvokeAsync(() => model.HasExternalIndex));
        Assert.Equal(201, await _ui.InvokeAsync(() => model.VisibleCount));
        Assert.Equal("f000.txt", await _ui.InvokeAsync(() => model.GetVisible(1).Name));
        await _ui.InvokeAsync(() => model.MarkAll(true));
        Assert.Equal(200, await _ui.InvokeAsync(() => model.MarkedCount));
        await _ui.InvokeAsync(() => model.Filter = Mask.Parse("f1*.txt"));
        await _ui.WaitUntilAsync(() => model.VisibleCount == 101);
        Assert.Equal(100, await _ui.InvokeAsync(() => model.GetMarkStats().HiddenByFilter));
        await _ui.InvokeAsync(() => model.Sort = model.Sort with { Descending = true });
        await _ui.WaitUntilAsync(() => model.GetVisible(1).Name == "f199.txt");
        Assert.True(await _ui.InvokeAsync(() => model.FocusName("f150.txt")));
        Assert.Equal("f150.txt", await _ui.InvokeAsync(() => model.TryGetFocused(out var entry) ? entry.Name : null));
        Assert.Equal(0, budget.ReservedBytes);
        Assert.True(await _ui.InvokeAsync(() => model.ExternalIndexBytes > 0));
        await _ui.InvokeAsync(model.Dispose);
    }
    [Fact]
    public async Task Loads_sorted_with_parent_row_and_directories_first()
    {
        _dir.File("b.txt");
        _dir.File("A.txt");
        _dir.Dir("zdir");
        _dir.Dir("adir");
        var m = await LoadAsync(_dir.Path);
        Assert.Equal(["..", "adir", "zdir", "A.txt", "b.txt"], await VisibleNames(m));
        Assert.Equal(4, await _ui.InvokeAsync(() => m.TotalCount));
    }

    [Fact]
    public async Task Missing_directory_fails_with_plain_error_not_empty_listing()
    {
        var m = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => m.Load(Location.FileSystem(Path.Combine(_dir.Path, "missing"))));
        await _ui.WaitUntilAsync(() => m.State is ListingState.Failed or ListingState.Complete);
        Assert.Equal(ListingState.Failed, await _ui.InvokeAsync(() => m.State));
        Assert.NotNull(await _ui.InvokeAsync(() => m.Error));
    }

    [Fact]
    public async Task Focus_name_is_applied_when_it_arrives()
    {
        for (int i = 0; i < 50; i++) _dir.File($"f{i:000}.txt");
        var m = await LoadAsync(_dir.Path, focus: "f042.txt");
        Assert.Equal("f042.txt", await _ui.InvokeAsync(() => m.TryGetFocused(out var e) ? e.Name : null));
    }

    [Fact]
    public async Task Filter_keeps_marks_and_reports_hidden_marked_items()
    {
        _dir.File("a.txt");
        _dir.File("b.log");
        _dir.File("c.txt");
        var m = await LoadAsync(_dir.Path);
        await _ui.InvokeAsync(() => m.MarkAll(true));
        Assert.Equal(3, await _ui.InvokeAsync(() => m.MarkedCount));
        await _ui.InvokeAsync(() => m.Filter = Mask.Parse("*.txt"));
        await _ui.WaitUntilAsync(() => m.VisibleCount == 3);
        var stats = await _ui.InvokeAsync(() => m.GetMarkStats());
        Assert.Equal(3, stats.Count);
        Assert.Equal(1, stats.HiddenByFilter);
        var visibleOnly = await _ui.InvokeAsync(() => m.GetSelection(includeHiddenMarks: false));
        Assert.Equal(2, visibleOnly.Count);
        await _ui.InvokeAsync(() => m.UnmarkHidden());
        Assert.Equal(2, await _ui.InvokeAsync(() => m.MarkedCount));
    }

    [Fact]
    public async Task Refresh_transfers_marks_by_identity_and_never_substitutes()
    {
        _dir.File("a.txt");
        var b = _dir.File("b.txt");
        _dir.File("c.txt");
        var m = await LoadAsync(_dir.Path);
        await _ui.InvokeAsync(() => m.MarkNames(["b.txt", "c.txt"], true));
        File.Delete(b);
        _dir.File("aa.txt");
        await _ui.InvokeAsync(() => m.Refresh());
        await _ui.WaitUntilAsync(() => !m.IsRefreshing && m.TotalCount == 3);
        var marked = await _ui.InvokeAsync(() => m.GetSelection().Select(r => r.Name).ToArray());
        Assert.Equal(["c.txt"], marked);
    }

    [Fact]
    public async Task Selection_falls_back_to_focus_and_excludes_parent_row()
    {
        _dir.File("only.txt");
        var m = await LoadAsync(_dir.Path);
        await _ui.InvokeAsync(() => m.SetFocus(0));
        Assert.Empty(await _ui.InvokeAsync(() => m.GetSelection()));
        await _ui.InvokeAsync(() => m.SetFocus(1));
        var sel = await _ui.InvokeAsync(() => m.GetSelection());
        Assert.Single(sel);
        Assert.Equal("only.txt", sel[0].Name);
        Assert.Equal(Path.Combine(_dir.Path, "only.txt"), sel[0].FileSystemPath);
    }

    [Fact]
    public async Task Mask_selection_and_invert_and_restore()
    {
        _dir.File("a.txt");
        _dir.File("b.txt");
        _dir.File("c.log");
        _dir.Dir("sub.txt");
        var m = await LoadAsync(_dir.Path);
        int n = await _ui.InvokeAsync(() => m.MarkByMask(Mask.Parse("*.txt"), true, includeDirectories: false));
        Assert.Equal(2, n);
        await _ui.InvokeAsync(() => m.InvertMarks(includeDirectories: false));
        Assert.Equal(["c.log"], await _ui.InvokeAsync(() => m.GetSelection().Select(s => s.Name).ToArray()));
        await _ui.InvokeAsync(() =>
        {
            m.RememberOperation([m.GetItemRef(m.FindStoreIndex("a.txt")), m.GetItemRef(m.FindStoreIndex("b.txt"))]);
            m.UnmarkEverything();
            Assert.True(m.RestoreSelection());
        });
        Assert.Equal(2, await _ui.InvokeAsync(() => m.MarkedCount));
    }

    [Fact]
    public async Task Hidden_items_can_be_hidden()
    {
        _dir.File("visible.txt");
        var h = _dir.File("hidden.txt");
        File.SetAttributes(h, FileAttributes.Hidden);
        var m = await LoadAsync(_dir.Path);
        if (!OperatingSystem.IsWindows()) return;
        Assert.Contains("hidden.txt", await VisibleNames(m));
        await _ui.InvokeAsync(() => m.ShowHidden = false);
        await _ui.WaitUntilAsync(() => m.VisibleCount == 2);
        Assert.DoesNotContain("hidden.txt", await VisibleNames(m));
    }

    [Fact]
    public async Task Large_listing_streams_and_sorts()
    {
        for (int i = 0; i < 3000; i++) File.WriteAllText(Path.Combine(_dir.Path, $"n{3000 - i}.dat"), "");
        var m = await LoadAsync(_dir.Path);
        var names = await VisibleNames(m);
        Assert.Equal(3001, names.Length);
        Assert.Equal("n1.dat", names[1]);
        Assert.Equal("n3000.dat", names[^1]);
        await _ui.InvokeAsync(() => m.Sort = new SortSpec(SortField.Name, Descending: true));
        await _ui.WaitUntilAsync(() => m.GetVisible(1).Name == "n3000.dat");
    }
    [Fact]
    public async Task Slow_provider_shows_first_rows_before_enumeration_completes()
    {
        var provider = new GatedProvider();
        _providers.Register(provider);
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => model.Load(Location.FileSystem(_dir.Path)));
        try
        {
            await _ui.WaitUntilAsync(() => model.VisibleCount == 1);
            Assert.Equal(ListingState.Loading, await _ui.InvokeAsync(() => model.State));
            Assert.Equal("first.txt", await _ui.InvokeAsync(() => model.GetVisible(0).Name));
        }
        finally { provider.Release(); }
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        Assert.Equal(2, await _ui.InvokeAsync(() => model.VisibleCount));
        await _ui.InvokeAsync(model.Dispose);
    }


    [Theory]
    [InlineData(false, "alpha.txt")]
    [InlineData(true, "zeta.txt")]
    public async Task Untouched_cursor_stays_on_the_first_row_while_entries_stream_in(bool userMoved, string expected)
    {
        var provider = new GatedProvider("zeta.txt", "alpha.txt");
        _providers.Register(provider);
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => model.Load(Location.FileSystem(_dir.Path)));
        try
        {
            await _ui.WaitUntilAsync(() => model.VisibleCount == 1);
            if (userMoved) await _ui.InvokeAsync(() => model.SetFocus(0));
        }
        finally { provider.Release(); }
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        Assert.Equal(["alpha.txt", "zeta.txt"], await VisibleNames(model));
        Assert.True(await _ui.InvokeAsync(() => model.TryGetFocused(out var e) && e.Name == expected));
        await _ui.InvokeAsync(model.Dispose);
    }
    [Fact]
    public async Task A_refresh_asked_for_during_a_refresh_runs_after_it_so_the_rows_are_current()
    {
        var provider = new ChangingProvider { Names = ["a.txt", "b.txt"] };
        _providers.Register(provider);
        var location = new Location("changing", "root");
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => model.Load(location));
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        Assert.Equal(["a.txt", "b.txt"], await VisibleNames(model));

        // A refresh reads the names, then waits; b.txt goes away meanwhile, and the change asks for another refresh.
        provider.Hold();
        await _ui.InvokeAsync(model.Refresh);
        Assert.True(provider.Read.Wait(TimeSpan.FromSeconds(10)));
        provider.Names = ["a.txt"];
        await _ui.InvokeAsync(model.Refresh);
        provider.Let();
        await _ui.WaitUntilAsync(() => !model.IsRefreshing && model.State == ListingState.Complete && model.VisibleCount == 1);
        Assert.Equal(["a.txt"], await VisibleNames(model));
        await _ui.InvokeAsync(model.Dispose);
    }

    /// <summary>Lists its current names; when held, waits after reading them until let go.</summary>
    private sealed class ChangingProvider : ResourceProvider
    {
        private readonly ManualResetEventSlim _go = new(true);
        public volatile string[] Names = [];
        public ManualResetEventSlim Read { get; } = new();
        public override string Scheme => "changing";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            var names = Names;
            Read.Set();
            _go.Wait(ct);
            sink.AddBatch(names.Select(n => new EntryData(n, EntryKind.File)).ToArray());
            return Task.CompletedTask;
        }
        public void Hold()
        {
            Read.Reset();
            _go.Reset();
        }
        public void Let() => _go.Set();
    }

    private sealed class GatedProvider(string first = "first.txt", string second = "second.txt") : ResourceProvider
    {
        private readonly ManualResetEventSlim _release = new();
        public override string Scheme => Schemes.FileSystem;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch([new EntryData(first, EntryKind.File)]);
            _release.Wait(ct);
            sink.AddBatch([new EntryData(second, EntryKind.File)]);
            return Task.CompletedTask;
        }
        public void Release() => _release.Set();
    }


    [Fact]
    public async Task Large_selection_is_a_leased_snapshot_that_survives_navigation()
    {
        for (int i = 0; i < 50; i++) _dir.File($"f{i:00}.txt");
        var other = _dir.Dir("other");
        var m = await LoadAsync(_dir.Path);
        var sel = await _ui.InvokeAsync(() =>
        {
            m.SnapshotThreshold = 10;
            m.MarkByMask(Mask.Parse("*.txt"), true, includeDirectories: false);
            return m.GetSelection();
        });
        var snapshot = Assert.IsType<SelectionSnapshot>(sel);
        Assert.Equal(50, snapshot.Count);
        Assert.Equal(Location.FileSystem(_dir.Path), snapshot.CommonParent);
        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"f{i:00}.txt"), snapshot.Select(s => s.Name));
        // Positions map straight to marks while the listing generation is current.
        await _ui.InvokeAsync(() => m.MarkItems(snapshot, [0, 1], false));
        Assert.Equal(48, await _ui.InvokeAsync(() => m.MarkedCount));
        await _ui.InvokeAsync(() => m.RememberOperation(snapshot));
        await _ui.InvokeAsync(() => m.Load(Location.FileSystem(other)));
        await _ui.WaitUntilAsync(() => m.State == ListingState.Complete);
        Assert.Equal("f49.txt", snapshot[49].Name);
        Assert.False(await _ui.InvokeAsync(() => m.HasLastOperation)); // navigation forgets the operation selection
        snapshot.Release();
        await _ui.WaitUntilAsync(() =>
        {
            try { _ = snapshot.Store[0]; return false; }
            catch (ObjectDisposedException) { return true; }
        });
    }

    [Fact]
    public async Task Completed_items_are_unmarked_by_identity_after_a_refresh()
    {
        _dir.File("a.txt");
        _dir.File("b.txt");
        var m = await LoadAsync(_dir.Path);
        var sel = await _ui.InvokeAsync(() =>
        {
            m.SnapshotThreshold = 0;
            m.MarkByMask(Mask.Parse("*.txt"), true, includeDirectories: false);
            return m.GetSelection();
        });
        _dir.File("c.txt");
        await _ui.InvokeAsync(() => m.Refresh());
        await _ui.WaitUntilAsync(() => !m.IsRefreshing && m.TotalCount == 3);
        Assert.Equal(2, await _ui.InvokeAsync(() => m.MarkedCount));
        await _ui.InvokeAsync(() => m.MarkItems(sel, [1], false)); // b.txt completed
        Assert.Equal(["a.txt"], await _ui.InvokeAsync(() => m.GetSelection().Select(s => s.Name).ToArray()));
        ItemSources.Release(sel);
    }
}
