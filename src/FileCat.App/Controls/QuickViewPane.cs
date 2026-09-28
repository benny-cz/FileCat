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
    private const double PictureSize = 256;
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
    private bool _binary, _pictureShown;
    private readonly DispatcherTimer _debounce;
    private TabViewModel? _source;
    private PagedReader? _reader;
    private string? _shownKey;

    public QuickViewPane()
    {
        Avalonia.Automation.AutomationProperties.SetName(this, "Quick view");
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(140), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            _ = LoadAsync();
        });
        var header = new StackPanel { Margin = new Thickness(8, 4), Spacing = 1, Children = { _title, _info } };
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
        if (_source is not null) _source.Listing.Changed -= OnSourceChanged;
        _source = source;
        _shownKey = null;
        if (source is not null)
        {
            source.Listing.Changed += OnSourceChanged;
            _debounce.Stop();
            _debounce.Start();
        }
        else
        {
            Close();
        }
    }

    private void OnSourceChanged(object? sender, ListingChange change)
    {
        if ((change & (ListingChange.Focus | ListingChange.Reset)) == 0) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Close()
    {
        _picture.IsVisible = false;
        _picture.Source = null;
        _pictureKey = null;
        _binary = _pictureShown = false;
        _text.SetReader(null, System.Text.Encoding.UTF8, 0);
        _hex.SetReader(null);
        _reader?.Dispose();
        _reader = null;
    }

    private async Task LoadAsync()
    {
        var tab = _source;
        if (tab?.Location is null || !tab.Listing.TryGetFocused(out var e)) return;
        var item = e.Kind == EntryKind.Parent ? null : tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        var key = item is null ? "parent" : item.ToString() + "|" + e.Modified + "|" + e.Size;
        if (key == _shownKey) return;
        _shownKey = key;
        Close();
        _title.Text = e.Kind == EntryKind.Parent ? ".." : Formatters.SafeName(e.Name);
        if (e.IsContainer || item is null)
        {
            _info.Text = e.Kind == EntryKind.Parent ? "Parent folder" : e.Has(EntryFlags.SizeComputed) ? $"Folder · {Formatters.SizeWithUnit(e.Size)}" : "Folder · Space in the source panel computes its size";
            ShowMessage("Quick view shows file contents. Ctrl+Q closes it; the panel's location stays the copy destination.");
            return;
        }
        _info.Text = $"{Formatters.ExactSize(e.Size)} · {Formatters.Date(e.Modified)}";
        if (e.Has(EntryFlags.Offline))
        {
            ShowMessage("This is a cloud placeholder; quick view does not download it. Press F3 to open it explicitly.");
            return;
        }
        var services = tab.Services;
        if (services.AllowedShellPictures is { } pictures && item.FileSystemPath is { } picturePath)
            _ = LoadPictureAsync(pictures, picturePath, e.Modified, (FileAttributes)e.Attributes, key);
        try
        {
            var result = await Task.Run<(PagedReader? Reader, EncodingGuess? Guess)>(() =>
            {
                var source = services.Providers.For(item.Parent).OpenContent(item);
                if (source is null) return (null, null);
                var reader = new PagedReader(source, maxPages: 64);
                var head = new byte[8192];
                int n = reader.Read(0, head);
                return (reader, TextDecoding.Detect(head.AsSpan(0, n)));
            });
            if (key != _shownKey)
            {
                result.Reader?.Dispose();
                return;
            }
            if (result.Reader is null)
            {
                ShowMessage("This item has no viewable content (for example an encrypted archive entry).");
                return;
            }
            _reader = result.Reader;
            _message.IsVisible = false;
            if (result.Guess!.LooksBinary)
            {
                _hex.SetReader(_reader);
                _hex.IsVisible = true;
                _text.IsVisible = false;
                _info.Text += " · binary, shown as hex";
                _binary = true;
                ShowPictureIfReady();
            }
            else
            {
                _text.SetReader(_reader, result.Guess.Encoding, result.Guess.PreambleLength);
                _text.IsVisible = true;
                _hex.IsVisible = false;
                _info.Text += $" · {result.Guess.Encoding.WebName}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowMessage("Cannot preview: " + Core.Jobs.ErrorText.Describe(ex));
        }
    }

    /// <summary>The Shell's thumbnail, asked of the helper while the content loads; shown only for binary content.</summary>
    private async Task LoadPictureAsync(FileCat.Platform.Windows.Shell.ShellPreviews pictures, string path, long modified, FileAttributes attributes, string key)
    {
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int size = (int)Math.Min(FileCat.Platform.Windows.Shell.ShellHostProtocol.MaxPixels, Math.Round(PictureSize * scale));
        var image = await pictures.GetAsync(FileCat.Platform.Windows.Shell.ShellImageKind.Thumbnail, path, modified, attributes, size, CancellationToken.None);
        if (image is null || key != _shownKey) return;
        _picture.Source = ShellBitmaps.ToBitmap(image, scale);
        _pictureKey = key;
        ShowPictureIfReady();
    }

    private void ShowPictureIfReady()
    {
        if (_pictureShown || !_binary || _pictureKey is null || _pictureKey != _shownKey) return;
        _pictureShown = true;
        _picture.IsVisible = true;
        _hex.IsVisible = false;
        _info.Text = _info.Text?.Replace(" · binary, shown as hex", string.Empty) + " · Shell thumbnail (F3 shows the bytes)";
    }

    private void ShowMessage(string text)
    {
        _picture.IsVisible = false;
        _message.Text = text;
        _message.IsVisible = true;
        _text.IsVisible = false;
        _hex.IsVisible = false;
    }
}
