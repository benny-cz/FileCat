using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Content;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.Views;

/// <summary>
/// F3 viewer in its own top-level window (plan §4.1, §16.1): one viewer with text and hex modes (F4 toggles),
/// encoding chosen with visible evidence (F8 cycles), search (Ctrl+F, F3/Shift+F3), go to (Ctrl+G), range
/// checksums, and follow mode for growing logs. Pictures open in a picture mode (Ctrl+P returns to it; Z switches between
/// fitted and actual size), decoded in a separate worker process (<see cref="PictureDecoder"/>). Opened with full
/// sharing, so other programs keep working.
/// </summary>
public sealed class ViewerWindow : Window
{
    // Screen readers follow a list's focused row and, on Linux, hear focus after window activation.
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new FileCat.App.Controls.DescendantFocusWindowAutomationPeer(this);

    private readonly AppServices _services;
    private readonly string _deviceKey;
    private PagedReader _reader;
    private readonly Func<CancellationToken, Task<IContentSource?>>? _reopen;
    private readonly TextViewer _text = new();
    private readonly Panel _dialogHost = new() { IsVisible = false };
    private readonly OverlayDialogService _dialogs;
    private readonly HexView _hex = new();
    private readonly TextBlock _status = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _encodingInfo = new() { Classes = { "small", "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { PlaceholderText = "Find (Ctrl+F)", MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = "Aa", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _hexSearch = new() { Content = "Hex bytes", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previousMatch = new() { Content = "Previous" };
    private readonly Button _nextMatch = new() { Content = "Next" };
    private readonly TextBlock _matchCount = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _stopSearch = new() { Content = "Stop search", IsVisible = false };
    private readonly Button _stopCount = new() { Content = "Stop count", IsVisible = false };
    private readonly Button _refresh = new() { Content = "Refresh" };
    private readonly Border _staleBanner = new() { IsVisible = false, Padding = new Thickness(8, 4), Classes = { "banner" } };
    private readonly TextBlock _staleText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private string? _refreshFailure;
    private bool _refreshing;
    private CancellationTokenSource? _refreshCts;
    private readonly ComboBox _encodingBox = new() { MinWidth = 130 };
    private readonly ToggleButton _modeText = new() { Content = "Text" };
    private readonly ToggleButton _modeHex = new() { Content = "Hex" };
    private readonly ToggleButton _modeInfo = new() { Content = "Info" };
    private readonly ToggleButton _modePicture = new() { Content = "Picture", IsVisible = false };
    /// <summary>A web page, drawn by the system's browser engine with scripts and the web off (D-51).</summary>
    private readonly ToggleButton _modePage = new() { Content = "Page", IsVisible = false };
    private readonly PageView _pageView;
    private HtmlPage? _htmlPage;
    private bool _isPage;
    private string? _pageSearchStatus;
    /// <summary>Why pages cannot be drawn here, once that is known.</summary>
    private string? _pageUnavailable;
    private readonly ToggleButton _fitPicture = new() { Content = "Fit", IsVisible = false };
    private readonly ToggleButton _actualSize = new() { Content = "100%", IsVisible = false };
    private readonly TextBlock _pictureZoom = new() { IsVisible = false, Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _modeLabel = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly Button _findToggle = new() { Content = "Find…" };
    private readonly WrapPanel _findBar = new() { IsVisible = false, Margin = new Thickness(8, 0, 8, 4), ItemSpacing = 8, LineSpacing = 4 };
    private bool _findOpen;
    private readonly Button _smallerText = new() { Content = "Text −" };
    private readonly Button _largerText = new() { Content = "Text +" };
    private readonly TextBlock _contentSize = new() { VerticalAlignment = VerticalAlignment.Center, Classes = { "small" } };
    private readonly PictureView _picture = new() { IsVisible = false };
    private bool _isPicture;
    private Task? _pictureLoad;
    // Decoded pictures, structure reports and loaded pages are snapshots. Reader refreshes must not describe them as current.
    private long _sourceGeneration, _pictureGeneration, _infoGeneration, _pageGeneration;
    private readonly List<Control> _textOnly = []; // toolbar parts that mean nothing for a picture

    /// <summary>The longest side a picture is decoded to (actual size beyond it shows the scaled picture, and says so).</summary>
    private const int PictureSide = 4096;
    /// <summary>
    /// The Info report in a text view of its own: long reports (thousands of imports and exports) scroll without laying
    /// out all of their text, and Find searches them like any text.
    /// </summary>
    private readonly TextViewer _info = new() { IsVisible = false, Wrap = false, ReportStyle = true };
    private PagedReader? _infoReader;
    private string _infoText = "";
    private int _infoLineCount;
    private bool _isInfo, _infoLoaded;
    private readonly CheckBox _wrap = new() { Content = "Wrap", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _follow = new() { Content = "Follow end", VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _changeTimer;
    private bool _checkingChanges;
    private readonly string _displayName;
    private EncodingGuess _guess = new(new UTF8Encoding(false), 0, "not yet examined", false);
    private bool _isHex;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _countCts;
    private SearchIdentity? _countIdentity;
    private ContentSearch.MatchCount? _countResult;
    private bool _countFailed;
    private bool _countCanceled;
    private bool _pageFoundMatch;
    private bool _pageTriedFind;
    private bool _pageFindUnavailable;
    private PageFindResult? _pageCountResult;
    private CancellationTokenSource? _checksumCts;
    private readonly Border _checksumPanel = new() { IsVisible = false, Padding = new Thickness(10), Classes = { "banner" } };
    private readonly ComboBox _checksumScope = new() { MinWidth = 230 };
    private readonly TextBox _checksumResult = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 190 };
    private readonly Button _computeChecksum = new() { Content = "Compute" };
    private readonly Button _copyChecksum = new() { Content = "Copy results", IsEnabled = false };
    private readonly Button _stopChecksum = new() { Content = "Stop checksum", IsVisible = false };
    private (long Start, long Length)? _checksumSelectedRange;
    private long _checksumScopeGeneration, _checksumWholeLength;
    private CancellationTokenSource? _copyCts;
    private readonly SemaphoreSlim _copyWrites = new(1, 1);
    private readonly ContentCopyNotice _copyNotice = new();
    private readonly Button _copySource = new() { Content = "Copy visible source text" };
    private readonly Button _copyDisplayed = new() { Content = "Copy displayed text" };
    private long _lastHit = -1;
    private int _lastHitLength;
    private SearchIdentity? _lastSearch;
    private bool _revealingSearch;
    private sealed record SearchIdentity(string Pattern, bool HexBytes, int Encoding, bool MatchCase, PagedReader Reader, bool HexView);
    private LineIndex? _lines;
    private CancellationTokenSource? _lineCts;
    private readonly CancellationTokenSource _closing = new();
    private static readonly List<ViewerWindow> s_open = [];
    private Size _initialWindowSize;
    private Size _lastNormalWindowSize;
    private bool _windowOpened, _userSizedWindow;
    private bool _restoringWindowState;
    private bool _normalSizePending;
    private readonly DispatcherTimer _restoreSizeTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _normalSizeTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _readyForRefresh;

    /// <summary>Open viewer windows, oldest first: the window list in the command palette (plan §4.1).</summary>
    public static IReadOnlyList<ViewerWindow> OpenWindows => s_open;

    /// <summary>The viewed item's full path or provider path.</summary>
    public string DisplayName => _displayName;

    public ViewerWindow(AppServices services, IContentSource source, string displayName, bool hex, string? deviceKey = null)
        : this(services, source, displayName, hex, deviceKey, null, null) { }

    internal ViewerWindow(AppServices services, IContentSource source, string displayName, bool hex, string? deviceKey,
        PagedReader? preparedReader, Func<CancellationToken, Task<IContentSource?>>? reopen = null)
    {
        _services = services;
        ReadabilityPolicy.ApplyDensity(services.Settings.RowDensity);
        Classes.Set("comfortable", ReadabilityPolicy.Comfortable(services.Settings.RowDensity));
        _deviceKey = deviceKey ?? "viewer-content";
        if (deviceKey is null && source.LocalPath is { } path)
        {
            var location = Core.Resources.Location.FileSystem(Path.GetDirectoryName(path) ?? path);
            _deviceKey = services.Providers.For(location).GetDeviceKey(location);
        }
        _displayName = displayName;
        _pageView = new PageView(services.Paths.PageViewDataDirectory) { IsVisible = false };
        Closed += (_, _) => s_open.Remove(this);
        _reader = preparedReader ?? new PagedReader(source, services.Io, _deviceKey);
        _reopen = reopen ?? (source.LocalPath is { } localPath
            ? ct => ViewerLauncher.ReopenSourceAsync(services, _deviceKey, () => new FileContentSource(localPath), ct)
            : null);
        Title = $"{Path.GetFileName(displayName.TrimEnd('\\', '/'))} — FileCat Viewer";
        Size usable = ViewerWorkingArea();
        Size restored = RestoreViewerSize(services.Settings.ViewerWidth, services.Settings.ViewerHeight, usable);
        MinWidth = Math.Min(480, Math.Max(1, usable.Width - 24));
        MinHeight = Math.Min(300, Math.Max(1, usable.Height - 24));
        Width = restored.Width;
        Height = restored.Height;
        Opened += (_, _) =>
        {
            if (Math.Abs(Width - restored.Width) < 1 && Math.Abs(Height - restored.Height) < 1)
            {
                // Screen information may first become available when the native window opens.
                Size currentArea = ViewerWorkingArea();
                Size currentSize = RestoreViewerSize(services.Settings.ViewerWidth, services.Settings.ViewerHeight, currentArea);
                MinWidth = Math.Min(480, Math.Max(1, currentArea.Width - 24));
                MinHeight = Math.Min(300, Math.Max(1, currentArea.Height - 24));
                Width = currentSize.Width;
                Height = currentSize.Height;
            }
            _initialWindowSize = new Size(Width, Height);
            _lastNormalWindowSize = _initialWindowSize;
            _windowOpened = true;
        };
        _restoreSizeTimer.Tick += (_, _) =>
        {
            _restoreSizeTimer.Stop();
            _restoringWindowState = false;
        };
        _normalSizeTimer.Tick += (_, _) =>
        {
            _normalSizeTimer.Stop();
            _normalSizePending = false;
            if (!_restoringWindowState && WindowState == WindowState.Normal)
                RememberNormalViewerSize(Bounds.Size);
        };
        PropertyChanged += (_, change) =>
        {
            if (!_windowOpened || change.Property != WindowStateProperty) return;
            // Some native backends send the maximized SizeChanged before
            // publishing WindowState. Discard that uncommitted candidate.
            _normalSizeTimer.Stop();
            _normalSizePending = false;
            _restoringWindowState = true;
            _restoreSizeTimer.Stop();
            if (WindowState == WindowState.Normal) _restoreSizeTimer.Start();
        };
        SizeChanged += (_, args) =>
        {
            if (!_windowOpened || _restoringWindowState || WindowState != WindowState.Normal ||
                !ValidViewerDimension(args.NewSize.Width) || !ValidViewerDimension(args.NewSize.Height)) return;
            _normalSizePending = true;
            _normalSizeTimer.Stop();
            _normalSizeTimer.Start();
        };
        Closed += (_, _) =>
        {
            _restoreSizeTimer.Stop();
            _normalSizeTimer.Stop();
            if (_normalSizePending && !_restoringWindowState && WindowState == WindowState.Normal)
                RememberNormalViewerSize(Bounds.Size);
            SaveViewerSize();
        };
        TextDecoding.EnsureCodePages();
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }

        foreach (var (name, _) in TextDecoding.Choices) _encodingBox.Items.Add(name);
        Avalonia.Automation.AutomationProperties.SetName(_search, "Find in file");
        Avalonia.Automation.AutomationProperties.SetName(_previousMatch, "Find previous match");
        Avalonia.Automation.AutomationProperties.SetName(_nextMatch, "Find next match");
        Avalonia.Automation.AutomationProperties.SetAutomationId(_matchCount, "Find match count");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_matchCount, Avalonia.Automation.AutomationLiveSetting.Polite);
        Avalonia.Automation.AutomationProperties.SetName(_encodingBox, "Text encoding");
        _wrap.IsChecked = services.Settings.ViewerWrap;
        _text.Wrap = services.Settings.ViewerWrap;
        _text.SetReader(_reader, _guess.Encoding, 0);
        _hex.SetReader(_reader);
        SetContentSize(ReadabilityPolicy.ContentSize(services.Settings.ContentFontSize));

        var toolbar = new WrapPanel { Margin = new Thickness(8, 4), ItemSpacing = 8, LineSpacing = 4 };
        toolbar.Children.Add(_modePage);
        toolbar.Children.Add(_modePicture);
        ToolTip.SetTip(_modePage, "The page as a browser draws it, with its scripts off and nothing fetched from the web");
        Avalonia.Automation.AutomationProperties.SetName(_pageView, "Web page");
        toolbar.Children.Add(_modeText);
        toolbar.Children.Add(_modeHex);
        toolbar.Children.Add(_modeInfo);
        _modeLabel.Text = "Mode: Text";
        Avalonia.Automation.AutomationProperties.SetName(_modeLabel, "Current viewer mode");
        toolbar.Children.Add(_modeLabel);
        toolbar.Children.Add(_fitPicture);
        toolbar.Children.Add(_actualSize);
        toolbar.Children.Add(_pictureZoom);
        ToolTip.SetTip(_modePicture, "The picture (Ctrl+P)");
        ToolTip.SetTip(_fitPicture, "Fit the whole picture in the window (0)");
        ToolTip.SetTip(_actualSize, "Show at 100%: one source pixel per device pixel, subject to the decoder's resolution cap (1 or Z)");
        Avalonia.Automation.AutomationProperties.SetName(_fitPicture, "Fit picture to window");
        Avalonia.Automation.AutomationProperties.SetName(_actualSize, "Picture at 100 percent");
        Avalonia.Automation.AutomationProperties.SetAutomationId(_pictureZoom, "Picture zoom percentage");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_pictureZoom, Avalonia.Automation.AutomationLiveSetting.Polite);
        Avalonia.Automation.AutomationProperties.SetName(_picture, "Picture");
        ToolTip.SetTip(_modeInfo, "Structure of executables and images: headers, sections, imports, version, EXIF (Ctrl+I)");
        Avalonia.Automation.AutomationProperties.SetName(_info, "File information");
        ShowInfoText("");
        var encodingLabel = new TextBlock { Text = "Encoding:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var goTo = new Button { Content = "Go to…" };
        goTo.Click += async (_, _) => await GoToAsync();
        _textOnly.AddRange([encodingLabel, _encodingBox, _wrap, _follow, _search, _matchCase, _hexSearch, goTo,
            _encodingInfo, _previousMatch, _nextMatch, _matchCount, _stopCount]);
        foreach (var part in _textOnly.Take(4)) toolbar.Children.Add(part);
        Avalonia.Automation.AutomationProperties.SetName(_findToggle, "Show or hide viewer Find bar");
        ToolTip.SetTip(_findToggle, new RichTip("Find in this view", "Ctrl+F", "Opens the focused Find row; the query stays when hidden."));
        _findToggle.Click += (_, _) => SetFindOpen(!_findOpen, focus: !_findOpen);
        toolbar.Children.Add(_findToggle);
        _findBar.Children.Add(_search);
        _findBar.Children.Add(_matchCase);
        _findBar.Children.Add(_hexSearch);
        Avalonia.Automation.AutomationProperties.SetName(_smallerText, "Decrease text and hex size");
        Avalonia.Automation.AutomationProperties.SetName(_largerText, "Increase text and hex size");
        Avalonia.Automation.AutomationProperties.SetName(_contentSize, "Text and hex size");
        ToolTip.SetTip(_smallerText, "Decrease text and hex size (Ctrl+minus)");
        ToolTip.SetTip(_largerText, "Increase text and hex size (Ctrl+plus)");
        _smallerText.Click += (_, _) => SetContentSize(_text.FontSize - 1);
        _largerText.Click += (_, _) => SetContentSize(_text.FontSize + 1);
        toolbar.Children.Add(_smallerText);
        toolbar.Children.Add(_contentSize);
        toolbar.Children.Add(_largerText);
        _textOnly.AddRange([_smallerText, _contentSize, _largerText]);
        ToolTip.SetTip(_previousMatch, "Find the previous match (Shift+F3)");
        ToolTip.SetTip(_nextMatch, "Find the next match (F3)");
        _previousMatch.Click += async (_, _) => await FindAsync(forward: false);
        _nextMatch.Click += async (_, _) => await FindAsync(forward: true);
        _findBar.Children.Add(_previousMatch);
        _findBar.Children.Add(_nextMatch);
        // Keep Stop beside the navigation it interrupts. A long count label
        // must not push the only cancellation control onto another row.
        _findBar.Children.Add(_stopSearch);
        _findBar.Children.Add(_matchCount);
        ToolTip.SetTip(_stopCount, "Stop counting matches in the background");
        _stopCount.Click += (_, _) => _countCts?.Cancel();
        _findBar.Children.Add(_stopCount);
        ToolTip.SetTip(_refresh, _reopen is null ? "This source cannot be reopened from this viewer"
            : "Reopen this source and refresh the current view (F5)");
        Avalonia.Automation.AutomationProperties.SetName(_refresh, "Refresh viewer content");
        _refresh.Click += async (_, _) => await RefreshAsync();
        toolbar.Children.Add(_refresh);
        toolbar.Children.Add(goTo);
        UpdateMatchControls();
        ToolTip.SetTip(_stopSearch, "Stop the current search (Esc). Other navigation remains available.");
        _stopSearch.Click += (_, _) => _searchCts?.Cancel();
        _copySource.Click += async (_, _) => await CopyContentAsync(displayed: false, explicitVisible: true);
        _copyDisplayed.Click += async (_, _) => await CopyContentAsync(displayed: true, explicitVisible: true);
        ToolTip.SetTip(_copySource, "Copy the selected source characters, or the captured visible lines. Original tabs and logical line endings are preserved. Ctrl+C; bounded to 1 MiB of source bytes.");
        ToolTip.SetTip(_copyDisplayed, "Copy the same text range using expanded tabs and visible control symbols. Ctrl+Shift+C. Wrapping and bounded segments do not add line endings.");
        var advanced = new Button { Content = "More…" };
        Avalonia.Automation.AutomationProperties.SetName(advanced, "More viewer actions");
        ToolTip.SetTip(advanced, new RichTip("More viewer actions", null, "Checksums, source copying and byte editing."));
        var advancedMenu = new ContextMenu();
        var advancedItems = new List<MenuItem>();
        MenuItem AddAdvanced(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            advancedItems.Add(item);
            return item;
        }
        AddAdvanced("Checksum…", ShowChecksum);
        var sourceCopy = AddAdvanced("Copy visible source text", () => _ = CopyContentAsync(displayed: false, explicitVisible: true));
        var displayedCopy = AddAdvanced("Copy displayed text", () => _ = CopyContentAsync(displayed: true, explicitVisible: true));
        _copyNotice.StopRequested += () => _copyCts?.Cancel();
        if (source.LocalPath is not null && OperatingSystem.IsWindows())
        {
            AddAdvanced("Edit bytes…", EditBytes);
        }
        advancedMenu.ItemsSource = advancedItems;
        advanced.ContextMenu = advancedMenu;
        advanced.Click += (_, _) =>
        {
            sourceCopy.Header = _copySource.Content;
            sourceCopy.IsEnabled = !_isPicture && !_isPage;
            displayedCopy.IsEnabled = !_isPicture && !_isPage && (!_isHex || _isInfo);
            advancedMenu.Open(advanced);
        };
        toolbar.Children.Add(advanced);

        // Recovered content with lost parts: say which bytes are zeros only because their data is gone (plan §17.1).
        Border? lostNotice = null;
        if (source is IPartialContent partial && (partial.MissingRanges.Count > 0 || partial.Caveat is not null))
        {
            string notice = string.Join(" ", new[] { partial.Caveat, partial.MissingRanges.Count > 0 ? PartialContent.Describe(partial.MissingRanges, _reader.Length) : null }.Where(t => t is not null));
            lostNotice = new Border
            {
                Padding = new Thickness(8, 4),
                Child = new TextBlock { Text = notice, Classes = { "warning" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            };
            DockPanel.SetDock(lostNotice, Dock.Top);
        }
        var statusBar = new Border { Classes = { "status" }, Child = new DockPanel { Children = { _encodingInfo, _status } } };
        DockPanel.SetDock(_encodingInfo, Dock.Right);
        var content = new Panel { Children = { _text, _hex, _info, _picture, _pageView } };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_findBar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_findBar);
        _staleBanner.Child = _staleText;
        DockPanel.SetDock(_staleBanner, Dock.Top);
        root.Children.Add(_staleBanner);
        if (lostNotice is not null) root.Children.Add(lostNotice);
        root.Children.Add(statusBar);
        BuildChecksumPanel();
        DockPanel.SetDock(_checksumPanel, Dock.Bottom);
        root.Children.Add(_checksumPanel);
        DockPanel.SetDock(_copyNotice, Dock.Bottom);
        root.Children.Add(_copyNotice);
        root.Children.Add(content);
        Content = new Grid { Children = { ThemeLayers.Over(root, content), _dialogHost } };
        _dialogs = new OverlayDialogService(_dialogHost, () => _isHex ? _hex : _isInfo ? _info : _text);

        _modeText.Click += (_, _) => SetMode(false);
        _modeHex.Click += (_, _) => SetMode(true);
        _modeInfo.Click += async (_, _) => await ShowInfoAsync();
        _modePicture.Click += (_, _) => ShowPicture();
        _modePage.Click += (_, _) => ShowPage();
        _pageView.Changed += () => { if (_isPage) UpdateStatus(); };
        _pageView.Failed += why =>
        {
            if (_refreshing && _isPage)
            {
                _refreshFailure = why;
                UpdateRefreshFeedback();
                return;
            }
            // Without an engine the page's source is what can be shown.
            _pageUnavailable = why;
            if (!_isPage) return;
            SetMode(false);
            _status.Text = why + " Its source is shown instead.";
        };
        _pageView.KeyRequested = OnPageKey;
        _fitPicture.Click += (_, _) => { _picture.Fit = true; UpdatePictureZoomControls(); };
        _actualSize.Click += (_, _) => { _picture.ShowActualSize(); UpdatePictureZoomControls(); };
        _picture.SizeChanged += (_, _) => { if (_isPicture) UpdateStatus(); };
        _picture.ZoomChanged += () => { UpdatePictureZoomControls(); if (_isPicture) UpdateStatus(); };
        _wrap.IsCheckedChanged += (_, _) =>
        {
            if (_isInfo) _info.Wrap = _wrap.IsChecked == true;
            else
            {
                _text.Wrap = _wrap.IsChecked == true;
                _services.Settings.ViewerWrap = _text.Wrap;
            }
        };
        _encodingBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != ComboBox.SelectedIndexProperty) return;
            InvalidateSearch();
            _copyCts?.Cancel();
            if (_encodingBox.SelectedIndex < 0) return;
            var enc = TextDecoding.Choices[_encodingBox.SelectedIndex].Get();
            bool bomMatches = _guess.PreambleLength > 0 && enc.WebName == _guess.Encoding.WebName;
            _text.SetEncoding(enc, bomMatches ? _guess.PreambleLength : 0);
            _encodingInfo.Text = bomMatches || enc.WebName == _guess.Encoding.WebName ? $"{enc.WebName}: {_guess.Evidence}" : $"{enc.WebName}: chosen manually (detected: {_guess.Evidence})";
        };
        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await FindAsync(forward: (e.KeyModifiers & KeyModifiers.Shift) == 0);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _searchCts?.Cancel();
                _countCts?.Cancel();
                FocusContent();
            }
        };
        // TextChanged is routed later by Avalonia. PropertyChanged invalidates synchronously, so a
        // queued notification for the text just searched cannot cancel the new Enter/F3 request.
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (!string.IsNullOrEmpty(_search.Text)) SetFindOpen(true);
            InvalidateSearch();
        };
        _matchCase.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidateSearch(); };
        _hexSearch.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidateSearch(); };
        _text.PositionChanged += () => SearchPositionChanged(_text);
        _info.PositionChanged += () => SearchPositionChanged(_info);
        _hex.CursorMoved += () => SearchPositionChanged(_hex);
        _text.ContentChanged += UpdateCopyActions;
        _info.ContentChanged += UpdateCopyActions;
        _hex.CursorMoved += UpdateCopyActions;
        // Damage found while reading (an archive member, part way) shows in the status line as soon as it is known.
        _reader.PageLoaded += OnContentPageLoaded;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        // The interval/callback constructor starts immediately. Poll only after initial encoding
        // detection has finished, so a refresh cannot race the reader's opening request.
        _changeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
        _changeTimer.Tick += (_, _) => { _ = CheckForChanges(); };
        Opened += async (_, _) =>
        {
            if (!s_open.Contains(this)) s_open.Add(this);
            SetMode(hex);
            await DetectEncodingAsync(forceHexIfBinary: !hex);
            if (_closing.IsCancellationRequested || _services.Io.IsStopped) return;
            _changeTimer.Start();
            _readyForRefresh = true;
            UpdateRefreshFeedback();
            FocusContent();
        };
        Closed += (_, _) =>
        {
            _dialogs.CancelAll();
            _changeTimer.Stop();
            _refreshCts?.Cancel();
            _searchCts?.Cancel();
            _countCts?.Cancel();
            _closing.Cancel();
            _text.SetReader(null, _text.Encoding, 0);
            _hex.SetReader(null);
            _info.SetReader(null, _info.Encoding, 0);
            _picture.Clear();
            _reader.Dispose();
            _infoReader?.Dispose();
        };
    }

    private Size ViewerWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return new Size(3840, 2160);
        double scale = double.IsFinite(screen.Scaling) && screen.Scaling > 0 ? screen.Scaling : 1;
        double width = screen.WorkingArea.Width / scale, height = screen.WorkingArea.Height / scale;
        return new Size(double.IsFinite(width) && width > 0 ? width : 3840,
            double.IsFinite(height) && height > 0 ? height : 2160);
    }

    private void OnContentPageLoaded()
    {
        if (_reader.ReadError is not null) Dispatcher.UIThread.Post(UpdateStatus);
    }

    internal static Size RestoreViewerSize(double? width, double? height, Size usableArea)
    {
        double availableWidth = double.IsFinite(usableArea.Width) && usableArea.Width > 0 ? usableArea.Width : 3840;
        double availableHeight = double.IsFinite(usableArea.Height) && usableArea.Height > 0 ? usableArea.Height : 2160;
        double maximumWidth = Math.Min(8192, Math.Max(1, availableWidth - 24));
        double maximumHeight = Math.Min(8192, Math.Max(1, availableHeight - 24));
        bool stored = width is { } w && height is { } h && ValidViewerDimension(w) && ValidViewerDimension(h);
        return new Size(Math.Clamp(stored ? width!.Value : 980, Math.Min(480, maximumWidth), maximumWidth),
            Math.Clamp(stored ? height!.Value : 700, Math.Min(300, maximumHeight), maximumHeight));
    }

    private static bool ValidViewerDimension(double value) => double.IsFinite(value) && value is >= 240 and <= 8192;

    private void RememberNormalViewerSize(Size size)
    {
        if (!ValidViewerDimension(size.Width) || !ValidViewerDimension(size.Height)) return;
        _lastNormalWindowSize = size;
        if (Math.Abs(size.Width - _initialWindowSize.Width) > 1 ||
            Math.Abs(size.Height - _initialWindowSize.Height) > 1) _userSizedWindow = true;
    }

    private void SaveViewerSize()
    {
        if (!_userSizedWindow || !ValidViewerDimension(_lastNormalWindowSize.Width) ||
            !ValidViewerDimension(_lastNormalWindowSize.Height)) return;
        _services.Settings.ViewerWidth = _lastNormalWindowSize.Width;
        _services.Settings.ViewerHeight = _lastNormalWindowSize.Height;
        _services.SaveSettings();
    }

    private async Task<byte[]> ReadOpeningPrefixAsync()
    {
        var ct = _closing.Token;
        var buffer = new byte[PagedReader.PageSize];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (_services.Io.IsStopped) throw new OperationCanceledException("Viewer admission stopped.");
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void PageLoaded() => loaded.TrySetResult();
            // A surface, including the initially hidden hex surface's cached semantic snapshot,
            // may already have requested this page. Subscribe before checking the cache so fast
            // completion cannot be lost; awaiting the notification occupies no device worker.
            _reader.PageLoaded += PageLoaded;
            try
            {
                bool complete = _reader.TryRead(0, buffer, out int count);
                ct.ThrowIfCancellationRequested();
                if (_services.Io.IsStopped) throw new OperationCanceledException("Viewer admission stopped.");
                if (complete) return buffer[..count];
                // PageLoaded also signals a failed read or canceled queued demand. The next
                // cache check preserves the reader's read-error state, or retires after stop.
                await loaded.Task.WaitAsync(ct);
            }
            finally { _reader.PageLoaded -= PageLoaded; }
        }
    }

    private async Task DetectEncodingAsync(bool forceHexIfBinary)
    {
        byte[] prefix;
        try { prefix = await ReadOpeningPrefixAsync(); }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested || _services.Io.IsStopped)
        {
            if (!_closing.IsCancellationRequested)
            {
                _encodingInfo.Text = "Opening stopped.";
                if (_closing.IsCancellationRequested) return;
                _status.Text = "Opening stopped before the file could be examined.";
                // Global scheduler shutdown cancels queued pages. Retire the surfaces too,
                // otherwise their PageLoaded callbacks could request the canceled page again.
                Close();
            }
            return;
        }
        if (_closing.IsCancellationRequested || _services.Io.IsStopped) return;
        _guess = TextDecoding.Detect(prefix);
        int index = Array.FindIndex(TextDecoding.Choices.ToArray(), c => c.Get().WebName == _guess.Encoding.WebName);
        _text.SetEncoding(_guess.Encoding, _guess.PreambleLength);
        // Content/automation subscribers can close the window during a source-state commit.
        if (_closing.IsCancellationRequested || _services.Io.IsStopped) return;
        _encodingBox.SelectedIndex = index >= 0 ? index : 0;
        if (_closing.IsCancellationRequested || _services.Io.IsStopped) return;
        _encodingInfo.Text = $"{_guess.Encoding.WebName}: {_guess.Evidence}";
        // A picture opens as the picture (Alt+F3 asked for the bytes: those stay, and the picture is a click away).
        _modePicture.IsVisible = PictureDecoder.Recognize(prefix) is not null;
        // A Markdown file opens drawn, as a page does (release issue I25); its text is an F4 away.
        _modePage.IsVisible = HtmlPage.IsHtml(_displayName, prefix) || Markdown.IsMarkdown(_displayName);
        if (Markdown.IsMarkdown(_displayName)) ToolTip.SetTip(_modePage, "The Markdown drawn as a page: its HTML shown as text, pictures only from its folder, nothing fetched from the web");
        // A web page opens as the page, as a browser shows it (its scripts and the web off); its source is an F4 away.
        if (_modePage.IsVisible && forceHexIfBinary) ShowPage();
        else if (_modePicture.IsVisible && forceHexIfBinary) ShowPicture();
        // Programs and libraries open on their structure, as Salamander's viewer opens them in its PE viewer; their bytes
        // are an F4 away.
        else if (IsExecutable(prefix) && forceHexIfBinary) await ShowInfoAsync();
        else if (_guess.LooksBinary && forceHexIfBinary) SetMode(true);
        UpdateStatus();
    }

    /// <summary>Windows (PE and DOS), Linux (ELF), and Apple (Mach-O, universal) programs and libraries, and Java classes.</summary>
    internal static bool IsExecutable(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 8) return false;
        if (prefix[0] == 'M' && prefix[1] == 'Z') return true;
        if (prefix[0] == 0x7F && prefix[1] == 'E' && prefix[2] == 'L' && prefix[3] == 'F') return true;
        uint little = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(prefix), big = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(prefix);
        return little is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE || big is 0xCAFEBABE or 0xCAFEBABF;
    }

    /// <summary>The picture mode: decoded once, by the worker, while the window stays responsive.</summary>
    public void ShowPicture()
    {
        if (!_modePicture.IsVisible) return;
        InvalidateSearch();
        _copyCts?.Cancel();
        LeavePage();
        _isPicture = true;
        _modeLabel.Text = "Mode: Picture";
        _findToggle.IsVisible = false;
        _findBar.IsVisible = false;
        _isInfo = false;
        _picture.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = _info.IsVisible = false;
        _modePicture.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = _modeInfo.IsChecked = false;
        _fitPicture.IsVisible = _actualSize.IsVisible = _pictureZoom.IsVisible = true;
        UpdatePictureZoomControls();
        foreach (var part in _textOnly) part.IsVisible = false;
        _wrap.IsEnabled = false;
        _picture.Focus();
        _pictureLoad ??= LoadPictureAsync();
        UpdateStatus();
        UpdateCopyActions();
    }

    /// <summary>The picture once it has loaded (tests).</summary>
    public Task? PictureLoad => _pictureLoad;

    public DecodedPicture? Picture => _picture.Picture;

    private async Task LoadPictureAsync()
    {
        var reader = _reader;
        _pictureGeneration = _sourceGeneration;
        _picture.ShowMessage("Decoding the picture…");
        try
        {
            var picture = await PictureDecoder.DecodeAsync(reader, _services.Io, _deviceKey, PictureSide, _closing.Token);
            if (_closing.IsCancellationRequested || !ReferenceEquals(reader, _reader))
            {
                picture.Bitmap.Dispose();
                return;
            }
            _picture.Show(picture);
        }
        catch (Exception) when (_closing.IsCancellationRequested)
        {
            return; // the window closed meanwhile
        }
        catch (Exception) when (!ReferenceEquals(reader, _reader))
        {
            return; // a newer source owns the picture view
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (ReferenceEquals(reader, _reader))
                _picture.ShowMessage($"This picture cannot be shown: {ex.Message}. Text and Hex (F4) show its bytes; Info (Ctrl+I) reads its headers.");
        }
        if (ReferenceEquals(reader, _reader)) UpdateStatus();
    }

    /// <summary>The page mode: the page drawn by the system's browser engine (a native view over the window's content).</summary>
    public void ShowPage()
    {
        if (_closing.IsCancellationRequested || !_modePage.IsVisible) return;
        InvalidateSearch();
        _copyCts?.Cancel();
        if (_pageUnavailable is { } why)
        {
            SetMode(false);
            _status.Text = why + " Its source is shown instead.";
            return;
        }
        LeavePicture();
        _isInfo = false;
        _isPage = true;
        _modeLabel.Text = "Mode: Page";
        _findBar.IsVisible = _findOpen;
        _pageView.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = _info.IsVisible = false;
        _modePage.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = _modeInfo.IsChecked = false;
        foreach (var part in _textOnly) part.IsVisible = part == _search || part == _matchCase ||
            part == _previousMatch || part == _nextMatch || part == _matchCount || part == _stopCount;
        UpdateMatchControls();
        if (_htmlPage is null)
        {
            _pageGeneration = _sourceGeneration;
            _htmlPage = Markdown.IsMarkdown(_displayName) ? HtmlPage.ForMarkdown(_reader, _displayName, _closing.Token) : new HtmlPage(_reader, _displayName, _closing.Token);
            _pageView.Show(_htmlPage);
        }
        _pageView.FocusPage();
        UpdateStatus();
        UpdateCopyActions();
    }

    /// <summary>Whether the page mode is shown (tests).</summary>
    internal bool IsPageShown => _isPage;

    internal string StatusLine => _status.Text ?? "";

    private void LeavePage()
    {
        if (_isPage) InvalidateSearch();
        _isPage = false;
        _pageView.IsVisible = false;
        _modePage.IsChecked = false;
    }

    /// <summary>
    /// Keys pressed while the page has the keyboard (it keeps them from the window): the viewer's own act as elsewhere.
    /// They are acted on after the page's event returns, as closing the window then is safe.
    /// </summary>
    private bool OnPageKey(Key key, KeyModifiers modifiers)
    {
        if (_dialogs.IsOpen) return false;
        Action? act = (key, modifiers) switch
        {
            (Key.Escape, KeyModifiers.None) when _searchCts is not null => () => _searchCts?.Cancel(),
            (Key.Escape or Key.F10, KeyModifiers.None) => Close,
            (Key.F4, KeyModifiers.None) => () => SetMode(false),
            (Key.F, KeyModifiers.Control) or (Key.F7, KeyModifiers.None) => () => SetFindOpen(true, focus: true),
            (Key.F3, KeyModifiers.None) => () => _ = FindAsync(forward: true),
            (Key.F3, KeyModifiers.Shift) => () => _ = FindAsync(forward: false),
            (Key.F5, KeyModifiers.None) => () => _ = RefreshAsync(),
            (Key.I, KeyModifiers.Control) => () => _ = ShowInfoAsync(),
            (Key.K, KeyModifiers.Control) => ShowChecksum,
            _ => null,
        };
        if (act is null) return false;
        Dispatcher.UIThread.Post(act);
        return true;
    }

    private void LeavePicture()
    {
        LeavePage();
        _isPicture = false;
        _picture.IsVisible = false;
        _modePicture.IsChecked = false;
        _findToggle.IsVisible = true;
        _findBar.IsVisible = _findOpen;
        _fitPicture.IsVisible = _actualSize.IsVisible = _pictureZoom.IsVisible = false;
        foreach (var part in _textOnly) part.IsVisible = true;
    }

    /// <summary>The Info mode: what a static inspector reads from the file's structure (plan §16.1), computed once.</summary>
    public async Task ShowInfoAsync()
    {
        if (_closing.IsCancellationRequested) return;
        InvalidateSearch();
        _copyCts?.Cancel();
        LeavePicture();
        if (!_isInfo) _lastHit = -1;
        _isInfo = true;
        _modeLabel.Text = "Mode: Info";
        _findBar.IsVisible = _findOpen;
        _info.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = false;
        _modeInfo.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = false;
        // Find works in the report; the text-only settings do not apply to it.
        foreach (var part in _textOnly) part.IsVisible = part == _search || part == _matchCase || part == _wrap ||
            part == _previousMatch || part == _nextMatch || part == _matchCount || part == _stopCount;
        UpdateMatchControls();
        _wrap.IsEnabled = true;
        _wrap.IsChecked = _info.Wrap;
        UpdateCopyActions();
        _info.Focus();
        UpdateStatus();
        // Shown again while the structure is still being read (a program opens on Info by itself): wait for the same reading.
        if (_infoLoading is { } reading)
        {
            await reading;
            return;
        }
        if (!_infoLoaded)
        {
            _infoLoaded = true;
            var done = new TaskCompletionSource();
            _infoLoading = done.Task;
            try { await LoadInfoAsync(); }
            finally
            {
                _infoLoading = null;
                done.SetResult();
            }
        }
    }

    private Task? _infoLoading;

    private async Task LoadInfoAsync()
    {
        var reader = _reader;
        _infoGeneration = _sourceGeneration;
        ShowInfoText("Reading the file's structure…");
        try
        {
            var text = await Task.Run(() =>
            {
                string result = "";
                reader.WithSource(source =>
                {
                    _closing.Token.ThrowIfCancellationRequested();
                    var report = FileCat.Core.Inspect.Inspectors.Inspect(new InspectionSource(source, _closing.Token), _closing.Token);
                    _closing.Token.ThrowIfCancellationRequested();
                    result = report?.ToText() ?? $"No structure inspector for this kind of file.\n\nSize: {source.Length:N0} bytes\nContent: {(_guess.LooksBinary ? "binary" : "text, " + _guess.Encoding.WebName + " (" + _guess.Evidence + ")")}";
                });
                return result;
            }, _closing.Token);
            if (_closing.IsCancellationRequested || !ReferenceEquals(reader, _reader)) return;
            ShowInfoText(text);
        }
        catch (Exception) when (_closing.IsCancellationRequested)
        {
            return; // the window closed while the structure was being read
        }
        catch (Exception ex)
        {
            // Inspectors report damage as warnings; anything else is shown here rather than ending the application.
            if (ReferenceEquals(reader, _reader)) ShowInfoText("The file's structure could not be read: " + ex.Message);
        }
        if (ReferenceEquals(reader, _reader)) UpdateStatus();
    }

    // A borrowed inspector source observes close between calls, including an inspector's short-read loop.
    // The reader retains ownership until the inspection returns.
    private sealed class InspectionSource(IContentSource source, CancellationToken ct) : IContentSource
    {
        public string DisplayName => source.DisplayName;
        public bool CanSeek => source.CanSeek;
        public string? LocalPath => source.LocalPath;
        public long Length
        {
            get
            {
                ct.ThrowIfCancellationRequested();
                long length = source.Length;
                ct.ThrowIfCancellationRequested();
                return length;
            }
        }
        public ContentRevision? GetRevision()
        {
            ct.ThrowIfCancellationRequested();
            var revision = source.GetRevision();
            ct.ThrowIfCancellationRequested();
            return revision;
        }
        public int Read(long offset, Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            int read = source.Read(offset, buffer);
            ct.ThrowIfCancellationRequested();
            return read;
        }
        public void Dispose() { } // borrowed; PagedReader releases the source
    }

    private void ShowInfoText(string text)
    {
        InvalidateSearch();
        if (_isInfo) _copyCts?.Cancel();
        _infoText = text;
        _infoLineCount = text.Count(c => c == '\n');
        var old = _infoReader;
        _infoReader = new PagedReader(new MemoryContentSource("Info", Encoding.UTF8.GetBytes(text)));
        _info.SetReader(_infoReader, new UTF8Encoding(false), 0);
        // Its pages count against the shared content budget until it is disposed.
        old?.Dispose();
        _lastHit = -1;
    }

    public string InfoText => _infoText;

    /// <summary>Whether Info mode is shown, the status line, and where the report is scrolled to (tests).</summary>
    internal bool IsInfoShown => _isInfo;

    internal string StatusText => _status.Text ?? "";

    internal long InfoTop => _info.TopOffset;

    private void SetMode(bool hex)
    {
        InvalidateSearch();
        _copyCts?.Cancel();
        LeavePicture();
        if (_isInfo) _lastHit = -1;
        _isInfo = false;
        _info.IsVisible = false;
        _modeInfo.IsChecked = false;
        _isHex = hex;
        _modeLabel.Text = hex ? "Mode: Hex" : "Mode: Text";
        _findBar.IsVisible = _findOpen;
        _hex.IsVisible = hex;
        _text.IsVisible = !hex;
        _modeHex.IsChecked = hex;
        _modeText.IsChecked = !hex;
        _wrap.IsEnabled = !hex;
        _wrap.IsChecked = _text.Wrap;
        if (hex) _hex.GoTo(_text.TopOffset);
        else _text.ScrollToOffset(_hex.CursorOffset);
        UpdateMatchControls();
        FocusContent();
        UpdateStatus();
        UpdateCopyActions();
    }

    private void FocusContent()
    {
        if (_isPicture) _picture.Focus();
        else if (_isPage) _pageView.FocusPage();
        else if (_isInfo) _info.Focus();
        else if (_isHex) _hex.Focus();
        else _text.Focus();
    }

    private void UpdatePictureZoomControls()
    {
        _fitPicture.IsChecked = _picture.Fit;
        _actualSize.IsChecked = !_picture.Fit && Math.Abs(_picture.Zoom - 1) < 0.001;
        _pictureZoom.Text = _picture.Fit ? $"Fit · {_picture.Zoom * 100:0}%" : $"Zoom {_picture.Zoom * 100:0}%";
    }

    private void SetFindOpen(bool open, bool focus = false)
    {
        _findOpen = open;
        _findBar.IsVisible = open && !_isPicture;
        _findToggle.Content = open ? "Hide Find" : "Find…";
        if (focus && _findBar.IsVisible)
        {
            _search.Focus();
            _search.SelectAll();
        }
    }

    private void SetContentSize(double requested)
    {
        double size = ReadabilityPolicy.ContentSize(requested);
        _text.FontSize = size;
        _hex.FontSize = size;
        _info.FontSize = size;
        _contentSize.Text = $"{size:0} size";
        _smallerText.IsEnabled = size > ReadabilityPolicy.MinimumContentSize;
        _largerText.IsEnabled = size < ReadabilityPolicy.MaximumContentSize;
    }

    private void UpdateStatus()
    {
        UpdateRefreshFeedback();
        if (_isPicture)
        {
            _status.Text = _picture.Picture is { } p
                ? $"{p.Format} · {p.Width:N0} × {p.Height:N0} pixels · {(_picture.Fit ? "fitted, " : "")}{_picture.Zoom * 100:0}%" +
                  (p.Frames > 1 ? $" · animated, {p.Frames:N0} frames: the first is shown" : "") +
                  (p.Incomplete ? " · the file ends early: the rest of the picture is blank" : "") +
                  $" · {Formatters.ExactSize(_reader.Length)}"
                : Formatters.ExactSize(_reader.Length);
            if (_pictureGeneration != _sourceGeneration)
                _status.Text = "The file changed; Refresh (F5) to update this picture. " + _status.Text;
            return;
        }
        if (_isInfo)
        {
            _status.Text = $"The file's structure: {_infoLineCount.ToString("N0", CultureInfo.CurrentCulture)} lines · Ctrl+F finds in it · F4 shows the bytes · {Formatters.ExactSize(_reader.Length)}";
            if (_infoGeneration != _sourceGeneration)
                _status.Text = "The file changed; Refresh (F5) to update this structure report. " + _status.Text;
            return;
        }
        if (_isPage)
        {
            int blocked = _pageView.BlockedCount;
            bool markdown = Markdown.IsMarkdown(_displayName);
            _status.Text = (!markdown && _pageView.Title is { Length: > 0 } title ? title + " · " : "") +
                           (markdown ? "Markdown drawn as a page: its HTML is shown as text, scripts do not run and nothing is fetched from the web"
                               : "Web page: its scripts do not run and nothing is fetched from the web") +
                           (blocked > 0 ? $" ({blocked.ToString("N0", CultureInfo.CurrentCulture)} {(blocked == 1 ? "request" : "requests")} refused)" : "") +
                           $" · F4 shows its {(markdown ? "text" : "source")} · {Formatters.ExactSize(_reader.Length)}";
            if (_pageGeneration != _sourceGeneration)
                _status.Text = "The file changed; Refresh (F5) to update this page. " + _status.Text;
            else if (_pageSearchStatus is { } searchStatus)
                _status.Text = searchStatus + " · " + _status.Text;
            return;
        }
        long len = _reader.Length;
        long pos = _isHex ? _hex.CursorOffset : _text.TopOffset;
        double pct = len > 0 ? 100.0 * pos / len : 0;
        var sel = _hex.Selection;
        _status.Text = (_reader.ReadError is { } error ? "Not all of the content could be read: " + error + " · " : string.Empty) +
                       $"{Formatters.ExactSize(len)} · offset 0x{pos:X} ({pos.ToString("N0", CultureInfo.CurrentCulture)}) · {pct:0.#}%" +
                       (_isHex && sel.Length > 1 ? $" · selected {sel.Length.ToString("N0", CultureInfo.CurrentCulture)} bytes" : string.Empty);
    }

    private async Task CheckForChanges()
    {
        if (_closing.IsCancellationRequested || _checkingChanges || _refreshing) return;
        _checkingChanges = true;
        var reader = _reader;
        try
        {
            bool changed = await _services.Io.Run(_deviceKey, IoPriority.Background,
                ct => reader.Refresh(ct), _closing.Token);
            if (_closing.IsCancellationRequested || !ReferenceEquals(reader, _reader) || !changed) return;
            _sourceGeneration++;
            _copyCts?.Cancel();
            InvalidateSearch();
            _checksumCts?.Cancel(); // A hash in progress belongs to the source revision before this refresh.
            if (_checksumPanel.IsVisible)
            {
                _copyChecksum.IsEnabled = false;
                _checksumResult.Text = "The file changed. Open Checksum again to review the current scope before computing.";
            }
            _text.SourceChanged();
            _hex.SourceChanged();
            if (_follow.IsChecked == true)
            {
                if (_isHex) _hex.GoTo(Math.Max(0, _reader.Length - 1));
                else _text.GoToEnd();
            }
            if (_isPicture || _isInfo || _isPage) UpdateStatus();
            else _status.Text = "The file changed on disk; showing its current content. " + _status.Text;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or NotSupportedException)
        {
            if (!_closing.IsCancellationRequested)
                _status.Text = "The file's current state could not be checked: " + ex.Message;
        }
        finally
        {
            _checkingChanges = false;
        }
    }

    private void UpdateRefreshFeedback()
    {
        bool stale = _isPicture ? _pictureGeneration != _sourceGeneration
            : _isInfo ? _infoGeneration != _sourceGeneration
            : _isPage && _pageGeneration != _sourceGeneration;
        _refresh.IsEnabled = _reopen is not null && _readyForRefresh && !_refreshing &&
            (!_isPage || _pageView.PageLoaded);
        _staleBanner.IsVisible = stale || _refreshFailure is not null;
        _staleText.Text = _refreshFailure is { } failure
            ? $"Refresh failed: {failure}. The previous view remains available{(stale ? " and is still stale" : "")}."
            : stale ? "File changed — Refresh (F5) to show the new version." : string.Empty;
    }

    /// <summary>Reopens the exact provider item and prepares its visible snapshot before retiring the old source.</summary>
    internal async Task RefreshAsync()
    {
        if (_reopen is null)
        {
            _status.Text = "This source cannot be reopened here; open it again from its provider to refresh.";
            return;
        }
        if (!_readyForRefresh || _refreshing || _closing.IsCancellationRequested) return;
        _refreshing = true;
        _refreshFailure = null;
        UpdateRefreshFeedback();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _refreshCts = cts;
        var ct = cts.Token;
        var oldReader = _reader;
        long oldGeneration = _sourceGeneration;
        bool pictureMode = _isPicture, infoMode = _isInfo, pageMode = _isPage, hexMode = _isHex;
        IContentSource? opened = null;
        PagedReader? prepared = null;
        DecodedPicture? decoded = null;
        bool replacementPageShown = false;
        HtmlPage? previousPage = _htmlPage;
        try
        {
            if (pageMode && !_pageView.PageLoaded)
                throw new InvalidOperationException("Wait for the current page to finish loading before refreshing");
            opened = await _reopen(ct) ?? throw new IOException("This provider cannot reopen the selected item");
            // Provider metadata may touch a remote device. Keep it off the UI thread and await
            // completion even after close, so the source cannot be disposed under an active call.
            var admitted = opened;
            prepared = await Task.Run(() => new PagedReader(admitted, _services.Io, _deviceKey), CancellationToken.None);
            opened = null; // the reader now owns it
            var reader = prepared;
            byte[] prefix = await _services.Io.Run(_deviceKey, IoPriority.Interactive, _ =>
            {
                var buffer = new byte[PagedReader.PageSize];
                int count = 0;
                reader.WithSource(source => count = source.Read(0, buffer));
                return buffer[..count];
            }, ct);
            EncodingGuess guess = TextDecoding.Detect(prefix);
            string? report = null;
            HtmlPage? page = null;
            if (pictureMode)
            {
                if (PictureDecoder.Recognize(prefix) is null)
                    throw new InvalidDataException("The new version is not a supported picture");
                decoded = await PictureDecoder.DecodeAsync(reader, _services.Io, _deviceKey, PictureSide, ct);
            }
            else if (infoMode)
            {
                report = await Task.Run(() =>
                {
                    string result = "";
                    reader.WithSource(source =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var inspection = FileCat.Core.Inspect.Inspectors.Inspect(new InspectionSource(source, ct), ct);
                        result = inspection?.ToText() ?? $"No structure inspector for this kind of file.\n\nSize: {source.Length:N0} bytes\nContent: {(guess.LooksBinary ? "binary" : "text, " + guess.Encoding.WebName + " (" + guess.Evidence + ")")}";
                    });
                    return result;
                }, ct);
            }
            else if (pageMode)
            {
                if (!Markdown.IsMarkdown(_displayName) && !HtmlPage.IsHtml(_displayName, prefix))
                    throw new InvalidDataException("The new version is not an HTML or Markdown page");
                page = Markdown.IsMarkdown(_displayName)
                    ? HtmlPage.ForMarkdown(reader, _displayName, ct)
                    : new HtmlPage(reader, _displayName, ct);
                var nextPage = page;
                if (await Task.Run(() => nextPage.Resolve("/" + nextPage.Name), ct) is null)
                    throw new IOException("The new page could not be read within the page-size limit");
            }
            if (await _services.Io.Run(_deviceKey, IoPriority.Interactive, token => reader.Refresh(token), ct))
                throw new IOException("The item changed again while the new view was being prepared");
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(oldReader, _reader) || oldGeneration != _sourceGeneration ||
                pictureMode != _isPicture || infoMode != _isInfo || pageMode != _isPage || hexMode != _isHex)
                throw new OperationCanceledException("The viewer changed while Refresh was preparing", ct);

            if (page is not null)
            {
                await LoadReplacementPageAsync(page, ct);
                replacementPageShown = true;
            }
            ct.ThrowIfCancellationRequested();
            if (page is not null && await _services.Io.Run(_deviceKey, IoPriority.Interactive, token => reader.Refresh(token), ct))
                throw new IOException("The page changed again while it was loading");

            long textTop = _text.TopOffset, infoTop = _info.TopOffset, hexCursor = _hex.CursorOffset;
            var textSelection = _text.HasSelection ? _text.Selection : (Start: 0L, Length: 0L);
            var infoSelection = _info.HasSelection ? _info.Selection : (Start: 0L, Length: 0L);
            var hexSelection = _hex.Selection;
            bool sameRevision = oldReader.Revision is { } revision && revision == reader.Revision &&
                oldReader.Length == reader.Length;
            bool sameReport = report is not null && string.Equals(report, _infoText, StringComparison.Ordinal);
            _copyCts?.Cancel();
            _checksumCts?.Cancel();
            _lineCts?.Cancel();
            _lines = null;
            InvalidateSearch();
            oldReader.PageLoaded -= OnContentPageLoaded;
            _reader = reader;
            prepared = null;
            _reader.PageLoaded += OnContentPageLoaded;
            _sourceGeneration++;
            var previousGuess = _guess;
            var chosenEncoding = _text.Encoding.WebName == previousGuess.Encoding.WebName ? guess.Encoding : _text.Encoding;
            _guess = guess;
            _text.SetReader(reader, chosenEncoding,
                chosenEncoding.WebName == guess.Encoding.WebName ? guess.PreambleLength : 0);
            int encodingIndex = Array.FindIndex(TextDecoding.Choices.ToArray(), choice => choice.Get().WebName == chosenEncoding.WebName);
            if (encodingIndex >= 0) _encodingBox.SelectedIndex = encodingIndex;
            _encodingInfo.Text = chosenEncoding.WebName == guess.Encoding.WebName
                ? $"{chosenEncoding.WebName}: {guess.Evidence}"
                : $"{chosenEncoding.WebName}: chosen manually (detected: {guess.Evidence})";
            _text.ScrollToOffset(textTop);
            if (sameRevision && textSelection.Length > 0)
                _text.SetSelection(textSelection.Start, textSelection.Start + textSelection.Length);
            _hex.SetReader(reader);
            if (sameRevision && hexSelection.Length > 0)
                _hex.GoTo(hexSelection.Start, select: true, length: hexSelection.Length);
            else _hex.GoTo(hexCursor);
            _modePicture.IsVisible = PictureDecoder.Recognize(prefix) is not null;
            _modePage.IsVisible = HtmlPage.IsHtml(_displayName, prefix) || Markdown.IsMarkdown(_displayName);
            if (pictureMode && decoded is not null)
            {
                _picture.ReplacePreservingView(decoded);
                decoded = null;
                _pictureGeneration = _sourceGeneration;
                _pictureLoad = Task.CompletedTask;
            }
            else
            {
                _picture.Clear();
                _pictureLoad = null;
            }
            if (infoMode && report is not null)
            {
                ShowInfoText(report);
                _info.ScrollToOffset(infoTop);
                if (sameReport && infoSelection.Length > 0)
                    _info.SetSelection(infoSelection.Start, infoSelection.Start + infoSelection.Length);
                _infoLoaded = true;
                _infoGeneration = _sourceGeneration;
            }
            else
            {
                ShowInfoText("");
                _infoLoaded = false;
            }
            _htmlPage = page;
            if (page is not null) _pageGeneration = _sourceGeneration;
            _refreshFailure = null;
            replacementPageShown = false;
            oldReader.Dispose(); // active source calls retire through PagedReader's ownership gate
            UpdateStatus();
            FocusContent();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _closing.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
                                   NotSupportedException or InvalidOperationException or OperationCanceledException or TimeoutException)
        {
            if (!_closing.IsCancellationRequested)
            {
                _refreshFailure = ex.Message;
                UpdateRefreshFeedback();
            }
        }
        finally
        {
            if (replacementPageShown && prepared is not null && previousPage is not null && !_closing.IsCancellationRequested)
                _pageView.Show(previousPage);
            decoded?.Bitmap.Dispose();
            prepared?.Dispose();
            opened?.Dispose();
            if (ReferenceEquals(_refreshCts, cts)) _refreshCts = null;
            _refreshing = false;
            UpdateRefreshFeedback();
        }
    }

    private async Task LoadReplacementPageAsync(HtmlPage next, CancellationToken ct)
    {
        var previous = _htmlPage ?? throw new InvalidOperationException("The current page is not ready");
        var completion = new TaskCompletionSource<(bool Ok, string? Why)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(bool ok, string? why) => completion.TrySetResult((ok, why));
        _pageView.LoadCompleted += Completed;
        try
        {
            _pageView.Show(next);
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            if (!result.Ok) throw new IOException("The new page could not be drawn: " + (result.Why ?? "unknown error"));
        }
        catch
        {
            if (!_closing.IsCancellationRequested) _pageView.Show(previous);
            throw;
        }
        finally { _pageView.LoadCompleted -= Completed; }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var focused = FocusManager?.GetFocusedElement();
        if (e.Key == Key.Escape && (_searchCts is not null || _countCts is not null ||
            _checksumCts is not null || _copyCts is not null))
        {
            if (_searchCts is not null) _searchCts.Cancel();
            else if (_countCts is not null) _countCts.Cancel();
            else if (_checksumCts is not null) _checksumCts.Cancel();
            else _copyCts?.Cancel();
            e.Handled = true;
            return;
        }
        if (focused is TextBox && e.Key is not (Key.F3 or Key.F4 or Key.F5 or Key.F8 or Key.F10)) return;
        switch (e.Key)
        {
            case Key.Escape when _searchCts is not null:
                _searchCts.Cancel();
                break;
            case Key.Escape when _checksumCts is not null:
                _checksumCts.Cancel();
                break;
            case Key.Escape when _lineCts is not null:
                // Esc first stops a running "go to line" scan.
                _lineCts.Cancel();
                break;
            case Key.Escape:
            case Key.F10:
                Close();
                break;
            case Key.F4:
                // From a picture to its bytes; from Info to the bytes as they read best (hex for a binary file).
                SetMode(_isPicture || (_isInfo ? _guess.LooksBinary : !_isHex));
                break;
            case Key.P when e.KeyModifiers == KeyModifiers.Control && _modePicture.IsVisible:
                ShowPicture();
                break;
            case Key.Z when e.KeyModifiers == KeyModifiers.None && _isPicture:
                if (_picture.Fit || Math.Abs(_picture.Zoom - 1) > 0.001) _picture.ShowActualSize();
                else _picture.Fit = true;
                break;
            case Key.I when e.KeyModifiers == KeyModifiers.Control:
                _ = ShowInfoAsync();
                break;
            case Key.F6 when _reader.Source.LocalPath is not null && OperatingSystem.IsWindows():
                EditBytes();
                break;
            case Key.F8:
                _encodingBox.SelectedIndex = (_encodingBox.SelectedIndex + 1) % TextDecoding.Choices.Count;
                break;
            case Key.F2 when (!_isHex || _isInfo) && !_isPicture && !_isPage:
                _wrap.IsChecked = !_wrap.IsChecked;
                break;
            case Key.F when ctrl:
            case Key.F7:
                SetFindOpen(true, focus: true);
                break;
            case Key.F3:
                await FindAsync(!shift);
                break;
            case Key.F5 when e.KeyModifiers == KeyModifiers.None:
                await RefreshAsync();
                break;
            case Key.G when ctrl && !_isInfo:
                await GoToAsync();
                break;
            case Key.C when ctrl:
                if (shift) await CopyContentAsync(displayed: true, explicitVisible: false);
                else await CopyAsync();
                break;
            case Key.K when ctrl:
                ShowChecksum();
                break;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                SetContentSize(_text.FontSize + 1);
                break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                SetContentSize(_text.FontSize - 1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>F6 (FAR's viewer-to-editor switch): the hex editor opens at this viewer's position.</summary>
    private void EditBytes()
    {
        long offset = _isHex ? _hex.CursorOffset : _text.TopOffset;
        if (HexEditorWindow.OpenOrActivate(_services, _reader.Source.LocalPath!, offset) is { } error) _status.Text = error;
    }

    private async Task FindAsync(bool forward)
    {
        if (_closing.IsCancellationRequested) return;
        var pattern = _search.Text ?? string.Empty;
        if (pattern.Length == 0)
        {
            _search.Focus();
            return;
        }
        if (_isPage)
        {
            await FindPageAsync(pattern, forward);
            return;
        }
        _searchCts?.Cancel();
        _searchCts = null;
        // In Info mode Find searches the report, as text.
        bool info = _isInfo && _infoReader is not null;
        var reader = info ? _infoReader! : _reader;
        var view = info ? _info : _text;
        bool hexMode = _hexSearch.IsChecked == true && !info;
        byte[]? bytes = hexMode ? ContentSearch.ParseHex(pattern) : null;
        if (hexMode && bytes is null)
        {
            _status.Text = "Hex search expects bytes such as \"4D 5A 90\".";
            return;
        }
        var enc = info ? new UTF8Encoding(false) : _text.Encoding;
        bool matchCase = _matchCase.IsChecked == true;
        var identity = new SearchIdentity(pattern, hexMode, enc.CodePage, matchCase, reader, _isHex && !info);
        bool continuing = _lastHit >= 0 && _lastSearch == identity;
        long from = continuing ? (forward ? _lastHit + (hexMode ? 1 : TextDecoding.UnitSize(enc)) : _lastHit)
            : info ? _info.TopOffset : _isHex ? _hex.CursorOffset : _text.TopOffset;
        using var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        _stopSearch.IsVisible = true;
        UpdateMatchControls();
        _status.Text = $"Searching {(forward ? "forward" : "backward")} from 0x{from:X}… (Esc stops)";
        long found;
        try
        {
            found = await Task.Run(() =>
            {
                if (bytes is not null)
                    return forward ? ContentSearch.FindBytes(reader, from, bytes, ct) : ContentSearch.FindBytesBackward(reader, from, bytes, ct);
                if (!forward) return ContentSearch.FindTextBackward(reader, enc, from, pattern, matchCase, ct);
                return ContentSearch.FindText(reader, enc, from, pattern, matchCase, ct);
            }, ct);
            if (_closing.IsCancellationRequested || !ReferenceEquals(_searchCts, cts)) return;
            ct.ThrowIfCancellationRequested();
            if (found < 0)
            {
                _status.Text = $"\"{pattern}\" was not found {(forward ? "after" : "before")} this position.";
                EnsureMatchCount(identity, reader, bytes, enc, matchCase);
                return;
            }
            _lastHit = found;
            _lastSearch = identity;
            _lastHitLength = bytes?.Length ?? Math.Max(1, enc.GetByteCount(pattern));
            view.SetHighlight(bytes is null ? pattern : null, matchCase);
            _revealingSearch = true;
            try
            {
                if (info) _info.ScrollToOffset(found);
                else
                {
                    if (!_isHex) _text.ScrollToOffset(found);
                    _hex.GoTo(found, select: true, _lastHitLength);
                }
            }
            finally { _revealingSearch = false; }
            UpdateStatus();
            EnsureMatchCount(identity, reader, bytes, enc, matchCase);
        }
        catch (OperationCanceledException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_searchCts, cts))
                _status.Text = "Search canceled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_searchCts, cts))
                // A provider can fail an in-flight read after Stop; the cancel request owns this search's result.
                _status.Text = ct.IsCancellationRequested
                    ? "Search canceled."
                    : "Search could not finish: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
                _stopSearch.IsVisible = false;
                UpdateMatchControls();
            }
        }
    }

    private async Task FindPageAsync(string pattern, bool forward)
    {
        _searchCts?.Cancel();
        _searchCts = null;
        if (_pageGeneration != _sourceGeneration)
        {
            _pageSearchStatus = "The file changed; Refresh (F5) before searching this page.";
            _pageFindUnavailable = true;
            UpdateMatchControls();
            UpdateStatus();
            return;
        }
        var page = _htmlPage;
        long generation = _sourceGeneration;
        using var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _stopSearch.IsVisible = true;
        _pageSearchStatus = $"Searching page {(forward ? "forward" : "backward")}… (Esc stops)";
        UpdateMatchControls();
        UpdateStatus();
        try
        {
            PageFindResult? result = await _pageView.FindAsync(pattern, _matchCase.IsChecked == true, forward, cts.Token);
            if (_closing.IsCancellationRequested || !ReferenceEquals(_searchCts, cts) || !_isPage ||
                !ReferenceEquals(page, _htmlPage) || generation != _sourceGeneration) return;
            _pageSearchStatus = result switch
            {
                { Found: true } => $"Found \"{pattern}\" in the page.",
                { Found: false } => $"\"{pattern}\" was not found in this page.",
                null => _pageView.PageLoaded ? "Page search is unavailable in this browser engine." : "The page is still loading; try search again.",
            };
            _pageTriedFind = result is not null;
            _pageFindUnavailable = result is null;
            _pageFoundMatch |= result?.Found == true;
            if (result is { MatchCount: not null }) _pageCountResult = result;
            UpdateMatchControls();
            UpdateStatus();
        }
        catch (OperationCanceledException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_searchCts, cts) && _isPage)
            {
                _pageSearchStatus = "Page search canceled.";
                UpdateStatus();
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searchCts = null;
                _stopSearch.IsVisible = false;
                UpdateMatchControls();
            }
        }
    }

    private void InvalidateSearch(bool keepCount = false)
    {
        if (_isPage) _pageView.ClearFind();
        _searchCts?.Cancel();
        _searchCts = null;
        _stopSearch.IsVisible = false;
        _lastHit = -1;
        _lastSearch = null;
        _pageSearchStatus = null;
        if (!keepCount)
        {
            _countCts?.Cancel();
            _countCts = null;
            _countIdentity = null;
            _countResult = null;
            _countFailed = false;
            _countCanceled = false;
            _pageTriedFind = false;
            _pageFoundMatch = false;
            _pageFindUnavailable = false;
            _pageCountResult = null;
            _stopCount.IsVisible = false;
        }
        UpdateMatchControls();
    }

    private void EnsureMatchCount(SearchIdentity identity, PagedReader reader, byte[]? bytes, Encoding encoding,
        bool matchCase)
    {
        if (_countIdentity == identity && (_countCts is not null || _countResult is not null ||
            _countFailed || _countCanceled))
        {
            UpdateMatchControls();
            return;
        }
        _countCts?.Cancel();
        var cts = new CancellationTokenSource();
        _countCts = cts;
        _countIdentity = identity;
        _countResult = null;
        _countFailed = false;
        _countCanceled = false;
        _stopCount.IsVisible = true;
        UpdateMatchControls();
        long byteLimit = ReferenceEquals(reader, _infoReader) || reader.Source.LocalPath is not null
            ? 64L << 20 : 8L << 20;
        _ = CountMatchesAsync(identity, reader, bytes, encoding, matchCase, byteLimit, cts);
    }

    private async Task CountMatchesAsync(SearchIdentity identity, PagedReader reader, byte[]? bytes,
        Encoding encoding, bool matchCase, long byteLimit, CancellationTokenSource cts)
    {
        try
        {
            var result = await Task.Run(() => bytes is not null
                ? ContentSearch.CountBytes(reader, bytes, byteLimit, 10_000, cts.Token)
                : ContentSearch.CountText(reader, encoding, identity.Pattern, matchCase, byteLimit, 10_000,
                    cts.Token), cts.Token);
            if (_closing.IsCancellationRequested || !ReferenceEquals(_countCts, cts) || _countIdentity != identity) return;
            _countResult = result;
        }
        catch (OperationCanceledException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_countCts, cts)) _countCanceled = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            FileCat.Core.Diagnostics.AppLog.Warn("Viewer match count: " + ex.Message);
            if (!_closing.IsCancellationRequested && ReferenceEquals(_countCts, cts)) _countFailed = true;
        }
        finally
        {
            if (ReferenceEquals(_countCts, cts))
            {
                _countCts = null;
                _stopCount.IsVisible = false;
                UpdateMatchControls();
            }
            cts.Dispose();
        }
    }

    private void UpdateMatchControls()
    {
        _stopCount.IsVisible = _countCts is not null && !_isPicture && !_isPage;
        if (_countResult is null)
        {
            ToolTip.SetTip(_matchCount, null);
            Avalonia.Automation.AutomationProperties.SetHelpText(_matchCount, string.Empty);
        }
        bool available = !_isPicture && !string.IsNullOrEmpty(_search.Text);
        _previousMatch.IsEnabled = available;
        _nextMatch.IsEnabled = available;
        if (!available)
        {
            ShowMatchCount(_isPicture ? string.Empty : "Enter text to find", outcome: false);
            return;
        }
        if (_isPage)
        {
            bool found = _pageCountResult is not null || _pageFoundMatch;
            ShowMatchCount(_pageCountResult is { MatchCount: { } total } pageCount
                ? pageCount.CountComplete ? $"{total:N0} {(total == 1 ? "match" : "matches")}" : $"At least {total:N0} matches · count limited"
                : _pageFoundMatch ? "At least 1 match · total unavailable in Page mode"
                : _searchCts is not null ? "Searching page…"
                : _pageFindUnavailable ? "Page search or count unavailable"
                : _pageTriedFind ? "0 matches from here · total unavailable in Page mode"
                : "Press Next or Previous to find",
                outcome: found || _searchCts is null && (_pageFindUnavailable || _pageTriedFind));
            if (_pageCountResult is { } counted)
            {
                string detail = counted.CountComplete
                    ? "The browser counted all matches in the rendered page."
                    : "The browser found more than 1,024 matches; this is a lower bound.";
                ToolTip.SetTip(_matchCount, detail);
                Avalonia.Automation.AutomationProperties.SetHelpText(_matchCount, detail);
            }
            return;
        }
        if (_countResult is { } count)
        {
            int known = count.Matches + (!count.Complete && _lastHit >= count.ScannedBytes ? 1 : 0);
            string noun = known == 1 ? "match" : "matches";
            ShowMatchCount(count.Complete ? $"{known:N0} {noun}" : $"At least {known:N0} {noun} · count limited", outcome: true);
            string detail = count.Complete ? "The whole searchable content was counted."
                : $"The count is a lower bound. Scanned {Formatters.ExactSize(count.ScannedBytes)} of " +
                  $"{Formatters.ExactSize(_countIdentity?.Reader.Length ?? 0)} or reached the 10,000-match limit.";
            ToolTip.SetTip(_matchCount, detail);
            Avalonia.Automation.AutomationProperties.SetHelpText(_matchCount, detail);
        }
        else if (_countCts is not null)
            ShowMatchCount(_lastHit >= 0 ? "At least 1 match · counting…" : "Counting matches…", outcome: false);
        else if (_countFailed)
            ShowMatchCount(_lastHit >= 0 ? "At least 1 match · count unavailable" : "Match count unavailable", outcome: true);
        else if (_countCanceled)
            ShowMatchCount(_lastHit >= 0 ? "At least 1 match · count stopped" : "Count stopped", outcome: true);
        else if (_searchCts is not null)
            ShowMatchCount("Searching…", outcome: false);
        else
            ShowMatchCount("Press Next or Previous to count", outcome: false);
    }

    /// <summary>Speaks only outcomes: hints and searching or counting states change while the user types and work runs.</summary>
    private void ShowMatchCount(string text, bool outcome)
    {
        Avalonia.Automation.AutomationProperties.SetLiveSetting(_matchCount,
            outcome ? Avalonia.Automation.AutomationLiveSetting.Polite : Avalonia.Automation.AutomationLiveSetting.Off);
        _matchCount.Text = text;
    }

    private void SearchPositionChanged(Control source)
    {
        // Inactive controls also adjust their scroll bars during layout/reader setup. Those are not
        // navigation in the searched surface and cannot cancel its request or replace its status.
        Control active = _isInfo ? _info : _isHex ? _hex : _text;
        if (_isPicture || _isPage || source != active) return;
        if (!_revealingSearch) InvalidateSearch(keepCount: true);
        UpdateStatus();
    }

    private async Task GoToAsync()
    {
        if (_closing.IsCancellationRequested) return;
        if (_isPage || _isPicture || _isInfo)
        {
            _status.Text = "Go to applies to Text or Hex. Switch to one of those views first.";
            return;
        }
        if (_reader.Length == 0)
        {
            _status.Text = "This file is empty; there is no position to go to.";
            return;
        }
        string initial = _isHex ? "0x" + _hex.CursorOffset.ToString("X", CultureInfo.InvariantCulture)
            : _text.TopOffset.ToString(CultureInfo.CurrentCulture);
        var result = await _dialogs.PromptAsync(new PromptOptions("Go to", "Byte offset (0x1F00 or 7936), percentage (50%), or line (L1200):")
        {
            Text = initial,
            ConfirmText = "Go",
            Hint = "Line navigation may scan the file; Esc stops a running scan.",
            Validate = text => ValidateGoTo(text, _reader.Length, _reader.Source.CanSeek, out _),
        });
        if (result is null || _closing.IsCancellationRequested) return;
        string? error = ValidateGoTo(result.Text, _reader.Length, _reader.Source.CanSeek, out var target);
        if (error is not null) { _status.Text = error; return; }
        if (target.IsLine) await GoToLineAsync(target.Value);
        else
        {
            if (_isHex) _hex.GoTo(target.Value);
            else _text.ScrollToOffset(target.Value);
            UpdateStatus();
        }
    }

    internal readonly record struct GoToTarget(bool IsLine, long Value);

    /// <summary>Pure validation for the shared prompt: keep a repairable draft in place until its target is valid.</summary>
    internal static string? ValidateGoTo(string text, long length, bool canSeek, out GoToTarget target)
    {
        target = default;
        var t = text.Trim();
        if (length <= 0) return "This file is empty; there is no position to go to.";
        if (t.Length == 0) return "Enter a byte offset, percentage, or line number.";
        bool linePrefix = t.StartsWith("line", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith('L') || t.StartsWith('l') || t.StartsWith(':');
        if (linePrefix)
        {
            var number = t.StartsWith("line", StringComparison.OrdinalIgnoreCase) ? t[4..] : t[1..];
            if (!long.TryParse(number.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out long line) || line < 1)
                return "Enter a positive line number, such as L1200 or line 1200.";
            if (!canSeek) return "Line navigation needs a seekable source; this provider cannot scan to a line.";
            target = new GoToTarget(true, line);
            return null;
        }
        long offset;
        if (t.EndsWith('%'))
        {
            if (!double.TryParse(t[..^1].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double percent) ||
                !double.IsFinite(percent) || percent is < 0 or > 100)
                return "Enter a percentage from 0% to 100%.";
            double scaled = length * (percent / 100);
            offset = scaled >= length - 1 ? length - 1 : (long)Math.Floor(scaled);
        }
        else if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(t[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong hex) ||
                hex > long.MaxValue)
                return "Enter hexadecimal bytes after 0x, such as 0x1F00.";
            offset = (long)hex;
        }
        else if (!long.TryParse(t.Replace(" ", "", StringComparison.Ordinal),
                     NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out offset))
            return "Enter a decimal byte offset, such as 7936.";
        if (offset < 0 || offset >= length)
            return $"Enter an offset from 0 to {length - 1:N0} (0x{length - 1:X}).";
        target = new GoToTarget(false, offset);
        return null;
    }

    /// <summary>
    /// Far-away lines need counting from the start (or the nearest checkpoint): the scan runs in the background, shows
    /// its progress, and Esc stops it (plan §13.1).
    /// </summary>
    private async Task GoToLineAsync(long line)
    {
        if (_closing.IsCancellationRequested) return;
        if (!_reader.Source.CanSeek)
        {
            _status.Text = "Going to a line needs content that can be read at any position.";
            return;
        }
        if (_isHex) SetMode(false);
        if (_lines is null || !ReferenceEquals(_lines.Encoding, _text.Encoding)) _lines = new LineIndex(_reader.Source, _text.Encoding, _text.ContentStart);
        _lineCts?.Cancel();
        using var cts = _lineCts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        var index = _lines;
        var progress = new Progress<long>(bytes =>
        {
            if (!ct.IsCancellationRequested && ReferenceEquals(_lineCts, cts))
                _status.Text = $"Counting lines to {line:N0}… {bytes / (1024.0 * 1024):N0} MB read (Esc stops)";
        });
        try
        {
            var start = await Task.Run(() =>
            {
                long? result = null;
                _reader.WithSource(_ => result = index.FindLineStart(line, progress, ct));
                ct.ThrowIfCancellationRequested();
                return result;
            }, ct);
            if (_closing.IsCancellationRequested || !ReferenceEquals(_lineCts, cts)) return;
            if (start is { } offset)
            {
                _text.ScrollToOffset(offset);
                UpdateStatus();
                _status.Text = $"Line {line:N0}. " + _status.Text;
            }
            else
            {
                _status.Text = $"The file has only {index.TotalLines:N0} lines.";
            }
        }
        catch (Exception) when (_closing.IsCancellationRequested || !ReferenceEquals(_lineCts, cts))
        {
            return;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Going to the line was stopped.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "The file could not be read: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_lineCts, cts)) _lineCts = null;
        }
    }

    private Task CopyAsync() => CopyContentAsync(displayed: false, explicitVisible: false);

    private void UpdateCopyActions()
    {
        bool hex = _isHex && !_isInfo;
        var view = _isInfo ? _info : _text;
        _copySource.IsVisible = !_isPicture && !_isPage;
        _copyDisplayed.IsVisible = !_isPicture && !_isPage && !hex;
        _copySource.Content = hex ? (_hex.Selection.Length > 0 ? "Copy selected hex bytes" : "Copy cursor byte")
            : view.HasSelection ? "Copy source selection" : "Copy visible source text";
        if (view.SelectionEnded is { } ended && !hex && !_closing.IsCancellationRequested)
            _copyNotice.Show(ended);
    }

    internal async Task CopyContentAsync(bool displayed, bool explicitVisible, Avalonia.Input.Platform.IClipboard? targetClipboard = null)
    {
        if (_closing.IsCancellationRequested || _isPicture || _isPage || (targetClipboard ?? Clipboard) is not { } clipboard) return;
        _copyCts?.Cancel();
        using var cts = _copyCts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        bool hex = _isHex && !_isInfo;
        bool info = _isInfo;
        long sourceGeneration = _sourceGeneration;
        var view = info ? _info : _text;
        var scope = hex ? null : view.CaptureCopyScope(explicitVisible);
        var reader = hex ? _reader : scope?.Reader;
        var (start, length) = hex ? _hex.Selection : scope is not null ? (scope.Start, scope.Length) : (0L, 0L);
        if (hex && length == 0) length = _reader.Length > 0 ? 1 : 0;
        bool selected = hex || scope?.Selected == true;
        bool publicationStarted = false;
        bool Current() => !ct.IsCancellationRequested && ReferenceEquals(_copyCts, cts) && sourceGeneration == _sourceGeneration && info == _isInfo
            && hex == (_isHex && !_isInfo) && !_isPicture && !_isPage
            && (hex || scope is not null && ReferenceEquals(scope.Reader, view.Reader) && scope.Generation == view.SourceGeneration);
        try
        {
            if (reader is null)
            {
                _copyNotice.Show(view.SelectionEnded ?? "The current text is still loading. Wait for it to appear before copying.");
                return;
            }
            var representation = hex ? ContentCopyRepresentation.HexBytes : displayed ? ContentCopyRepresentation.DisplayedText : ContentCopyRepresentation.SourceText;
            _copyNotice.Show($"Reading the captured {(selected ? "selection" : "visible lines")}: {length:N0} source bytes from offset {start:N0}…", running: true);
            var revision = hex ? reader.Revision : scope!.Revision;
            var result = await Task.Run(() => ContentCopy.Read(reader, start, length, representation, scope?.Encoding,
                scope?.DisplayColumn ?? 0, revision, ct), ct);
            if (!Current()) return;
            if (result.CanPublish)
            {
                await _copyWrites.WaitAsync(ct);
                try
                {
                    if (!Current()) return;
                    publicationStarted = true;
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, result.Text);
                }
                finally { _copyWrites.Release(); }
            }
            if (Current())
            {
                string message = result.Describe(selected);
                _copyNotice.Show(message);
                _status.Text = message;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_copyCts, cts)) _copyNotice.Show(ContentCopy.CanceledMessage(publicationStarted));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            if (Current()) _copyNotice.Show("Copy could not finish: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_copyCts, cts))
            {
                if (ct.IsCancellationRequested && !_closing.IsCancellationRequested) _copyNotice.Show(ContentCopy.CanceledMessage(publicationStarted));
                _copyCts = null;
            }
        }
    }

    private void BuildChecksumPanel()
    {
        Avalonia.Automation.AutomationProperties.SetName(_checksumScope, "Checksum scope");
        Avalonia.Automation.AutomationProperties.SetName(_checksumResult, "Checksum results");
        if (OperatingSystem.IsLinux())
        {
            // Avalonia 12.1's AT-SPI Text interface returns this entry's fixed
            // accessible name instead of its value. Keep the complete, current
            // result in its description so Orca can inspect the actual digest.
            _checksumResult.TextChanged += (_, _) =>
                Avalonia.Automation.AutomationProperties.SetHelpText(_checksumResult, _checksumResult.Text ?? string.Empty);
        }
        var close = new Button { Content = "Close checksums" };
        close.Click += (_, _) =>
        {
            _checksumCts?.Cancel();
            _checksumCts = null;
            _checksumPanel.IsVisible = false;
            FocusContent();
        };
        _computeChecksum.Click += async (_, _) => await ChecksumAsync();
        _copyChecksum.Click += async (_, _) => await CopyChecksumAsync();
        _stopChecksum.Click += (_, _) => _checksumCts?.Cancel();
        _checksumScope.SelectionChanged += (_, _) =>
        {
            _checksumCts?.Cancel();
            _checksumCts = null;
            _stopChecksum.IsVisible = false;
            _copyChecksum.IsEnabled = false;
            _checksumResult.Text = "Choose the scope, then Compute. Results stay here until you change the scope or close this panel.";
        };
        var controls = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { _checksumScope, _computeChecksum, _stopChecksum, _copyChecksum, close } };
        var sourceName = new TextBlock { Text = Formatters.SafeName(_displayName), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2 };
        ToolTip.SetTip(sourceName, Formatters.SafeName(_displayName));
        _checksumPanel.Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Checksums", FontWeight = FontWeight.SemiBold },
                sourceName,
                controls,
                _checksumResult,
            },
        };
    }

    /// <summary>The panel captures the selected range and exposes whole file as an explicit alternative.</summary>
    private void ShowChecksum()
    {
        if (_closing.IsCancellationRequested) return;
        _checksumCts?.Cancel();
        _checksumCts = null;
        _stopChecksum.IsVisible = false;
        _checksumSelectedRange = _isHex && !_isInfo && _hex.Selection.Length > 0 ? _hex.Selection : null;
        _checksumWholeLength = _reader.Length;
        _checksumScopeGeneration = _sourceGeneration;
        _checksumScope.Items.Clear();
        _checksumScope.Items.Add($"Whole file · {_checksumWholeLength:N0} bytes");
        if (_checksumSelectedRange is { } selected)
            _checksumScope.Items.Add($"Selected range · {selected.Length:N0} bytes at 0x{selected.Start:X}");
        _checksumScope.SelectedIndex = _checksumSelectedRange is null ? 0 : 1;
        _checksumPanel.IsVisible = true;
        _checksumScope.Focus();
    }

    internal string ChecksumResultText => _checksumResult.Text ?? "";

    private Task CopyChecksumAsync() => CopyChecksumToAsync(Clipboard);

    internal async Task CopyChecksumToAsync(Avalonia.Input.Platform.IClipboard? clipboard)
    {
        if (!_copyChecksum.IsEnabled || clipboard is null || _closing.IsCancellationRequested) return;
        string text = ChecksumResultText;
        try
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            if (!_closing.IsCancellationRequested) _status.Text = "Checksum results copied.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            if (!_closing.IsCancellationRequested) _status.Text = "Checksum results could not be copied: " + ex.Message;
        }
    }

    private async Task ChecksumAsync()
    {
        if (_closing.IsCancellationRequested) return;
        if (!_checksumPanel.IsVisible) ShowChecksum();
        if (_checksumScopeGeneration != _sourceGeneration)
        {
            _checksumResult.Text = "The file changed. Open Checksum again to review the current scope before computing.";
            return;
        }
        _checksumCts?.Cancel();
        using var cts = _checksumCts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        long generation = _sourceGeneration;
        long expectedSourceLength = _checksumWholeLength;
        bool selected = _checksumScope.SelectedIndex == 1 && _checksumSelectedRange is not null;
        var (start, length) = selected ? _checksumSelectedRange!.Value : (0L, _checksumWholeLength);
        string scope = selected ? $"selected range 0x{start:X}–0x{start + length - 1:X}" : "whole file";
        _copyChecksum.IsEnabled = false;
        _stopChecksum.IsVisible = true;
        _checksumResult.Text = $"Computing {scope}, {length:N0} bytes… (Esc stops)";
        _status.Text = "Computing checksums…";
        try
        {
            var result = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                ContentRevision? before = null;
                long initialLength = -1;
                _reader.WithSource(source => { before = source.GetRevision(); initialLength = source.Length; });
                ct.ThrowIfCancellationRequested();
                if (initialLength < 0)
                    throw new IOException("The source length is unavailable. Its checksum scope cannot be established.");
                if (initialLength != expectedSourceLength)
                    throw new IOException("The source length changed after the scope was reviewed. Reopen the viewer and review the current scope.");
                if (before is not null && before != _reader.Revision)
                    throw new IOException("The source changed before the checksum started. Reopen the viewer to read its current content.");
                using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                uint crc = 0;
                var buffer = new byte[1024 * 1024];
                long pos = start, end = checked(start + length);
                var progressClock = System.Diagnostics.Stopwatch.StartNew();
                while (pos < end)
                {
                    int n = _reader.Read(pos, buffer.AsSpan(0, (int)Math.Min(buffer.Length, end - pos)), ct);
                    if (n <= 0) break;
                    sha.AppendData(buffer, 0, n);
                    crc = Crc32.Append(crc, buffer.AsSpan(0, n));
                    pos += n;
                    if (progressClock.ElapsedMilliseconds >= 200)
                    {
                        long read = pos - start;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!ct.IsCancellationRequested && ReferenceEquals(_checksumCts, cts) && generation == _sourceGeneration)
                                _checksumResult.Text = $"Computing {scope}, {read:N0} of {length:N0} bytes read… (Esc stops)";
                        });
                        progressClock.Restart();
                    }
                }
                ct.ThrowIfCancellationRequested();
                ContentRevision? after = null;
                long finalLength = -1;
                _reader.WithSource(source => { after = source.GetRevision(); finalLength = source.Length; });
                ct.ThrowIfCancellationRequested();
                if (before != after || initialLength != finalLength)
                    throw new IOException("The source changed while computing the checksum. No checksum is published for the mixed content; Refresh (F5) and compute again.");
                return (Sha: Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), Crc: crc, Read: pos - start, Revision: before);
            }, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_checksumCts, cts) || generation != _sourceGeneration) return;
            var message = $"Source: {Formatters.SafeName(_displayName)}\n{scope}, {result.Read:N0} of {length:N0} bytes read\nSHA-256: {result.Sha}\nCRC-32: {result.Crc:x8}";
            if (result.Read < length)
                message = "INCOMPLETE: content ended early; these checksums cover only the bytes read.\n" + message;
            if (_reader.ReadError is { } error)
                message = "READ ERROR: " + error + "\n" + message;
            message += "\n" + (result.Revision is { } revision
                ? $"Revision evidence unchanged during read: {revision.Length:N0} bytes"
                    + (revision.ModifiedTicks > 0 && revision.ModifiedTicks <= DateTime.MaxValue.Ticks
                        ? $", modified {new DateTime(revision.ModifiedTicks, DateTimeKind.Utc):u}." : " (no modification time supplied).")
                : "Revision unavailable: source consistency could not be verified.");
            _checksumResult.Text = message;
            _copyChecksum.IsEnabled = true;
            _status.Text = result.Read < length || _reader.ReadError is not null
                ? "Checksum is incomplete. The visible result states the bytes read and the warning."
                : $"Checksums ready for {scope}; full results are shown below.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_checksumCts, cts) && generation == _sourceGeneration)
            {
                _checksumResult.Text = "Checksum canceled. No complete result was produced.";
                _status.Text = "Checksum canceled.";
            }
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_checksumCts, cts) && generation == _sourceGeneration)
            {
                _checksumResult.Text = "Checksum could not finish: " + ex.Message;
                _status.Text = "Checksum could not finish; details are shown below.";
            }
        }
        finally
        {
            if (ReferenceEquals(_checksumCts, cts)) { _checksumCts = null; _stopChecksum.IsVisible = false; }
        }
    }

}

