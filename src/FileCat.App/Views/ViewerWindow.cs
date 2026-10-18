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
    private readonly AppServices _services;
    private readonly string _deviceKey;
    private readonly PagedReader _reader;
    private readonly TextViewer _text = new();
    private readonly HexView _hex = new();
    private readonly TextBlock _status = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _encodingInfo = new() { Classes = { "small", "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { PlaceholderText = "Find (Ctrl+F)", MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = "Aa", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _hexSearch = new() { Content = "Hex bytes", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _stopSearch = new() { Content = "Stop search", IsVisible = false };
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
    private readonly ToggleButton _actualSize = new() { Content = "Actual size", IsVisible = false };
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

    /// <summary>Open viewer windows, oldest first: the window list in the command palette (plan §4.1).</summary>
    public static IReadOnlyList<ViewerWindow> OpenWindows => s_open;

    /// <summary>The viewed item's full path or provider path.</summary>
    public string DisplayName => _displayName;

    public ViewerWindow(AppServices services, IContentSource source, string displayName, bool hex, string? deviceKey = null)
        : this(services, source, displayName, hex, deviceKey, null) { }

    internal ViewerWindow(AppServices services, IContentSource source, string displayName, bool hex, string? deviceKey, PagedReader? preparedReader)
    {
        _services = services;
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
        SizeChanged += (_, args) =>
        {
            if (!_windowOpened || WindowState != WindowState.Normal ||
                !ValidViewerDimension(args.NewSize.Width) || !ValidViewerDimension(args.NewSize.Height)) return;
            _lastNormalWindowSize = args.NewSize;
            if (Math.Abs(args.NewSize.Width - _initialWindowSize.Width) > 1 ||
                Math.Abs(args.NewSize.Height - _initialWindowSize.Height) > 1) _userSizedWindow = true;
        };
        Closed += (_, _) => SaveViewerSize();
        TextDecoding.EnsureCodePages();
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }

        foreach (var (name, _) in TextDecoding.Choices) _encodingBox.Items.Add(name);
        Avalonia.Automation.AutomationProperties.SetName(_search, "Find in file");
        Avalonia.Automation.AutomationProperties.SetName(_encodingBox, "Text encoding");
        _wrap.IsChecked = services.Settings.ViewerWrap;
        _text.Wrap = services.Settings.ViewerWrap;
        _text.SetReader(_reader, _guess.Encoding, 0);
        _hex.SetReader(_reader);

        var toolbar = new WrapPanel { Margin = new Thickness(8, 4), ItemSpacing = 8, LineSpacing = 4 };
        toolbar.Children.Add(_modePage);
        toolbar.Children.Add(_modePicture);
        ToolTip.SetTip(_modePage, "The page as a browser draws it, with its scripts off and nothing fetched from the web");
        Avalonia.Automation.AutomationProperties.SetName(_pageView, "Web page");
        toolbar.Children.Add(_modeText);
        toolbar.Children.Add(_modeHex);
        toolbar.Children.Add(_modeInfo);
        toolbar.Children.Add(_actualSize);
        ToolTip.SetTip(_modePicture, "The picture (Ctrl+P)");
        ToolTip.SetTip(_actualSize, "Actual size or fitted to the window (Z). The mouse wheel zooms around the pointer and dragging moves the picture; + and − zoom, 0 fits, 1 is actual size, a double click switches.");
        Avalonia.Automation.AutomationProperties.SetName(_picture, "Picture");
        ToolTip.SetTip(_modeInfo, "Structure of executables and images: headers, sections, imports, version, EXIF (Ctrl+I)");
        Avalonia.Automation.AutomationProperties.SetName(_info, "File information");
        ShowInfoText("");
        var encodingLabel = new TextBlock { Text = "Encoding:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var goTo = new Button { Content = "Go to…" };
        goTo.Click += async (_, _) => await GoToAsync();
        _textOnly.AddRange([encodingLabel, _encodingBox, _wrap, _follow, _search, _matchCase, _hexSearch, goTo, _encodingInfo]);
        foreach (var part in _textOnly.Take(8)) toolbar.Children.Add(part);
        ToolTip.SetTip(_stopSearch, "Stop the current search (Esc). Other navigation remains available.");
        _stopSearch.Click += (_, _) => _searchCts?.Cancel();
        toolbar.Children.Add(_stopSearch);
        var checksum = new Button { Content = "Checksum…" };
        checksum.Click += (_, _) => ShowChecksum();
        toolbar.Children.Add(checksum);
        _copySource.Click += async (_, _) => await CopyContentAsync(displayed: false, explicitVisible: true);
        _copyDisplayed.Click += async (_, _) => await CopyContentAsync(displayed: true, explicitVisible: true);
        ToolTip.SetTip(_copySource, "Copy the selected source characters, or the captured visible lines. Original tabs and logical line endings are preserved. Ctrl+C; bounded to 1 MiB of source bytes.");
        ToolTip.SetTip(_copyDisplayed, "Copy the same text range using expanded tabs and visible control symbols. Ctrl+Shift+C. Wrapping and bounded segments do not add line endings.");
        toolbar.Children.Add(_copySource);
        toolbar.Children.Add(_copyDisplayed);
        _copyNotice.StopRequested += () => _copyCts?.Cancel();
        if (source.LocalPath is not null && OperatingSystem.IsWindows())
        {
            var editBytes = new Button { Content = "Edit bytes…" };
            ToolTip.SetTip(editBytes, "Open this file in the hex editor at the current offset (F6)");
            editBytes.Click += (_, _) => EditBytes();
            toolbar.Children.Add(editBytes);
        }

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
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        if (lostNotice is not null) root.Children.Add(lostNotice);
        root.Children.Add(statusBar);
        BuildChecksumPanel();
        DockPanel.SetDock(_checksumPanel, Dock.Bottom);
        root.Children.Add(_checksumPanel);
        DockPanel.SetDock(_copyNotice, Dock.Bottom);
        root.Children.Add(_copyNotice);
        root.Children.Add(content);
        Content = ThemeLayers.Over(root, content);

        _modeText.Click += (_, _) => SetMode(false);
        _modeHex.Click += (_, _) => SetMode(true);
        _modeInfo.Click += async (_, _) => await ShowInfoAsync();
        _modePicture.Click += (_, _) => ShowPicture();
        _modePage.Click += (_, _) => ShowPage();
        _pageView.Changed += () => { if (_isPage) UpdateStatus(); };
        _pageView.Failed += why =>
        {
            // Without an engine the page's source is what can be shown.
            _pageUnavailable = why;
            if (!_isPage) return;
            SetMode(false);
            _status.Text = why + " Its source is shown instead.";
        };
        _pageView.KeyRequested = OnPageKey;
        _actualSize.IsCheckedChanged += (_, _) =>
        {
            _picture.Fit = _actualSize.IsChecked != true;
            Dispatcher.UIThread.Post(UpdateStatus, DispatcherPriority.Background);
        };
        _picture.SizeChanged += (_, _) => { if (_isPicture) UpdateStatus(); };
        // Zooming with the wheel or the keys leaves the fitted view: the button says so too.
        _picture.ZoomChanged += () =>
        {
            _actualSize.IsChecked = !_picture.Fit;
            if (_isPicture) UpdateStatus();
        };
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
                FocusContent();
            }
        };
        // TextChanged is routed later by Avalonia. PropertyChanged invalidates synchronously, so a
        // queued notification for the text just searched cannot cancel the new Enter/F3 request.
        _search.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) InvalidateSearch(); };
        _matchCase.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidateSearch(); };
        _hexSearch.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) InvalidateSearch(); };
        _text.PositionChanged += () => SearchPositionChanged(_text);
        _info.PositionChanged += () => SearchPositionChanged(_info);
        _hex.CursorMoved += () => SearchPositionChanged(_hex);
        _text.ContentChanged += UpdateCopyActions;
        _info.ContentChanged += UpdateCopyActions;
        _hex.CursorMoved += UpdateCopyActions;
        // Damage found while reading (an archive member, part way) shows in the status line as soon as it is known.
        _reader.PageLoaded += () =>
        {
            if (_reader.ReadError is not null) Dispatcher.UIThread.Post(UpdateStatus);
        };
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
            FocusContent();
        };
        Closed += (_, _) =>
        {
            _changeTimer.Stop();
            _searchCts?.Cancel();
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
        _isInfo = false;
        _picture.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = _info.IsVisible = false;
        _modePicture.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = _modeInfo.IsChecked = false;
        _actualSize.IsVisible = true;
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
        _pictureGeneration = _sourceGeneration;
        _picture.ShowMessage("Decoding the picture…");
        try
        {
            var picture = await PictureDecoder.DecodeAsync(_reader, _services.Io, _deviceKey, PictureSide, _closing.Token);
            if (_closing.IsCancellationRequested)
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
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _picture.ShowMessage($"This picture cannot be shown: {ex.Message}. Text and Hex (F4) show its bytes; Info (Ctrl+I) reads its headers.");
        }
        UpdateStatus();
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
        _pageView.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = _info.IsVisible = false;
        _modePage.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = _modeInfo.IsChecked = false;
        foreach (var part in _textOnly) part.IsVisible = part == _search || part == _matchCase;
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
        Action? act = (key, modifiers) switch
        {
            (Key.Escape, KeyModifiers.None) when _searchCts is not null => () => _searchCts?.Cancel(),
            (Key.Escape or Key.F10, KeyModifiers.None) => Close,
            (Key.F4, KeyModifiers.None) => () => SetMode(false),
            (Key.F, KeyModifiers.Control) or (Key.F7, KeyModifiers.None) => () => { _search.Focus(); _search.SelectAll(); },
            (Key.F3, KeyModifiers.None) => () => _ = FindAsync(forward: true),
            (Key.F3, KeyModifiers.Shift) => () => _ = FindAsync(forward: false),
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
        _actualSize.IsVisible = false;
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
        _info.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = false;
        _modeInfo.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = false;
        // Find works in the report; the text-only settings do not apply to it.
        foreach (var part in _textOnly) part.IsVisible = part == _search || part == _matchCase || part == _wrap;
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
        _infoGeneration = _sourceGeneration;
        ShowInfoText("Reading the file's structure…");
        try
        {
            var text = await Task.Run(() =>
            {
                string result = "";
                _reader.WithSource(source =>
                {
                    _closing.Token.ThrowIfCancellationRequested();
                    var report = FileCat.Core.Inspect.Inspectors.Inspect(new InspectionSource(source, _closing.Token), _closing.Token);
                    _closing.Token.ThrowIfCancellationRequested();
                    result = report?.ToText() ?? $"No structure inspector for this kind of file.\n\nSize: {source.Length:N0} bytes\nContent: {(_guess.LooksBinary ? "binary" : "text, " + _guess.Encoding.WebName + " (" + _guess.Evidence + ")")}";
                });
                return result;
            }, _closing.Token);
            if (_closing.IsCancellationRequested) return;
            ShowInfoText(text);
        }
        catch (Exception) when (_closing.IsCancellationRequested)
        {
            return; // the window closed while the structure was being read
        }
        catch (Exception ex)
        {
            // Inspectors report damage as warnings; anything else is shown here rather than ending the application.
            ShowInfoText("The file's structure could not be read: " + ex.Message);
        }
        UpdateStatus();
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
        _hex.IsVisible = hex;
        _text.IsVisible = !hex;
        _modeHex.IsChecked = hex;
        _modeText.IsChecked = !hex;
        _wrap.IsEnabled = !hex;
        _wrap.IsChecked = _text.Wrap;
        if (hex) _hex.GoTo(_text.TopOffset);
        else _text.ScrollToOffset(_hex.CursorOffset);
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

    private void UpdateStatus()
    {
        if (_isPicture)
        {
            _status.Text = _picture.Picture is { } p
                ? $"{p.Format} · {p.Width:N0} × {p.Height:N0} pixels · {(_picture.Fit ? "fitted, " : "")}{_picture.Zoom * 100:0}%" +
                  (p.Frames > 1 ? $" · animated, {p.Frames:N0} frames: the first is shown" : "") +
                  (p.Incomplete ? " · the file ends early: the rest of the picture is blank" : "") +
                  $" · {Formatters.ExactSize(_reader.Length)}"
                : Formatters.ExactSize(_reader.Length);
            if (_pictureGeneration != _sourceGeneration)
                _status.Text = "The file changed; reopen the viewer to refresh this picture. " + _status.Text;
            return;
        }
        if (_isInfo)
        {
            _status.Text = $"The file's structure: {_infoLineCount.ToString("N0", CultureInfo.CurrentCulture)} lines · Ctrl+F finds in it · F4 shows the bytes · {Formatters.ExactSize(_reader.Length)}";
            if (_infoGeneration != _sourceGeneration)
                _status.Text = "The file changed; reopen the viewer to refresh this structure report. " + _status.Text;
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
                _status.Text = "The file changed; reopen the viewer to refresh this page. " + _status.Text;
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
        if (_closing.IsCancellationRequested || _checkingChanges) return;
        _checkingChanges = true;
        try
        {
            bool changed = await _services.Io.Run(_deviceKey, IoPriority.Background,
                ct => _reader.Refresh(ct), _closing.Token);
            if (_closing.IsCancellationRequested || !changed) return;
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

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var focused = FocusManager?.GetFocusedElement();
        if (e.Key == Key.Escape && (_searchCts is not null || _checksumCts is not null || _copyCts is not null))
        {
            if (_searchCts is not null) _searchCts.Cancel();
            else if (_checksumCts is not null) _checksumCts.Cancel();
            else _copyCts?.Cancel();
            e.Handled = true;
            return;
        }
        if (focused is TextBox && e.Key is not (Key.F3 or Key.F4 or Key.F8 or Key.F10)) return;
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
                _actualSize.IsChecked = _actualSize.IsChecked != true;
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
                _search.Focus();
                _search.SelectAll();
                break;
            case Key.F3:
                await FindAsync(!shift);
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
                _text.FontSize += 1;
                _hex.FontSize += 1;
                _info.FontSize += 1;
                break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                _text.FontSize -= 1;
                _hex.FontSize -= 1;
                _info.FontSize -= 1;
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
        }
        catch (OperationCanceledException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_searchCts, cts))
                _status.Text = "Search canceled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_searchCts, cts))
                _status.Text = "Search could not finish: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) { _searchCts = null; _stopSearch.IsVisible = false; }
        }
    }

    private async Task FindPageAsync(string pattern, bool forward)
    {
        _searchCts?.Cancel();
        _searchCts = null;
        if (_pageGeneration != _sourceGeneration)
        {
            _pageSearchStatus = "The file changed; reopen the viewer to search this page.";
            UpdateStatus();
            return;
        }
        var page = _htmlPage;
        long generation = _sourceGeneration;
        using var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _stopSearch.IsVisible = true;
        _pageSearchStatus = $"Searching page {(forward ? "forward" : "backward")}… (Esc stops)";
        UpdateStatus();
        try
        {
            bool? found = await _pageView.FindAsync(pattern, _matchCase.IsChecked == true, forward, cts.Token);
            if (_closing.IsCancellationRequested || !ReferenceEquals(_searchCts, cts) || !_isPage ||
                !ReferenceEquals(page, _htmlPage) || generation != _sourceGeneration) return;
            _pageSearchStatus = found switch
            {
                true => $"Found \"{pattern}\" in the page.",
                false => $"\"{pattern}\" was not found in this page.",
                null => _pageView.PageLoaded ? "Page search is unavailable in this browser engine." : "The page is still loading; try search again.",
            };
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
            if (ReferenceEquals(_searchCts, cts)) { _searchCts = null; _stopSearch.IsVisible = false; }
        }
    }

    private void InvalidateSearch()
    {
        if (_isPage) _pageView.ClearFind();
        _searchCts?.Cancel();
        _searchCts = null;
        _stopSearch.IsVisible = false;
        _lastHit = -1;
        _lastSearch = null;
        _pageSearchStatus = null;
    }

    private void SearchPositionChanged(Control source)
    {
        // Inactive controls also adjust their scroll bars during layout/reader setup. Those are not
        // navigation in the searched surface and cannot cancel its request or replace its status.
        Control active = _isInfo ? _info : _isHex ? _hex : _text;
        if (_isPicture || _isPage || source != active) return;
        if (!_revealingSearch) InvalidateSearch();
        UpdateStatus();
    }

    private async Task GoToAsync()
    {
        var box = new TextBox { PlaceholderText = "0x1F00, 7936, 50%, or L1200 (line)" };
        var dialog = new Window
        {
            Title = "Go to",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children = { new TextBlock { Text = "Offset (hex with 0x, decimal), percentage, or line (L1200):" }, box },
            },
        };
        string? result = null;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = box.Text; dialog.Close(); }
            else if (e.Key == Key.Escape) dialog.Close();
        };
        dialog.Opened += (_, _) => box.Focus();
        await dialog.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(result)) return;
        var t = result.Trim();
        if (TryParseLine(t, out long line))
        {
            await GoToLineAsync(line);
            return;
        }
        long len = _reader.Length;
        long offset;
        if (t.EndsWith('%') && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.CurrentCulture, out var pct)) offset = (long)(len * Math.Clamp(pct, 0, 100) / 100);
        else if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexVal)) offset = hexVal;
        else if (long.TryParse(t.Replace(" ", "").Replace(" ", ""), NumberStyles.Integer, CultureInfo.CurrentCulture, out var dec)) offset = dec;
        else
        {
            _status.Text = $"\"{t}\" is not an offset.";
            return;
        }
        if (_isHex) _hex.GoTo(offset);
        else _text.ScrollToOffset(offset);
        UpdateStatus();
    }

    /// <summary>"L1200", "line 1200", or ":1200".</summary>
    private static bool TryParseLine(string text, out long line)
    {
        line = 0;
        var t = text.Trim();
        if (t.StartsWith("line", StringComparison.OrdinalIgnoreCase)) t = t[4..];
        else if (t.StartsWith('L') || t.StartsWith('l') || t.StartsWith(':')) t = t[1..];
        else return false;
        return long.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out line) && line > 0;
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
                    throw new IOException("The source changed while computing the checksum. No checksum is published for the mixed content; reopen the viewer and compute again.");
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
            window = new ViewerWindow(services, source, name, hex, deviceKey, reader);
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
