using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class ClippedCellInspectionTests
{
    [AvaloniaFact]
    public void Both_text_layout_paths_retain_the_full_source_and_detect_clipping()
    {
        var face = new Typeface("Arial");
        var glyphs = SimpleGlyphs.TryCreate(face, 14);
        if (glyphs is not null)
        {
            using var shortRun = glyphs.TryLayout("short.txt", Brushes.Black, 400);
            using var clippedRun = glyphs.TryLayout("annual-report-final.txt", Brushes.Black, 25);
            // The headless font backend can expose a face without a full glyph map.
            if (shortRun is not null && clippedRun is not null)
            {
                Assert.False(shortRun.IsTruncated);
                Assert.True(clippedRun.IsTruncated);
                Assert.Equal("annual-report-final.txt", clippedRun.Source);
            }
        }

        using var shortFormatted = CellText.Formatted("短", Brushes.Black, face, 14, 400);
        using var clippedFormatted = CellText.Formatted("Annual financial report 2026 — 最終版", Brushes.Black, face, 14, 35);
        Assert.False(shortFormatted.IsTruncated);
        Assert.True(clippedFormatted.IsTruncated);
        Assert.Equal("Annual financial report 2026 — 最終版", clippedFormatted.Source);
    }

    [AvaloniaFact]
    public async Task Hover_reveals_only_clipped_cells_and_includes_the_split_extension()
    {
        string longName = new string('a', 95) + "." + new string('z', 65);
        using var f = new FileListFixture("short.txt", longName);
        await f.Load();
        f.Tab.ColumnProfile = 0; // Details: Name and Ext are separate cells.
        Dispatcher.UIThread.RunJobs();

        int shortRow = Row(f, "short.txt");
        Hover(f, shortRow, 65);
        Assert.Null(ToolTip.GetTip(f.List));

        int longRow = Row(f, longName);
        Hover(f, longRow, 65);
        Assert.Equal("Full name: " + longName, ToolTip.GetTip(f.List));

        // A long extension is independently clipped, yet inspection gives the complete identity.
        double extX = X(f.List, 1) + 12;
        Hover(f, longRow, extX);
        Assert.Equal("Full name: " + longName, ToolTip.GetTip(f.List));
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Clipped_folder_and_details_cells_expose_complete_safe_provider_text()
    {
        string folder = "folder-" + new string('f', 95) + "\u202E";
        string detail = "group-" + new string('d', 95) + "\u200B";
        var entry = new EntryData("result.txt", EntryKind.File)
        {
            Tag = new ResultTag(new Location(FileListFixture.Scheme, "first"), "result.txt", EntryKind.File)
            {
                Folder = folder,
                Note = detail,
            },
        };
        using var f = new FileListFixture(new FileListFixture.ListProvider { Entries = [entry] });
        f.Services.Columns.Replace([("Inspect", [
            new ColumnSpec(ColumnField.Name, "Name", 90),
            new ColumnSpec(ColumnField.Folder, "Folder", 65),
            new ColumnSpec(ColumnField.Details, "Details", 65),
        ])]);
        f.Tab.ColumnProfile = 0;
        await f.Load();
        Assert.Equal(detail.Replace("\u200B", "\\u200B"), f.List.CellString(entry,
            new ColumnSpec(ColumnField.Details, "Details", 65)));
        Dispatcher.UIThread.RunJobs();
        // Listing completion can precede the headless compositor's next frame.
        // Render explicitly before asserting which cells were actually drawn.
        f.Window.CaptureRenderedFrame()?.Dispose();
        var rendered = (System.Collections.IDictionary)typeof(FileListControl).GetField("_renderedCells",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.List)!;
        Assert.True(rendered.Contains((0, 2)), $"Details cell was not rendered; width={f.List.Bounds.Width}, x={X(f.List, 2)}");
        Hover(f, 0, X(f.List, 1) + 15);
        Assert.Equal("Folder: " + folder.Replace("\u202E", "\\u202E"), ToolTip.GetTip(f.List));
        Hover(f, 0, X(f.List, 2) + 15);
        Assert.Equal("Details: " + detail.Replace("\u200B", "\\u200B"), ToolTip.GetTip(f.List));
        f.Click(0);
        string report = Assert.IsType<string>(f.List.FocusedInspectionText());
        Assert.Contains(folder.Replace("\u202E", "\\u202E"), report);
        Assert.Contains(detail.Replace("\u200B", "\\u200B"), report);
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Keyboard_inspection_reports_complete_values_without_metadata_or_item_requests()
    {
        string name = new string('n', 170) + ".txt\u200B";
        using var f = new FileListFixture(name);
        await f.Load();
        f.Click(Row(f, name));
        string? report = null;
        f.List.InspectRequested += (_, value) => report = value;
        f.List.Focus();
        f.Window.KeyPress(Key.I, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.I, "I");
        Assert.NotNull(report);
        Assert.Contains("Focused item: " + new string('n', 170) + ".txt\\u200B", report);
        Assert.Contains("zero-width space U+200B", report);
        Assert.Equal(0, f.Provider.ItemReferencesRequested);
    }

    [AvaloniaFact]
    public async Task Main_window_shortcut_opens_read_only_full_value_dialog()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string name = new string('r', 120) + ".txt";
            File.WriteAllText(Path.Combine(root, "files", name), "content");
            var list = window.GetVisualDescendants().OfType<FileListControl>()
                .First(l => l.Tab?.Location?.Path == Path.Combine(root, "files"));
            list.Tab!.Refresh();
            await FileListFixture.Wait(() => Enumerable.Range(0, list.Tab.Listing.VisibleCount)
                .Any(i => list.Tab.Listing.GetVisible(i).Name == name));
            int row = Enumerable.Range(0, list.Tab.Listing.VisibleCount)
                .Single(i => list.Tab.Listing.GetVisible(i).Name == name);
            list.Tab.Listing.SetFocus(row);
            list.Focus();
            window.KeyPress(Key.I, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.I, "I");
            await FileListFixture.Wait(() => vm.Dialogs.IsOpen);
            var text = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Full values of focused item");
            Assert.True(text.IsReadOnly);
            Assert.Contains(name, text.Text);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await FileListFixture.Wait(() => !vm.Dialogs.IsOpen);

            var panel = window.GetVisualDescendants().OfType<PanelView>()
                .Single(p => ReferenceEquals(p.List, list));
            var menu = ContextMenuFactory.Build(vm);
            panel.AppendInspectionItem(menu);
            var inspect = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>()
                .Single(item => item.Header as string == "Inspect focused row…");
            Assert.True(inspect.IsEnabled);
            inspect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await FileListFixture.Wait(() => vm.Dialogs.IsOpen);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(name, window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Full values of focused item").Text);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await FileListFixture.Wait(() => !vm.Dialogs.IsOpen);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Hover_tip_retires_on_scroll_sort_refresh_and_tab_switch()
    {
        string old = "old-" + new string('x', 145);
        using var f = new FileListFixture([old, .. Enumerable.Range(0, 70).Select(i => $"row-{i:D2}-" + new string('y', 145))]);
        await f.Load();
        Hover(f, Row(f, old), 65);
        Assert.Equal("Full name: " + old, ToolTip.GetTip(f.List));

        var scroll = f.List.GetVisualDescendants().OfType<ScrollBar>()
            .Single(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical);
        scroll.Value = Math.Min(10, scroll.Maximum);
        Assert.Null(ToolTip.GetTip(f.List));

        Hover(f, 10, 65);
        Assert.NotNull(ToolTip.GetTip(f.List));
        f.Tab.Listing.Sort = new SortSpec(Descending: true);
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
        Assert.Null(ToolTip.GetTip(f.List));

        Dispatcher.UIThread.RunJobs();
        Hover(f, 10, 65);
        Assert.NotNull(ToolTip.GetTip(f.List));
        var beforeRefresh = f.Tab.Listing.Store;
        f.Tab.Refresh();
        await FileListFixture.Wait(() => !ReferenceEquals(beforeRefresh, f.Tab.Listing.Store));
        Assert.Null(ToolTip.GetTip(f.List));

        Dispatcher.UIThread.RunJobs();
        f.Click(Row(f, old));
        Hover(f, Row(f, old), 65);
        Assert.Equal("Full name: " + old, ToolTip.GetTip(f.List));
        f.Provider.Items = ["other-short.txt"];
        var other = f.Panel.OpenTab(new FileCat.Core.Resources.Location(FileListFixture.Scheme, "second"));
        await FileListFixture.Wait(() => other.Listing.State == ListingState.Complete);
        f.List.Tab = other;
        Assert.Null(ToolTip.GetTip(f.List));
        Hover(f, 0, 65);
        Assert.Null(ToolTip.GetTip(f.List));
    }

    private static int Row(FileListFixture f, string name) =>
        Enumerable.Range(0, f.Tab.Listing.VisibleCount).Single(row => f.Tab.Listing.GetVisible(row).Name == name);

    private static void Hover(FileListFixture f, int row, double x)
    {
        Dispatcher.UIThread.RunJobs();
        var point = f.List.TranslatePoint(new Point(x, f.List.GetRowBounds(row).Center.Y), f.Window)!.Value;
        f.Window.MouseMove(point);
    }

    private static double X(FileListControl list, int column) =>
        ((double[])typeof(FileListControl).GetField("_columnX", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.GetValue(list)!)[column];
}