/// <summary>Opens viewer windows for items of any provider that exposes content.</summary>
public static class ViewerLauncher
{
    /// <summary>Admits initial metadata off the UI thread; transfers the source to a shown window or disposes it on failure.</summary>
    public static async Task Open(AppServices services, ItemRef item, IContentSource source, bool hex)
    {
        PagedReader? reader = null;
        ViewerWindow? window = null;
        bool transferred = false;
        try
        {
            var name = item.FileSystemPath ?? services.Providers.Display(item.Parent).TrimEnd('\\', '/') + "/" + item.Name;
            string deviceKey = services.Providers.For(item.Parent).GetDeviceKey(item.Parent);
            reader = await services.Io.Run(deviceKey, IoPriority.Interactive, _ => new PagedReader(source, services.Io, deviceKey));
            if (services.Io.IsStopped) throw new OperationCanceledException("Viewer admission stopped.");
            var provider = services.Providers.For(item.Parent);
            Task<IContentSource?> Reopen(CancellationToken ct) => ReopenProviderAsync(services, provider, item, deviceKey, ct);
            window = new ViewerWindow(services, source, name, hex, deviceKey, reader, Reopen);
            window.Show();
            transferred = true;
        }
        finally
        {
            if (!transferred)
            {
                window?.Close();
                if (reader is not null) reader.Dispose();
                else source.Dispose();
            }
        }
    }

