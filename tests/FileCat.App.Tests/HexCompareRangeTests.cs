using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class HexCompareRangeTests
{
    private static async Task<CompareWindow> Open(byte[] left, byte[] right)
    {
        var window = CompareWindow.Open("left.bin", new MemoryContentSource("left.bin", left),
            "right.bin", new MemoryContentSource("right.bin", right));
        await Until(() => !window.IsComparing);
        window.Width = 640;
        window.Height = 320;
        window.UpdateLayout();
        return window;
    }

    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready());
    }

    private static T Named<T>(CompareWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static void StartRange(CompareWindow window)
    {
        window.KeyPress(Key.B, RawInputModifiers.Alt, PhysicalKey.B, null);
        window.UpdateLayout();
        Assert.True(Named<ComboBox>(window, "Source side for byte selection").IsEffectivelyVisible);
    }

    private static void Select(CompareWindow window, int side, string offset, string count)
    {
        Named<ComboBox>(window, "Source side for byte selection").SelectedIndex = side;
        Named<TextBox>(window, "Start offset in decimal or 0x hexadecimal").Text = offset;
        var number = Named<TextBox>(window, "Number of source bytes in decimal");
        number.Text = count;
        number.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
    }

    [AvaloniaFact]
    public async Task Keyboard_action_selects_exact_four_bytes_on_each_side_and_exposes_source_evidence()
    {
        byte[] left = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        byte[] right = left.Select(b => (byte)(b ^ 0x80)).ToArray();
        left[0] = right[0] = 0;
        var window = await Open(left, right);
        try
        {
            var hex = window.GetVisualDescendants().OfType<HexCompareView>().Single();
            var peer = Assert.IsType<HexCompareViewAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(hex));
            Assert.Contains("No byte range selected", ((IValueProvider)peer).Value);
            StartRange(window);
            Named<ComboBox>(window, "Source side for byte selection").SelectedIndex = 1;
            Assert.Equal("right.bin", Named<TextBox>(window, "Full path of selected source file").Text);
            Select(window, 1, "0xF", "4"); // crosses the 16-byte row boundary
            await Until(() => hex.Selection == (1, 15L, 4L));
            Assert.NotNull(hex.PointOf(1, 15));
            Assert.Contains("Right file, offset 0xF, 4 selected bytes", ((IValueProvider)peer).Value);
            await Until(() => Named<TextBox>(window, "Selected source bytes and comparison view").Text!.Contains(HexCompareView.HexText(right.AsSpan(15, 4)), StringComparison.Ordinal));
            hex.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            await Until(() => hex.Status.StartsWith("Copied", StringComparison.Ordinal));
            Assert.Equal(HexCompareView.HexText(right.AsSpan(15, 4)), await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));

            StartRange(window);
            Select(window, 0, "15", "4");
            await Until(() => hex.Selection == (0, 15L, 4L));
            hex.Focus();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            await Until(() => hex.Status == "Copied 4 bytes as hex.");
            Assert.Equal(HexCompareView.HexText(left.AsSpan(15, 4)), await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.True(((IValueProvider)peer).IsReadOnly);
            Assert.Throws<InvalidOperationException>(() => ((IValueProvider)peer).SetValue("overwrite"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Invalid_and_overflowing_input_keeps_the_review_and_draft_without_selecting()
    {
        byte[] left = [0, 1, 2, 3, 4, 5];
        byte[] right = [0, 9, 8, 3, 4, 5];
        var window = await Open(left, right);
        try
        {
            var hex = window.GetVisualDescendants().OfType<HexCompareView>().Single();
            StartRange(window);
            Select(window, 1, "0xFFFFFFFFFFFFFFFF", "4");
            Assert.Null(hex.Selection);
            Assert.Equal("0xFFFFFFFFFFFFFFFF", Named<TextBox>(window, "Start offset in decimal or 0x hexadecimal").Text);
            Assert.Contains("overflow", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("error") && t.IsEffectivelyVisible).Text);
            Select(window, 1, "4", "3");
            Assert.Null(hex.Selection);
            Assert.Equal("3", Named<TextBox>(window, "Number of source bytes in decimal").Text);
            Select(window, 1, "4", "2");
            await Until(() => hex.Selection == (1, 4L, 2L));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Cancel_and_source_replacement_retire_the_range_review_without_reusing_its_draft()
    {
        byte[] first = [0, 1, 2, 3, 4, 5, 6, 7];
        byte[] next = [0, 9, 8, 7, 6, 5, 4, 3];
        var window = CompareWindow.Open("left.bin", new MemoryContentSource("left.bin", first),
            "right.bin", new MemoryContentSource("right.bin", first),
            reopen: () => (new MemoryContentSource("left-new", next), new MemoryContentSource("right-new", next)));
        try
        {
            await Until(() => !window.IsComparing);
            window.Width = 640; window.Height = 320; window.UpdateLayout();
            var hex = window.GetVisualDescendants().OfType<HexCompareView>().Single();
            hex.Focus();
            StartRange(window);
            Named<TextBox>(window, "Start offset in decimal or 0x hexadecimal").Text = "0x7";
            var cancel = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel") && b.IsEffectivelyVisible);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(hex.IsFocused);
            Assert.Null(hex.Selection);

            StartRange(window);
            Named<TextBox>(window, "Start offset in decimal or 0x hexadecimal").Text = "0x7";
            window.CompareAgain();
            await Until(() => !window.IsComparing && window.Summary.StartsWith("Identical:", StringComparison.Ordinal));
            window.UpdateLayout();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(), c =>
                AutomationProperties.GetName(c) == "Source side for byte selection" && c.IsEffectivelyVisible);
            Assert.Null(hex.Selection);
            Assert.True(hex.TrySelectRange(1, 4, 4));
            await hex.CopyAsync(asText: false);
            Assert.Equal("06 05 04 03", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Aligned_source_ranges_cross_visual_gaps_without_inventing_counterpart_bytes()
    {
        byte[] left = Enumerable.Range(0, 2048).Select(i => (byte)((i * 31) % 251)).ToArray();
        left[0] = 0;
        byte[] right = left[..64].Concat(new byte[] { 251, 252, 253, 254 }).Concat(left[64..]).ToArray();
        var window = await Open(left, right);
        try
        {
            var align = window.GetVisualDescendants().OfType<CheckBox>().Single(b => Equals(b.Content, "Align shifted bytes"));
            align.IsChecked = true;
            await Until(() => !window.IsComparing && window.Summary.StartsWith("Aligned:", StringComparison.Ordinal));
            var hex = window.GetVisualDescendants().OfType<HexCompareView>().Single();
            Assert.True(hex.TrySelectRange(1, 62, 6));
            Assert.Equal((1, 62L, 6L), hex.Selection);
            Assert.NotNull(hex.PointOf(1, 62));
            await hex.CopyAsync(asText: false);
            Assert.Equal(HexCompareView.HexText(right.AsSpan(62, 6)), await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.True(hex.TrySelectRange(0, 62, 6));
            await hex.CopyAsync(asText: false);
            Assert.Equal(HexCompareView.HexText(left.AsSpan(62, 6)), await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.False(hex.TrySelectRange(1, right.Length, 1));
            Assert.Equal((0, 62L, 6L), hex.Selection);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_superseded_copy_cannot_publish_bytes_from_the_old_side()
    {
        const long heldOffset = 2L * PagedReader.PageSize;
        byte[] data = new byte[3 * PagedReader.PageSize];
        data[heldOffset] = 0xA5;
        using var left = new PagedReader(new MemoryContentSource("left", data));
        using var held = new HeldSource(data, heldOffset);
        using var right = new PagedReader(held);
        var hex = new HexCompareView();
        var window = new Window { Content = hex, Width = 640, Height = 320 };
        window.Show();
        try
        {
            window.UpdateLayout();
            hex.SetContent(left, right, null, []);
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(window.Clipboard!, "previous clipboard");
            held.Arm();
            Assert.True(hex.TrySelectRange(1, heldOffset, 4));
            var old = hex.CopyAsync(asText: false);
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(hex.TrySelectRange(0, 0, 4));
            held.Release.Set();
            await old.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("previous clipboard", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            await hex.CopyAsync(asText: false);
            Assert.Equal("00 00 00 00", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
        }
        finally { held.Release.Set(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Copy_keeps_the_four_megabyte_cap_and_discloses_a_short_read()
    {
        byte[] data = new byte[HexCompareView.MaxCopy + 9];
        data[0] = 0xAB;
        data[HexCompareView.MaxCopy - 1] = 0xCD;
        using var left = new PagedReader(new MemoryContentSource("left", data));
        using var right = new PagedReader(new MemoryContentSource("right", data));
        var hex = new HexCompareView();
        var window = new Window { Content = hex, Width = 640, Height = 320 };
        window.Show();
        try
        {
            window.UpdateLayout();
            hex.SetContent(left, right, null, []);
            Assert.True(hex.TrySelectRange(1, 0, data.Length));
            await hex.CopyAsync(asText: false);
            string copied = (await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!))!;
            Assert.Equal(HexCompareView.MaxCopy * 3 - 1, copied.Length);
            Assert.StartsWith("AB 00", copied);
            Assert.EndsWith(" CD", copied);
            Assert.Contains("first", hex.Status);
            Assert.Contains((data.Length).ToString("N0"), hex.Status);
        }
        finally { window.Close(); }

        using var shortLeft = new PagedReader(new ShortSource());
        using var shortRight = new PagedReader(new ShortSource());
        var shortHex = new HexCompareView();
        var shortWindow = new Window { Content = shortHex, Width = 640, Height = 320 };
        shortWindow.Show();
        try
        {
            shortWindow.UpdateLayout();
            shortHex.SetContent(shortLeft, shortRight, null, []);
            Assert.True(shortHex.TrySelectRange(1, 0, 10));
            await shortHex.CopyAsync(asText: false);
            Assert.Equal("01 02 03 04 05", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(shortWindow.Clipboard!));
            Assert.Contains("rest could not be read", shortHex.Status);
        }
        finally { shortWindow.Close(); }
    }

    [AvaloniaFact]
    public async Task Copy_refuses_a_source_that_changed_after_the_comparison_snapshot()
    {
        using var source = new ChangedSource();
        using var left = new PagedReader(new MemoryContentSource("left", [0, 1, 2, 3]));
        using var right = new PagedReader(source);
        var hex = new HexCompareView();
        var window = new Window { Content = hex, Width = 640, Height = 320 };
        window.Show();
        try
        {
            window.UpdateLayout();
            hex.SetContent(left, right, null, []);
            Assert.True(hex.TrySelectRange(1, 0, 4));
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(window.Clipboard!, "keep this clipboard");
            source.Change();
            await hex.CopyAsync(asText: false);
            Assert.Equal("keep this clipboard", await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.Contains("source changed", hex.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally { window.Close(); }
    }

    private sealed class HeldSource(byte[] data, long heldOffset) : IContentSource
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        public string DisplayName => "held right";
        public long Length => data.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(data.Length, 0);
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset == heldOffset && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Held copy was not released.");
            }
            int count = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public void Dispose() => Release.Set();
    }

    private sealed class ShortSource : IContentSource
    {
        public string DisplayName => "short source";
        public long Length => 10;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(10, 0);
        public int Read(long offset, Span<byte> buffer)
        {
            byte[] actual = [1, 2, 3, 4, 5];
            if (offset >= actual.Length) return 0;
            int count = (int)Math.Min(buffer.Length, actual.Length - offset);
            actual.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public void Dispose() { }
    }

    private sealed class ChangedSource : IContentSource
    {
        private int _revision;
        public string DisplayName => "changed source";
        public long Length => 4;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(4, Volatile.Read(ref _revision));
        public int Read(long offset, Span<byte> buffer)
        {
            byte[] bytes = [0, 4, 5, 6];
            if (offset >= bytes.Length) return 0;
            int count = (int)Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        internal void Change() => Interlocked.Increment(ref _revision);
        public void Dispose() { }
    }
}
