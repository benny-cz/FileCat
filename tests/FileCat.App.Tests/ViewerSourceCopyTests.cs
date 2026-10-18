using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>FCUX-076: actual window commands, clipboard contents, completeness and request ownership.</summary>
public sealed class ViewerSourceCopyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Text_and_Info_copy_source_or_displayed_characters_with_exact_logical_endings(bool info, bool wrap)
    {
        string original = "A\tB\u0001\r\nnext\n" + new string('x', 40_000) + "tail";
        byte[] bytes = Encoding.UTF8.GetBytes(original);
        using var f = await ViewerFixture.Open(bytes, hex: false);
        TextViewer view = f.Text;
        ViewerResourceEvidence.Record($"copy info={info} wrap={wrap}: text opened", view);
        if (info)
        {
            await f.Window.ShowInfoAsync();
            Invoke(f.Window, "ShowInfoText", original);
            view = Field<TextViewer>(f.Window, "_info");
            Assert.True(Field<CheckBox>(f.Window, "_wrap").IsVisible);
            Assert.True(Field<CheckBox>(f.Window, "_wrap").IsEnabled);
            ViewerResourceEvidence.Record($"copy info={info} wrap={wrap}: report replaced", view);
        }
        view.Wrap = wrap;
        await Wait(() => view.SourceGlyphBounds(0) is not null);
        ViewerResourceEvidence.Record($"copy info={info} wrap={wrap}: arranged before selection", view);
        view.Focus(); f.Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        ViewerResourceEvidence.Record($"copy info={info} wrap={wrap}: selected", view);
        Assert.Equal((0L, (long)bytes.Length), view.Selection);
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        Assert.Equal(original, await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Contains("original tabs and line endings", f.Notice.ResultText);
        ViewerResourceEvidence.Record($"copy info={info} wrap={wrap}: source copied", view);
        await f.Window.CopyContentAsync(displayed: true, explicitVisible: false);
        Assert.Equal("A   B␁\r\nnext\n" + new string('x', 40_000) + "tail", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Contains("displayed text", f.Notice.ResultText);
        Assert.Equal(bytes, f.Source.Bytes);
        ViewerResourceEvidence.Record($"copy info={info} wrap={wrap}: display copied", view);
    }

    [AvaloniaFact]
    public async Task Keyboard_default_Copy_is_source_text_and_the_named_alternative_is_displayed_text()
    {
        using var f = await ViewerFixture.Open(Encoding.UTF8.GetBytes("A\tB\r\nlast"), hex: false);
        await ClipboardExtensions.SetTextAsync(f.Window.Clipboard!, "before");
        f.Text.Focus();
        f.Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        f.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        await Wait(() => f.Notice.ResultText.StartsWith("Copied", StringComparison.Ordinal));
        Assert.Equal("A\tB\r\nlast", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        f.Window.KeyPress(Key.C, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.C, null);
        await Wait(() => f.Notice.ResultText.Contains("as displayed text", StringComparison.Ordinal));
        Assert.Equal("A   B\r\nlast", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
    }

    [AvaloniaFact]
    public async Task NUL_has_an_explicit_refusal_while_display_and_hex_represent_the_original_content()
    {
        byte[] bytes = [0x41, 0, 0x42, 1, 0x43, 0x7f];
        using var f = await ViewerFixture.Open(bytes, hex: false);
        f.Text.SetSelection(0, bytes.Length);
        await ClipboardExtensions.SetTextAsync(f.Window.Clipboard!, "preserve previous result");
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        Assert.Equal("preserve previous result", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Contains("U+0000", f.Notice.ResultText);
        Assert.Contains("Hex mode", f.Notice.ResultText);
        await f.Window.CopyContentAsync(displayed: true, explicitVisible: false);
        Assert.Equal("A·B␁C␡", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Invoke(f.Window, "SetMode", true);
        f.Hex.GoTo(0, select: true, bytes.Length);
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        Assert.Equal(Convert.ToHexString(bytes), await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Equal(bytes, f.Source.Bytes);
    }

    [AvaloniaTheory]
    [InlineData(1024 * 1024)]
    [InlineData(1024 * 1024 + 1)]
    [InlineData(2 * 1024 * 1024)]
    public async Task Viewer_hex_copy_states_selected_and_copied_counts_and_keeps_evidence_after_navigation(int count)
    {
        byte[] bytes = Enumerable.Range(0, count).Select(i => (byte)(i % 251)).ToArray();
        using var f = await ViewerFixture.Open(bytes, hex: true);
        f.Hex.GoTo(0, select: true, count);
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        Assert.Equal(Convert.ToHexString(bytes.AsSpan(0, Math.Min(count, ContentCopy.ByteLimit))), await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        string evidence = f.Notice.ResultText;
        Assert.Contains($"{count:N0}", evidence);
        if (count > ContentCopy.ByteLimit)
        {
            Assert.Contains($"first {ContentCopy.ByteLimit:N0} of {count:N0}", evidence);
            Assert.Contains("remaining bytes were not copied", evidence);
        }
        else Assert.DoesNotContain("limit", evidence, StringComparison.OrdinalIgnoreCase);
        f.Hex.GoTo(2);
        f.Hex.Focus(); f.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(f.Notice.IsVisible);
        Assert.Equal(evidence, f.Notice.ResultText);
        Assert.Equal(bytes, f.Source.Bytes);
    }

    [AvaloniaTheory]
    [InlineData(512 * 1024, false)]
    [InlineData(512 * 1024, true)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    public async Task A_short_read_or_failure_is_not_reported_as_a_complete_capped_copy(int readable, bool fail)
    {
        byte[] bytes = Enumerable.Repeat((byte)0x41, readable).ToArray();
        using var f = await ViewerFixture.Open(bytes, hex: true, reportedLength: 2 * ContentCopy.ByteLimit, failAfterBytes: fail);
        f.Hex.GoTo(0, select: true, 2 * ContentCopy.ByteLimit);
        await ClipboardExtensions.SetTextAsync(f.Window.Clipboard!, "previous result");
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        string evidence = f.Notice.ResultText;
        Assert.Contains("INCOMPLETE READING", evidence);
        Assert.Equal(readable == 0 ? "previous result" : Convert.ToHexString(bytes), await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        if (readable > 0)
        {
            Assert.Contains($"{readable:N0} of {2 * ContentCopy.ByteLimit:N0}", evidence);
            Assert.Contains("budget also limits", evidence);
        }
        Assert.Equal(bytes, f.Source.Bytes);
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("encoding")]
    [InlineData("mode")]
    [InlineData("source")]
    [InlineData("new_copy")]
    public async Task Delayed_source_Copy_cannot_publish_or_replace_current_results_after_its_scope_ends(string transition)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("current\tvalue\n" + new string('x', 4 * PagedReader.PageSize));
        using var f = await ViewerFixture.Open(bytes, hex: false);
        f.Source.HoldAt = 2 * PagedReader.PageSize;
        f.Text.SetSelection(0, bytes.Length);
        // The window releases its platform implementation on Close; retain the independently
        // captured clipboard service so the stale-publication assertion still inspects it afterward.
        var clipboard = Assert.IsAssignableFrom<IClipboard>(f.Window.Clipboard);
        await ClipboardExtensions.SetTextAsync(clipboard, "previous result");
        Task old = f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        await f.Source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(old.IsCompleted);
        switch (transition)
        {
            case "cancel": Field<CancellationTokenSource>(f.Window, "_copyCts").Cancel(); break;
            case "close": f.Window.Close(); break;
            case "encoding": f.Text.SetEncoding(Encoding.Latin1, 0); break;
            case "mode": Invoke(f.Window, "SetMode", true); break;
            case "source":
                f.Source.RevisionNumber++;
                f.Text.SourceChanged();
                break;
            case "new_copy":
                f.Text.SetSelection(0, 13);
                await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
                Assert.Equal("current\tvalue", await ClipboardExtensions.TryGetTextAsync(clipboard));
                break;
        }
        string clipboardBefore = (await ClipboardExtensions.TryGetTextAsync(clipboard))!;
        string noticeBefore = f.Notice.ResultText;
        f.Source.Release.Set();
        await old.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(clipboardBefore, await ClipboardExtensions.TryGetTextAsync(clipboard));
        if (transition is "new_copy" or "source" or "encoding" or "close") Assert.Equal(noticeBefore, f.Notice.ResultText);
        else Assert.Contains("canceled", f.Notice.ResultText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytes, f.Source.Bytes);
        Assert.Equal(0, f.Source.UiReads);
        Assert.Equal(0, f.Source.DisposedDuringRead);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_completed_background_refresh_cannot_rebind_old_selection_before_its_pending_UI_commit(bool toolbarCopy)
    {
        byte[] original = Encoding.UTF8.GetBytes("old selected content");
        byte[] replacement = Encoding.UTF8.GetBytes("new replaced content");
        Assert.Equal(original.Length, replacement.Length);
        using var f = await ViewerFixture.Open(original.ToArray(), hex: false);
        var clipboard = ClipboardIntentTests.ControlledClipboard.Create();
        await ClipboardExtensions.SetTextAsync(clipboard.Api, "previous clipboard result");
        var previousClipboard = clipboard.Current;
        int writesBefore = clipboard.WriteCount;
        f.Text.SetSelection(4, 12);
        Assert.Equal("selected", f.Text.GetSelectedOrVisibleText());
        var reader = Field<PagedReader>(f.Window, "_reader");
        var committedRevision = f.Text.CaptureCopyScope(false)!.Revision;
        long generation = f.Text.SourceGeneration;
        replacement.CopyTo(f.Source.Bytes, 0);
        f.Source.RevisionNumber++;
        f.Source.HoldRevision = 1;
        using var refreshCompleted = new ManualResetEventSlim();
        bool refreshed = false;
        Exception? refreshError = null;
        Task refresh = Task.Run(() =>
        {
            try { refreshed = reader.Refresh(TestContext.Current.CancellationToken); }
            catch (Exception ex) { refreshError = ex; }
            finally { refreshCompleted.Set(); }
        });
        await f.Source.RevisionEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        bool surfaceCallbackRan = false;
        using (Dispatcher.UIThread.DisableProcessing())
        {
            f.Source.RevisionRelease.Set();
            Assert.True(refreshCompleted.Wait(TimeSpan.FromSeconds(10)));
            Assert.Null(refreshError);
            Assert.True(refreshed);
            Dispatcher.UIThread.Post(() => { surfaceCallbackRan = true; f.Text.SourceChanged(); }, DispatcherPriority.Background);
            Assert.NotEqual(committedRevision, reader.Revision);
            Assert.Equal("selected", f.Text.GetSelectedOrVisibleText());
            Assert.Equal((4L, 8L), f.Text.Selection);
            // Both Ctrl+C and a toolbar that still displayed "Copy source selection" must refuse.
            Task copy = f.Window.CopyContentAsync(displayed: false, explicitVisible: toolbarCopy, targetClipboard: clipboard.Api);
            Assert.True(copy.IsCompletedSuccessfully);
            Assert.False(surfaceCallbackRan);
            Assert.False(f.Text.HasSelection);
            Assert.Equal(generation + 1, f.Text.SourceGeneration);
            Assert.Contains("file changed", f.Notice.ResultText, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(writesBefore, clipboard.WriteCount);
            Assert.Same(previousClipboard, clipboard.Current);
        }
        await refresh;
        await Wait(() => surfaceCallbackRan && f.Text.SemanticValue.Contains("new replaced content", StringComparison.Ordinal));
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false, targetClipboard: clipboard.Api);
        Assert.Equal(writesBefore, clipboard.WriteCount); // ordinary Copy still cannot silently become visible Copy
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: true, targetClipboard: clipboard.Api);
        Assert.Equal("new replaced content", await ClipboardExtensions.TryGetTextAsync(clipboard.Api));
        Assert.Contains("captured visible lines", f.Notice.ResultText);
        Assert.Equal(reader.Revision, f.Text.CaptureCopyScope(true)!.Revision);
        Assert.Equal(replacement, f.Source.Bytes);
        Assert.Equal(0, f.Source.UiReads);
    }

    [AvaloniaFact]
    public async Task Retired_text_selection_requires_an_explicit_visible_scope_before_Copy_can_replace_the_clipboard()
    {
        using var f = await ViewerFixture.Open(Encoding.UTF8.GetBytes("A\tB"), hex: false);
        f.Text.SetSelection(0, 3);
        f.Text.SetEncoding(Encoding.Latin1, 0);
        await ClipboardExtensions.SetTextAsync(f.Window.Clipboard!, "previous result");
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: false);
        Assert.Equal("previous result", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Contains("selection ended", f.Notice.ResultText);
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: true);
        Assert.Equal("A\tB", await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Contains("captured visible lines", f.Notice.ResultText);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_write_boundary_is_serialized_and_its_cancellation_limit_is_truthful(bool cancelOnly)
    {
        using var f = await ViewerFixture.Open(Encoding.UTF8.GetBytes("first second"), hex: false);
        var clipboard = ClipboardIntentTests.ControlledClipboard.Create();
        await ClipboardExtensions.SetTextAsync(clipboard.Api, "previous result");
        clipboard.HoldWrite();
        f.Text.SetSelection(0, 5);
        Task first = f.Window.CopyContentAsync(displayed: false, explicitVisible: false, clipboard.Api);
        await clipboard.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Task? second = null;
        if (cancelOnly) Field<CancellationTokenSource>(f.Window, "_copyCts").Cancel();
        else
        {
            f.Text.SetSelection(6, 12);
            second = f.Window.CopyContentAsync(displayed: false, explicitVisible: false, clipboard.Api);
            await Task.Delay(30, TestContext.Current.CancellationToken);
            Assert.Equal(2, clipboard.WriteCount); // seed + held first; the new result waits for publication ownership
            Assert.False(second.IsCompleted);
        }
        clipboard.ReleaseWrite();
        await first.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(cancelOnly ? "first" : "second", await ClipboardExtensions.TryGetTextAsync(clipboard.Api));
        Assert.Contains(cancelOnly ? "clipboard write began" : "Copied 6 bytes", f.Notice.ResultText);
        Assert.DoesNotContain("No new clipboard result", f.Notice.ResultText);
    }

    [AvaloniaFact]
    public async Task Clipboard_failure_keeps_a_readable_error_and_preserves_the_previous_value()
    {
        using var f = await ViewerFixture.Open(Encoding.UTF8.GetBytes("A\tB"), hex: false);
        var clipboard = ClipboardIntentTests.ControlledClipboard.Create();
        await ClipboardExtensions.SetTextAsync(clipboard.Api, "previous");
        clipboard.WriteFailure = new IOException("test clipboard unavailable");
        await f.Window.CopyContentAsync(displayed: false, explicitVisible: true, clipboard.Api);
        Assert.Equal("previous", await ClipboardExtensions.TryGetTextAsync(clipboard.Api));
        Assert.Contains("clipboard unavailable", f.Notice.ResultText);
        Assert.True(f.Notice.IsVisible);
    }

    [AvaloniaFact]
    public async Task Report_Copy_all_remains_complete_and_source_selection_reconciles_on_report_replacement()
    {
        string report = "A\tB\r\n" + new string('r', ContentCopy.ByteLimit + 10) + "tail";
        var window = new ReportWindow("copy report", "original subject", _ => Task.FromResult(report));
        window.Show();
        try
        {
            await window.Reading!;
            var view = Field<TextViewer>(window, "_view");
            await Wait(() => view.SourceGlyphBounds(0) is not null);
            await InvokeTask(window, "CopyAsync", true);
            Assert.Equal(report, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            var notice = Field<ContentCopyNotice>(window, "_copyNotice");
            Assert.Contains("complete original report", notice.ResultText);
            view.SetSelection(0, 6);
            await window.CopyTextAsync(displayed: false, explicitVisible: false);
            Assert.Equal("A\tB\r\nr", await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            await window.CopyTextAsync(displayed: true, explicitVisible: false);
            Assert.Equal("A   B\r\nr", await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Invoke(window, "Show", "replacement\tcurrent");
            await Wait(() => view.SourceGlyphBounds(0) is not null);
            string previous = (await ClipboardExtensions.TryGetTextAsync(window.Clipboard!))!;
            await window.CopyTextAsync(displayed: false, explicitVisible: false);
            Assert.Equal(previous, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.Contains("selection ended", notice.ResultText);
            await window.CopyTextAsync(displayed: false, explicitVisible: true);
            Assert.Equal("replacement\tcurrent", await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
        }
        finally { window.Close(); }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static Task InvokeTask(object owner, string name, params object?[] args) => (Task)Invoke(owner, name, args)!;
    private static async Task Wait(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Copy test checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ViewerFixture : IDisposable
    {
        private readonly string _root;
        private readonly AppServices _services;
        internal readonly TestSource Source;
        internal readonly ViewerWindow Window;
        internal TextViewer Text => Field<TextViewer>(Window, "_text");
        internal HexView Hex => Field<HexView>(Window, "_hex");
        internal ContentCopyNotice Notice => Field<ContentCopyNotice>(Window, "_copyNotice");
        private ViewerFixture(byte[] bytes, bool hex, long? reportedLength, bool failAfterBytes)
        {
            _root = Path.Join(Path.GetTempPath(), "filecat-viewer-source-copy-" + Guid.NewGuid().ToString("N"));
            _services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: _root));
            Source = new TestSource(bytes, reportedLength ?? bytes.Length, failAfterBytes);
            Window = new ViewerWindow(_services, Source, "copy-fixture.txt", hex, "viewer-source-copy-fixture");
        }
        internal static async Task<ViewerFixture> Open(byte[] bytes, bool hex, long? reportedLength = null, bool failAfterBytes = false)
        {
            var f = new ViewerFixture(bytes, hex, reportedLength, failAfterBytes);
            try
            {
                f.Window.Show();
                await Wait(() => Field<DispatcherTimer>(f.Window, "_changeTimer").IsEnabled);
                Field<DispatcherTimer>(f.Window, "_changeTimer").Stop();
                Invoke(f.Window, "SetMode", hex);
                if (!hex) await Wait(() => f.Text.SourceGlyphBounds(0) is not null || bytes.Length == 0);
                return f;
            }
            catch { f.Dispose(); throw; }
        }
        public void Dispose()
        {
            Source.Release.Set(); Window.Close(); _services.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestSource(byte[] bytes, long reportedLength, bool failAfterBytes) : IContentSource
    {
        internal byte[] Bytes => bytes;
        internal long HoldAt = -1;
        internal int RevisionNumber = 1;
        internal int HoldRevision;
        internal readonly TaskCompletionSource RevisionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim RevisionRelease = new();
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        internal int UiReads, DisposedDuringRead;
        private int _active;
        public string DisplayName => "controlled source-copy fixture";
        public long Length => reportedLength;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision()
        {
            if (Interlocked.Exchange(ref HoldRevision, 0) != 0)
            {
                RevisionEntered.TrySetResult();
                if (!RevisionRelease.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Metadata refresh was not released.");
            }
            return new(reportedLength, RevisionNumber);
        }
        public int Read(long offset, Span<byte> destination)
        {
            Interlocked.Increment(ref _active);
            if (Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref UiReads);
            try
            {
                if (offset == HoldAt)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Copy read was not released.");
                }
                if (offset >= bytes.Length && failAfterBytes) throw new IOException("Test source cannot read the remaining bytes.");
                int count = (int)Math.Min(destination.Length, Math.Max(0, bytes.Length - offset));
                if (count > 0) bytes.AsSpan((int)offset, count).CopyTo(destination);
                return count;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() { if (Volatile.Read(ref _active) > 0) Interlocked.Increment(ref DisposedDuringRead); Release.Set(); RevisionRelease.Set(); }
    }
}
