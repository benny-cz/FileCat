using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class SettingsDialogTests
{
    [AvaloniaFact]
    public async Task Privacy_setting_names_only_automatic_release_checks_and_explains_requested_connections()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.CheckForUpdates = false;
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(dialogs.IsOpen);
            window.UpdateLayout();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == "Privacy");
            window.UpdateLayout();
            var updates = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => c.Content as string == "Automatically check for updates");
            Assert.False(updates.IsChecked);
            var note = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text?.StartsWith("At most once a day at startup", StringComparison.Ordinal) == true);
            Assert.Contains("does not download or install update packages", note.Text);
            Assert.Contains("Help → Check for updates", note.Text);
            Assert.Contains("network locations still connect", note.Text);
            Assert.DoesNotContain("no network requests", note.Text, StringComparison.OrdinalIgnoreCase);
            updates.IsChecked = true;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(services.Settings.CheckForUpdates);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>Settings names the default verification as the copy dialog does, and stores what was chosen.</summary>
    [AvaloniaFact]
    public async Task Default_copy_verification_is_chosen_by_the_names_the_copy_dialog_uses()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            window.UpdateLayout(); // Realize the modal's scrollable body before querying its tabs.
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == "Behavior");
            window.UpdateLayout(); // Selecting a tab attaches a new visual subtree.
            ComboBox? Verify() => window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => AutomationProperties.GetName(c) == "Default copy verification");
            for (int i = 0; i < 100 && Verify() is null; i++) await Task.Delay(20, ct);
            Assert.Equal(["Size and metadata (fast)", "Read back and compare content"], Verify()!.Items.OfType<string>());
            Assert.Equal("Size and metadata (fast)", Verify()!.SelectedItem);
            Verify()!.SelectedIndex = 1;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.Equal("ReadBack", services.Settings.DefaultVerify);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_settings_explain_Find_context_overlaps_and_reject_same_context_conflicts()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            vm.Execute(CommandIds.Settings);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            window.UpdateLayout();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == "Keyboard");
            window.UpdateLayout();
            var bindings = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.PlaceholderText == "command.id = Ctrl+Shift+X, F12");
            TextBlock Preview() => window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => AutomationProperties.GetName(t) == "Find shortcut context preview");
            Assert.Contains("Ctrl+W", Preview().Text);
            Assert.Contains("Find", Preview().Text);
            Assert.Contains("file panels", Preview().Text);

            bindings.Text = $"{CommandIds.FindIntersect} = Ctrl+S";
            for (int i = 0; i < 100 && !(Preview().Text?.Contains("Shortcut conflicts in one context") ?? false); i++)
                await Task.Delay(20, ct);
            Assert.Contains("Shortcut conflicts in one context", Preview().Text);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialogs.IsOpen);
            Assert.False(services.Settings.KeyBindings.ContainsKey(CommandIds.FindIntersect));

            bindings.Text = $"{CommandIds.FindSubtract} = Ctrl+Shift+S";
            for (int i = 0; i < 100 && (Preview().Text?.Contains("Shortcut conflicts") ?? false); i++)
                await Task.Delay(20, ct);
            Assert.DoesNotContain("Shortcut conflicts", Preview().Text);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
