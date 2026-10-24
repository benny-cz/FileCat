using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>FCUX-008: explicit immutable scope, complete selectable results and user-owned clipboard.</summary>
public sealed class ViewerChecksumScopeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ClipboardSentinel = "clipboard content the user is keeping";

    [AvaloniaTheory]
    [InlineData(480)]
    [InlineData(980)]
    public async Task One_byte_selection_hashes_one_byte_and_complete_results_remain_selectable_until_explicit_copy(int width)
    {
        byte[] bytes = [0x00, 0x10, 0x2a, 0x80, 0xff];
        using var f = await Fixture.Create(bytes, width: width);
        f.Hex.GoTo(2, select: true, length: 1);
        f.Show();
        Assert.Equal(1, f.Scope.SelectedIndex);
        Assert.Contains("1 bytes at 0x2", Assert.IsType<string>(f.Scope.SelectedItem));
        Assert.DoesNotContain("SHA-256:", f.Result);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
        await f.Compute();
        Assert.Contains("selected range 0x2–0x2, 1 of 1 bytes read", f.Result);
        Assert.Contains("SHA-256: " + Hash([bytes[2]]), f.Result);
        Assert.Contains($"CRC-32: {Crc32.Append(0, [bytes[2]]):x8}", f.Result);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
        var box = Field<TextBox>(f.Viewer, "_checksumResult");
        f.Viewer.UpdateLayout();
        Assert.Equal(width, f.Viewer.Bounds.Width);
        var origin = box.TranslatePoint(default, f.Viewer)!.Value;
        Assert.InRange(box.Bounds.Width, 100, width);
        Assert.InRange(box.Bounds.Height, 80, 190);
        Assert.InRange(origin.X, 0, f.Viewer.Bounds.Width - box.Bounds.Width);
        Assert.InRange(origin.Y, 0, f.Viewer.Bounds.Height - box.Bounds.Height);
        Assert.True(box.IsReadOnly);
        Assert.Equal("Checksum results", AutomationProperties.GetName(box));
        if (OperatingSystem.IsLinux())
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(f.Result, AutomationProperties.GetHelpText(box));
        }
        box.SelectAll();
        Assert.Equal(f.Result, box.SelectedText);
        string result = f.Result;
        f.Hex.GoTo(4);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(result, f.Result);
        Assert.True(Field<Border>(f.Viewer, "_checksumPanel").IsVisible);
        await (Task)Invoke(f.Viewer, "CopyChecksumAsync")!;
        Assert.Equal(result, await f.ClipboardText());
    }

    [AvaloniaFact]
    public async Task Whole_file_is_an_explicit_scope_and_opening_the_panel_does_not_compute_or_replace_clipboard()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        using var f = await Fixture.Create(bytes);
        Assert.Equal(0, f.Hex.Selection.Length);
        f.Show();
        Assert.Single(f.Scope.Items);
        Assert.Equal(0, f.Scope.SelectedIndex);
        Assert.Contains("Whole file", Assert.IsType<string>(f.Scope.SelectedItem));
        Assert.DoesNotContain("SHA-256:", f.Result);
        Assert.Null(typeof(ViewerWindow).GetField("_checksumCts", Fields)!.GetValue(f.Viewer));
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
        await f.Compute();
        Assert.Contains("whole file, 5 of 5 bytes read", f.Result);
        Assert.Contains("SHA-256: " + Hash(bytes), f.Result);
        f.Hex.GoTo(1, select: true, length: 1);
        f.Show();
        Assert.Equal(1, f.Scope.SelectedIndex);
        f.Scope.SelectedIndex = 0;
        await f.Compute();
        Assert.Contains("whole file, 5 of 5 bytes read", f.Result);
        Assert.Contains("SHA-256: " + Hash(bytes), f.Result);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
    }

    [AvaloniaFact]
    public async Task Scope_review_captures_range_before_caret_movement()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        using var f = await Fixture.Create(bytes);
        f.Hex.GoTo(1, select: true, length: 2);
        f.Show();
        f.Hex.GoTo(4, select: true, length: 1);
        await f.Compute();
        Assert.Contains("selected range 0x1–0x2, 2 of 2 bytes read", f.Result);
        Assert.Contains("SHA-256: " + Hash(bytes[1..3]), f.Result);
    }

    [AvaloniaFact]
    public async Task An_early_end_is_visible_with_actual_count_and_full_partial_hash_without_clipboard_access()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        using var f = await Fixture.Create(bytes, new TestSource(bytes, reportedLength: 100));
        f.Show();
        await f.Compute();
        Assert.Contains("INCOMPLETE", f.Result);
        Assert.Contains("5 of 100 bytes read", f.Result);
        Assert.Contains("SHA-256: " + Hash(bytes), f.Result);
        Assert.Contains("incomplete", Field<TextBlock>(f.Viewer, "_status").Text);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
    }

    [AvaloniaTheory]
    [InlineData("stop")]
    [InlineData("escape_from_results")]
    [InlineData("source_changed")]
    [InlineData("read_error")]
    [InlineData("scope_moved")]
    public async Task Delayed_checksum_retains_scope_and_handles_cancellation_revision_and_read_failure(string action)
    {
        byte[] bytes = Enumerable.Range(0, 4 * PagedReader.PageSize).Select(i => (byte)(i % 251)).ToArray();
        var source = new TestSource(bytes);
        using var f = await Fixture.Create(bytes, source);
        // Navigation commits a current accessible row and can request its page before painting.
        // Arm before that legitimate demand, so both it and checksum work share the held read.
        source.Arm();
        if (action == "scope_moved") f.Hex.GoTo(TestSource.HeldOffset, select: true, length: 4096);
        f.Show();
        var work = f.Compute();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(work.IsCompleted);
            var stop = Field<Button>(f.Viewer, "_stopChecksum");
            Assert.True(stop.IsVisible);
            if (action == "stop") stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (action == "escape_from_results")
            {
                Field<TextBox>(f.Viewer, "_checksumResult").Focus();
                var key = new KeyEventArgs { Key = Key.Escape, RoutedEvent = InputElement.KeyDownEvent };
                f.Viewer.RaiseEvent(key);
                Assert.True(key.Handled);
                Assert.True(f.Viewer.IsVisible);
            }
            else if (action == "source_changed") source.Revision++;
            else if (action == "read_error") source.FailRead = true;
            else f.Hex.GoTo(0, select: true, length: 8);
            source.Release.Set();
            await work;
            Assert.False(stop.IsVisible);
            Assert.Null(typeof(ViewerWindow).GetField("_checksumCts", Fields)!.GetValue(f.Viewer));
            if (action is "stop" or "escape_from_results")
            {
                Assert.Contains("Checksum canceled", f.Result);
                Assert.DoesNotContain("SHA-256:", f.Result);
                Assert.False(Field<Button>(f.Viewer, "_copyChecksum").IsEnabled);
            }
            else if (action == "source_changed")
            {
                Assert.Contains("source changed while computing", f.Result);
                Assert.DoesNotContain("SHA-256:", f.Result);
            }
            else if (action == "read_error")
            {
                Assert.Contains("READ ERROR: deterministic source failure", f.Result);
                Assert.Contains("INCOMPLETE", f.Result);
                Assert.Contains($"{TestSource.HeldOffset:N0} of {bytes.Length:N0} bytes read", f.Result);
            }
            else
            {
                Assert.Contains("selected range 0x20000–0x20FFF", f.Result);
                Assert.Contains("SHA-256: " + Hash(bytes[(int)TestSource.HeldOffset..((int)TestSource.HeldOffset + 4096)]), f.Result);
            }
            Assert.Equal(ClipboardSentinel, await f.ClipboardText());
        }
        finally
        {
            source.Release.Set();
            await work;
        }
    }

    [AvaloniaFact]
    public async Task A_source_without_revision_evidence_does_not_claim_verified_current_content()
    {
        byte[] bytes = [1, 2, 3];
        using var f = await Fixture.Create(bytes, new TestSource(bytes) { RevisionKnown = false });
        f.Show();
        await f.Compute();
        Assert.Contains("SHA-256: " + Hash(bytes), f.Result);
        Assert.Contains("Revision unavailable: source consistency could not be verified", f.Result);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
    }

    [AvaloniaFact]
    public async Task A_failed_explicit_copy_keeps_the_selectable_result_and_explains_the_failure()
    {
        using var f = await Fixture.Create([1, 2, 3]);
        f.Show();
        await f.Compute();
        string result = f.Result;
        var clipboard = DispatchProxy.Create<IClipboard, FailedClipboard>();
        await f.Viewer.CopyChecksumToAsync(clipboard);
        Assert.Equal(result, f.Result);
        Assert.Contains("Checksum results could not be copied: clipboard busy", Field<TextBlock>(f.Viewer, "_status").Text);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
    }

    [AvaloniaFact]
    public async Task Unknown_source_length_cannot_be_reported_as_a_complete_empty_file_checksum()
    {
        using var f = await Fixture.Create([1, 2, 3], new TestSource([1, 2, 3], reportedLength: -1) { RevisionKnown = false });
        f.Show();
        await f.Compute();
        Assert.Contains("source length is unavailable", f.Result);
        Assert.DoesNotContain("SHA-256:", f.Result);
        Assert.Equal(ClipboardSentinel, await f.ClipboardText());
    }

    public class FailedClipboard : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IClipboard.SetDataAsync)
            ? Task.FromException(new IOException("clipboard busy")) : throw new NotSupportedException(method?.Name);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Fields)!.Invoke(owner, args);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Checksum checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public required string Root;
        public required AppServices Services;
        public required ViewerWindow Viewer;
        public HexView Hex => Field<HexView>(Viewer, "_hex");
        public ComboBox Scope => Field<ComboBox>(Viewer, "_checksumScope");
        public string Result => Viewer.ChecksumResultText;
        public static async Task<Fixture> Create(byte[] bytes, IContentSource? source = null, int width = 980)
        {
            string root = Path.Join(Path.GetTempPath(), "filecat-checksum-scope-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Join(root, "content.bin");
            File.WriteAllBytes(path, bytes);
            var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
            var viewer = new ViewerWindow(services, source ?? new FileContentSource(path), path, hex: true) { Width = width };
            viewer.Show();
            await Until(() => Field<DispatcherTimer>(viewer, "_changeTimer").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            await Until(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(Field<PagedReader>(viewer, "_reader"))! == 0);
            await ClipboardExtensions.SetTextAsync(viewer.Clipboard!, ClipboardSentinel);
            return new Fixture { Root = root, Services = services, Viewer = viewer };
        }
        public void Show() => Invoke(Viewer, "ShowChecksum");
        public Task Compute() => ((Task)Invoke(Viewer, "ChecksumAsync")!).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        public Task<string?> ClipboardText() => ClipboardExtensions.TryGetTextAsync(Viewer.Clipboard!);
        public void Dispose()
        {
            Viewer.Close();
            Services.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class TestSource(byte[] bytes, long? reportedLength = null) : IContentSource
    {
        public const long HeldOffset = 2 * PagedReader.PageSize;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        private int _armed;
        public long Revision = 1;
        public bool RevisionKnown = true, FailRead;
        public string DisplayName => "controlled checksum source";
        public long Length => reportedLength ?? bytes.LongLength;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => RevisionKnown ? new(Length, Interlocked.Read(ref Revision)) : null;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset == HeldOffset && Interlocked.Exchange(ref _armed, 0) != 0)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Checksum test did not release its source.");
            }
            if (FailRead && offset >= HeldOffset) throw new IOException("deterministic source failure");
            if (offset >= bytes.Length) return 0;
            int count = (int)Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public void Dispose() => Release.Set();
    }
}
