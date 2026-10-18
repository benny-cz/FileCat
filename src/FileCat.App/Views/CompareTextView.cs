using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.Core.Compare;

namespace FileCat.App.Views;

/// <summary>
/// The aligned source rows remain virtualized by the list. Each realized row owns two clipped text layouts:
/// one shared horizontal position when unwrapped, or a shared row height when wrapped. Only exceptional
/// source lines exceed the explicit shaping budget and use the complete, bounded line-detail reader.
/// </summary>
internal sealed class CompareTextView : Grid, IDisposable
{
    internal const int MaximumLineCharacters = 16_384;
    private readonly ListBox _rows;
    private readonly ScrollBar _horizontal = new()
    {
        Orientation = Orientation.Horizontal, AllowAutoHide = false, Focusable = true,
        SmallChange = 40, MinHeight = 16,
    };
    private int _generation, _revealRequest;
    private bool _wrap, _disposed, _metricsQueued, _settingOffset;
    private double _extent, _paneWidth;
    private PendingReveal? _pendingReveal;
    private sealed record PendingReveal(int Index, int Generation, int Request, object? Items, CompareRow Source);

    internal event Action<bool>? DetailsRequested;
    internal bool IsDisposed => _disposed;
    internal double HorizontalOffset => _wrap ? 0 : _horizontal.Value;
    internal ScrollBar HorizontalBar => _horizontal;
    internal int RealizedRowCount => RealizedRows().Count();

