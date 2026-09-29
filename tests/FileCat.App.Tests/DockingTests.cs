using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>Arranging panels (plan §4.1, ADR-18, UX-009): moving, docking, swapping, and keeping the arrangement.</summary>
public sealed class DockingTests
{
    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
    }

    private static Rect Bounds(Visual view, Visual relativeTo) =>
        new(view.TranslatePoint(default, relativeTo) ?? default, view.Bounds.Size);

    [AvaloniaFact]
    public async Task Keyboard_moves_put_a_panel_below_and_back_and_numbers_follow_the_places()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var ws = vm.Workspace;
            var (first, second) = (ws.Panels[0], ws.Panels[1]);
            Assert.Equal($"cols({first.Id},{second.Id})", ws.LayoutTree.ToString());
            ws.Activate(first);
            vm.Execute(CommandIds.PanelMoveDown);
            Assert.Equal($"rows({second.Id},{first.Id})", ws.LayoutTree.ToString());
            // Numbers follow reading order; identity, tabs, and the target stay with each panel.
            Assert.Equal(1, second.Number);
            Assert.Equal(2, first.Number);
            Assert.Same(first, ws.ActivePanel);
            Assert.Same(second, ws.ActiveTarget);
            await Task.Delay(50, ct);
            var host = window.FindControl<Grid>("WorkspaceHost")!;
            var split = Assert.IsType<Grid>(Assert.Single(host.Children));
            Assert.Equal(3, split.RowDefinitions.Count); // two panels and a splitter, stacked

            vm.Execute(CommandIds.PanelMoveUp);
            Assert.Equal($"rows({first.Id},{second.Id})", ws.LayoutTree.ToString());
            vm.Execute(CommandIds.PanelMoveUp); // already at the top
            Assert.Contains("already at the top edge", vm.Notification);
            vm.Execute(CommandIds.RotatePanels);
            Assert.Equal($"cols({first.Id},{second.Id})", ws.LayoutTree.ToString());

            // A third panel below the active one; Alt+Shift+Right moves it out of that column, then past the next panel.
            vm.Execute(CommandIds.AddPanelBelow);
            var third = ws.ActivePanel!;
            Assert.Equal($"cols(rows({first.Id},{third.Id}),{second.Id})", ws.LayoutTree.ToString());
            window.KeyPress(Key.Right, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            Assert.Equal($"cols({first.Id},{third.Id},{second.Id})", ws.LayoutTree.ToString());
            window.KeyPress(Key.Right, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            Assert.Equal($"cols({first.Id},{second.Id},{third.Id})", ws.LayoutTree.ToString());
            Assert.Same(third, ws.ActivePanel); // the moved panel keeps the keyboard
            vm.Execute(CommandIds.EqualizePanels);
            Assert.All(ws.LayoutTree.Children, c => Assert.Equal(1, c.Size));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Dragging_a_panels_number_onto_another_panels_edge_docks_it_and_the_middle_swaps()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var ws = vm.Workspace;
            var (first, second) = (ws.Panels[0], ws.Panels[1]);
            await Task.Delay(100, ct);
            var host = window.FindControl<Grid>("WorkspaceHost")!;
            var views = window.GetVisualDescendants().OfType<PanelView>().ToList();
            var firstView = views.Single(v => ReferenceEquals(v.DataContext, first));
            var secondView = views.Single(v => ReferenceEquals(v.DataContext, second));
            var target = Bounds(secondView, host);

            // Where each part of the second panel would put the first one.
            var right = window.DropAt(first, new Point(target.Right - 10, target.Center.Y));
            Assert.Equal((second, DockSide.Right, true), (right.Target, right.Side, right.Allowed));
            Assert.Equal("Panel 1 right of panel 2", right.Preview);
            var middle = window.DropAt(first, target.Center);
            Assert.Null(middle.Side);
            Assert.Equal("Swap places with panel 2", middle.Preview);
            Assert.Null(window.DropAt(first, Bounds(firstView, host).Center).Target); // onto itself: nothing

            // A real drag: press the number, move to the lower part of the other panel, release.
            var badge = firstView.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NumberBadge");
            var from = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window)!.Value;
            var to = secondView.TranslatePoint(new Point(secondView.Bounds.Width / 2, secondView.Bounds.Height - 12), window)!.Value;
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(new Point(from.X + 20, from.Y + 20));
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            await Task.Delay(50, ct);
            Assert.Equal($"rows({second.Id},{first.Id})", ws.LayoutTree.ToString());
            Assert.Same(first, ws.ActivePanel);
            Assert.Equal("Panel 2 is now below panel 1.", vm.Notification); // numbers follow the places

            // Esc during a drag leaves everything as it was.
            badge = window.GetVisualDescendants().OfType<PanelView>().Single(v => ReferenceEquals(v.DataContext, first))
                .GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NumberBadge");
            from = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window)!.Value;
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(new Point(from.X + 30, from.Y - 200));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.MouseUp(new Point(from.X + 30, from.Y - 200), MouseButton.Left);
            Assert.Equal($"rows({second.Id},{first.Id})", ws.LayoutTree.ToString());
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_drop_that_leaves_a_panel_too_small_is_refused_with_the_reason()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            window.Width = 900;
            window.Height = 420; // the least height: room for two panels stacked, not three
            var ws = vm.Workspace;
            var (top, bottom) = (ws.Panels[0], ws.Panels[1]);
            ws.Activate(bottom);
            vm.Execute(CommandIds.PanelMoveDown); // top above bottom
            vm.Execute(CommandIds.AddPanel); // a third beside the bottom one
            var third = ws.ActivePanel!;
            await Task.Delay(100, ct);
            var host = window.FindControl<Grid>("WorkspaceHost")!;
            var view = window.GetVisualDescendants().OfType<PanelView>().Single(v => ReferenceEquals(v.DataContext, bottom));
            var target = Bounds(view, host);
            var below = window.DropAt(third, new Point(target.Center.X, target.Bottom - 8));
            Assert.Equal(DockSide.Bottom, below.Side);
            Assert.False(below.Allowed, $"host {host.Bounds}");
            Assert.StartsWith("No room: every panel keeps at least 260 × 140 pixels", below.Preview);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public void The_arrangement_is_saved_and_P2_rows_load_as_one_stacked_split()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ws = vm.Workspace;
            ws.Activate(ws.Panels[0]);
            vm.Execute(CommandIds.AddPanelBelow);
            string shape = ws.LayoutTree.ToString();
            var state = ws.ToState(null);
            Assert.Equal(shape, state.Tree!.ToString());
            ws.LoadState(state);
            Assert.Equal(shape, ws.LayoutTree.ToString());

            // A P2 workspace: no tree, its panels in rows.
            state.Tree = null;
            state.Layout = "Rows";
            ws.LoadState(state);
            Assert.True(ws.LayoutTree is { IsPanel: false, Stacked: true, Children.Count: 3 });
            // A tree that does not match the panels falls back the same way.
            state.Tree = PanelLayoutNode.Split(false, [PanelLayoutNode.Panel("gone"), PanelLayoutNode.Panel("other")]);
            ws.LoadState(state);
            Assert.True(ws.LayoutTree.Stacked);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
