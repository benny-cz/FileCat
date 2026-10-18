using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerGoToDialogTests
{
    [Fact]
    public void Validator_distinguishes_offsets_percentages_and_lines_without_clamping()
    {
        static ViewerWindow.GoToTarget Valid(string input, long length = 100, bool seek = true)
        {
            Assert.Null(ViewerWindow.ValidateGoTo(input, length, seek, out var target));
            return target;
        }

        Assert.Equal(new ViewerWindow.GoToTarget(false, 16), Valid("0x10"));
        Assert.Equal(new ViewerWindow.GoToTarget(false, 42), Valid("42"));
        Assert.Equal(new ViewerWindow.GoToTarget(false, 50), Valid("50%"));
        Assert.Equal(new ViewerWindow.GoToTarget(false, 99), Valid("100%"));
        Assert.Equal(new ViewerWindow.GoToTarget(true, 1200), Valid("L1200"));
        Assert.Equal(new ViewerWindow.GoToTarget(true, 2), Valid("line 2"));
        Assert.Equal(new ViewerWindow.GoToTarget(true, 3), Valid(":3"));
        Assert.Equal(new ViewerWindow.GoToTarget(false, long.MaxValue - 1), Valid("100%", long.MaxValue));

        foreach (string invalid in new[] { "", "0xZZ", "0xFFFFFFFFFFFFFFFF", "-1", "100", "101%", "-1%", "NaN%", "L0", "L-2", "Labc", "L9223372036854775808" })
            Assert.NotNull(ViewerWindow.ValidateGoTo(invalid, 100, true, out _));
        Assert.Contains("seekable", ViewerWindow.ValidateGoTo("L2", 100, false, out _)!);
        Assert.Contains("empty", ViewerWindow.ValidateGoTo("0", 0, true, out _)!, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Invalid_draft_stays_open_with_inline_error_and_pointer_actions_navigate_after_correction()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-viewer-goto-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new MemoryContentSource("sample.bin", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()),
            "sample.bin", hex: true) { Width = 480, Height = 300 };
        try
        {
            viewer.Show();
            var host = (Panel)typeof(ViewerWindow).GetField("_dialogHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewer)!;
            var pending = (Task)typeof(ViewerWindow).GetMethod("GoToAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewer, [])!;
            await Until(() => host.IsVisible && host.GetVisualDescendants().OfType<TextBox>().Any());
            var box = host.GetVisualDescendants().OfType<TextBox>().Single();
            var go = host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Go");
            var cancel = host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");
            await Until(() => box.Bounds.Width > 0 && go.Bounds.Width > 0);
            Assert.True(cancel.IsVisible);
            Assert.True(go.IsVisible);
            Assert.True(box.Bounds.Width > 0);
            Assert.True(go.Bounds.Width > 0);
            var goPosition = go.TranslatePoint(default, viewer);
            Assert.NotNull(goPosition);
            Assert.True(goPosition.Value.Y + go.Bounds.Height <= viewer.Bounds.Height);
            box.Text = "0xZZ";
            await Until(() => !go.IsEnabled);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible &&
                t.Text?.Contains("hexadecimal", StringComparison.OrdinalIgnoreCase) == true);
            Assert.Equal("0xZZ", box.Text);
            Assert.False(pending.IsCompleted);

            box.Text = "300";
            await Until(() => host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible &&
                t.Text?.Contains("offset from 0 to", StringComparison.Ordinal) == true));
            Assert.False(go.IsEnabled);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible &&
                t.Text?.Contains("0 to 255", StringComparison.Ordinal) == true);
            Assert.False(pending.IsCompleted);

            box.Text = "0x10";
            await Until(() => go.IsEnabled);
            go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(host.IsVisible);
            var hex = (HexView)typeof(ViewerWindow).GetField("_hex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewer)!;
            Assert.Equal(16, hex.CursorOffset);

            pending = (Task)typeof(ViewerWindow).GetMethod("GoToAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewer, [])!;
            await Until(() => host.IsVisible);
            host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(16, hex.CursorOffset);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Nonseekable_line_is_rejected_inside_the_prompt_before_a_scan_starts()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-viewer-goto-nonseek-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new NonseekableSource(), "remote.txt", hex: false);
        try
        {
            viewer.Show();
            var host = (Panel)typeof(ViewerWindow).GetField("_dialogHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewer)!;
            var pending = (Task)typeof(ViewerWindow).GetMethod("GoToAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewer, [])!;
            await Until(() => host.IsVisible && host.GetVisualDescendants().OfType<TextBox>().Any());
            var box = host.GetVisualDescendants().OfType<TextBox>().Single();
            box.Text = "L2";
            var go = host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Go");
            await Until(() => !go.IsEnabled);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.IsVisible &&
                t.Text?.Contains("seekable", StringComparison.OrdinalIgnoreCase) == true);
            Assert.False(pending.IsCompleted);
            host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_enter_navigates_to_a_line_and_escape_cancels_a_new_draft()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-viewer-goto-line-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        string content = string.Concat(Enumerable.Range(1, 1200).Select(i => $"line {i:D4} {new string('x', 60)}\n"));
        var viewer = new ViewerWindow(services, new MemoryContentSource("lines.txt", System.Text.Encoding.UTF8.GetBytes(content)),
            "lines.txt", hex: false) { Width = 540, Height = 360 };
        try
        {
            viewer.Show();
            var host = (Panel)typeof(ViewerWindow).GetField("_dialogHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewer)!;
            var pending = (Task)typeof(ViewerWindow).GetMethod("GoToAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewer, [])!;
            await Until(() => host.IsVisible && host.GetVisualDescendants().OfType<TextBox>().Any());
            var box = host.GetVisualDescendants().OfType<TextBox>().Single();
            box.Text = "L900";
            await Until(() => host.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Go").IsEnabled);
            box.Focus();
            viewer.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Contains("Line 900", viewer.StatusText);
            var text = (TextViewer)typeof(ViewerWindow).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewer)!;
            long atLine = System.Text.Encoding.UTF8.GetByteCount(string.Concat(Enumerable.Range(1, 899).Select(i => $"line {i:D4} {new string('x', 60)}\n")));
            Assert.InRange(text.TopOffset, atLine - 1000, atLine + 1000);

            pending = (Task)typeof(ViewerWindow).GetMethod("GoToAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewer, [])!;
            await Until(() => host.IsVisible && host.GetVisualDescendants().OfType<TextBox>().Any());
            box = host.GetVisualDescendants().OfType<TextBox>().Single();
            box.Text = "L1000";
            box.Focus();
            viewer.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(host.IsVisible);
            Assert.InRange(text.TopOffset, atLine - 1000, atLine + 1000);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private sealed class NonseekableSource : IContentSource
    {
        private readonly MemoryContentSource _inner = new("remote.txt", "first\nsecond\n"u8.ToArray());
        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => false;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        public void Dispose() => _inner.Dispose();
    }
}
