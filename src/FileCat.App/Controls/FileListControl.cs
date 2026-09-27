using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Controls;

/// <summary>
/// Commander-style virtualized listing (ADR-02): draws only the visible rows straight from the
/// <see cref="ListingModel"/>, so a million entries never become a million controls. Focus, marked
/// selection, active panel, and target panel each have a non-color cue (outline, mark bar + bold,
/// accent header, dashed outline) in addition to color (PI-04).
/// </summary>
public sealed class FileListControl : Control
{
    public static readonly StyledProperty<TabViewModel?> TabProperty =
        AvaloniaProperty.Register<FileListControl, TabViewModel?>(nameof(Tab));

    public static readonly StyledProperty<bool> IsActivePanelProperty =
        AvaloniaProperty.Register<FileListControl, bool>(nameof(IsActivePanel));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<FileListControl>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<FileListControl>();

    private const double Padding = 6;
    private const double IconSize = 16;
    private const double MarkGutter = 5;
    private const double ResizeGrip = 4;

    private readonly ScrollBar _vbar;
    private readonly Dictionary<long, CellText> _textCache = new();
    private SimpleGlyphs? _glyphs;
    private SimpleGlyphs? _boldGlyphs;

    /// <summary>Diagnostics hook (TV-01 benchmark): duration (ms) and UI-thread allocation (bytes) of each list render.</summary>
    internal static Action<double, long>? RenderTimed;
    private ListingModel? _listing;
    private int _topRow;
    private double _rowHeight = 20;
    private double _headerHeight = 22;
    private ColumnSpec[] _columns = ColumnProfiles.Defaults[0].Columns;
    private double[] _columnX = [];
    private double[] _columnW = [];
    private Typeface _typeface;
    private Typeface _boldTypeface;
    // Cached row text belongs to one store instance: a refresh swaps stores, so identity (not generation) keys it.
    private EntryStore? _cachedStore;
    private int _anchorRow = -1;
    private bool? _extendState;
    private int _resizingColumn = -1;
    private double _resizeStartX;
    private double _resizeStartWidth;
    // Live widths while dragging a border, by column position (cleared when the profile stores them).
    private readonly Dictionary<int, double> _widthOverrides = new();
    private bool _resizeInverse;
    private Point? _dragStart;
    private PointerPressedEventArgs? _pressArgs;
    private int _pressedRow = -1;
    private DispatcherTimer? _loadingHintTimer;
    private bool _showLoadingHint;
    private IImage? _linkOverlay;
    private readonly Action _metadataHandler;
    private Core.Metadata.MetadataService? _metadataSource;

    private readonly TextBox _renameEditor;
    private readonly Border _renameErrorBox;
    private readonly TextBlock _renameErrorText;
    private TaskCompletionSource<string?>? _renameCompletion;
    private PromptOptions? _renameOptions;
    private int _renameStoreIndex = -1;

    public bool IsRenaming => _renameCompletion is not null;

    // Brushes resolved from theme tokens.
    private IBrush _bg = Brushes.White, _bgInactive = Brushes.White, _header = Brushes.LightGray, _text = Brushes.Black;
    private IBrush _muted = Brushes.Gray, _dim = Brushes.Gray, _dir = Brushes.Black, _archive = Brushes.Brown, _exec = Brushes.Green, _link = Brushes.Blue;
    private IBrush _markedText = Brushes.Red, _markedBg = Brushes.MistyRose, _focusBg = Brushes.LightBlue, _focusText = Brushes.Black;
    private IBrush _focusBorder = Brushes.Blue, _focusInactive = Brushes.Gray, _grid = Brushes.LightGray, _headerText = Brushes.Black;
    private IBrush _accent = Brushes.Blue, _error = Brushes.Red;

