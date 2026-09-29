using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>Choosing where F5 and F6 go with three or more panels (plan §4.2): the role chips and the copy dialog.</summary>
public sealed class TargetPanelTests
{
    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 250 && !condition(); i++) await Task.Delay(20, ct);
    }

    private static Button RoleButton(Window window, ViewModels.PanelViewModel panel) =>
        window.GetVisualDescendants().OfType<PanelView>().Single(v => ReferenceEquals(v.DataContext, panel)).FindControl<Button>("RoleButton")!;

    [AvaloniaFact]
    public async Task Set_as_target_on_another_panel_changes_the_target_and_keeps_the_keyboard_where_it_is()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var ws = vm.Workspace;
            var (first, second) = (ws.Panels[0], ws.Panels[1]);
            ws.Activate(first);
            // Two panels: the other one is the target, and says so.
            Assert.Equal("TARGET", second.RoleLabel);
            Assert.Equal("", first.RoleLabel);

            vm.Execute(CommandIds.AddPanel);
            var third = ws.ActivePanel!;
            ws.Activate(first);
            await Task.Delay(50, ct);
            Assert.Same(second, ws.ActiveTarget);
            Assert.Equal($"→ {second.Number} ▾", first.RoleLabel); // the new panel sits between them, so the numbers moved
            Assert.Equal("TARGET", second.RoleLabel);
            Assert.Equal("Set as target", third.RoleLabel);
            Assert.True(third.OffersTarget);
            Assert.True(RoleButton(window, third).IsEffectivelyVisible);

            RoleButton(window, third).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(first, ws.ActivePanel);
            Assert.Same(third, ws.ActiveTarget);
            Assert.Equal("TARGET", third.RoleLabel);
            Assert.Equal("Set as target", second.RoleLabel);
            Assert.Equal($"→ {third.Number} ▾", first.RoleLabel);
            Assert.Contains($"now copies and moves to panel {third.Number}", vm.Notification);

            // The F5 dialog offers every other panel's folder; a click puts it in the destination box.
            string other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
            second.ActiveTab!.Navigate(Location.FileSystem(other));
            var listing = first.ActiveTab!.Listing;
            await WaitFor(() => listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3, ct);
            listing.SetFocus(1);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            vm.Execute(CommandIds.Copy);
            await WaitFor(() => dialogs.IsOpen, ct);
            await Task.Delay(50, ct);
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(b => AutomationProperties.GetName(b)?.StartsWith("To panel ", StringComparison.Ordinal) == true).ToList();
            Assert.Equal(2, buttons.Count);
            var destination = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Destination");
            Assert.StartsWith(third.ActiveTab!.Location!.Path, destination.Text);
            buttons.Single(b => AutomationProperties.GetName(b)!.StartsWith($"To panel {second.Number}:", StringComparison.Ordinal))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(other + Path.DirectorySeparatorChar, destination.Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == $"To (panel {second.Number}):");
            window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
            await WaitFor(() => !dialogs.IsOpen, ct);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public void Closing_the_target_leaves_the_choice_to_the_user_and_says_so()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ws = vm.Workspace;
            var first = ws.Panels[0];
            ws.Activate(first);
            vm.Execute(CommandIds.AddPanel);
            vm.Execute(CommandIds.AddPanel);
            var fourth = ws.ActivePanel!;
            Assert.Equal(4, ws.Panels.Count);
            ws.Activate(first);
            vm.SetPanelTarget(first, fourth);
            Assert.Same(fourth, ws.ActiveTarget);
            ws.RemovePanel(fourth);
            Assert.Equal(3, ws.Panels.Count);
            // D-09: no guess; the chip says there is no target, and the other panels offer themselves.
            Assert.Null(ws.ActiveTarget);
            Assert.Equal("no target ▾", first.RoleLabel);
            Assert.All(ws.Panels.Where(p => !ReferenceEquals(p, first)), p => Assert.Equal("Set as target", p.RoleLabel));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
