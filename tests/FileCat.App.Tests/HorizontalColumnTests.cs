using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class HorizontalColumnTests
{
    [AvaloniaTheory]
    [InlineData(320, 13)]
    [InlineData(480, 13)]
    [InlineData(640, 13)]
    [InlineData(800, 13)]
    [InlineData(320, 20)]
    [InlineData(640, 20)]
    public void Full_profile_columns_are_reachable_at_narrow_widths_and_large_fonts(int width, int fontSize)
    {
        using var f = new Fixture(width, fontSize);
        Assert.True(f.List.HorizontalScrollBarVisible);
        Assert.True(f.List.HorizontalExtent > f.List.HorizontalViewport);
        for (int i = 0; i < f.Tab.Columns.Length; i++)
        {
            f.List.ScrollToHorizontalOffset(f.List.HorizontalOffset + f.List.ColumnLeftEdge(i));
            f.Frame();
            Assert.InRange(f.List.ColumnLeftEdge(i), -0.1, f.List.HorizontalViewport);
            Assert.True(f.List.ColumnRightEdge(i) > 0, $"Column {i} could not be reached.");
        }
        f.List.ScrollToHorizontalOffset(double.MaxValue);
        f.Frame();
        Assert.InRange(f.List.ColumnRightEdge(f.Tab.Columns.Length - 1), 0, f.List.HorizontalViewport + 0.1);
        f.List.ScrollToHorizontalOffset(0);
        f.Frame();
        Assert.Equal(0, f.List.HorizontalOffset);
        Assert.InRange(f.List.ColumnLeftEdge(0), 0, 0.1);
    }

    [AvaloniaFact]
    public async Task Scrolled_header_sort_resize_rename_and_keyboard_use_the_displayed_geometry()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        int size = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Size);
        Assert.True(size > 0);
        f.List.ScrollToHorizontalOffset(f.List.HorizontalOffset + f.List.ColumnLeftEdge(size) - 30);
        f.Frame();
        double headerX = f.List.ColumnLeftEdge(size) + 20;
        Assert.InRange(headerX, 5, f.List.HorizontalViewport - 5);
        f.Window.MouseDown(new Point(headerX, 8), MouseButton.Left);
        f.Window.MouseUp(new Point(headerX, 8), MouseButton.Left);
        Assert.Equal(SortField.Size, f.Tab.Listing.Sort.Field);

        double border = f.List.ColumnRightEdge(size);
        Assert.InRange(border, 8, f.List.HorizontalViewport - 8);
        double oldWidth = f.Services.Columns.Profiles[2].Columns[size].Width;
        f.Window.MouseMove(new Point(border, 8));
        f.Window.MouseDown(new Point(border, 8), MouseButton.Left);
        f.Window.MouseMove(new Point(border + 25, 8));
        f.Window.MouseUp(new Point(border + 25, 8), MouseButton.Left);
        Assert.True(f.Services.Columns.Profiles[2].Columns[size].Width > oldWidth);

        f.List.ScrollToHorizontalOffset(double.MaxValue);
        f.Frame();
        Assert.True(f.List.HorizontalOffset > 0);
        f.Tab.Listing.SetFocus(1);
        var rename = f.List.BeginRename(new PromptOptions("Rename", "Name") { Text = "a.txt" });
        Assert.NotNull(rename);
        f.Frame();
        Assert.Equal(0, f.List.HorizontalOffset);
        var editor = f.Window.GetVisualDescendants().OfType<TextBox>().Single(t =>
            AutomationProperties.GetName(t) == "Rename selected item");
        Assert.InRange(editor.Bounds.X, 0, f.List.ColumnRightEdge(0));
        f.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(await rename!);

        f.List.Focus();
        f.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Assert.True(f.List.HorizontalOffset > 0);
        f.Window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal(0, f.List.HorizontalOffset);
        f.Window.MouseWheel(new Point(70, 70), new Vector(-1, 0));
        Assert.True(f.List.HorizontalOffset > 0);
    }

    [AvaloniaFact]
    public void Automation_reports_and_controls_horizontal_scroll_and_profile_switch_resets_it()
    {
        using var f = new Fixture(320, 13);
        var peer = Assert.IsAssignableFrom<IScrollProvider>(ControlAutomationPeer.CreatePeerForElement(f.List));
        Assert.True(peer.HorizontallyScrollable);
        Assert.InRange(peer.HorizontalViewSize, 1, 99);
        peer.SetScrollPercent(100, ScrollPatternIdentifiers.NoScroll);
        f.Frame();
        Assert.InRange(peer.HorizontalScrollPercent, 99.9, 100);
        peer.Scroll(ScrollAmount.LargeDecrement, ScrollAmount.NoAmount);
        Assert.True(peer.HorizontalScrollPercent < 100);
        f.Tab.ColumnProfile = 0;
        f.Frame();
        Assert.Equal(0, f.List.HorizontalOffset);
        f.Tab.ColumnProfile = 2;
        f.Frame();
        Assert.Equal(0, f.List.HorizontalOffset);
        f.Services.Columns.Replace([("Compact", [
            new ColumnSpec(ColumnField.Name, "Name", 1, Star: true),
        ])]);
        f.Tab.ColumnProfile = 0;
        f.Frame();
        Assert.False(peer.HorizontallyScrollable);
        Assert.Equal(ScrollPatternIdentifiers.NoScroll, peer.HorizontalScrollPercent);
    }

    [AvaloniaFact]
    public async Task Vertical_bar_and_multiple_fill_columns_keep_the_horizontal_extent_reachable()
    {
        using var f = new Fixture(320, 13);
        f.Services.Columns.Replace([("Two fill", [
            new ColumnSpec(ColumnField.Name, "Name", 1, Star: true),
            new ColumnSpec(ColumnField.Extension, "Ext", 1, Star: true),
            new ColumnSpec(ColumnField.Size, "Size", 130, RightAlign: true),
        ])]);
        for (int i = 0; i < 100; i++) File.WriteAllText(Path.Combine(f.Folder, $"item-{i:D3}.txt"), "x");
        f.Tab.Refresh();
        await f.Load(100);
        f.Frame();
        Assert.True(f.List.VerticalScrollBarVisible);
        Assert.True(f.List.HorizontalScrollBarVisible);
        Assert.True(f.List.HorizontalViewport < f.List.Bounds.Width);
        f.List.ScrollToHorizontalOffset(double.MaxValue);
        f.Frame();
        Assert.InRange(f.List.ColumnRightEdge(2), 0, f.List.HorizontalViewport + 0.1);
        Assert.True(f.List.VisibleRowCapacity < (int)(f.List.Bounds.Height / 20));

        var other = f.Panel.OpenTab(Location.FileSystem(f.Folder));
        f.List.Tab = other;
        f.Frame();
        Assert.Equal(0, f.List.HorizontalOffset);
        f.List.Tab = f.Tab;
        other.Dispose();
    }

    [AvaloniaFact]
    public async Task Precision_wheel_batches_scroll_the_same_rows_and_one_row_paging_moves_focus()
    {
        using var f = new Fixture(480, 13, fileCount: 30);
        await f.Load(30);
        var point = new Point(70, 70);
        for (int i = 0; i < 10; i++) f.Window.MouseWheel(point, new Vector(0, -0.1));
        Assert.Equal(3, f.List.TopRow);
        f.List.ScrollToTopRow(0);
        f.Window.MouseWheel(point, new Vector(0, -1));
        Assert.Equal(3, f.List.TopRow);
        for (int i = 0; i < 10; i++) f.Window.MouseWheel(point, new Vector(0, 0.1));
        Assert.Equal(0, f.List.TopRow);
        f.Window.MouseWheel(point, new Vector(0, 0.1));
        f.Window.MouseWheel(point, new Vector(0, -0.1));
        Assert.Equal(0, f.List.TopRow);
        for (int i = 0; i < 3; i++) f.Window.MouseWheel(point, new Vector(0, -0.1));
        Assert.Equal(1, f.List.TopRow);
        f.List.ScrollToTopRow(0);
        for (int i = 0; i < 2; i++) f.Window.MouseWheel(point, new Vector(0, -0.1));
        f.List.ScrollToTopRow(1);
        for (int i = 0; i < 2; i++) f.Window.MouseWheel(point, new Vector(0, -0.1));
        Assert.Equal(1, f.List.TopRow);

        f.Window.Height = 45;
        f.Frame();
        Assert.Equal(1, f.List.VisibleRowCapacity);
        f.Tab.Listing.SetFocus(0);
        f.List.Focus();
        f.Window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Assert.Equal(1, f.Tab.Listing.FocusedIndex);
        f.Window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
        Assert.Equal(0, f.Tab.Listing.FocusedIndex);
    }

    [AvaloniaFact]
    public async Task Precision_wheel_keeps_inline_rename_attached_without_committing_it()
    {
        using var f = new Fixture(480, 13, fileCount: 30);
        await f.Load(30);
        f.Tab.Listing.SetFocus(5);
        var rename = f.List.BeginRename(new PromptOptions("Rename", "Name") { Text = "row-004.txt" });
        Assert.NotNull(rename);
        f.Frame();
        var editor = f.Window.GetVisualDescendants().OfType<TextBox>().Single(t =>
            AutomationProperties.GetName(t) == "Rename selected item");
        double before = editor.TranslatePoint(new Point(0, 0), f.List)!.Value.Y;
        for (int i = 0; i < 4; i++) f.Window.MouseWheel(new Point(70, 70), new Vector(0, -0.1));
        f.Frame();
        double after = editor.TranslatePoint(new Point(0, 0), f.List)!.Value.Y;
        Assert.Equal(1, f.List.TopRow);
        Assert.InRange(before - after, f.List.GetRowBounds(5).Height - 1, f.List.GetRowBounds(5).Height + 1);
        Assert.True(f.List.IsRenaming);
        Assert.False(rename!.IsCompleted);
        f.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(await rename);
    }

    /// <summary>
    /// A border held while the list's columns are replaced (another profile with the button still down, Alt+1) stops
    /// where it is: its width is not stored for the column now in that place.
    /// </summary>
    [AvaloniaFact]
    public async Task A_border_drag_stops_when_the_columns_it_holds_are_replaced()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        var (full, brief) = (f.Services.Columns.Profiles[2].Columns, f.Services.Columns.Profiles[1].Columns);
        // Full's Ext, whose place Brief's Size takes.
        double border = HoldNarrower(f, Array.FindIndex(full, c => c.Field == ColumnField.Extension));
        f.Tab.ColumnProfile = 1;
        f.Window.MouseMove(new Point(border - 60, 8));
        f.Window.MouseUp(new Point(border - 60, 8), MouseButton.Left);
        f.Frame();
        Assert.Same(full, f.Services.Columns.Profiles[2].Columns);
        Assert.Same(brief, f.Services.Columns.Profiles[1].Columns);
    }

    /// <summary>The same when the list is given another tab mid-drag (Ctrl+Tab with the button still down).</summary>
    [AvaloniaFact]
    public async Task A_border_drag_stops_when_the_list_shows_another_tab()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        var second = f.Panel.OpenTab(Location.FileSystem(f.Folder));
        second.ColumnProfile = 1;
        var (full, brief) = (f.Services.Columns.Profiles[2].Columns, f.Services.Columns.Profiles[1].Columns);
        double border = HoldNarrower(f, Array.FindIndex(full, c => c.Field == ColumnField.Extension));
        f.List.Tab = second;
        f.Window.MouseMove(new Point(border - 60, 8));
        f.Window.MouseUp(new Point(border - 60, 8), MouseButton.Left);
        f.Frame();
        Assert.Same(full, f.Services.Columns.Profiles[2].Columns);
        Assert.Same(brief, f.Services.Columns.Profiles[1].Columns);
        second.Dispose();
    }

    /// <summary>
    /// Pictures turned on while a border is held (they have no columns): the drag stops with nothing stored, and the
    /// column is drawn as the profile keeps it once the pictures are off again.
    /// </summary>
    [AvaloniaFact]
    public async Task A_border_drag_stops_when_pictures_replace_the_columns()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        var stored = f.Services.Columns.Profiles[2].Columns;
        int ext = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Extension);
        double before = f.List.ColumnRightEdge(ext) - f.List.ColumnLeftEdge(ext);
        double border = HoldNarrower(f, ext);
        f.List.PictureMode = true;
        f.Window.MouseMove(new Point(border - 60, 8));
        f.Window.MouseUp(new Point(border - 60, 8), MouseButton.Left);
        Assert.Same(stored, f.Services.Columns.Profiles[2].Columns);
        f.List.PictureMode = false;
        f.Frame();
        Assert.Equal(before, f.List.ColumnRightEdge(ext) - f.List.ColumnLeftEdge(ext), 1);
    }

    /// <summary>
    /// The pointer taken away mid-drag (another window took it): the drag ends as a release ends it, the width as drawn
    /// stored, and the button coming up later stores nothing more.
    /// </summary>
    [AvaloniaFact]
    public async Task A_border_drag_ends_as_a_release_when_the_pointer_is_taken_away()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        int ext = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Extension);
        double stored = f.Services.Columns.Profiles[2].Columns[ext].Width;
        double border = HoldNarrower(f, ext);
        var pointer = Assert.IsAssignableFrom<IPointer>(typeof(FileListControl)
            .GetField("_resizePointer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(f.List));
        pointer.Capture(null);
        double taken = f.Services.Columns.Profiles[2].Columns[ext].Width;
        Assert.True(taken < stored, $"the width as drawn should be stored when the pointer is taken ({taken}, was {stored})");
        var after = f.Services.Columns.Profiles[2].Columns;
        f.Window.MouseMove(new Point(border - 60, 8));
        f.Window.MouseUp(new Point(border - 60, 8), MouseButton.Left);
        Assert.Same(after, f.Services.Columns.Profiles[2].Columns);
    }

    /// <summary>
    /// A border dragged in a list scrolled sideways, or a width stored from another list on the profile, leaves the list
    /// where it is scrolled (it once jumped back to the first column, the dragged border out of sight); another folder
    /// starts from the first column.
    /// </summary>
    [AvaloniaFact]
    public async Task A_dragged_width_keeps_the_list_where_it_is_scrolled()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        int created = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Created);
        f.List.ScrollToHorizontalOffset(f.List.HorizontalOffset + f.List.ColumnRightEdge(created) - f.List.HorizontalViewport / 2);
        f.Frame();
        double offset = f.List.HorizontalOffset;
        Assert.True(offset > 0);
        double stored = f.Services.Columns.Profiles[2].Columns[created].Width;
        double border = f.List.ColumnRightEdge(created);
        Assert.InRange(border, 8, f.List.HorizontalViewport - 8);

        f.Window.MouseMove(new Point(border, 8));
        f.Window.MouseDown(new Point(border, 8), MouseButton.Left);
        f.Window.MouseMove(new Point(border + 20, 8));
        f.Window.MouseUp(new Point(border + 20, 8), MouseButton.Left);
        f.Frame();
        Assert.True(f.Services.Columns.Profiles[2].Columns[created].Width > stored);
        Assert.Equal(offset, f.List.HorizontalOffset, 1);

        int size = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Size);
        f.Services.Columns.SetWidth(2, size, f.Services.Columns.Profiles[2].Columns[size].Width + 10);
        f.Frame();
        Assert.Equal(offset, f.List.HorizontalOffset, 1);

        f.Tab.Navigate(Location.FileSystem(Path.GetDirectoryName(f.Folder)!));
        f.Frame();
        Assert.Equal(0, f.List.HorizontalOffset);
    }

    /// <summary>
    /// A width stored from another list on the profile while a border is held here (two pointers at once) leaves the
    /// drag going: the same columns at other widths still hold the column it is on, and its width is stored on release.
    /// </summary>
    [AvaloniaFact]
    public async Task A_border_drag_goes_on_when_another_list_stores_a_width()
    {
        using var f = new Fixture(480, 13);
        await f.Load();
        int ext = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Extension);
        int size = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Size);
        double stored = f.Services.Columns.Profiles[2].Columns[ext].Width;
        double border = HoldNarrower(f, ext);
        f.Services.Columns.SetWidth(2, size, f.Services.Columns.Profiles[2].Columns[size].Width + 10);
        f.Window.MouseMove(new Point(border - 40, 8));
        f.Window.MouseUp(new Point(border - 40, 8), MouseButton.Left);
        f.Frame();
        Assert.True(f.Services.Columns.Profiles[2].Columns[ext].Width < stored, "the held border's width should be stored on release");
    }

    /// <summary>Presses a column's border on the header row and drags it 30 pixels narrower, holding the button.</summary>
    private static double HoldNarrower(Fixture f, int column)
    {
        double border = f.List.ColumnRightEdge(column);
        Assert.InRange(border, 40, f.List.HorizontalViewport - 8);
        double width = f.List.ColumnRightEdge(column) - f.List.ColumnLeftEdge(column);
        f.Window.MouseMove(new Point(border, 8));
        f.Window.MouseDown(new Point(border, 8), MouseButton.Left);
        f.Window.MouseMove(new Point(border - 30, 8));
        f.Frame();
        // The drag is under way: the column is drawn narrower while the button is held.
        Assert.True(f.List.ColumnRightEdge(column) - f.List.ColumnLeftEdge(column) < width - 20, "the border drag did not start");
        return border;
    }

    /// <summary>
    /// A border dragged narrower than a profile keeps any column (24 pixels at the default font) ends drawn at the width
    /// the profile keeps, as every other list on the profile draws it, not at the drag's own narrowest.
    /// </summary>
    [AvaloniaFact]
    public async Task A_drag_the_profile_cannot_take_ends_at_the_width_the_profile_keeps()
    {
        using var f = new Fixture(480, 20);
        await f.Load();
        int ext = Array.FindIndex(f.Tab.Columns, c => c.Field == ColumnField.Extension);
        f.Services.Columns.SetWidth(2, ext, 24);
        f.Frame();
        f.List.ScrollToHorizontalOffset(f.List.HorizontalOffset + f.List.ColumnRightEdge(ext) - f.List.HorizontalViewport / 2);
        f.Frame();
        double kept = f.List.ColumnRightEdge(ext) - f.List.ColumnLeftEdge(ext);
        Assert.True(kept > 30, $"the narrowest kept width should draw wider than a drag's narrowest at this font ({kept})");
        double border = f.List.ColumnRightEdge(ext);
        Assert.InRange(border, 8, f.List.HorizontalViewport - 8);

        f.Window.MouseMove(new Point(border, 8));
        f.Window.MouseDown(new Point(border, 8), MouseButton.Left);
        f.Window.MouseMove(new Point(border - 200, 8));
        f.Window.MouseUp(new Point(border - 200, 8), MouseButton.Left);
        f.Frame();

        Assert.Equal(24, f.Services.Columns.Profiles[2].Columns[ext].Width);
        Assert.Equal(kept, f.List.ColumnRightEdge(ext) - f.List.ColumnLeftEdge(ext), 1);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public AppServices Services { get; }
        public PanelViewModel Panel { get; }
        public string Folder { get; }
        public TabViewModel Tab { get; }
        public FileListControl List { get; }
        public Window Window { get; }

        public Fixture(int width, int fontSize, int fileCount = 1)
        {
            _root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
            Folder = Directory.CreateDirectory(Path.Combine(_root, "files")).FullName;
            File.WriteAllText(Path.Combine(Folder, "a.txt"), "alpha");
            for (int i = 1; i < fileCount; i++) File.WriteAllText(Path.Combine(Folder, $"row-{i:D3}.txt"), "content");
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: _root));
            var workspace = new WorkspaceViewModel(Services);
            Panel = new PanelViewModel(workspace, Services);
            workspace.Panels.Add(Panel);
            Tab = Panel.OpenTab(Location.FileSystem(Folder));
            Tab.ColumnProfile = 2;
            List = new FileListControl { Tab = Tab, FontSize = fontSize };
            Window = new Window { Width = width, Height = 300, Content = List };
            Window.Show();
            Frame();
        }

        public void Frame() => Window.CaptureRenderedFrame();

        public async Task Load(int count = 1)
        {
            for (int i = 0; i < 250 && (Tab.Listing.State != ListingState.Complete || Tab.Listing.VisibleCount < count); i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(ListingState.Complete, Tab.Listing.State);
            Assert.True(Tab.Listing.VisibleCount >= count);
            Frame();
        }

        public void Dispose()
        {
            Window.Close();
            Tab.Dispose();
            Services.Dispose();
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }
}
