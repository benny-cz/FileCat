using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.Core.Content;

namespace FileCat.App.Controls;

/// <summary>
/// Text view that scrolls by byte offset (plan §13.1): it never builds a whole-file line index, handles giant
/// single lines by splitting them into bounded segments, and decodes only what is visible. Line starts are
/// found by bounded scans around the viewport, so a multi-gigabyte log opens instantly.
/// </summary>
public sealed class TextViewer : Control
{
    private const int MaxLineBytes = 16 * 1024;
    private readonly ScrollBar _vbar;
    private PagedReader? _reader;
    private Encoding _encoding = new UTF8Encoding(false);
    private int _unit = 1;
    private long _start;
    private long _topLine;
    private int _topSub;
    private bool _wrap = true;
    private Typeface _typeface = new(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"));
    private double _fontSize = 13;
    private double _charWidth = 8;
    private double _rowHeight = 18;
    private readonly List<Row> _rows = [];
    private bool _layoutIncomplete;
    private bool _pendingInvalidate;
    private bool _updatingScroll;
    private bool _scrollUpdatePosted;
    private string? _highlight;
    private bool _highlightCase;
    private (int Row, int Col)? _selStart;
    private (int Row, int Col)? _selEnd;
    private IBrush _text = Brushes.Black, _muted = Brushes.Gray, _bg = Brushes.White, _hit = Brushes.Yellow, _sel = Brushes.LightBlue;
    private IBrush _accent = Brushes.SteelBlue, _warning = Brushes.DarkOrange;
    private Typeface _bold = new(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"), FontStyle.Normal, FontWeight.Bold);

    /// <summary>
    /// Reports FileCat writes (Info, file-system records): lines that start at the margin are headings, drawn bold in the
    /// accent colour; lines starting with ⚠ in the warning colour; table rules muted.
    /// </summary>
    public bool ReportStyle { get; set; }

    private sealed record Row(long LineStart, int Sub, string Text, long NextLine, bool IsLastRowOfLine);

    public TextViewer()
    {
        Focusable = true;
        ClipToBounds = true;
        PositionChanged += () => _automationPeer?.AnnounceContent();
        _vbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Vertical, AllowAutoHide = false };
        _vbar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_updatingScroll && _reader is not null) ScrollToOffset((long)_vbar.Value);
        };
        VisualChildren.Add(_vbar);
        LogicalChildren.Add(_vbar);
    }

    public event Action? PositionChanged;

    private TextViewerAutomationPeer? _automationPeer;

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => _automationPeer = new TextViewerAutomationPeer(this);

    public long TopOffset => _topLine;

    /// <summary>Where the text starts (after a byte-order mark).</summary>
    public long ContentStart => _start;

    public bool Wrap
    {
        get => _wrap;
        set
        {
            _wrap = value;
            _topSub = 0;
            InvalidateVisual();
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = Math.Clamp(value, 8, 32);
            MeasureFont();
            InvalidateVisual();
        }
    }

    public Encoding Encoding => _encoding;

    public void SetReader(PagedReader? reader, Encoding encoding, int preambleLength)
    {
        if (_reader is not null) _reader.PageLoaded -= OnPageLoaded;
        _reader = reader;
        if (reader is not null) reader.PageLoaded += OnPageLoaded;
        SetEncoding(encoding, preambleLength);
    }

    public void SetEncoding(Encoding encoding, int preambleLength)
    {
        _encoding = encoding;
        _unit = TextDecoding.UnitSize(encoding);
        _start = preambleLength;
        _topLine = Math.Max(_start, Align(_topLine));
        _topSub = 0;
        MeasureFont();
        InvalidateVisual();
    }

    public void SetHighlight(string? text, bool matchCase)
    {
        _highlight = string.IsNullOrEmpty(text) ? null : text;
        _highlightCase = matchCase;
        InvalidateVisual();
    }

    private void OnPageLoaded()
    {
        if (_pendingInvalidate) return;
        _pendingInvalidate = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pendingInvalidate = false;
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    public void ResolveBrushes()
    {
        IBrush B(string k, IBrush f) => this.TryFindResource(k, ActualThemeVariant, out var v) && v is IBrush b ? b : f;
        _text = B("FcText", Brushes.Black);
        _muted = B("FcTextMuted", Brushes.Gray);
        _bg = B("FcPanel", Brushes.White);
        _hit = B("FcSearchHit", Brushes.Yellow);
        _sel = B("FcFocusBackground", Brushes.LightBlue);
        _accent = B("FcActiveAccent", Brushes.SteelBlue);
        _warning = B("FcWarning", Brushes.DarkOrange);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResolveBrushes();
        MeasureFont();
    }

    private void MeasureFont()
    {
        var ft = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
        _charWidth = Math.Max(1, ft.WidthIncludingTrailingWhitespace);
        _rowHeight = Math.Ceiling(_fontSize * 1.45);
    }

    private int VisibleRows => Math.Max(1, (int)(Bounds.Height / _rowHeight));
    private int Columns => Math.Max(10, (int)((Bounds.Width - 16 - (_vbar.IsVisible ? _vbar.Bounds.Width : 0)) / _charWidth));

    protected override Size MeasureOverride(Size availableSize)
    {
        _vbar.Measure(availableSize);
        return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double sbw = _vbar.DesiredSize.Width > 0 ? _vbar.DesiredSize.Width : 12;
        _vbar.Arrange(new Rect(finalSize.Width - sbw, 0, sbw, finalSize.Height));
        return finalSize;
    }

    private long Align(long v) => _unit <= 1 ? v : v - (v - _start) % _unit;

    // ---- Line reading ------------------------------------------------------------------------------------

    /// <summary>Reads one logical line (or a bounded segment of a giant line) from cached pages.</summary>
    private bool TryReadLine(long pos, out string text, out long next)
    {
        text = string.Empty;
        next = pos;
        var reader = _reader!;
        long len = reader.Length;
        if (pos >= len) return true;
        int want = (int)Math.Min(MaxLineBytes + 2 * _unit, len - pos);
        var buffer = new byte[want];
        if (!reader.TryRead(pos, buffer, out int n)) return false;
        int nl = -1, nlLen = 0;
        if (_unit == 1)
        {
            for (int i = 0; i < n; i++)
            {
                byte b = buffer[i];
                if (b == (byte)'\n') { nl = i; nlLen = 1; break; }
                if (b == (byte)'\r') { nl = i; nlLen = i + 1 < n && buffer[i + 1] == (byte)'\n' ? 2 : 1; break; }
            }
        }
        else
        {
            bool be = _encoding is UnicodeEncoding && _encoding.GetPreamble() is { Length: 2 } p && p[0] == 0xFE || _encoding.WebName == "utf-16BE";
            for (int i = 0; i + 1 < n; i += 2)
            {
                int c = be ? buffer[i] << 8 | buffer[i + 1] : buffer[i + 1] << 8 | buffer[i];
                if (c == '\n') { nl = i; nlLen = 2; break; }
                if (c == '\r')
                {
                    int c2 = i + 3 < n ? (be ? buffer[i + 2] << 8 | buffer[i + 3] : buffer[i + 3] << 8 | buffer[i + 2]) : -1;
                    nl = i;
                    nlLen = c2 == '\n' ? 4 : 2;
                    break;
                }
            }
        }
        int lineBytes;
        if (nl >= 0 && nl <= MaxLineBytes)
        {
            lineBytes = nl;
            next = pos + nl + nlLen;
        }
        else if (pos + n >= len && nl < 0)
        {
            lineBytes = n;
            next = len;
        }
        else
        {
            // Giant line: cut a bounded segment at a character boundary.
            lineBytes = Math.Min(n, MaxLineBytes);
            if (_encoding is UTF8Encoding)
                while (lineBytes > 1 && (buffer[lineBytes] & 0xC0) == 0x80) lineBytes--;
            else lineBytes -= lineBytes % _unit;
            next = pos + lineBytes;
        }
        text = Sanitize(_encoding.GetString(buffer, 0, lineBytes));
        return true;
    }

    private static string Sanitize(string s)
    {
        StringBuilder? sb = null;
        int col = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\t')
            {
                sb ??= new StringBuilder(s, 0, i, s.Length + 16);
                int spaces = 4 - col % 4;
                sb.Append(' ', spaces);
                col += spaces;
                continue;
            }
            if (c < 0x20 || c == 0x7F)
            {
                sb ??= new StringBuilder(s, 0, i, s.Length + 16);
                sb.Append(c == 0 ? '·' : (char)(0x2400 + (c == 0x7F ? 0x21 : c)));
                col++;
                continue;
            }
            sb?.Append(c);
            col++;
        }
        return sb?.ToString() ?? s;
    }

    /// <summary>
    /// The rows that show a line: the whole line when it fits (or wrapping is off), otherwise wrapped after the last
    /// space that keeps a row within the width; a word longer than half a row is cut at the width instead. Put together,
    /// the rows are the line: a row keeps the space it wraps at (invisible, even when it is one past the width).
    /// </summary>
    internal static IEnumerable<string> Segments(string line, int cols, bool wrap)
    {
        if (!wrap || line.Length <= cols)
        {
            yield return line;
            yield break;
        }
        int start = 0;
        while (line.Length - start > cols)
        {
            int end = start + cols;
            int space = line.LastIndexOf(' ', end - 1, cols);
            int next = line[end] == ' ' ? end + 1 : space >= start + cols / 2 ? space + 1 : end;
            yield return line[start..next];
            start = next;
        }
        if (start < line.Length) yield return line[start..];
    }

    private IEnumerable<string> Segments(string line) => Segments(line, Columns, _wrap);

    /// <summary>Start of the line before <paramref name="lineStart"/> (bounded backward scan).</summary>
    private bool TryFindPreviousLineStart(long lineStart, out long previous)
    {
        previous = lineStart;
        if (lineStart <= _start) return true;
        var reader = _reader!;
        long windowStart = Math.Max(_start, lineStart - MaxLineBytes - 4 * _unit);
        int count = (int)(lineStart - windowStart);
        var buffer = new byte[count];
        if (!reader.TryRead(windowStart, buffer, out int n)) return false;
        int end = n;
        // Skip the terminator of the previous line.
        if (_unit == 1)
        {
            if (end > 0 && buffer[end - 1] == '\n') end--;
            if (end > 0 && buffer[end - 1] == '\r') end--;
            for (int i = end - 1; i >= 0; i--)
            {
                if (buffer[i] is (byte)'\n' or (byte)'\r')
                {
                    previous = windowStart + i + 1;
                    return true;
                }
            }
        }
        else
        {
            end -= end % 2;
            bool be = _encoding.WebName == "utf-16BE";
            int Char(int i) => be ? buffer[i] << 8 | buffer[i + 1] : buffer[i + 1] << 8 | buffer[i];
            if (end >= 2 && Char(end - 2) == '\n') end -= 2;
            if (end >= 2 && Char(end - 2) == '\r') end -= 2;
            for (int i = end - 2; i >= 0; i -= 2)
            {
                if (Char(i) is '\n' or '\r')
                {
                    previous = windowStart + i + 2;
                    return true;
                }
            }
        }
        previous = windowStart <= _start ? _start : Align(windowStart);
        return true;
    }

    private void Layout()
    {
        _rows.Clear();
        _layoutIncomplete = false;
        if (_reader is null) return;
        long pos = _topLine;
        int skip = _topSub;
        int needed = VisibleRows + 1;
        while (_rows.Count < needed && pos < _reader.Length)
        {
            if (!TryReadLine(pos, out var text, out var next))
            {
                _layoutIncomplete = true;
                break;
            }
            var segs = Segments(text).ToList();
            for (int s = skip; s < segs.Count && _rows.Count < needed; s++)
                _rows.Add(new Row(pos, s, segs[s], next, s == segs.Count - 1));
            skip = 0;
            if (next <= pos) break;
            pos = next;
        }
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(_bg, new Rect(Bounds.Size));
        if (_reader is null) return;
        Layout();
        // Properties must not change during the render pass; the scrollbar follows right after it.
        if (!_scrollUpdatePosted)
        {
            _scrollUpdatePosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                _scrollUpdatePosted = false;
                UpdateScroll();
            }, DispatcherPriority.Background);
        }
        (IBrush Brush, Typeface Face) lineStyle = (_text, _typeface);
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            double y = i * _rowHeight;
            if (row.Text.Length == 0) continue;
            // A wrapped line keeps the style of its first row.
            if (row.Sub == 0 || i == 0) lineStyle = ReportStyle && row.Sub == 0 ? StyleOf(row.Text) : (_text, _typeface);
            DrawSelection(dc, i, row, y);
            if (_highlight is not null)
            {
                int idx = row.Text.IndexOf(_highlight, _highlightCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase);
                while (idx >= 0)
                {
                    dc.FillRectangle(_hit, new Rect(8 + idx * _charWidth, y, _highlight.Length * _charWidth, _rowHeight));
                    idx = row.Text.IndexOf(_highlight, idx + 1, _highlightCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase);
                }
            }
            var ft = new FormattedText(row.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, lineStyle.Face, _fontSize, lineStyle.Brush);
            dc.DrawText(ft, new Point(8, y + (_rowHeight - ft.Height) / 2));
        }
        if (_layoutIncomplete && _rows.Count < VisibleRows)
        {
            var ft = new FormattedText("Loading…", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, _muted);
            dc.DrawText(ft, new Point(8, _rows.Count * _rowHeight));
        }
        else if (_reader.Length == 0)
        {
            var ft = new FormattedText("The file is empty.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, _muted);
            dc.DrawText(ft, new Point(8, 0));
        }
    }

    private (IBrush, Typeface) StyleOf(string line)
    {
        if (line[0] == '⚠') return (_warning, _typeface);
        if (line[0] != ' ') return (_accent, _bold);
        return line.TrimStart().StartsWith('─') ? (_muted, _typeface) : (_text, _typeface);
    }

    private void DrawSelection(DrawingContext dc, int index, Row row, double y)
    {
        if (_selStart is not { } a || _selEnd is not { } b) return;
        var (s, e) = Compare(a, b) <= 0 ? (a, b) : (b, a);
        if (index < s.Row || index > e.Row) return;
        int from = index == s.Row ? s.Col : 0;
        int to = index == e.Row ? e.Col : row.Text.Length;
        if (to > from) dc.FillRectangle(_sel, new Rect(8 + from * _charWidth, y, (to - from) * _charWidth, _rowHeight));
    }

    private static int Compare((int Row, int Col) a, (int Row, int Col) b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.Col.CompareTo(b.Col);

    private void UpdateScroll()
    {
        if (_reader is null) return;
        _updatingScroll = true;
        long len = Math.Max(1, _reader.Length);
        _vbar.Maximum = len;
        _vbar.ViewportSize = Math.Max(1, _rows.Count > 0 ? _rows[^1].NextLine - _topLine : 1);
        _vbar.LargeChange = _vbar.ViewportSize;
        _vbar.SmallChange = Math.Max(1, _vbar.ViewportSize / Math.Max(1, VisibleRows));
        _vbar.Value = _topLine;
        _updatingScroll = false;
    }

    // ---- Navigation ----------------------------------------------------------------------------------------

    public void ScrollRows(int delta)
    {
        if (_reader is null) return;
        ClearSelection();
        for (int i = 0; i < Math.Abs(delta); i++)
        {
            if (delta > 0 && !StepDown()) break;
            if (delta < 0 && !StepUp()) break;
        }
        InvalidateVisual();
        PositionChanged?.Invoke();
    }

    private bool StepDown()
    {
        if (!TryReadLine(_topLine, out var text, out var next)) return false;
        int rows = Segments(text).Count();
        if (_topSub + 1 < rows)
        {
            _topSub++;
            return true;
        }
        if (next >= _reader!.Length || next <= _topLine) return false;
        _topLine = next;
        _topSub = 0;
        return true;
    }

    private bool StepUp()
    {
        if (_topSub > 0)
        {
            _topSub--;
            return true;
        }
        if (_topLine <= _start) return false;
        if (!TryFindPreviousLineStart(_topLine, out var prev)) return false;
        if (!TryReadLine(prev, out var text, out _)) return false;
        _topLine = prev;
        _topSub = Math.Max(0, Segments(text).Count() - 1);
        return true;
    }

    public void ScrollToOffset(long offset)
    {
        if (_reader is null) return;
        offset = Math.Clamp(Align(offset), _start, Math.Max(_start, _reader.Length));
        if (offset > _start && TryFindPreviousLineStart(offset + 1, out var ls) && ls <= offset) offset = ls;
        _topLine = offset;
        _topSub = 0;
        ClearSelection();
        InvalidateVisual();
        PositionChanged?.Invoke();
    }

    public void GoToEnd()
    {
        if (_reader is null) return;
        long pos = _reader.Length;
        _topLine = pos;
        _topSub = 0;
        for (int i = 0; i < VisibleRows - 1; i++) if (!StepUp()) break;
        if (_topLine >= _reader.Length) ScrollToOffset(Math.Max(_start, _reader.Length - 1));
        InvalidateVisual();
        PositionChanged?.Invoke();
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
            case Key.PageDown: case Key.Space when e.KeyModifiers == KeyModifiers.None: ScrollRows(VisibleRows - 1); break;
            case Key.PageUp: ScrollRows(-(VisibleRows - 1)); break;
            case Key.Home when ctrl: ScrollToOffset(_start); break;
            case Key.End when ctrl: GoToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollRows(-(int)Math.Round(e.Delta.Y * 3));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        _selStart = Hit(p);
        _selEnd = _selStart;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_selStart is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _selEnd = Hit(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private (int Row, int Col) Hit(Point p)
    {
        int row = Math.Clamp((int)(p.Y / _rowHeight), 0, Math.Max(0, _rows.Count - 1));
        int col = Math.Max(0, (int)Math.Round((p.X - 8) / _charWidth));
        if (row < _rows.Count) col = Math.Min(col, _rows[row].Text.Length);
        return (row, col);
    }

    private void ClearSelection()
    {
        _selStart = null;
        _selEnd = null;
    }

    /// <summary>Selected text, or the visible rows when nothing is selected.</summary>
    public string GetSelectedOrVisibleText()
    {
        if (_rows.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        if (_selStart is { } a && _selEnd is { } b && Compare(a, b) != 0)
        {
            var (s, e) = Compare(a, b) <= 0 ? (a, b) : (b, a);
            for (int i = s.Row; i <= e.Row && i < _rows.Count; i++)
            {
                var t = _rows[i].Text;
                int from = i == s.Row ? Math.Min(s.Col, t.Length) : 0;
                int to = i == e.Row ? Math.Min(e.Col, t.Length) : t.Length;
                sb.Append(t, from, Math.Max(0, to - from));
                if (i < e.Row && _rows[i].IsLastRowOfLine) sb.AppendLine();
            }
            return sb.ToString();
        }
        foreach (var r in _rows.Take(VisibleRows))
        {
            sb.Append(r.Text);
            if (r.IsLastRowOfLine) sb.AppendLine();
        }
        return sb.ToString();
    }
}
