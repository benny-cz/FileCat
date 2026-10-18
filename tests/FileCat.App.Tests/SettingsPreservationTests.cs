using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.State;
using FileCat.Core.FileSystem;
using FileCat.Core.Tools;

namespace FileCat.App.Tests;

public sealed class SettingsPreservationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_edit_and_theme_only_saves_preserve_all_tool_values_and_real_launch_arguments(bool changeTheme)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            string expected = ToolsSnapshot(services.Settings);
            var before = ToolLauncher.Plan(services.Settings.Editor!, new ToolContext([Path.Combine(root, "files", "a.txt")], root), root, out _);
            for (int i = 0; i < 2; i++)
            {
                var showing = SettingsDialog.ShowAsync(vm);
                Dispatcher.UIThread.RunJobs();
                if (changeTheme)
                {
                    var theme = Field<ComboBox>(window, "Theme");
                    theme.SelectedIndex = ThemeManager.Names.ToList().IndexOf(i == 0 ? "ClassicDark" : "Classic");
                }
                Click(window, "OK");
                await showing.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(expected, ToolsSnapshot(services.Settings));
                var after = ToolLauncher.Plan(services.Settings.Editor!, new ToolContext([Path.Combine(root, "files", "a.txt")], root), root, out _);
                Assert.Equal(before.Single().Executable, after.Single().Executable);
                Assert.Equal(before.Single().Arguments, after.Single().Arguments);
                var saved = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(services.Paths.SettingsFile))!;
                Assert.Equal(expected, ToolsSnapshot(saved));
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Editing_arguments_preserves_other_editor_fields_and_never_splits_quoted_values()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            var expected = JsonSerializer.Deserialize<ToolDefinition>(JsonSerializer.Serialize(services.Settings.Editor))!;
            expected.Arguments = ["--profile", "C:\\My Editor Profile", "", "literal\"quote", "{files}"];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            Field<TextBox>(window, "Editor arguments").Text = "--profile \"C:\\My Editor Profile\" \"\" \"literal\"\"quote\" {files}";
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(services.Settings.Editor));
            var invocation = ToolLauncher.Plan(services.Settings.Editor!, new ToolContext([Path.Combine(root, "files", "a.txt")], root), root, out _).Single();
            Assert.Equal(new[] { "--profile", "C:\\My Editor Profile", "", "literal\"quote", Path.Combine(root, "files", "a.txt") }, invocation.Arguments);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Editor_preview_shows_resolved_argument_boundaries_and_token_insertion_stays_in_the_draft()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            string before = ToolsSnapshot(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var sample = Field<TextBox>(window, "Preview file (sample only)");
            sample.Text = Path.Combine(root, "files", "a b.txt");
            var preview = Field<SelectableTextBlock>(window, "Editor invocation preview");
            var previewButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Preview invocation"));
            Assert.Same(window, TopLevel.GetTopLevel(previewButton));
            previewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Checking invocation", preview.Text);
            for (int i = 0; i < 100 && !preview.Text!.Contains("Program:", StringComparison.Ordinal); i++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Contains("Arguments (5):", preview.Text);
            Assert.Contains("3: \"\"", preview.Text);
            Assert.Contains(JsonSerializer.Serialize(sample.Text), preview.Text);

            var shellMode = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(b => Equals(b.Content, "Allow shell interpretation for batch programs"));
            shellMode.IsChecked = false;
            Assert.Contains("out of date", preview.Text);

            var arguments = Field<TextBox>(window, "Editor arguments");
            arguments.Text = "--profile \"two words\" \"\"";
            Field<ComboBox>(window, "Argument token to add").SelectedItem = "{file}";
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add token"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTextGrammar.TryParseArguments(arguments.Text, out var tokens, out _));
            Assert.Equal(["--profile", "two words", "", "{file}"], tokens);
            Assert.Contains("out of date", preview.Text);

            arguments.Text = "\"unterminated";
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add token"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("\"unterminated", arguments.Text);
            Assert.Contains("Editor arguments:", preview.Text);
            Assert.NotNull(AutomationProperties.GetHelpText(arguments));

            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, ToolsSnapshot(services.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Editor_shell_interpretation_is_an_explicit_draft_choice_that_preserves_other_fields()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            var before = JsonSerializer.Deserialize<ToolDefinition>(JsonSerializer.Serialize(services.Settings.Editor))!;
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var shellMode = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(b => Equals(b.Content, "Allow shell interpretation for batch programs"));
            Assert.True(shellMode.IsChecked);
            shellMode.IsChecked = false;
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            before.ShellMode = false;
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(services.Settings.Editor));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Saved_filter_editor_adds_exclusion_masks_and_reorders_typed_drafts()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.SavedFilters = [new() { Name = "images", Mask = "*.png" }, new() { Name = "code", Mask = "*.cs" }];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Behavior");
            Assert.Equal(2, Field<ListBox>(window, "Saved filter list").ItemCount);
            Assert.False(BodyButton(window, "Move filter up").IsEnabled);
            Assert.True(BodyButton(window, "Move filter down").IsEnabled);
            BodyButton(window, "Add filter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(BodyButton(window, "Move filter down").IsEnabled);
            Field<TextBox>(window, "Selected filter name").Text = "sources";
            Field<TextBox>(window, "Selected filter mask").Text = "*.cs|*.g.cs";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("sources", Field<ListBox>(window, "Saved filter list").ItemsSource!.Cast<string>().Last());
            BodyButton(window, "Move filter up").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, Field<ListBox>(window, "Saved filter list").SelectedIndex);
            BodyButton(window, "Duplicate filter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, Field<ListBox>(window, "Saved filter list").ItemCount);
            Assert.Contains("sources-copy", Field<ListBox>(window, "Saved filter list").ItemsSource!.Cast<string>().ElementAt(2));
            BodyButton(window, "Remove filter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, Field<ListBox>(window, "Saved filter list").ItemCount);
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["images", "sources", "code"], services.Settings.SavedFilters.Select(f => f.Name));
            Assert.Equal("*.cs|*.g.cs", services.Settings.SavedFilters[1].Mask);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Saved_filter_raw_text_reports_errors_and_cancel_discards_a_valid_round_trip()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.SavedFilters = [new() { Name = "code", Mask = "*.cs" }];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Behavior");
            var advanced = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Edit filter definitions as text"));
            advanced.IsChecked = true;
            var raw = Field<TextBox>(window, "Saved filters advanced text");
            raw.Text = "broken line";
            advanced.IsChecked = false;
            Assert.True(advanced.IsChecked);
            Click(window, "OK");
            Assert.False(showing.IsCompleted);
            Assert.Contains("Saved filters:", AutomationProperties.GetHelpText(raw));
            Assert.Equal("code", Assert.Single(services.Settings.SavedFilters).Name);

            raw.Text = "code = *.cs\nsource = *.cs|*.g.cs";
            advanced.IsChecked = false;
            Assert.False(advanced.IsChecked);
            Assert.Equal(2, Field<ListBox>(window, "Saved filter list").ItemCount);
            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("code", Assert.Single(services.Settings.SavedFilters).Name);

            showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Behavior");
            advanced = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Edit filter definitions as text"));
            advanced.IsChecked = true;
            Field<TextBox>(window, "Saved filters advanced text").Text = "code = *.cs\nsource = *.cs|*.g.cs";
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["code", "source"], services.Settings.SavedFilters.Select(f => f.Name));
            Assert.Equal("*.cs|*.g.cs", services.Settings.SavedFilters[1].Mask);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Saved_filter_mask_error_keeps_the_named_item_in_the_draft_until_corrected()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.SavedFilters = [new() { Name = "code", Mask = "*.cs" }];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Behavior");
            var mask = Field<TextBox>(window, "Selected filter mask");
            mask.Text = "@missing";
            Dispatcher.UIThread.RunJobs();
            Click(window, "OK");
            Dispatcher.UIThread.RunJobs();
            Assert.False(showing.IsCompleted);
            Assert.Contains("code", AutomationProperties.GetHelpText(mask));
            Assert.Equal("*.cs", Assert.Single(services.Settings.SavedFilters).Mask);
            mask.Text = "*.cs|*.g.cs";
            Dispatcher.UIThread.RunJobs();
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("*.cs|*.g.cs", Assert.Single(services.Settings.SavedFilters).Mask);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task User_command_editor_preserves_nested_menus_and_reorders_siblings()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.UserCommands =
            [
                new() { Name = "Tools", Children = [new() { Name = "First", Executable = "first", Arguments = ["{file}"] }] },
                new() { Name = "Outside", Executable = "outside", Arguments = ["{file}"] },
            ];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var list = Field<ListBox>(window, "User command and menu list");
            Assert.Equal(3, list.ItemCount);
            list.SelectedIndex = 0;
            BodyButton(window, "Add command").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field<TextBox>(window, "Selected command or menu name").Text = "Second";
            Field<TextBox>(window, "Selected command program").Text = "C:\\Program Files\\Editor\\editor.exe";
            Field<TextBox>(window, "Selected command arguments").Text = "--profile \"two words\" {file}";
            Field<TextBox>(window, "Selected command working directory").Text = "{dir}";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Second", list.ItemsSource!.Cast<string>().ElementAt(2));
            BodyButton(window, "Move command or menu up").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, list.SelectedIndex);
            BodyButton(window, "Duplicate command or menu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(5, list.ItemCount);
            BodyButton(window, "Remove command or menu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(4, list.ItemCount);
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["Second", "First"], services.Settings.UserCommands[0].Children!.Select(c => c.Name));
            Assert.Equal("Outside", services.Settings.UserCommands[1].Name);
            Assert.Equal(["--profile", "two words", "{file}"], services.Settings.UserCommands[0].Children![0].Arguments);
            Assert.Equal("C:\\Program Files\\Editor\\editor.exe", services.Settings.UserCommands[0].Children![0].Executable);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task User_command_editor_reports_per_item_errors_without_committing_then_accepts_correction()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.UserCommands = [new() { Name = "Run", Executable = "runner", Arguments = ["{file}"] }];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var arguments = Field<TextBox>(window, "Selected command arguments");
            arguments.Text = "\"unterminated";
            Dispatcher.UIThread.RunJobs();
            Click(window, "OK");
            Assert.False(showing.IsCompleted);
            Assert.Contains("arguments", AutomationProperties.GetHelpText(arguments));
            Assert.Equal(["{file}"], Assert.Single(services.Settings.UserCommands).Arguments);
            arguments.Text = "\"two words\" {file}";
            Dispatcher.UIThread.RunJobs();
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["two words", "{file}"], Assert.Single(services.Settings.UserCommands).Arguments);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task User_command_raw_and_structured_round_trip_preserves_complex_definitions_and_cancel_discards_draft()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.UserCommands =
            [
                new() { Name = "Empty menu", Children = [] },
                new() { Name = "Other", Executable = "runner", Arguments = ["line\nbreak", ""], Mask = "*.txt", Intent = "open", Hotkey = "" },
            ];
            string before = JsonSerializer.Serialize(services.Settings.UserCommands);
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var advanced = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Edit user commands as text"));
            advanced.IsChecked = true;
            Assert.StartsWith("@tools:", Field<TextBox>(window, "User commands advanced text").Text);
            advanced.IsChecked = false;
            Assert.Equal(2, Field<ListBox>(window, "User command and menu list").ItemCount);
            advanced.IsChecked = true;
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, JsonSerializer.Serialize(services.Settings.UserCommands));

            showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            BodyButton(window, "Add menu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, JsonSerializer.Serialize(services.Settings.UserCommands));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Settings_search_finds_labels_in_other_tabs_and_navigates_without_applying_changes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string before = JsonSerializer.Serialize(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            var search = Field<TextBox>(window, "Search settings");
            search.Text = "copy verification";
            Dispatcher.UIThread.RunJobs();
            var matches = Field<ListBox>(window, "Matching settings");
            Assert.True(matches.IsVisible);
            Assert.Contains(matches.ItemsSource!.Cast<string>(), item => item.Contains("Behavior · Default copy verification", StringComparison.Ordinal));
            BodyButton(window, "Go to setting").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Behavior", (window.GetVisualDescendants().OfType<TabControl>().Single().SelectedItem as TabItem)?.Header);
            search.Text = "no such preference xyz";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, matches.ItemCount);
            Assert.False(BodyButton(window, "Go to setting").IsEnabled);
            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, JsonSerializer.Serialize(services.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Association_priority_controls_change_the_first_match_without_launching_a_program()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.Associations =
            [
                new() { Name = "Broad", Mask = "*.txt", Intent = Associations.View, Executable = "broad.exe", Arguments = ["{file}"] },
                new() { Name = "Specific", Mask = "secret*.txt", Intent = Associations.View, Executable = "specific.exe", Arguments = ["{file}"] },
            ];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var sample = Field<TextBox>(window, "Association preview filename");
            sample.Text = "secret-file.txt";
            var preview = Field<SelectableTextBlock>(window, "Association first match preview");
            BodyButton(window, "Preview first match").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("association 1 wins (*.txt)", preview.Text);
            Assert.Contains("broad.exe", preview.Text);
            var list = Field<ListBox>(window, "Association priority list");
            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            BodyButton(window, "Move association up").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("out of date", preview.Text);
            BodyButton(window, "Preview first match").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("association 1 wins (secret*.txt)", preview.Text);
            Assert.Contains("specific.exe", preview.Text);
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Specific", Associations.Find(services.Settings.Associations, Associations.View, "secret-file.txt")?.Name);
            Assert.Equal(["Specific", "Broad"], services.Settings.Associations.Select(a => a.Name));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Association_argument_error_stays_on_its_item_and_raw_toggle_round_trips()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            string before = ToolsSnapshot(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var advanced = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, "Edit associations as text"));
            advanced.IsChecked = true;
            Assert.Contains("*.txt", Field<TextBox>(window, "Associations advanced text").Text);
            advanced.IsChecked = false;
            Assert.False(advanced.IsChecked);
            var arguments = Field<TextBox>(window, "Association arguments");
            arguments.Text = "\"unterminated";
            Dispatcher.UIThread.RunJobs();
            Click(window, "OK");
            Dispatcher.UIThread.RunJobs();
            Assert.False(showing.IsCompleted);
            Assert.Contains("Association 1, arguments", AutomationProperties.GetHelpText(arguments));
            Assert.Equal(before, ToolsSnapshot(services.Settings));
            advanced.IsChecked = true;
            Assert.False(advanced.IsChecked);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t =>
                t.IsVisible && t.Classes.Contains("error") && t.Text?.Contains("Association 1, arguments", StringComparison.Ordinal) == true);
            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, ToolsSnapshot(services.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Association_preview_resolves_a_filter_added_in_the_same_settings_draft()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            services.Settings.Associations = [new()
            {
                Name = "Draft match", Mask = "@draft", Intent = Associations.View,
                Executable = "draft.exe", Arguments = ["{file}"],
            }];
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Behavior");
            BodyButton(window, "Add filter").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field<TextBox>(window, "Selected filter name").Text = "draft";
            Field<TextBox>(window, "Selected filter mask").Text = "secret*.txt";
            Dispatcher.UIThread.RunJobs();
            SelectPage(window, "Tools");
            Field<TextBox>(window, "Association preview filename").Text = "secret-file.txt";
            BodyButton(window, "Preview first match").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("association 1 wins (@draft)", Field<SelectableTextBlock>(window, "Association first match preview").Text);
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("draft", Assert.Single(services.Settings.SavedFilters).Name);
            Assert.Equal("Draft match", Assert.Single(services.Settings.Associations).Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("Editor arguments", "--profile \"unterminated", "Editor arguments:")]
    [InlineData("User commands advanced text", "Valid | tool | x\nBroken | tool | x | shell | ignored", "Line 2:")]
    [InlineData("Associations advanced text", "*.txt | view | tool | \"unterminated", "Line 1:")]
    public async Task Invalid_fields_keep_the_same_draft_session_and_focus_without_a_partial_commit(string fieldName, string draft, string message)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            string before = JsonSerializer.Serialize(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            if (fieldName == "Associations advanced text")
                window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Edit associations as text")).IsChecked = true;
            if (fieldName == "User commands advanced text")
                window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Edit user commands as text")).IsChecked = true;
            var field = Field<TextBox>(window, fieldName);
            field.Text = draft;
            field.CaretIndex = Math.Min(5, draft.Length);
            Click(window, "OK");
            Dispatcher.UIThread.RunJobs();
            Assert.False(showing.IsCompleted);
            Assert.True(vm.Dialogs.IsOpen);
            Assert.Same(field, Field<TextBox>(window, fieldName));
            Assert.Equal(draft, field.Text);
            Assert.True(field.IsFocused);
            Assert.Contains(message, AutomationProperties.GetHelpText(field));
            Assert.Equal(before, JsonSerializer.Serialize(services.Settings));
            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, JsonSerializer.Serialize(services.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Reverting_a_valid_argument_draft_after_another_error_cannot_commit_stale_parsed_arguments()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            var expected = services.Settings.Editor!.Arguments.ToArray();
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
            var args = Field<TextBox>(window, "Editor arguments");
            var exe = Field<TextBox>(window, "Editor (F4) program");
            window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Edit user commands as text")).IsChecked = true;
            var commands = Field<TextBox>(window, "User commands advanced text");
            string originalArguments = args.Text!, originalCommands = commands.Text!;
            args.Text = "valid-but-reverted";
            commands.Text = "broken";
            Click(window, "OK");
            Assert.False(showing.IsCompleted);
            args.Text = originalArguments;
            commands.Text = originalCommands;
            // A semantically identical but explicitly edited executable exercises the editor reconstruction path.
            exe.Text = "\"" + exe.Text + "\"";
            Click(window, "OK");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(expected, services.Settings.Editor!.Arguments);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Cancel_restores_the_saved_theme_after_preview_and_preserves_the_tools()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            string before = ToolsSnapshot(services.Settings), originalTheme = services.Settings.Theme;
            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            Field<ComboBox>(window, "Theme").SelectedIndex = ThemeManager.Names.ToList().IndexOf("ClassicDark");
            Assert.Equal("ClassicDark", ThemeManager.RequestedName);
            Click(window, "Cancel");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(originalTheme, ThemeManager.RequestedName);
            Assert.Equal(before, ToolsSnapshot(services.Settings));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(StateLoadStatus.NewerSchemaReadOnly, "newer FileCat")]
    [InlineData(StateLoadStatus.OversizedReadOnly, "safe input limit")]
    [InlineData(StateLoadStatus.UnavailableReadOnly, "could not be read safely")]
    public async Task Protected_profiles_remain_inspectable_with_the_correct_reason_and_no_edit_actions(StateLoadStatus status, string reason)
    {
        if (status == StateLoadStatus.UnavailableReadOnly && OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix non-regular settings input fixture; Windows profile permission validation is a native gate.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "filecat-settings-protected", Guid.NewGuid().ToString("N"));
        var paths = AppPaths.Resolve(overrideRoot: root);
        Directory.CreateDirectory(paths.SettingsDirectory);
        if (status == StateLoadStatus.NewerSchemaReadOnly) File.WriteAllText(paths.SettingsFile, "{\"SchemaVersion\":999,\"Theme\":\"Classic\"}");
        else if (status == StateLoadStatus.OversizedReadOnly) File.WriteAllText(paths.SettingsFile, new string('x', 8 * 1024 * 1024 + 1));
        else Assert.Equal(0, MkFifo(paths.SettingsFile, 0x180));
        byte[]? original = status == StateLoadStatus.UnavailableReadOnly ? null : File.ReadAllBytes(paths.SettingsFile);
        var services = AppServices.CreateForPaths(paths);
        var vm = new MainViewModel(services);
        var window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
        try
        {
            vm.Initialize(null);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(status, services.SettingsStatus);
            string before = JsonSerializer.Serialize(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(reason, StringComparison.Ordinal) == true);
            Assert.False(Field<ComboBox>(window, "Theme").IsEnabled);
            SelectPage(window, "Tools");
            Assert.True(Field<TextBox>(window, "Editor arguments").IsReadOnly);
            Assert.True(Field<TextBox>(window, "Editor arguments").IsEffectivelyEnabled);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "OK");
            Click(window, "Close");
            await showing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(before, JsonSerializer.Serialize(services.Settings));
            if (original is not null) Assert.Equal(original, File.ReadAllBytes(paths.SettingsFile));
            else Assert.False(UnixFiles.Stat(paths.SettingsFile)!.Value.IsRegular);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static void ConfigureTools(AppSettings settings, string root)
    {
        settings.Editor = new ToolDefinition
        {
            Name = "Custom editor", Executable = Environment.ProcessPath!, Arguments = ["--profile", "C:\\My Editor Profile", "", "literal\"quote", "{files}"],
            WorkingDirectory = root, ShellMode = true, Hotkey = "Ctrl+Alt+E", Intent = "edit", Mask = "*.txt",
        };
        settings.Viewer = new() { Name = "Viewer", Executable = "viewer", Arguments = [""] };
        settings.DiffTool = new() { Name = "Diff", Executable = "diff", WorkingDirectory = "custom", ShellMode = true };
        settings.Terminal = new() { Shell = OperatingSystem.IsWindows() ? "cmd" : "posix", CustomExecutable = "custom-terminal", Defaults = 9 };
        settings.UserCommands = [new() { Name = "#Build", Executable = "build", Arguments = [""] }, new() { Name = "#Tools", Children = [] }];
        settings.Associations = [new() { Name = "Custom association", Intent = "view", Mask = "*.txt|secret*", Executable = "viewer", Arguments = ["a|b", ""], WorkingDirectory = "custom", ShellMode = true, Hotkey = "" }];
    }

    private static string ToolsSnapshot(AppSettings s) => JsonSerializer.Serialize(new { s.Editor, s.Viewer, s.DiffTool, s.Terminal, s.UserCommands, s.Associations });
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
    private static T Field<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
    private static void Click(Window window, string text) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content as string == text && b.IsEffectivelyEnabled &&
            b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("dialogFooter")))
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button BodyButton(Window window, string text) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.Content, text));
    private static void SelectPage(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == name);
        Dispatcher.UIThread.RunJobs();
    }
}
