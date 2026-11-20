using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Interactivity;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class HexEditorFindControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Typing_in_hex_editor_finds_without_enter_and_invalid_hex_stays_neutral()
    {
        string root = Path.Join(Path.GetTempPath(), "filecat-editor-live-find-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        string path = Path.Join(root, "owned.bin");
        File.WriteAllBytes(path, "alpha beta alpha"u8.ToArray());
        Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
        var editor = Assert.Single(HexEditorWindow.OpenWindows);
        try
        {
            var search = Field<TextBox>(editor, "_search");
            search.Text = "alpha";
            await WaitFor(() => search.Classes.Contains("searchFound"));
            var hex = Field<HexView>(editor, "_hex");
            Assert.Equal(0, hex.Selection.Start);
            hex.GoTo(5);
            Assert.Contains("searchFound", search.Classes);
            search.Text = "missing";
            Assert.DoesNotContain("searchFound", search.Classes);
            await WaitFor(() => search.Classes.Contains("searchMissing"));
            Field<CheckBox>(editor, "_hexSearch").IsChecked = true;
            search.Text = "GG";
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("searchMissing", search.Classes);
            search.Text = "61 6C";
            await WaitFor(() => search.Classes.Contains("searchFound"));
        }
        finally
        {
            editor.CloseNow();
            Directory.Delete(root, recursive: true);
        }
    }

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
            var hex = Field<HexView>(editor, "_hex");
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
            Assert.StartsWith("Find: " + count.Text, hex.DescribeCursor());
            Click(next);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{second:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("2 distinct matches visited · total not counted", count.Text);
            Assert.StartsWith("Find: " + count.Text, hex.DescribeCursor());

            var contentRoot = Assert.IsAssignableFrom<Control>(editor.Content);
            contentRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.InRange(contentRoot.DesiredSize.Width, 600, 760);
            Assert.InRange(contentRoot.DesiredSize.Height, 350, 540);
            Click(previous);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{first:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("2 distinct matches visited · total not counted", count.Text);
            Click(previous);
            await WaitFor(() => status.Text?.StartsWith($"Wrapped to the end. Found at 0x{second:X}.",
                StringComparison.Ordinal) == true);
            Assert.Equal("2 distinct matches visited · total not counted", count.Text);
            Assert.Contains("Find: " + count.Text, hex.SemanticStatus);
            Click(next);
            await WaitFor(() => status.Text?.StartsWith($"Wrapped to the beginning. Found at 0x{first:X}.",
                StringComparison.Ordinal) == true);

            editor.Width = 720;
            editor.Height = 500;
            editor.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            editor.CaptureRenderedFrame()?.Dispose();
            Assert.InRange(editor.ClientSize.Width, 700, 740);
            Assert.InRange(editor.ClientSize.Height, 480, 520);
            foreach (var control in new Control[] { search, previous, next, count })
            {
                Assert.True(control.Bounds.Width > 8 && control.Bounds.Height > 8);
                var point = control.TranslatePoint(new Point(0, 0), editor);
                Assert.NotNull(point);
                Assert.InRange(point.Value.X, 0, editor.ClientSize.Width);
                Assert.InRange(point.Value.Y, 0, editor.ClientSize.Height);
                Assert.True(point.Value.X + control.Bounds.Width <= editor.ClientSize.Width + 1);
                Assert.True(point.Value.Y + control.Bounds.Height <= editor.ClientSize.Height + 1);
            }
            editor.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(search.IsFocused);

            search.Text = "second";
            Assert.Equal("0 matches visited · total not counted", count.Text);
            Assert.DoesNotContain("2 distinct matches visited", hex.SemanticStatus);
            Click(next);
            await WaitFor(() => status.Text?.StartsWith($"Found at 0x{second + 7:X}.", StringComparison.Ordinal) == true);
            Assert.Equal("1 distinct match visited · total not counted", count.Text);
            Assert.StartsWith("Find: " + count.Text, hex.DescribeCursor());
            search.Text = "";
            Assert.False(previous.IsEnabled);
            Assert.False(next.IsEnabled);
            Assert.Equal("Enter text to find matches", count.Text);
            Assert.DoesNotContain("Find:", hex.SemanticStatus);
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
