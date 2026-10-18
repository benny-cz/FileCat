using System.Diagnostics;
using System.Collections;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>FCUX-012/073: actual glyph/viewport geometry, source selection, and event payloads before any paint.</summary>
public sealed class ViewerGeometryAndSemanticsTests
{
    public static TheoryData<int, int, bool> TextWidths
    {
        get
        {
            var data = new TheoryData<int, int, bool>();
            foreach (int width in new[] { 480, 980 })
                foreach (int font in new[] { 13, 18, 24, 32 })
                    foreach (bool report in new[] { false, true }) data.Add(width, font, report);
            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(TextWidths))]
    public async Task Unwrapped_text_and_Info_reveal_the_actual_far_glyph_at_every_supported_font(int width, int font, bool report)
    {
        string content = new string('x', 20_000) + "UNIQUE_SUFFIX" + new string('y', 20_000);
        using var f = new TextFixture(Encoding.UTF8.GetBytes(content), width, font, wrap: false, report);
        f.View.ScrollToOffset(20_000);
        await Wait(() => f.View.SourceGlyphBounds(20_000) is not null);
        Assert.InRange(f.Window.Bounds.Width, width - 1, width + 1);
        Assert.True(f.View.HorizontalOffset > 0);
        var target = Assert.IsType<Rect>(f.View.SourceGlyphBounds(20_000));
        AssertInside(f.View.ContentViewport, target);
        var visible = Assert.IsType<TextCopyScope>(f.View.CaptureCopyScope(true));
        Assert.InRange(20_000L, visible.Start, visible.Start + visible.Length - 1);
        Assert.True(f.View.ContentViewport.Width < f.Window.Bounds.Width); // real scrollbar clearance
        f.View.Focus();
        f.Window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
        Assert.Equal(0, f.View.HorizontalOffset);
        f.Window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        Assert.True(f.View.HorizontalOffset > 0);
        f.View.ScrollToOffset(20_000);
        target = Assert.IsType<Rect>(f.View.SourceGlyphBounds(20_000));
        AssertInside(f.View.ContentViewport, target);
        Click(f.Window, f.View, new Point(target.Left + .1, target.Center.Y));
        Assert.Equal((20_000L, 0L), f.View.Selection);
    }

