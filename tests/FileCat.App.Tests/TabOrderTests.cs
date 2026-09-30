using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>A panel's tabs change their order by keyboard and by dragging, and move to another panel by dragging.</summary>
public sealed class TabOrderTests
{
    private static string[] Order(PanelViewModel panel) => [.. panel.Tabs.Select(t => t.Title)];

    private static Button TabButton(Window window, TabViewModel tab) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tab") && ReferenceEquals(b.Tag, tab));

    /// <summary>A point on a tab's button, at <paramref name="fraction"/> of its width, in the window's coordinates.</summary>
    private static Point On(Window window, TabViewModel tab, double fraction)
    {
        var button = TabButton(window, tab);
        return button.TranslatePoint(new Point(button.Bounds.Width * fraction, button.Bounds.Height / 2), window)!.Value;
    }

    private static void Drag(Window window, Point from, Point to, bool cancel = false)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 12, from.Y));
        window.MouseMove(to);
        if (cancel) window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public async Task Tabs_move_by_keyboard_by_dragging_and_to_another_panel()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var left = vm.Workspace.Panels[0];
            var right = vm.Workspace.Panels[1];
            foreach (var name in new[] { "alpha", "beta", "gamma" }) Directory.CreateDirectory(Path.Combine(root, name));
            left.ActiveTab!.Navigate(Location.FileSystem(Path.Combine(root, "alpha")));
            left.OpenTab(Location.FileSystem(Path.Combine(root, "beta")));
            left.OpenTab(Location.FileSystem(Path.Combine(root, "gamma")));
            vm.Workspace.Activate(left);
            vm.View.FocusActivePanel();
            for (int i = 0; i < 250 && !Order(left).SequenceEqual(["alpha", "beta", "gamma"]); i++) await Task.Delay(20, ct);
            Assert.Equal(["alpha", "beta", "gamma"], Order(left));
            await Task.Delay(100, ct);

            // Ctrl+Shift+PageUp and PageDown move the active tab, as browsers do; it stays active.
            window.KeyPress(Key.PageUp, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.PageUp, null);
            Assert.Equal(["alpha", "gamma", "beta"], Order(left));
            window.KeyPress(Key.PageUp, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.PageUp, null);
            window.KeyPress(Key.PageUp, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.PageUp, null);
            Assert.Equal(["gamma", "alpha", "beta"], Order(left));
            window.KeyPress(Key.PageDown, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.PageDown, null);
            Assert.Equal(["alpha", "gamma", "beta"], Order(left));
            Assert.Equal("gamma", left.ActiveTab!.Title);
            // The order is what the workspace keeps.
            Assert.Equal(["alpha", "gamma", "beta"], left.Tabs.Select(t => Path.GetFileName(t.ToState().Location!.Path.TrimEnd('\\', '/'))));
            await Task.Delay(50, ct);

            // Dragged along the strip: alpha, dropped on beta's right half, goes after it and is the active tab.
            var alpha = left.Tabs[0];
            Drag(window, On(window, alpha, 0.5), On(window, left.Tabs[2], 0.8));
            Assert.Equal(["gamma", "beta", "alpha"], Order(left));
            Assert.Same(alpha, left.ActiveTab);
            await Task.Delay(50, ct);

            // Esc during a drag leaves the tabs as they were.
            Drag(window, On(window, left.Tabs[0], 0.5), On(window, left.Tabs[2], 0.8), cancel: true);
            Assert.Equal(["gamma", "beta", "alpha"], Order(left));
            await Task.Delay(50, ct);

            // Dropped on another panel's strip: it moves there, before the tab it was dropped on the left half of.
            var beta = left.Tabs[1];
            var rightFirst = right.Tabs[0];
            Drag(window, On(window, beta, 0.5), On(window, rightFirst, 0.2));
            Assert.Equal(["gamma", "alpha"], Order(left));
            Assert.Same(beta, right.Tabs[0]);
            Assert.Same(right, beta.Panel);
            Assert.Same(right, vm.Workspace.ActivePanel);
            Assert.Same(beta, right.ActiveTab);
            await Task.Delay(50, ct);

            // A panel keeps a tab: its last one moved away leaves a fresh one at the same place behind.
            vm.Workspace.MoveTabToPanel(right.Tabs[1], left, 0);
            vm.Workspace.MoveTabToPanel(beta, left);
            Assert.Single(right.Tabs);
            Assert.Equal("beta", right.Tabs[0].Title);
            Assert.NotSame(beta, right.Tabs[0]);
            Assert.Same(beta, left.Tabs[^1]);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
