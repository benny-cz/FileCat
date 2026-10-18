using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>FCUX-031: focused splitters edit the saved tree under the same minimum policy as pointer resizing.</summary>
public sealed class PanelKeyboardResizeTests
{
    [AvaloniaFact]
    public async Task A_keyboard_boundary_can_be_adjusted_canceled_and_saved()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Width = 1100;
            window.Height = 800;
            window.CaptureRenderedFrame();
            var tree = vm.Workspace.LayoutTree;
            Assert.Equal(2, tree.Children.Count);
            Assert.NotNull(services.Commands.Get(CommandIds.ResizePanel));
            Assert.True(vm.GetAvailability(CommandIds.ResizePanel).Enabled);
            await vm.ExecuteAsync(CommandIds.ResizePanel);
            var splitter = Assert.Single(window.GetVisualDescendants().OfType<GridSplitter>(),
                s => s.Classes.Contains("panelResize"));
            Assert.True(splitter.IsFocused);
            Assert.Contains("Vertical panel boundary", AutomationProperties.GetName(splitter));
            Assert.Contains("Escape restores", AutomationProperties.GetHelpText(splitter));
            var resizeStrip = window.FindControl<Border>("ResizeStrip")!;
            Assert.True(resizeStrip.IsVisible);
            string initialName = AutomationProperties.GetName(splitter)!;
            string initialText = window.FindControl<TextBlock>("ResizeText")!.Text!;
            double original = tree.Children[0].Size;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.True(tree.Children[0].Size > original);
            Assert.NotEqual(initialName, AutomationProperties.GetName(splitter));
            Assert.NotEqual(initialText, window.FindControl<TextBlock>("ResizeText")!.Text);
            Assert.Contains("resizeActive", splitter.Classes);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(original, tree.Children[0].Size);
            Assert.DoesNotContain("resizeActive", splitter.Classes);
            Assert.False(resizeStrip.IsVisible);

            await vm.ExecuteAsync(CommandIds.ResizePanel);
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(tree.Children[0].Size > original);
            var saved = vm.Workspace.ToState(null);
            Assert.Equal(tree.Children[0].Size, saved.Tree!.Children[0].Size);
            vm.Workspace.LoadState(saved);
            Assert.Equal(saved.Tree.Children[0].Size, vm.Workspace.LayoutTree.Children[0].Size);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Nested_boundaries_expose_orientation_identity_and_a_keyboard_route_to_each()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Width = 1300;
            window.Height = 900;
            window.CaptureRenderedFrame();
            var first = vm.Workspace.Panels[0];
            vm.Workspace.Activate(first);
            await vm.ExecuteAsync(CommandIds.AddPanelBelow);
            window.CaptureRenderedFrame();
            Assert.Equal(3, vm.Workspace.Panels.Count);
            vm.Workspace.Activate(first);
            await vm.ExecuteAsync(CommandIds.ResizePanel);
            var row = window.GetVisualDescendants().OfType<GridSplitter>()
                .Single(s => s.Classes.Contains("resizeActive"));
            Assert.Contains("Horizontal panel boundary", AutomationProperties.GetName(row));
            var inner = vm.Workspace.LayoutTree.Children[0];
            double beforeRow = inner.Children[0].Size;
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.True(inner.Children[0].Size > beforeRow);

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var column = window.GetVisualDescendants().OfType<GridSplitter>()
                .Single(s => s.Classes.Contains("resizeActive"));
            Assert.NotSame(row, column);
            Assert.Contains("Vertical panel boundary", AutomationProperties.GetName(column));
            double beforeColumn = vm.Workspace.LayoutTree.Children[0].Size;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.True(vm.Workspace.LayoutTree.Children[0].Size > beforeColumn);
            Assert.True(inner.Children[0].Size > beforeRow);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Minimum_and_replaced_layouts_cannot_be_overridden_by_stale_resize_input()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Width = 740;
            window.Height = 620;
            window.CaptureRenderedFrame();
            await vm.ExecuteAsync(CommandIds.ResizePanel);
            for (int i = 0; i < 25; i++)
                window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
            window.CaptureRenderedFrame();
            var split = Assert.IsType<Grid>(Assert.Single(window.FindControl<Grid>("WorkspaceHost")!.Children));
            Assert.True(split.ColumnDefinitions[0].ActualWidth >= PanelLayout.PanelMinWidth - 0.5);
            Assert.True(split.ColumnDefinitions[2].ActualWidth >= PanelLayout.PanelMinWidth - 0.5);
            Assert.Contains("minimum", vm.Notification!, StringComparison.OrdinalIgnoreCase);

            vm.Workspace.EqualizeSizes(); // Rebuild retires the old splitter and its cancel snapshot.
            var replacement = vm.Workspace.LayoutTree;
            Assert.All(replacement.Children, c => Assert.Equal(1, c.Size));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Same(replacement, vm.Workspace.LayoutTree);
            Assert.All(replacement.Children, c => Assert.Equal(1, c.Size));

            var saved = vm.Workspace.ToState(null);
            await vm.ExecuteAsync(CommandIds.ResizePanel);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            vm.Workspace.LoadState(saved);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(saved.Tree!.Children[0].Size, vm.Workspace.LayoutTree.Children[0].Size);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Outside_resize_mode_file_list_arrows_and_commander_shortcuts_keep_their_routes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.CaptureRenderedFrame();
            var first = vm.Workspace.ActivePanel!;
            var tree = vm.Workspace.LayoutTree;
            window.FocusActivePanel();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.Same(tree, vm.Workspace.LayoutTree);
            Assert.All(tree.Children, c => Assert.Equal(1, c.Size));

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.NotSame(first, vm.Workspace.ActivePanel);
            window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
            Assert.NotNull(vm.Workspace.MaximizedPanel);
            window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
            Assert.Null(vm.Workspace.MaximizedPanel);
            window.KeyPress(Key.Left, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
            Assert.Same(vm.Workspace.Panels[0], vm.Workspace.ActivePanel);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
