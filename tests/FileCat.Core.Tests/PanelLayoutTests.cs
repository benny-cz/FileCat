using FileCat.Core.State;

namespace FileCat.Core.Tests;

/// <summary>The workspace layout tree (ADR-18, UX-009): docking, swapping, moving, and minimum sizes.</summary>
public sealed class PanelLayoutTests
{
    private static PanelLayoutNode Cols(params PanelLayoutNode[] c) => PanelLayoutNode.Split(false, c);
    private static PanelLayoutNode Rows(params PanelLayoutNode[] c) => PanelLayoutNode.Split(true, c);
    private static PanelLayoutNode P(string id, double size = 1) => PanelLayoutNode.Panel(id, size);

    [Fact]
    public void Flat_layouts_are_one_split()
    {
        Assert.Equal("cols(a,b)", PanelLayout.Default(["a", "b"]).ToString());
        Assert.Equal("rows(a,b,c)", PanelLayout.Default(["a", "b", "c"], stacked: true).ToString());
        Assert.Equal("a", PanelLayout.Default(["a"]).ToString());
        var sized = PanelLayout.Default(["a", "b"], sizes: [2, 1]);
        Assert.Equal([2.0, 1.0], sized.Children.Select(c => c.Size));
        Assert.True(PanelLayout.Matches(sized, ["b", "a"]));
        Assert.False(PanelLayout.Matches(sized, ["a"]));
        Assert.False(PanelLayout.Matches(Cols(P("a"), P("a")), ["a"]));
        Assert.False(PanelLayout.Matches(null, ["a"]));
    }

    [Fact]
    public void Docking_on_an_edge_puts_the_panel_beside_and_the_middle_swaps()
    {
        Assert.Equal("cols(a,rows(b,c))", PanelLayout.Insert(Cols(P("a"), P("b")), "c", "b", DockSide.Bottom).ToString());
        Assert.Equal("cols(rows(c,a),b)", PanelLayout.Dock(Cols(P("a"), P("b"), P("c")), "c", "a", DockSide.Top).ToString());
        Assert.Equal("cols(b,a)", PanelLayout.Dock(Cols(P("a"), P("b")), "a", "b", DockSide.Right).ToString());
        Assert.Equal("rows(b,a)", PanelLayout.Dock(Cols(P("a"), P("b")), "a", "b", DockSide.Bottom).ToString());
        Assert.Equal("cols(a,b)", PanelLayout.Dock(Cols(P("a"), P("b")), "a", "a", DockSide.Right).ToString());
        Assert.Equal("cols(c,rows(b,a))", PanelLayout.Swap(Cols(P("a"), Rows(P("b"), P("c"))), "a", "c").ToString());
        // The neighbor gives the new panel half its space.
        var halved = PanelLayout.Insert(Cols(P("a", 2), P("b", 2)), "c", "b", DockSide.Right);
        Assert.Equal([2.0, 1.0, 1.0], halved.Children.Select(c => c.Size));
    }

    [Fact]
    public void Moving_goes_past_the_neighbor_then_out_of_the_split_then_to_the_workspace_edge()
    {
        var two = Cols(P("a"), P("b"));
        Assert.Equal("cols(b,a)", PanelLayout.MoveToward(two, "a", DockSide.Right).ToString());
        Assert.Equal("cols(a,b)", PanelLayout.MoveToward(two, "a", DockSide.Left).ToString()); // already there
        Assert.Equal("rows(b,a)", PanelLayout.MoveToward(two, "a", DockSide.Bottom).ToString());
        Assert.Equal("rows(a,b)", PanelLayout.MoveToward(two, "a", DockSide.Top).ToString());
        // The third panel takes the whole top.
        Assert.Equal("rows(c,cols(a,b))", PanelLayout.MoveToward(Cols(P("a"), P("b"), P("c")), "c", DockSide.Top).ToString());
        // Out of its row, above it; out of its row, to the right of everything.
        var grid = Rows(Cols(P("a"), P("b")), P("c"));
        Assert.Equal("rows(a,b,c)", PanelLayout.MoveToward(grid, "a", DockSide.Top).ToString());
        Assert.Equal("cols(rows(a,c),b)", PanelLayout.MoveToward(grid, "b", DockSide.Right).ToString());
        Assert.Equal("cols(a,b,c)", PanelLayout.MoveToward(Cols(Rows(P("a"), P("b")), P("c")), "a", DockSide.Left).ToString());
        Assert.Equal("a", PanelLayout.MoveToward(P("a"), "a", DockSide.Left).ToString()); // a lone panel stays
    }

    [Fact]
    public void Rotating_turns_the_split_and_merges_it_into_one_that_runs_the_same_way()
    {
        Assert.Equal("rows(a,b)", PanelLayout.Rotate(Cols(P("a"), P("b")), "a").ToString());
        Assert.Equal("cols(a,b,c)", PanelLayout.Rotate(Cols(P("a"), Rows(P("b"), P("c"))), "b").ToString());
        Assert.Equal("a", PanelLayout.Rotate(P("a"), "a").ToString());
    }

    [Fact]
    public void Removing_gives_the_space_to_the_siblings_and_normalizing_keeps_proportions()
    {
        Assert.Equal("cols(a,c)", PanelLayout.Remove(Cols(P("a"), Rows(P("b"), P("c"))), "b").ToString());
        Assert.Equal("b", PanelLayout.Remove(Cols(P("a"), P("b")), "a").ToString());
        var nested = PanelLayout.Normalize(Cols(P("a", 1), PanelLayoutNode.Split(false, [P("b", 1), P("c", 3)], size: 2)));
        Assert.Equal("cols(a,b,c)", nested.ToString());
        Assert.Equal([1.0, 0.5, 1.5], nested.Children.Select(c => c.Size));
        Assert.All(PanelLayout.Equalize(nested).Children, c => Assert.Equal(1, c.Size));
    }

    [Fact]
    public void Neighbors_are_found_by_place_and_minimum_sizes_add_up()
    {
        var layout = Cols(P("a"), Rows(P("b", 1), P("c", 3)));
        var rects = PanelLayout.Arrange(layout);
        Assert.Equal(new UnitRect(0, 0, 0.5, 1), rects["a"]);
        Assert.Equal(new UnitRect(0.5, 0, 0.5, 0.25), rects["b"]);
        Assert.Equal("c", PanelLayout.Neighbor(layout, "a", DockSide.Right)); // overlaps a more than b does
        Assert.Equal("c", PanelLayout.Neighbor(layout, "b", DockSide.Bottom));
        Assert.Equal("a", PanelLayout.Neighbor(layout, "b", DockSide.Left));
        Assert.Null(PanelLayout.Neighbor(layout, "a", DockSide.Left));
        Assert.Null(PanelLayout.Neighbor(layout, "b", DockSide.Top));

        Assert.Equal((525.0, 285.0), PanelLayout.MinimumSize(Cols(P("a"), Rows(P("b"), P("c")))));
        Assert.True(PanelLayout.Fits(layout, 600, 300));
        Assert.False(PanelLayout.Fits(layout, 500, 300));
        Assert.False(PanelLayout.Fits(layout, 600, 280));
    }
}