    /// <summary>
    /// Keep ownership of a late provider result even when cancellation or scheduler shutdown completes
    /// the admission task before its synchronous source open returns.
    /// </summary>
    private static Task<IContentSource?> ReopenProviderAsync(AppServices services, FileCat.Core.Resources.ResourceProvider provider,
        ItemRef item, string deviceKey, CancellationToken ct) =>
        ReopenSourceAsync(services, deviceKey, () => provider.OpenContent(item), ct);

    internal static async Task<IContentSource?> ReopenSourceAsync(AppServices services, string deviceKey,
        Func<IContentSource?> open, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<IContentSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => result.TrySetCanceled(ct));
        var admission = services.Io.Run(deviceKey, IoPriority.Interactive, _ =>
        {
            IContentSource? source = null;
            try
            {
                source = open();
                if (!result.TrySetResult(source))
                {
                    try { source?.Dispose(); }
                    catch (Exception) { } // the late source has no remaining owner or UI to report to
                }
            }
            catch (Exception ex)
            {
                if (!result.TrySetException(ex) && source is not null)
                {
                    try { source.Dispose(); }
                    catch (Exception) { }
                }
            }
        });
        _ = admission.ContinueWith(task =>
        {
            if (task.IsCanceled) result.TrySetCanceled();
            else if (task.IsFaulted) result.TrySetException(task.Exception!.InnerException ?? task.Exception);
        }, TaskScheduler.Default);
        IContentSource? opened = await result.Task;
        if (ct.IsCancellationRequested)
        {
            opened?.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        return opened;
    }

    public static async Task OpenPath(AppServices services, string path)
    {
        try
        {
            var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
            string deviceKey = services.Providers.For(item.Parent).GetDeviceKey(item.Parent);
            var source = await services.Io.Run(deviceKey, IoPriority.Interactive, _ => new FileContentSource(path));
            await Open(services, item, source, hex: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // The caller shows its own errors; a missing file simply opens nothing.
        }
    }
}
