using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Controls;

/// <summary>
/// A panel's path with each folder on the way a link, after Salamander's directory line: a click goes there, Ctrl+click
/// copies the path up to there, a middle click opens it in a new tab, and the context menu copies the full path, a part,
/// or the name. A click on the last part or past the text edits the path. Long paths keep their root and their end.
/// </summary>
public sealed class PathLine : Control
{
    /// <summary>Characters [0, <see cref="End"/>) of the text are the path of <see cref="Target"/>.</summary>
    public sealed record Segment(int End, Location Target);

    private string _text = "";
    private IReadOnlyList<Segment> _segments = [];
    private TextLayout? _layout;
    private string _shown = "";
    /// <summary>For each shown character, the character of the path it stands for (-1 for the ellipsis).</summary>
    private int[] _map = [];
    private int _hot = -1;
    private Point? _pressedAt;
    private IBrush _textBrush = Brushes.Black, _linkBrush = Brushes.Blue;

    public PathLine()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    /// <summary>A part was chosen: go there (in a new tab when <c>true</c>).</summary>
    public event Action<Location, bool>? NavigateRequested;

    /// <summary>The path is to be edited (a click on its last part or past it, or a double click).</summary>
    public event Action? EditRequested;

    /// <summary>Text to put on the clipboard (Ctrl+click on a part).</summary>
    public event Action<string>? CopyRequested;

    /// <summary>The context menu, at a point, with the part under it (or -1).</summary>
    public event Action<Point, int>? MenuRequested;

    public string Text => _text;

    public IReadOnlyList<Segment> Segments => _segments;

    /// <summary>The path up to and including a part.</summary>
    public string PathUpTo(int segment) => _text[..Math.Min(_text.Length, _segments[segment].End)];

    /// <summary>A part's own name ("marek" in C:\Users\marek).</summary>
    public string NameOf(int segment)
    {
        int start = segment == 0 ? 0 : _segments[segment - 1].End;
        return _text[start..Math.Min(_text.Length, _segments[segment].End)].Trim('\\', '/');
    }

    public void SetPath(string text, IReadOnlyList<Segment> segments)
    {
        _text = text;
        _segments = segments;
        _hot = -1;
        _layout = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Services.ThemeManager.ThemeChanged += OnThemeChanged;
        OnThemeChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Services.ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        IBrush B(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;
        _textBrush = B("FcText", Brushes.Black);
        _linkBrush = B("FcTextLink", Brushes.Blue);
        _layout = null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, Math.Ceiling(FontSize() * 1.4));

    private double FontSize() => GetValue(TextElement.FontSizeProperty);

    private Typeface Typeface => new(GetValue(TextElement.FontFamilyProperty));

    private TextLayout Layout(string text, IReadOnlyList<ValueSpan<TextRunProperties>>? overrides = null) =>
        new(text, Typeface, FontSize(), _textBrush, maxWidth: double.PositiveInfinity, textStyleOverrides: overrides);

    /// <summary>
    /// The text as it fits: whole, or its root, an ellipsis, and as many of its last parts as fit. The part under the
    /// pointer is drawn as a link: in the link color, underlined.
    /// </summary>
    private void Arrange(double width)
    {
        _shown = _text;
        _map = [.. Enumerable.Range(0, _text.Length)];
        if (Layout(_shown).WidthIncludingTrailingWhitespace > width && _segments.Count >= 3)
        {
            int root = _segments[0].End;
            for (int k = 1; k < _segments.Count - 1; k++)
            {
                int from = _segments[k].End;
                // "C:\…\marek\Documents": the root, an ellipsis, and the path from a part on.
                _shown = _text[..root] + "…" + _text[from..];
                _map = [.. Enumerable.Range(0, root), -1, .. Enumerable.Range(from, _text.Length - from)];
                if (Layout(_shown).WidthIncludingTrailingWhitespace <= width) break;
            }
        }
        IReadOnlyList<ValueSpan<TextRunProperties>>? overrides = null;
        if (_hot >= 0 && ShownRange(_hot) is var (start, end) && end > start)
            overrides = [new ValueSpan<TextRunProperties>(start, end - start,
                new GenericTextRunProperties(Typeface, FontSize(), TextDecorations.Underline, _linkBrush))];
        _layout = Layout(_shown, overrides);
    }

    public override void Render(DrawingContext context)
    {
        // The whole line takes clicks, text or not (the edit box beneath hides its own text meanwhile).
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_text.Length == 0) return;
        if (_layout is null) Arrange(Bounds.Width);
        _layout!.Draw(context, new Point(0, (Bounds.Height - _layout.Height) / 2));
    }