    public FileListControl()
    {
        Focusable = true;
        ClipToBounds = true;
        // FileCat draws its own focus cues (row outline, panel frame); the default adorner would duplicate them.
        FocusAdorner = null;
        _vbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Vertical, AllowAutoHide = false };
        _vbar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                int top = (int)Math.Round(_vbar.Value);
                if (top != _topRow)
                {
                    _topRow = top;
                    if (IsRenaming) InvalidateArrange();
                    InvalidateVisual();
                }
            }
        };
        VisualChildren.Add(_vbar);
        LogicalChildren.Add(_vbar);
        _renameEditor = new TextBox
        {
            IsVisible = false,
            AcceptsReturn = false,
            Padding = new Thickness(3, 0),
            MinWidth = 80,
        };
        AutomationProperties.SetName(_renameEditor, "Rename selected item");
        _renameEditor.KeyDown += OnRenameKeyDown;
        _renameEditor.LostFocus += (_, _) => FinishRename(null, restoreFocus: false);
        _renameEditor.TextChanged += (_, _) => ClearRenameError();
        VisualChildren.Add(_renameEditor);
        LogicalChildren.Add(_renameEditor);
        _renameErrorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _renameErrorBox = new Border
        {
            Child = _renameErrorText,
            IsVisible = false,
            Padding = new Thickness(5, 2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        VisualChildren.Add(_renameErrorBox);
        LogicalChildren.Add(_renameErrorBox);
        ThemeManager.ThemeChanged += OnThemeChanged;
        _metadataHandler = () => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
        UpdateTypefaces();
    }

    public TabViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public bool IsActivePanel
    {
        get => GetValue(IsActivePanelProperty);
        set => SetValue(IsActivePanelProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public int VisibleRowCapacity => Math.Max(1, (int)((Bounds.Height - _headerHeight) / _rowHeight));

    /// <summary>Raised when the user asks to open the focused row (double click).</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised when the control is clicked so the owning panel becomes active.</summary>
    public event EventHandler? ActivateRequested;

    public event EventHandler<Point>? ContextMenuRequested;

    /// <summary>Raised when a drag starts; carries the original press, which drag-and-drop requires.</summary>
    public event EventHandler<PointerPressedEventArgs>? DragRequested;

    public event EventHandler<int>? MiddleClickRequested;

    /// <summary>Edits the focused name in its row. Null means the user canceled or the row vanished.</summary>
    public Task<string?>? BeginRename(PromptOptions options)
    {
        if (_listing is null || !_listing.TryGetFocused(out var entry) || entry.Kind == EntryKind.Parent)
            return null;
        FinishRename(null, restoreFocus: false);
        EnsureFocusVisible();
        _renameOptions = options;
        _renameStoreIndex = _listing.FocusedStoreIndex;
        _renameEditor.Text = options.Text;
        _renameCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _renameEditor.IsVisible = true;
        ClearRenameError();
        InvalidateMeasure();
        InvalidateArrange();
        _renameEditor.Focus();
        SelectRenameText(options);
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsRenaming) return;
            _renameEditor.Focus();
            SelectRenameText(options);
        }, DispatcherPriority.Input);
        return _renameCompletion.Task;
    }

    private void SelectRenameText(PromptOptions options)
    {
        var name = _renameEditor.Text ?? string.Empty;
        if (options.SelectStem)
        {
            int dot = name.LastIndexOf('.');
            _renameEditor.SelectionStart = 0;
            _renameEditor.SelectionEnd = dot > 0 ? dot : name.Length;
        }
        else _renameEditor.SelectAll();
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            FinishRename(null);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var name = _renameEditor.Text ?? string.Empty;
            var error = _renameOptions?.Validate?.Invoke(name);
            if (error is not null) ShowRenameError(error);
            else FinishRename(name);
        }
    }

    private void ShowRenameError(string error)
    {
        _renameErrorText.Text = error;
        _renameErrorText.Foreground = _error;
        _renameErrorBox.BorderBrush = _error;
        _renameErrorBox.Background = _bg;
        _renameEditor.BorderBrush = _error;
        ToolTip.SetTip(_renameEditor, error);
        _renameErrorBox.IsVisible = true;
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void ClearRenameError()
    {
        if (_renameErrorBox is null) return;
        _renameErrorBox.IsVisible = false;
        _renameEditor.BorderBrush = null;
        ToolTip.SetTip(_renameEditor, null);
    }

    private void FinishRename(string? name, bool restoreFocus = true)
    {
        var completion = _renameCompletion;
        if (completion is null) return;
        _renameCompletion = null;
        _renameOptions = null;
        _renameStoreIndex = -1;
        _renameEditor.IsVisible = false;
        ClearRenameError();
        InvalidateMeasure();
        InvalidateArrange();
        if (restoreFocus) Focus();
        completion.TrySetResult(name);
    }

    private void ArrangeRename(Size finalSize, double scrollWidth)
    {
        if (!IsRenaming || _listing is null) return;
        int row = _listing.GetVisibleIndex(_renameStoreIndex);
        if (row < 0) { FinishRename(null, restoreFocus: false); return; }
        int nameColumn = Array.FindIndex(_columns, c => c.Field == ColumnField.Name);
        if (nameColumn < 0) nameColumn = 0;
        double y = _headerHeight + (row - _topRow) * _rowHeight;
        double x = _columnX[nameColumn] + MarkGutter + IconSize + 4;
        double available = Math.Max(80, finalSize.Width - scrollWidth - x - Padding);
        double width = Math.Min(available, Math.Max(140, _columnW[nameColumn] - (x - _columnX[nameColumn]) - Padding));
        _renameEditor.Arrange(new Rect(x, y + 1, width, Math.Max(18, _rowHeight - 2)));
        if (_renameErrorBox.IsVisible)
        {
            double errorY = y + _rowHeight + 2;
            if (errorY + 40 > finalSize.Height) errorY = Math.Max(_headerHeight, y - 42);
            _renameErrorBox.Arrange(new Rect(x, errorY, Math.Min(Math.Max(240, width), available), 40));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TabProperty)
        {
            FinishRename(null, restoreFocus: false);
            if (change.OldValue is TabViewModel oldTab)
            {
                oldTab.Listing.Changed -= OnListingChanged;
                oldTab.ColumnsChanged -= OnColumnsChanged;
            }
            _listing = null;
            if (change.NewValue is TabViewModel tab)
            {
                _listing = tab.Listing;
                _listing.Changed += OnListingChanged;
                if (!ReferenceEquals(_metadataSource, tab.Services.Metadata))
                {
                    if (_metadataSource is not null) _metadataSource.ValuesChanged -= _metadataHandler;
                    _metadataSource = tab.Services.Metadata;
                    _metadataSource.ValuesChanged += _metadataHandler;
                }
                tab.ColumnsChanged += OnColumnsChanged;
                _columns = tab.Columns;
            }
            _widthOverrides.Clear();
            ClearTextCache();
            _topRow = 0;
            EnsureFocusVisible();
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == IsActivePanelProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == FontFamilyProperty || change.Property == FontSizeProperty)
        {
            UpdateTypefaces();
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == BoundsProperty)
        {
            EnsureFocusVisible();
        }
    }

    private void OnColumnsChanged(object? sender, EventArgs e)
    {
        if (Tab is null) return;
        _columns = Tab.Columns;
        _widthOverrides.Clear();
        ClearTextCache();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnThemeChanged()
    {
        ClearTextCache();
        _linkOverlay = null;
        ResolveBrushes();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResolveBrushes();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loadingHintTimer?.Stop();
        FinishRename(null, restoreFocus: false);
    }

    private void UpdateTypefaces()
    {
        var family = FontFamily ?? FontFamily.Default;
        _typeface = new Typeface(family);
        _boldTypeface = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
        double size = FontSize > 0 ? FontSize : 13;
        _rowHeight = Math.Ceiling(size * 1.62);
        _headerHeight = Math.Ceiling(size * 1.75);
        _glyphs = SimpleGlyphs.TryCreate(_typeface, size);
        _boldGlyphs = SimpleGlyphs.TryCreate(_boldTypeface, size);
        ClearTextCache();
    }

    private void ClearTextCache()
    {
        foreach (var cell in _textCache.Values) cell.Dispose();
        _textCache.Clear();
    }

    private void ResolveBrushes()
    {
        IBrush B(string key, IBrush fallback) =>
            this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;
        _bg = B("FcPanel", Brushes.White);
        _bgInactive = B("FcPanelInactive", _bg);
        _header = B("FcHeader", Brushes.LightGray);
        _text = B("FcText", Brushes.Black);
        _muted = B("FcTextMuted", Brushes.Gray);
        _dim = B("FcTextDim", Brushes.Gray);
        _dir = B("FcTextDirectory", _text);
        _archive = B("FcTextArchive", _text);
        _exec = B("FcTextExecutable", _text);
        _link = B("FcTextLink", _text);
        _markedText = B("FcTextMarked", Brushes.Red);
        _markedBg = B("FcMarkedBackground", Brushes.Transparent);
        _focusBg = B("FcFocusBackground", Brushes.LightBlue);
        _focusText = B("FcFocusText", _text);
        _focusBorder = B("FcFocusBorder", Brushes.Blue);
        _focusInactive = B("FcFocusInactiveBorder", Brushes.Gray);
        _grid = B("FcGridLine", Brushes.LightGray);
        _headerText = B("FcHeaderText", _text);
        _accent = B("FcActiveAccent", Brushes.Blue);
        _error = B("FcError", Brushes.Red);
    }

    // ---- Listing notifications ------------------------------------------------------------------------------

    private void OnListingChanged(object? sender, ListingChange change)
    {
        if (!ReferenceEquals(sender, _listing)) return;
        // Screen readers follow the focused item through the list peer's name.
        if ((change & (ListingChange.Focus | ListingChange.Marks | ListingChange.Reset)) != 0) _automationPeer?.AnnounceFocus();
        if (IsRenaming)
        {
            if ((change & ListingChange.Reset) != 0 || _listing!.GetVisibleIndex(_renameStoreIndex) < 0)
                FinishRename(null, restoreFocus: false);
            else InvalidateArrange();
        }
        if ((change & ListingChange.Reset) != 0)
        {
            ClearTextCache();
            _topRow = 0;
            _anchorRow = -1;
            _cachedStore = _listing!.Store;
            _showLoadingHint = false;
            _loadingHintTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
            {
                _loadingHintTimer!.Stop();
                _showLoadingHint = true;
                InvalidateVisual();
            });
            _loadingHintTimer.Stop();
            _loadingHintTimer.Start();
        }
        EnsureCacheStore();
        if ((change & (ListingChange.Rows | ListingChange.Marks)) != 0 && _textCache.Count > 4000) ClearTextCache();
        if ((change & ListingChange.Marks) != 0) ClearTextCache();
        if ((change & (ListingChange.Focus | ListingChange.Reset | ListingChange.Rows)) != 0) EnsureFocusVisible();
        UpdateScrollBar();
        InvalidateVisual();
    }

    private void EnsureCacheStore()
    {
        if (_listing is not null && !ReferenceEquals(_listing.Store, _cachedStore))
        {
            ClearTextCache();
            _cachedStore = _listing.Store;
        }
    }

    public void EnsureFocusVisible()
    {
        if (_listing is null) return;
        int f = _listing.FocusedIndex;
        if (f < 0) return;
        int cap = VisibleRowCapacity;
        if (f < _topRow) _topRow = f;
        else if (f >= _topRow + cap) _topRow = f - cap + 1;
        ClampTop();
        UpdateScrollBar();
    }

    private void ClampTop()
    {
        int count = _listing?.VisibleCount ?? 0;
        _topRow = Math.Clamp(_topRow, 0, Math.Max(0, count - VisibleRowCapacity));
    }

    private void UpdateScrollBar()
    {
        int count = _listing?.VisibleCount ?? 0;
        int cap = VisibleRowCapacity;
        _vbar.Maximum = Math.Max(0, count - cap);
        _vbar.ViewportSize = cap;
        _vbar.SmallChange = 1;
        _vbar.LargeChange = Math.Max(1, cap - 1);
        _vbar.Value = _topRow;
        _vbar.IsVisible = count > cap;
    }

    // ---- Layout -----------------------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        _vbar.Measure(availableSize);
        if (IsRenaming) _renameEditor.Measure(availableSize);
        if (_renameErrorBox.IsVisible) _renameErrorBox.Measure(availableSize);
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double sbw = _vbar.DesiredSize.Width > 0 ? _vbar.DesiredSize.Width : 12;
        _vbar.Arrange(new Rect(finalSize.Width - sbw, _headerHeight, sbw, Math.Max(0, finalSize.Height - _headerHeight)));
        LayoutColumns(finalSize.Width - (_vbar.IsVisible ? sbw : 0));
        ClampTop();
        UpdateScrollBar();
        ArrangeRename(finalSize, _vbar.IsVisible ? sbw : 0);
        return finalSize;
    }

    private void LayoutColumns(double totalWidth)
    {
        int n = _columns.Length;
        _columnX = new double[n];
        _columnW = new double[n];
        double fixedSum = 0, starSum = 0;
        for (int i = 0; i < n; i++)
        {
            var c = _columns[i];
            double w = _widthOverrides.TryGetValue(i, out var ow) ? ow : c.Width;
            if (c.Star && !_widthOverrides.ContainsKey(i)) starSum += w;
            else fixedSum += w;
        }
        double starSpace = Math.Max(80, totalWidth - fixedSum);
        double x = 0;
        for (int i = 0; i < n; i++)
        {
            var c = _columns[i];
            double w = _widthOverrides.TryGetValue(i, out var ow) ? ow
                : c.Star ? starSpace * (c.Width / Math.Max(1, starSum)) : c.Width;
            _columnX[i] = x;
            _columnW[i] = Math.Max(24, w);
            x += _columnW[i];
        }
    }

    // ---- Rendering ----------------------------------------------------------------------------------------------

    public override void Render(DrawingContext dc)
    {
        long started = RenderTimed is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        long allocated = started == 0 ? 0 : GC.GetAllocatedBytesForCurrentThread();
        RenderCore(dc);
        if (started != 0) RenderTimed?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private void RenderCore(DrawingContext dc)
    {
        var bounds = new Rect(Bounds.Size);
        bool active = IsActivePanel;
        dc.FillRectangle(active ? _bg : _bgInactive, bounds);
        double contentWidth = bounds.Width - (_vbar.IsVisible ? _vbar.Bounds.Width : 0);
        RenderHeader(dc, contentWidth, active);

        var listing = _listing;
        if (listing is null) return;
        EnsureCacheStore();
        int count = listing.VisibleCount;
        using (dc.PushClip(new Rect(0, _headerHeight, contentWidth, Math.Max(0, bounds.Height - _headerHeight))))
        {
            int cap = VisibleRowCapacity + 1;
            // Scrolling must not keep every page it passed: a few pages of cells, so cached text dies young (GC).
            if (_textCache.Count > cap * _columns.Length * 3) ClearTextCache();
            int focused = listing.FocusedIndex;
            var visible = new HashSet<int>();
            for (int row = _topRow; row < Math.Min(count, _topRow + cap); row++)
            {
                visible.Add(listing.GetStoreIndex(row));
                double y = _headerHeight + (row - _topRow) * _rowHeight;
                RenderRow(dc, listing, row, y, contentWidth, row == focused, active);
            }
            RenderEmptyState(dc, listing, count, contentWidth, bounds.Height);
            if (Tab is { } tab) tab.VisibleStoreIndices = visible;
        }
    }

    private void RenderHeader(DrawingContext dc, double width, bool active)
    {
        var rect = new Rect(0, 0, Bounds.Width, _headerHeight);
        dc.FillRectangle(_header, rect);
        // Active panel: a strong accent bar under the header (non-color cue: thickness).
        dc.FillRectangle(active ? _accent : _grid, new Rect(0, _headerHeight - (active ? 2 : 1), Bounds.Width, active ? 2 : 1));
        var sort = _listing?.Sort ?? default;
        for (int i = 0; i < _columns.Length; i++)
        {
            var c = _columns[i];
            string title = c.Header;
            if (c.SortField is { } sf && sf == sort.Field && (sf != SortField.Metadata || sort.MetadataId == c.MetadataId)) title += sort.Descending ? " ▼" : " ▲";
            using var ft = MakeText(title, _headerText, _typeface, _columnW[i] - 2 * Padding);
            double x = c.RightAlign ? _columnX[i] + _columnW[i] - Padding - ft.Width : _columnX[i] + Padding + (i == 0 ? IconSize + MarkGutter + 4 : 0);
            ft.Draw(dc, new Point(x, (_headerHeight - ft.Height) / 2));
            if (i > 0) dc.FillRectangle(_grid, new Rect(_columnX[i], 4, 1, _headerHeight - 8));
        }
    }

    private void RenderRow(DrawingContext dc, ListingModel listing, int row, double y, double width, bool focused, bool active)
    {
        var e = listing.GetVisible(row);
        int storeIndex = listing.GetStoreIndex(row);
        bool marked = listing.IsMarked(storeIndex);
        var rowRect = new Rect(0, y, width, _rowHeight);

        if (marked) dc.FillRectangle(_markedBg, rowRect);
        if (focused && active) dc.FillRectangle(_focusBg, rowRect);
        if (marked) dc.FillRectangle(_markedText, new Rect(0, y + 1, 3, _rowHeight - 2));

        IBrush textBrush = marked ? _markedText
            : focused && active ? _focusText
            : e.Has(EntryFlags.Hidden) || e.Has(EntryFlags.System) || e.Has(EntryFlags.Unavailable) ? _dim
            : e.IsContainer ? _dir
            : e.Has(EntryFlags.Container) ? _archive
            : IconProvider.Classify(e) == IconKind.Executable ? _exec
            : _text;
        int style = marked ? 1 : focused && active ? 2 : ReferenceEquals(textBrush, _dim) ? 3 : ReferenceEquals(textBrush, _dir) ? 4
            : ReferenceEquals(textBrush, _archive) ? 5 : ReferenceEquals(textBrush, _exec) ? 6 : 0;
        var typeface = marked ? _boldTypeface : _typeface;

        for (int i = 0; i < _columns.Length; i++)
        {
            var c = _columns[i];
            double colX = _columnX[i];
            double colW = _columnW[i];
            double textX = colX + Padding;
            double avail = colW - 2 * Padding;
            if (i == 0)
            {
                double iconX = colX + MarkGutter;
                var icon = Tab?.Services.Icons.GetIcon(e);
                if (icon is not null)
                {
                    var iconRect = new Rect(iconX, y + (_rowHeight - IconSize) / 2, IconSize, IconSize);
                    bool dimIcon = e.Has(EntryFlags.Hidden) || e.Has(EntryFlags.Unavailable);
                    using (dimIcon ? dc.PushOpacity(0.5) : default(DrawingContext.PushedState?))
                    {
                        dc.DrawImage(icon, iconRect);
                    }
                    if (e.Has(EntryFlags.Link))
                    {
                        _linkOverlay ??= VectorIcons.LinkOverlay();
                        dc.DrawImage(_linkOverlay, iconRect);
                    }
                }
                textX = iconX + IconSize + 4;
                avail = colX + colW - textX - Padding;
            }
            if (avail < 4) continue;
            if (c.Field == ColumnField.Metadata && c.MetadataId is { } metadataId)
            {
                // Metadata changes state (pending -> value), so it is not cached with the static cells.
                bool pending = false;
                var mtext = Tab is { } owner ? owner.GetMetadataText(e, storeIndex, metadataId, out pending) : string.Empty;
                if (mtext.Length == 0) continue;
                using var mft = MakeText(mtext, pending ? _muted : textBrush, typeface, avail);
                double mx = c.RightAlign ? colX + colW - Padding - mft.Width : textX;
                mft.Draw(dc, new Point(mx, y + (_rowHeight - mft.Height) / 2));
                continue;
            }
            long key = ((long)storeIndex << 16) | ((long)i << 8) | (uint)style;
            if (!_textCache.TryGetValue(key, out var ft) || Math.Abs(ft.MaxWidth - avail) > 0.5)
            {
                var s = CellString(e, c.Field);
                ft?.Dispose();
                _textCache.Remove(key);
                if (s.Length == 0) continue;
                ft = MakeText(s, textBrush, typeface, avail);
                _textCache[key] = ft;
            }
            double x = c.RightAlign ? colX + colW - Padding - ft.Width : textX;
            ft.Draw(dc, new Point(x, y + (_rowHeight - ft.Height) / 2));
        }

        if (focused)
        {
            var r = rowRect.Deflate(new Thickness(0.5, 0.5, 0.5, 0.5));
            if (active)
            {
                dc.DrawRectangle(null, new Pen(_focusBorder, 1.5), r);
            }
            else
            {
                dc.DrawRectangle(null, new Pen(_focusInactive, 1, new DashStyle([2, 2], 0)), r);
            }
        }
    }

    private void RenderEmptyState(DrawingContext dc, ListingModel listing, int count, double width, double height)
    {
        int realRows = count - (listing.HasParentRow ? 1 : 0);
        string? message = null;
        IBrush brush = _muted;
        if (listing.State == ListingState.Failed)
        {
            message = (listing.Error ?? "This location cannot be listed.") + "\nCtrl+R retries · Backspace goes up";
            brush = _error;
        }
        else if (listing.State == ListingState.Loading && realRows <= 0)
        {
            if (_showLoadingHint) message = "Loading…  (Esc stops)";
        }
        else if (realRows <= 0 && listing.State == ListingState.Complete)
        {
            message = listing.Filter is not null && listing.TotalCount > 0
                ? $"No items match the filter \"{listing.Filter.Text}\".\nEsc clears the filter."
                : "This location is empty.";
        }
        if (message is null) return;
        var ft = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, FontSize > 0 ? FontSize : 13, brush)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = Math.Max(50, width - 40),
        };
        double top = _headerHeight + (listing.HasParentRow ? _rowHeight : 0) + 24;
        dc.DrawText(ft, new Point(20, Math.Min(top, Math.Max(_headerHeight, height / 2 - ft.Height))));
    }

    /// <summary>Simple-script text becomes a glyph run; anything needing shaping or font fallback is formatted.</summary>
    private CellText MakeText(string s, IBrush brush, Typeface typeface, double maxWidth)
    {
        var glyphs = typeface == _boldTypeface ? _boldGlyphs : _glyphs;
        return glyphs?.TryLayout(s, brush, maxWidth) ?? Controls.CellText.Formatted(s, brush, typeface, FontSize > 0 ? FontSize : 13, maxWidth);
    }

    private string CellString(in EntryData e, ColumnField field) => field switch
    {
        ColumnField.Name => DisplayName(e),
        ColumnField.Extension => e.IsContainer ? string.Empty : NameParts.GetExtension(e.Name),
        ColumnField.Size => Formatters.SizeCell(e),
        ColumnField.Modified => e.Kind == EntryKind.Parent ? string.Empty : Formatters.Date(e.Modified),
        ColumnField.Created => e.Kind == EntryKind.Parent ? string.Empty : Formatters.Date(e.Created),
        ColumnField.Attributes => Formatters.Attributes(e),
        ColumnField.Folder => Tab?.GetFolderText(e) ?? string.Empty,
        ColumnField.Kind => Tab?.GetKindText(e) ?? string.Empty,
        ColumnField.Details => Tab?.GetDetailsText(e) ?? string.Empty,
        _ => string.Empty,
    };

    private string DisplayName(in EntryData e)
    {
        if (e.Kind == EntryKind.RegistryValue && e.Name.Length == 0) return "(Default)";
        if (e.Kind == EntryKind.Drive && e.Tag is Core.FileSystem.DriveTag t)
            return string.IsNullOrEmpty(t.Label) ? $"{e.Name}  {t.DriveType}" : $"{e.Name}  {t.Label}";
        bool showExt = !_columns.Any(c => c.Field == ColumnField.Extension);
        var name = e.IsContainer || showExt ? e.Name : NameParts.GetExtension(e.Name).Length > 0 ? NameParts.GetStem(e.Name) : e.Name;
        return Formatters.SafeName(name);
    }

    // ---- Keyboard ------------------------------------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _listing is null || IsRenaming) return;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool other = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        if (other) return;
        int f = Math.Max(0, _listing.FocusedIndex);
        int count = _listing.VisibleCount;
        int cap = VisibleRowCapacity;
        int target = e.Key switch
        {
            Key.Up => f - 1,
            Key.Down => f + 1,
            Key.PageUp => f - (cap - 1),
            Key.PageDown => f + (cap - 1),
            Key.Home => 0,
            Key.End => count - 1,
            _ => int.MinValue,
        };
        if (target == int.MinValue)
        {
            if (!shift) _extendState = null;
            return;
        }
        e.Handled = true;
        target = Math.Clamp(target, 0, Math.Max(0, count - 1));
        if (shift)
        {
            // Commander-style: Shift+movement marks (or unmarks) the rows passed over.
            _extendState ??= !_listing.IsVisibleMarked(f);
            bool inclusive = e.Key is Key.Home or Key.End;
            if (target > f) _listing.SetMarkRange(f, inclusive ? target : target - 1, _extendState.Value);
            else if (target < f) _listing.SetMarkRange(inclusive ? target : target + 1, f, _extendState.Value);
            else _listing.SetMark(f, _extendState.Value);
        }
        else
        {
            _extendState = null;
        }
        _listing.SetFocus(target);
        Tab?.OnUserMovedFocus();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftShift or Key.RightShift) _extendState = null;
    }

    // ---- Pointer --------------------------------------------------------------------------------------------------

    private int RowAt(Point p)
    {
        if (p.Y < _headerHeight) return -1;
        int row = _topRow + (int)((p.Y - _headerHeight) / _rowHeight);
        return _listing is not null && row < _listing.VisibleCount ? row : -1;
    }

    /// <summary>
    /// The column a header border drag resizes. A fill column's right border resizes the column after it (dragging
    /// right narrows that one), so the fill column keeps absorbing the window width; the last fill border does nothing.
    /// </summary>
    private int ColumnEdgeAt(Point p) => ResizeTargetAt(p, out _);

    private int ResizeTargetAt(Point p, out bool inverse)
    {
        inverse = false;
        if (p.Y >= _headerHeight) return -1;
        for (int i = 0; i < _columns.Length; i++)
        {
            double right = _columnX[i] + _columnW[i];
            if (Math.Abs(p.X - right) > ResizeGrip) continue;
            if (!_columns[i].Star) return i;
            if (i + 1 < _columns.Length && !_columns[i + 1].Star)
            {
                inverse = true;
                return i + 1;
            }
            return -1;
        }
        return -1;
    }

    private int ColumnAt(Point p)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (p.X >= _columnX[i] && p.X < _columnX[i] + _columnW[i]) return i;
        }
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        ActivateRequested?.Invoke(this, EventArgs.Empty);
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        if (_listing is null) return;

        int edge = ResizeTargetAt(pos, out bool inverse);
        if (edge >= 0 && point.Properties.IsLeftButtonPressed)
        {
            _resizingColumn = edge;
            _resizeInverse = inverse;
            _resizeStartX = pos.X;
            _resizeStartWidth = _columnW[edge];
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (pos.Y < _headerHeight)
        {
            int col = ColumnAt(pos);
            if (col >= 0 && point.Properties.IsLeftButtonPressed && _columns[col] is { Field: ColumnField.Metadata, MetadataId: { } mid }) Tab?.SortByMetadata(mid);
            else if (col >= 0 && point.Properties.IsLeftButtonPressed && _columns[col].SortField is { } sf) Tab?.SortBy(sf);
            e.Handled = true;
            return;
        }

        int row = RowAt(pos);
        if (point.Properties.IsMiddleButtonPressed)
        {
            if (row >= 0) MiddleClickRequested?.Invoke(this, row);
            e.Handled = true;
            return;
        }
        if (row < 0)
        {
            e.Handled = true;
            return;
        }
        var mods = e.KeyModifiers;
        if (point.Properties.IsRightButtonPressed)
        {
            if (!_listing.IsVisibleMarked(row)) _listing.SetFocus(row);
            ContextMenuRequested?.Invoke(this, pos);
            e.Handled = true;
            return;
        }
        if ((mods & KeyModifiers.Control) != 0)
        {
            _listing.ToggleMark(row);
            _listing.SetFocus(row);
        }
        else if ((mods & KeyModifiers.Shift) != 0)
        {
            int from = _anchorRow >= 0 ? _anchorRow : Math.Max(0, _listing.FocusedIndex);
            _listing.SetMarkRange(from, row, true);
            _listing.SetFocus(row);
        }
        else
        {
            _listing.SetFocus(row);
            _anchorRow = row;
            if (e.ClickCount >= 2)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            _dragStart = pos;
            _pressedRow = row;
            _pressArgs = e;
        }
        Tab?.OnUserMovedFocus();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_resizingColumn >= 0)
        {
            double delta = pos.X - _resizeStartX;
            double w = Math.Max(30, _resizeStartWidth + (_resizeInverse ? -delta : delta));
            _widthOverrides[_resizingColumn] = w;
            ClearTextCache();
            InvalidateArrange();
            InvalidateVisual();
            return;
        }
        Cursor = ColumnEdgeAt(pos) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
        if (_dragStart is { } start && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(pos.X - start.X) > 6 || Math.Abs(pos.Y - start.Y) > 6)
            {
                _dragStart = null;
                if (_pressedRow >= 0 && _pressArgs is { } press) DragRequested?.Invoke(this, press);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        if (_resizingColumn >= 0)
        {
            int column = _resizingColumn;
            _resizingColumn = -1;
            e.Pointer.Capture(null);
            // The profile keeps the width for every tab using it (and across restarts).
            if (_widthOverrides.TryGetValue(column, out var width)) Tab?.SetColumnWidth(column, width);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _topRow -= (int)Math.Round(e.Delta.Y * 3);
        ClampTop();
        UpdateScrollBar();
        if (IsRenaming) InvalidateArrange();
        InvalidateVisual();
        e.Handled = true;
    }

    public Rect GetRowBounds(int row) => new(0, _headerHeight + (row - _topRow) * _rowHeight, Bounds.Width, _rowHeight);

    private FileListAutomationPeer? _automationPeer;

    protected override AutomationPeer OnCreateAutomationPeer() => _automationPeer = new FileListAutomationPeer(this);

    /// <summary>Right border of a laid-out column (tests and automation).</summary>
    internal double ColumnRightEdge(int column) => _columnX[column] + _columnW[column];

    internal string DescribeFocus()
    {
        if (_listing is null || !_listing.TryGetFocused(out var e)) return "Empty list";
        int i = _listing.FocusedIndex + 1;
        var kind = e.Kind == EntryKind.Parent ? "parent folder" : e.IsContainer ? "folder" : "file";
        var details = e.IsContainer ? string.Empty : ", " + Formatters.SizeWithUnit(e.Size);
        if (e.Modified > 0 && e.Kind != EntryKind.Parent) details += ", modified " + Formatters.Date(e.Modified);
        var marked = _listing.IsMarked(_listing.FocusedStoreIndex) ? ", marked" : string.Empty;
        var spokenName = e.Kind == EntryKind.RegistryValue && e.Name.Length == 0 ? "(Default)" : e.Name;
        return $"{spokenName}, {kind}{details}{marked}, {i} of {_listing.VisibleCount}";
    }
}

/// <summary>Summarized accessibility: exposes the list and its focused item without millions of nodes (§18.3).</summary>
internal sealed class FileListAutomationPeer(FileListControl owner) : ControlAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => "FileList";

    protected override string? GetNameCore() => owner.DescribeFocus();

    private string? _announced;

    /// <summary>Raises a name change when the focused item (or its mark) changed, so assistive technology speaks it.</summary>
    public void AnnounceFocus()
    {
        var name = owner.DescribeFocus();
        if (name == _announced) return;
        RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, _announced, name);
        _announced = name;
    }

    protected override bool IsKeyboardFocusableCore() => true;
}
