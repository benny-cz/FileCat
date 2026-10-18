using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Controls;

/// <summary>
/// Bounded, paged text inspection. Selection belongs to decoder-consumed source bytes, while each visible
/// segment maps those bytes to shaped display positions. Reflow does not change the selected source.
/// Layout and semantic state are committed before events; painting never reads or changes content state.
/// </summary>
public sealed class TextViewer : Control
{
    internal const int MaxLineBytes = 16 * 1024;
    private const double Gutter = 8;
    private readonly ScrollBar _vbar, _hbar;
    private readonly WheelRowAccumulator _wheelRows = new();
    private PagedReader? _reader;
    private Action? _pageLoaded;
    private Encoding _encoding = new UTF8Encoding(false);
    private int _unit = 1;
    private long _start, _topLine;
    private int _topSub;
    private long? _targetOffset, _selStart, _selEnd;
    private int _selStartColumn, _selEndColumn;
    private string? _selectionEnded;
    private long _sourceGeneration, _readerAttachment;
    private readonly Dictionary<long, (long Start, int Column)> _previousSegments = [];
    private ContentRevision? _observedRevision;
    private long _observedLength;
    private bool _wrap = true, _updatingScroll, _committing, _commitPending, _layoutIncomplete;
    private readonly DispatcherTimer _barSeekTimer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private long? _pendingBarOffset;
    private string? _layoutError;
    private double _horizontalOffset;
    private Size _viewportSize;
    private readonly List<Row> _rows = [];
    private readonly List<TextLayout> _layouts = [];
    private readonly Dictionary<(long Offset, int Column), SegmentLayout> _segmentLayouts = [];
    private long _layoutFormat, _cachedFormat = -1;
    private double _cachedWrapWidth = -1;
    private readonly Dictionary<TextSourceSegment, (int Start, int End)[]> _highlightRanges = [];
    private readonly List<Rect> _selectionGeometry = [], _highlightGeometry = [];
    private Typeface _typeface = new(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"));
    private Typeface _bold = new(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"), FontStyle.Normal, FontWeight.Bold);
    private double _fontSize = 13, _charWidth = 8, _rowHeight = 18;
    private string? _highlight;
    private bool _highlightCase;
    private IBrush _text = Brushes.Black, _muted = Brushes.Gray, _bg = Brushes.White, _hit = Brushes.Yellow, _hitText = Brushes.Black, _sel = Brushes.LightBlue, _selectionText = Brushes.Black;
    private IBrush _accent = Brushes.SteelBlue, _warning = Brushes.DarkOrange;
    private TextViewerAutomationPeer? _automationPeer;
    private string _semanticValue = "No content", _semanticStatus = "No content";
    private bool _reportStyle, _fontsResolved;

    private sealed record SegmentLayout(TextSourceSegment Segment, TextLayout Layout, string? ReadError)
    {
        internal TextLayout? SelectionLayout { get; set; }
        internal TextLayout? HighlightLayout { get; set; }
        internal void Dispose() { Layout.Dispose(); SelectionLayout?.Dispose(); HighlightLayout?.Dispose(); }
    }
    internal int ShapedSegmentCount { get; private set; }
    internal int DecorationGeometryBuildCount { get; private set; }
    internal int DecorationCharactersMeasured { get; private set; }
    internal int CachedTextRunCount => _segmentLayouts.Values.Sum(cached => cached.Layout.TextLines.Sum(line => line.TextRuns.Count));
    internal int CachedTextLineCount => _segmentLayouts.Values.Sum(cached => cached.Layout.TextLines.Count);

    private sealed record Row(TextSourceSegment Segment, TextLine Line, int Sub, SegmentLayout Cached)
    {
        internal int First => Line.FirstTextSourceIndex;
        internal int End => Math.Min(Segment.Display.Length, First + Line.Length - Line.NewLineLength);
        internal long StartByte => Segment.ByteAtDisplay(First);
        internal long EndByte => Segment.ByteAtDisplay(End, trailing: true);
    }

    public TextViewer()
    {
        Focusable = true;
        ClipToBounds = true;
        _vbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Vertical, AllowAutoHide = false };
        _hbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Horizontal, AllowAutoHide = false, IsVisible = false };
        AutomationProperties.SetName(_vbar, "Text vertical position");
        AutomationProperties.SetName(_hbar, "Text horizontal position");
        _vbar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_updatingScroll && _reader is not null)
            {
                _wheelRows.Reset();
                QueueBarSeek((long)_vbar.Value);
            }
        };
        _barSeekTimer.Tick += (_, _) =>
        {
            _barSeekTimer.Stop();
            if (_pendingBarOffset is not { } latest || _reader is null) return;
            _pendingBarOffset = null;
            ScrollToOffset(latest, preserveSelection: true);
        };
        _hbar.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || _updatingScroll) return;
            _horizontalOffset = _hbar.Value;
            PublishState(); InvalidateVisual(); PositionChanged?.Invoke();
        };
        VisualChildren.Add(_vbar); LogicalChildren.Add(_vbar);
        VisualChildren.Add(_hbar); LogicalChildren.Add(_hbar);
    }

    public event Action? PositionChanged;
    /// <summary>Current source, copy scope or committed content changed; does not imply manual navigation.</summary>
    public event Action? ContentChanged;
    public long TopOffset => _rows.Count > 0 ? _rows[0].StartByte : _topLine;
    public long ContentStart => _start;
    public Encoding Encoding => _encoding;
    public bool HasSelection => _selStart is { } a && _selEnd is { } b && a != b;
    public (long Start, long Length) Selection => _selStart is { } a && _selEnd is { } b ? (Math.Min(a, b), Math.Abs(b - a)) : (TopOffset, 0);
    internal long SourceGeneration => _sourceGeneration;
    internal PagedReader? Reader => _reader;
    internal string? SelectionEnded => _selectionEnded;
    internal double HorizontalOffset => _horizontalOffset;
    internal Rect ContentViewport => new(0, 0, Math.Max(0, _viewportSize.Width - VerticalBarWidth), Math.Max(0, _viewportSize.Height - HorizontalBarHeight));
    internal string SemanticValue => _semanticValue;
    internal string SemanticStatus => _semanticStatus;

    public bool ReportStyle
    {
        get => _reportStyle;
        set { if (_reportStyle == value) return; _wheelRows.Reset(); _reportStyle = value; _layoutFormat++; CommitLayout(); }
    }

    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value) return;
            _wheelRows.Reset();
            CancelPendingBarSeek();
            long anchor = VisibleAnchor();
            _wrap = value; _hbar.IsVisible = !value;
            _layoutFormat++;
            _targetOffset = anchor; _horizontalOffset = 0;
            InvalidateMeasure(); CommitLayout();
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            double size = Math.Clamp(value, 8, 32);
            if (_fontSize == size) return;
            _wheelRows.Reset();
            CancelPendingBarSeek();
            long anchor = VisibleAnchor();
            _fontSize = size; MeasureFont(); _targetOffset = anchor;
            _layoutFormat++;
            CommitLayout();
        }
    }

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => _automationPeer = new TextViewerAutomationPeer(this);

    public void SetReader(PagedReader? reader, Encoding encoding, int preambleLength)
    {
        _wheelRows.Reset();
        CancelPendingBarSeek();
        if (_reader is not null && _pageLoaded is not null) _reader.PageLoaded -= _pageLoaded;
        RetireSelection("The source changed; the previous selection ended. Select again, or explicitly choose Copy visible source text.");
        _sourceGeneration++;
        _layoutFormat++;
        long attachment = ++_readerAttachment;
        _previousSegments.Clear();
        _reader = reader; _observedRevision = reader?.Revision; _observedLength = reader?.Length ?? 0;
        _encoding = encoding; _unit = TextDecoding.UnitSize(encoding);
        _start = Math.Clamp(preambleLength, 0, Math.Max(0, _observedLength));
        _topLine = _start; _topSub = 0; _targetOffset = null; _horizontalOffset = 0;
        if (reader is not null)
        {
            int queued = 0;
            _pageLoaded = () =>
            {
                if (Interlocked.Exchange(ref queued, 1) != 0) return;
                Dispatcher.UIThread.Post(() =>
                {
                    Interlocked.Exchange(ref queued, 0);
                    if (ReferenceEquals(reader, _reader) && attachment == _readerAttachment &&
                        (_layoutIncomplete || _targetOffset is not null || reader.Revision != _observedRevision || reader.Length != _observedLength)) CommitLayout();
                }, DispatcherPriority.Background);
            };
            reader.PageLoaded += _pageLoaded;
        }
        else _pageLoaded = null;
        MeasureFont(); CommitLayout();
    }

    public void SetEncoding(Encoding encoding, int preambleLength)
    {
        if (_encoding.CodePage == encoding.CodePage && _start == preambleLength) return;
        CancelPendingBarSeek();
        RetireSelection("The encoding changed; the previous selection ended. Select again, or explicitly choose Copy visible source text.");
        _sourceGeneration++; _layoutFormat++; _previousSegments.Clear();
        _encoding = encoding; _unit = TextDecoding.UnitSize(encoding);
        _start = Math.Clamp(preambleLength, 0, Math.Max(0, _reader?.Length ?? 0));
        _topLine = Math.Clamp(Align(_topLine), _start, Math.Max(_start, _reader?.Length ?? 0));
        _topSub = 0; _targetOffset = _topLine;
        CommitLayout();
    }

    /// <summary>After a changed source revision is committed, old byte ranges are not reinterpreted.</summary>
    public void SourceChanged()
    {
        CancelPendingBarSeek();
        RetireSelection("The file changed; the previous selection ended. Select again, or explicitly choose Copy visible source text.");
        _sourceGeneration++; _layoutFormat++; _previousSegments.Clear();
        _observedRevision = _reader?.Revision; _observedLength = _reader?.Length ?? 0;
        _topLine = Math.Clamp(_topLine, _start, Math.Max(_start, _observedLength));
        _targetOffset = _topLine; CommitLayout();
    }

    public void SetHighlight(string? text, bool matchCase)
    {
        _highlight = string.IsNullOrEmpty(text) ? null : text; _highlightCase = matchCase;
        CommitHighlights();
        CommitDecorationGeometry();
        InvalidateVisual();
    }

    public void ResolveBrushes()
    {
        IBrush B(string k, IBrush f) => this.TryFindResource(k, ActualThemeVariant, out var v) && v is IBrush b ? b : f;
        _text = B("FcText", Brushes.Black); _muted = B("FcTextMuted", Brushes.Gray);
        _bg = B("FcPanel", Brushes.White); _hit = B("FcSearchHit", Brushes.Yellow);
        _hitText = B("FcOnSearchHit", Brushes.Black);
        _sel = B("FcFocusBackground", Brushes.LightBlue); _selectionText = B("FcFocusText", Brushes.Black);
        _accent = B("FcActiveAccent", Brushes.SteelBlue);
        if (_accent is ISolidColorBrush a && _text is ISolidColorBrush t && Math.Sqrt(Math.Pow(a.Color.R - t.Color.R, 2) + Math.Pow(a.Color.G - t.Color.G, 2) + Math.Pow(a.Color.B - t.Color.B, 2)) < 160)
            _accent = B("FcHeaderText", _accent);
        _warning = B("FcWarning", Brushes.DarkOrange); _layoutFormat++; CommitLayout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); MeasureFont(); ResolveBrushes();
    }

    private void MeasureFont()
    {
        if (!_fontsResolved)
        {
            // This is a fixed list of platform alternatives, not a user font setting. Keep the
            // actual face chosen by that list, then address it by its real family name. Repeated
            // fallback probes for a synthetic style otherwise recreate uncached alias faces for
            // every unsupported character in the pinned font stack. Unicode still uses the font
            // manager's fallback after this selected primary face. Resolve only in the existing
            // initialized font-measurement lifecycle, never from the control constructor.
            _typeface = CanonicalSystemFace(_typeface);
            _bold = CanonicalSystemFace(_bold);
            _fontsResolved = true;
        }
        var ft = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
        _charWidth = Math.Max(1, ft.WidthIncludingTrailingWhitespace); _rowHeight = Math.Ceiling(_fontSize * 1.45);
    }

    private static Typeface CanonicalSystemFace(Typeface requested)
        => new(new FontFamily(requested.GlyphTypeface.FamilyName), requested.Style, requested.Weight, requested.Stretch);
    private double VerticalBarWidth => _vbar.DesiredSize.Width > 0 ? _vbar.DesiredSize.Width : 12;
    private double HorizontalBarHeight => !_wrap ? (_hbar.DesiredSize.Height > 0 ? _hbar.DesiredSize.Height : 12) : 0;
    private int VisibleRows => Math.Clamp((int)(ContentViewport.Height / _rowHeight), 1, 512);
    private double TextWidth => Math.Max(_charWidth, ContentViewport.Width - 2 * Gutter);

    protected override Size MeasureOverride(Size availableSize)
    {
        _vbar.Measure(availableSize); _hbar.Measure(availableSize);
        return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        long anchor = VisibleAnchor();
        bool widthChanged = Math.Abs(_viewportSize.Width - finalSize.Width) > 0.5;
        _viewportSize = finalSize;
        _vbar.Arrange(new Rect(Math.Max(0, finalSize.Width - VerticalBarWidth), 0, VerticalBarWidth, ContentViewport.Height));
        _hbar.Arrange(new Rect(0, ContentViewport.Height, ContentViewport.Width, HorizontalBarHeight));
        if (widthChanged && _targetOffset is null) _targetOffset = anchor;
        CommitLayout(); return finalSize;
    }
    private long Align(long v) => _unit <= 1 ? v : v - (v - _start) % _unit;

    private bool TryReadLine(long pos, out TextSourceSegment segment, int initialColumn = 0)
    {
        segment = TextSourceSegment.Decode([], pos, _encoding);
        var reader = _reader!;
        if (pos >= reader.Length) return true;
        int want = (int)Math.Min(MaxLineBytes + 16, reader.Length - pos);
        var buffer = new byte[want];
        if (!reader.TryRead(pos, buffer, out int n)) return false;
        if (n == 0) { _layoutError = reader.ReadError ?? "The content ended before its reported length."; return true; }
        int nl = -1, nlLen = 0;
        for (int i = 0; i + _unit <= n && i <= MaxLineBytes; i += _unit)
        {
            int c = Unit(buffer, i);
            if (c is not ('\r' or '\n')) continue;
            nl = i; nlLen = _unit;
            if (c == '\r' && i + 2 * _unit <= n && Unit(buffer, i + _unit) == '\n') nlLen += _unit;
            break;
        }
        int count = nl >= 0 ? nl : n <= MaxLineBytes ? n : ContentCopy.BoundaryBefore(buffer.AsSpan(0, n), MaxLineBytes, _encoding);
        segment = TextSourceSegment.Decode(buffer.AsSpan(0, count), pos, _encoding,
            nl >= 0 ? buffer.AsSpan(nl, nlLen) : default, initialColumn);
        if (segment.Next > pos)
        {
            if (_previousSegments.Count >= 128) _previousSegments.Remove(_previousSegments.Keys.First());
            _previousSegments[segment.Next] = (pos, initialColumn);
        }
        if (n < want && segment.Next < reader.Length && nl < 0) _layoutError = reader.ReadError ?? "The content ended before its reported length.";
        return true;
    }

    private int Unit(ReadOnlySpan<byte> bytes, int i)
    {
        if (_unit == 1) return bytes[i];
        bool be = _encoding.CodePage is 1201 or 12001;
        int value = 0;
        for (int k = 0; k < _unit; k++) value |= bytes[i + k] << (8 * (be ? _unit - k - 1 : k));
        return value;
    }

    private bool TryFindPreviousLineStart(long lineStart, out long previous)
    {
        previous = lineStart;
        if (lineStart <= _start) return true;
        if (_previousSegments.TryGetValue(lineStart, out var saved)) { previous = saved.Start; return true; }
        var reader = _reader!;
        lineStart = Math.Min(lineStart, reader.Length);
        long intendedStart = Math.Max(_start, Align(lineStart - MaxLineBytes - 4 * _unit));
        long windowStart = Math.Max(_start, Align(intendedStart - 4));
        var buffer = new byte[(int)(lineStart - windowStart)];
        if (!reader.TryRead(windowStart, buffer, out int n)) return false;
        int end = n - n % _unit;
        if (end >= _unit && Unit(buffer, end - _unit) == '\n') end -= _unit;
        if (end >= _unit && Unit(buffer, end - _unit) == '\r') end -= _unit;
        for (int i = end - _unit; i >= 0; i -= _unit)
            if (Unit(buffer, i) is '\n' or '\r') { previous = windowStart + i + _unit; return true; }
        previous = windowStart + ContentCopy.BoundaryBefore(buffer.AsSpan(0, n), (int)(intendedStart - windowStart), _encoding);
        return true;
    }

    private bool TryFindTargetSegmentStart(long target, out long first)
    {
        first = _start;
        if (target <= _start) return true;
        // Reuse a decoded segment when possible, so Go-to, search and reflow share the same source
        // origin. This bounded cache is not a file-wide line index.
        var known = _previousSegments.Where(pair => target >= pair.Value.Start && target < pair.Key)
            .OrderByDescending(pair => pair.Value.Start).FirstOrDefault();
        if (known.Key > known.Value.Start) { first = known.Value.Start; return true; }

        // A new giant-line target uses a stable 16KiB source block, rather than a sliding read window
        // whose final few bytes happen to contain the target. Keep enough prefix to align UTF groups.
        long block = _start + (target - _start) / MaxLineBytes * MaxLineBytes;
        long scanStart = Math.Max(_start, Align(block - 4 * _unit));
        long end = target + Math.Min(_unit, Math.Max(0, _reader!.Length - target));
        var bytes = new byte[(int)(end - scanStart)];
        if (!_reader.TryRead(scanStart, bytes, out int read)) return false;
        int count = read - read % _unit;
        if (count >= _unit && Unit(bytes, count - _unit) == '\n') count -= _unit;
        if (count >= _unit && Unit(bytes, count - _unit) == '\r') count -= _unit;
        for (int i = count - _unit; i >= 0; i -= _unit)
            if (Unit(bytes, i) is '\n' or '\r') { first = scanStart + i + _unit; return true; }
        first = scanStart + ContentCopy.BoundaryBefore(bytes.AsSpan(0, read), (int)(block - scanStart), _encoding);
        return true;
    }

    private TextLayout Shape(TextSourceSegment segment, IBrush? foreground = null)
    {
        if (foreground is null) ShapedSegmentCount++;
        var (brush, face) = ReportStyle && segment.Display.Length > 0 ? StyleOf(segment.Display) : (_text, _typeface);
        return new TextLayout(segment.Display, face, _fontSize, foreground ?? brush, textWrapping: _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            maxWidth: _wrap ? TextWidth : double.PositiveInfinity, lineHeight: _rowHeight);
    }

    private void CommitLayout()
    {
        if (_committing)
        {
            // Automation/content subscribers may synchronously replace the source. Do not let the
            // previous rows authorize Copy or describe that new reader while its commit is deferred.
            _commitPending = true;
            _rows.Clear(); _highlightRanges.Clear(); _selectionGeometry.Clear(); _highlightGeometry.Clear();
            _semanticValue = _reader is null ? "No content" : "Loading current content…";
            _semanticStatus = _reader is null ? "No content" : "The current content is updating; wait for it before copying.";
            return;
        }
        _committing = true;
        try
        {
            if (_reader is not null && (_reader.Revision != _observedRevision || _reader.Length != _observedLength))
            {
                RetireSelection("The file changed; the previous selection ended. Select again, or explicitly choose Copy visible source text.");
                _sourceGeneration++; _layoutFormat++; _previousSegments.Clear();
                _observedRevision = _reader.Revision; _observedLength = _reader.Length;
            }
            _rows.Clear();
            _highlightRanges.Clear();
            if (_layoutFormat != _cachedFormat || _wrap && Math.Abs(_cachedWrapWidth - TextWidth) > .5)
            {
                foreach (var cached in _segmentLayouts.Values) cached.Dispose();
                _segmentLayouts.Clear();
                _cachedFormat = _layoutFormat; _cachedWrapWidth = TextWidth;
            }
            _layouts.Clear(); _layoutIncomplete = false; _layoutError = null;
            if (_reader is null) { UpdateScroll(); PublishState(); InvalidateVisual(); return; }
            // Before the first real arrangement there is no usable wrap width. Shaping an entire
            // segment at a transient one-character width creates thousands of unnecessary rows.
            if (_viewportSize.Width <= 0 || _viewportSize.Height <= 0)
            {
                _layoutIncomplete = _reader.Length > 0; PublishState(); InvalidateVisual(); return;
            }
            long? reveal = _targetOffset;
            if (reveal is { } target)
            {
                target = Math.Clamp(Align(target), _start, Math.Max(_start, _reader.Length));
                if (!TryFindTargetSegmentStart(target, out long first))
                {
                    _layoutIncomplete = true; PublishState(); InvalidateVisual(); return;
                }
                _topLine = first;
                _topSub = 0;
            }
            long pos = Math.Clamp(_topLine, _start, Math.Max(_start, _reader.Length));
            int skip = _topSub, column = SegmentColumn(pos);
            while (_rows.Count < VisibleRows + 1 && pos < _reader.Length)
            {
                var key = (pos, column);
                _segmentLayouts.TryGetValue(key, out var cached);
                TextSourceSegment segment;
                if (cached is not null) { segment = cached.Segment; _layoutError ??= cached.ReadError; }
                else if (!TryReadLine(pos, out segment, column)) { _layoutIncomplete = true; break; }
                if (segment.Next <= pos) break;
                if (reveal is { } pending && pending > segment.ContentEnd)
                {
                    column = segment.Ending.Length > 0 ? 0 : segment.FinalColumn;
                    pos = segment.Next;
                    continue;
                }
                var layout = cached?.Layout ?? Shape(segment);
                if (cached is null)
                {
                    cached = new SegmentLayout(segment, layout, _layoutError);
                    _segmentLayouts.Add(key, cached);
                }
                _layouts.Add(layout);
                var lines = layout.TextLines;
                if (reveal is { } offset && offset >= pos && offset <= segment.ContentEnd)
                {
                    int display = segment.DisplayAtByte(offset);
                    for (int i = 0; i < lines.Count; i++)
                        if (display >= lines[i].FirstTextSourceIndex && (display < lines[i].FirstTextSourceIndex + lines[i].Length || i == lines.Count - 1))
                        { skip = i; break; }
                    _topLine = pos; _topSub = skip;
                }
                for (int i = Math.Min(skip, Math.Max(0, lines.Count - 1)); i < lines.Count && _rows.Count < VisibleRows + 1; i++)
                    _rows.Add(new Row(segment, lines[i], i, cached));
                skip = 0; column = segment.Ending.Length > 0 ? 0 : segment.FinalColumn; pos = segment.Next;
            }
            UpdateScroll();
            if (reveal is { } at && _rows.Count > 0 && _rows.Any(r => at >= r.Segment.Start && at <= r.Segment.ContentEnd))
            {
                RevealHorizontal(at); _targetOffset = null;
            }
            CommitHighlights();
            PublishState(); InvalidateVisual();
        }
        finally
        {
            // Layout ownership follows the committed viewport. Reuse its segments across height and
            // toolbar/result-panel changes; release every layout that is no longer represented.
            foreach (var key in _segmentLayouts.Where(pair => !_layouts.Contains(pair.Value.Layout)).Select(pair => pair.Key).ToArray())
            {
                _segmentLayouts[key].Dispose();
                _segmentLayouts.Remove(key);
            }
            _committing = false;
            if (_commitPending) { _commitPending = false; CommitLayout(); }
        }
    }

    private (IBrush, Typeface) StyleOf(string line)
    {
        if (line[0] == '⚠') return (_warning, _typeface);
        if (line[0] != ' ') return (_accent, _bold);
        return line.TrimStart().StartsWith('─') ? (_muted, _typeface) : (_text, _typeface);
    }

    private void CommitHighlights()
    {
        _highlightRanges.Clear();
        if (_highlight is null) return;
        var comparison = _highlightCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var segment in _rows.Select(row => row.Segment).Distinct())
        {
            var ranges = new List<(int Start, int End)>();
            int index = segment.Source.IndexOf(_highlight, comparison);
            while (index >= 0)
            {
                ranges.Add((segment.DisplayAtSource(index), segment.DisplayAtSource(index + _highlight.Length, trailing: true)));
                index = segment.Source.IndexOf(_highlight, index + 1, comparison);
            }
            _highlightRanges.Add(segment, ranges.ToArray());
        }
    }

    private void CommitDecorationGeometry()
    {
        _selectionGeometry.Clear(); _highlightGeometry.Clear();
        DecorationCharactersMeasured = 0;
        if (_commitPending) return;
        DecorationGeometryBuildCount++;
        if (!HasSelection && _highlightRanges.Count == 0) return;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i]; double y = i * _rowHeight;
            int selectionBefore = _selectionGeometry.Count, highlightBefore = _highlightGeometry.Count;
            foreach (var (left, right) in VisibleTextRanges(row))
            {
                if (HasSelection)
                {
                    var (start, length) = Selection;
                    AddRangeGeometry(_selectionGeometry, row, Math.Max(left, row.Segment.DisplayAtByte(start)),
                        Math.Min(right, row.Segment.DisplayAtByte(start + length, trailing: true)), y);
                }
                if (_highlightRanges.TryGetValue(row.Segment, out var ranges))
                {
                    int low = 0, high = ranges.Length;
                    while (low < high) { int middle = low + (high - low) / 2; if (ranges[middle].End <= left) low = middle + 1; else high = middle; }
                    for (int r = low; r < ranges.Length && ranges[r].Start < right; r++)
                        AddRangeGeometry(_highlightGeometry, row, Math.Max(left, ranges[r].Start), Math.Min(right, ranges[r].End), y);
                }
            }
            if (_selectionGeometry.Count > selectionBefore)
                row.Cached.SelectionLayout ??= Shape(row.Segment, _selectionText);
            if (_highlightGeometry.Count > highlightBefore)
                row.Cached.HighlightLayout ??= Shape(row.Segment, _hitText);
        }
    }

    private IEnumerable<(int Start, int End)> VisibleTextRanges(Row row)
    {
        if (_wrap) { yield return (row.First, row.End); yield break; }
        double left = _horizontalOffset - Gutter, right = _horizontalOffset + ContentViewport.Width - Gutter;
        double x = row.Line.Start;
        foreach (var textRun in row.Line.TextRuns)
        {
            if (textRun is not DrawableTextRun drawable) continue;
            double next = x + drawable.Size.Width;
            if (next > left && x < right)
            {
                // TextRuns are in visual order. Map each intersecting run independently so an RTL
                // run between LTR viewport-edge hits cannot disappear from the selected geometry.
                if (textRun is not ShapedTextRun shaped ||
                    !MemoryMarshal.TryGetString(shaped.Text, out var text, out int first, out int length) ||
                    !ReferenceEquals(text, row.Segment.Display))
                {
                    // A future formatter may return non-string-backed runs. Preserve correctness
                    // in that case with the already bounded source segment, rather than omitting it.
                    yield return (row.First, row.End);
                    yield break;
                }
                var glyphs = shaped.GlyphRun;
                var a = glyphs.GetCharacterHitFromDistance(Math.Max(0, left - x), out _);
                var b = glyphs.GetCharacterHitFromDistance(Math.Min(drawable.Size.Width, right - x), out _);
                int origin = first - glyphs.Metrics.FirstCluster;
                int from = Math.Max(first, origin + Math.Min(a.FirstCharacterIndex, b.FirstCharacterIndex));
                int to = Math.Min(first + length, origin + Math.Max(a.FirstCharacterIndex + Math.Max(1, a.TrailingLength), b.FirstCharacterIndex + Math.Max(1, b.TrailingLength)));
                if (to > from) yield return (from, to);
            }
            x = next;
            if (x >= right) yield break;
        }
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(_bg, new Rect(Bounds.Size));
        using var clip = dc.PushClip(ContentViewport);
        foreach (var rect in _highlightGeometry) dc.FillRectangle(_hit, rect);
        foreach (var rect in _selectionGeometry) dc.FillRectangle(_sel, rect);
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var origin = new Point(Gutter - _horizontalOffset, i * _rowHeight);
            row.Line.Draw(dc, origin);
            foreach (var rect in _highlightGeometry)
            {
                if (rect.Y >= (i + 1) * _rowHeight || rect.Bottom <= i * _rowHeight) continue;
                var highlighted = row.Cached.HighlightLayout;
                if (highlighted is null) continue;
                using var hitClip = dc.PushClip(rect);
                highlighted.TextLines[row.Sub].Draw(dc, origin);
            }
            // Shape a paired foreground only for selected visible segments. The same
            // source, font and wrap width keep glyph positions identical for RTL text.
            foreach (var rect in _selectionGeometry)
            {
                if (rect.Y >= (i + 1) * _rowHeight || rect.Bottom <= i * _rowHeight) continue;
                var selected = row.Cached.SelectionLayout;
                if (selected is null) continue;
                using var selectedClip = dc.PushClip(rect);
                selected.TextLines[row.Sub].Draw(dc, origin);
            }
        }
        string? notice = _layoutError ?? (_layoutIncomplete ? "Loading…" : _reader is { Length: 0 } ? "The file is empty." : null);
        if (notice is not null && _rows.Count < VisibleRows)
        {
            var text = new FormattedText(notice, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, _muted);
            dc.DrawText(text, new Point(Gutter, _rows.Count * _rowHeight));
        }
    }

    private void AddRangeGeometry(List<Rect> target, Row row, int from, int to, double y)
    {
        from = Math.Max(from, row.First); to = Math.Min(to, row.End);
        if (to <= from) return;
        DecorationCharactersMeasured += to - from;
        foreach (var bounds in row.Line.GetTextBounds(from, to - from))
        {
            var visible = new Rect(Gutter - _horizontalOffset + bounds.Rectangle.X, y, bounds.Rectangle.Width, _rowHeight).Intersect(ContentViewport);
            if (visible.Width > 0 && visible.Height > 0) target.Add(visible);
        }
    }

    private void QueueBarSeek(long offset)
    {
        // A dragged thumb can publish hundreds of byte offsets per second. Shape at most one
        // latest viewport per frame; intermediate positions must not monopolize the UI thread.
        _pendingBarOffset = offset;
        if (!_barSeekTimer.IsEnabled) _barSeekTimer.Start();
    }

    private void CancelPendingBarSeek()
    {
        _pendingBarOffset = null;
        _barSeekTimer.Stop();
    }

    private void UpdateScroll()
    {
        _updatingScroll = true;
        try
        {
            _vbar.Maximum = Math.Max(0, _reader?.Length ?? 0);
            _vbar.ViewportSize = Math.Max(1, _rows.Count > 0 ? _rows[^1].Segment.Next - _topLine : 1);
            _vbar.LargeChange = _vbar.ViewportSize; _vbar.SmallChange = Math.Max(1, _vbar.ViewportSize / VisibleRows);
            if (_pendingBarOffset is null) _vbar.Value = _topLine;
            double width = _rows.Count > 0 ? _rows.Max(r => r.Line.WidthIncludingTrailingWhitespace) : 0;
            _hbar.Maximum = _wrap ? 0 : Math.Max(0, width - TextWidth); _hbar.ViewportSize = TextWidth;
            _hbar.LargeChange = Math.Max(_charWidth, TextWidth * .8); _hbar.SmallChange = _charWidth * 4;
            _horizontalOffset = Math.Clamp(_horizontalOffset, 0, _hbar.Maximum); _hbar.Value = _horizontalOffset;
        }
        finally { _updatingScroll = false; }
    }

    private long VisibleAnchor()
    {
        if (_rows.Count == 0) return _topLine;
        if (_wrap || _horizontalOffset <= 0) return _rows[0].StartByte;
        var hit = _rows[0].Line.GetCharacterHitFromDistance(_horizontalOffset);
        return _rows[0].Segment.ByteAtDisplay(hit.FirstCharacterIndex + hit.TrailingLength);
    }

    private void RevealHorizontal(long offset)
    {
        if (_wrap) return;
        var row = _rows.FirstOrDefault(r => offset >= r.StartByte && offset <= r.EndByte);
        if (row is null) return;
        int index = row.Segment.DisplayAtByte(offset);
        double left = row.Line.GetDistanceFromCharacterHit(new CharacterHit(index, 0)), right = left + _charWidth;
        var bounds = row.Line.GetTextBounds(index, Math.Min(1, Math.Max(0, row.End - index)));
        if (bounds.Count > 0) { left = bounds[0].Rectangle.X; right = bounds[0].Rectangle.Right; }
        if (left < _horizontalOffset) _horizontalOffset = left;
        else if (right > _horizontalOffset + TextWidth) _horizontalOffset = Math.Max(0, right - TextWidth + _charWidth);
        UpdateScroll();
    }

    internal Rect? SourceGlyphBounds(long offset)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (offset < row.StartByte || offset >= row.EndByte) continue;
            var bounds = row.Line.GetTextBounds(row.Segment.DisplayAtByte(offset), 1);
            if (bounds.Count > 0) return new Rect(Gutter - _horizontalOffset + bounds[0].Rectangle.X, i * _rowHeight, bounds[0].Rectangle.Width, _rowHeight);
        }
        return null;
    }

    public void ScrollRows(int delta) => ScrollRowsCore(delta, fromWheel: false);

    private void ScrollRowsCore(int delta, bool fromWheel)
    {
        if (!fromWheel) _wheelRows.Reset();
        if (_reader is null || delta == 0) return;
        CancelPendingBarSeek();
        _targetOffset = null;
        for (int i = 0; i < Math.Min(Math.Abs((long)delta), 512); i++)
        {
            if (delta > 0 && !StepDown()) break;
            if (delta < 0 && !StepUp()) break;
        }
        CommitLayout(); PositionChanged?.Invoke();
    }
    private bool StepDown()
    {
        if (!TryGetSegmentLayout(_topLine, out var cached) || cached.Segment.Next <= _topLine) return false;
        if (_topSub + 1 < cached.Layout.TextLines.Count) { _topSub++; return true; }
        if (cached.Segment.Next >= _reader!.Length) return false;
        _topLine = cached.Segment.Next; _topSub = 0; return true;
    }
    private bool StepUp()
    {
        if (_topSub > 0) { _topSub--; return true; }
        if (_topLine <= _start || !TryFindPreviousLineStart(_topLine, out long previous) || previous >= _topLine) return false;
        if (!TryGetSegmentLayout(previous, out var cached)) return false;
        _topLine = previous; _topSub = Math.Max(0, cached.Layout.TextLines.Count - 1); return true;
    }
    private int SegmentColumn(long offset) => _previousSegments.Values.FirstOrDefault(segment => segment.Start == offset).Column;
    private bool TryGetSegmentLayout(long offset, out SegmentLayout cached)
    {
        var key = (offset, SegmentColumn(offset));
        if (_segmentLayouts.TryGetValue(key, out cached!)) return true;
        if (!TryReadLine(offset, out var segment, key.Item2)) return false;
        cached = new SegmentLayout(segment, Shape(segment), _layoutError);
        _segmentLayouts.Add(key, cached);
        return true;
    }
    public void ScrollToOffset(long offset, bool preserveSelection = false)
    {
        _wheelRows.Reset();
        if (_reader is null) return;
        CancelPendingBarSeek();
        if (!preserveSelection) ClearSelection();
        _targetOffset = Math.Clamp(offset, _start, Math.Max(_start, _reader.Length));
        CommitLayout(); PositionChanged?.Invoke();
    }
    public void GoToEnd()
    {
        _wheelRows.Reset();
        if (_reader is null) return;
        CancelPendingBarSeek();
        ClearSelection(); _targetOffset = null; _topLine = _reader.Length; _topSub = 0;
        for (int i = 0; i < VisibleRows; i++) if (!StepUp()) break;
        if (_topLine >= _reader.Length) _targetOffset = Math.Max(_start, _reader.Length - _unit);
        CommitLayout(); PositionChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _reader is null) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        switch (e.Key)
        {
            case Key.Down: ScrollRows(1); break;
            case Key.Up: ScrollRows(-1); break;
            case Key.PageDown: case Key.Space when e.KeyModifiers == KeyModifiers.None: ScrollRows(Math.Max(1, VisibleRows - 1)); break;
            case Key.PageUp: ScrollRows(-Math.Max(1, VisibleRows - 1)); break;
            case Key.Home when ctrl: ScrollToOffset(_start); break;
            case Key.End when ctrl: GoToEnd(); break;
            case Key.A when ctrl: SetSelection(_start, _reader.Length); break;
            case Key.Left when !_wrap: ScrollHorizontal(-_charWidth * (ctrl ? 16 : 4)); break;
            case Key.Right when !_wrap: ScrollHorizontal(_charWidth * (ctrl ? 16 : 4)); break;
            case Key.Home when !_wrap: ScrollHorizontal(-_hbar.Maximum); break;
            case Key.End when !_wrap: ScrollHorizontal(_hbar.Maximum); break;
            default: return;
        }
        e.Handled = true;
    }
    private void ScrollHorizontal(double delta)
    {
        _horizontalOffset = Math.Clamp(_horizontalOffset + delta, 0, _hbar.Maximum);
        UpdateScroll(); PublishState(); InvalidateVisual(); PositionChanged?.Invoke();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!_wrap && ((e.KeyModifiers & KeyModifiers.Shift) != 0 || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)))
        {
            _wheelRows.Reset();
            ScrollHorizontal(-(e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * _charWidth * 4);
        }
        else if (_reader is not null)
        {
            bool beyondStart = e.Delta.Y > 0 && _topLine == _start && _topSub == 0;
            if (beyondStart) _wheelRows.Reset();
            long priorLine = _topLine;
            int priorSub = _topSub;
            int rows = beyondStart ? 0 : _wheelRows.Consume(e.Delta.Y);
            ScrollRowsCore(-rows, fromWheel: true);
            if (rows != 0 && priorLine == _topLine && priorSub == _topSub) _wheelRows.Reset();
        }
        e.Handled = true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !ContentViewport.Contains(e.GetPosition(this))) return;
        Focus(); long? at = Hit(e.GetPosition(this));
        if (at is null) return;
        if ((e.KeyModifiers & KeyModifiers.Shift) == 0 || _selStart is null)
        { _selStart = at; _selStartColumn = ColumnAtByte(at.Value); }
        _selEnd = at; _selEndColumn = ColumnAtByte(at.Value); _selectionEnded = null; e.Pointer.Capture(this);
        PublishState(); InvalidateVisual(); PositionChanged?.Invoke();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_selStart is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (point.Y < 0) ScrollRows(-1);
        else if (point.Y >= ContentViewport.Height) ScrollRows(1);
        if (!_wrap && point.X < Gutter) ScrollHorizontal(-_charWidth * 4);
        else if (!_wrap && point.X > ContentViewport.Width - Gutter) ScrollHorizontal(_charWidth * 4);
        _selEnd = Hit(point) ?? _selEnd;
        if (_selEnd is { } end) _selEndColumn = ColumnAtByte(end);
        PublishState(); InvalidateVisual(); PositionChanged?.Invoke();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); e.Pointer.Capture(null); }
    private long? Hit(Point point)
    {
        if (_rows.Count == 0) return null;
        var row = _rows[Math.Clamp((int)(point.Y / _rowHeight), 0, Math.Min(VisibleRows - 1, _rows.Count - 1))];
        var hit = row.Line.GetCharacterHitFromDistance(point.X - Gutter + _horizontalOffset);
        return row.Segment.ByteAtDisplay(hit.FirstCharacterIndex + hit.TrailingLength);
    }
    internal void SetSelection(long start, long end)
    {
        if (_reader is null) return;
        _selStart = Math.Clamp(start, _start, Math.Max(_start, _reader.Length));
        _selEnd = Math.Clamp(end, _start, Math.Max(_start, _reader.Length)); _selectionEnded = null;
        _selStartColumn = ColumnAtByte(_selStart.Value); _selEndColumn = ColumnAtByte(_selEnd.Value);
        PublishState(); InvalidateVisual(); PositionChanged?.Invoke();
    }
    private void ClearSelection() { _selStart = _selEnd = null; }
    private void RetireSelection(string reason) { if (HasSelection) _selectionEnded = reason; ClearSelection(); }

    private int ColumnAtByte(long offset) => _rows.Select(r => r.Segment)
        .FirstOrDefault(s => offset >= s.Start && offset <= s.ContentEnd)?.ColumnAtByte(offset) ?? 0;

    internal TextCopyScope? CaptureCopyScope(bool explicitVisible)
    {
        if (_committing || _commitPending) return null;
        // Refresh can finish on a worker before its UI continuation commits the new source. The
        // selection belongs to the revision represented by our rows, not a newly refreshed reader.
        if (_reader is not null && (_reader.Revision != _observedRevision || _reader.Length != _observedLength))
        {
            CommitLayout();
            // Even the toolbar could still have said "Copy source selection" before this call. A
            // second, deliberately visible-scope action is required after the refreshed view commits.
            return null;
        }
        if (_reader is null || _rows.Count == 0 || !HasSelection && _selectionEnded is not null && !explicitVisible) return null;
        if (explicitVisible && !HasSelection) _selectionEnded = null;
        var (start, length) = HasSelection ? Selection : VisibleRange();
        int column = HasSelection ? (_selStart <= _selEnd ? _selStartColumn : _selEndColumn) : ColumnAtByte(start);
        return new TextCopyScope(_reader, _encoding, start, length, column, HasSelection, _sourceGeneration, _observedRevision);
    }
    private (long Start, long Length) VisibleRange()
    {
        if (_rows.Count == 0) return (_topLine, 0);
        var last = _rows[Math.Min(VisibleRows, _rows.Count) - 1];
        long end = last.EndByte == last.Segment.ContentEnd ? last.Segment.Next : last.EndByte;
        return (_rows[0].StartByte, Math.Max(0, end - _rows[0].StartByte));
    }
    /// <summary>Cached decoded source text only; never reads synchronously or inserts visual line endings.</summary>
    public string GetSelectedOrVisibleText()
    {
        if (_commitPending) return "";
        var (start, length) = HasSelection ? Selection : VisibleRange();
        var result = new StringBuilder();
        foreach (var segment in _rows.Select(r => r.Segment).Distinct())
        {
            if (segment.Next <= start || segment.Start >= start + length) continue;
            result.Append(segment.SliceSource(Math.Max(start, segment.Start), Math.Min(start + length, segment.Next)));
        }
        return result.ToString();
    }

    private string GetSemanticText()
    {
        if (_wrap || _horizontalOffset <= 0 || HasSelection)
            return ContentCopy.DisplayText(GetSelectedOrVisibleText(), ColumnAtByte(HasSelection ? Selection.Start : TopOffset));
        var result = new StringBuilder();
        foreach (var row in _rows.Take(VisibleRows))
        {
            var hit = row.Line.GetCharacterHitFromDistance(_horizontalOffset);
            long start = row.Segment.ByteAtDisplay(hit.FirstCharacterIndex);
            result.Append(ContentCopy.DisplayText(row.Segment.SliceSource(start, row.Segment.Next), row.Segment.ColumnAtByte(start)));
            if (result.Length >= 8192) break;
        }
        return result.ToString();
    }
    private void PublishState()
    {
        if (_commitPending) return;
        CommitDecorationGeometry();
        var (start, length) = Selection;
        _semanticStatus = _reader is null ? "No content" :
            $"{(HasSelection ? $"Selected source bytes {start:N0} through {start + length - 1:N0}, {length:N0} bytes" : "No selection; Copy visible source text copies the visible lines")}; {_encoding.WebName}; {(_wrap ? "wrapped" : $"unwrapped, horizontal position {_horizontalOffset:0} of {_hbar.Maximum:0}")}.";
        if (_selectionEnded is not null) _semanticStatus = _selectionEnded + " " + _semanticStatus;
        if (_layoutError is not null) _semanticStatus += " Incomplete reading: " + _layoutError;
        else if (_layoutIncomplete) _semanticStatus += " Loading current content…";
        // Text platforms and assistive clients may treat NUL as a terminator. The semantic excerpt
        // uses the same explicit control symbols as the painted view; source Copy remains separate.
        string visible = GetSemanticText();
        int excerptEnd = Math.Min(8192, visible.Length);
        if (excerptEnd < visible.Length && excerptEnd > 0 && char.IsHighSurrogate(visible[excerptEnd - 1]) && char.IsLowSurrogate(visible[excerptEnd])) excerptEnd--;
        _semanticValue = _reader is null ? "No content" : visible.Length > 0 ?
            (visible.Length <= 8192 ? visible : visible[..excerptEnd] + "\n[Accessible displayed excerpt limited to 8,192 characters; navigate to inspect more.]") :
            _layoutError ?? (_layoutIncomplete ? "Loading current content…" : _reader.Length == 0 ? "The file is empty." : "No selected content in the current viewport.");
        _automationPeer?.AnnounceContent(); ContentChanged?.Invoke();
    }
    /// <summary>Word wrapping policy: concatenating the pieces preserves every character.</summary>
    internal static IEnumerable<string> Segments(string line, int cols, bool wrap)
    {
        if (!wrap || line.Length <= cols) { yield return line; yield break; }
        int start = 0;
        while (line.Length - start > cols)
        {
            int end = start + cols; int space = line.LastIndexOf(' ', end - 1, cols);
            int next = line[end] == ' ' ? end + 1 : space >= start + cols / 2 ? space + 1 : end;
            yield return line[start..next]; start = next;
        }
        if (start < line.Length) yield return line[start..];
    }
}

internal sealed record TextCopyScope(PagedReader Reader, Encoding Encoding, long Start, long Length, int DisplayColumn,
    bool Selected, long Generation, ContentRevision? Revision);
