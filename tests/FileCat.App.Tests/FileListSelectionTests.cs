using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

/// <summary>FCUX-002: actual pointer/keyboard input through the reusable list, after asynchronous view changes.</summary>
public sealed class FileListSelectionTests
{
    [AvaloniaFact]
    public async Task Preloaded_tab_switch_uses_the_new_tabs_focus_and_preserves_ctrl_marks()
    {
        using var f = new FileListFixture(Enumerable.Range(0, 100).Select(i => $"a{i:D3}").ToArray());
        await f.Load();
        var first = f.Tab;
        f.Provider.Items = Enumerable.Range(0, 10).Select(i => $"b{i}").ToArray();
        var second = f.Panel.OpenTab(new Location(FileListFixture.Scheme, "second"));
        await FileListFixture.Wait(() => second.Listing.State == ListingState.Complete);
        second.Listing.SetFocus(4);
        f.List.Tab = first;
        f.Click(90);
        Assert.Equal("a090", first.Listing.GetVisible(first.Listing.FocusedIndex).Name);

        f.List.Tab = second;
        f.Click(2, RawInputModifiers.Shift);
        Assert.Equal(new[] { "b2", "b3", "b4" }, FileListFixture.Marked(second.Listing));
        f.Click(6, RawInputModifiers.Control);
        f.Click(8, RawInputModifiers.Shift);
        Assert.Equal(new[] { "b2", "b3", "b4", "b5", "b6", "b7", "b8" }, FileListFixture.Marked(second.Listing));
        Assert.Equal(0, first.Listing.MarkedCount);
    }

