using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Content;
using FileCat.Platform.Windows;

namespace FileCat.App.Views;

/// <summary>Dedicated fixed-length editor over a protected local file and sparse patch overlay.</summary>
public sealed class HexEditorWindow : Window
{
    private static readonly List<HexEditorWindow> s_open = [];
    public static IReadOnlyList<HexEditorWindow> OpenWindows => s_open;
    public string DisplayName => _file.LocalPath!;
    private readonly AppServices _services;
    private readonly ProtectedHexFile _file;
    private readonly HexPatchOverlay _overlay;
    private readonly PagedReader _reader;
    private readonly HexView _hex = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Save in place…" };
    private readonly Button _saveAs = new() { Content = "Save As new file…" };
    private readonly Button _exportPatch = new() { Content = "Export patch…" };
    private readonly Button _undo = new() { Content = "Undo" };
    private readonly Button _redo = new() { Content = "Redo" };
    private readonly Button _cancelCopy = new() { Content = "Stop copy", IsVisible = false };
    private int _highNibble = -1;
    private long _lastCursor;
    private bool _saving, _requiresRecovery, _allowClose;
    private bool _closed;
    private CancellationTokenSource? _copyStop;

    public HexEditorWindow(AppServices services, string path)
    {
        _services = services;
        _file = new ProtectedHexFile(path);
        _overlay = new HexPatchOverlay(_file);
        _reader = new PagedReader(_overlay);
        _hex.SetReader(_reader);
        _hex.IsModified = _overlay.IsModified;
        _hex.CursorMoved += () =>
        {
            if (_lastCursor != _hex.CursorOffset) _highNibble = -1;
            _lastCursor = _hex.CursorOffset;
            UpdateStatus();
        };
        _overlay.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;
            _reader.Refresh();
            _hex.InvalidateVisual();
            UpdateStatus();
        });

        Title = $"{Path.GetFileName(path)} — FileCat Hex Editor";
        Width = 980; Height = 700; MinWidth = 550; MinHeight = 350;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch { }
        var editBytes = new Button { Content = "Replace bytes…" };
        var goTo = new Button { Content = "Go to offset…" };
        var toolbar = new WrapPanel { Margin = new Thickness(8), ItemSpacing = 8, LineSpacing = 5 };
        foreach (var button in new[] { _save, _saveAs, _exportPatch, _cancelCopy, editBytes, _undo, _redo, goTo }) toolbar.Children.Add(button);
        var hint = new TextBlock
        {
            Text = "Type two hex digits to replace the byte at the cursor. Arrow keys move; edits never insert, delete, or resize bytes. Orange bytes are unsaved.",
            Margin = new Thickness(8, 0, 8, 5), TextWrapping = TextWrapping.Wrap,
        };
        var footer = new Border { Classes = { "status" }, Child = _status, Padding = new Thickness(8, 5) };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(toolbar); root.Children.Add(hint); root.Children.Add(footer); root.Children.Add(_hex);
        Content = root;
        _save.Click += async (_, _) => await SaveAsync();
        _saveAs.Click += async (_, _) => await SaveAsAsync();
        _exportPatch.Click += async (_, _) => await ExportPatchAsync();
        _cancelCopy.Click += (_, _) => _copyStop?.Cancel();
        editBytes.Click += async (_, _) => await ReplaceBytesAsync();
        _undo.Click += (_, _) => { _overlay.Undo(); UpdateStatus(); };
        _redo.Click += (_, _) => { _overlay.Redo(); UpdateStatus(); };
        goTo.Click += async (_, _) => await GoToAsync();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => { s_open.Add(this); _hex.Focus(); UpdateStatus(); };
        Closing += OnClosing;
        Closed += (_, _) => { _closed = true; s_open.Remove(this); _reader.Dispose(); };
    }

    private void UpdateStatus()
    {
        Title = $"{(_overlay.DirtyBytes > 0 ? "● " : "")}{Path.GetFileName(_file.LocalPath)} — FileCat Hex Editor";
        _status.Text = _requiresRecovery
            ? "An interrupted save journal remains. Close this editor and use Tools → Recover interrupted hex save before editing this file again."
            : $"Offset 0x{_hex.CursorOffset:X} of {_file.Length:N0} bytes · {_overlay.DirtyBytes:N0} modified bytes · {_overlay.TouchedBytes:N0} touched bytes" +
              (_highNibble >= 0 ? $" · enter low nibble for {_highNibble:X}_" : "") +
              (_saving ? " · Saving…" : " · Protected baseline: other ordinary writers are denied");
        _save.IsEnabled = !_saving && !_requiresRecovery && _overlay.DirtyBytes > 0;
        _saveAs.IsEnabled = !_saving && !_requiresRecovery;
        _exportPatch.IsEnabled = !_saving && !_requiresRecovery && _overlay.DirtyBytes > 0;
        _undo.IsEnabled = !_saving && !_requiresRecovery && _overlay.CanUndo;
        _redo.IsEnabled = !_saving && !_requiresRecovery && _overlay.CanRedo;
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_saving || _requiresRecovery) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ctrl && e.Key == Key.S) { e.Handled = true; await SaveAsync(); return; }
        if (ctrl && e.Key == Key.Z) { e.Handled = true; _overlay.Undo(); return; }
        if (ctrl && e.Key == Key.Y) { e.Handled = true; _overlay.Redo(); return; }
        if (ctrl && e.Key == Key.G) { e.Handled = true; await GoToAsync(); return; }
        if (FocusManager?.GetFocusedElement() != _hex || ctrl || (e.KeyModifiers & KeyModifiers.Alt) != 0) return;
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        int digit = HexDigit(e.Key);
        if (digit < 0) return;
        e.Handled = true;
        if (_file.Length == 0) return;
        if (_hex.Selection.Length > 1)
        { await AlertAsync("Selected range", "Use Replace bytes… to overwrite a selected range with exactly the same number of bytes."); return; }
        if (_highNibble < 0) { _highNibble = digit; UpdateStatus(); return; }
        try
        {
            _overlay.Write(_hex.CursorOffset, [(byte)((_highNibble << 4) | digit)]);
            _highNibble = -1;
            _hex.GoTo(Math.Min(_file.Length - 1, _hex.CursorOffset + 1));
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or InvalidOperationException)
        { await AlertAsync("Cannot edit byte", ex.Message); }
        UpdateStatus();
    }

    private static int HexDigit(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        >= Key.A and <= Key.F => key - Key.A + 10,
        _ => -1,
    };

    private async Task ReplaceBytesAsync()
    {
        var input = await PromptAsync("Replace bytes", "Hex bytes at the cursor (spaces allowed; no insertion):", "");
        if (input is null) return;
        try
        {
            var compact = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (compact.Length == 0 || compact.Length % 2 != 0) throw new FormatException("Enter complete pairs of hex digits.");
            byte[] bytes = Convert.FromHexString(compact);
            var selected = _hex.Selection;
            long offset = selected.Length > 1 ? selected.Start : _hex.CursorOffset;
            if (selected.Length > 1 && bytes.Length != selected.Length)
                throw new FormatException($"Enter exactly {selected.Length:N0} bytes for the selected range.");
            _overlay.Write(offset, bytes);
            _hex.GoTo(Math.Min(Math.Max(0, _file.Length - 1), offset + bytes.Length));
        }
        catch (Exception ex) when (ex is FormatException or IOException or ArgumentOutOfRangeException)
        { await AlertAsync("Cannot replace bytes", ex.Message); }
    }

    private async Task GoToAsync()
    {
        var input = await PromptAsync("Go to offset", "Byte offset (decimal or 0x hexadecimal):", "0x" + _hex.CursorOffset.ToString("X"));
        if (input is null) return;
        bool hex = input.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!long.TryParse(hex ? input[2..] : input, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long offset) || offset < 0 || offset >= _file.Length)
        { await AlertAsync("Invalid offset", $"Choose an offset from 0 to {Math.Max(0, _file.Length - 1):N0}."); return; }
        _hex.GoTo(offset);
        _hex.Focus();
    }

    private async Task SaveAsync()
    {
        if (_saving || _requiresRecovery || _overlay.DirtyBytes == 0) return;
        if (!await ConfirmAsync("Save modified bytes in place?",
            $"Write {_overlay.DirtyBytes:N0} modified bytes to {_file.LocalPath}? The file length and identity stay fixed. " +
            "The protected handle blocks ordinary concurrent writers. A durable journal keeps original and replacement bytes; a crash can leave mixed ranges for guarded recovery. This save is not atomic.",
            "Save in place")) return;
        _saving = true; UpdateStatus();
        var before = HexSaveJournal.Pending(_services.Paths.HexRecoveryDirectory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            await Task.Run(() => HexSaveJournal.Save(_file, _overlay, _services.Paths.HexRecoveryDirectory));
            _reader.Refresh(); _hex.InvalidateVisual();
            await AlertAsync("Saved", "Modified bytes were written, flushed, and read back. The file kept its identity and length.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _requiresRecovery = HexSaveJournal.Pending(_services.Paths.HexRecoveryDirectory).Any(p => !before.Contains(p));
            await AlertAsync("Hex save stopped", ex.Message + (_requiresRecovery
                ? "\n\nAn interrupted journal was kept. Use Tools → Recover interrupted hex save before editing this file again." : ""));
        }
        finally { _saving = false; UpdateStatus(); }
    }

    private async Task SaveAsAsync()
    {
        if (_saving || _requiresRecovery) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Save edited content as a new file", SuggestedFileName = Path.GetFileName(_file.LocalPath) });
        string? target = file?.TryGetLocalPath();
        if (target is null) return;
        if (!await ConfirmAsync("Create edited copy?",
            $"Create {target} by reading all {_file.Length:N0} bytes from the protected source? This may take substantial time and space. The new file does not inherit the source's ACLs, streams, or sparse allocation.",
            "Create new file")) return;
        _saving = true; UpdateStatus();
        _copyStop = new CancellationTokenSource();
        _cancelCopy.IsVisible = true;
        try
        {
            await Task.Run(() => HexSaveAs.CreateNew(_file, _overlay, target, _copyStop.Token));
            await AlertAsync("Copy created", $"Edited content was saved to {target}. The original remains open with its unsaved overlay.");
        }
        catch (OperationCanceledException) { await AlertAsync("Copy stopped", "The incomplete temporary copy was removed. Your edits remain open."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { await AlertAsync("Save As failed", ex.Message); }
        finally { _copyStop.Dispose(); _copyStop = null; _cancelCopy.IsVisible = false; _saving = false; UpdateStatus(); }
    }

    private async Task ExportPatchAsync()
    {
        if (_saving || _requiresRecovery || _overlay.DirtyBytes == 0) return;
        var selected = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export exact hex patch record",
            SuggestedFileName = Path.GetFileName(_file.LocalPath) + ".filecat-patch.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("FileCat patch JSON") { Patterns = ["*.json"] }],
        });
        string? target = selected?.TryGetLocalPath();
        if (target is null) return;
        _saving = true; UpdateStatus();
        try
        {
            await Task.Run(() => HexPatchExport.Export(_file, _overlay, target));
            await AlertAsync("Patch exported", $"Exact original and replacement ranges were saved to {target}. This is a review record; exporting it does not change the file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { await AlertAsync("Patch export failed", ex.Message); }
        finally { _saving = false; UpdateStatus(); }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        if (_saving) { e.Cancel = true; return; }
        if (_requiresRecovery || _overlay.DirtyBytes == 0) return;
        e.Cancel = true;
        if (await ConfirmAsync("Discard unsaved hex edits?",
            $"Discard {_overlay.DirtyBytes:N0} modified bytes from this editor? The original file has not been changed by these unsaved edits.", "Discard edits"))
        { _allowClose = true; Close(); }
    }

    private async Task<string?> PromptAsync(string title, string label, string initial)
    {
        var dialog = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var input = new TextBox { Text = initial, MinWidth = 430 };
        var ok = new Button { Content = "Apply", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 10, Children =
        {
            new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Children = { cancel, ok } },
        } };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var dialog = new Window { Title = title, Width = 600, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var yes = new Button { Content = action, IsDefault = true };
        var no = new Button { Content = "Cancel", IsCancel = true };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children =
        {
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8, Children = { no, yes } },
        } };
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task AlertAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 560, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children =
        {
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { close } },
        } };
        await dialog.ShowDialog(this);
    }
}
