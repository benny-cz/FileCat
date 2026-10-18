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

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public AppServices Services { get; }
        public PanelViewModel Panel { get; }
        public string Folder { get; }
        public TabViewModel Tab { get; }
        public FileListControl List { get; }
        public Window Window { get; }

        public Fixture(int width, int fontSize)
        {
            _root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
            Folder = Directory.CreateDirectory(Path.Combine(_root, "files")).FullName;
            File.WriteAllText(Path.Combine(Folder, "a.txt"), "alpha");
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