    [AvaloniaFact]
    public async Task Sorting_resolves_the_clicked_identity_in_the_new_visible_order()
    {
        using var f = new FileListFixture("a", "b", "c", "d", "e");
        await f.Load();
        f.Click(1);
        f.Tab.Listing.Sort = new SortSpec(Descending: true);
        await FileListFixture.Wait(() => f.Tab.Listing.GetVisible(0).Name == "e");
        Assert.Equal(3, f.Tab.Listing.FocusedIndex);
        f.Click(4, RawInputModifiers.Shift);
        Assert.Equal(new[] { "a", "b" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task Mac_command_click_toggles_marks_without_replacing_the_range_anchor()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var f = new FileListFixture("a", "b", "c", "d");
        await f.Load();
        f.Click(1);
        f.Click(3, RawInputModifiers.Meta);
        Assert.Equal(new[] { "d" }, FileListFixture.Marked(f.Tab.Listing));
        Assert.Equal("d", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);

        f.Click(2, RawInputModifiers.Shift);
        Assert.Equal(new[] { "b", "c", "d" }, FileListFixture.Marked(f.Tab.Listing));
        Assert.Equal("c", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);

        f.Click(3, RawInputModifiers.Meta);
        Assert.Equal(new[] { "b", "c" }, FileListFixture.Marked(f.Tab.Listing));
        Assert.Equal("d", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
    }

    [AvaloniaFact]
    public async Task A_visible_anchor_survives_filter_reordering()
    {
        using var f = new FileListFixture("a", "b", "c", "d", "e");
        await f.Load();
        f.Click(2);
        f.Tab.Listing.Filter = Mask.Parse("*|a");
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 4);
        f.Click(3, RawInputModifiers.Shift);
        Assert.Equal(new[] { "c", "d", "e" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task A_filtered_anchor_is_retired_and_cannot_return_when_revealed()
    {
        using var f = new FileListFixture("a", "b", "c", "d", "e");
        await f.Load();
        f.Click(1);
        f.Tab.Listing.Filter = Mask.Parse("*|b");
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 4);
        Assert.Equal("c", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        f.Tab.Listing.Filter = null;
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 5);
        f.Click(4, RawInputModifiers.Shift);
        Assert.Equal(new[] { "c", "d", "e" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task Refresh_retires_the_old_store_anchor_after_creation_and_deletion()
    {
        using var f = new FileListFixture("a", "b", "c", "d", "e");
        await f.Load();
        f.Click(3);
        f.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        f.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Assert.Equal("b", f.Tab.Listing.GetVisible(f.Tab.Listing.FocusedIndex).Name);
        var store = f.Tab.Listing.Store;
        f.Provider.Items = ["0", "a", "b", "c", "e"];
        f.Tab.Refresh();
        await FileListFixture.Wait(() => !ReferenceEquals(store, f.Tab.Listing.Store) && !f.Tab.Listing.IsRefreshing);
        f.Click(4, RawInputModifiers.Shift);
        Assert.Equal(new[] { "b", "c", "e" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task Streaming_rows_preserves_an_anchor_in_the_same_store()
    {
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new FileListFixture(["b", "d"], finish, ["a", "c", "e"]);
        await FileListFixture.Wait(() => f.Tab.Listing.VisibleCount == 2);
        f.Click(1);
        var store = f.Tab.Listing.Store;
        finish.SetResult();
        await f.Load();
        Assert.Same(store, f.Tab.Listing.Store);
        f.Click(4, RawInputModifiers.Shift);
        Assert.Equal(new[] { "d", "e" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task Shift_movement_preserves_commander_toggle_intent_until_shift_is_released()
    {
        using var f = new FileListFixture("a", "b", "c", "d", "e");
        await f.Load();
        f.Tab.Listing.SetMark(0, true);
        f.Tab.Listing.SetMark(4, true);
        f.List.Focus();
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Assert.Equal(new[] { "e" }, FileListFixture.Marked(f.Tab.Listing));
        f.Window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        f.Window.KeyPress(Key.Up, RawInputModifiers.Shift, PhysicalKey.ArrowUp, null);
        Assert.Equal(new[] { "c", "e" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task Shift_release_outside_the_list_ends_the_previous_unmark_gesture()
    {
        using var f = new FileListFixture("a", "b", "c");
        await f.Load();
        f.Tab.Listing.SetMark(0, true);
        f.List.Focus();
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Assert.Empty(FileListFixture.Marked(f.Tab.Listing));
        f.Other.Focus();
        f.Window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        f.List.Focus();
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Assert.Equal(new[] { "b" }, FileListFixture.Marked(f.Tab.Listing));
    }

    [AvaloniaFact]
    public async Task Releasing_shift_in_another_window_ends_the_native_window_gesture()
    {
        using var f = new FileListFixture("a", "b", "c");
        await f.Load();
        f.Tab.Listing.SetMark(0, true);
        f.List.Focus();
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        var elsewhere = new Window { Width = 200, Height = 100, Content = new TextBox() };
        elsewhere.Show();
        try
        {
            elsewhere.Activate();
            elsewhere.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
            f.Window.Activate();
            f.List.Focus();
            f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Assert.Equal(new[] { "b" }, FileListFixture.Marked(f.Tab.Listing));
        }
        finally { elsewhere.Close(); }
    }

    [AvaloniaFact]
    public async Task Switching_tabs_during_shift_movement_ends_the_previous_unmark_gesture()
    {
        using var f = new FileListFixture("a", "b", "c");
        await f.Load();
        var second = f.Panel.OpenTab(new Location(FileListFixture.Scheme, "second"));
        await FileListFixture.Wait(() => second.Listing.State == ListingState.Complete);
        f.List.Tab = f.Tab;
        f.Tab.Listing.SetMark(0, true);
        f.List.Focus();
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        f.List.Tab = second;
        f.Window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Assert.Equal(new[] { "a" }, FileListFixture.Marked(second.Listing));
    }
}

/// <summary>One real headless list with a provider whose ordering and asynchronous publication are controlled.</summary>
internal sealed class FileListFixture : IDisposable
{
    internal const string Scheme = "list-fixture";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-list-tests", Guid.NewGuid().ToString("N"));
    internal AppServices Services { get; }
    internal PanelViewModel Panel { get; }
    internal TabViewModel Tab { get; }
    internal FileListControl List { get; }
    internal Window Window { get; }
    internal Button Other { get; } = new() { Content = "Other focus target", Height = 28 };
    internal ListProvider Provider { get; }

    internal FileListFixture(params string[] names) : this(names, null, null) { }

    internal FileListFixture(string[] names, TaskCompletionSource? finish, string[]? lastItems)
        : this(new ListProvider { Items = names, Finish = finish, LastItems = lastItems ?? [] }) { }

    internal FileListFixture(ListProvider provider, Window? window = null)
    {
        Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: _root));
        Provider = provider;
        Services.Providers.Register(Provider);
        var workspace = new WorkspaceViewModel(Services);
        Panel = new PanelViewModel(workspace, Services);
        workspace.Panels.Add(Panel);
        workspace.ActivePanel = Panel;
        Tab = Panel.OpenTab(new Location(Scheme, "first"));
        // Keep ordinary visible metadata-column production out of the accessibility I/O assertion.
        // The Brief profile uses already listed name/size data on every platform.
        Tab.ColumnProfile = 1;
        List = new FileListControl { Tab = Tab };
        var layout = new DockPanel();
        DockPanel.SetDock(Other, Dock.Bottom);
        layout.Children.Add(Other);
        layout.Children.Add(List);
        Window = window ?? new Window();
        Window.Width = 640;
        Window.Height = 420;
        Window.Content = layout;
        Window.Show();
    }

    internal Task Load() => Wait(() => Tab.Listing.State == ListingState.Complete, Provider.SyntheticCount > 100_000 ? 3000 : 300);

    internal void Click(int row, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var scroll = Assert.Single(List.GetVisualDescendants().OfType<ScrollBar>(),
            bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical);
        // Scroll without changing keyboard focus: Shift-click's fallback must be the previous focused item.
        var bounds = List.GetRowBounds(row);
        if (bounds.Top < List.GetRowBounds((int)scroll.Value).Top || bounds.Bottom > List.Bounds.Height)
            scroll.Value = Math.Clamp(row - List.VisibleRowCapacity / 2, 0, (int)scroll.Maximum);
        bounds = List.GetRowBounds(row);
        var point = List.TranslatePoint(new Point(60, bounds.Center.Y), Window)!.Value;
        Window.MouseDown(point, MouseButton.Left, modifiers);
        Window.MouseUp(point, MouseButton.Left, modifiers);
    }

    internal static string[] Marked(ListingModel listing) =>
        Enumerable.Range(0, listing.Store.Count).Where(listing.IsMarked)
            .Select(i => listing.Store[i].Name).Order(StringComparer.Ordinal).ToArray();

    internal static async Task Wait(Func<bool> condition, int attempts = 300)
    {
        for (int i = 0; i < attempts && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The asynchronous listing did not reach the required state.");
    }

    public void Dispose()
    {
        Provider.Finish?.TrySetResult();
        Window.Close();
        foreach (var tab in Panel.Tabs) tab.Dispose();
        Services.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    internal sealed class ListProvider : ResourceProvider
    {
        internal string[] Items { get; set; } = [];
        internal string[] LastItems { get; set; } = [];
        internal TaskCompletionSource? Finish { get; set; }
        internal Exception? Failure { get; set; }
        internal EntryData[]? Entries { get; set; }
        internal int SyntheticCount { get; set; }
        internal bool HasParent { get; set; }
        internal int ItemReferencesRequested { get; private set; }
        public override string Scheme => FileListFixture.Scheme;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => HasParent ? new Location(Scheme, "parent") : null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override ItemRef GetItemRef(Location location, in EntryData entry)
        {
            ItemReferencesRequested++;
            return base.GetItemRef(location, entry);
        }
        public override async Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            if (Failure is { } failure) throw failure;
            if (SyntheticCount > 0)
            {
                var batch = new EntryData[512];
                for (int start = 0; start < SyntheticCount; start += batch.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    int count = Math.Min(batch.Length, SyntheticCount - start);
                    for (int i = 0; i < count; i++) batch[i] = new EntryData($"item-{start + i:D7}", EntryKind.File, 1);
                    sink.AddBatch(batch.AsSpan(0, count));
                }
            }
            else sink.AddBatch(Entries ?? Items.Select(n => new EntryData(n, EntryKind.File, 1)).ToArray());
            // A configured gate is awaited before the last batch to exercise identity during streaming.
            if (Finish is { } finish)
            {
                await finish.Task.WaitAsync(ct);
                sink.AddBatch(LastItems.Select(n => new EntryData(n, EntryKind.File, 1)).ToArray());
            }
        }
    }
}