    internal bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value || _disposed) return;
            _wrap = value;
            _horizontal.IsVisible = !value;
            foreach (var row in RealizedRows()) row.UpdateWrapping();
            QueueMetrics();
            RevealSelected();
        }
    }

    internal CompareTextView(ListBox rows)
    {
        _rows = rows;
        RowDefinitions = RowDefinitions.Parse("*,Auto");
        Children.Add(rows);
        SetRow(_horizontal, 1);
        Children.Add(_horizontal);
        AutomationProperties.SetName(this, "Aligned source text comparison");
        AutomationProperties.SetName(_horizontal, "Horizontal position of both comparison files");
        AutomationProperties.SetHelpText(_horizontal, "Scrolls both unwrapped source panes together. Alt+Left and Alt+Right also scroll them; Wrap long lines keeps each source pair aligned.");
        ScrollViewer.SetHorizontalScrollBarVisibility(rows, ScrollBarVisibility.Disabled);
        // A finite viewport is essential: an ordinary StackPanel would materialize the whole comparison.
        rows.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        rows.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            CompareRow row => new CompareTextRow(this, row, _generation),
            string note => new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4) },
            _ => new TextBlock(),
        });
        rows.ContainerPrepared += OnContainerPrepared;
        _horizontal.ValueChanged += (_, _) =>
        {
            if (!_settingOffset) _revealRequest++; // later deliberate scrolling owns the viewport
            foreach (var row in RealizedRows()) row.UpdateOffset();
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => _revealRequest++, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) =>
        {
            QueueMetrics();
            if (_rows.SelectedIndex >= 0) RevealSelected();
        };
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (_disposed || e.Container is not ContentControl { Presenter: { } presenter } container ||
            !ReferenceEquals(presenter.TemplatedParent, container)) return;
        // Immediate retirement clears the former presenter's child and contexts. Its local null must
        // not shadow the container template's Content binding when virtualization reuses the container.
        // Restore only this owned outer presenter; retired nested templates and their models stay clear.
        presenter.ClearValue(ContentPresenter.ContentProperty);
        presenter.UpdateChild();
    }

    internal void Reset()
    {
        _generation++;
        _revealRequest++;
        _pendingReveal = null;
        foreach (var row in RealizedRows().ToArray()) row.Retire();
        _extent = _paneWidth = 0;
        _horizontal.Maximum = 0;
        _horizontal.Value = 0;
    }

    private IEnumerable<CompareTextRow> RealizedRows() => _rows.GetRealizedContainers()
        .SelectMany(c => c.GetVisualDescendants().OfType<CompareTextRow>());

    internal CompareTextRow? RealizedRow(int index) => _rows.ContainerFromIndex(index)?
        .GetVisualDescendants().OfType<CompareTextRow>().FirstOrDefault();

    internal bool IsCurrent(CompareTextRow row) => !_disposed && row.Source is not null && row.Generation == _generation &&
        TopLevel.GetTopLevel(this) is { } root && ReferenceEquals(TopLevel.GetTopLevel(row), root);

    internal void ObserveExtent(double extent, double viewport)
    {
        if (_disposed || _wrap || !double.IsFinite(extent) || viewport <= 0) return;
        _extent = Math.Max(_extent, extent);
        _paneWidth = viewport;
        QueueMetrics();
    }

    private void QueueMetrics()
    {
        if (_disposed || _metricsQueued) return;
        _metricsQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _metricsQueued = false;
            if (_disposed) return;
            _settingOffset = true;
            try
            {
                _horizontal.ViewportSize = Math.Max(1, _paneWidth);
                _horizontal.LargeChange = Math.Max(40, _paneWidth * 0.9);
                _horizontal.Maximum = Math.Max(0, _extent - _paneWidth);
            }
            finally { _settingOffset = false; }
            _horizontal.IsVisible = !_wrap;
        }, DispatcherPriority.Loaded);
    }

    internal void OpenDetails(CompareTextRow row)
    {
        if (!IsCurrent(row)) return;
        var container = row.FindAncestorOfType<ListBoxItem>();
        int index = container is null ? -1 : _rows.IndexFromContainer(container);
        if (index < 0 || !ReferenceEquals(_rows.Items[index], row.Source)) return;
        _rows.SelectedIndex = index;
        DetailsRequested?.Invoke(true);
    }

    internal void RevealSelected()
    {
        if (_disposed || _rows.SelectedIndex < 0 || _rows.SelectedItem is not CompareRow source) return;
        int index = _rows.SelectedIndex, generation = _generation, request = ++_revealRequest;
        _pendingReveal = new PendingReveal(index, generation, request, _rows.ItemsSource, source);
        _rows.ScrollIntoView(index);
        // A queued or retained dispatcher operation owns value tokens only. Reset/Dispose can release
        // the previous result immediately even if the UI queue has not reached this operation yet.
        Dispatcher.UIThread.Post(() => RevealPending(request), DispatcherPriority.Loaded);
    }

    private void RevealPending(int request)
    {
        if (_pendingReveal is not { } pending || pending.Request != request) return;
        try
        {
            bool OwnsRequest() => !_disposed && ReferenceEquals(_pendingReveal, pending) &&
                request == _revealRequest && pending.Generation == _generation &&
                ReferenceEquals(pending.Items, _rows.ItemsSource) && _rows.SelectedIndex == pending.Index &&
                ReferenceEquals(_rows.SelectedItem, pending.Source);
            if (!OwnsRequest()) return;
            _rows.UpdateLayout();
            if (!OwnsRequest()) return;
            var row = RealizedRow(pending.Index);
            if (row is null || !IsCurrent(row)) return;
            if (row.IsExceptional)
            {
                // The main row says exactly why it is summarized; the existing detail view exposes every part.
                row.BringIntoView();
                DetailsRequested?.Invoke(false);
                return;
            }
            var source = pending.Source;
            var evidence = row.Difference;
            int at = evidence.Start >= 0 ? evidence.Start : source.EndingDiffers ? Math.Max(source.LeftText.Length, source.RightText.Length) : 0;
            Rect? left = row.LeftCell?.SourceBounds(Math.Min(at, source.LeftText.Length));
            Rect? right = row.RightCell?.SourceBounds(Math.Min(at, source.RightText.Length));
            if (!_wrap)
            {
                var positions = new[] { left, right }.OfType<Rect>().ToArray();
                if (positions.Length > 0)
                {
                    double start = positions.Min(r => r.X), end = positions.Max(r => r.Right);
                    double width = Math.Min(row.LeftCell?.Bounds.Width ?? double.MaxValue, row.RightCell?.Bounds.Width ?? double.MaxValue);
                    if (!double.IsFinite(width)) width = _paneWidth;
                    _extent = Math.Max(_extent, Math.Max(row.LeftCell?.TextWidth ?? 0, row.RightCell?.TextWidth ?? 0));
                    _paneWidth = Math.Max(1, width);
                    _horizontal.Maximum = Math.Max(0, _extent - _paneWidth);
                    _settingOffset = true;
                    try
                    {
                        if (start < HorizontalOffset + 8 || end > HorizontalOffset + width - 8)
                            _horizontal.Value = Math.Clamp(start - 16, 0, _horizontal.Maximum);
                    }
                    finally { _settingOffset = false; }
                    foreach (var realized in RealizedRows()) realized.UpdateOffset();
                    row.UpdateLayout();
                    if (!OwnsRequest()) return;
                }
            }
            // Bring the actual text rectangle into view, not the whole (potentially very tall) wrapped row.
            var bounds = new List<Rect>(2);
            if (left is { } l && row.LeftCell is { } lc) bounds.Add(l.Translate((Vector)(lc.TranslatePoint(default, row) ?? default)));
            if (right is { } r && row.RightCell is { } rc) bounds.Add(r.Translate((Vector)(rc.TranslatePoint(default, row) ?? default)));
            if (bounds.Count > 0)
            {
                double top = bounds.Min(r => r.Y), bottom = bounds.Max(r => r.Bottom);
                row.BringIntoView(new Rect(0, top, row.Bounds.Width, Math.Max(1, bottom - top)).Inflate(new Thickness(0, 4)));
            }
        }
        finally
        {
            if (ReferenceEquals(_pendingReveal, pending)) _pendingReveal = null;
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_disposed || _wrap || e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Left or Key.Right)) return;
        _horizontal.Value += e.Key == Key.Left ? -_horizontal.SmallChange : _horizontal.SmallChange;
        e.Handled = true;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        _revealRequest++;
        if (_disposed || _wrap) return;
        double delta = e.Delta.X != 0 ? e.Delta.X : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y : 0;
        if (delta == 0) return;
        _horizontal.Value -= delta * _horizontal.SmallChange;
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rows.ContainerPrepared -= OnContainerPrepared;
        Reset();
        DetailsRequested = null;
    }
}

