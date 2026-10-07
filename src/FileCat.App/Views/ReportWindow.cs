using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

/// <summary>
/// A report FileCat writes about an item, such as its file-system record (D-56): read-only text that opens at once and
/// fills in when ready, with Find (Ctrl+F, F3), Wrap (F2), Copy (Ctrl+C: the selection, or everything), Save as, and
/// Refresh (F5) to read it again. None of the viewer's modes, encodings, or checksums: there is no file here.
/// </summary>
public sealed class ReportWindow : Window
{
    private readonly Func<CancellationToken, Task<string>> _produce;
    private readonly string _subject;
    private readonly string _kind;
    private readonly TextViewer _view = new() { Wrap = false, ReportStyle = true };
    private readonly TextBox _search = new() { PlaceholderText = "Find (Ctrl+F)", MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = "Aa", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _wrap = new() { Content = "Wrap", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _refresh = new() { Content = "Refresh" };
    private readonly TextBlock _status = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private readonly CancellationTokenSource _closing = new();
    private CancellationTokenSource? _reading;
    private CancellationTokenSource? _searching;
    private PagedReader? _reader;
    private string _text = "";
    private long _lastHit = -1;
    private int _lastHitLength;
    private DateTime _readAt;

    /// <param name="title">What the report is ("File-system record").</param>
    /// <param name="subject">What it is about (a path), shown in the window's title and status line.</param>
    /// <param name="produce">Writes the report; run off the UI thread, again on Refresh.</param>
    public ReportWindow(string title, string subject, Func<CancellationToken, Task<string>> produce)
    {
        _produce = produce;
        _subject = subject;
        string name = Path.GetFileName(subject.TrimEnd('\\', '/'));
        Title = $"{(name.Length > 0 ? name : subject)} · {title} — FileCat";
        _kind = title;
        Width = 1000;
        Height = 760;
        MinWidth = 480;
        MinHeight = 300;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }

        Avalonia.Automation.AutomationProperties.SetName(_view, title);
        Avalonia.Automation.AutomationProperties.SetName(_search, "Find in the report");
        ToolTip.SetTip(_refresh, "Read it again (F5)");
        ToolTip.SetTip(_wrap, "Wrap long lines (F2)");
        ToolTip.SetTip(_matchCase, "Match case");
        var copy = new Button { Content = "Copy all" };
        ToolTip.SetTip(copy, "Copy the whole report as text (Ctrl+C copies a selection)");
        var save = new Button { Content = "Save as…" };
        ToolTip.SetTip(save, "Save the report as a text file");
        var toolbar = new WrapPanel { Margin = new Thickness(8, 4), ItemSpacing = 8, LineSpacing = 4 };
        foreach (var part in new Control[] { _refresh, _search, _matchCase, _wrap, copy, save }) toolbar.Children.Add(part);
        var statusBar = new Border { Classes = { "status" }, Child = _status };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        var surface = new Border { Child = _view };
        root.Children.Add(surface);
        Content = ThemeLayers.Over(root, surface);

        _refresh.Click += async (_, _) => await ReadAsync();
        copy.Click += async (_, _) => await CopyAsync(all: true);
        save.Click += async (_, _) => await SaveAsync();
        _wrap.IsCheckedChanged += (_, _) => _view.Wrap = _wrap.IsChecked == true;
        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await FindAsync((e.KeyModifiers & KeyModifiers.Shift) == 0);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _view.Focus();
            }
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        ThemeManager.ThemeChanged += OnThemeChanged;
        Opened += async (_, _) =>
        {
            _view.Focus();
            await ReadAsync();
        };
        Closed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _closing.Cancel();
            _reading?.Cancel();
            _searching?.Cancel();
            _reader?.Dispose();
        };
    }

    /// <summary>The report's text as shown (tests).</summary>
    public string Text => _text;

    internal string StatusText => _status.Text ?? "";

    /// <summary>Where the report is scrolled to, in bytes of its text (tests).</summary>
    internal long TopOffset => _view.TopOffset;

    /// <summary>The reading in progress, if any (tests wait for it).</summary>
    internal Task? Reading { get; private set; }

    private void OnThemeChanged() => _view.ResolveBrushes();

    private Task ReadAsync() => Reading = ReadCoreAsync();

    private async Task ReadCoreAsync()
    {
        if (_closing.IsCancellationRequested) return;
        _reading?.Cancel();
        _searching?.Cancel();
        using var cts = _reading = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        _refresh.IsEnabled = false;
        if (_text.Length == 0) Show("Reading…");
        _status.Text = $"Reading {_subject}…";
        try
        {
            string text;
            try
            {
                text = await Task.Run(() => _produce(ct), ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return; // closed, or read again
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or System.ComponentModel.Win32Exception)
            {
                text = $"It could not be read: {ex.Message}";
            }
            if (ct.IsCancellationRequested || _closing.IsCancellationRequested || !ReferenceEquals(_reading, cts)) return;
            _readAt = DateTime.Now;
            long top = _view.TopOffset;
            Show(text);
            // Refresh keeps the place in the report.
            if (top > 0 && top < Encoding.UTF8.GetByteCount(text)) _view.ScrollToOffset(top);
            UpdateStatus();
        }
        finally
        {
            if (ReferenceEquals(_reading, cts))
            {
                _reading = null;
                if (!_closing.IsCancellationRequested) _refresh.IsEnabled = true;
            }
        }
    }

    private void Show(string text)
    {
        _searching?.Cancel();
        _text = text;
        var old = _reader;
        _reader = new PagedReader(new MemoryContentSource("Report", Encoding.UTF8.GetBytes(text)));
        _view.SetReader(_reader, new UTF8Encoding(false), 0);
        // Its pages count against the shared content budget until it is disposed.
        old?.Dispose();
        _lastHit = -1;
    }

    private void UpdateStatus()
    {
        int lines = _text.Count(c => c == '\n');
        _status.Text = $"{_subject} · read at {_readAt.ToString("T", CultureInfo.CurrentCulture)} · {lines.ToString("N0", CultureInfo.CurrentCulture)} lines · F5 reads it again";
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (FocusManager?.GetFocusedElement() is TextBox && e.Key is not (Key.F3 or Key.F5 or Key.F10)) return;
        switch (e.Key)
        {
            case Key.Escape:
            case Key.F10:
                Close();
                break;
            case Key.F5:
            case Key.R when ctrl:
                await ReadAsync();
                break;
            case Key.F when ctrl:
            case Key.F7:
                _search.Focus();
                _search.SelectAll();
                break;
            case Key.F3:
                await FindAsync(!shift);
                break;
            case Key.F2:
                _wrap.IsChecked = _wrap.IsChecked != true;
                break;
            case Key.C when ctrl:
                await CopyAsync(all: false);
                break;
            case Key.S when ctrl:
                await SaveAsync();
                break;
            case Key.OemPlus when ctrl:
            case Key.Add when ctrl:
                _view.FontSize += 1;
                break;
            case Key.OemMinus when ctrl:
            case Key.Subtract when ctrl:
                _view.FontSize -= 1;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private async Task FindAsync(bool forward)
    {
        _searching?.Cancel();
        if (_closing.IsCancellationRequested) return;
        string pattern = _search.Text ?? "";
        if (pattern.Length == 0 || _reader is not { } reader)
        {
            _search.Focus();
            return;
        }
        using var cts = _searching = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        var ct = cts.Token;
        var encoding = new UTF8Encoding(false);
        bool matchCase = _matchCase.IsChecked == true;
        long from = _lastHit >= 0 ? (forward ? _lastHit + Math.Max(1, _lastHitLength) : _lastHit) : _view.TopOffset;
        try
        {
            long found = await Task.Run(() =>
            {
                if (forward) return ContentSearch.FindText(reader, encoding, from, pattern, matchCase, ct);
                long last = -1, at = 0;
                while (true)
                {
                    long hit = ContentSearch.FindText(reader, encoding, at, pattern, matchCase, ct);
                    if (hit < 0 || hit >= from) return last;
                    last = hit;
                    at = hit + 1;
                }
            }, ct);
            if (ct.IsCancellationRequested || _closing.IsCancellationRequested || !ReferenceEquals(_searching, cts) || !ReferenceEquals(_reader, reader)) return;
            if (found < 0)
            {
                _status.Text = $"\"{pattern}\" was not found {(forward ? "after" : "before")} this place.";
                return;
            }
            _lastHit = found;
            _lastHitLength = encoding.GetByteCount(pattern);
            _view.SetHighlight(pattern, matchCase);
            _view.ScrollToOffset(found);
            UpdateStatus();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer search, refresh, or closure owns the report now.
        }
        finally
        {
            if (ReferenceEquals(_searching, cts)) _searching = null;
        }
    }

    private async Task CopyAsync(bool all)
    {
        if (Clipboard is null) return;
        string text = all ? _text : _view.GetSelectedOrVisibleText();
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, text);
        _status.Text = all ? "The whole report is on the clipboard." : "Copied.";
    }

    private async Task SaveAsync()
    {
        string stem = Path.GetFileName(_subject.TrimEnd('\\', '/'));
        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the report as a text file",
            SuggestedFileName = $"{(stem.Length > 0 ? stem : "report")} - {_kind}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
        });
        if (picked?.TryGetLocalPath() is not { } target) return;
        try
        {
            await File.WriteAllTextAsync(target, _text, new UTF8Encoding(true));
            _status.Text = $"Saved as {target}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"It could not be saved: {ex.Message}";
        }
    }
}
