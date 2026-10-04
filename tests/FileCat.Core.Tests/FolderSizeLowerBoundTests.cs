using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class FolderSizeLowerBoundTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lower_bound_survives_refresh_or_waits_for_its_row_and_remains_retryable(bool whileLoading)
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        string sub = Directory.CreateDirectory(Path.Combine(folder.Path, "sub")).FullName;
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        await ui.InvokeAsync(() =>
        {
            listing.Load(Location.FileSystem(folder.Path));
            if (whileLoading) listing.SetComputedSize("sub", 1000, true, lowerBound: true);
        });
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        await ui.InvokeAsync(() =>
        {
            listing.MarkNames(["sub"], true);
            if (!whileLoading) listing.SetComputedSize("sub", 1000, true, lowerBound: true);
            listing.Refresh();
        });
        await ui.WaitUntilAsync(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
        await ui.InvokeAsync(() =>
        {
            var e = listing.Store[listing.FindStoreIndex("sub")];
            Assert.Equal(1000, e.Size);
            Assert.True(e.Has(EntryFlags.SizeLowerBound));
            var stats = listing.GetMarkStats();
            Assert.Equal(1000, stats.Bytes);
            Assert.True(stats.SizesIncomplete);
            Assert.Equal(1, stats.LowerBoundDirectories);
            Assert.Equal(0, stats.UnsizedDirectories);
            Assert.Equal(listing.FindStoreIndex("sub"), Assert.Single(listing.MarkedUnsizedFolders()));
            // A successful retry replaces the partial subtotal and its cached state.
            listing.SetComputedSize("sub", 1234, true);
            Assert.False(listing.Store[listing.FindStoreIndex("sub")].Has(EntryFlags.SizeLowerBound));
            Assert.False(listing.GetMarkStats().SizesIncomplete);
            Assert.Equal(1234, listing.GetMarkStats().Bytes);
            Assert.Empty(listing.MarkedUnsizedFolders());
            listing.Refresh();
        });
        await ui.WaitUntilAsync(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
        await ui.InvokeAsync(() =>
        {
            Assert.Equal(1234, listing.GetMarkStats().Bytes);
            Assert.False(listing.GetMarkStats().SizesIncomplete);
            Directory.SetLastWriteTimeUtc(sub, Directory.GetLastWriteTimeUtc(sub).AddMinutes(1));
            listing.Refresh();
        });
        await ui.WaitUntilAsync(() => !listing.IsRefreshing && listing.State == ListingState.Complete);
        await ui.InvokeAsync(() =>
        {
            Assert.True(listing.Store[listing.FindStoreIndex("sub")].Size < 0);
            Assert.Equal(1, listing.GetMarkStats().UnsizedDirectories);
            Assert.Equal(0, listing.GetMarkStats().LowerBoundDirectories);
            listing.Dispose();
        });
    }

    [Fact]
    public async Task Mixed_marks_include_partial_subtotals_but_never_claim_a_complete_total()
    {
        using var folder = new TempDir();
        using var scratch = new TempDir();
        foreach (var name in new[] { "partial", "complete", "unknown" }) Directory.CreateDirectory(Path.Combine(folder.Path, name));
        File.WriteAllBytes(Path.Combine(folder.Path, "file.bin"), new byte[20]);
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var listing = await ui.InvokeAsync(() => new ListingModel(providers, io, ui, scratch.Path, 1024));
        await ui.InvokeAsync(() => listing.Load(Location.FileSystem(folder.Path)));
        await ui.WaitUntilAsync(() => listing.State == ListingState.Complete);
        await ui.InvokeAsync(() =>
        {
            listing.MarkNames(["partial", "complete", "unknown", "file.bin"], true);
            listing.SetComputedSize("partial", 1000, true, lowerBound: true);
            listing.SetComputedSize("complete", 2000, true);
            Assert.Equal(3020, listing.GetMarkStats().Bytes);
            Assert.True(listing.GetMarkStats().SizesIncomplete);
            Assert.Equal(1, listing.GetMarkStats().LowerBoundDirectories);
            Assert.Equal(1, listing.GetMarkStats().UnsizedDirectories);
            Assert.Equal(new[] { "partial", "unknown" }, listing.MarkedUnsizedFolders().Select(si => listing.Store[si].Name).Order(StringComparer.Ordinal));
            // New in-progress demand invalidates the old cached lower bound.
            listing.SetComputedSize("partial", 500, false);
            Assert.Equal(2020, listing.GetMarkStats().Bytes);
            Assert.Equal(0, listing.GetMarkStats().LowerBoundDirectories);
            Assert.Equal(2, listing.GetMarkStats().UnsizedDirectories);
            listing.Dispose();
        });
    }
}