/// <summary>One physical aligned row. Both side cells are measured before Grid fixes their common height.</summary>
internal sealed class CompareTextRow : Grid
{
    private readonly CompareTextView _owner;
    // The live result list owns models. A detached template can be retained by scroll/render state
    // after its presenter and DataContext are cleared; it must not keep the former model alive.
    private WeakReference<CompareRow>? _source;
    internal CompareRow? Source => _source is not null && _source.TryGetTarget(out var source) ? source : null;
    internal int Generation { get; }
    internal CompareSourceCell? LeftCell { get; }
    internal CompareSourceCell? RightCell { get; }
    internal CompareTextEvidence.Difference Difference { get; }
    internal bool IsExceptional { get; }

    internal CompareTextRow(CompareTextView owner, CompareRow source, int generation)
    {
        _owner = owner;
        _source = new WeakReference<CompareRow>(source);
        Generation = generation;
        IsExceptional = source.LeftText.Length > CompareTextView.MaximumLineCharacters || source.RightText.Length > CompareTextView.MaximumLineCharacters;
        ColumnDefinitions = ColumnDefinitions.Parse("80,*,80,*");
        MinHeight = 22;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Difference = IsExceptional ? new(-1, 0, 0) : CompareTextEvidence.FirstDifference(source.LeftText, source.RightText, CancellationToken.None);
        var spans = !IsExceptional && source.Kind == DiffKind.Changed ? InlineDiff.Compare(source.LeftText, source.RightText) : null;
        AddNumber(0, source.LeftLine, "Left", source.Kind == DiffKind.LeftOnly ? "− " : "", source.EndingDiffers);
        AddNumber(2, source.RightLine, "Right", source.Kind == DiffKind.RightOnly ? "+ " : "", false);
        LeftCell = AddSource(1, source.LeftLine, source.LeftText, "Left", spans?.Left);
        RightCell = AddSource(3, source.RightLine, source.RightText, "Right", spans?.Right);
        AutomationProperties.SetName(this, source.ToString());
        AutomationProperties.SetHelpText(this, source.Kind == DiffKind.Unaligned
            ? "Unaligned source content; no line-by-line match is claimed. Alt+L opens complete source-line details."
            : source.EndingDiffers ? "Source text matches but line endings differ. Binary view exposes the exact ending bytes."
            : "Both source lines share this row. Underlined text identifies changed spans. Alt+L focuses selectable source-line details.");
    }

