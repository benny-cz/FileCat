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
/// Hex view over a <see cref="PagedReader"/> (plan §13.1): opens without scanning, addresses 64-bit offsets,
/// draws only visible rows, and never blocks the UI thread on I/O (missing bytes show as "··" until loaded).
/// </summary>
public sealed class HexView : Control
{
    public const int BytesPerRow = 16;
    private readonly ScrollBar _vbar;
    private PagedReader? _reader;
    private long _topRow;
    private long _cursor;
    private long _anchor = -1;
    private Typeface _typeface = new(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"));
    private double _fontSize = 13;
    private double _charWidth = 8;
    private double _rowHeight = 18;
    private bool _pendingInvalidate;
    private IBrush _text = Brushes.Black, _muted = Brushes.Gray, _selection = Brushes.LightBlue, _cursorBrush = Brushes.Blue, _bg = Brushes.White, _hit = Brushes.Yellow, _modified = Brushes.Orange;
    private (long Start, long Length)? _highlight;

    public HexView()
    {
        Focusable = true;
        ClipToBounds = true;
        CursorMoved += () => _automationPeer?.AnnounceCursor();
        _vbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Vertical, AllowAutoHide = false };
        _vbar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && _reader is not null)
            {
                long top = (long)Math.Round(_vbar.Value);
                if (top != _topRow)
                {
                    _topRow = top;
                    InvalidateVisual();
                }
            }
        };
        VisualChildren.Add(_vbar);
        LogicalChildren.Add(_vbar);
    }

    public event Action? CursorMoved;
    /// <summary>Optional sparse edit marker; queried only for visible bytes.</summary>
    public Func<long, bool>? IsModified { get; set; }

    /// <summary>Editing: the high nibble typed at the cursor, shown as "A_" until the low nibble arrives (-1: none).</summary>
    public int PendingNibble
    {
        get => _pendingNibble;
        set { _pendingNibble = value; InvalidateVisual(); }
    }

    /// <summary>Editing: typing goes to the text column; the cursor there is drawn as the stronger one.</summary>
    public bool TextColumnActive
    {
        get => _textColumnActive;
        set
        {
            if (_textColumnActive == value) return;
            _textColumnActive = value;
            InvalidateVisual();
            CursorMoved?.Invoke();
        }
    }

    /// <summary>Clicks choose the active column (hex or text) for an editor.</summary>
    public bool TracksActiveColumn { get; set; }

    private int _pendingNibble = -1;
    private bool _textColumnActive;

    public void ClearSelection()
    {
        if (_anchor < 0) return;
        _anchor = -1;
        InvalidateVisual();
        CursorMoved?.Invoke();
    }

    private HexViewAutomationPeer? _automationPeer;

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => _automationPeer = new HexViewAutomationPeer(this);

    /// <summary>The cursor byte and its row, for assistive technology (never blocks on I/O).</summary>
    internal string DescribeCursor()
    {
        if (_reader is null) return "No content";
        long row = _cursor / BytesPerRow * BytesPerRow;
        Span<byte> bytes = stackalloc byte[BytesPerRow];
        _reader.TryRead(row, bytes, out int n);
        int at = (int)(_cursor - row);
        var hex = string.Join(" ", bytes[..n].ToArray().Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
        var value = at < n ? $"byte {bytes[at]:X2} hex" : "byte not loaded yet";
        return $"Offset {_cursor:N0} of {_reader.Length:N0}, {value}; row {row:X8}: {hex}";
    }

    public long CursorOffset => _cursor;

    public (long Start, long Length) Selection
    {
        get
        {
            if (_anchor < 0) return (_cursor, 0);
            long a = Math.Min(_anchor, _cursor), b = Math.Max(_anchor, _cursor);
            return (a, b - a + 1);
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = Math.Clamp(value, 8, 32);
            Measure();
            InvalidateVisual();
        }
    }

    public void SetReader(PagedReader? reader)
    {
        if (_reader is not null) _reader.PageLoaded -= OnPageLoaded;
        _reader = reader;
        if (reader is not null) reader.PageLoaded += OnPageLoaded;
        _topRow = 0;
        _cursor = 0;
        _anchor = -1;
        Measure();
        UpdateScroll();
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
        _selection = B("FcFocusBackground", Brushes.LightBlue);
        _cursorBrush = B("FcFocusBorder", Brushes.Blue);
        _bg = B("FcPanel", Brushes.White);
        _hit = B("FcSearchHit", Brushes.Yellow);
        _modified = B("FcChangedByte", Brushes.Orange);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResolveBrushes();
    }

    private void Measure()
    {
        var ft = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
        _charWidth = ft.WidthIncludingTrailingWhitespace;
        _rowHeight = Math.Ceiling(_fontSize * 1.45);
    }

    private long RowCount => _reader is null ? 0 : (_reader.Length + BytesPerRow - 1) / BytesPerRow;
    private int VisibleRows => Math.Max(1, (int)(Bounds.Height / _rowHeight));
    private int OffsetDigits => _reader is null || _reader.Length <= 0xFFFFFFFFL ? 8 : _reader.Length <= 0xFFFFFFFFFFFFL ? 12 : 16;

    /// <summary>Room left of the offsets, so they do not touch the window's edge.</summary>
    private const double Gutter = 8;

    private double HexColumnX => Gutter + (OffsetDigits + 2) * _charWidth;

    protected override Size ArrangeOverride(Size finalSize)
    {
        double sbw = _vbar.DesiredSize.Width > 0 ? _vbar.DesiredSize.Width : 12;
        _vbar.Arrange(new Rect(finalSize.Width - sbw, 0, sbw, finalSize.Height));
        UpdateScroll();
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _vbar.Measure(availableSize);
        return new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
    }

    private void UpdateScroll()
    {
        long max = Math.Max(0, RowCount - VisibleRows);
        _topRow = Math.Clamp(_topRow, 0, max);
        _vbar.Maximum = max;
        _vbar.ViewportSize = VisibleRows;
        _vbar.LargeChange = Math.Max(1, VisibleRows - 1);
        _vbar.SmallChange = 1;
        _vbar.Value = _topRow;
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(_bg, new Rect(Bounds.Size));
        if (_reader is null) return;
        int digits = OffsetDigits;
        double xHex = HexColumnX;
        double xText = TextColumnX;
        var sel = Selection;
        var buffer = new byte[BytesPerRow];
        long len = _reader.Length;
        for (int r = 0; r <= VisibleRows; r++)
        {
            long row = _topRow + r;
            long offset = row * BytesPerRow;
            if (offset >= len && len > 0 || len == 0 && r > 0) break;
            double y = r * _rowHeight;
            bool ok = _reader.TryRead(offset, buffer, out int n);
            DrawText(dc, offset.ToString("X" + digits, CultureInfo.InvariantCulture), Gutter, y, _muted);
            var hex = new StringBuilder(BytesPerRow * 3 + 2);
            var text = new StringBuilder(BytesPerRow);
            for (int i = 0; i < BytesPerRow; i++)
            {
                long pos = offset + i;
                if (pos >= len)
                {
                    hex.Append("   ");
                    continue;
                }
                bool inSel = sel.Length > 0 && pos >= sel.Start && pos < sel.Start + sel.Length;
                bool isHit = _highlight is { } h && pos >= h.Start && pos < h.Start + h.Length;
                double bx = xHex + (i * 3 + (i >= 8 ? 1 : 0)) * _charWidth;
                double tx = xText + i * _charWidth;
                bool modified = IsModified?.Invoke(pos) == true;
                if (inSel || isHit || modified)
                {
                    var brush = isHit ? _hit : inSel ? _selection : _modified;
                    dc.FillRectangle(brush, new Rect(bx - 1, y, _charWidth * 2 + 2, _rowHeight));
                    dc.FillRectangle(brush, new Rect(tx, y, _charWidth, _rowHeight));
                }
                if (modified)
                {
                    // A non-color cue as well (plan PI-04): modified bytes are underlined in both columns.
                    var mark = new Pen(_text, 1.5);
                    dc.DrawLine(mark, new Point(bx, y + _rowHeight - 1.5), new Point(bx + _charWidth * 2, y + _rowHeight - 1.5));
                    dc.DrawLine(mark, new Point(tx, y + _rowHeight - 1.5), new Point(tx + _charWidth, y + _rowHeight - 1.5));
                }
                if (pos == _cursor)
                {
                    double hexPen = TracksActiveColumn && _textColumnActive ? 1 : 1.5, textPen = TracksActiveColumn && _textColumnActive ? 2 : 1;
                    dc.DrawRectangle(null, new Pen(_cursorBrush, hexPen), new Rect(bx - 1, y + 0.5, _charWidth * 2 + 2, _rowHeight - 1));
                    dc.DrawRectangle(null, new Pen(_cursorBrush, textPen), new Rect(tx, y + 0.5, _charWidth, _rowHeight - 1));
                }
                if (pos == _cursor && _pendingNibble >= 0)
                {
                    hex.Append(_pendingNibble.ToString("X", CultureInfo.InvariantCulture)).Append("_ ");
                    text.Append(i < n ? Printable(buffer[i]) : '·');
                }
                else if (i < n)
                {
                    byte b = buffer[i];
                    hex.Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                    text.Append(Printable(b));
                }
                else
                {
                    hex.Append("·· ");
                    text.Append('·');
                }
                if (i == 7) hex.Append(' ');
            }
            DrawText(dc, hex.ToString(), xHex, y, ok ? _text : _muted);
            DrawText(dc, text.ToString(), xText, y, ok ? _text : _muted);
        }
    }

    private static char Printable(byte b) => b is >= 0x20 and < 0x7F ? (char)b : b >= 0xA0 ? (char)b : '.';

    private void DrawText(DrawingContext dc, string s, double x, double y, IBrush brush)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush);
        dc.DrawText(ft, new Point(x, y + (_rowHeight - ft.Height) / 2));
    }

    public void GoTo(long offset, bool select = false, long length = 0)
    {
        if (_reader is null) return;
        long max = Math.Max(0, _reader.Length - 1);
        offset = Math.Clamp(offset, 0, max);
        if (select && length > 0)
        {
            _anchor = offset;
            _cursor = Math.Min(max, offset + length - 1);
        }
        else
        {
            _anchor = -1;
            _cursor = offset;
        }
        _highlight = length > 0 ? (offset, length) : null;
        EnsureVisible();
    }

    private void EnsureVisible()
    {
        long row = _cursor / BytesPerRow;
        if (row < _topRow) _topRow = row;
        else if (row >= _topRow + VisibleRows) _topRow = row - VisibleRows + 1;
        UpdateScroll();
        InvalidateVisual();
        CursorMoved?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_reader is null || e.Handled) return;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        long len = _reader.Length;
        long target = e.Key switch
        {
            Key.Left => _cursor - 1,
            Key.Right => _cursor + 1,
            Key.Up => _cursor - BytesPerRow,
            Key.Down => _cursor + BytesPerRow,
            Key.PageUp => _cursor - BytesPerRow * (VisibleRows - 1),
            Key.PageDown => _cursor + BytesPerRow * (VisibleRows - 1),
            Key.Home => ctrl ? 0 : _cursor - _cursor % BytesPerRow,
            Key.End => ctrl ? len - 1 : _cursor - _cursor % BytesPerRow + BytesPerRow - 1,
            _ => long.MinValue,
        };
        if (target == long.MinValue) return;
        e.Handled = true;
        if (shift && _anchor < 0) _anchor = _cursor;
        if (!shift) _anchor = -1;
        _highlight = null;
        _cursor = Math.Clamp(target, 0, Math.Max(0, len - 1));
        EnsureVisible();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        long pos = HitTest(p);
        if (pos < 0) return;
        if (TracksActiveColumn) _textColumnActive = p.X >= TextColumnX;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            if (_anchor < 0) _anchor = _cursor;
        }
        else _anchor = -1;
        _cursor = pos;
        _highlight = null;
        EnsureVisible();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        long pos = HitTest(e.GetPosition(this));
        if (pos < 0 || pos == _cursor) return;
        if (_anchor < 0) _anchor = _cursor;
        _cursor = pos;
        EnsureVisible();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private double TextColumnX => HexColumnX + (BytesPerRow * 3 + 2) * _charWidth;

    private long HitTest(Point p)
    {
        if (_reader is null) return -1;
        long row = _topRow + (long)(p.Y / _rowHeight);
        double xHex = HexColumnX;
        double xText = TextColumnX;
        int col;
        if (p.X >= xText) col = (int)((p.X - xText) / _charWidth);
        else
        {
            double rel = (p.X - xHex) / _charWidth;
            if (rel > 24) rel -= 1;
            col = (int)(rel / 3);
        }
        col = Math.Clamp(col, 0, BytesPerRow - 1);
        return Math.Clamp(row * BytesPerRow + col, 0, Math.Max(0, _reader.Length - 1));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _topRow -= (long)Math.Round(e.Delta.Y * 3);
        UpdateScroll();
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>Selected bytes (bounded) for copy; background read.</summary>
    public byte[] ReadSelection(int max = 16 * 1024 * 1024)
    {
        if (_reader is null) return [];
        var (start, length) = Selection;
        if (length == 0) length = 1;
        var buf = new byte[(int)Math.Min(length, max)];
        int n = _reader.Read(start, buf);
        return buf[..n];
    }
}
