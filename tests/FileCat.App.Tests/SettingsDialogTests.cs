using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class SettingsDialogTests
{
    [AvaloniaFact]
    public async Task Clean_settings_escape_closes_without_discard_review_after_navigation_or_reverted_edit()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1100, height: 760);
        try
        {
            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            var tabs = Assert.Single(card.GetVisualDescendants().OfType<TabControl>());
            tabs.SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var search = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Search settings");
            search.Text = "privacy";
            var updates = card.GetVisualDescendants().OfType<CheckBox>()
                .Single(check => Equals(check.Content, "Automatically check for updates"));
            bool? original = updates.IsChecked;
            updates.IsChecked = !original;
            updates.IsChecked = original;

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(window.GetVisualDescendants().OfType<DialogCard>());
            Assert.Equal(original, services.Settings.CheckForUpdates);

            showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            card.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "DialogCloseButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(window.GetVisualDescendants().OfType<DialogCard>());
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Settings_search_only_offers_reachable_editor_fields_and_new_profile_is_ready_to_rename()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1100, height: 760);
        try
        {
            var showing = SettingsDialog.ShowAsync(vm);
            Layout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            var search = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Search settings");
            var results = card.GetVisualDescendants().OfType<ListBox>()
                .Single(list => AutomationProperties.GetName(list) == "Matching settings");
            var tabs = Assert.Single(card.GetVisualDescendants().OfType<TabControl>());

            search.Text = "Privacy";
            Layout();
            Assert.Contains("Open Privacy tab", results.Items.OfType<string>());
            card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Go to setting"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            Assert.Equal(5, tabs.SelectedIndex);

            search.Text = "One filter per line";
            Layout();
            Assert.Equal("No settings match.", card.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Text == "No settings match.").Text);
            Assert.Empty(results.Items);

            tabs.SelectedIndex = 1;
            Layout();
            card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Add filter"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var filterName = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Selected filter name");
            Assert.True(filterName.IsFocused);
            Assert.Equal("new-filter", filterName.SelectedText);
            var advanced = card.GetVisualDescendants().OfType<CheckBox>()
                .Single(check => Equals(check.Content, "Edit filter definitions as text"));
            advanced.IsChecked = true;
            Layout();
            Assert.Contains(results.Items.OfType<string>(), item => item.Contains("One filter per line"));
            advanced.IsChecked = false;
            Layout();
            Assert.Empty(results.Items);

            tabs.SelectedIndex = 3;
            Layout();
            card.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "New profile")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            var name = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Profile name");
            Assert.True(name.IsFocused);
            Assert.Equal("New profile", name.SelectedText);
            card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { AccessibilityTests.Close(services, window, root); }

        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public async Task Settings_tab_change_starts_at_the_new_pages_top_after_scrolling()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1100, height: 500);
        try
        {
            var showing = SettingsDialog.ShowAsync(vm);
            Layout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            var tabs = Assert.Single(card.GetVisualDescendants().OfType<TabControl>());
            var scroll = card.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(view => view.Name == "DialogBodyScroll");
            tabs.SelectedIndex = 2;
            Layout();
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 100);
            scroll.Offset = new Vector(0, 150);
            Layout();
            Assert.True(scroll.Offset.Y > 100);
            var shell = card.GetVisualDescendants().OfType<ComboBox>()
                .Single(box => AutomationProperties.GetName(box) == "Command line shell");
            shell.SelectedIndex = shell.SelectedIndex == 0 ? 1 : 0;
            Layout();
            Assert.True(scroll.Offset.Y > 100);
            tabs.SelectedIndex = 0;
            Layout();
            Assert.InRange(scroll.Offset.Y, 0, 1);
            var search = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Search settings");
            search.Text = "Alt+F3 always opens";
            Layout();
            card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Go to setting"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            Assert.Equal(2, tabs.SelectedIndex);
            Assert.True(scroll.Offset.Y > 100);
            card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { AccessibilityTests.Close(services, window, root); }

        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public async Task Settings_remains_anchored_and_requires_an_explicit_decision_to_discard_a_draft()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1100, height: 760);
        try
        {
            var showing = SettingsDialog.ShowAsync(vm);
            Layout();
            var card = Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());
            Point origin = card.TranslatePoint(default, window)!.Value;
            var tabs = Assert.Single(card.GetVisualDescendants().OfType<TabControl>());
            foreach (var index in new[] { 2, 0, 4, 2 })
            {
                tabs.SelectedIndex = index;
                Layout();
                Point position = card.TranslatePoint(default, window)!.Value;
                Assert.InRange(Math.Abs(position.X - origin.X), 0, 1);
                Assert.InRange(Math.Abs(position.Y - origin.Y), 0, 1);
            }
            Assert.Contains("Choose Preview invocation", card.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(text => AutomationProperties.GetName(text) == "Command invocation preview").Text);
            Assert.Contains("Choose Preview invocation", card.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(text => AutomationProperties.GetName(text) == "Association invocation preview").Text);

            // A backdrop press must keep Settings open even before any draft edit.
            window.MouseMove(new Point(5, 5));
            window.MouseDown(new Point(5, 5), MouseButton.Left);
            window.MouseUp(new Point(5, 5), MouseButton.Left);
            Assert.False(showing.IsCompleted);
            Assert.Single(window.GetVisualDescendants().OfType<DialogCard>());

            var add = card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add command"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var name = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Selected command or menu name");
            Assert.Equal(string.Empty, name.Text);
            Assert.Equal("New command", name.PlaceholderText);
            Assert.True(name.IsFocused);
            Assert.DoesNotContain(services.Settings.UserCommands, command => string.IsNullOrWhiteSpace(command.Name));
            card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add association"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var associationName = card.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Association name");
            Assert.Equal(string.Empty, associationName.Text);
            Assert.Equal("New association", associationName.PlaceholderText);
            Assert.True(associationName.IsFocused);
            window.MouseDown(new Point(5, 5), MouseButton.Left);
            window.MouseUp(new Point(5, 5), MouseButton.Left);
            Assert.False(showing.IsCompleted);
            card.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "OK"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            Assert.False(showing.IsCompleted);
            var validation = card.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text?.StartsWith("User commands:", StringComparison.Ordinal) == true);
            Assert.True(validation.IsVisible);
            var pinned = validation.GetVisualAncestors().OfType<Border>().First();
            Assert.Equal(2, Grid.GetRow(pinned));
            Assert.True(pinned.IsVisible);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Layout();
            Assert.False(showing.IsCompleted);
            Assert.Equal(2, window.GetVisualDescendants().OfType<DialogCard>().Count());
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Discard draft"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(services.Settings.UserCommands);
        }
        finally { AccessibilityTests.Close(services, window, root); }

        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

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
            Assert.Equal(note.Text, AutomationProperties.GetHelpText(updates));
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
