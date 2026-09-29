using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class KeyboardReferenceTests
{
    [AvaloniaFact]
    public async Task Search_filters_commands_and_enter_runs_selected_command()
    {
        var host = new Grid();
        var window = new Window { Width = 1000, Height = 750, Content = host };
        window.Show();
        try
        {
            var dialogs = new OverlayDialogService(host, () => null);
            var task = dialogs.KeyboardReferenceAsync([
                new KeyboardHelpEntry("file.copy", "Copy", "Files", "F5", null),
                new KeyboardHelpEntry("file.move", "Move", "Files", "F6", null),
                new KeyboardHelpEntry("app.settings", "Settings", "App", null, null, false, "No active workspace"),
            ]);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            var search = host.GetVisualDescendants().OfType<TextBox>().Single(x => x.PlaceholderText == "Search command, shortcut, or command ID");
            var list = host.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.True(search.Focus());
            search.Text = "F6";
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Single(list.ItemsSource!.Cast<KeyboardHelpEntry>());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(new KeyboardReferenceChoice("file.move", ChangeShortcut: false), await task);
            Assert.False(dialogs.IsOpen);
            var unavailable = dialogs.KeyboardReferenceAsync([
                new KeyboardHelpEntry("app.settings", "Settings", "App", null, null, false, "No active workspace"),
            ]);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            var disabledRun = host.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Run command"));
            Assert.False(disabledRun.IsEnabled);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("No active workspace", StringComparison.Ordinal) == true);
            var secondSearch = host.GetVisualDescendants().OfType<TextBox>().Single(x => x.PlaceholderText == "Search command, shortcut, or command ID");
            secondSearch.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.False(unavailable.IsCompleted);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Null(await unavailable);
        }
        finally { window.Close(); }
    }
}
