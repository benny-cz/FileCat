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

namespace FileCat.App.Views;

/// <summary>
/// F3 viewer in its own top-level window (plan §4.1, §16.1): one viewer with text and hex modes (F4 toggles),
/// encoding chosen with visible evidence (F8 cycles), search (Ctrl+F, F3/Shift+F3), go to (Ctrl+G), range
/// checksums, and follow mode for growing logs. Opened with full sharing, so other programs keep working.
/// </summary>
public sealed class ViewerWindow : Window
{
    private readonly AppServices _services;
    private readonly PagedReader _reader;
    private readonly TextViewer _text = new();
    private readonly HexView _hex = new();
    private readonly TextBlock _status = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _encodingInfo = new() { Classes = { "small", "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _search = new() { PlaceholderText = "Find (Ctrl+F)", MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = "Aa", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _hexSearch = new() { Content = "Hex bytes", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _encodingBox = new() { MinWidth = 130 };
    private readonly ToggleButton _modeText = new() { Content = "Text" };
    private readonly ToggleButton _modeHex = new() { Content = "Hex" };
    private readonly ToggleButton _modeInfo = new() { Content = "Info" };
    private readonly TextBox _info = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, IsVisible = false,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
    };
    private readonly IContentSource _source;
    private bool _isInfo, _infoLoaded;
    private readonly CheckBox _wrap = new() { Content = "Wrap", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _follow = new() { Content = "Follow end", VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _changeTimer;
    private readonly string _displayName;
    private EncodingGuess _guess = new(new UTF8Encoding(false), 0, "not yet examined", false);
    private bool _isHex;
    private CancellationTokenSource? _searchCts;
    private long _lastHit = -1;
    private int _lastHitLength;
    private LineIndex? _lines;
    private CancellationTokenSource? _lineCts;
    private readonly CancellationTokenSource _closing = new();
    private static readonly List<ViewerWindow> s_open = [];

    /// <summary>Open viewer windows, oldest first: the window list in the command palette (plan §4.1).</summary>
    public static IReadOnlyList<ViewerWindow> OpenWindows => s_open;

    /// <summary>The viewed item's full path or provider path.</summary>
    public string DisplayName => _displayName;

    public ViewerWindow(AppServices services, IContentSource source, string displayName, bool hex)
    {
        _services = services;
        _displayName = displayName;
        _source = source;
        s_open.Add(this);
        Closed += (_, _) => s_open.Remove(this);
        _reader = new PagedReader(source);
        Title = $"{Path.GetFileName(displayName.TrimEnd('\\', '/'))} — FileCat Viewer";
        Width = 980;
        Height = 700;
        MinWidth = 480;
        MinHeight = 300;
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
        toolbar.Children.Add(_modeText);
        toolbar.Children.Add(_modeHex);
        toolbar.Children.Add(_modeInfo);
        ToolTip.SetTip(_modeInfo, "Structure of executables and images: headers, sections, imports, version, EXIF (Ctrl+I)");
        Avalonia.Automation.AutomationProperties.SetName(_info, "File information");
        toolbar.Children.Add(new TextBlock { Text = "Encoding:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        toolbar.Children.Add(_encodingBox);
        toolbar.Children.Add(_wrap);
        toolbar.Children.Add(_follow);
        toolbar.Children.Add(_search);
        toolbar.Children.Add(_matchCase);
        toolbar.Children.Add(_hexSearch);
        var goTo = new Button { Content = "Go to…" };
        goTo.Click += async (_, _) => await GoToAsync();
        var checksum = new Button { Content = "Checksum…" };
        checksum.Click += async (_, _) => await ChecksumAsync();
        toolbar.Children.Add(goTo);
        toolbar.Children.Add(checksum);
        if (source.LocalPath is not null && OperatingSystem.IsWindows())
        {
            var editBytes = new Button { Content = "Edit bytes…" };
            ToolTip.SetTip(editBytes, "Open this file in the hex editor at the current offset (F6)");
            editBytes.Click += (_, _) => EditBytes();
            toolbar.Children.Add(editBytes);
        }

        // Recovered content with lost parts: say which bytes are zeros only because their data is gone (plan §17.1).
        Border? lostNotice = null;
        if (source is IPartialContent { MissingRanges.Count: > 0 } partial)
        {
            lostNotice = new Border
            {
                Padding = new Thickness(8, 4),
                Child = new TextBlock { Text = PartialContent.Describe(partial.MissingRanges, source.Length), Classes = { "warning" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            };
            DockPanel.SetDock(lostNotice, Dock.Top);
        }
        var statusBar = new Border { Classes = { "status" }, Child = new DockPanel { Children = { _encodingInfo, _status } } };
        DockPanel.SetDock(_encodingInfo, Dock.Right);
        var content = new Panel { Children = { _text, _hex, _info } };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        if (lostNotice is not null) root.Children.Add(lostNotice);
        root.Children.Add(statusBar);
        root.Children.Add(content);
        Content = root;

        _modeText.Click += (_, _) => SetMode(false);
        _modeHex.Click += (_, _) => SetMode(true);
        _modeInfo.Click += async (_, _) => await ShowInfoAsync();
        _wrap.IsCheckedChanged += (_, _) =>
        {
            _text.Wrap = _wrap.IsChecked == true;
            _services.Settings.ViewerWrap = _text.Wrap;
        };
        _encodingBox.SelectionChanged += (_, _) =>
        {
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
        _text.PositionChanged += UpdateStatus;
        _hex.CursorMoved += UpdateStatus;
        // Damage found while reading (an archive member, part way) shows in the status line as soon as it is known.
        _reader.PageLoaded += () =>
        {
            if (_reader.ReadError is not null) Dispatcher.UIThread.Post(UpdateStatus);
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        _changeTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => CheckForChanges());
        Opened += async (_, _) =>
        {
            SetMode(hex);
            await DetectEncodingAsync(forceHexIfBinary: !hex);
            _changeTimer.Start();
            FocusContent();
        };
        Closed += (_, _) =>
        {
            _changeTimer.Stop();
            _searchCts?.Cancel();
            _closing.Cancel();
            _reader.Dispose();
        };
    }

    private async Task DetectEncodingAsync(bool forceHexIfBinary)
    {
        var prefix = await Task.Run(() =>
        {
            var buf = new byte[64 * 1024];
            int n = _reader.Read(0, buf);
            return buf[..n];
        });
        _guess = TextDecoding.Detect(prefix);
        int index = Array.FindIndex(TextDecoding.Choices.ToArray(), c => c.Get().WebName == _guess.Encoding.WebName);
        _text.SetEncoding(_guess.Encoding, _guess.PreambleLength);
        _encodingBox.SelectedIndex = index >= 0 ? index : 0;
        _encodingInfo.Text = $"{_guess.Encoding.WebName}: {_guess.Evidence}";
        if (_guess.LooksBinary && forceHexIfBinary) SetMode(true);
        UpdateStatus();
    }

    /// <summary>The Info mode: what a static inspector reads from the file's structure (plan §16.1), computed once.</summary>
    public async Task ShowInfoAsync()
    {
        _isInfo = true;
        _info.IsVisible = true;
        _text.IsVisible = _hex.IsVisible = false;
        _modeInfo.IsChecked = true;
        _modeText.IsChecked = _modeHex.IsChecked = false;
        if (!_infoLoaded)
        {
            _infoLoaded = true;
            _info.Text = "Reading the file's structure…";
            try
            {
                var report = await Task.Run(() => FileCat.Core.Inspect.Inspectors.Inspect(_source, _closing.Token), _closing.Token);
                _info.Text = report?.ToText() ?? $"No structure inspector for this kind of file.\n\nSize: {_source.Length:N0} bytes\nContent: {(_guess.LooksBinary ? "binary" : "text, " + _guess.Encoding.WebName + " (" + _guess.Evidence + ")")}";
            }
            catch (Exception) when (_closing.IsCancellationRequested)
            {
                return; // the window closed while the structure was being read
            }
            catch (Exception ex)
            {
                // Inspectors report damage as warnings; anything else is shown here rather than ending the application.
                _info.Text = "The file's structure could not be read: " + ex.Message;
            }
        }
        _info.Focus();
    }

    public string InfoText => _info.Text ?? "";

    private void SetMode(bool hex)
    {
        _isInfo = false;
        _info.IsVisible = false;
        _modeInfo.IsChecked = false;
        _isHex = hex;
        _hex.IsVisible = hex;
        _text.IsVisible = !hex;
        _modeHex.IsChecked = hex;
        _modeText.IsChecked = !hex;
        _wrap.IsEnabled = !hex;
        if (hex) _hex.GoTo(_text.TopOffset);
        else _text.ScrollToOffset(_hex.CursorOffset);
        FocusContent();
        UpdateStatus();
    }

    private void FocusContent()
    {
        if (_isHex) _hex.Focus();
        else _text.Focus();
    }

    private void UpdateStatus()
    {
        long len = _reader.Length;
        long pos = _isHex ? _hex.CursorOffset : _text.TopOffset;
        double pct = len > 0 ? 100.0 * pos / len : 0;
        var sel = _hex.Selection;
        _status.Text = (_reader.ReadError is { } error ? "Not all of the content could be read: " + error + " · " : string.Empty) +
                       $"{Formatters.ExactSize(len)} · offset 0x{pos:X} ({pos.ToString("N0", CultureInfo.CurrentCulture)}) · {pct:0.#}%" +
                       (_isHex && sel.Length > 1 ? $" · selected {sel.Length.ToString("N0", CultureInfo.CurrentCulture)} bytes" : string.Empty);
    }

    private void CheckForChanges()
    {
        if (!_reader.Refresh()) return;
        _text.InvalidateVisual();
        _hex.InvalidateVisual();
        if (_follow.IsChecked == true)
        {
            if (_isHex) _hex.GoTo(Math.Max(0, _reader.Length - 1));
            else _text.GoToEnd();
        }
        _status.Text = "The file changed on disk; showing its current content. " + _status.Text;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var focused = FocusManager?.GetFocusedElement();
        // The Info text keeps its own selection and copy keys; Esc, F4 and F10 still leave it.
        if (focused == _info && e.Key is not (Key.Escape or Key.F4 or Key.F10)) return;
        if (focused is TextBox && focused != _info && e.Key is not (Key.F3 or Key.F4 or Key.F8 or Key.F10)) return;
        switch (e.Key)
        {
            case Key.Escape when _lineCts is not null:
                // Esc first stops a running "go to line" scan.
                _lineCts.Cancel();
                break;
            case Key.Escape:
            case Key.F10:
                Close();
                break;
            case Key.F4:
                SetMode(_isInfo ? false : !_isHex);
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
            case Key.F2 when !_isHex:
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
            case Key.G when ctrl:
                await GoToAsync();
                break;
            case Key.C when ctrl:
                await CopyAsync();
                break;
            case Key.K when ctrl:
                await ChecksumAsync();
                break;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                _text.FontSize += 1;
                _hex.FontSize += 1;
                break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                _text.FontSize -= 1;
                _hex.FontSize -= 1;
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
        var pattern = _search.Text ?? string.Empty;
        if (pattern.Length == 0)
        {
            _search.Focus();
            return;
        }
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        long from = _lastHit >= 0 ? (forward ? _lastHit + Math.Max(1, _lastHitLength) : _lastHit) : _isHex ? _hex.CursorOffset : _text.TopOffset;
        bool hexMode = _hexSearch.IsChecked == true;
        byte[]? bytes = hexMode ? ContentSearch.ParseHex(pattern) : null;
        if (hexMode && bytes is null)
        {
            _status.Text = "Hex search expects bytes such as \"4D 5A 90\".";
            return;
        }
        var enc = _text.Encoding;
        bool matchCase = _matchCase.IsChecked == true;
        _status.Text = "Searching…";
        long found;
        try
        {
            found = await Task.Run(() =>
            {
                if (bytes is not null)
                    return forward ? ContentSearch.FindBytes(_reader, from, bytes, cts.Token) : ContentSearch.FindBytesBackward(_reader, from, bytes, cts.Token);
                if (!forward)
                {
                    // Backward text search: search forward from a window before the position and keep the last hit.
                    long start = Math.Max(0, from - 4 * 1024 * 1024), last = -1, p = start;
                    while (true)
                    {
                        long hit = ContentSearch.FindText(_reader, enc, p, pattern, matchCase, cts.Token);
                        if (hit < 0 || hit >= from) break;
                        last = hit;
                        p = hit + 1;
                    }
                    return last;
                }
                return ContentSearch.FindText(_reader, enc, from, pattern, matchCase, cts.Token);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Search canceled.";
            return;
        }
        if (found < 0)
        {
            _status.Text = $"\"{pattern}\" was not found {(forward ? "after" : "before")} this position.";
            return;
        }
        _lastHit = found;
        _lastHitLength = bytes?.Length ?? Math.Max(1, enc.GetByteCount(pattern));
        _text.SetHighlight(bytes is null ? pattern : null, matchCase);
        if (_isHex) _hex.GoTo(found, select: true, _lastHitLength);
        else _text.ScrollToOffset(found);
        _hex.GoTo(found, select: true, _lastHitLength);
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
        if (!_reader.Source.CanSeek)
        {
            _status.Text = "Going to a line needs content that can be read at any position.";
            return;
        }
        if (_isHex) SetMode(false);
        if (_lines is null || !ReferenceEquals(_lines.Encoding, _text.Encoding)) _lines = new LineIndex(_reader.Source, _text.Encoding, _text.ContentStart);
        _lineCts?.Cancel();
        var cts = _lineCts = new CancellationTokenSource();
        var index = _lines;
        var progress = new Progress<long>(bytes => _status.Text = $"Counting lines to {line:N0}… {bytes / (1024.0 * 1024):N0} MB read (Esc stops)");
        try
        {
            var start = await Task.Run(() => index.FindLineStart(line, progress, cts.Token), cts.Token);
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

    private async Task CopyAsync()
    {
        if (Clipboard is null) return;
        if (_isHex)
        {
            var bytes = await Task.Run(() => _hex.ReadSelection(1024 * 1024));
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, Convert.ToHexString(bytes));
            _status.Text = $"Copied {bytes.Length:N0} bytes as hex.";
        }
        else
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, _text.GetSelectedOrVisibleText());
            _status.Text = "Copied text.";
        }
    }

    private async Task ChecksumAsync()
    {
        var (start, length) = _isHex && _hex.Selection.Length > 1 ? _hex.Selection : (0L, _reader.Length);
        _status.Text = "Computing checksums…";
        var result = await Task.Run(() =>
        {
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            uint crc = 0;
            var buf = new byte[1024 * 1024];
            long pos = start, end = start + length;
            while (pos < end)
            {
                int n = _reader.Read(pos, buf.AsSpan(0, (int)Math.Min(buf.Length, end - pos)));
                if (n <= 0) break;
                sha.AppendData(buf, 0, n);
                crc = Crc32.Append(crc, buf.AsSpan(0, n));
                pos += n;
            }
            return (Sha: Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), Crc: crc, Read: pos - start);
        });
        var scope = length == _reader.Length ? "whole file" : $"range 0x{start:X}–0x{start + length - 1:X}";
        var message = $"{scope}, {result.Read:N0} bytes\nSHA-256: {result.Sha}\nCRC-32: {result.Crc:x8}";
        if (result.Read < length) message += "\nWarning: the content ended early; the checksum covers only the bytes read.";
        _status.Text = $"SHA-256 {result.Sha[..16]}… · CRC-32 {result.Crc:x8} ({scope})";
        if (Clipboard is not null) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, message);
        _status.Text += " · copied to the clipboard";
    }
}

/// <summary>Opens viewer windows for items of any provider that exposes content.</summary>
public static class ViewerLauncher
{
    /// <summary>Shows content already opened (off the UI thread) for the item; the window disposes it.</summary>
    public static void Open(AppServices services, ItemRef item, IContentSource source, bool hex)
    {
        var name = item.FileSystemPath ?? services.Providers.Display(item.Parent).TrimEnd('\\', '/') + "/" + item.Name;
        new ViewerWindow(services, source, name, hex).Show();
    }

    public static void OpenPath(AppServices services, string path)
    {
        try
        {
            new ViewerWindow(services, new FileContentSource(path), path, hex: false).Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller shows its own errors; a missing file simply opens nothing.
        }
    }
}