    [AvaloniaTheory]
    [InlineData(13, false)]
    [InlineData(13, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public async Task Resolving_platform_aliases_keeps_the_selected_face_style_and_actual_shaped_font_size(int font, bool report)
    {
        var requested = new Typeface(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"),
            FontStyle.Normal, report ? FontWeight.Bold : FontWeight.Normal);
        var selected = requested.GlyphTypeface;
        const string original = "Heading Ω e\u0301 👩‍💻 אבג\tend";
        using var f = new TextFixture(Encoding.UTF8.GetBytes(original), 480, font, wrap: false, report);
        await Wait(() => f.View.SourceGlyphBounds(0) is not null);
        var actual = (Typeface)typeof(TextViewer).GetField(report ? "_bold" : "_typeface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.View)!;
        Assert.Equal(selected.FamilyName, actual.FontFamily.Name);
        Assert.Equal(requested.Weight, actual.Weight);
        Assert.Equal(requested.Style, actual.Style);
        Assert.Equal(requested.Stretch, actual.Stretch);
        Assert.Equal(selected.FamilyName, actual.GlyphTypeface.FamilyName);
        Assert.Equal(selected.FontSimulations, actual.GlyphTypeface.FontSimulations);
        Assert.Equal(selected.Weight, actual.GlyphTypeface.Weight);
        var runs = ViewerResourceEvidence.Runs(f.View).ToArray();
        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.Equal(font, run.GlyphRun.FontRenderingEmSize));
        f.View.SetSelection(0, f.Bytes.Length);
        Assert.Equal(original, await CopySource(f.View));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Horizontal_selection_geometry_keeps_each_visible_mixed_direction_run_and_bounds_the_work_before_measuring(bool report)
    {
        string original = new string('x', 400) + "abc אבג DEF مرحبا Ω e\u0301 👩‍💻 " + new string('z', 12_000);
        using var f = new TextFixture(Encoding.UTF8.GetBytes(original), 480, 24, wrap: false, report);
        await Wait(() => f.View.SourceGlyphBounds(0) is not null);
        f.View.SetSelection(0, f.Bytes.Length);
        foreach (string target in new[] { "א", "D", "م", "Ω", "e\u0301", "👩" })
        {
            long offset = Encoding.UTF8.GetByteCount(original.AsSpan(0, original.IndexOf(target, StringComparison.Ordinal)));
            f.View.ScrollToOffset(offset, preserveSelection: true);
            var glyph = Assert.IsType<Rect>(f.View.SourceGlyphBounds(offset));
            AssertInside(f.View.ContentViewport, glyph);
            var selection = (IReadOnlyList<Rect>)typeof(TextViewer).GetField("_selectionGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.View)!;
            Assert.Contains(selection, bounds => bounds.Contains(glyph.Center));
            Assert.InRange(f.View.DecorationCharactersMeasured, 1, 300);
            Assert.Equal((0L, (long)f.Bytes.Length), f.View.Selection);
        }
        Assert.Equal(original, await CopySource(f.View));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Pointer_selection_keeps_source_bytes_and_copy_through_resize_zoom_and_wrap(bool giant, bool tab)
    {
        string selected = tab ? "A\tBΩC" : "SELECT_ME";
        string prefix = new('p', giant ? 17_000 : 80);
        string original = prefix + selected + new string('s', 250);
        using var f = new TextFixture(Encoding.UTF8.GetBytes(original), 480, 13, wrap: true);
        long start = prefix.Length, end = start + Encoding.UTF8.GetByteCount(selected);
        f.View.ScrollToOffset(start);
        await Wait(() => f.View.SourceGlyphBounds(start) is not null && f.View.SourceGlyphBounds(end - 1) is not null);
        var a = f.View.SourceGlyphBounds(start)!.Value;
        var b = f.View.SourceGlyphBounds(end - 1)!.Value;
        Drag(f.Window, f.View, new Point(a.Left + .1, a.Center.Y), new Point(b.Right - .1, b.Center.Y));
        Assert.Equal((start, end - start), f.View.Selection);
        Assert.Equal(selected, await CopySource(f.View));
        foreach (var (width, font, wrap) in new[] { (980, 18, false), (480, 32, true), (980, 24, true), (480, 13, false) })
        {
            f.Resize(width);
            f.View.FontSize = font; f.View.Wrap = wrap;
            Assert.Equal((start, end - start), f.View.Selection);
            Assert.Equal(selected, await CopySource(f.View));
            Assert.Contains($"{end - start:N0} bytes", f.View.SemanticStatus);
        }
        Assert.Equal(Encoding.UTF8.GetBytes(original), f.Bytes);
    }

    [AvaloniaFact]
    public async Task Malformed_UTF8_pointer_hit_selects_B_at_original_byte_two_and_survives_reflow()
    {
        using var f = new TextFixture([0x41, 0x80, 0x42], 480, 18, wrap: false);
        await Wait(() => f.View.SourceGlyphBounds(2) is not null);
        var glyph = f.View.SourceGlyphBounds(2)!.Value;
        Drag(f.Window, f.View, new Point(glyph.Left + .1, glyph.Center.Y), new Point(glyph.Right - .1, glyph.Center.Y));
        Assert.Equal((2L, 1L), f.View.Selection);
        f.Resize(980); f.View.FontSize = 32; f.View.Wrap = true;
        Assert.Equal((2L, 1L), f.View.Selection);
        Assert.Equal("B", await CopySource(f.View));
    }

    [AvaloniaFact]
    public async Task Select_all_across_giant_line_segments_adds_no_newline_before_or_after_wrapping()
    {
        string content = new string('q', 16_383) + "\tΩ" + new string('z', 23_617);
        using var f = new TextFixture(Encoding.UTF8.GetBytes(content), 480, 13, wrap: false);
        await Wait(() => f.View.SourceGlyphBounds(0) is not null);
        f.View.Focus(); f.Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        Assert.Equal((0L, (long)f.Bytes.Length), f.View.Selection);
        Assert.Equal(content, await CopySource(f.View));
        f.View.Wrap = true; f.View.FontSize = 24; f.Resize(980);
        Assert.Equal(content, await CopySource(f.View));
        Assert.DoesNotContain("\n", await CopySource(f.View));
    }

    [AvaloniaFact]
    public async Task A_selected_tab_keeps_its_display_stop_after_the_selection_leaves_the_viewport()
    {
        using var f = new TextFixture(Encoding.UTF8.GetBytes("A\tB\r\nsecond line\nthird line"), 480, 13, wrap: true);
        await Wait(() => f.View.SourceGlyphBounds(1) is not null);
        f.View.SetSelection(1, 2);
        f.View.ScrollRows(1);
        Assert.Equal((1L, 1L), f.View.Selection);
        Assert.Null(f.View.SourceGlyphBounds(1));
        var scope = Assert.IsType<TextCopyScope>(f.View.CaptureCopyScope(false));
        var display = await Task.Run(() => ContentCopy.Read(scope.Reader, scope.Start, scope.Length,
            ContentCopyRepresentation.DisplayedText, scope.Encoding, scope.DisplayColumn, scope.Revision, TestContext.Current.CancellationToken));
        Assert.Equal("   ", display.Text);
        Assert.Equal("\t", await CopySource(f.View));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_common_search_term_in_a_giant_line_can_repaint_without_rescanning_every_atom_for_each_hit(bool wrap)
    {
        string content = new('x', 10_000);
        using var f = new TextFixture(Encoding.UTF8.GetBytes(content), 480, 13, wrap);
        await Wait(() => f.View.SourceGlyphBounds(0) is not null);
        var peer = (TextViewerAutomationPeer)ControlAutomationPeer.CreatePeerForElement(f.View);
        int semanticEvents = 0;
        peer.PropertyChanged += (_, _) => semanticEvents++;
        string semanticBefore = peer.Value!;
        f.View.SetHighlight("x", matchCase: true);
        for (int i = 0; i < 3; i++) f.Window.CaptureRenderedFrame()?.Dispose();
        AssertInside(f.View.ContentViewport, f.View.SourceGlyphBounds(0)!.Value);
        Assert.Equal(semanticBefore, peer.Value);
        Assert.Equal(0, semanticEvents);
        f.View.SetSelection(0, content.Length);
        Assert.Equal(content, await CopySource(f.View));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Long_report_segments_reuse_owned_shaping_across_height_changes_and_wrapped_row_navigation(bool wrap)
    {
        long openingAllocated = GC.GetAllocatedBytesForCurrentThread();
        using var f = new TextFixture(Encoding.UTF8.GetBytes("Header\n" + new string('x', 40_000)), 980, 13, wrap, report: true);
        await Wait(() => f.View.SourceGlyphBounds(7) is not null);
        f.Window.CaptureRenderedFrame()?.Dispose();
        // The source/page IO is deliberately off-thread. This guards UI shaping's prior multi-GiB
        // alias-synthesis amplification without charging unrelated parallel test/background work.
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - openingAllocated, 0, 256L * 1024 * 1024);
        ViewerResourceEvidence.Record("report initially arranged", f.View);
        int shaped = f.View.ShapedSegmentCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        foreach (int height in new[] { 340, 420, 380 })
        {
            f.Window.Height = height;
            f.Window.CaptureRenderedFrame()?.Dispose();
            Assert.InRange(f.Window.Bounds.Height, height - 1, height + 1);
            Assert.Equal(shaped, f.View.ShapedSegmentCount);
        }
        if (wrap)
        {
            f.View.ScrollRows(1); // leaves the short heading and reuses the already-visible giant segment
            f.View.ScrollRows(3);
            Assert.Equal(shaped, f.View.ShapedSegmentCount);
        }
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocated, 0, 64L * 1024 * 1024);
        ViewerResourceEvidence.Record("report repeated height/navigation", f.View);
        f.View.FontSize = 18;
        Assert.True(f.View.ShapedSegmentCount > shaped); // changed font metrics receive a new valid layout
        ViewerResourceEvidence.Record("report changed font", f.View);
        f.View.SetSelection(7, 40_007);
        f.Window.CaptureRenderedFrame()?.Dispose();
        int decorationBuilds = f.View.DecorationGeometryBuildCount;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5; i++) { f.View.InvalidateVisual(); f.Window.CaptureRenderedFrame()?.Dispose(); }
        Assert.Equal(decorationBuilds, f.View.DecorationGeometryBuildCount);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocated, 0, 16L * 1024 * 1024);
        Assert.Equal(new string('x', 40_000), await CopySource(f.View));
        ViewerResourceEvidence.Record("report selected repaint and exact copy", f.View);
    }

    [AvaloniaFact]
    public async Task A_tab_at_a_known_giant_segment_start_retains_its_display_column_through_navigation_and_reflow()
    {
        string prefix = "Ω" + new string('x', TextViewer.MaxLineBytes - 2);
        using var f = new TextFixture(Encoding.UTF8.GetBytes(prefix + "\tB tail"), 980, 13, wrap: false);
        await Wait(() => f.View.SourceGlyphBounds(TextViewer.MaxLineBytes) is not null);
        f.View.SetSelection(TextViewer.MaxLineBytes, TextViewer.MaxLineBytes + 1);
        var first = Assert.IsType<TextCopyScope>(f.View.CaptureCopyScope(false));
        Assert.Equal(3, first.DisplayColumn); // UTF-8 bytes and decoded columns have different lengths
        f.View.ScrollToOffset(TextViewer.MaxLineBytes, preserveSelection: true);
        f.View.Wrap = true; f.View.FontSize = 18;
        var current = Assert.IsType<TextCopyScope>(f.View.CaptureCopyScope(false));
        Assert.Equal(3, current.DisplayColumn);
        var displayed = await Task.Run(() => ContentCopy.Read(current.Reader, current.Start, current.Length,
            ContentCopyRepresentation.DisplayedText, current.Encoding, current.DisplayColumn, current.Revision, TestContext.Current.CancellationToken));
        Assert.Equal(" ", displayed.Text);
        Assert.Equal("\t", await CopySource(f.View));
    }

    [AvaloniaFact]
    public async Task Horizontal_navigation_exposes_current_source_context_beyond_the_initial_accessible_excerpt()
    {
        using var f = new TextFixture(Encoding.UTF8.GetBytes(new string('x', 14_000) + "CURRENT_SOURCE_SUFFIX"), 480, 13, wrap: false);
        await Wait(() => f.View.SourceGlyphBounds(0) is not null);
        var peer = (TextViewerAutomationPeer)ControlAutomationPeer.CreatePeerForElement(f.View);
        Assert.DoesNotContain("CURRENT_SOURCE_SUFFIX", peer.Value);
        var values = new List<string>();
        peer.PropertyChanged += (_, e) => { if (e.Property == ValuePatternIdentifiers.ValueProperty && e.NewValue is string text) values.Add(text); };
        f.View.ScrollToOffset(14_000);
        AssertInside(f.View.ContentViewport, f.View.SourceGlyphBounds(14_000)!.Value);
        Assert.Contains("CURRENT_SOURCE_SUFFIX", peer.Value);
        Assert.Contains(values, text => text.Contains("CURRENT_SOURCE_SUFFIX", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Source_and_encoding_replacement_end_selection_without_implicit_viewport_copy()
    {
        using var f = new TextFixture(Encoding.UTF8.GetBytes("A\tB selected"), 480, 13, wrap: true);
        await Wait(() => f.View.SourceGlyphBounds(0) is not null);
        f.View.Focus(); f.Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
        f.View.SetEncoding(Encoding.Latin1, 0);
        Assert.False(f.View.HasSelection);
        Assert.Contains("encoding changed", f.View.SelectionEnded);
        Assert.Null(f.View.CaptureCopyScope(false));
        Assert.NotNull(f.View.CaptureCopyScope(true));
        f.View.SetSelection(2, 3);
        using var next = new PagedReader(new MemoryContentSource("replacement", Encoding.UTF8.GetBytes("another file")));
        f.View.SetReader(next, Encoding.UTF8, 0);
        await Wait(() => f.View.SemanticValue.Contains("another file", StringComparison.Ordinal));
        Assert.Contains("source changed", f.View.SelectionEnded);
        Assert.Null(f.View.CaptureCopyScope(false));
        Assert.Equal("another file", await CopySource(f.View, explicitVisible: true));
        f.View.SetReader(null, Encoding.UTF8, 0);
    }

    public static TheoryData<int, int, bool> HexWidths
    {
        get
        {
            var data = new TheoryData<int, int, bool>();
            foreach (int font in new[] { 13, 18, 24, 32 })
            {
                data.Add(480, font, false); data.Add(980, font, false);
                data.Add(620, font, true); data.Add(980, font, true);
            }
            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(HexWidths))]
    public async Task Every_hex_byte_and_text_column_stays_reachable_beyond_four_GiB(int width, int font, bool editor)
    {
        using var reader = new PagedReader(new VirtualSource((1L << 32) + 4096));
        var view = new HexView { TracksActiveColumn = editor, FontSize = font };
        var window = new Window { Width = width, Height = 340, Content = view };
        view.SetReader(reader); window.Show();
        try
        {
            await Wait(() => view.Bounds.Width > 0);
            Assert.InRange(window.Bounds.Width, width - 1, width + 1);
            long row = (1L << 32) + 48;
            foreach (bool text in new[] { false, true })
            {
                // The public editing-column action and pointer choice use this same geometry.
                view.TextColumnActive = text;
                view.GoTo(row); view.Focus();
                for (int col = 0; col < 16; col++)
                {
                    Assert.Equal(row + col, view.CursorOffset);
                    var caret = view.CursorBounds(text);
                    AssertInside(view.ContentViewport, caret);
                    if (col < 15) window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                }
                Rect last = view.CursorBounds(text);
                Click(window, view, last.Center);
                Assert.Equal(row + 15, view.CursorOffset);
                Assert.Equal(text, view.TextColumnActive);
                window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
                Assert.Equal((row + 14, 2L), view.Selection);
                Assert.Contains($"{row + 14:N0}", view.SemanticStatus);
            }
        }
        finally { view.SetReader(null); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Content_events_commit_new_rows_before_notification_without_waiting_for_paint()
    {
        using var f = new TextFixture(Encoding.UTF8.GetBytes("first unique\nsecond unique\nthird unique"), 480, 13, wrap: true);
        await Wait(() => f.View.SemanticValue.Contains("first unique", StringComparison.Ordinal));
        var peer = (TextViewerAutomationPeer)ControlAutomationPeer.CreatePeerForElement(f.View);
        var events = new List<(string? Event, string? Getter)>();
        peer.PropertyChanged += (_, e) => { if (e.Property == ValuePatternIdentifiers.ValueProperty) events.Add((e.NewValue as string, peer.Value)); };
        f.View.ScrollRows(1); // deliberately no CaptureRenderedFrame or dispatcher run before these assertions
        Assert.DoesNotContain("first unique", peer.Value);
        Assert.Contains("second unique", peer.Value);
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(e.Event, e.Getter));
        Assert.All(events, e => Assert.DoesNotContain("first unique", e.Event));
        int count = events.Count;
        for (int i = 0; i < 5; i++) { f.View.InvalidateVisual(); f.Window.CaptureRenderedFrame()?.Dispose(); }
        Assert.Equal(count, events.Count);
        f.View.SetSelection(13, 19);
        Assert.Contains("Selected source bytes", peer.GetHelpText());
        Assert.True(events.Count > count);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_reentrant_source_replacement_or_clear_cannot_leave_old_rows_or_Copy_scope_authoritative(bool clear, bool automationEvent)
    {
        using var f = new TextFixture(Encoding.UTF8.GetBytes("first old\nsecond old\nthird old"), 480, 13, wrap: true);
        using var replacement = new PagedReader(new MemoryContentSource("replacement", Encoding.UTF8.GetBytes("replacement current content")));
        await Wait(() => f.View.SemanticValue.Contains("first old", StringComparison.Ordinal));
        var peer = (TextViewerAutomationPeer)ControlAutomationPeer.CreatePeerForElement(f.View);
        bool changed = false;
        void Replace()
        {
            if (changed) return;
            changed = true;
            f.View.SetReader(clear ? null : replacement, Encoding.UTF8, 0);
            Assert.DoesNotContain("old", peer.Value);
            Assert.Equal("", f.View.GetSelectedOrVisibleText());
            Assert.Null(f.View.CaptureCopyScope(explicitVisible: true));
        }
        if (automationEvent) peer.PropertyChanged += (_, e) => { if (e.Property == ValuePatternIdentifiers.ValueProperty) Replace(); };
        else f.View.ContentChanged += Replace;
        f.View.ScrollRows(1);
        Assert.True(changed);
        Assert.DoesNotContain("old", peer.Value);
        if (clear)
        {
            Assert.Equal("No content", peer.Value);
            Assert.Null(f.View.CaptureCopyScope(true));
        }
        else
        {
            await Wait(() => peer.Value!.Contains("replacement current content", StringComparison.Ordinal));
            var current = Assert.IsType<TextCopyScope>(f.View.CaptureCopyScope(true));
            Assert.Same(replacement, current.Reader);
            Assert.Equal("replacement current content", await CopySource(f.View));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_delayed_page_announces_only_current_source_content_and_never_reads_in_a_getter(bool replace)
    {
        using var source = new HeldSource(Encoding.UTF8.GetBytes("old delayed page"));
        using var reader = new PagedReader(source);
        using var current = new PagedReader(new MemoryContentSource("new", Encoding.UTF8.GetBytes("new current page")));
        var view = new TextViewer();
        view.SetReader(reader, Encoding.UTF8, 0);
        var window = new Window { Width = 480, Height = 200, Content = view };
        var peer = (TextViewerAutomationPeer)ControlAutomationPeer.CreatePeerForElement(view);
        var events = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, e) => events.Add(e);
        Assert.Equal(0, source.Reads); // no zero-width eager shaping or getter-triggered source reads
        window.Show();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Contains("Loading", peer.Value);
            for (int i = 0; i < 100; i++) { _ = peer.Value; _ = peer.GetHelpText(); }
            Assert.Equal(1, source.Reads);
            if (replace)
            {
                view.SetReader(current, Encoding.UTF8, 0);
                await Wait(() => peer.Value!.Contains("new current page", StringComparison.Ordinal));
                events.Clear();
            }
            source.Release.Set();
            await source.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (!replace) await Wait(() => peer.Value!.Contains("old delayed page", StringComparison.Ordinal));
            else { await Task.Delay(30, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal(replace ? "new current page" : "old delayed page", peer.Value);
            Assert.Equal(0, source.UiReads);
            if (replace) Assert.DoesNotContain(events, e => e.NewValue is string s && s.Contains("old delayed", StringComparison.Ordinal));
            else Assert.Contains(events, e => e.Property == ValuePatternIdentifiers.ValueProperty && e.NewValue is string s && s.Contains("old delayed page", StringComparison.Ordinal));
        }
        finally { source.Release.Set(); view.SetReader(null, Encoding.UTF8, 0); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Hex_page_selection_column_nibble_unsaved_and_blocked_state_publish_without_repaint_floods()
    {
        using var source = new HeldSource([0x41, 0x42, 0x43, 0x44]);
        using var reader = new PagedReader(source);
        var view = new HexView { TracksActiveColumn = true };
        var peer = (HexViewAutomationPeer)ControlAutomationPeer.CreatePeerForElement(view);
        var events = new List<AutomationPropertyChangedEventArgs>();
        peer.PropertyChanged += (_, e) => events.Add(e);
        view.SetReader(reader);
        var window = new Window { Width = 620, Height = 200, Content = view };
        window.Show();
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Contains("not loaded", peer.Value);
            source.Release.Set();
            await Wait(() => peer.Value!.Contains("41 42 43 44", StringComparison.Ordinal));
            Assert.Contains(events, e => e.Property == ValuePatternIdentifiers.ValueProperty && e.NewValue is string s && s.Contains("byte 41", StringComparison.Ordinal));
            view.GoTo(1, select: true, length: 2);
            view.TextColumnActive = true;
            view.PendingNibble = 0xA;
            view.SetEditingState(canEdit: false, unsavedBytes: 7, "An interrupted save needs recovery");
            Assert.True(((IValueProvider)peer).IsReadOnly); // the descriptive string is not a binary SetValue API
            Assert.Contains("Selected bytes 1 through 2, 2 bytes", peer.Value);
            Assert.Contains("text (ASCII)", peer.Value);
            Assert.Contains("pending first nibble A", peer.Value);
            Assert.Contains("7 modified bytes, unsaved", peer.Value);
            Assert.Contains("editing blocked", peer.Value);
            Assert.Contains("interrupted save", peer.GetHelpText());
            Assert.Throws<InvalidOperationException>(() => ((IValueProvider)peer).SetValue("overwrite"));
            int count = events.Count;
            for (int i = 0; i < 10; i++) { view.ContentChanged(); view.InvalidateVisual(); window.CaptureRenderedFrame()?.Dispose(); _ = peer.Value; }
            Assert.Equal(count, events.Count);
            Assert.Equal(0, source.UiReads);
        }
        finally { source.Release.Set(); view.SetReader(null); window.Close(); }
    }

    private static async Task<string> CopySource(TextViewer view, bool explicitVisible = false)
    {
        var scope = Assert.IsType<TextCopyScope>(view.CaptureCopyScope(explicitVisible));
        var result = await Task.Run(() => ContentCopy.Read(scope.Reader, scope.Start, scope.Length, ContentCopyRepresentation.SourceText,
            scope.Encoding, scope.DisplayColumn, scope.Revision, TestContext.Current.CancellationToken));
        Assert.True(result.CanPublish, result.Describe());
        Assert.False(result.ShortRead);
        return result.Text;
    }
    private static void AssertInside(Rect viewport, Rect glyph)
    {
        Assert.True(glyph.Width > 0 && glyph.Height > 0, glyph.ToString());
        Assert.True(glyph.Left >= viewport.Left - .5 && glyph.Right <= viewport.Right + .5 && glyph.Top >= viewport.Top - .5 && glyph.Bottom <= viewport.Bottom + .5,
            $"Glyph {glyph} must be in the actual viewport {viewport}.");
    }
    private static void Click(Window window, Control control, Point local)
    {
        Point point = control.TranslatePoint(local, window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void Drag(Window window, Control control, Point first, Point last)
    {
        Point a = control.TranslatePoint(first, window)!.Value, b = control.TranslatePoint(last, window)!.Value;
        window.MouseDown(a, MouseButton.Left); window.MouseMove(b, RawInputModifiers.LeftMouseButton); window.MouseUp(b, MouseButton.Left);
    }
    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Viewer geometry/content checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
    private sealed class TextFixture : IDisposable
    {
        internal readonly byte[] Bytes;
        internal readonly PagedReader Reader;
        internal readonly TextViewer View;
        internal readonly Window Window;
        internal TextFixture(byte[] bytes, int width, int font, bool wrap, bool report = false)
        {
            Bytes = bytes;
            Reader = new PagedReader(new MemoryContentSource("text fixture", bytes));
            View = new TextViewer { Wrap = wrap, FontSize = font, ReportStyle = report };
            View.SetReader(Reader, Encoding.UTF8, 0);
            Window = new Window { Width = width, Height = 380, Content = View };
            Window.Show();
        }
        internal void Resize(int width)
        {
            Window.Width = width;
            Dispatcher.UIThread.RunJobs();
            Window.CaptureRenderedFrame()?.Dispose();
            Assert.InRange(Window.Bounds.Width, width - 1, width + 1);
        }
        public void Dispose() { View.SetReader(null, Encoding.UTF8, 0); Window.Close(); Reader.Dispose(); }
    }
    private sealed class VirtualSource(long length) : IContentSource
    {
        public string DisplayName => "virtual greater-than-four-GiB source";
        public long Length => length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(length, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, Math.Max(0, length - offset));
            for (int i = 0; i < count; i++) buffer[i] = (byte)((offset + i) % 251);
            return count;
        }
        public void Dispose() { }
    }
    private sealed class HeldSource(byte[] bytes) : IContentSource
    {
        private int _reads, _uiReads;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        internal int Reads => Volatile.Read(ref _reads);
        internal int UiReads => Volatile.Read(ref _uiReads);
        public string DisplayName => "held semantic source";
        public long Length => bytes.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(bytes.Length, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            if (Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref _uiReads);
            Entered.TrySetResult();
            try
            {
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Semantic source was not released.");
                int count = (int)Math.Min(buffer.Length, Math.Max(0, bytes.Length - offset));
                bytes.AsSpan((int)offset, count).CopyTo(buffer);
                return count;
            }
            finally { Completed.TrySetResult(); }
        }
        public void Dispose() { Release.Set(); }
    }
}

/// <summary>Retained in TRX for the long-line resource acceptance cases, without changing product behavior.</summary>
internal static class ViewerResourceEvidence
{
    internal static void Record(string checkpoint, TextViewer view)
    {
        using var process = Process.GetCurrentProcess();
        var memory = GC.GetGCMemoryInfo();
        TestContext.Current.TestOutputHelper?.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            checkpoint,
            view.ShapedSegmentCount,
            view.DecorationGeometryBuildCount,
            view.CachedTextRunCount,
            view.CachedTextLineCount,
            FontEvidence = DescribeFonts(view),
            AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true),
            memory.HeapSizeBytes,
            memory.TotalCommittedBytes,
            process.WorkingSet64,
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2),
            DrawingConfiguration = "HeadlessTestAppBuilder default drawing; native rendering is a separate acceptance gate",
        }));
    }

    internal static IEnumerable<ShapedTextRun> Runs(TextViewer view)
    {
        var cache = (IEnumerable)typeof(TextViewer).GetField("_segmentLayouts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        return cache.Cast<object>().Select(entry => entry.GetType().GetProperty("Value")!.GetValue(entry)!)
            .Select(entry => (TextLayout)entry.GetType().GetProperty("Layout")!.GetValue(entry)!)
            .SelectMany(layout => layout.TextLines).SelectMany(line => line.TextRuns).OfType<ShapedTextRun>();
    }

    private static object[] DescribeFonts(TextViewer view)
        => Runs(view).Take(8)
            .Select(run => (object)new
            {
                run.Length,
                Glyphs = run.GlyphRun.GlyphInfos.Count,
                RequestedFamily = run.Properties.Typeface.FontFamily.Name,
                RequestedWeight = run.Properties.Typeface.Weight,
                run.ShapedBuffer.GlyphTypeface.FamilyName,
                run.ShapedBuffer.GlyphTypeface.Weight,
                run.ShapedBuffer.GlyphTypeface.FontSimulations,
            }).ToArray();
}
