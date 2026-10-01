using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class EntryStoreTests
{
    [Fact]
    public void Spill_preserves_identity_metadata_and_updates_and_removes_files_on_close()
    {
        using var scratch = new TempDir();
        var store = new EntryStore(scratch.Path, 256);
        var entries = Enumerable.Range(0, 300)
            .Select(i => new EntryData($"δοκιμή-{i:000}-😀.txt", EntryKind.File, i * 13, i + 1)
            {
                Flags = EntryFlags.Hidden,
                Attributes = 7,
                Created = i + 2,
            }).ToArray();
        store.Append(entries.AsSpan(0, 150));
        store.Append(entries.AsSpan(150));
        Assert.True(store.IsSpilled);
        Assert.Equal(300, store.Count);
        Assert.True(store.SpillBytes > 0);
        Assert.True(store.EstimateBytes() < 4 * 1024 * 1024);
        for (int i = 0; i < entries.Length; i++)
        {
            var actual = store[i];
            Assert.Equal(entries[i].Name, actual.Name);
            Assert.Equal(entries[i].Size, actual.Size);
            Assert.Equal(entries[i].Modified, actual.Modified);
            Assert.Equal(entries[i].Created, actual.Created);
            Assert.Equal(entries[i].Flags, actual.Flags);
            Assert.Equal(entries[i].Attributes, actual.Attributes);
        }
        var changed = store[42];
        changed.Size = 9000;
        changed.Flags |= EntryFlags.SizeComputed;
        store.Update(42, changed);
        Assert.Equal(9000, store[42].Size);
        Assert.True(store[42].Has(EntryFlags.SizeComputed));
        store.Dispose();
        Assert.Empty(Directory.GetFiles(scratch.Path, "listing-*"));
    }

    [Fact]
    public void Spill_preserves_unpaired_utf16_code_units()
    {
        using var scratch = new TempDir();
        using var store = new EntryStore(scratch.Path, 1);
        var name = "raw-" + (char)0xD800 + "-name";
        store.Append(new EntryData(name, EntryKind.File));
        Assert.True(store.IsSpilled);
        Assert.Equal(name, store[0].Name);
    }

    [Fact]
    public void Tagged_entries_never_lose_provider_payload_during_spill()
    {
        using var scratch = new TempDir();
        using var store = new EntryStore(scratch.Path, 1);
        store.Append(new EntryData("tagged", EntryKind.File) { Tag = new object() });
        store.Append(new EntryData("other", EntryKind.File));
        Assert.False(store.IsSpilled);
        Assert.NotNull(store[0].Tag);
    }

    [Fact]
    public void Spilled_ten_thousand_entries_sort_without_materializing_records()
    {
        using var scratch = new TempDir();
        using var store = new EntryStore(scratch.Path, 1024);
        for (int from = 0; from < 10_000; from += 250)
        {
            var batch = Enumerable.Range(from, 250)
                .Select(i => new EntryData($"entry-{10_000 - i:00000}.txt", EntryKind.File)).ToArray();
            store.Append(batch);
        }
        var sorted = Enumerable.Range(0, store.Count).ToArray();
        StableSort.Sort(sorted, EntrySorter.CreateComparison(store, new SortSpec()));
        Assert.Equal("entry-00001.txt", store[sorted[0]].Name);
        Assert.Equal("entry-10000.txt", store[sorted[^1]].Name);
        Assert.True(store.EstimateBytes() < 4 * 1024 * 1024);
    }

    [Fact]
    public async Task Listing_uses_spill_and_retains_marks_sort_and_refresh()
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        for (int i = 0; i < 350; i++) folder.File($"item-{350 - i:000}.txt");
        using var ui = new TestDispatcher();
        using var io = new FileCat.Core.Threading.DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new ComputerProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(folder.Path)));
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        Assert.True(await ui.InvokeAsync(() => listing.Store.IsSpilled));
        Assert.Equal("item-001.txt", await ui.InvokeAsync(() => listing.GetVisible(1).Name));
        await ui.InvokeAsync(() => listing.MarkNames(["item-042.txt"], true));
        Assert.Equal("item-042.txt", (await ui.InvokeAsync(() => listing.GetSelection())).Single().Name);
        await ui.InvokeAsync(() => listing.Refresh());
        await ui.WaitUntilAsync(() => !listing.IsRefreshing);
        Assert.Equal("item-042.txt", (await ui.InvokeAsync(() => listing.GetSelection())).Single().Name);
        await ui.InvokeAsync(listing.Dispose);
        await ui.WaitUntilAsync(() => !Directory.EnumerateFiles(scratch.Path, "listing-*").Any());
    }

    [Fact]
    public async Task A_disposed_listing_does_not_load_again()
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        folder.File("one.txt");
        using var ui = new TestDispatcher();
        using var io = new FileCat.Core.Threading.DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(folder.Path)));
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        await ui.InvokeAsync(listing.Dispose);
        // A closed tab's late work (a connection made, a search's results) asks it to load: nothing is read.
        string other = Directory.CreateDirectory(Path.Combine(folder.Path, "other")).FullName;
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(other)));
        Assert.Equal(Location.FileSystem(folder.Path), await ui.InvokeAsync(() => listing.Location));
        Assert.Equal(ListingState.Complete, await ui.InvokeAsync(() => listing.State));
    }

    [Fact]
    public async Task A_location_nothing_can_list_leaves_the_listing_as_it_was()
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        folder.File("keep.txt");
        using var ui = new TestDispatcher();
        using var io = new FileCat.Core.Threading.DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new ComputerProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(folder.Path)));
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        // No provider for it: the navigation fails, and the folder still shown can still be read (saving the workspace
        // on exit read a released store and failed).
        await Assert.ThrowsAsync<InvalidOperationException>(() => ui.InvokeAsync(() => listing.Load(new Location("nowhere", "x"))));
        Assert.Equal(folder.Path, (await ui.InvokeAsync(() => listing.Location))!.Path);
        Assert.Equal("keep.txt", await ui.InvokeAsync(() => listing.GetVisible(1).Name));
        await ui.InvokeAsync(listing.Dispose);
    }

    [Fact]
    public async Task A_size_measured_while_its_folder_is_listed_again_lands_when_its_row_returns()
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        Directory.CreateDirectory(Path.Combine(folder.Path, "sub"));
        folder.File("keep.txt");
        using var ui = new TestDispatcher();
        using var io = new FileCat.Core.Threading.DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new ComputerProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        // The size arrives after the rows were replaced and before the folder's row is back (the folder watcher's
        // reread on a busy machine): it used to be dropped.
        await ui.InvokeAsync(() =>
        {
            listing.Load(Location.FileSystem(folder.Path));
            listing.SetComputedSize("sub", 5000, true);
        });
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        long size = await ui.InvokeAsync(() => listing.GetVisible(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == "sub")).Size);
        Assert.Equal(5000L, size);
        await ui.InvokeAsync(listing.Dispose);
    }

    [Fact]
    public async Task A_computed_folder_size_survives_a_refresh_until_the_folder_changes()
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        Directory.CreateDirectory(Path.Combine(folder.Path, "sub"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "other"));
        folder.File("keep.txt");
        using var ui = new TestDispatcher();
        using var io = new FileCat.Core.Threading.DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new ComputerProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(folder.Path)));
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        long SizeOf(string name) => ui.InvokeAsync(() => listing.GetVisible(Enumerable.Range(0, listing.VisibleCount).First(i => listing.GetVisible(i).Name == name)).Size).Result;
        await ui.InvokeAsync(() =>
        {
            listing.SetComputedSize("sub", 5000, true);
            listing.SetComputedSize("other", 7000, true);
        });

        // A refresh (the folder watcher's, say) keeps sizes of folders that did not change.
        await ui.InvokeAsync(() => listing.Refresh());
        await ui.WaitUntilAsync(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
        Assert.Equal((5000L, 7000L), (SizeOf("sub"), SizeOf("other")));

        // One that changed inside has a stale size: it goes.
        File.WriteAllText(Path.Combine(folder.Path, "other", "new.txt"), "changed");
        Directory.SetLastWriteTimeUtc(Path.Combine(folder.Path, "other"), DateTime.UtcNow.AddMinutes(1));
        await ui.InvokeAsync(() => listing.Refresh());
        await ui.WaitUntilAsync(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
        Assert.Equal(5000L, SizeOf("sub"));
        Assert.True(SizeOf("other") < 0);

        // Leaving the folder forgets them.
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(folder.Path)));
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        Assert.True(SizeOf("sub") < 0);
        await ui.InvokeAsync(listing.Dispose);
    }

    [Theory]
    [InlineData(SortField.Name, false)]
    [InlineData(SortField.Size, true)]
    [InlineData(SortField.Extension, false)]
    [InlineData(SortField.Modified, true)]
    public void Spilled_sort_reads_in_place_and_matches_the_in_memory_order(SortField field, bool descending)
    {
        using var scratch = new TempDir();
        using var spilled = new EntryStore(scratch.Path, 1024);
        using var memory = new EntryStore();
        var random = new Random(7);
        var entries = Enumerable.Range(0, 3000).Select(i => new EntryData(
            $"{(char)('a' + random.Next(26))}name{random.Next(500)}.{(i % 3 == 0 ? "txt" : i % 3 == 1 ? "log" : "Ωmega")}",
            i % 10 == 0 ? EntryKind.Directory : EntryKind.File, random.Next(100), random.Next(1000))).ToArray();
        spilled.Append(entries);
        memory.Append(entries);
        Assert.True(spilled.IsSpilled);
        var spec = new SortSpec(field, descending);
        using (var reader = spilled.OpenSpillReader(spilled.Count - 100)) // a prefix of files that keep growing
        {
            Assert.NotNull(reader);
            Assert.Equal(entries[17].Name, reader!.Get(17).Name.ToString());
        }
        var expected = Enumerable.Range(0, entries.Length).ToArray();
        StableSort.Sort(expected, EntrySorter.CreateComparison(memory, spec));
        var actual = Enumerable.Range(0, entries.Length).ToArray();
        using (var comparer = EntrySorter.CreateComparer(spilled, spec, null, spilled.Count))
            StableSort.Sort(actual, comparer.Comparison);
        Assert.Equal(expected, actual);
        Assert.Null(memory.OpenSpillReader(memory.Count));
    }
}