    private void AddNumber(int column, int? line, string side, string prefix, bool ending)
    {
        var label = new TextBlock
        {
            Text = line is { } n ? prefix + n.ToString("N0", CultureInfo.CurrentCulture) + (ending ? " ⏎≠" : "") : "—",
            Margin = new Thickness(2, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Classes = { "muted" },
        };
        AutomationProperties.SetName(label, line is { } number ? $"{side} source line {number:N0}" : $"{side} gap, no corresponding source line");
        SetColumn(label, column);
        Children.Add(label);
    }

    private CompareSourceCell? AddSource(int column, int? line, string text, string side, IReadOnlyList<(int Start, int Length)>? spans)
    {
        Control content;
        CompareSourceCell? cell = null;
        if (text.Length > CompareTextView.MaximumLineCharacters)
        {
            var full = new Button
            {
                Content = new TextBlock { Text = $"{text.Length:N0}-character source line — inspect complete line", TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 4, 2),
            };
            AutomationProperties.SetName(full, $"Inspect complete {side.ToLowerInvariant()} source line {line:N0}, {text.Length:N0} UTF-16 characters");
            AutomationProperties.SetHelpText(full, $"This line exceeds the {CompareTextView.MaximumLineCharacters:N0}-character main-row shaping limit. Full-line details provide all source text through bounded parts.");
            full.Click += (_, _) => _owner.OpenDetails(this);
            content = full;
        }
        else if (line is null)
        {
            content = new Border();
            AutomationProperties.SetName(content, side + " gap, no source text");
        }
        else
        {
            cell = new CompareSourceCell(_owner, text, spans);
            AutomationProperties.SetName(cell, $"{side} source line {line:N0}");
            content = cell;
        }
        SetColumn(content, column);
        Children.Add(content);
        return cell;
    }

    internal void UpdateWrapping()
    {
        LeftCell?.UpdateWrapping();
        RightCell?.UpdateWrapping();
        InvalidateMeasure();
    }

    internal void UpdateOffset()
    {
        LeftCell?.InvalidateVisual();
        RightCell?.InvalidateVisual();
    }

    internal void Retire()
    {
        _source = null;
        LeftCell?.Retire();
        RightCell?.Retire();
    }
}

/// <summary>
/// A source-faithful bounded text cell. Its read-only text peer exposes the complete source, while
/// owned measurement and drawing keep arranging/scrolling from rebuilding every wrapped text line.
/// </summary>
internal sealed class CompareSourceCell : TemplatedControl
{
    // Grid can measure star cells at zero while resolving columns. Do not shape a 16K source
    // into one visual row per character before the actual readable pane width is known.
    internal const double MinimumShapingWidth = 80;
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace");
    private readonly CompareTextView _owner;
    private readonly IReadOnlyList<(int Start, int Length)>? _spans;
    private TextLayout? _sourceLayout;
    private LayoutKey? _layoutKey;
    private Rect[] _underlines = [];
    private bool _retired;
    internal string Text { get; }
    internal TextLayout? SourceLayout => _sourceLayout;
    internal double TextWidth => _sourceLayout?.WidthIncludingTrailingWhitespace ?? 0;
    internal Point TextOrigin => new(-_owner.HorizontalOffset, 0);
    internal int LayoutBuildCount { get; private set; }
    internal int UnderlineFragmentCount => _underlines.Length;

    private readonly record struct LayoutKey(string Source, Typeface Typeface, double FontSize,
        IBrush? Foreground, FlowDirection Direction, double Width, TextWrapping Wrap,
        double LetterSpacing, FontFeatureCollection? Features);

    static CompareSourceCell() => AffectsMeasure<CompareSourceCell>(FontFamilyProperty, FontFeaturesProperty,
        FontSizeProperty, FontStyleProperty, FontWeightProperty, FontStretchProperty, ForegroundProperty,
        FlowDirectionProperty, LetterSpacingProperty);

