using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>FCUX-030: panel admission, warning and compact address geometry use the actual workspace host.</summary>
public sealed class PanelViewportLayoutTests
{
    [AvaloniaFact]
    public async Task Panel_admission_uses_the_measured_host_as_chrome_appears_or_disappears()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Width = 640;
            window.Height = 420;
            window.CaptureRenderedFrame();
            var host = window.FindControl<Grid>("WorkspaceHost")!;
            var panel = vm.Workspace.ActivePanel!;
            Assert.Equal(host.Bounds.Size, window.WorkspaceViewport);
            AssertAvailability();

            double initialHeight = host.Bounds.Height;
            vm.ShowToolbar = false;
            vm.ShowCommandLine = false;
            vm.ShowKeyBar = false;
            window.CaptureRenderedFrame();
            Assert.True(host.Bounds.Height > initialHeight);
            AssertAvailability();

            int before = vm.Workspace.Panels.Count;
            bool admitted = vm.GetAvailability(CommandIds.AddPanelBelow).Enabled;
            await vm.ExecuteAsync(CommandIds.AddPanelBelow);
            Assert.Equal(before + (admitted ? 1 : 0), vm.Workspace.Panels.Count);
            window.CaptureRenderedFrame();
            var strip = window.FindControl<Border>("MaximizedStrip")!;
            if (admitted) Assert.False(strip.IsVisible);
            else Assert.Contains("no room", vm.Notification!, StringComparison.OrdinalIgnoreCase);

