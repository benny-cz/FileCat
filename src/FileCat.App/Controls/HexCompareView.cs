using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Compare;
using FileCat.Core.Content;

namespace FileCat.App.Controls;

/// <summary>
/// Two files' bytes side by side (plan §16.2, TV-08; after Salamander's File Comparator): offsets, hex in groups of four
/// and text on each side, as many bytes per row as fit in half the width. Rows pair the bytes at the same offsets, or,
/// aligned, the stretches an aligned comparison found (where one side has bytes the other lacks, the other side is
/// empty). Bytes that differ are highlighted and underlined, the current difference strongest and outlined. Reads go
/// through paged readers and never block the UI thread (bytes not read yet show as "··"). Dragging over bytes selects
/// them on that side, for copying.
/// </summary>
public sealed class HexCompareView : Control
{
    /// <summary>At most this many selected bytes are copied.</summary>
    public const int MaxCopy = 4 << 20;
    private const double Gutter = 10;
    private const double Divider = 9;
    private const double RightMargin = 4;
    private static readonly IBrush ChangedFill = Tint(0xE0, 0xB0, 0x30, 0x50), ChangedCurrent = Tint(0xE0, 0xB0, 0x30, 0x9C);
    private static readonly IBrush LeftFill = Tint(0xE0, 0x40, 0x40, 0x48), LeftCurrent = Tint(0xE0, 0x40, 0x40, 0x90);
    private static readonly IBrush RightFill = Tint(0x40, 0xC0, 0x40, 0x48), RightCurrent = Tint(0x40, 0xC0, 0x40, 0x90);
    private static readonly IBrush UnalignedFill = Tint(0x90, 0x90, 0xA0, 0x40), UnalignedCurrent = Tint(0x90, 0x90, 0xA0, 0x80);
    private static readonly IBrush GapFill = Tint(0x90, 0x90, 0xA0, 0x1C);

