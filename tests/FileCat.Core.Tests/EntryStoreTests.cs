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
}