            void AssertAvailability()
            {
                var viewport = window.WorkspaceViewport!.Value;
                Assert.Equal(PanelLayout.Fits(PanelLayout.Insert(vm.Workspace.LayoutTree, "new panel", panel.Id, DockSide.Right),
                    viewport.Width, viewport.Height), vm.GetAvailability(CommandIds.AddPanel).Enabled);
                Assert.Equal(PanelLayout.Fits(PanelLayout.Insert(vm.Workspace.LayoutTree, "new panel", panel.Id, DockSide.Bottom),
                    viewport.Width, viewport.Height), vm.GetAvailability(CommandIds.AddPanelBelow).Enabled);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Narrow_panels_keep_location_filter_and_target_separate_and_usable()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Width = 640;
            window.Height = 420;
            window.CaptureRenderedFrame();
            var panel = vm.Workspace.Panels.First(p => p.RoleLabel.Length > 0);
            var view = window.GetVisualDescendants().OfType<PanelView>().Single(v => ReferenceEquals(v.DataContext, panel));
            var address = view.FindControl<Grid>("AddressRow")!;
            var path = view.FindControl<Panel>("PathCell")!;
            var pathBox = view.FindControl<TextBox>("PathBox")!;
            var filter = view.FindControl<TextBox>("FilterBox")!;
            var role = view.FindControl<Button>("RoleButton")!;
            var free = view.FindControl<TextBlock>("FreeSpaceLabel")!;
            Assert.True(address.Bounds.Width < 480);
            Assert.Equal(3, Grid.GetColumnSpan(path));
            Assert.Equal(1, Grid.GetRow(filter));
            Assert.Equal(1, Grid.GetRow(free));
            Assert.True(pathBox.Bounds.Width >= 120, $"Location width: {pathBox.Bounds.Width:0.0}");
            Assert.True(path.Bounds.Right <= role.Bounds.Left + 1);
            Assert.Equal(panel.RoleTip, AutomationProperties.GetName(role));
            Assert.True(filter.IsEffectivelyVisible && role.IsEffectivelyVisible);
            Assert.True(pathBox.Focus());
            Assert.True(pathBox.IsFocused);

            window.Width = 1280;
            window.CaptureRenderedFrame();
            Assert.True(address.Bounds.Width >= 480);
            Assert.Equal(1, Grid.GetColumnSpan(path));
            Assert.Equal(0, Grid.GetRow(filter));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Both_panels_show_the_default_filter_mask_inside_their_address_rows_after_resize()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            foreach (var width in new[] { 1200, 800, 640, 1600, 800 })
            {
                window.Width = width;
                window.CaptureRenderedFrame()?.Dispose();
                foreach (var view in window.GetVisualDescendants().OfType<PanelView>())
                {
                    var address = view.FindControl<Grid>("AddressRow")!;
                    var filter = view.FindControl<TextBox>("FilterBox")!;
                    filter.ApplyTemplate();
                    var origin = filter.TransformToVisual(address)!.Value.Transform(new Point());
                    Assert.Equal(PanelView.AllItemsMask, filter.Text);
                    Assert.DoesNotContain("linked", filter.Classes);
                    Assert.True(Assert.Single(filter.GetVisualDescendants().OfType<TextPresenter>()).Opacity > 0.5);
                    Assert.True(filter.IsEffectivelyVisible && filter.Bounds.Width >= 72 &&
                        origin.X >= -1 && origin.X + filter.Bounds.Width <= address.Bounds.Width + 1,
                        $"At window width {width}, filter mask bounds are {origin.X:0.0}..{origin.X + filter.Bounds.Width:0.0} " +
                        $"inside address width {address.Bounds.Width:0.0}.");
                }
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Four_panel_workspace_keeps_a_readable_default_mask_next_to_free_space_and_target()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 2048, height: 870);
        try
        {
            vm.Workspace.AddPanel(side: DockSide.Bottom);
            vm.Workspace.AddPanel(side: DockSide.Bottom);
            window.CaptureRenderedFrame()?.Dispose();
            Assert.Equal(4, vm.Workspace.Panels.Count);
            var views = window.GetVisualDescendants().OfType<PanelView>().ToArray();
            Assert.Equal(4, views.Length);
            foreach (var view in views)
            {
                var address = view.FindControl<Grid>("AddressRow")!;
                var path = view.FindControl<TextBox>("PathBox")!;
                var filter = view.FindControl<TextBox>("FilterBox")!;
                path.ApplyTemplate();
                filter.ApplyTemplate();
                var origin = filter.TransformToVisual(address)!.Value.Transform(new Point());
                Assert.Single(path.GetVisualDescendants().OfType<TextPresenter>());
                Assert.Equal(PanelView.AllItemsMask, filter.Text);
                Assert.DoesNotContain("linked", filter.Classes);
                Assert.True(Assert.Single(filter.GetVisualDescendants().OfType<TextPresenter>()).Opacity > 0.5);
                Assert.True(filter.IsEffectivelyVisible && filter.Bounds.Width >= 72 &&
                    origin.X >= -1 && origin.X + filter.Bounds.Width <= address.Bounds.Width + 1,
                    $"Panel {(view.DataContext as FileCat.App.ViewModels.PanelViewModel)?.Number}: filter bounds {origin.X:0.0}..{origin.X + filter.Bounds.Width:0.0} " +
                    $"inside address width {address.Bounds.Width:0.0}.");
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_restored_cramped_tree_has_an_explicit_reversible_F11_state()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            window.Width = 640;
            window.Height = 420;
            window.CaptureRenderedFrame();
            var host = window.FindControl<Grid>("WorkspaceHost")!;
            while (vm.Workspace.Panels.Count < 4 && PanelLayout.Fits(vm.Workspace.LayoutTree, host.Bounds.Width, host.Bounds.Height))
                vm.Workspace.AddPanel(side: DockSide.Bottom);
            window.CaptureRenderedFrame();
            Assert.False(PanelLayout.Fits(vm.Workspace.LayoutTree, host.Bounds.Width, host.Bounds.Height));
            var strip = window.FindControl<Border>("MaximizedStrip")!;
            Assert.True(strip.IsVisible);
            Assert.Contains("F11", window.FindControl<TextBlock>("MaximizedText")!.Text);

            await vm.ExecuteAsync(CommandIds.MaximizePanel);
            Assert.NotNull(vm.Workspace.MaximizedPanel);
            Assert.Contains("F11 restores", window.FindControl<TextBlock>("MaximizedText")!.Text);
            await vm.ExecuteAsync(CommandIds.MaximizePanel);
            Assert.Null(vm.Workspace.MaximizedPanel);
            Assert.True(strip.IsVisible);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void A_panel_at_the_declared_minimum_keeps_a_usable_list_viewport()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        Window? probe = null;
        try
        {
            var view = new PanelView { DataContext = vm.Workspace.Panels[0] };
            probe = new Window
            {
                Width = PanelLayout.PanelMinWidth,
                Height = PanelLayout.PanelMinHeight,
                DataContext = vm,
                Content = view,
            };
            probe.Show();
            probe.CaptureRenderedFrame();
            Assert.True(view.List.VisibleRowCapacity >= 2,
                $"At {PanelLayout.PanelMinWidth} × {PanelLayout.PanelMinHeight}, list height is {view.List.Bounds.Height:0.0} and capacity {view.List.VisibleRowCapacity}.");
        }
        finally
        {
            probe?.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
