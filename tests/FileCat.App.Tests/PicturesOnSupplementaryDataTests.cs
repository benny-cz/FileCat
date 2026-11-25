using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.HiddenData;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

/// <summary>
/// I649: File → Inspect → Streams and attributes and View → Show pictures, in either order, ended FileCat on Windows and
/// macOS. The picture grid asked the listing for the item of every tile, ".." included, and a streams and attributes
/// listing answers that with "Supplementary row category is unavailable; refresh required." Every frame threw, until the
/// crash guard gave up. The grid now draws such a listing's rows as plain tiles and reads none of their values, and a
/// provider that can no longer describe a row it listed never ends a frame: the listing is read again instead.
/// </summary>
public sealed class PicturesOnSupplementaryDataTests
{
    /// <summary>A file's streams and attributes, one of them named like a picture. Any read of a value is counted.</summary>
    private sealed class Data : IHiddenData
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public bool IsSupported => true;
        public IReadOnlyList<HiddenItem> List(string path) => throw new InvalidOperationException("Legacy inventory invoked.");
        public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct) =>
            new(path, DateTimeOffset.UtcNow, [new("fq.note", HiddenKind.Stream, 27), new("cover.png", HiddenKind.Stream, 64)],
                [new(HiddenKind.Stream, HiddenInventoryState.Complete, 2, "fixture")], "fixture") { BoundToObject = true };
        public byte[] Read(string path, HiddenItem item, int max) { Interlocked.Increment(ref _reads); throw new IOException("A value was read."); }
        public Stream Open(string path, HiddenItem item) { Interlocked.Increment(ref _reads); throw new IOException("A value was opened."); }
        public void Delete(string path, HiddenItem item) => throw new IOException("A value was deleted.");
    }

    /// <summary>
    /// A place whose provider cannot describe its picture's row while <see cref="Stale"/> is set: it throws what the streams
    /// and attributes provider throws for a row it does not know.
    /// </summary>
    private sealed class StaleRowProvider : ResourceProvider
    {
        private int _enumerations;
        public volatile bool Stale = true;
        public int Enumerations => Volatile.Read(ref _enumerations);
        public override string Scheme => "stale-row-test";
        public override string GetDisplayPath(Location location) => "stale rows";
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            Interlocked.Increment(ref _enumerations);
            sink.AddBatch([new EntryData("photo.png", EntryKind.File, 64), new EntryData("notes.txt", EntryKind.File, 5)]);
            return Task.CompletedTask;
        }
        public override ItemRef GetItemRef(Location listing, in EntryData entry) => Stale && entry.Name == "photo.png"
            ? throw new InvalidOperationException("Supplementary row category is unavailable; refresh required.")
            : base.GetItemRef(listing, entry);
        // A thumbnail asked for gets nothing to decode: the test needs only the asking.
        public override IContentSource? OpenContent(ItemRef item) => null;
    }

    private static async Task Until(Func<bool> condition, Func<string> what)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), what());
    }

    private static FileListControl ListOf(Window window, PanelViewModel panel) =>
        window.GetVisualDescendants().OfType<PanelView>().Single(view => ReferenceEquals(view.DataContext, panel)).FindControl<FileListControl>("List")!;

    /// <summary>Draws the window as the screen would, a few frames, with the work each frame posts.</summary>
    private static void Draw(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            window.CaptureRenderedFrame()?.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Draws the grid again, as a menu or a refresh over it does, from a clean record of what it drew: the list records the
    /// rows it drew only at the end of a frame it finished, so a frame that threw leaves the record empty. (An unchanged
    /// window draws nothing again, so the list is invalidated first.)
    /// </summary>
    private static void DrawGrid(Window window, TabViewModel tab, FileListControl list)
    {
        tab.VisibleStoreIndices = [];
        int renders = 0;
        void Count(double ms, long bytes) => renders++;
        FileListControl.RenderTimed += Count;
        try
        {
            list.InvalidateVisual();
            Draw(window);
        }
        finally { FileListControl.RenderTimed -= Count; }
        _lastRenders = renders;
    }

    /// <summary>How many list frames the last <see cref="DrawGrid"/> began (and finished: the hook runs after a frame).</summary>
    private static int _lastRenders;

    /// <summary>Every visible row's store index: the grid records them at the end of a frame it drew completely.</summary>
    private static int[] VisibleStoreIndices(TabViewModel tab) =>
        Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetStoreIndex).ToArray();

    private static async Task<(TabViewModel Tab, FileListControl List)> FolderWithFileFocused(MainViewModel vm, Window window)
    {
        var panel = vm.Workspace.ActivePanel!;
        var tab = panel.ActiveTab!;
        await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FindStoreIndex("a.txt") >= 0, () => "the folder is listed");
        tab.Listing.SetFocus(tab.Listing.GetVisibleIndex(tab.Listing.FindStoreIndex("a.txt")));
        return (tab, ListOf(window, panel));
    }

    private static async Task OpenStreamsAndAttributes(MainViewModel vm, TabViewModel tab)
    {
        vm.Execute(CommandIds.HiddenData);
        await Until(() => tab.Location?.Scheme == Schemes.HiddenData && tab.Listing.State == ListingState.Complete &&
                          !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 2,
            () => $"the streams and attributes are listed (location: {tab.Location}; state: {tab.Listing.State}; rows: {tab.Listing.TotalCount}; notice: {vm.Notification})");
        Assert.True(tab.Listing.HasParentRow, "The listing has a \"..\" row, the one the grid used to ask for.");
    }

    /// <summary>The grid drew every tile, as plain tiles: no thumbnail was asked for and no value was read.</summary>
    private static void AssertPlainTiles(TabViewModel tab, FileListControl list, Data data, List<Exception> unhandled)
    {
        Assert.Empty(unhandled.Select(e => e.ToString()));
        Assert.True(list.PictureMode);
        Assert.True(tab.PictureView);
        Assert.True(VisibleStoreIndices(tab).Order().SequenceEqual(tab.VisibleStoreIndices.Order()),
            $"drawn: [{string.Join(", ", tab.VisibleStoreIndices)}]; rows: {tab.Listing.VisibleCount}; bounds: {list.Bounds}; " +
            $"shown: {list.IsEffectivelyVisible}; list renders: {_lastRenders}");
        Assert.Equal(0, list.PictureThumbnailLoadCount);
        Assert.Equal(0, list.PendingPictureThumbnailCount);
        Assert.Equal(0, list.CachedPictureThumbnailCount);
        Assert.Equal(0, data.Reads);
    }

    [AvaloniaFact]
    public async Task Show_pictures_on_streams_and_attributes_draws_plain_tiles_and_FileCat_keeps_running()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var data = new Data();
        services.Providers.Register(new HiddenDataProvider(data));
        var unhandled = new List<Exception>();
        void Record(object? sender, DispatcherUnhandledExceptionEventArgs e) { unhandled.Add(e.Exception); e.Handled = true; }
        Dispatcher.UIThread.UnhandledException += Record;
        try
        {
            var (tab, list) = await FolderWithFileFocused(vm, window);
            await OpenStreamsAndAttributes(vm, tab);
            Draw(window);

            // View → Show pictures: the grid, its rows drawn as plain tiles, and a word on why there are no thumbnails.
            vm.Execute(CommandIds.PictureView);
            DrawGrid(window, tab, list);
            AssertPlainTiles(tab, list, data, unhandled);
            Assert.Contains("plain tiles", vm.Notification ?? "", StringComparison.Ordinal);

            // Then the View menu, and Ctrl+R: each drew the grid again, and the crash guard ended FileCat.
            var view = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().Single(item => (item.Header as string)?.Replace("_", "") == "View");
            view.IsSubMenuOpen = true;
            DrawGrid(window, tab, list);
            AssertPlainTiles(tab, list, data, unhandled);
            view.IsSubMenuOpen = false;
            DrawGrid(window, tab, list);
            AssertPlainTiles(tab, list, data, unhandled);
            vm.Execute(CommandIds.Refresh);
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing, () => "the listing is read again");
            DrawGrid(window, tab, list);
            AssertPlainTiles(tab, list, data, unhandled);
            Assert.Equal(Schemes.HiddenData, tab.Location?.Scheme);
            Assert.Equal(2, tab.Listing.TotalCount);
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Record;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Streams_and_attributes_opened_in_a_tab_showing_pictures_are_drawn_as_plain_tiles()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var data = new Data();
        services.Providers.Register(new HiddenDataProvider(data));
        var unhandled = new List<Exception>();
        void Record(object? sender, DispatcherUnhandledExceptionEventArgs e) { unhandled.Add(e.Exception); e.Handled = true; }
        Dispatcher.UIThread.UnhandledException += Record;
        try
        {
            var (tab, list) = await FolderWithFileFocused(vm, window);
            vm.Execute(CommandIds.PictureView);
            Draw(window);
            Assert.True(list.PictureMode);

            // The other order: the tab already shows pictures, and the streams and attributes open in it.
            await OpenStreamsAndAttributes(vm, tab);
            DrawGrid(window, tab, list);
            AssertPlainTiles(tab, list, data, unhandled);
            vm.Execute(CommandIds.Refresh);
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing, () => "the listing is read again");
            DrawGrid(window, tab, list);
            AssertPlainTiles(tab, list, data, unhandled);
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Record;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_row_its_provider_can_no_longer_describe_is_drawn_plain_and_the_listing_is_read_again_once()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var provider = new StaleRowProvider();
        services.Providers.Register(provider);
        var unhandled = new List<Exception>();
        void Record(object? sender, DispatcherUnhandledExceptionEventArgs e) { unhandled.Add(e.Exception); e.Handled = true; }
        Dispatcher.UIThread.UnhandledException += Record;
        try
        {
            var panel = vm.Workspace.ActivePanel!;
            var tab = panel.ActiveTab!;
            var list = ListOf(window, panel);
            tab.Navigate(new Location(provider.Scheme, "pictures"));
            await Until(() => tab.Location?.Scheme == provider.Scheme && tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 2,
                () => $"the place is listed (state: {tab.Listing.State})");
            Draw(window);
            int enumerations = provider.Enumerations;
            tab.PictureView = true;
            DrawGrid(window, tab, list);

            // The frame finished, the picture's tile drawn plain; the listing is read again after it, once.
            Assert.Empty(unhandled.Select(e => e.ToString()));
            Assert.Equal(VisibleStoreIndices(tab).Order(), tab.VisibleStoreIndices.Order());
            Assert.Equal(0, list.PictureThumbnailLoadCount);
            await Until(() => provider.Enumerations == enumerations + 1 && tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing,
                () => $"the listing is read again once (enumerations: {provider.Enumerations - enumerations})");
            // A provider that keeps refusing gets no endless rereads: the next frames draw the tile plain again.
            for (int frame = 0; frame < 3; frame++)
            {
                DrawGrid(window, tab, list);
                Assert.Equal(VisibleStoreIndices(tab).Order(), tab.VisibleStoreIndices.Order());
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            Assert.Equal(enumerations + 1, provider.Enumerations);
            Assert.Empty(unhandled.Select(e => e.ToString()));
            Assert.Equal(0, list.PictureThumbnailLoadCount);

            // Once the provider can describe the row (the user refreshed after what changed), its thumbnail is asked for.
            provider.Stale = false;
            vm.Execute(CommandIds.Refresh);
            await Until(() => provider.Enumerations == enumerations + 2 && tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing,
                () => "the user's refresh reads the place");
            await Until(() =>
            {
                DrawGrid(window, tab, list);
                return list.PictureThumbnailLoadCount > 0;
            }, () => "the picture's thumbnail is asked for");
            Assert.Empty(unhandled.Select(e => e.ToString()));
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Record;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_metadata_cell_of_a_row_its_provider_can_no_longer_describe_is_left_unread_and_the_listing_read_again()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var provider = new StaleRowProvider();
        services.Providers.Register(provider);
        try
        {
            var tab = vm.Workspace.ActivePanel!.ActiveTab!;
            tab.Navigate(new Location(provider.Scheme, "pictures"));
            await Until(() => tab.Location?.Scheme == provider.Scheme && tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 2,
                () => $"the place is listed (state: {tab.Listing.State})");
            int enumerations = provider.Enumerations;
            int index = tab.Listing.FindStoreIndex("photo.png");
            var entry = tab.Listing.Store[index];
            // The list draws its metadata columns through this; a refusal there ended the frame just the same.
            Assert.Null(tab.CaptureMetadataContext(entry, index));
            Assert.NotNull(tab.CaptureMetadataContext(tab.Listing.Store[tab.Listing.FindStoreIndex("notes.txt")], tab.Listing.FindStoreIndex("notes.txt")));
            await Until(() => provider.Enumerations == enumerations + 1 && tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing,
                () => $"the listing is read again once (enumerations: {provider.Enumerations - enumerations})");
            index = tab.Listing.FindStoreIndex("photo.png");
            Assert.Null(tab.CaptureMetadataContext(tab.Listing.Store[index], index));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(enumerations + 1, provider.Enumerations);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
