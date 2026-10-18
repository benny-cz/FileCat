using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Content;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using FileCat.App.Views;

namespace FileCat.App.Controls;

/// <summary>
/// Quick view (Ctrl+Q): previews the source panel's focused item inside the target panel. The target keeps
/// its header and location, which remains the F5/F6 destination (plan §4.1). Binary files on local drives show the
/// Shell's thumbnail when one exists; it comes from the restricted helper (TV-16), never from a handler in FileCat.
/// </summary>
public sealed class QuickViewPane : Border
{
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _info = new() { Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextViewer _text = new();
    private readonly HexView _hex = new();
    private readonly TextBlock _message = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), IsVisible = false };
    private readonly TextBlock _notice = new() { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _openFull = new() { Content = "Open full viewer (F3)", IsVisible = false };
    private readonly Button _cancelPreview = new() { Content = "Cancel preview", IsVisible = false };
    private readonly Button _retryPreview = new() { Content = "Retry preview", IsVisible = false };
    private const double MinimumPictureSide = 256;
    private const double MaximumPictureSide = 1024;
    private static readonly TimeSpan LoadingDelay = TimeSpan.FromMilliseconds(180);
    private enum PreviewState { None, Loading, Text, Hex, Picture, Message }
    private PreviewState _state;
    private readonly Image _picture = new()
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.DownOnly,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(12),
        IsVisible = false,
    };
    private string? _pictureKey;
    private bool _binary, _pictureShown, _isPictureFile;
    private string? _pictureCaption; // what the shown picture is: the Shell's thumbnail, or FileCat's own decoding
    private bool _shellUnavailable, _decodeStarted;
    private PreviewRequest? _request;
    private CancellationTokenSource? _openFullCts;
    private ItemRef? _currentItem;

    /// <summary>Pictures larger than this are not decoded just for a glance (F3 still shows them).</summary>
    private const long MaxDecodedBytes = 64L * 1024 * 1024;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _loadingDelay;
    private TabViewModel? _source;
    private PagedReader? _reader;
    private string? _shownKey;
    private string? _folderDemandKey;

    // The scheduler may cancel its task before a synchronous device call returns. Keep ownership separate from
    // that task: late readers are disposed on the worker, and a completed reader is transferred exactly once.
    private sealed class PreviewRequest : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly object _lock = new();
        private bool _disposed;
        private PagedReader? _reader;
        public CancellationToken Token { get; }
        public EncodingGuess? Guess { get; private set; }
        public bool Picture { get; private set; }

        public PreviewRequest() => Token = _cts.Token;

        public bool Publish(PagedReader reader, EncodingGuess guess, bool picture)
        {
            lock (_lock)
            {
                if (_disposed) return false;
                _reader = reader;
                Guess = guess;
                Picture = picture;
                return true;
            }
        }

        public PagedReader? TakeReader()
        {
            lock (_lock)
            {
                var reader = _reader;
                _reader = null;
                return reader;
            }
        }

        public void Dispose()
        {
            PagedReader? reader;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                reader = _reader;
                _reader = null;
            }
            _cts.Cancel();
            _cts.Dispose();
            reader?.Dispose();
        }
    }

    public QuickViewPane()
    {
        Avalonia.Automation.AutomationProperties.SetName(this, "Quick view");
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(140), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            _ = LoadAsync();
        });
        _loadingDelay = new DispatcherTimer(LoadingDelay, DispatcherPriority.Background, (_, _) =>
        {
            _loadingDelay!.Stop();
            if (_request is { } request && IsCurrent(request))
            {
                _message.Text = $"Loading preview of \"{_title.Text}\"…";
                SetState(PreviewState.Loading);
            }
        });
        Avalonia.Automation.AutomationProperties.SetName(_openFull, "Open focused file in full viewer");
        Avalonia.Automation.AutomationProperties.SetName(_cancelPreview, "Cancel quick preview");
        Avalonia.Automation.AutomationProperties.SetName(_retryPreview, "Retry quick preview");
        _openFull.Click += async (_, _) => await OpenFullAsync();
        _cancelPreview.Click += (_, _) => CancelPreview();
        _retryPreview.Click += (_, _) => { _shownKey = null; _ = LoadAsync(); };
        _openFull.Margin = _cancelPreview.Margin = _retryPreview.Margin = new Thickness(0, 0, 6, 4);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal,
            Children = { _openFull, _cancelPreview, _retryPreview } };
        var header = new StackPanel { Margin = new Thickness(8, 4), Spacing = 4,
            Children = { _title, _info, _notice, actions } };
        var content = new Panel { Children = { _text, _hex, _picture, _message } };
        Avalonia.Automation.AutomationProperties.SetName(_picture, "Thumbnail");
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(content);
        Child = dock;
        _text.Focusable = false;
        _hex.Focusable = false;
    }

    public void Attach(TabViewModel? source)
    {
        _debounce.Stop();
        Close();
        if (_source is not null) _source.Listing.Changed -= OnSourceChanged;
        _source = source;
        _folderDemandKey = FocusedFolderKey();
        if (source is not null)
        {
            source.Listing.Changed += OnSourceChanged;
            _debounce.Start();
        }
    }

    private void OnSourceChanged(object? sender, ListingChange change)
    {
        bool focusChanged = (change & (ListingChange.Focus | ListingChange.Reset)) != 0;
        if (!focusChanged && (change & ListingChange.Rows) == 0) return;
        var folderKey = FocusedFolderKey();
        if (folderKey is not null)
        {
            // Counts in other rows and unchanged refresh results must neither clear the caption nor postpone
            // its first display. Keep the pending folder demand as well as the already displayed one.
            if (folderKey == _folderDemandKey) return;
        }
        else if (!focusChanged) return; // File previews keep their existing row-update policy.
        _folderDemandKey = folderKey;
        Close(); // abandoned demand ends immediately, before the next focus settles through the debounce
        _debounce.Stop();
        _debounce.Start();
    }

    private string? FocusedFolderKey()
    {
        if (_source is not { Location: not null } tab || !tab.Listing.TryGetFocused(out var entry) || !entry.IsContainer) return null;
        var item = entry.Kind == EntryKind.Parent ? null : tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        return PreviewKey(item, entry);
    }

    private static string PreviewKey(ItemRef? item, in EntryData entry) => item is null ? "parent"
        : item.ToString() + "|" + entry.Modified + "|" + entry.Size
          + (entry.IsContainer ? "|" + (entry.Flags & (EntryFlags.SizeComputed | EntryFlags.SizeLowerBound)) : "");

    private void Close()
    {
        _loadingDelay.Stop();
        _openFullCts?.Cancel();
        _openFullCts = null;
        _openFull.IsEnabled = true;
        _request?.Dispose();
        _request = null;
        _currentItem = null;
        _shownKey = null;
        var bitmap = _picture.Source as IDisposable;
        _picture.IsVisible = false;
        _picture.Source = null;
        bitmap?.Dispose();
        _pictureKey = null;
        _pictureCaption = null;
        _shellUnavailable = _decodeStarted = false;
        _binary = _pictureShown = _isPictureFile = false;
        _text.SetReader(null, System.Text.Encoding.UTF8, 0);
        _hex.SetReader(null);
        _reader?.Dispose();
        _reader = null;
        _notice.Text = null;
        _notice.IsVisible = false;
        _openFull.IsVisible = false;
        SetState(PreviewState.None);
        _title.Text = _info.Text = null;
    }

    private async Task LoadAsync()
    {
        var tab = _source;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var e)) return;
        var item = e.Kind == EntryKind.Parent ? null : tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var key = PreviewKey(item, e);
        if (key == _shownKey) return;
        Close();
        _shownKey = key;
        _currentItem = item;
        _openFull.IsVisible = item is not null && !e.IsContainer;
        _title.Text = e.Kind == EntryKind.Parent ? ".." : Formatters.SafeName(e.Name);
        if (e.IsContainer || item is null)
        {
            _info.Text = e.Kind == EntryKind.Parent ? "Parent folder" : e.Has(EntryFlags.SizeComputed)
                ? e.Has(EntryFlags.SizeLowerBound) ? $"Folder · at least {Formatters.SizeWithUnit(e.Size)} (lower bound)" : $"Folder · {Formatters.SizeWithUnit(e.Size)}"
                : "Folder · Space in the source panel computes its size";
            ShowMessage("Quick view shows file contents. Ctrl+Q closes it; the panel's location stays the copy destination.");
            return;
        }
        _info.Text = $"{Formatters.ExactSize(e.Size)} · {Formatters.Date(e)}";
        if (e.Has(EntryFlags.Offline))
        {
            ShowMessage("This is a cloud placeholder; quick view does not download it. Press F3 to open it explicitly.");
            return;
        }
        var services = tab.Services;
        var request = _request = new PreviewRequest();
        _loadingDelay.Start();
        bool shellPicture = services.AllowedShellPictures is not null && item.FileSystemPath is not null && e.Size > 0;
        if (shellPicture && services.AllowedShellPictures is { } pictures && item.FileSystemPath is { } picturePath)
            _ = LoadPictureAsync(pictures, picturePath, e.Modified, (FileAttributes)e.Attributes, key, request);
        try
        {
            var provider = services.Providers.For(item.Parent);
            await services.Io.Run(provider.GetDeviceKey(item.Parent), IoPriority.Interactive, ct =>
            {
                IContentSource? source = null;
                PagedReader? reader = null;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    source = provider.OpenContent(item);
                    ct.ThrowIfCancellationRequested();
                    if (source is null) return;
                    reader = new PagedReader(source, maxPages: 64);
                    source = null; // ownership has moved to the reader
                    ct.ThrowIfCancellationRequested();
                    var head = new byte[8192];
                    int n = reader.Read(0, head);
                    ct.ThrowIfCancellationRequested();
                    if (reader.ReadError is { } error) throw new IOException(error);
                    if (request.Publish(reader, TextDecoding.Detect(head.AsSpan(0, n)), PictureDecoder.Recognize(head.AsSpan(0, n)) is not null))
                        reader = null;
                }
                finally
                {
                    reader?.Dispose();
                    source?.Dispose();
                }
            }, request.Token);
            if (!IsCurrent(request)) return;
            var result = request.TakeReader();
            if (result is null)
            {
                _request = null;
                request.Dispose();
                ShowMessage("This provider cannot supply a preview for this item (for example an encrypted archive entry). Open full viewer (F3) for the provider's explanation.");
                return;
            }
            _reader = result;
            _loadingDelay.Stop();
            if (result.Length == 0)
            {
                ShowMessage("This file is empty. Open full viewer (F3) for its details.");
                return;
            }
            _isPictureFile = request.Picture;
            // No Shell thumbnail coming (Linux, macOS, archives, servers, or Shell pictures turned off): FileCat decodes
            // the picture itself, in its worker process.
            if (request.Picture && (!shellPicture || _shellUnavailable))
                StartDecodedPicture(request, provider.GetDeviceKey(item.Parent), key);
            if (request.Guess!.LooksBinary)
            {
                _hex.SetReader(_reader);
                SetState(PreviewState.Hex);
                _info.Text += " · binary, shown as hex";
                _binary = true;
                if (request.Picture && _reader.Length > MaxDecodedBytes && _pictureKey is null)
                    ShowNotice($"Automatic picture preview is limited to 64 MiB; this file is {Formatters.SizeWithUnit(_reader.Length)}. Hex bytes are shown. Open full viewer (F3) for more detail.");
                else if (!request.Picture && _shellUnavailable)
                    ShowNotice("No thumbnail is available for this file. Hex bytes are shown; Open full viewer (F3) for more detail.");
                ShowPictureIfReady();
            }
            else
            {
                _text.SetReader(_reader, request.Guess.Encoding, request.Guess.PreambleLength);
                SetState(PreviewState.Text);
                _info.Text += $" · {request.Guess.Encoding.WebName}";
            }
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or TimeoutException)
        {
            if (IsCurrent(request))
            {
                _request = null;
                request.Dispose();
                ShowMessage("Cannot preview: " + Core.Jobs.ErrorText.Describe(ex));
            }
        }
    }

    private bool IsCurrent(PreviewRequest request) => ReferenceEquals(request, _request) && !request.Token.IsCancellationRequested;

    /// <summary>The Shell's thumbnail, asked of the helper while the content loads; shown only for binary content.</summary>
    private async Task LoadPictureAsync(FileCat.Platform.Windows.Shell.ShellPreviews pictures, string path, long modified, FileAttributes attributes, string key, PreviewRequest request)
    {
        try
        {
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int size = Math.Min(FileCat.Platform.Windows.Shell.ShellHostProtocol.MaxPixels, PreviewSide());
            // Asked once for what is on screen: a helper that was still starting (or had just died) is given a second go.
            var image = await pictures.GetForDisplayAsync(FileCat.Platform.Windows.Shell.ShellImageKind.Thumbnail, path, modified, attributes, size, request.Token);
            if (!IsCurrent(request)) return;
            if (image is null) { ShellUnavailable(request, key); return; }
            _picture.Source = ShellBitmaps.ToBitmap(image, scale);
            _pictureKey = key;
            ShowPictureIfReady();
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (IsCurrent(request)) ShellUnavailable(request, key);
        }
    }

    private int PreviewSide()
    {
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double paneSide = Math.Max(Bounds.Width - 24, Bounds.Height - 96);
        return Math.Clamp((int)Math.Round(Math.Clamp(paneSide, MinimumPictureSide, MaximumPictureSide) * scale), 16, 1024);
    }

    private void ShellUnavailable(PreviewRequest request, string key)
    {
        _shellUnavailable = true;
        if (request.Picture && _currentItem is { } item)
            StartDecodedPicture(request, _source!.Services.Providers.For(item.Parent).GetDeviceKey(item.Parent), key);
        else if (_binary) ShowNotice("No thumbnail is available for this file. Hex bytes are shown; Open full viewer (F3) for more detail.");
    }

    private void StartDecodedPicture(PreviewRequest request, string deviceKey, string key)
    {
        if (_decodeStarted || !IsCurrent(request) || _reader is null || _reader.Length > MaxDecodedBytes) return;
        _decodeStarted = true;
        _ = LoadDecodedPictureAsync(_reader, _source!.Services.Io, deviceKey, key, request);
    }

    /// <summary>A picture FileCat decodes itself (<see cref="PictureDecoder"/>), for the glance quick view gives.</summary>
    private async Task LoadDecodedPictureAsync(PagedReader reader, DeviceIoScheduler io, string deviceKey, string key, PreviewRequest request)
    {
        try
        {
            var picture = await PictureDecoder.DecodeAsync(reader, io, deviceKey, PreviewSide(), request.Token);
            if (!IsCurrent(request))
            {
                picture.Bitmap.Dispose();
                return;
            }
            _picture.Source = picture.Bitmap;
            _pictureKey = key;
            _pictureCaption = $" · {picture.Format}, {picture.Width:N0} × {picture.Height:N0} (F3 shows the picture)";
            ShowPictureIfReady();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException or OperationCanceledException or ObjectDisposedException or
                                   InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (IsCurrent(request) && ex is not OperationCanceledException)
                ShowNotice("Picture preview failed: " + Core.Jobs.ErrorText.Describe(ex) + " Hex bytes are shown; Open full viewer (F3) for more detail.");
        }
    }

    private void ShowPictureIfReady()
    {
        if (_pictureShown || !_binary || _pictureKey is null || _pictureKey != _shownKey) return;
        _pictureShown = true;
        SetState(PreviewState.Picture);
        _notice.IsVisible = false;
        _info.Text = _info.Text?.Replace(" · binary, shown as hex", string.Empty) +
                     (_pictureCaption ?? (_isPictureFile ? " · Shell thumbnail (F3 shows the picture)" : " · Shell thumbnail (F3 shows the bytes)"));
    }

    private void ShowMessage(string text)
    {
        _loadingDelay.Stop();
        _notice.Text = null;
        _notice.IsVisible = false;
        _message.Text = text;
        SetState(PreviewState.Message);
    }

    private void ShowNotice(string text)
    {
        _notice.Text = text;
        _notice.IsVisible = true;
    }

    private void SetState(PreviewState state)
    {
        _state = state;
        _text.IsVisible = state == PreviewState.Text;
        _hex.IsVisible = state == PreviewState.Hex;
        _picture.IsVisible = state == PreviewState.Picture;
        _message.IsVisible = state is PreviewState.Loading or PreviewState.Message;
        _cancelPreview.IsVisible = state == PreviewState.Loading;
        _retryPreview.IsVisible = state == PreviewState.Message && _currentItem is not null && _request is null;
    }

    private void CancelPreview()
    {
        if (_state != PreviewState.Loading) return;
        _request?.Dispose();
        _request = null;
        var bitmap = _picture.Source as IDisposable;
        _picture.Source = null;
        bitmap?.Dispose();
        _pictureKey = null;
        ShowMessage("Preview canceled. Retry it or choose another file.");
    }

    private async Task OpenFullAsync()
    {
        if (_currentItem is not { } item || _source is not { } tab || _openFullCts is not null) return;
        var cts = _openFullCts = new CancellationTokenSource();
        _openFull.IsEnabled = false;
        try
        {
            var provider = tab.Services.Providers.For(item.Parent);
            string deviceKey = provider.GetDeviceKey(item.Parent);
            var source = await ViewerLauncher.ReopenSourceAsync(tab.Services, deviceKey,
                () => provider.OpenContent(item), cts.Token);
            if (source is null) throw new IOException("This item has no viewable content");
            if (cts.IsCancellationRequested)
            {
                source.Dispose();
                cts.Token.ThrowIfCancellationRequested();
            }
            await ViewerLauncher.Open(tab.Services, item, source, hex: false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            if (!cts.IsCancellationRequested && _currentItem == item)
                ShowNotice("Cannot open full viewer: " + Core.Jobs.ErrorText.Describe(ex));
        }
        finally
        {
            if (ReferenceEquals(_openFullCts, cts))
            {
                _openFullCts = null;
                _openFull.IsEnabled = true;
            }
            cts.Dispose();
        }
    }
}