    private readonly ScrollBar _vbar;
    private readonly Typeface _typeface = new(new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"));
    private readonly double _fontSize = 12;
    private double _charWidth = 7, _rowHeight = 17;
    private PagedReader? _left, _right;
    /// <summary>Aligned stretches in file order, or one stretch over both files for the same offsets.</summary>
    private IReadOnlyList<AlignedRange> _stretches = [];
    private bool _sameOffsets = true;
    /// <summary>The first row of each stretch, and the row count at the end.</summary>
    private long[] _rowStart = [0];
    private int _bytesPerRow = 16;
    private long _topRow;
    private IReadOnlyList<AlignedRange> _differences = [];
    private AlignedRange? _current;
    private int _selectionSide = -1;
    private long _anchor, _caret;
    private bool _pendingInvalidate, _dragging;
    private IBrush _text = Brushes.Black, _muted = Brushes.Gray, _selection = Brushes.LightBlue, _accent = Brushes.Blue, _bg = Brushes.White, _divider = Brushes.Gray;
    private string _status = "";

    public HexCompareView()
    {
        Focusable = true;
        ClipToBounds = true;
        _vbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Vertical, AllowAutoHide = false };
        _vbar.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            long top = (long)Math.Round(_vbar.Value);
            if (top == _topRow) return;
            _topRow = top;
            InvalidateVisual();
        };
        VisualChildren.Add(_vbar);
        LogicalChildren.Add(_vbar);
        var typeface = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
        _charWidth = typeface.WidthIncludingTrailingWhitespace;
        _rowHeight = Math.Ceiling(_fontSize * 1.45);
        ContextMenu = BuildMenu();
    }

    /// <summary>A difference's bytes were clicked: its index in the differences given.</summary>
    public event Action<int>? DifferenceClicked;

    /// <summary>What the view says about the selection or the last copy (shown under it).</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            StatusChanged?.Invoke();
        }
    }

    public event Action? StatusChanged;

    public int BytesPerRow => _bytesPerRow;

    /// <summary>The first row shown (tests).</summary>
    public long TopRow => _topRow;

    public long RowCount => _rowStart[^1];

    /// <summary>The selected bytes: the side (0 left, 1 right) and the range, or null.</summary>
    public (int Side, long Start, long Length)? Selection =>
        _selectionSide < 0 ? null : (_selectionSide, Math.Min(_anchor, _caret), Math.Abs(_caret - _anchor) + 1);

    /// <summary>
    /// Shows two files' bytes: at the same offsets (<paramref name="aligned"/> null), or in an aligned comparison's
    /// stretches. <paramref name="differences"/> are the ones a click on their bytes chooses (in file order).
    /// </summary>
    public void SetContent(PagedReader? left, PagedReader? right, IReadOnlyList<AlignedRange>? aligned, IReadOnlyList<AlignedRange> differences)
    {
        if (_left is not null) _left.PageLoaded -= OnPageLoaded;
        if (_right is not null) _right.PageLoaded -= OnPageLoaded;
        _left = left;
        _right = right;
        if (left is not null) left.PageLoaded += OnPageLoaded;
        if (right is not null) right.PageLoaded += OnPageLoaded;
        _sameOffsets = aligned is null;
        _stretches = aligned ?? (left is null || right is null ? [] : [new AlignedRange(DiffKind.Equal, 0, left.Length, 0, right.Length)]);
        _differences = differences;
        _current = null;
        _selectionSide = -1;
        _topRow = 0;
        Status = "";
        Layout(Bounds.Width, keepTop: false, force: true);
        InvalidateVisual();
    }

    /// <summary>Highlights a difference as the current one and scrolls to it when it is not in view (with rows before it).</summary>
    public void ShowDifference(AlignedRange? difference)
    {
        _current = difference;
        if (difference is not null)
        {
            long row = RowOf(difference);
            long rows = RowsOf(difference);
            int visible = VisibleRows;
            if (row < _topRow || row + Math.Min(rows, visible) > _topRow + visible) _topRow = Math.Max(0, row - Math.Min(3, visible / 4));
            UpdateScroll();
        }
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.ThemeChanged += ResolveBrushes;
        ResolveBrushes();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.ThemeChanged -= ResolveBrushes;
    }

    private void ResolveBrushes()
    {
        IBrush B(string k, IBrush f) => this.TryFindResource(k, ActualThemeVariant, out var v) && v is IBrush b ? b : f;
        _text = B("FcText", Brushes.Black);
        _muted = B("FcTextMuted", Brushes.Gray);
        _selection = B("FcFocusBackground", Brushes.LightBlue);
        _accent = B("FcActiveAccent", Brushes.Blue);
        _bg = B("FcPanel", Brushes.White);
        _divider = B("FcBorder", Brushes.Gray);
        if (ContextMenu is { } menu)
            foreach (var item in menu.Items.OfType<MenuItem>())
                if (item.Tag is string icon) item.Icon = MenuIcon(icon);
        InvalidateVisual();
    }

    private static SolidColorBrush Tint(byte r, byte g, byte b, byte a) => new(Color.FromArgb(a, r, g, b));

    private long MaxLength => Math.Max(_left?.Length ?? 0, _right?.Length ?? 0);
    private int OffsetDigits => MaxLength <= 0xFFFFFFFFL ? 8 : MaxLength <= 0xFFFFFFFFFFFFL ? 12 : 16;
    private double ScrollBarWidth => _vbar.DesiredSize.Width > 0 ? _vbar.DesiredSize.Width : 12;
    private double PaneWidth => Math.Max(0, (Bounds.Width - ScrollBarWidth - Divider) / 2);
    private double HexX => Gutter + (OffsetDigits + 2) * _charWidth;
    private double TextX => HexX + (_bytesPerRow * 3 + _bytesPerRow / 4) * _charWidth;
    private int VisibleRows => Math.Max(1, (int)(Bounds.Height / _rowHeight));

    /// <summary>The character column of byte <paramref name="i"/> in the hex column: three per byte, one more between groups of four.</summary>
    private static int HexColumn(int i) => i * 3 + i / 4;

    protected override Size MeasureOverride(Size availableSize)
    {
        _vbar.Measure(availableSize);
        return new Size(double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double sbw = ScrollBarWidth;
        _vbar.Arrange(new Rect(finalSize.Width - sbw, 0, sbw, finalSize.Height));
        Layout(finalSize.Width, keepTop: true);
        return finalSize;
    }

    /// <summary>
    /// Bytes per row: as many groups of four as fit in half the width (Salamander's rule), at least one and at most sixteen.
    /// Rows are counted anew when that changes, and the first row shown keeps showing the same bytes.
    /// </summary>
    private void Layout(double width, bool keepTop, bool force = false)
    {
        double pane = Math.Max(0, (width - ScrollBarWidth - Divider) / 2);
        double available = pane - Gutter - RightMargin - (OffsetDigits + 2) * _charWidth;
        int bytes = Math.Clamp((int)(available / (17 * _charWidth)), 1, 16) * 4;
        if (force || bytes != _bytesPerRow)
        {
            // The first byte of the top row, to find its row again.
            int topStretch = keepTop && _rowStart[^1] > 0 ? StretchOf(_topRow) : -1;
            long topByte = topStretch >= 0 ? (_topRow - _rowStart[topStretch]) * _bytesPerRow : 0;
            _bytesPerRow = bytes;
            var starts = new long[_stretches.Count + 1];
            long row = 0;
            for (int i = 0; i < _stretches.Count; i++)
            {
                starts[i] = row;
                row += RowsOf(_stretches[i]);
            }
            starts[^1] = row;
            _rowStart = starts;
            _topRow = topStretch >= 0 ? starts[topStretch] + topByte / bytes : 0;
        }
        UpdateScroll();
    }

    private long RowsOf(AlignedRange stretch)
    {
        long length = Math.Max(stretch.LeftLength, stretch.RightLength);
        return length <= 0 ? 0 : (length + _bytesPerRow - 1) / _bytesPerRow;
    }

    /// <summary>The stretch that row <paramref name="row"/> is in.</summary>
    private int StretchOf(long row)
    {
        // The last stretch that starts at or before the row (stretches of no rows start where the next one does).
        int lo = 0, hi = _stretches.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_rowStart[mid] <= row) lo = mid + 1;
            else hi = mid;
        }
        return Math.Clamp(lo - 1, 0, Math.Max(0, _stretches.Count - 1));
    }

    /// <summary>The row a difference starts on.</summary>
    private long RowOf(AlignedRange difference)
    {
        if (_stretches.Count == 0) return 0;
        if (_sameOffsets) return Math.Max(difference.LeftOffset, difference.RightOffset) / _bytesPerRow;
        // Offsets on both sides grow with every stretch, so their sum orders them.
        long key = difference.LeftOffset + difference.RightOffset;
        int lo = 0, hi = _stretches.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_stretches[mid].LeftOffset + _stretches[mid].RightOffset < key) lo = mid + 1;
            else hi = mid;
        }
        return _rowStart[lo];
    }

    private void UpdateScroll()
    {
        long max = Math.Max(0, _rowStart[^1] - VisibleRows);
        _topRow = Math.Clamp(_topRow, 0, max);
        _vbar.Maximum = max;
        _vbar.ViewportSize = VisibleRows;
        _vbar.LargeChange = Math.Max(1, VisibleRows - 1);
        _vbar.SmallChange = 1;
        _vbar.Value = _topRow;
    }

    /// <summary>The bytes of a row on each side: where they start and how many there are.</summary>
    private (AlignedRange Stretch, long Left, int LeftCount, long Right, int RightCount) RowBytes(long row)
    {
        int s = StretchOf(row);
        var stretch = _stretches[s];
        long skip = (row - _rowStart[s]) * _bytesPerRow;
        return (stretch, stretch.LeftOffset + skip, (int)Math.Clamp(stretch.LeftLength - skip, 0, _bytesPerRow),
            stretch.RightOffset + skip, (int)Math.Clamp(stretch.RightLength - skip, 0, _bytesPerRow));
    }

    private bool PerByte(AlignedRange stretch) =>
        _sameOffsets || stretch.Kind == DiffKind.Equal || stretch.Kind == DiffKind.Changed && stretch.LeftLength == stretch.RightLength;

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(_bg, new Rect(Bounds.Size));
        if (_left is null || _right is null || _stretches.Count == 0) return;
        double pane = PaneWidth;
        double middle = Math.Round(pane + Divider / 2) + 0.5;
        dc.DrawLine(new Pen(_divider, 1), new Point(middle, 0), new Point(middle, Bounds.Height));
        var leftBytes = new byte[_bytesPerRow];
        var rightBytes = new byte[_bytesPerRow];
        long rows = _rowStart[^1];
        for (int r = 0; r <= VisibleRows && _topRow + r < rows; r++)
        {
            var (stretch, left, leftCount, right, rightCount) = RowBytes(_topRow + r);
            _left.TryRead(left, leftBytes.AsSpan(0, leftCount), out int leftRead);
            _right.TryRead(right, rightBytes.AsSpan(0, rightCount), out int rightRead);
            double y = r * _rowHeight;
            var l = new RowSide(0, left, leftCount, leftRead, leftBytes);
            var ri = new RowSide(1, right, rightCount, rightRead, rightBytes);
            DrawSide(dc, 0, y, stretch, l, ri);
            DrawSide(dc, pane + Divider, y, stretch, ri, l);
        }
    }

    private readonly record struct RowSide(int Side, long Offset, int Count, int Read, byte[] Bytes);

    /// <summary>How one byte shows: 0 as it is, else the way it differs (1 changed, 2 only left, 3 only right, 4 not aligned); and whether it is current or selected.</summary>
    private (int Kind, bool Current, bool Selected) Mark(AlignedRange stretch, RowSide mine, RowSide other, int i)
    {
        int kind;
        if (PerByte(stretch))
        {
            if (i < other.Count)
                // Bytes not read yet on either side are not called different.
                kind = i < mine.Read && i < other.Read && mine.Bytes[i] != other.Bytes[i] ? 1 : 0;
            else kind = mine.Side == 0 ? 2 : 3;
        }
        else kind = stretch.Kind switch { DiffKind.Changed => 1, DiffKind.LeftOnly => 2, DiffKind.RightOnly => 3, DiffKind.Unaligned => 4, _ => 0 };
        long at = mine.Offset + i;
        bool current = kind != 0 && _current is { } c && (mine.Side == 0 ? at >= c.LeftOffset && at < c.LeftOffset + c.LeftLength : at >= c.RightOffset && at < c.RightOffset + c.RightLength);
        bool selected = mine.Side == _selectionSide && at >= Math.Min(_anchor, _caret) && at <= Math.Max(_anchor, _caret);
        return (kind, current, selected);
    }

    private static IBrush? Fill(int kind, bool current) => kind switch
    {
        1 => current ? ChangedCurrent : ChangedFill,
        2 => current ? LeftCurrent : LeftFill,
        3 => current ? RightCurrent : RightFill,
        4 => current ? UnalignedCurrent : UnalignedFill,
        _ => null,
    };

    private void DrawSide(DrawingContext dc, double x0, double y, AlignedRange stretch, RowSide mine, RowSide other)
    {
        double pane = PaneWidth;
        if (mine.Count == 0)
        {
            // Bytes the other side has and this one lacks.
            if (other.Count > 0) dc.FillRectangle(GapFill, new Rect(x0 + Gutter / 2, y, pane - Gutter / 2, _rowHeight));
            return;
        }
        double cw = _charWidth;
        double xHex = x0 + HexX, xText = x0 + TextX;
        bool anyCurrent = false;
        var underline = new Pen(_text, 1);
        for (int i = 0; i < mine.Count;)
        {
            var mark = Mark(stretch, mine, other, i);
            int j = i + 1;
            while (j < mine.Count && Mark(stretch, mine, other, j) == mark) j++;
            var fill = mark.Selected ? _selection : Fill(mark.Kind, mark.Current);
            double hx0 = xHex + HexColumn(i) * cw - 2, hx1 = xHex + (HexColumn(j - 1) + 2) * cw + 2;
            double tx0 = xText + i * cw, tx1 = xText + j * cw;
            if (fill is not null)
            {
                dc.FillRectangle(fill, new Rect(hx0, y, hx1 - hx0, _rowHeight));
                dc.FillRectangle(fill, new Rect(tx0, y, tx1 - tx0, _rowHeight));
            }
            if (mark.Kind != 0)
            {
                // Not by color alone (plan PI-04): differing bytes are underlined in both columns.
                double uy = y + _rowHeight - 1.5;
                dc.DrawLine(underline, new Point(hx0 + 2, uy), new Point(hx1 - 2, uy));
                dc.DrawLine(underline, new Point(tx0, uy), new Point(tx1, uy));
            }
            if (mark.Current)
            {
                anyCurrent = true;
                var outline = new Pen(_accent, 1);
                dc.DrawRectangle(null, outline, new Rect(hx0 + 0.5, y + 0.5, hx1 - hx0 - 1, _rowHeight - 1));
                dc.DrawRectangle(null, outline, new Rect(tx0 + 0.5, y + 0.5, tx1 - tx0 - 1, _rowHeight - 1));
            }
            i = j;
        }
        // The current difference's rows are marked at the edge, too.
        if (anyCurrent) dc.FillRectangle(_accent, new Rect(x0 + 2, y + 1, 3, _rowHeight - 2));
        var hex = new StringBuilder(_bytesPerRow * 4);
        var text = new StringBuilder(_bytesPerRow);
        for (int i = 0; i < mine.Count; i++)
        {
            if (i > 0) hex.Append(i % 4 == 0 ? "  " : " ");
            if (i < mine.Read)
            {
                hex.Append(mine.Bytes[i].ToString("X2", CultureInfo.InvariantCulture));
                text.Append(Printable(mine.Bytes[i]));
            }
            else
            {
                hex.Append("··");
                text.Append('·');
            }
        }
        var brush = mine.Read < mine.Count ? _muted : _text;
        DrawText(dc, mine.Offset.ToString("X" + OffsetDigits, CultureInfo.InvariantCulture), x0 + Gutter, y, _muted);
        DrawText(dc, hex.ToString(), xHex, y, brush);
        DrawText(dc, text.ToString(), xText, y, brush);
    }

    internal static char Printable(byte b) => b is >= 0x20 and < 0x7F ? (char)b : b >= 0xA0 ? (char)b : '.';

    private void DrawText(DrawingContext dc, string s, double x, double y, IBrush brush)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush);
        dc.DrawText(ft, new Point(x, y + (_rowHeight - ft.Height) / 2));
    }

    /// <summary>The byte under a point: its side and offset, or null outside the bytes.</summary>
    private (int Side, long Offset, AlignedRange Stretch)? HitTest(Point p)
    {
        if (_left is null || _right is null || _stretches.Count == 0) return null;
        long row = _topRow + (long)Math.Floor(p.Y / _rowHeight);
        if (row < 0 || row >= _rowStart[^1]) return null;
        double pane = PaneWidth;
        int side = p.X < pane + Divider / 2 ? 0 : 1;
        double x = p.X - (side == 0 ? 0 : pane + Divider);
        var (stretch, left, leftCount, right, rightCount) = RowBytes(row);
        int count = side == 0 ? leftCount : rightCount;
        if (count == 0) return null;
        int i;
        if (x >= TextX) i = (int)((x - TextX) / _charWidth);
        else if (x >= HexX - _charWidth)
        {
            int column = Math.Max(0, (int)((x - HexX + _charWidth / 2) / _charWidth));
            i = column / 13 * 4 + Math.Min(3, column % 13 / 3);
        }
        else i = 0;
        i = Math.Clamp(i, 0, count - 1);
        return (side, (side == 0 ? left : right) + i, stretch);
    }

    /// <summary>The middle of a byte's hex digits, when its row is in view (tests).</summary>
    internal Point? PointOf(int side, long offset)
    {
        for (int r = 0; r <= VisibleRows && _topRow + r < _rowStart[^1]; r++)
        {
            var (_, left, leftCount, right, rightCount) = RowBytes(_topRow + r);
            long start = side == 0 ? left : right;
            int count = side == 0 ? leftCount : rightCount;
            if (offset >= start && offset < start + count)
                return new Point((side == 0 ? 0 : PaneWidth + Divider) + HexX + (HexColumn((int)(offset - start)) + 1) * _charWidth, (r + 0.5) * _rowHeight);
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (HitTest(point.Position) is not { } hit) return;
        if (point.Properties.IsRightButtonPressed)
        {
            // The menu copies the selection; a click outside it selects the byte clicked.
            if (Selection is not { } s || s.Side != hit.Side || hit.Offset < s.Start || hit.Offset >= s.Start + s.Length) Select(hit.Side, hit.Offset, hit.Offset);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && _selectionSide == hit.Side) Select(hit.Side, _anchor, hit.Offset);
        else Select(hit.Side, hit.Offset, hit.Offset);
        _dragging = true;
        e.Pointer.Capture(this);
        // A difference's bytes make it the current one.
        if (DifferenceAt(hit.Side, hit.Offset, hit.Stretch) is int index) DifferenceClicked?.Invoke(index);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _selectionSide < 0) return;
        var p = e.GetPosition(this);
        // Past the top or bottom edge, the view scrolls on with the selection.
        if (p.Y < 0) ScrollBy(-1);
        else if (p.Y > Bounds.Height) ScrollBy(1);
        var clamped = new Point(p.X, Math.Clamp(p.Y, 0, Math.Max(0, Bounds.Height - 1)));
        if (HitTest(clamped) is { } hit && hit.Side == _selectionSide && hit.Offset != _caret) Select(_selectionSide, _anchor, hit.Offset);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollBy(-(long)Math.Round(e.Delta.Y * 3));
        e.Handled = true;
    }

    private void ScrollBy(long rows)
    {
        _topRow += rows;
        UpdateScroll();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || (e.KeyModifiers & KeyModifiers.Alt) != 0) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        int page = Math.Max(1, VisibleRows - 1);
        switch (e.Key)
        {
            case Key.Up: ScrollBy(-1); break;
            case Key.Down: ScrollBy(1); break;
            case Key.PageUp: ScrollBy(-page); break;
            case Key.PageDown: ScrollBy(page); break;
            case Key.Home: ScrollBy(-_topRow); break;
            case Key.End: ScrollBy(_rowStart[^1]); break;
            case Key.C when ctrl:
            case Key.Insert when ctrl:
                _ = CopyAsync(asText: false);
                break;
            case Key.A when ctrl:
                SelectAll();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Select(int side, long anchor, long caret)
    {
        _selectionSide = side;
        _anchor = anchor;
        _caret = caret;
        long length = Math.Abs(caret - anchor) + 1;
        long start = Math.Min(anchor, caret);
        Status = $"{(side == 0 ? "Left" : "Right")} file: {Size(length)} selected at 0x{start:X}" +
                 (length > 1 ? $"–0x{start + length - 1:X}" : "") + ". Ctrl+C copies them as hex; the context menu has more.";
        InvalidateVisual();
    }

    /// <summary>Ctrl+A: the whole file on the side selected last (the left one first).</summary>
    private void SelectAll()
    {
        int side = Math.Max(0, _selectionSide);
        long length = (side == 0 ? _left : _right)?.Length ?? 0;
        if (length > 0) Select(side, 0, length - 1);
    }

    private static string Size(long bytes) => bytes == 1 ? "1 byte" : $"{bytes:N0} bytes";

    /// <summary>The listed difference whose bytes include this one.</summary>
    private int? DifferenceAt(int side, long offset, AlignedRange stretch)
    {
        if (_sameOffsets)
        {
            // Listed in offset order: the last one that starts at or before the byte.
            int lo = 0, hi = _differences.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                var d = _differences[mid];
                if ((side == 0 ? d.LeftOffset : d.RightOffset) <= offset) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0) return null;
            var found = _differences[lo - 1];
            long start = side == 0 ? found.LeftOffset : found.RightOffset, length = side == 0 ? found.LeftLength : found.RightLength;
            return offset < start + length ? lo - 1 : null;
        }
        if (stretch.Kind == DiffKind.Equal) return null;
        long key = stretch.LeftOffset + stretch.RightOffset;
        int a = 0, b = _differences.Count;
        while (a < b)
        {
            int mid = (a + b) >>> 1;
            if (_differences[mid].LeftOffset + _differences[mid].RightOffset < key) a = mid + 1;
            else b = mid;
        }
        return a < _differences.Count && _differences[a].LeftOffset + _differences[a].RightOffset == key ? a : null;
    }

    private ContextMenu BuildMenu()
    {
        MenuItem Item(string header, string? gesture, string icon, Action run)
        {
            var item = new MenuItem { Header = header, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture), Tag = icon, Icon = MenuIcon(icon) };
            item.Click += (_, _) => run();
            return item;
        }
        var menu = new ContextMenu
        {
            Items =
            {
                Item("Copy as hex", "Ctrl+C", "compare.copyHex", () => _ = CopyAsync(asText: false)),
                Item("Copy as text", null, "compare.copyText", () => _ = CopyAsync(asText: true)),
                Item("Copy offset", null, "compare.copyOffset", () => _ = CopyOffsetAsync()),
                new Separator(),
                Item("Select all on this side", "Ctrl+A", "compare.selectAll", SelectAll),
            },
        };
        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>().Where(i => i.Tag is not "compare.selectAll")) item.IsEnabled = _selectionSide >= 0;
        };
        return menu;
    }

    private static Image? MenuIcon(string id) => CommandIcons.Get(id) is { } icon ? new Image { Source = icon, Width = 16, Height = 16 } : null;

    /// <summary>
    /// Copies the selected bytes, as hex ("4D 5A 90") or as the text column shows them. Read off the UI thread; at most
    /// <see cref="MaxCopy"/> bytes, and the status says when the selection was longer.
    /// </summary>
    public async Task CopyAsync(bool asText)
    {
        if (Selection is not { } selection || (selection.Side == 0 ? _left : _right) is not { } reader) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        int length = (int)Math.Min(selection.Length, MaxCopy);
        byte[] bytes;
        try
        {
            bytes = await Task.Run(() =>
            {
                var buffer = new byte[length];
                int n = reader.Read(selection.Start, buffer);
                return buffer[..n];
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Status = "The bytes could not be read: " + ex.Message;
            return;
        }
        string text = asText ? new string(bytes.Select(Printable).ToArray()) : HexText(bytes);
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
        Status = bytes.Length < selection.Length
            ? $"Copied the first {Size(bytes.Length)} of the {Size(selection.Length)} selected{(bytes.Length < length ? " (the rest could not be read)" : "")}."
            : $"Copied {Size(bytes.Length)} as {(asText ? "text" : "hex")}.";
    }

    private async Task CopyOffsetAsync()
    {
        if (Selection is not { } selection || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        string offset = "0x" + selection.Start.ToString("X", CultureInfo.InvariantCulture);
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, offset);
        Status = $"Copied the offset {offset}.";
    }

    /// <summary>"4D 5A 90": two digits per byte, spaces between.</summary>
    public static string HexText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return "";
        var chars = new char[bytes.Length * 3 - 1];
        const string digits = "0123456789ABCDEF";
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 3] = digits[bytes[i] >> 4];
            chars[i * 3 + 1] = digits[bytes[i] & 15];
            if (i + 1 < bytes.Length) chars[i * 3 + 2] = ' ';
        }
        return new string(chars);
    }
}
