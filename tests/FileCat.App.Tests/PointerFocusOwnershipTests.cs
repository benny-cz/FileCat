using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.App.Services;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class PointerFocusOwnershipTests
{
    [AvaloniaFact]
    public void A_panel_tab_switch_rejects_a_late_accessibility_request_for_the_old_filter()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var first = vm.Workspace.Panels[0];
            var second = vm.Workspace.Panels[1];
            var firstView = window.GetVisualDescendants().OfType<PanelView>()
                .Single(view => ReferenceEquals(view.DataContext, first));
            var oldFilter = firstView.FindControl<TextBox>("FilterBox")!;
            var oldPath = firstView.FindControl<TextBox>("PathBox")!;
            window.CaptureRenderedFrame();
            vm.Workspace.Activate(first);
            vm.View.FocusActivePanel();
            Assert.True(window.ActiveList!.IsFocused);
            Assert.Equal(CommandIds.SwitchPanel, services.Keymap.Resolve(
                KeyMapper.ToChord(Key.Tab, KeyModifiers.None)!.Value, CommandContext.Panel));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Same(second, vm.Workspace.ActivePanel);
            Assert.NotNull(window.ActiveList);
            Assert.True(window.ShouldRejectStalePanelAccessibilityFocus(oldFilter));
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Same(second, vm.Workspace.ActivePanel);
            Assert.True(window.ShouldRejectStalePanelAccessibilityFocus(oldFilter));

            ControlAutomationPeer.CreatePeerForElement(oldFilter).SetFocus();
            ControlAutomationPeer.CreatePeerForElement(oldPath).SetFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, vm.Workspace.ActivePanel);
            Assert.False(oldFilter.IsFocused);
            Assert.False(oldPath.IsFocused);

            // A fresh physical key clears the guard so explicit AX navigation
            // can immediately choose the other panel again.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            ControlAutomationPeer.CreatePeerForElement(oldFilter).SetFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, vm.Workspace.ActivePanel);
            Assert.True(oldFilter.IsFocused);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void A_panel_click_rejects_a_late_accessibility_request_for_the_previous_filter()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var first = vm.Workspace.Panels[0];
            var second = vm.Workspace.Panels[1];
            var views = window.GetVisualDescendants().OfType<PanelView>().ToArray();
            var firstView = views.Single(view => ReferenceEquals(view.DataContext, first));
            var secondView = views.Single(view => ReferenceEquals(view.DataContext, second));
            var filter = secondView.FindControl<TextBox>("FilterBox")!;
            var path = secondView.FindControl<TextBox>("PathBox")!;
            window.CaptureRenderedFrame();
            var tab = firstView.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Tag, first.ActiveTab));
            var otherTab = secondView.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Tag, second.ActiveTab));
            Point tabCenter = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
            Point otherTabCenter = otherTab.TranslatePoint(new Point(otherTab.Bounds.Width / 2, otherTab.Bounds.Height / 2), window)!.Value;

            filter.Focus();
            Assert.Same(second, vm.Workspace.ActivePanel);
            var observed = new List<object?>();
            vm.Workspace.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.Workspace.ActivePanel)) observed.Add(vm.Workspace.ActivePanel);
            };
            window.MouseDown(tabCenter, MouseButton.Left);
            window.MouseUp(tabCenter, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, vm.Workspace.ActivePanel);

            // The stale AX callback is simulated after the pointer event has
            // dispatched. Wall-clock sleep here made the 250 ms ownership
            // window expire under heavily loaded macOS CI runners.
            ControlAutomationPeer.CreatePeerForElement(filter).SetFocus();
            ControlAutomationPeer.CreatePeerForElement(path).SetFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, vm.Workspace.ActivePanel);
            Assert.DoesNotContain(second, observed);
            Assert.False(filter.IsFocused);
            Assert.False(path.IsFocused);

            // A new physical press is a newer intent, so the other panel must
            // be immediately usable rather than being trapped by the guard.
            window.MouseDown(otherTabCenter, MouseButton.Left);
            window.MouseUp(otherTabCenter, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, vm.Workspace.ActivePanel);
            ControlAutomationPeer.CreatePeerForElement(filter).SetFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(filter.IsFocused);

            window.MouseDown(tabCenter, MouseButton.Left);
            window.MouseUp(tabCenter, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(first, vm.Workspace.ActivePanel);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            ControlAutomationPeer.CreatePeerForElement(filter).SetFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(second, vm.Workspace.ActivePanel);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
