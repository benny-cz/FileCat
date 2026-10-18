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

    [AvaloniaTheory]
    [InlineData("Editor arguments", "--profile \"unterminated", "Editor arguments:")]
    [InlineData("User commands (F9)", "Valid | tool | x\nBroken | tool | x | shell | ignored", "Line 2:")]
    [InlineData("Associations", "*.txt | view | tool | \"unterminated", "Line 1:")]
    public async Task Invalid_fields_keep_the_same_draft_session_and_focus_without_a_partial_commit(string fieldName, string draft, string message)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            ConfigureTools(services.Settings, root);
            string before = JsonSerializer.Serialize(services.Settings);
            var showing = SettingsDialog.ShowAsync(vm);
            SelectPage(window, "Tools");
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
            var commands = Field<TextBox>(window, "User commands (F9)");
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
    private static void SelectPage(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList().FindIndex(t => t.Header as string == name);
        Dispatcher.UIThread.RunJobs();
    }
}
