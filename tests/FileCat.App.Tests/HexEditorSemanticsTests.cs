using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

/// <summary>FCUX-012/073: editing-host eligibility and focus routes, through real key input and a durable stopped-save fixture.</summary>
public sealed class HexEditorSemanticsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Actual_hex_editing_exposes_pending_nibble_column_and_unsaved_state_and_returns_from_toolbar_and_search()
    {
        using var f = new EditorFixture();
        await Wait(() => f.Peer.Value!.Contains("00 01", StringComparison.Ordinal));
        var events = new List<AutomationPropertyChangedEventArgs>();
        f.Peer.PropertyChanged += (_, e) => events.Add(e);
        f.Hex.Focus();
        f.Window.KeyTextInput("A");
        Assert.Equal(10, f.Hex.PendingNibble);
        Assert.Contains("pending first nibble A", f.Peer.Value);
        f.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.Same(Field<Button>(f.Window, "_saveAs"), f.Window.FocusManager!.GetFocusedElement());
        f.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.Same(f.Hex, f.Window.FocusManager.GetFocusedElement());
        Assert.Equal(10, f.Hex.PendingNibble);
        f.Window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
        Assert.Same(Field<TextBox>(f.Window, "_search"), f.Window.FocusManager.GetFocusedElement());
        f.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Same(f.Hex, f.Window.FocusManager.GetFocusedElement());
        Assert.Equal(10, f.Hex.PendingNibble);
        f.Window.KeyTextInput("B");
        Assert.Contains("1 modified bytes, unsaved", f.Peer.Value);
        string footer = ControlAutomationPeer.CreatePeerForElement(Field<TextBlock>(f.Window, "_state")).GetName()!;
        Assert.Contains("modified (unsaved)", footer);
        if (ProtectedHexFile.ExcludesWriters) Assert.Contains("other programs cannot write", footer);
        else
        {
            Assert.Contains("advisory lock", footer);
            Assert.Contains("other programs may still write", footer);
            Assert.DoesNotContain("other programs cannot write", footer);
        }
        Assert.DoesNotContain("pending first nibble", f.Peer.Value);
        f.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.Same(f.Hex, f.Window.FocusManager.GetFocusedElement());
        Assert.True(f.Hex.TextColumnActive);
        Assert.Contains("active typing column: text (ASCII)", f.Peer.Value);
        f.Window.KeyTextInput("Z");
        Assert.Contains("2 modified bytes, unsaved", f.Peer.Value);
        f.Hex.GoTo(0, select: true, 2);
        Assert.Contains("Selected bytes 0 through 1, 2 bytes", f.Peer.Value);
        Assert.Contains(events, e => e.Property == ValuePatternIdentifiers.ValueProperty && e.NewValue is string text && text.Contains("pending first nibble A", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Property == AutomationElementIdentifiers.HelpTextProperty && e.NewValue is string text && text.Contains("text (ASCII)", StringComparison.Ordinal));
        Assert.Equal(f.Original, f.ReadOriginal());
    }

    [AvaloniaFact]
    public async Task At_minimum_editor_width_and_maximum_font_both_columns_can_edit_and_copy_every_byte_without_hiding_the_caret()
    {
        using var f = new EditorFixture();
        f.Window.Width = 620; f.Hex.FontSize = 32;
        f.Window.CaptureRenderedFrame()?.Dispose();
        Assert.InRange(f.Window.Bounds.Width, 619, 621);
        f.Hex.GoTo(0); f.Hex.Focus();
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(i, f.Hex.CursorOffset);
            AssertInside(f.Hex.ContentViewport, f.Hex.CursorBounds(textColumn: false));
            f.Window.KeyTextInput("AB");
        }
        f.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        for (int i = 16; i < 32; i++)
        {
            Assert.Equal(i, f.Hex.CursorOffset);
            AssertInside(f.Hex.ContentViewport, f.Hex.CursorBounds(textColumn: true));
            f.Window.KeyTextInput("Z");
        }
        f.Hex.GoTo(0, select: true, 32);
        f.Hex.TextColumnActive = false;
        await InvokeTask(f.Window, "CopyAsync");
        Assert.Equal(string.Join(' ', Enumerable.Repeat("AB", 16).Concat(Enumerable.Repeat("5A", 16))), await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        f.Hex.TextColumnActive = true;
        await InvokeTask(f.Window, "CopyAsync");
        Assert.Equal(new string('.', 16) + new string('Z', 16), await ClipboardExtensions.TryGetTextAsync(f.Window.Clipboard!));
        Assert.Contains("displayed ASCII", Field<ContentCopyNotice>(f.Window, "_clipboardNotice").ResultText);
        Assert.Equal(f.Original, f.ReadOriginal());
    }

    [AvaloniaFact]
    public async Task A_real_durable_interrupted_save_blocks_typing_and_F6_reaches_recovery_then_returns_with_state_intact()
    {
        using var f = new EditorFixture();
        f.Hex.Focus(); f.Window.KeyTextInput("AB");
        var file = Field<ProtectedHexFile>(f.Window, "_file");
        var overlay = Field<HexPatchOverlay>(f.Window, "_overlay");
        var stopped = await Task.Run(() => Assert.Throws<HexSaveInterruptedException>(() => HexSaveJournal.Save(file, overlay,
            f.Services.Paths.HexRecoveryDirectory, step => { if (step == 0) throw new IOException("Stopped after durable journal, before target write."); })));
        Invoke(f.Window, "ShowInterrupted", stopped.JournalPath, stopped.Message);
        f.Window.CaptureRenderedFrame()?.Dispose();
        Assert.Contains("Byte editing blocked", f.Peer.Value);
        Assert.Contains("interrupted save must be finished or restored", f.Peer.Value);
        Assert.Contains("1 modified bytes, unsaved", f.Peer.Value);
        long before = f.Hex.CursorOffset;
        f.Window.KeyTextInput("CD");
        Assert.Equal(before, f.Hex.CursorOffset);
        Assert.Equal(1, overlay.DirtyBytes);
        f.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        var focused = Assert.IsType<Button>(f.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("Finish saving", focused.Content);
        f.Window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.Same(f.Hex, f.Window.FocusManager.GetFocusedElement());
        Assert.Equal(before, f.Hex.CursorOffset);
        Assert.Contains("Byte editing blocked", f.Peer.Value);
        Assert.Equal(f.Original, f.ReadOriginal());
        Assert.Single(HexSaveJournal.Pending(f.Services.Paths.HexRecoveryDirectory));
    }

    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("replace")]
    [InlineData("close")]
    [InlineData("stop")]
    public async Task Edited_copy_refresh_retains_the_source_until_return_and_cannot_publish_after_its_owner_changes(string action)
    {
        using var f = new EditorFixture();
        var reader = Field<PagedReader>(f.Window, "_reader");
        await Wait(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Private)!.GetValue(reader)! == 0);
        f.Hex.Focus(); f.Window.KeyTextInput("AB");
        f.Hex.GoTo(0, select: true, 2);
        var clipboard = Assert.IsAssignableFrom<IClipboard>(f.Window.Clipboard);
        await ClipboardExtensions.SetTextAsync(clipboard, "earlier clipboard result");
        var held = new HeldRevisionSource(reader.Source);
        typeof(PagedReader).GetField("_source", Private)!.SetValue(reader, held);
        Task? copy = null;
        try
        {
            held.Arm();
            copy = InvokeTask(f.Window, "CopyAsync");
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(copy.IsCompleted);
            Assert.Equal(0, held.UiMetadataCalls);
            Assert.Equal(0, held.ReadsAfterArm);
            if (action == "edit") { f.Hex.GoTo(1); f.Hex.Focus(); f.Window.KeyTextInput("CD"); }
            else if (action == "replace") Invoke(f.Window, "SwitchTo", f.CreateReplacement(), "");
            else if (action == "close") f.Window.CloseNow();
            else
            {
                var stop = Assert.Single(Field<ContentCopyNotice>(f.Window, "_clipboardNotice").GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Stop copy"));
                stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            string status = Field<TextBlock>(f.Window, "_status").Text ?? "";
            Assert.Equal(0, held.DisposalsDuringCall);
            held.Release.Set();
            await copy.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("earlier clipboard result", await ClipboardExtensions.TryGetTextAsync(clipboard));
            Assert.Equal(status, Field<TextBlock>(f.Window, "_status").Text ?? "");
            Assert.Equal(0, held.ReadsAfterArm);
            Assert.Equal(0, held.DisposalsDuringCall);
            if (action != "close")
            {
                f.Hex.GoTo(0, select: true, 2);
                await InvokeTask(f.Window, "CopyAsync");
                Assert.Equal(action switch { "edit" => "AB CD", "replace" => "FE FD", _ => "AB 01" }, await ClipboardExtensions.TryGetTextAsync(clipboard));
            }
            Assert.Equal(f.Original, f.ReadOriginal());
        }
        finally
        {
            held.Release.Set();
            if (copy is not null) await copy.WaitAsync(TimeSpan.FromSeconds(10));
            f.Window.CloseNow();
            await Wait(() => held.Disposals == 1);
            held.Release.Dispose();
        }
    }

    private static void AssertInside(Rect viewport, Rect glyph)
        => Assert.True(glyph.Left >= viewport.Left - .5 && glyph.Right <= viewport.Right + .5 && glyph.Top >= viewport.Top - .5 && glyph.Bottom <= viewport.Bottom + .5,
            $"Editable byte {glyph} must remain in the actual viewport {viewport}.");
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Private)!.Invoke(owner, args);
    private static Task InvokeTask(object owner, string name, params object?[] args) => (Task)Invoke(owner, name, args)!;
    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Editor semantic checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
    private sealed class EditorFixture : IDisposable
    {
        private readonly string _root = Path.Join(Path.GetTempPath(), "filecat-editor-semantic-" + Guid.NewGuid().ToString("N"));
        private readonly string _path;
        internal readonly AppServices Services;
        internal readonly byte[] Original = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        internal readonly HexEditorWindow Window;
        internal HexView Hex => Field<HexView>(Window, "_hex");
        internal HexViewAutomationPeer Peer => (HexViewAutomationPeer)ControlAutomationPeer.CreatePeerForElement(Hex);
        internal EditorFixture()
        {
            Directory.CreateDirectory(_root);
            _path = Path.Join(_root, "editor-source.bin");
            File.WriteAllBytes(_path, Original);
            Services = AppServices.CreateForPaths(AppPaths.Resolve(dataRoot: Path.Join(_root, "state")));
            Assert.Null(HexEditorWindow.OpenOrActivate(Services, _path));
            Window = Assert.Single(HexEditorWindow.OpenWindows, window => window.DisplayName == _path);
        }
        internal byte[] ReadOriginal()
        {
            using var source = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[checked((int)source.Length)]; source.ReadExactly(bytes); return bytes;
        }
        internal string CreateReplacement()
        {
            string replacement = Path.Join(_root, "replacement.bin");
            File.WriteAllBytes(replacement, [0xfe, 0xfd, 0xfc]);
            return replacement;
        }
        public void Dispose() { Window.CloseNow(); Services.Dispose(); Directory.Delete(_root, recursive: true); }
    }

    private sealed class HeldRevisionSource(IContentSource inner) : IContentSource
    {
        private int _armed, _counting, _active, _reads, _disposals, _during, _ui;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        internal int ReadsAfterArm => Volatile.Read(ref _reads);
        internal int Disposals => Volatile.Read(ref _disposals);
        internal int DisposalsDuringCall => Volatile.Read(ref _during);
        internal int UiMetadataCalls => Volatile.Read(ref _ui);
        public string DisplayName => inner.DisplayName;
        public long Length => inner.Length;
        public bool CanSeek => inner.CanSeek;
        public string? LocalPath => inner.LocalPath;
        internal void Arm() { Interlocked.Exchange(ref _counting, 1); Interlocked.Exchange(ref _armed, 1); }
        public ContentRevision? GetRevision()
        {
            if (Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref _ui);
            Interlocked.Increment(ref _active);
            try
            {
                var revision = inner.GetRevision();
                if (Interlocked.Exchange(ref _armed, 0) != 0)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Editor Copy metadata was not released.");
                }
                return revision;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public int Read(long offset, Span<byte> buffer)
        {
            if (Volatile.Read(ref _counting) != 0) Interlocked.Increment(ref _reads);
            return inner.Read(offset, buffer);
        }
        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposals) != 1) return;
            if (Volatile.Read(ref _active) != 0) Interlocked.Increment(ref _during);
            inner.Dispose();
        }
    }
}
