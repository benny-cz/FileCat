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
using FileCat.Core.Search;

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
    private readonly ScrollBar _hbar;
    private bool _updatingScrollBar;
    private bool _updatingHorizontalScrollBar;
    private int? _refreshTopRow;
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
    private double _contentExtent;
    private double _horizontalViewport;
    private double _horizontalOffset;
    private double _layoutHeight;
    private Typeface _typeface;
    private Typeface _boldTypeface;
    // Cached row text belongs to one store instance: a refresh swaps stores, so identity (not generation) keys it.
    private EntryStore? _cachedStore;
    // A visible row number is not an identity: sorting, filtering and streaming can move it.
    // Store indexes stay stable only within one EntryStore. Refresh intentionally starts a new anchor.
    private EntryStore? _anchorStore;
    private PanelViewModel? _identityPanel;
    private Window? _focusWindow;
    private bool _viewEventsAttached;
    private int _anchorStoreIndex = -1;
    private bool? _extendState;
    private int _resizingColumn = -1;
    private double _resizeStartX;
    private double _resizeStartWidth;
    // Live widths while dragging a border, by column position (cleared when the profile stores them).
    private readonly Dictionary<int, double> _widthOverrides = new();

    /// <summary>
    /// Fixed column widths are set for Segoe UI at 13 pixels; a larger size or a wider font (DejaVu Sans on Linux, a
    /// theme's monospace) widens them in proportion, so dates and sizes still fit. Never below 1: a narrower font keeps
    /// the widths set.
    /// </summary>
    private double _widthScale = 1;

    /// <summary>
    /// Date columns also follow the date format: widths are set for "28.09.2026 19:39", and "12/28/2026 10:58 PM" or a
    /// format with seconds needs more.
    /// </summary>
    private double _dateScale = 1;

    /// <summary>What <see cref="SampleWidth"/> measures in Segoe UI at 13 pixels, the font the fixed widths were set for.</summary>
    private const double ReferenceSampleWidth = 162.86;

    /// <summary>How much wider than set the fixed columns are drawn for the current font (tests).</summary>
    internal double WidthScale => _widthScale;

    /// <summary>How much wider than set the date columns are drawn for the current font and date format (tests).</summary>
    internal double DateScale => _dateScale;

    private double ScaleOf(ColumnSpec column) => column.Field is ColumnField.Modified or ColumnField.Created ? _dateScale : _widthScale;
    private double MinimumStarWidth(ColumnSpec column) => (column.Field == ColumnField.Name ? 140 : 80) * _widthScale;
    private bool _resizeInverse;
    private Point? _dragStart;
    private PointerPressedEventArgs? _pressArgs;
    private int _pressedRow = -1;
    private DispatcherTimer? _loadingHintTimer;
    private bool _showLoadingHint;
    private readonly Action _metadataHandler;
    private Core.Metadata.MetadataService? _metadataSource;
    private GitStatusSnapshot? _gitStatuses;
    private string? _gitStatusFolder;
    private CancellationTokenSource? _gitStatusCancel;
    private int _gitStatusGeneration;

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
                if (_updatingScrollBar) return;
                int top = (int)Math.Round(_vbar.Value);
                if (top != _topRow)
                {
                    _topRow = top;
                    if (_refreshTopRow.HasValue) _refreshTopRow = top;
                    if (IsRenaming) InvalidateArrange();
                    _automationPeer?.QueueUpdate(ListingChange.Rows);
                    InvalidateVisual();
                }
            }
        };
        VisualChildren.Add(_vbar);
        LogicalChildren.Add(_vbar);
        _hbar = new ScrollBar { Orientation = Avalonia.Layout.Orientation.Horizontal, AllowAutoHide = false, IsVisible = false };
        AutomationProperties.SetName(_hbar, "Listing columns");
        _hbar.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_updatingHorizontalScrollBar)
                ScrollToHorizontalOffset(_hbar.Value);
        };
        VisualChildren.Add(_hbar);
        LogicalChildren.Add(_hbar);
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
        _metadataHandler = () => Dispatcher.UIThread.Post(() =>
        {
            InvalidateVisual();
            _automationPeer?.QueueUpdate(ListingChange.State);
        }, DispatcherPriority.Background);
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

    private double HorizontalBarHeight => _hbar.DesiredSize.Height > 0 ? _hbar.DesiredSize.Height : 12;
    public int VisibleRowCapacity => Math.Max(1, (int)(((_layoutHeight > 0 ? _layoutHeight : Bounds.Height) - _headerHeight - (_hbar.IsVisible ? HorizontalBarHeight : 0)) / _rowHeight));
    internal double HorizontalOffset => _horizontalOffset;
    internal double HorizontalExtent => _contentExtent;
    internal double HorizontalViewport => _horizontalViewport;
    internal bool HorizontalScrollBarVisible => _hbar.IsVisible;
    internal bool VerticalScrollBarVisible => _vbar.IsVisible;

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
        ScrollToHorizontalOffset(0);
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
        double x = _columnX[nameColumn] - _horizontalOffset + MarkGutter + IconSize + 4;
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
            ResetSelectionGesture();
            if (change.OldValue is TabViewModel oldTab)
            {
                oldTab.Listing.Changed -= OnListingChanged;
                oldTab.ColumnsChanged -= OnColumnsChanged;
            }
            if (_identityPanel is not null) _identityPanel.PropertyChanged -= OnPanelIdentityChanged;
            _identityPanel = null;
            _listing = null;
            if (change.NewValue is TabViewModel tab)
            {
                _listing = tab.Listing;
                _listing.Changed += OnListingChanged;
                if (!ReferenceEquals(_metadataSource, tab.Services.Metadata))
                {
                    if (_metadataSource is not null) _metadataSource.ValuesChanged -= _metadataHandler;
                    _metadataSource = tab.Services.Metadata;
                    if (_viewEventsAttached) _metadataSource.ValuesChanged += _metadataHandler;
                }
                tab.ColumnsChanged += OnColumnsChanged;
                _identityPanel = tab.Panel;
                if (_viewEventsAttached) _identityPanel.PropertyChanged += OnPanelIdentityChanged;
                _columns = tab.Columns;
            }
            else if (_metadataSource is not null)
            {
                _metadataSource.ValuesChanged -= _metadataHandler;
                _metadataSource = null;
            }
            _widthOverrides.Clear();
            ClearTextCache();
            _cachedStore = _listing?.Store;
            _topRow = 0;
            _horizontalOffset = 0;
            _refreshTopRow = null;
            EnsureFocusVisible();
            RefreshGitStatuses();
            _automationPeer?.QueueUpdate(ListingChange.Reset);
            InvalidateMeasure();
            InvalidateVisual();
        }
        else if (change.Property == IsActivePanelProperty)
        {
            _automationPeer?.QueueUpdate(ListingChange.State);
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
            // Resizing the panel changes its row capacity, not the user's chosen scroll position.
            ClampTop();
            UpdateScrollBar();
            _automationPeer?.QueueUpdate(ListingChange.Rows);
        }
    }

    private void OnPanelIdentityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PanelViewModel.Number) or nameof(PanelViewModel.IsActive) or nameof(PanelViewModel.IsTarget))
            _automationPeer?.QueueUpdate(ListingChange.State);
    }

    private void OnColumnsChanged(object? sender, EventArgs e)
    {
        if (Tab is null) return;
        _columns = Tab.Columns;
        _horizontalOffset = 0;
        _widthOverrides.Clear();
        ClearTextCache();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnPaletteTick() => InvalidateVisual();

    private void OnThemeChanged()
    {
        ClearTextCache();
        ResolveBrushes();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewEventsAttached = true;
        if (_metadataSource is not null) _metadataSource.ValuesChanged += _metadataHandler;
        if (_identityPanel is not null) _identityPanel.PropertyChanged += OnPanelIdentityChanged;
        _focusWindow = TopLevel.GetTopLevel(this) as Window;
        if (_focusWindow is not null) _focusWindow.Deactivated += OnWindowDeactivated;
        // Static events: subscribed while shown, so a closed list is not kept alive by them.
        ThemeManager.ThemeChanged += OnThemeChanged;
        ThemeManager.PaletteTick += OnPaletteTick;
        Formatters.DateFormatChanged += OnDateFormatChanged;
        UpdateDateScale(); // the format may have changed while this list was not shown
        ResolveBrushes();
        RefreshGitStatuses();
        _automationPeer?.QueueUpdate(ListingChange.Rows | ListingChange.Focus | ListingChange.Marks);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _viewEventsAttached = false;
        if (_metadataSource is not null) _metadataSource.ValuesChanged -= _metadataHandler;
        if (_identityPanel is not null) _identityPanel.PropertyChanged -= OnPanelIdentityChanged;
        if (_focusWindow is not null) _focusWindow.Deactivated -= OnWindowDeactivated;
        _focusWindow = null;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        ThemeManager.PaletteTick -= OnPaletteTick;
        Formatters.DateFormatChanged -= OnDateFormatChanged;
        _gitStatusGeneration++;
        _gitStatusCancel?.Cancel();
        _gitStatusCancel = null;
        _gitStatuses = null;
        _gitStatusFolder = null;
        _loadingHintTimer?.Stop();
        ResetSelectionGesture();
        FinishRename(null, restoreFocus: false);
        _automationPeer?.QueueUpdate(ListingChange.Rows | ListingChange.Focus | ListingChange.Marks);
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
        _widthScale = Math.Max(1, SampleWidth(_typeface, size) / ReferenceSampleWidth);
        UpdateDateScale();
        ClearTextCache();
        InvalidateArrange();
    }

    private void UpdateDateScale()
    {
        double size = FontSize > 0 ? FontSize : 13;
        // Two-digit months, days, and hours, afternoon and morning (AM is the wider designator), with seconds.
        double widest = Math.Max(TextWidth(Formatters.Date(new DateTime(2026, 12, 28, 22, 58, 58)), _typeface, size),
                                 TextWidth(Formatters.Date(new DateTime(2026, 12, 28, 10, 58, 58)), _typeface, size));
        _dateScale = _widthScale * Math.Max(1, widest / TextWidth("28.09.2026 19:39", _typeface, size));
    }

    private void OnDateFormatChanged()
    {
        // The format is a process-wide setting: whoever changes it, the list redraws on its own thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnDateFormatChanged);
            return;
        }
        UpdateDateScale();
        ClearTextCache();
        InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>What a date and a size take in a font: the widest cells the fixed columns hold.</summary>
    private static double SampleWidth(Typeface typeface, double size) => TextWidth("28.09.2026 19:39 12 345 678", typeface, size);

    private static double TextWidth(string text, Typeface typeface, double size) =>
        Math.Max(1, new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black).Width);

    /// <summary>Where the focused row is drawn (in this control), or null when it is scrolled out of view.</summary>
    public Rect? FocusedRowBounds()
    {
        if (_listing is null || _listing.FocusedIndex < 0) return null;
        int row = _listing.FocusedIndex - _topRow;
        if (row < 0 || row >= VisibleRowCapacity) return null;
        return new Rect(0, _headerHeight + row * _rowHeight, _horizontalViewport, _rowHeight);
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
        _dim = B("FcActionableDimText", _text);
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
        if ((change & ListingChange.Reset) != 0 ||
            _anchorStore is not null && !ReferenceEquals(_anchorStore, _listing!.Store))
            ResetSelectionGesture();
        else if (_anchorStoreIndex >= 0 && _listing!.GetVisibleIndex(_anchorStoreIndex) < 0)
        {
            // Once filtered/deleted, the old anchor must not come back when that row is revealed again.
            // A subsequent Shift-click starts from the current visible focus instead.
            _anchorStore = null;
            _anchorStoreIndex = -1;
        }
        if (IsRenaming)
        {
            if ((change & ListingChange.Reset) != 0 || _listing!.GetVisibleIndex(_renameStoreIndex) < 0)
                FinishRename(null, restoreFocus: false);
            else InvalidateArrange();
        }
        if ((change & ListingChange.Reset) != 0)
        {
            RefreshGitStatuses();
            ClearTextCache();
            _topRow = 0;
            _refreshTopRow = null;
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
        if ((change & ListingChange.RefreshRows) != 0)
        {
            _refreshTopRow ??= _topRow;
            _topRow = Math.Min(_refreshTopRow.Value, Math.Max(0, _listing!.VisibleCount - VisibleRowCapacity));
        }
        else if ((change & (ListingChange.Focus | ListingChange.Reset)) != 0)
        {
            EnsureFocusVisible();
            if (_refreshTopRow.HasValue) _refreshTopRow = _topRow;
        }
        if ((change & ListingChange.RefreshComplete) != 0) _refreshTopRow = null;
        bool hadVerticalBar = _vbar.IsVisible;
        UpdateScrollBar();
        if (hadVerticalBar != _vbar.IsVisible) InvalidateArrange();
        _automationPeer?.QueueUpdate(change);
        InvalidateVisual();
    }

    private void EnsureCacheStore()
    {
        if (_listing is not null && !ReferenceEquals(_listing.Store, _cachedStore))
        {
            ClearTextCache();
            ResetSelectionGesture();
            _cachedStore = _listing.Store;
        }
    }

    internal void RefreshGitStatuses()
    {
        _gitStatusGeneration++;
        _gitStatusCancel?.Cancel();
        if (VisualRoot is null || Tab?.Location is not { IsFileSystem: true } location)
        {
            _gitStatuses = null;
            _gitStatusFolder = null;
            return;
        }
        // A focus return or filesystem notification can refresh the same folder repeatedly. Keep the
        // existing badges on screen until the replacement snapshot is ready; only navigation clears them.
        if (!string.Equals(_gitStatusFolder, location.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            _gitStatuses = null;
        _gitStatusFolder = location.Path;
        var cancellation = new CancellationTokenSource();
        _gitStatusCancel = cancellation;
        int generation = _gitStatusGeneration;
        string folder = location.Path;
        _ = Task.Run(async () =>
        {
            try
            {
                // Coalesce rapid navigation and filesystem notifications before launching Git.
                await Task.Delay(250, cancellation.Token);
                var snapshot = await GitStatusReader.ReadAsync(folder, cancellation.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation != _gitStatusGeneration || Tab?.Location?.Path != folder) return;
                    _gitStatuses = snapshot;
                    InvalidateVisual();
                    _automationPeer?.QueueUpdate(ListingChange.State);
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { FileCat.Core.Diagnostics.AppLog.Warn("Git icon status: " + ex.Message); }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(_gitStatusCancel, cancellation)) _gitStatusCancel = null;
                    cancellation.Dispose();
                }, DispatcherPriority.Background);
            }
        });
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
        _automationPeer?.QueueUpdate(ListingChange.Rows);
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
        _updatingScrollBar = true;
        try
        {
            _vbar.Maximum = Math.Max(0, count - cap);
            _vbar.ViewportSize = cap;
            _vbar.SmallChange = 1;
            _vbar.LargeChange = Math.Max(1, cap - 1);
            _vbar.Value = _topRow;
            _vbar.IsVisible = count > cap;
        }
        finally { _updatingScrollBar = false; }
    }

    internal void ScrollToHorizontalOffset(double offset)
    {
        double next = Math.Clamp(offset, 0, Math.Max(0, _contentExtent - _horizontalViewport));
        if (Math.Abs(next - _horizontalOffset) < 0.01) return;
        _horizontalOffset = next;
        _updatingHorizontalScrollBar = true;
        try { _hbar.Value = next; }
        finally { _updatingHorizontalScrollBar = false; }
        if (IsRenaming) InvalidateArrange();
        _automationPeer?.QueueUpdate(ListingChange.Rows);
        InvalidateVisual();
    }

    private void UpdateHorizontalScrollBar()
    {
        _updatingHorizontalScrollBar = true;
        try
        {
            double maximum = Math.Max(0, _contentExtent - _horizontalViewport);
            _horizontalOffset = Math.Clamp(_horizontalOffset, 0, maximum);
            _hbar.Maximum = maximum;
            _hbar.ViewportSize = _horizontalViewport;
            _hbar.SmallChange = 48;
            _hbar.LargeChange = Math.Max(48, _horizontalViewport * 0.8);
            _hbar.Value = _horizontalOffset;
        }
        finally { _updatingHorizontalScrollBar = false; }
        _automationPeer?.QueueUpdate(ListingChange.Rows);
    }

    // ---- Layout -----------------------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        _vbar.Measure(availableSize);
        _hbar.Measure(availableSize);
        if (IsRenaming) _renameEditor.Measure(availableSize);
        if (_renameErrorBox.IsVisible) _renameErrorBox.Measure(availableSize);
        double w = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double sbw = _vbar.DesiredSize.Width > 0 ? _vbar.DesiredSize.Width : 12;
        double hbh = HorizontalBarHeight;
        _layoutHeight = finalSize.Height;
        for (int i = 0; i < 4; i++)
        {
            bool wasVertical = _vbar.IsVisible, wasHorizontal = _hbar.IsVisible;
            _horizontalViewport = Math.Max(0, finalSize.Width - (wasVertical ? sbw : 0));
            LayoutColumns(_horizontalViewport);
            _hbar.IsVisible = _contentExtent > _horizontalViewport + 0.5;
            ClampTop();
            UpdateScrollBar();
            if (wasVertical == _vbar.IsVisible && wasHorizontal == _hbar.IsVisible) break;
        }
        _horizontalViewport = Math.Max(0, finalSize.Width - (_vbar.IsVisible ? sbw : 0));
        LayoutColumns(_horizontalViewport);
        _hbar.IsVisible = _contentExtent > _horizontalViewport + 0.5;
        ClampTop();
        UpdateScrollBar();
        UpdateHorizontalScrollBar();
        _vbar.Arrange(new Rect(finalSize.Width - sbw, _headerHeight, sbw,
            Math.Max(0, finalSize.Height - _headerHeight - (_hbar.IsVisible ? hbh : 0))));
        _hbar.Arrange(new Rect(0, Math.Max(_headerHeight, finalSize.Height - hbh), _horizontalViewport, hbh));
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
            double w = _widthOverrides.TryGetValue(i, out var ow) ? ow : c.Star ? c.Width : c.Width * ScaleOf(c);
            if (c.Star && !_widthOverrides.ContainsKey(i)) starSum += w;
            else fixedSum += w;
        }
        double minimumStarSpace = _columns.Where(c => c.Star).Sum(MinimumStarWidth);
        double starSpace = Math.Max(minimumStarSpace, totalWidth - fixedSum);
        double x = 0;
        for (int i = 0; i < n; i++)
        {
            var c = _columns[i];
            double w = _widthOverrides.TryGetValue(i, out var ow) ? ow
                : c.Star ? Math.Max(MinimumStarWidth(c),
                    starSpace * (c.Width / Math.Max(1, starSum))) : c.Width * ScaleOf(c);
            _columnX[i] = x;
            _columnW[i] = Math.Max(24, w);
            x += _columnW[i];
        }
        _contentExtent = x;
    }

    // ---- Rendering ----------------------------------------------------------------------------------------------

    public override void Render(DrawingContext dc)
    {
        long started = RenderTimed is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        long allocated = started == 0 ? 0 : GC.GetAllocatedBytesForCurrentThread();
        RenderCore(dc);
        if (started != 0) RenderTimed?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private bool _inCloudRoot;

    private void RenderCore(DrawingContext dc)
    {
        // Icons are made for the pixels this display gives a 16-point icon (crisp at 125%, 150%, 200%).
        Tab?.Services.Icons.SetScale(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var bounds = new Rect(Bounds.Size);
        bool active = IsActivePanel;
        dc.FillRectangle(active ? _bg : _bgInactive, bounds);
        double contentWidth = _horizontalViewport > 0 ? _horizontalViewport : bounds.Width - (_vbar.IsVisible ? _vbar.Bounds.Width : 0);
        // New columns (another tab or location) can reach a frame before the next arrange lays them out.
        if (_columnW.Length != _columns.Length) LayoutColumns(contentWidth);
        RenderHeader(dc, contentWidth, active);

        var listing = _listing;
        if (listing is null) return;
        EnsureCacheStore();
        // In a cloud provider's folder (OneDrive, Dropbox, …) a file with no cloud attribute is on this computer.
        _inCloudRoot = Tab is { Location: { IsFileSystem: true } here } shown
            && Core.FileSystem.CloudFiles.RootOf(here.Path, shown.Services.Shell.CloudSyncRoots) is not null;
        int count = listing.VisibleCount;
        using (dc.PushClip(new Rect(0, _headerHeight, contentWidth,
                   Math.Max(0, bounds.Height - _headerHeight - (_hbar.IsVisible ? HorizontalBarHeight : 0)))))
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
        using var clip = dc.PushClip(new Rect(0, 0, width, _headerHeight));
        using var transform = dc.PushTransform(Matrix.CreateTranslation(-_horizontalOffset, 0));
        for (int i = 0; i < _columns.Length; i++)
        {
            if (_columnX[i] + _columnW[i] < _horizontalOffset || _columnX[i] > _horizontalOffset + width) continue;
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

        using (dc.PushTransform(Matrix.CreateTranslation(-_horizontalOffset, 0)))
        for (int i = 0; i < _columns.Length; i++)
        {
            var c = _columns[i];
            double colX = _columnX[i];
            double colW = _columnW[i];
            if (colX + colW < _horizontalOffset || colX > _horizontalOffset + width) continue;
            double textX = colX + Padding;
            double avail = colW - 2 * Padding;
            if (i == 0)
            {
                double iconX = colX + MarkGutter;
                var icons = Tab?.Services.Icons;
                GitStatusKind git = _gitStatuses?.ForName(e.Name) ?? GitStatusKind.None;
                // A Git state is FileCat's own mark: an installed overlay (TortoiseGit's) drawn at 16 pixels hardly reads.
                var shellOverlay = _gitStatuses is null || git != GitStatusKind.None ? null : icons?.ShellOverlayIcon(e, Tab!.Location, git);
                var icon = shellOverlay ?? icons?.GetIcon(e, Tab!.Location);
                if (icon is not null)
                {
                    var iconRect = new Rect(iconX, y + (_rowHeight - IconSize) / 2, IconSize, IconSize);
                    bool dimIcon = e.Has(EntryFlags.Hidden) || e.Has(EntryFlags.System) || e.Has(EntryFlags.Unavailable);
                    using (dimIcon ? dc.PushOpacity(0.5) : default(DrawingContext.PushedState?))
                    {
                        dc.DrawImage(icon, iconRect);
                    }
                    if (shellOverlay is null && (e.Has(EntryFlags.Link) || IconProvider.IsShortcut(e))) dc.DrawImage(icons!.LinkOverlay, iconRect);
                    if (shellOverlay is null && e.Kind is EntryKind.File or EntryKind.Directory && icons?.GitOverlay(git) is { } gitOverlay)
                        dc.DrawImage(gitOverlay, iconRect);
                    // What checking it against the checksums and signatures beside it found (D-57): only in folders that have them.
                    if (e.Kind == EntryKind.File && Tab is { HasSidecars: true } checking && checking.Verification(e, storeIndex) is { } verified
                        && icons?.VerificationOverlay(verified.State) is { } verifiedOverlay)
                        dc.DrawImage(verifiedOverlay, iconRect);
                    // A cloud provider's file: only in the cloud, on this computer, or kept here always (from its attributes).
                    if (e.Kind is EntryKind.File or EntryKind.Directory
                        && icons?.CloudOverlay(Core.FileSystem.CloudFiles.StateOf((FileAttributes)e.Attributes, _inCloudRoot)) is { } cloudOverlay)
                        dc.DrawImage(cloudOverlay, iconRect);
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
                var s = CellString(e, c);
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
            if (_showLoadingHint) message = (listing.LoadingProgress ?? "Loading…") + "  (Esc stops)";
        }
        else if (realRows <= 0 && listing.State == ListingState.Complete)
        {
            message = listing.Filter is not null && listing.TotalCount > 0
                ? $"No items match the filter \"{listing.Filter.Text}\".\nEsc clears the filter."
                : Core.Search.ResultSetProvider.IsWorkingSetList(listing.Location)
                    ? "No working sets yet.\nF7 creates one; Ctrl+Shift+W adds marked items to a set."
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

    internal string CellString(in EntryData e, ColumnSpec column) => column.Field switch
    {
        ColumnField.Name => DisplayName(e),
        ColumnField.Extension => e.IsContainer ? string.Empty : Formatters.SafeName(NameParts.GetExtension(e.Name)),
        ColumnField.Size => Formatters.SizeCell(e),
        ColumnField.Modified => e.Kind == EntryKind.Parent ? string.Empty : Formatters.Date(e, column.Seconds),
        ColumnField.Created => e.Kind == EntryKind.Parent ? string.Empty : Formatters.Date(e.Created),
        ColumnField.Attributes => Formatters.Attributes(e),
        ColumnField.Folder => Formatters.SafeName(Tab?.GetFolderText(e) ?? string.Empty),
        ColumnField.Kind => Formatters.SafeName(Tab?.GetKindText(e) ?? string.Empty),
        ColumnField.Details => Formatters.SafeName(Tab?.GetDetailsText(e) ?? string.Empty),
        _ => string.Empty,
    };

    private string DisplayName(in EntryData e)
    {
        if (e.Kind == EntryKind.RegistryValue && e.Name.Length == 0) return "(Default)";
        if (e.Kind == EntryKind.Drive && e.Tag is Core.FileSystem.DriveTag t)
            return Formatters.SafeName(string.IsNullOrEmpty(t.Label) ? $"{e.Name}  {t.DriveType}" : $"{e.Name}  {t.Label}");
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
        if (!shift && !other && _hbar.IsVisible && e.Key is Key.Left or Key.Right)
        {
            ScrollToHorizontalOffset(_horizontalOffset + (e.Key == Key.Right ? 64 : -64));
            e.Handled = true;
            return;
        }
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

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        // KeyUp may go to another control/window; never continue its mark/unmark intent on return.
        EndInputGesture();
        _automationPeer?.QueueUpdate(ListingChange.Focus);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => EndInputGesture();

    private void EndInputGesture()
    {
        // Some platforms retain the window's focused control while another native window is active.
        _extendState = null;
        _dragStart = null;
        _pressArgs = null;
        _pressedRow = -1;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _automationPeer?.QueueUpdate(ListingChange.Focus);
    }

    private void ResetSelectionGesture()
    {
        _anchorStore = null;
        _anchorStoreIndex = -1;
        _extendState = null;
        _dragStart = null;
        _pressArgs = null;
        _pressedRow = -1;
    }

    private int ResolveRangeAnchor()
    {
        if (_listing is null) return -1;
        int row = ReferenceEquals(_anchorStore, _listing.Store)
            ? _listing.GetVisibleIndex(_anchorStoreIndex) : -1;
        if (row >= 0) return row;
        // Capture the fallback identity once so repeated Shift-clicks extend from the same item.
        row = _listing.FocusedIndex;
        if (row >= 0) SetRangeAnchor(row);
        return row;
    }

    private void SetRangeAnchor(int row)
    {
        _anchorStore = _listing!.Store;
        _anchorStoreIndex = _listing.GetStoreIndex(row);
    }

    // ---- Pointer --------------------------------------------------------------------------------------------------

    private int RowAt(Point p)
    {
        if (p.Y < _headerHeight || p.Y >= Bounds.Height - (_hbar.IsVisible ? HorizontalBarHeight : 0) ||
            p.X < 0 || p.X >= _horizontalViewport) return -1;
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
        if (p.Y >= _headerHeight || p.X < 0 || p.X >= _horizontalViewport) return -1;
        double x = p.X + _horizontalOffset;
        for (int i = 0; i < _columns.Length; i++)
        {
            double right = _columnX[i] + _columnW[i];
            if (Math.Abs(x - right) > ResizeGrip) continue;
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
        if (p.X < 0 || p.X >= _horizontalViewport) return -1;
        double x = p.X + _horizontalOffset;
        for (int i = 0; i < _columns.Length; i++)
        {
            if (x >= _columnX[i] && x < _columnX[i] + _columnW[i]) return i;
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
        _extendState = null; // A pointer gesture does not inherit a keyboard range's mark/unmark choice.

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
            if (col >= 0 && point.Properties.IsLeftButtonPressed && _columns[col] is { Field: ColumnField.Metadata or ColumnField.Folder, MetadataId: { } mid }) Tab?.SortByMetadata(mid);
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
            if (!_listing.IsVisibleMarked(row))
            {
                _listing.UnmarkEverything();
                _listing.SetFocus(row);
            }
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
            int from = ResolveRangeAnchor();
            if (from < 0) from = row;
            _listing.SetMarkRange(from, row, true);
            _listing.SetFocus(row);
        }
        else
        {
            _listing.SetFocus(row);
            SetRangeAnchor(row);
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
        UpdateTip(pos);
        if (_dragStart is { } start && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(pos.X - start.X) > 6 || Math.Abs(pos.Y - start.Y) > 6)
            {
                _dragStart = null;
                if (_pressedRow >= 0 && _pressArgs is { } press) DragRequested?.Invoke(this, press);
            }
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        UpdateTip(null);
    }

    private string? _tip;

    /// <summary>
    /// Over a file's icon or its Verified cell: what checking it against the checksums and signatures beside it found,
    /// claim by claim (D-57). Nothing elsewhere, so the list does not throw tips at a passing pointer.
    /// </summary>
    private void UpdateTip(Point? pos)
    {
        string? tip = null;
        if (pos is { } p && _listing is { } listing && RowAt(p) is var row and >= 0 && ColumnAt(p) is var col and >= 0)
        {
            bool overIcon = col == 0 && p.X + _horizontalOffset >= _columnX[0] + MarkGutter &&
                p.X + _horizontalOffset < _columnX[0] + MarkGutter + IconSize + 2;
            bool overCell = _columns[col] is { Field: ColumnField.Metadata, MetadataId: "verified" };
            var entry = listing.GetVisible(row);
            if ((overIcon || overCell) && entry.Kind == EntryKind.File && Tab is { HasSidecars: true } tab)
                tip = tab.VerificationTip(entry, listing.GetStoreIndex(row));
            if (tip is null && _columns[col].Field is ColumnField.Name or ColumnField.Extension)
                tip = IdentityTip(entry);
        }
        if (tip == _tip) return;
        _tip = tip;
        ToolTip.SetTip(this, tip);
    }

    internal static string? IdentityTip(in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return null;
        string safe = Formatters.SafeName(entry.Name);
        if (safe == entry.Name) return null;
        return $"Full name: {safe}\n{Formatters.DescribeHiddenNameCharacters(entry.Name)}";
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
            // Stored at the default font's scale (the width the user sees is the stored one times the font's).
            if (_widthOverrides.TryGetValue(column, out var width) && column < _columns.Length)
                Tab?.SetColumnWidth(column, width / ScaleOf(_columns[column]));
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_hbar.IsVisible && (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) || (e.KeyModifiers & KeyModifiers.Shift) != 0))
        {
            double delta = Math.Abs(e.Delta.X) > 0 ? e.Delta.X : e.Delta.Y;
            ScrollToHorizontalOffset(_horizontalOffset - delta * 48);
            e.Handled = true;
            return;
        }
        _topRow -= (int)Math.Round(e.Delta.Y * 3);
        ClampTop();
        if (_refreshTopRow.HasValue) _refreshTopRow = _topRow;
        UpdateScrollBar();
        if (IsRenaming) InvalidateArrange();
        _automationPeer?.QueueUpdate(ListingChange.Rows);
        InvalidateVisual();
        e.Handled = true;
    }

    public Rect GetRowBounds(int row) => new(0, _headerHeight + (row - _topRow) * _rowHeight, Bounds.Width, _rowHeight);

    private FileListAutomationPeer? _automationPeer;

    protected override AutomationPeer OnCreateAutomationPeer() => _automationPeer = new FileListAutomationPeer(this);

    internal ListingModel? AutomationListing => _viewEventsAttached && _listing is { IsDisposed: false } ? _listing : null;
    internal int TopRow => _topRow;

    internal Rect VisibleRowBounds(int row)
    {
        if (AutomationListing is not { } listing || (uint)row >= (uint)listing.VisibleCount) return default;
        var viewport = new Rect(0, _headerHeight, _horizontalViewport,
            Math.Max(0, Bounds.Height - _headerHeight - (_hbar.IsVisible ? HorizontalBarHeight : 0)));
        return GetRowBounds(row).Intersect(viewport);
    }

    internal void ScrollToRow(int row)
    {
        if (AutomationListing is not { } listing || (uint)row >= (uint)listing.VisibleCount)
            throw new InvalidOperationException("This item is no longer visible. Review marked items to inspect hidden marks.");
        int cap = VisibleRowCapacity;
        if (row < _topRow) _topRow = row;
        else if (row >= _topRow + cap) _topRow = row - cap + 1;
        ScrollToTopRow(_topRow);
    }

    internal void ScrollToTopRow(int row)
    {
        _topRow = row;
        ClampTop();
        if (_refreshTopRow.HasValue) _refreshTopRow = _topRow;
        UpdateScrollBar();
        if (IsRenaming) InvalidateArrange();
        InvalidateVisual();
        _automationPeer?.QueueUpdate(ListingChange.Rows);
    }

    internal void FocusAutomationItem(int row, bool invoke = false)
    {
        if (IsRenaming) throw new InvalidOperationException("Finish renaming before opening another item.");
        var listing = AutomationListing;
        if (listing is null || (uint)row >= (uint)listing.VisibleCount)
            throw new InvalidOperationException("This item is no longer visible. Review marked items to inspect hidden marks.");
        var store = listing.Store;
        int item = listing.GetStoreIndex(row);
        var tab = Tab;
        void CheckOwner()
        {
            if (!ReferenceEquals(listing, AutomationListing) || !ReferenceEquals(store, listing.Store) || !ReferenceEquals(tab, Tab))
                throw new InvalidOperationException("The listing changed while activating this item. Review the current items again.");
        }
        ScrollToRow(row);
        ActivateRequested?.Invoke(this, EventArgs.Empty);
        CheckOwner();
        Focus();
        CheckOwner();
        int currentRow = listing.GetVisibleIndex(item);
        if (currentRow < 0) throw new InvalidOperationException("This item became hidden while activating it. Review marked items to inspect hidden marks.");
        listing.SetFocus(currentRow);
        CheckOwner();
        tab?.OnUserMovedFocus();
        CheckOwner();
        if (invoke)
        {
            if (listing.FocusedStoreIndex != item) throw new InvalidOperationException("Focus changed while activating this item. Review the current focus before opening it.");
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Right border of a laid-out column (tests and automation).</summary>
    internal double ColumnRightEdge(int column) => _columnX[column] + _columnW[column] - _horizontalOffset;
    internal double ColumnLeftEdge(int column) => _columnX[column] - _horizontalOffset;

    internal string DescribeFocus()
    {
        if (AutomationListing is not { } listing) return "No location loaded";
        if (!listing.TryGetFocused(out _)) return DescribeListingState();
        return DescribeItem(listing.FocusedStoreIndex);
    }

    internal string DescribeListName()
    {
        if (Tab is not { } tab) return "File list, no location loaded";
        var panel = tab.Panel;
        var role = panel.IsActive ? "source" : panel.IsTarget ? "target" : "panel";
        return $"Panel {panel.Number}, {role}, {Formatters.SafeName(tab.DisplayPath)}";
    }

    internal string DescribeListingState()
    {
        if (AutomationListing is not { } listing) return "No location loaded";
        if (listing.State == ListingState.Failed) return "Not available. " + Formatters.SafeName(listing.Error ?? "The location could not be read.") + " Use Refresh to try again or edit Location.";
        if (listing.State == ListingState.Loading) return "Loading. " + Formatters.SafeName(listing.LoadingProgress ?? "Items are still arriving.") + " Escape stops listing.";
        if (listing.IsPartial) return "Listing incomplete. " + Formatters.SafeName(listing.Issues.LastOrDefault() ?? "Only part of this location is shown.") + " Use Refresh to try again.";
        if (listing.IsRefreshing) return "Refreshing. " + Formatters.SafeName(listing.LoadingProgress ?? "Current items remain available.");
        if (listing.LastRefreshError is { } error) return "Refresh failed. " + Formatters.SafeName(ListingModel.DescribeError(error)) + " Previously listed items are shown. Use Refresh to try again.";
        if (listing.VisibleCount <= (listing.HasParentRow ? 1 : 0))
            return listing.Filter is not null ? "No items match the filter. Clear the filter to show other items."
                : !listing.ShowHidden && listing.TotalCount > 0 ? "All items are hidden. Enable Show hidden items to display them."
                : "Empty location.";
        return $"{listing.VisibleCount:N0} visible rows, {listing.TotalCount:N0} items.";
    }

    internal string DescribeListHelp()
    {
        if (AutomationListing is not { } listing) return DescribeListingState();
        // Tab status already maintains and caches complete mark statistics; this read performs no provider I/O.
        var marks = listing.GetMarkStats();
        string selection = marks.Count == 0 ? "No marked items. Commands use the focused item."
            : $"{marks.Count:N0} marked items, {marks.HiddenByFilter:N0} hidden by filtering or Show hidden items.";
        string filter = listing.Filter is { } mask ? " Filter: " + Formatters.SafeName(mask.Text) + "." : " No filter.";
        return DescribeListingState() + " " + selection + filter + $" Sort: {listing.Sort.Field}, {(listing.Sort.Descending ? "descending" : "ascending")}. " +
            "The Review marked items command opens the complete selection, including hidden and offscreen marks. " +
            "Up and Down move focus; Shift with movement marks or unmarks; Insert toggles the focused mark." +
            (_hbar.IsVisible ? " Left and Right scroll the columns; Shift with the wheel also scrolls columns." : string.Empty);
    }

    internal static string ItemKind(in EntryData e) => e.Kind switch
    {
        EntryKind.Parent => "parent navigation",
        EntryKind.Directory => "folder",
        EntryKind.Drive => "drive",
        EntryKind.Server => "server",
        EntryKind.Share => "share",
        EntryKind.RegistryKey => "Registry key",
        EntryKind.RegistryValue => "Registry value",
        _ => e.Tag is IDisplayDetails details && details.KindText.Length > 0 ? Formatters.SafeName(details.KindText) : "file",
    };

    internal static string ItemName(in EntryData e) => e.Kind == EntryKind.Parent ? "Go to parent location" :
        e.Kind == EntryKind.RegistryValue && e.Name.Length == 0 ? "(Default)" : Formatters.SafeName(e.Name);

    /// <summary>Cached original identity for result sets; never resolves an item or asks a provider to read it.</summary>
    internal static string ItemLocation(TabViewModel tab, in EntryData e)
    {
        if (e.Tag is not ResultTag result) return Formatters.SafeName(tab.DisplayPath);
        var parent = result.Parent;
        string location = Formatters.SafeName(result.Folder is { Length: > 0 } folder ? folder :
            parent.IsFileSystem ? parent.Path : parent.ToString());
        // ToString omits session qualifiers. Equal paths can belong to distinct remote profiles,
        // Registry views or nested archive owners; retaining these qualifiers prevents false identity.
        for (var owner = parent; owner is not null; owner = owner.Container)
            if (owner.Session is { Length: > 0 } session)
                location += $" ({Formatters.SafeName(owner.Scheme)} view: {Formatters.SafeName(session)})";
        if (result.Ordinal > 0) location += $", archive occurrence {(long)result.Ordinal + 1:N0}";
        return location;
    }

    internal static string ItemWarnings(in EntryData e)
    {
        string details = string.Empty;
        if (Formatters.DescribeHiddenNameCharacters(e.Name) is { } hidden) details += ", " + hidden;
        if (e.Has(EntryFlags.Offline)) details += ", cloud placeholder or offline; opening may download content";
        if (e.Has(EntryFlags.Unavailable)) details += ", unavailable";
        if (e.Has(EntryFlags.Protected)) details += ", protected content";
        if (e.Has(EntryFlags.Link)) details += ", link";
        if (e.Tag is IDisplayDetails display && display.DetailsText.Length > 0) details += ", " + Formatters.SafeName(display.DetailsText);
        return details;
    }

    internal string DescribeItem(int storeIndex)
    {
        if (AutomationListing is not { } listing || (uint)storeIndex >= (uint)listing.Store.Count) return "Item no longer available";
        var e = listing.Store[storeIndex];
        int row = listing.GetVisibleIndex(storeIndex);
        var kind = ItemKind(e);
        var details = e.IsContainer ? string.Empty : ", " + Formatters.SizeWithUnit(e.Size);
        if (e.Modified > 0 && e.Kind != EntryKind.Parent) details += ", modified " + Formatters.Date(e);
        var marked = listing.IsMarked(storeIndex) ? ", marked" : ", not marked";
        var git = _gitStatuses?.ForName(e.Name) switch
        {
            GitStatusKind.Clean => ", Git clean",
            GitStatusKind.Untracked => ", Git untracked",
            GitStatusKind.Added => ", Git added",
            GitStatusKind.Modified => ", Git changed",
            GitStatusKind.Conflict => ", Git conflict",
            _ => string.Empty,
        };
        details += ItemWarnings(e);
        if (Tab is { } owner && e.Tag is ResultTag) details += ", location: " + ItemLocation(owner, e);
        // Inspection may only read cached verification; asking an offscreen peer must not start file reads.
        if (Tab is { HasSidecars: true, Location.IsFileSystem: true } tab && e.Kind == EntryKind.File &&
            tab.Services.Metadata.Peek("verified", Path.Join(tab.Location.Path, e.Name), e).Value is Core.Verification.VerificationResult verification && verification.Text.Length > 0)
            details += ", verification: " + Formatters.SafeName(verification.Text);
        string position = row < 0 ? "hidden by filtering or Show hidden items" : $"{row + 1} of {listing.VisibleCount}";
        return $"{ItemName(e)}, {kind}{details}{marked}{git}, {position}";
    }
}
