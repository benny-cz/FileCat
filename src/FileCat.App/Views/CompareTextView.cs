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

internal enum CompareTextCopyAction { Selection, Line, Difference }

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
    private readonly record struct TextPoint(int Row, int Side, int Offset);
    private TextPoint? _selectionAnchor, _selectionEnd;
    private CompareSourceCell? _pointerCell;
    internal bool SelectionGestureActive { get; private set; }
    internal int? SelectedSide => _selectionAnchor?.Side;
    internal bool HasSelection => OrderedSelection() is { } s &&
        (s.Start.Row != s.End.Row || s.Start.Offset != s.End.Offset);
    internal event Action? SelectionChanged;
    internal event Action<CompareTextCopyAction, int>? CopyRequested;
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
        AddHandler(PointerPressedEvent, OnSourcePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnSourcePointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnSourcePointerReleased, RoutingStrategies.Tunnel);
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
        _selectionAnchor = _selectionEnd = null;
        _pointerCell = null;
        InvalidateSelection();
        foreach (var row in RealizedRows().ToArray()) row.Retire();
        _extent = _paneWidth = 0;
        _horizontal.Maximum = 0;
        _horizontal.Value = 0;
    }

    private IEnumerable<CompareTextRow> RealizedRows() => _rows.GetRealizedContainers()
        .SelectMany(c => c.GetVisualDescendants().OfType<CompareTextRow>());

    private CompareSourceCell? SourceAt(Point point)
    {
        foreach (var row in RealizedRows())
        {
            if (!IsCurrent(row)) continue;
            foreach (var cell in new[] { row.LeftCell, row.RightCell })
            {
                if (cell is null || cell.TranslatePoint(default, this) is not { } origin) continue;
                if (new Rect(origin, cell.Bounds.Size).Contains(point)) return cell;
            }
        }
        return null;
    }

    private void OnSourcePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _revealRequest++;
        if (_disposed || SourceAt(e.GetPosition(this)) is not { } cell) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            cell.ContextMenu?.Open(cell);
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        cell.Focus();
        SelectCell(cell, cell.SourceOffset(e.GetPosition(cell)), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _pointerCell = cell;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnSourcePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pointerCell is not { } start || SourceAt(e.GetPosition(this)) is not { } cell ||
            cell.Side != start.Side) return;
        ExtendCellSelection(cell, cell.SourceOffset(e.GetPosition(cell)));
    }

    private void OnSourcePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pointerCell is null) return;
        OnSourcePointerMoved(sender, e);
        _pointerCell = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>Selection is keyed to aligned row and source side, never to a recycled visual cell.</summary>
    internal void SelectCell(CompareSourceCell cell, int offset, bool extend)
    {
        if (_disposed || cell.FindAncestorOfType<CompareTextRow>() is not { } row || !IsCurrent(row)) return;
        var container = row.FindAncestorOfType<ListBoxItem>();
        int index = container is null ? -1 : _rows.IndexFromContainer(container);
        if (index < 0 || !ReferenceEquals(_rows.Items[index], row.Source)) return;
        var point = new TextPoint(index, cell.Side, Math.Clamp(offset, 0, cell.Text.Length));
        if (!extend || _selectionAnchor is not { } anchor || anchor.Side != point.Side)
            _selectionAnchor = point;
        _selectionEnd = point;
        SelectionGestureActive = true;
        try { _rows.SelectedIndex = index; }
        finally { SelectionGestureActive = false; }
        InvalidateSelection();
    }

    internal void ExtendCellSelection(CompareSourceCell cell, int offset)
    {
        if (_selectionAnchor is not { } anchor || anchor.Side != cell.Side) return;
        SelectCell(cell, offset, extend: true);
    }

    internal void SetSelection(int side, int startRow, int startOffset, int endRow, int endOffset)
    {
        if (_disposed || side is not (0 or 1) || _rows.ItemsSource is not IReadOnlyList<object> rows ||
            startRow < 0 || endRow < 0 || startRow >= rows.Count || endRow >= rows.Count ||
            rows[startRow] is not CompareRow first || rows[endRow] is not CompareRow last ||
            (side == 0 ? first.LeftLine : first.RightLine) is null ||
            (side == 0 ? last.LeftLine : last.RightLine) is null) return;
        _selectionAnchor = new TextPoint(startRow, side, Math.Clamp(startOffset, 0, (side == 0 ? first.LeftText : first.RightText).Length));
        _selectionEnd = new TextPoint(endRow, side, Math.Clamp(endOffset, 0, (side == 0 ? last.LeftText : last.RightText).Length));
        InvalidateSelection();
    }

    internal void ClearSelection()
    {
        _selectionAnchor = _selectionEnd = null;
        InvalidateSelection();
    }

    internal void RequestCopy(CompareTextCopyAction action, CompareSourceCell cell)
    {
        if (_disposed || cell.FindAncestorOfType<CompareTextRow>() is not { } row || !IsCurrent(row)) return;
        var container = row.FindAncestorOfType<ListBoxItem>();
        int index = container is null ? -1 : _rows.IndexFromContainer(container);
        if (index < 0 || !ReferenceEquals(_rows.Items[index], row.Source)) return;
        if (action != CompareTextCopyAction.Selection) _rows.SelectedIndex = index;
        CopyRequested?.Invoke(action, cell.Side);
    }

    private (TextPoint Start, TextPoint End)? OrderedSelection()
    {
        if (_selectionAnchor is not { } anchor || _selectionEnd is not { } end || anchor.Side != end.Side)
            return null;
        return anchor.Row < end.Row || anchor.Row == end.Row && anchor.Offset <= end.Offset
            ? (anchor, end) : (end, anchor);
    }

    internal (int Start, int Length)? SelectionFor(CompareSourceCell cell)
    {
        if (OrderedSelection() is not { } selection || cell.Side != selection.Start.Side ||
            cell.FindAncestorOfType<CompareTextRow>() is not { } row ||
            !IsCurrent(row) ||
            row.FindAncestorOfType<ListBoxItem>() is not { } container) return null;
        int index = _rows.IndexFromContainer(container);
        if (index < selection.Start.Row || index > selection.End.Row) return null;
        int start = index == selection.Start.Row ? selection.Start.Offset : 0;
        int end = index == selection.End.Row ? selection.End.Offset : cell.Text.Length;
        start = Math.Clamp(start, 0, cell.Text.Length);
        end = Math.Clamp(end, start, cell.Text.Length);
        return end > start ? (start, end - start) : null;
    }

    /// <summary>Source text only: gaps are omitted, source lines are joined with LF, and no final LF is added.</summary>
    internal (string? Text, bool TooLarge) SelectedSourceText(int maxCharacters = 4 * 1024 * 1024)
    {
        if (OrderedSelection() is not { } selection || _rows.ItemsSource is not IReadOnlyList<object> rows ||
            selection.Start.Row < 0 || selection.End.Row >= rows.Count ||
            selection.Start.Row == selection.End.Row && selection.Start.Offset == selection.End.Offset) return (null, false);
        var parts = new List<string>();
        int total = 0;
        for (int i = selection.Start.Row; i <= selection.End.Row; i++)
        {
            if (rows[i] is not CompareRow row) continue;
            int? line = selection.Start.Side == 0 ? row.LeftLine : row.RightLine;
            if (line is null) continue;
            string source = selection.Start.Side == 0 ? row.LeftText : row.RightText;
            int start = Math.Clamp(i == selection.Start.Row ? selection.Start.Offset : 0, 0, source.Length);
            int end = Math.Clamp(i == selection.End.Row ? selection.End.Offset : source.Length, start, source.Length);
            int length = end - start + (parts.Count > 0 ? 1 : 0);
            if (length > maxCharacters - total) return (null, true);
            total += length;
            parts.Add(source[start..end]);
        }
        return (parts.Count == 0 ? null : string.Join('\n', parts), false);
    }

    private void InvalidateSelection()
    {
        AutomationProperties.SetHelpText(this, OrderedSelection() is { } selected
            ? $"{(selected.Start.Side == 0 ? "Left" : "Right")} source selected from aligned row {selected.Start.Row + 1:N0}, character {selected.Start.Offset + 1:N0}, to row {selected.End.Row + 1:N0}, character {selected.End.Offset + 1:N0}. Ctrl+C copies source text; gaps are omitted and selected lines use LF."
            : "Choose a source side by clicking its text. Drag, Shift+click, or use Shift+arrow keys to select; Ctrl+C copies source text.");
        foreach (var row in RealizedRows())
        {
            row.LeftCell?.InvalidateVisual();
            row.RightCell?.InvalidateVisual();
        }
        SelectionChanged?.Invoke();
    }

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
            int at = _selectionEnd is { } selection && selection.Row == pending.Index
                ? selection.Offset
                : evidence.Start >= 0 ? evidence.Start : source.EndingDiffers ? Math.Max(source.LeftText.Length, source.RightText.Length) : 0;
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
        if (_disposed) return;
        if (e.Source is CompareSourceCell cell && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift &&
            e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.Up or Key.Down &&
            MoveSourceCaret(cell, e.Key, e.KeyModifiers == KeyModifiers.Shift))
        {
            e.Handled = true;
            return;
        }
        if (_wrap || e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Left or Key.Right)) return;
        _horizontal.Value += e.Key == Key.Left ? -_horizontal.SmallChange : _horizontal.SmallChange;
        e.Handled = true;
    }

    private bool MoveSourceCaret(CompareSourceCell cell, Key key, bool extend)
    {
        if (cell.FindAncestorOfType<CompareTextRow>() is not { } visual || !IsCurrent(visual) ||
            visual.FindAncestorOfType<ListBoxItem>() is not { } container ||
            _rows.ItemsSource is not IReadOnlyList<object> rows) return false;
        int sourceRow = _rows.IndexFromContainer(container);
        if (sourceRow < 0 || sourceRow >= rows.Count || !ReferenceEquals(rows[sourceRow], visual.Source)) return false;
        int side = cell.Side;
        var current = _selectionEnd is { } end && end.Side == side ? end : new TextPoint(sourceRow, side, 0);
        int row = current.Row, offset = current.Offset;
        if (row < 0 || row >= rows.Count || rows[row] is not CompareRow source) return false;
        int length = (side == 0 ? source.LeftText : source.RightText).Length;
        switch (key)
        {
            case Key.Home: offset = 0; break;
            case Key.End: offset = length; break;
            case Key.Left when offset > 0: offset--; break;
            case Key.Right when offset < length: offset++; break;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                int step = key is Key.Left or Key.Up ? -1 : 1;
                int next = row + step;
                while (next >= 0 && next < rows.Count &&
                    (rows[next] is not CompareRow candidate ||
                     (side == 0 ? candidate.LeftLine : candidate.RightLine) is null)) next += step;
                if (next < 0 || next >= rows.Count || rows[next] is not CompareRow nextSource) return true;
                row = next;
                int nextLength = (side == 0 ? nextSource.LeftText : nextSource.RightText).Length;
                offset = key switch
                {
                    Key.Left => nextLength,
                    Key.Right => 0,
                    _ => Math.Min(offset, nextLength),
                };
                break;
        }
        if (!extend) _selectionAnchor = new TextPoint(row, side, offset);
        else if (_selectionAnchor is not { } anchor || anchor.Side != side) _selectionAnchor = current;
        _selectionEnd = new TextPoint(row, side, offset);
        InvalidateSelection();
        SelectionGestureActive = true;
        try { _rows.SelectedIndex = row; }
        finally { SelectionGestureActive = false; }
        _rows.ScrollIntoView(row);
        RevealSelected();
        return true;
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
        CopyRequested = null;
        SelectionChanged = null;
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
        LeftCell = AddSource(1, 0, source.LeftLine, source.LeftText, "Left", spans?.Left);
        RightCell = AddSource(3, 1, source.RightLine, source.RightText, "Right", spans?.Right);
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

    private CompareSourceCell? AddSource(int column, int sideIndex, int? line, string text, string side, IReadOnlyList<(int Start, int Length)>? spans)
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
            cell = new CompareSourceCell(_owner, text, spans, sideIndex);
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
    private Color _selectionBase;
    private IBrush? _selectionFill;
    private LayoutKey? _layoutKey;
    private Rect[] _underlines = [];
    private bool _retired;
    internal string Text { get; }
    internal int Side { get; }
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

    internal CompareSourceCell(CompareTextView owner, string source, IReadOnlyList<(int Start, int Length)>? spans, int side = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.Length, CompareTextView.MaximumLineCharacters);
        _owner = owner;
        _spans = spans;
        Side = side;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        Margin = new Thickness(0, 2, 4, 2);
        Text = source;
        FontFamily = Mono;
        AutomationProperties.SetHelpText(this, "Read-only source text. Drag or Shift+click to select source characters; Ctrl+C copies them without line numbers. Alt+L opens complete source-line details.");
        var copySelected = new MenuItem { Header = "Copy selected text" };
        var copyLine = new MenuItem { Header = "Copy this source line" };
        var copyDifference = new MenuItem { Header = "Copy aligned difference" };
        copySelected.Click += (_, _) => _owner.RequestCopy(CompareTextCopyAction.Selection, this);
        copyLine.Click += (_, _) => _owner.RequestCopy(CompareTextCopyAction.Line, this);
        copyDifference.Click += (_, _) => _owner.RequestCopy(CompareTextCopyAction.Difference, this);
        ContextMenu = new ContextMenu { Items = { copySelected, copyLine, copyDifference } };
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

    internal int SourceOffset(Point point)
    {
        if (_sourceLayout is null) EnsureLayout(Bounds.Width);
        if (_sourceLayout is null) return 0;
        var local = new Point(point.X + _owner.HorizontalOffset, Math.Clamp(point.Y, 0, _sourceLayout.Height));
        return Math.Clamp(_sourceLayout.HitTestPoint(local).TextPosition, 0, Text.Length);
    }

    public override void Render(DrawingContext context)
    {
        if (_sourceLayout is not { } layout || Bounds.Width < MinimumShapingWidth) return;
        var origin = TextOrigin;
        if (_owner.SelectionFor(this) is { } selection)
        {
            Color color = this.TryFindResource("FcFocusBackground", ActualThemeVariant, out var resource) &&
                resource is ISolidColorBrush brush ? brush.Color : Colors.CornflowerBlue;
            if (_selectionFill is null || _selectionBase != color)
            {
                _selectionBase = color;
                _selectionFill = new SolidColorBrush(Color.FromArgb(0x70, color.R, color.G, color.B));
            }
            using (context.PushClip(new Rect(0, 0, Bounds.Width, Bounds.Height)))
            {
                foreach (var bounds in layout.HitTestTextRange(selection.Start, selection.Length))
                    context.DrawRectangle(_selectionFill, null, bounds.Translate(new Vector(origin.X, origin.Y)));
            }
        }
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
