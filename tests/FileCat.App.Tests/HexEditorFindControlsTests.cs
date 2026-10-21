using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class HexEditorFindControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Visible_direction_buttons_report_distinct_visited_matches_and_retire_changed_query()
    {
        string root = Path.Join(Path.GetTempPath(), "filecat-editor-find-controls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        const int first = 1024 * 1024 + 20;
        const int second = 6 * 1024 * 1024 + 20;
        var bytes = new byte[7 * 1024 * 1024];
        Array.Fill(bytes, (byte)'x');
        "NEEDLE first"u8.CopyTo(bytes.AsSpan(first));
        "NEEDLE second"u8.CopyTo(bytes.AsSpan(second));
        string path = Path.Join(root, "owned.bin");
        File.WriteAllBytes(path, bytes);
        byte[] expectedHash = SHA256.HashData(bytes);
        Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
        var editor = Assert.Single(HexEditorWindow.OpenWindows);
        try
        {
            var search = Field<TextBox>(editor, "_search");
            var previous = Field<Button>(editor, "_previousMatch");
            var next = Field<Button>(editor, "_nextMatch");
            var count = Field<TextBlock>(editor, "_matchCount");
            var status = Field<TextBlock>(editor, "_status");
            Assert.Equal("Find previous match", AutomationProperties.GetName(previous));
            Assert.Equal("Find next match", AutomationProperties.GetName(next));
            Assert.False(previous.IsEnabled);
            Assert.False(next.IsEnabled);
            Assert.Equal("Enter text to find matches", count.Text);

            search.Text = "NEEDLE";
            Assert.True(previous.IsEnabled);
            Assert.True(next.IsEnabled);
            Assert.Equal("0 matches visited · total not counted", count.Text);
            Click(next);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{first:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("1 distinct match visited · total not counted", count.Text);
            Click(next);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{second:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("2 distinct matches visited · total not counted", count.Text);
            Click(previous);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{first:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("2 distinct matches visited · total not counted", count.Text);
            Click(previous);
            await WaitFor(() => status.Text?.StartsWith("\"NEEDLE\" was not found before the cursor.",
                StringComparison.Ordinal) == true);
            Assert.Equal("2 distinct matches visited · total not counted", count.Text);

            search.Text = "second";
            Assert.Equal("0 matches visited · total not counted", count.Text);
            Click(next);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{second + 7:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("1 distinct match visited · total not counted", count.Text);
            search.Text = "";
            Assert.False(previous.IsEnabled);
            Assert.False(next.IsEnabled);
            Assert.Equal("Enter text to find matches", count.Text);
            Assert.False(editor.HasUnsavedWork);
        }
        finally
        {
            editor.CloseNow();
            Assert.Equal(expectedHash, SHA256.HashData(File.ReadAllBytes(path)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static T Field<T>(HexEditorWindow owner, string name) =>
        (T)typeof(HexEditorWindow).GetField(name, Private)!.GetValue(owner)!;

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task WaitFor(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("The editor search result did not settle.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