    internal CompareSourceCell(CompareTextView owner, string source, IReadOnlyList<(int Start, int Length)>? spans)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, CompareTextView.MaximumLineCharacters);
        _owner = owner;
        _spans = spans;
        ClipToBounds = true;
        Margin = new Thickness(0, 2, 4, 2);
        Text = source;
        FontFamily = Mono;
        AutomationProperties.SetHelpText(this, "Read-only source text. Alt+L opens standard selectable fields for both complete source lines.");
    }

    internal void UpdateWrapping()
    {
        InvalidateMeasure();
    }

    private void EnsureLayout(double width)
    {
        if (width < MinimumShapingWidth || _retired || _owner.IsDisposed) return;
        var key = new LayoutKey(Text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize, Foreground, FlowDirection, _owner.Wrap ? width : double.PositiveInfinity,
            _owner.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, LetterSpacing, FontFeatures);
        if (_sourceLayout is not null && _layoutKey == key) return;
        _sourceLayout?.Dispose();
        _sourceLayout = null;
        _underlines = [];
        _sourceLayout = new TextLayout(key.Source, key.Typeface, key.FontSize, key.Foreground,
            textWrapping: key.Wrap, textTrimming: TextTrimming.None, flowDirection: key.Direction,
            maxWidth: key.Width, letterSpacing: key.LetterSpacing,
            fontFeatures: key.Features);
        _layoutKey = key;
        LayoutBuildCount++;
        _underlines = BuildUnderlines(_sourceLayout);
    }

    private Rect[] BuildUnderlines(TextLayout layout)
    {
        if (_spans is not { Count: > 0 }) return [];
        // InlineDiff produces ordered non-overlapping spans. Sweep them against the actual
        // visual lines, avoiding HitTestTextRange's all-lines allocation for every tiny span.
        var result = new List<Rect>();
        int firstSpan = 0;
        double top = 0;
        foreach (var line in layout.TextLines)
        {
            int start = line.FirstTextSourceIndex, end = start + line.Length;
            while (firstSpan < _spans.Count && _spans[firstSpan].Start + _spans[firstSpan].Length <= start) firstSpan++;
            for (int i = firstSpan; i < _spans.Count && _spans[i].Start < end; i++)
            {
                int from = Math.Max(start, _spans[i].Start), to = Math.Min(end, _spans[i].Start + _spans[i].Length);
                if (to <= from) continue;
                foreach (var bounds in line.GetTextBounds(from, to - from))
                    result.Add(bounds.Rectangle.Translate(new Vector(0, top)));
            }
            top += line.Height;
        }
        return result.ToArray();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 240;
        if (width < MinimumShapingWidth) return new Size(width, 18);
        EnsureLayout(width);
        _owner.ObserveExtent(TextWidth, width);
        return new Size(width, Math.Max(18, _sourceLayout?.Height ?? 18));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureLayout(finalSize.Width);
        _owner.ObserveExtent(TextWidth, finalSize.Width);
        return finalSize;
    }

    internal Rect SourceBounds(int sourceOffset) => _sourceLayout?.HitTestTextPosition(sourceOffset) ?? default;

    public override void Render(DrawingContext context)
    {
        if (_sourceLayout is not { } layout || Bounds.Width < MinimumShapingWidth) return;
        var origin = TextOrigin;
        layout.Draw(context, origin);
        // Geometry and source offsets come from the same cached layout, and the current text
        // foreground preserves the decoration in monochrome and across theme changes.
        var pen = new Pen(Foreground ?? Brushes.Black, 1);
        foreach (var source in _underlines)
        {
            double left = Math.Max(0, source.Left + origin.X), right = Math.Min(Bounds.Width, source.Right + origin.X);
            if (right > left)
                context.DrawLine(pen, new Point(left, source.Bottom - 1), new Point(right, source.Bottom - 1));
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SourceTextPeer(this);

    private sealed class SourceTextPeer(CompareSourceCell source) : ControlAutomationPeer(source), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetClassNameCore() => "ComparisonSourceText";
        public bool IsReadOnly => true;
        public string Value => source.Text;
        public void SetValue(string? value) => throw new InvalidOperationException("Comparison source text is read-only.");
    }

    internal void ReleaseLayout()
    {
        _sourceLayout?.Dispose();
        _sourceLayout = null;
        _layoutKey = null;
        _underlines = [];
        InvalidateMeasure();
    }

    internal void Retire()
    {
        _retired = true;
        ReleaseLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseLayout();
        base.OnDetachedFromVisualTree(e);
    }
}

internal static class CompareTextEvidence
{
    internal readonly record struct Difference(int Start, int LeftLength, int RightLength);

    internal static Difference FirstDifference(string left, string right, CancellationToken ct)
    {
        int at = 0, checkAt = 0;
        while (at < left.Length && at < right.Length)
        {
            if (at >= checkAt) { ct.ThrowIfCancellationRequested(); checkAt = at + 4096; }
            int l = StringInfo.GetNextTextElementLength(left.AsSpan(at));
            int r = StringInfo.GetNextTextElementLength(right.AsSpan(at));
            if (l != r || !left.AsSpan(at, l).SequenceEqual(right.AsSpan(at, r))) return new(at, l, r);
            at += l;
        }
        ct.ThrowIfCancellationRequested();
        if (at == left.Length && at == right.Length) return new(-1, 0, 0);
        return new(at, at < left.Length ? StringInfo.GetNextTextElementLength(left.AsSpan(at)) : 0,
            at < right.Length ? StringInfo.GetNextTextElementLength(right.AsSpan(at)) : 0);
    }
}