    /// <summary>A part's name in the shown text (without its separators), as shown characters [start, end).</summary>
    private (int Start, int End) ShownRange(int segment)
    {
        int from = segment == 0 ? 0 : _segments[segment - 1].End, to = _segments[segment].End;
        int start = -1, end = -1;
        for (int i = 0; i < _map.Length; i++)
        {
            int original = _map[i];
            if (original < from || original >= to || _text[original] is '\\' or '/' && segment > 0) continue;
            if (start < 0) start = i;
            end = i + 1;
        }
        return start < 0 ? (0, 0) : (start, end);
    }

    /// <summary>The part under a point, or -1 (past the text, on the ellipsis, or on a separator).</summary>
    internal int SegmentAt(Point point)
    {
        if (_layout is null) Arrange(Bounds.Width);
        var layout = _layout!;
        if (point.X < 0 || point.X > layout.WidthIncludingTrailingWhitespace) return -1;
        var hit = layout.HitTestPoint(new Point(point.X, layout.Height / 2));
        int shown = Math.Clamp(hit.TextPosition, 0, _map.Length - 1);
        if (_map.Length == 0 || _map[shown] < 0) return -1;
        int original = _map[shown];
        for (int i = 0; i < _segments.Count; i++)
            if (original < _segments[i].End) return i;
        return -1;
    }

    /// <summary>Where a part's name is shown (tests click it there).</summary>
    internal Point? PointOf(int segment)
    {
        if (_layout is null) Arrange(Bounds.Width);
        var (start, end) = ShownRange(segment);
        if (end <= start) return null;
        var rects = _layout!.HitTestTextRange(start, end - start).ToList();
        return rects.Count == 0 ? null : new Point((rects.Min(r => r.X) + rects.Max(r => r.Right)) / 2, Bounds.Height / 2);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _layout = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int hot = SegmentAt(e.GetPosition(this));
        if (hot == _hot) return;
        _hot = hot;
        _layout = null;
        bool link = hot >= 0 && hot < _segments.Count - 1;
        Cursor = new Cursor(link ? StandardCursorType.Hand : StandardCursorType.Ibeam);
        ToolTip.SetTip(this, hot < 0 ? "Click to edit the path · right-click to copy it"
            : link ? $"Go to {PathUpTo(hot)} · Ctrl+click copies it · middle click opens it in a new tab · right-click for more"
            : "Click to edit the path · Ctrl+click copies it · right-click for more");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hot < 0) return;
        _hot = -1;
        _layout = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        int segment = SegmentAt(point.Position);
        switch (point.Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed when e.ClickCount >= 2:
                EditRequested?.Invoke();
                break;
            case PointerUpdateKind.LeftButtonPressed:
                _pressedAt = point.Position;
                break;
            case PointerUpdateKind.MiddleButtonPressed when segment >= 0:
                NavigateRequested?.Invoke(_segments[segment].Target, true);
                break;
            case PointerUpdateKind.RightButtonPressed:
                MenuRequested?.Invoke(point.Position, segment);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressedAt is not { } pressed || e.InitialPressMouseButton != MouseButton.Left) return;
        _pressedAt = null;
        var at = e.GetPosition(this);
        // A click, not a drag: the part under it acts.
        if (Math.Abs(at.X - pressed.X) > 4 || Math.Abs(at.Y - pressed.Y) > 4) return;
        int segment = SegmentAt(at);
        if ((e.KeyModifiers & KeyModifiers.Control) != 0 && segment >= 0) CopyRequested?.Invoke(PathUpTo(segment));
        else if (segment >= 0 && segment < _segments.Count - 1) NavigateRequested?.Invoke(_segments[segment].Target, false);
        else EditRequested?.Invoke();
        e.Handled = true;
    }
}
