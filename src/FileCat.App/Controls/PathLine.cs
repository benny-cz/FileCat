using System.Globalization;
using Avalonia;
using Avalonia.Automation;
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
        ToolTip.SetTip(this, "Full path: " + Services.Formatters.SafeName(text) + " · Alt+Up browses ancestors · Ctrl+L edits");
        AutomationProperties.SetName(this, string.IsNullOrEmpty(text)
            ? "Current location unavailable"
            : $"Current location: {Services.Formatters.SafeName(text)}");
        AutomationProperties.SetHelpText(this, "Alt+Up opens the keyboard-accessible ancestor menu; Ctrl+L edits the full location.");
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
        var full = Escaped(_text, [.. Enumerable.Range(0, _text.Length)]);
        (_shown, _map) = full;
        if (!Fits(_shown, width) && _segments.Count >= 3)
        {
            int root = _segments[0].End;
            for (int k = 1; k < _segments.Count - 1; k++)
            {
                int from = _segments[k].End;
                // "C:\…\marek\Documents": the root, an ellipsis, and the path from a part on.
                (_shown, _map) = Escaped(_text[..root] + "…" + _text[from..], [.. Enumerable.Range(0, root), -1, .. Enumerable.Range(from, _text.Length - from)]);
                if (Fits(_shown, width)) break;
            }
        }
        if (!Fits(_shown, width)) Fallback(width, full);
        IReadOnlyList<ValueSpan<TextRunProperties>>? overrides = null;
        if (_hot >= 0 && ShownRange(_hot) is var (start, end) && end > start)
            overrides = [new ValueSpan<TextRunProperties>(start, end - start,
                new GenericTextRunProperties(Typeface, FontSize(), TextDecorations.Underline, _linkBrush))];
        _layout = Layout(_shown, overrides);
    }

    private bool Fits(string shown, double width) => shown.Length <= 4096
        && width > 0 && Layout(shown).WidthIncludingTrailingWhitespace <= width;

    private readonly record struct DisplayAtom(int Start, int End);

    /// <summary>A grapheme or complete safe-display escape; never cut inside either.</summary>
    private static List<DisplayAtom> Atoms(string shown, int[] map)
    {
        var starts = StringInfo.ParseCombiningCharacters(shown);
        var atoms = new List<DisplayAtom>(starts.Length);
        for (int i = 0; i < starts.Length; i++)
        {
            int start = starts[i], end = i + 1 < starts.Length ? starts[i + 1] : shown.Length;
            // SafeName renders an invisible original character as several ordinary display characters, all
            // mapped to that one source index. Keep its whole escape together.
            if (atoms.Count > 0 && map[start] >= 0 && map[start] == map[atoms[^1].Start]
                && Enumerable.Range(atoms[^1].Start, end - atoms[^1].Start).All(k => map[k] == map[start]))
                atoms[^1] = atoms[^1] with { End = end };
            else atoms.Add(new DisplayAtom(start, end));
        }
        return atoms;
    }

    /// <summary>When root plus leaf cannot fit, keep a root cue, one honest gap and the leaf's distinguishing tail.</summary>
    private void Fallback(double width, (string Shown, int[] Map) full)
    {
        double gapWidth = Layout("…").WidthIncludingTrailingWhitespace;
        if (width < gapWidth)
        {
            (_shown, _map) = (string.Empty, []);
            return;
        }
        int rootEnd = _segments.Count > 0 ? Math.Clamp(_segments[0].End, 0, _text.Length) : Math.Min(_text.Length, 1);
        int leafStart = _segments.Count > 1 ? Math.Clamp(_segments[^2].End, 0, _text.Length) : 0;
        var root = _segments.Count <= 1
            ? full : Escaped(_text[..rootEnd], [.. Enumerable.Range(0, rootEnd)]);
        var leaf = _segments.Count <= 1
            ? full : Escaped(_text[leafStart..], [.. Enumerable.Range(leafStart, _text.Length - leafStart)]);
        var rootAtoms = Atoms(root.Shown, root.Map);
        int rootCount = PrefixCount(root.Shown, rootAtoms, Math.Max(0, width * 0.35));
        var leafAtoms = _segments.Count <= 1 ? Atoms(leaf.Shown, leaf.Map).Skip(rootCount).ToList()
            : Atoms(leaf.Shown, leaf.Map);
        int tailCount = TailCount(leaf.Shown, leafAtoms,
            Math.Max(0, width - gapWidth - WidthOf(root.Shown, rootAtoms, rootCount)));

        (string Shown, int[] Map) Candidate()
        {
            int leftEnd = rootCount == 0 ? 0 : rootAtoms[rootCount - 1].End;
            int rightStart = tailCount == 0 ? leaf.Shown.Length : leafAtoms[^tailCount].Start;
            return (root.Shown[..leftEnd] + "…" + leaf.Shown[rightStart..],
                [.. root.Map[..leftEnd], -1, .. leaf.Map[rightStart..]]);
        }
        var candidate = Candidate();
        while (!Fits(candidate.Shown, width) && (rootCount > 0 || tailCount > 0))
        {
            if (rootCount > 1) rootCount--;
            else if (tailCount > 0) tailCount--;
            else rootCount--;
            candidate = Candidate();
        }
        (_shown, _map) = candidate;
    }

    private double WidthOf(string shown, List<DisplayAtom> atoms, int count) =>
        count == 0 ? 0 : Layout(shown[..atoms[count - 1].End]).WidthIncludingTrailingWhitespace;

    private int PrefixCount(string shown, List<DisplayAtom> atoms, double budget)
    {
        int lo = 0, hi = Math.Min(atoms.Count, 128);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (WidthOf(shown, atoms, mid) <= budget) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private int TailCount(string shown, List<DisplayAtom> atoms, double budget)
    {
        int lo = 0, hi = Math.Min(atoms.Count, 512);
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Layout(shown[atoms[^mid].Start..]).WidthIncludingTrailingWhitespace <= budget) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    public override void Render(DrawingContext context)
    {
        // The whole line takes clicks, text or not (the edit box beneath hides its own text meanwhile).
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_text.Length == 0) return;
        if (_layout is null) Arrange(Bounds.Width);
        _layout!.Draw(context, new Point(0, (Bounds.Height - _layout.Height) / 2));
    }

    /// <summary>
    /// The text with control and bidirectional characters escaped, as the file list shows names (§18.3), so a folder's
    /// name cannot turn the path around or hide part of it; each escape stands for its one character of the path.
    /// </summary>
    private static (string Shown, int[] Map) Escaped(string shown, int[] map)
    {
        if (Services.Formatters.SafeName(shown) is var safe && safe.Length == shown.Length) return (shown, map);
        var text = new System.Text.StringBuilder(safe.Length);
        var escapedMap = new List<int>(safe.Length);
        var starts = StringInfo.ParseCombiningCharacters(shown);
        for (int part = 0; part < starts.Length; part++)
        {
            int i = starts[part], end = part + 1 < starts.Length ? starts[part + 1] : shown.Length;
            string original = shown[i..end];
            string piece = Services.Formatters.SafeName(original);
            text.Append(piece);
            if (piece == original)
                for (int k = i; k < end; k++) escapedMap.Add(map[k]);
            else for (int k = 0; k < piece.Length; k++) escapedMap.Add(map[i]);
        }
        return (text.ToString(), escapedMap.ToArray());
    }

    /// <summary>A part's name in the shown text (without its separators), as shown characters [start, end).</summary>
    private (int Start, int End) ShownRange(int segment)
    {
        int from = segment == 0 ? 0 : _segments[segment - 1].End, to = _segments[segment].End;
        int bestStart = -1, bestEnd = -1, runStart = -1;
        for (int i = 0; i <= _map.Length; i++)
        {
            int original = i < _map.Length ? _map[i] : -1;
            bool belongs = original >= from && original < to
                && (_text[original] is not ('\\' or '/') || segment == 0);
            if (belongs)
            {
                if (runStart < 0) runStart = i;
                continue;
            }
            if (runStart < 0) continue;
            // A fallback may show two disconnected fragments of a single long part. The leaf's final
            // fragment identifies it; the root's first fragment is its cue. Never include the gap in a link.
            if (bestStart < 0 || segment == _segments.Count - 1 || i - runStart > bestEnd - bestStart)
                (bestStart, bestEnd) = (runStart, i);
            runStart = -1;
        }
        return bestStart < 0 ? (0, 0) : (bestStart, bestEnd);
    }

    /// <summary>The part under a point, or -1 (past the text, on the ellipsis, or on a separator).</summary>
    internal int SegmentAt(Point point)
    {
        if (_layout is null) Arrange(Bounds.Width);
        if (_map.Length == 0) return -1;
        var layout = _layout!;
        if (point.X < 0 || point.X > layout.WidthIncludingTrailingWhitespace) return -1;
        var hit = layout.HitTestPoint(new Point(point.X, layout.Height / 2));
        int shown = Math.Clamp(hit.TextPosition, 0, _map.Length - 1);
        if (_map[shown] < 0) return -1;
        int original = _map[shown];
        for (int i = 0; i < _segments.Count; i++)
            if (original < _segments[i].End) return i;
        return -1;
    }

    /// <summary>The text as drawn (tests read it).</summary>
    internal string ShownText
    {
        get
        {
            if (_layout is null) Arrange(Bounds.Width);
            return _shown;
        }
    }

    internal double ShownWidth
    {
        get
        {
            if (_layout is null) Arrange(Bounds.Width);
            return _layout!.WidthIncludingTrailingWhitespace;
        }
    }

    internal Point? GapPoint
    {
        get
        {
            if (_layout is null) Arrange(Bounds.Width);
            int at = Array.IndexOf(_map, -1);
            if (at < 0) return null;
            var rect = _layout!.HitTestTextRange(at, 1).FirstOrDefault();
            return rect.Width <= 0 ? null : new Point(rect.Center.X, Bounds.Height / 2);
        }
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
        string full = "Full path: " + Services.Formatters.SafeName(_text);
        ToolTip.SetTip(this, hot < 0 ? full + " · click to edit · Alt+Up browses ancestors"
            : link ? $"Go to {Services.Formatters.SafeName(PathUpTo(hot))} · Ctrl+click copies it · middle click opens it in a new tab · {full}"
            : full + " · click to edit · Ctrl+click copies it · Alt+Up browses ancestors");
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
