using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Controls;

namespace FileCat.App.Tests;

/// <summary>
/// I664: in Pictures mode the accessibility tree holds every tile the grid draws, those of the partly shown row too, and
/// nothing more than one row past the view. rc.2 exposed the list's count there (its whole rows and one item), so a
/// drawn tile went missing: 9 items in 2 columns, 6 drawn, 5 exposed. The list keeps its own rule.
/// </summary>
public sealed class PictureGridAutomationTests
{
    [AvaloniaTheory]
    [InlineData(2, 9)] // the record's folder
    [InlineData(2, 20)]
    [InlineData(3, 20)]
    [InlineData(4, 20)]
    [InlineData(4, 9)] // the partly shown row holds a single tile: no more than the items
    public async Task Every_drawn_tile_is_exposed_and_nothing_past_the_partly_shown_row(int columns, int items)
    {
        using var f = new FileListFixture(Enumerable.Range(1, items).Select(i => $"tile-{i:D2}.txt").ToArray());
        await f.Load();
        f.List.PictureMode = true;
        // Columns are 184 pixels wide, beside the scroll bar: room for exactly this many.
        f.Window.Width = columns * 184 + 112;
        Dispatcher.UIThread.RunJobs();
        double tile = f.List.GetRowBounds(0).Height;
        // Two whole rows of tiles and half of a third; the fixture's button below the list is 28 pixels high.
        f.Window.Height = 28 + tile * 2.5;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(tile, f.List.GetRowBounds(0).Height);
        Assert.Equal(0, f.List.GetRowBounds(columns - 1).Top);
        Assert.Equal(tile, f.List.GetRowBounds(columns).Top);
        Assert.Equal(2 * columns, f.List.VisibleRowCapacity);
        var peer = Assert.IsType<FileListAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(f.List));

        int maximumTop = f.List.MaximumTopRow;
        Assert.True(maximumTop > 0, "The grid has rows below the view.");
        foreach (int top in new[] { 0, columns, maximumTop }.Distinct())
        {
            f.List.ScrollToTopRow(top);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(top, f.List.TopRow);
            // Drawn: the tiles that meet the view, as the grid clips them. At the top that is the record's case.
            var drawn = Enumerable.Range(top, items - top).Where(row => f.List.VisibleRowBounds(row) is { Width: > 0, Height: > 0 })
                .Select(Name).ToList();
            if (top == 0)
            {
                Assert.Equal(Math.Min(items, 3 * columns), drawn.Count);
                var partly = f.List.VisibleRowBounds(2 * columns);
                Assert.InRange(partly.Height, 1, tile - 1);
            }
            var exposed = peer.GetChildren().Where(p => p.GetAutomationControlType() == AutomationControlType.ListItem)
                .Select(p => p.GetName()).ToList();
            string? focused = f.Tab.Listing.FocusedIndex is >= 0 and var index ? Name(index) : null;
            Assert.True(drawn.All(exposed.Contains),
                $"Top {top}, {columns} columns: drawn {string.Join(", ", drawn)}; exposed {string.Join(", ", exposed)}.");
            // Beyond what is drawn, only the focused tile when it is out of view: nothing past the partly shown row.
            Assert.DoesNotContain(exposed.Except(drawn), name => name != focused);
            Assert.Equal(exposed.Count, exposed.Distinct().Count());
        }

        string Name(int row) => f.Tab.Listing.GetVisible(row).Name;
    }

    [AvaloniaFact]
    public async Task The_list_still_exposes_its_whole_rows_and_the_one_partly_shown()
    {
        using var f = new FileListFixture(Enumerable.Range(0, 100).Select(i => $"item-{i:D3}").ToArray());
        await f.Load();
        Assert.False(f.List.PictureMode);
        // A partly shown last row, whatever the font's metrics.
        double row = f.List.GetRowBounds(f.List.TopRow + 1).Top - f.List.GetRowBounds(f.List.TopRow).Top;
        f.Window.Height -= row / 2;
        Dispatcher.UIThread.RunJobs();
        int capacity = f.List.VisibleRowCapacity;
        if (f.List.VisibleRowBounds(capacity) is not { Height: > 0 })
        {
            f.Window.Height += row / 3;
            Dispatcher.UIThread.RunJobs();
            capacity = f.List.VisibleRowCapacity;
        }
        Assert.InRange(f.List.VisibleRowBounds(capacity).Height, double.Epsilon, row - double.Epsilon);
        var peer = Assert.IsType<FileListAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(f.List));
        var exposed = peer.GetChildren().Where(p => p.GetAutomationControlType() == AutomationControlType.ListItem).Select(p => p.GetName());
        Assert.Equal(Enumerable.Range(0, capacity + 1).Select(i => $"item-{i:D3}"), exposed);
    }
}
