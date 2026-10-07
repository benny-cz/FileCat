using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Content;
using FileCat.Platform.Windows;

namespace FileCat.App.Views;

/// <summary>
/// Fixed-length hex editor in its own window (plan §4.1, §13): a protected (deny-write) baseline, a sparse overlay
/// with bounded undo, explicit journaled in-place saves with guarded recovery, Save As a new file, and patch
/// export/apply. Typing never inserts, deletes, or resizes.
/// </summary>
public sealed class HexEditorWindow : Window
{
    private static readonly List<HexEditorWindow> s_open = [];
    private static readonly Encoding s_latin1 = Encoding.Latin1;

    /// <summary>Open editors, oldest first (the window list and exit checks).</summary>
    public static IReadOnlyList<HexEditorWindow> OpenWindows => s_open;

    /// <summary>Raised when any editor's unsaved state changes: modified bytes, a running save, or a pending recovery.</summary>
    public static event Action? UnsavedStateChanged;

    private readonly AppServices _services;
    private readonly HexView _hex = new() { TracksActiveColumn = true };
    private readonly OverlayDialogService _dialogs;
    private readonly TextBlock _status = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _state = new() { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _save = new() { Content = "Save" };
    private readonly Button _saveAs = new() { Content = "Save As…" };
    private readonly Button _exportPatch = new() { Content = "Export patch…" };
    private readonly Button _applyPatch = new() { Content = "Apply patch…" };
    private readonly Button _undo = new() { Content = "Undo" };
    private readonly Button _redo = new() { Content = "Redo" };
    private readonly Button _replace = new() { Content = "Replace bytes…" };
    private readonly Button _goTo = new() { Content = "Go to…" };
    private readonly Button _stopCopy = new() { Content = "Stop", IsVisible = false, Classes = { "danger" } };
    private readonly TextBox _search = new() { PlaceholderText = "Find (Ctrl+F)", MinWidth = 200 };
    private readonly CheckBox _hexSearch = new() { Content = "Hex bytes", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _matchCase = new() { Content = "Aa", VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _recoveryBar = new() { Classes = { "banner" }, IsVisible = false };
    private readonly TextBlock _recoveryText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private ProtectedHexFile _file;
    private HexPatchOverlay _overlay;
    private PagedReader _reader;
    private HexRecoveryRecord? _interrupted;
    private string? _message;
    private long _lastCursor = -1;
    private bool _saving, _allowClose, _closed, _saveConfirmed;
    private CancellationTokenSource? _copyStop, _searchCts;
    private long _lastHit = -1;
    private int _lastHitLength;

    private HexEditorWindow(AppServices services, ProtectedHexFile file)
    {
        _services = services;
        _file = file;
        _overlay = new HexPatchOverlay(file);
        _reader = new PagedReader(_overlay);
        Attach();

        Width = 1040; Height = 720; MinWidth = 620; MinHeight = 360; // the toolbar on one row
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        ToolTip.SetTip(_save, "Save in place (Ctrl+S): writes only the modified bytes, through a recovery journal");
        ToolTip.SetTip(_saveAs, "Save the edited content as a new file (Ctrl+Shift+S); the original stays unchanged");
        ToolTip.SetTip(_exportPatch, "Save the exact original and new bytes of every change to a patch file; the file is not changed");
        ToolTip.SetTip(_applyPatch, "Stage a FileCat patch file as unsaved edits, after checking every original byte");
        ToolTip.SetTip(_undo, "Undo (Ctrl+Z)");
        ToolTip.SetTip(_redo, "Redo (Ctrl+Y)");
        ToolTip.SetTip(_replace, "Overwrite bytes at the cursor, or the selected bytes, with hex values (Ctrl+V pastes hex)");
        ToolTip.SetTip(_goTo, "Go to an offset (Ctrl+G)");
        ToolTip.SetTip(_stopCopy, "Stop creating the copy; the incomplete file is removed");
        Avalonia.Automation.AutomationProperties.SetName(_search, "Find in file");

        var toolbar = new WrapPanel { Margin = new Thickness(8, 6), ItemSpacing = 6, LineSpacing = 4 };
        foreach (var c in new Control[] { _save, _saveAs, _stopCopy, _exportPatch, _applyPatch, Separator(), _undo, _redo, _replace, _goTo, Separator(), _search, _hexSearch, _matchCase })
            toolbar.Children.Add(c);

        var finish = new Button { Content = "Finish saving", Classes = { "primary" } };
        var restore = new Button { Content = "Restore original bytes" };
        ToolTip.SetTip(finish, "Write the remaining new bytes, then verify them and remove the recovery journal");
        ToolTip.SetTip(restore, "Write the journaled original bytes back; your edits stay open and unsaved");
        finish.Click += async (_, _) => await RecoverInEditorAsync(rollback: false);
        restore.Click += async (_, _) => await RecoverInEditorAsync(rollback: true);
        var recoveryButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 0, 0), Children = { finish, restore } };
        DockPanel.SetDock(recoveryButtons, Dock.Right);
        _recoveryBar.Child = new DockPanel { Children = { recoveryButtons, _recoveryText } };

        var hint = new TextBlock
        {
            Classes = { "small", "muted" },
            Text = "Type hex digits to overwrite bytes · Tab switches between the hex and text columns · the file length never changes · modified bytes are underlined"
                + (ProtectedHexFile.ExcludesWriters ? string.Empty : " · other programs can still write this file here, so each save first checks that the bytes it replaces are unchanged"),
            Margin = new Thickness(8, 0, 8, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        var statusRow = new DockPanel { Children = { _state, _status } };
        DockPanel.SetDock(_state, Dock.Right);
        var footer = new Border { Classes = { "status" }, Child = statusRow, Padding = new Thickness(8, 4) };
        var main = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_recoveryBar, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        main.Children.Add(toolbar);
        main.Children.Add(_recoveryBar);
        main.Children.Add(hint);
        main.Children.Add(footer);
        var hexSurface = new Border { Child = _hex };
        main.Children.Add(hexSurface);
        var overlayHost = new Panel { IsVisible = false };
        Content = ThemeLayers.Over(new Grid { Children = { main, overlayHost } }, hexSurface);
        _dialogs = new OverlayDialogService(overlayHost, () => _hex);

        _save.Click += async (_, _) => await SaveAsync();
        _saveAs.Click += async (_, _) => await SaveAsAsync();
        _exportPatch.Click += async (_, _) => await ExportPatchAsync();
        _applyPatch.Click += async (_, _) => await ApplyPatchAsync();
        _stopCopy.Click += (_, _) => _copyStop?.Cancel();
        _replace.Click += async (_, _) => await ReplaceBytesAsync();
        _undo.Click += (_, _) => UndoRedo(redo: false);
        _redo.Click += (_, _) => UndoRedo(redo: true);
        _goTo.Click += async (_, _) => await GoToAsync();
        _search.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await FindAsync(forward: (e.KeyModifiers & KeyModifiers.Shift) == 0); }
            else if (e.Key == Key.Escape) { e.Handled = true; _searchCts?.Cancel(); _hex.Focus(); }
        };
        _hex.CursorMoved += () =>
        {
            if (_hex.CursorOffset != _lastCursor)
            {
                _hex.PendingNibble = -1;
                _message = null;
            }
            _lastCursor = _hex.CursorOffset;
            UpdateStatus();
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        Opened += (_, _) => { _hex.Focus(); UpdateStatus(); };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _closed = true;
            _copyStop?.Cancel();
            _searchCts?.Cancel();
            s_open.Remove(this);
            _reader.Dispose();
            UnsavedStateChanged?.Invoke();
        };
        s_open.Add(this);
    }

    /// <summary>The edited file's full path.</summary>
    public string DisplayName => _file.LocalPath!;

    /// <summary>Unsaved bytes, a running save, or an interrupted save that still needs a decision.</summary>
    public bool HasUnsavedWork => _overlay.DirtyBytes > 0 || _saving || _interrupted is not null;

    public string UnsavedSummary => _saving ? "saving" : _interrupted is not null ? "interrupted save needs a decision"
        : Formatters.Plural(_overlay.DirtyBytes, "modified byte", "modified bytes");

    /// <summary>
    /// Opens (or brings forward) the editor for a local file. Returns an explanation instead of throwing when the file
    /// cannot be edited, including when an interrupted save of it still needs recovery.
    /// </summary>
    public static string? OpenOrActivate(AppServices services, string path, long offset = 0)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return ex.Message; }
        var existing = s_open.FirstOrDefault(w => string.Equals(w.DisplayName, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.BringForward(offset > 0 ? offset : null);
            return null;
        }
        foreach (var pending in HexSaveJournal.Pending(services.Paths.HexRecoveryDirectory))
        {
            try
            {
                if (string.Equals(Path.GetFullPath(HexSaveJournal.Read(pending).TargetPath), full, StringComparison.OrdinalIgnoreCase))
                    return "An earlier save of this file was interrupted. Finish or roll it back first: Tools → Recover interrupted hex save.";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        try
        {
            var window = new HexEditorWindow(services, new ProtectedHexFile(full));
            window.Show();
            if (offset > 0) window._hex.GoTo(offset);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return "The hex editor cannot open this file: " + ex.Message;
        }
    }

    public void BringForward(long? offset = null)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (offset is { } o) _hex.GoTo(o);
        _hex.Focus();
    }

    private static Control Separator() => new Border { Width = 1, Margin = new Thickness(4, 2), Background = Brushes.Gray, Opacity = 0.4 };

    /// <summary>Connects the view to the current file, overlay, and reader (after opening or Save As).</summary>
    private void Attach()
    {
        _hex.SetReader(_reader);
        _hex.IsModified = _overlay.IsModified;
        _overlay.Changed += OnOverlayChanged;
        Title = WindowTitle();
    }

    private void OnOverlayChanged(HexOverlayChange change)
    {
        var overlay = _overlay;
        void Apply()
        {
            if (_closed || !ReferenceEquals(overlay, _overlay)) return;
            if (change.Bytes is { } bytes) _reader.Overwrite(change.Offset, bytes);
            _hex.InvalidateVisual();
            UpdateStatus();
            UnsavedStateChanged?.Invoke();
        }
        // Edits from the keyboard update the cached page before the next frame; background changes are posted.
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    private string WindowTitle() => $"{(_overlay.DirtyBytes > 0 ? "● " : "")}{Path.GetFileName(_file.LocalPath)} — FileCat Hex Editor";

    private bool CanEdit => !_saving && _interrupted is null;

    private void Status(string message)
    {
        _message = message;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_closed) return;
        Title = WindowTitle();
        long cursor = _hex.CursorOffset;
        var sb = new StringBuilder();
        if (_message is not null) sb.Append(_message).Append("  ·  ");
        sb.Append(CultureInfo.CurrentCulture, $"Offset 0x{cursor:X} ({cursor:N0}) of {Formatters.ExactSize(_file.Length)}");
        Span<byte> value = stackalloc byte[4];
        if (_reader.TryRead(cursor, value, out int n) && n > 0)
        {
            sb.Append(CultureInfo.CurrentCulture, $" · byte 0x{value[0]:X2} = {value[0]}");
            if (value[0] is >= 0x20 and < 0x7F) sb.Append(CultureInfo.InvariantCulture, $" '{(char)value[0]}'");
            if (n == 4) sb.Append(CultureInfo.CurrentCulture, $" · UInt32 LE {BitConverter.ToUInt32(value)}");
        }
        var sel = _hex.Selection;
        if (sel.Length > 1) sb.Append(CultureInfo.CurrentCulture, $" · {sel.Length:N0} bytes selected");
        if (_hex.PendingNibble >= 0) sb.Append(CultureInfo.InvariantCulture, $" · type the second digit of {_hex.PendingNibble:X}_");
        sb.Append(_hex.TextColumnActive ? " · typing: text (ASCII)" : " · typing: hex");
        _status.Text = sb.ToString();

        int dirty = _overlay.DirtyBytes;
        _state.Text = _saving ? "Saving…"
            : _interrupted is not null ? "Interrupted save: choose Finish or Restore above"
            : dirty > 0 ? $"{Formatters.Plural(dirty, "byte", "bytes")} modified (unsaved) · other programs cannot write this file while it is open"
            : "No unsaved changes · other programs cannot write this file while it is open";
        if (_overlay.TouchedBytes > HexPatchOverlay.MaxTouchedBytes / 2)
            _state.Text += $" · edit budget {100.0 * _overlay.TouchedBytes / HexPatchOverlay.MaxTouchedBytes:0}% used";
        bool idle = CanEdit;
        _save.IsEnabled = idle && dirty > 0;
        _saveAs.IsEnabled = idle;
        _exportPatch.IsEnabled = idle && dirty > 0;
        _applyPatch.IsEnabled = idle;
        _undo.IsEnabled = idle && _overlay.CanUndo;
        _redo.IsEnabled = idle && _overlay.CanRedo;
        _replace.IsEnabled = idle;
    }

    // ---- Keyboard ----------------------------------------------------------------------------------------------

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogs.IsOpen) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox)
        {
            // Text boxes keep their own editing keys; only search-next and save pass through.
            if (e.Key == Key.F3) { e.Handled = true; await FindAsync(!shift); }
            else if (ctrl && e.Key == Key.S) { e.Handled = true; if (shift) await SaveAsAsync(); else await SaveAsync(); }
            return;
        }
        switch (e.Key)
        {
            case Key.S when ctrl && shift: e.Handled = true; await SaveAsAsync(); return;
            case Key.S when ctrl: e.Handled = true; await SaveAsync(); return;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl: e.Handled = true; UndoRedo(redo: true); return;
            case Key.Z when ctrl: e.Handled = true; UndoRedo(redo: false); return;
            case Key.G when ctrl: e.Handled = true; await GoToAsync(); return;
            case Key.F when ctrl:
            case Key.F7: e.Handled = true; _search.Focus(); _search.SelectAll(); return;
            case Key.F3: e.Handled = true; await FindAsync(!shift); return;
            case Key.C when ctrl: e.Handled = true; await CopyAsync(); return;
            case Key.V when ctrl: e.Handled = true; await PasteAsync(); return;
            case Key.OemPlus or Key.Add when ctrl: e.Handled = true; _hex.FontSize += 1; return;
            case Key.OemMinus or Key.Subtract when ctrl: e.Handled = true; _hex.FontSize -= 1; return;
            case Key.F10: e.Handled = true; Close(); return;
        }
        if (focused != _hex || ctrl || alt) return;
        switch (e.Key)
        {
            case Key.Tab:
                e.Handled = true;
                _hex.PendingNibble = -1;
                _hex.TextColumnActive = !_hex.TextColumnActive;
                break;
            case Key.Escape:
                e.Handled = true;
                if (_hex.PendingNibble >= 0) { _hex.PendingNibble = -1; UpdateStatus(); }
                else if (_hex.Selection.Length > 1) _hex.ClearSelection();
                else Close();
                break;
            case Key.Back:
                e.Handled = true;
                if (_hex.PendingNibble >= 0) { _hex.PendingNibble = -1; UpdateStatus(); }
                else _hex.GoTo(Math.Max(0, _hex.CursorOffset - 1));
                break;
            case Key.Delete:
            case Key.Insert:
                e.Handled = true;
                Status("Bytes cannot be inserted or deleted: this editor keeps the file length fixed. Type over bytes instead.");
                break;
        }
    }

    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (_dialogs.IsOpen || string.IsNullOrEmpty(e.Text) || FocusManager?.GetFocusedElement() != _hex) return;
        e.Handled = true;
        if (!CanEdit)
        {
            Status(_saving ? "Wait for the save to finish." : "Finish or roll back the interrupted save first (see above).");
            return;
        }
        foreach (char c in e.Text)
            if (!TypeCharacter(c)) break;
    }

    /// <summary>Hex column: two digits make one byte. Text column: one printable ASCII character is one byte.</summary>
    private bool TypeCharacter(char c)
    {
        if (char.IsControl(c)) return true;
        CollapseSelectionToStart();
        long at = _hex.CursorOffset;
        if (_hex.TextColumnActive)
        {
            if (c is < ' ' or > '~')
            {
                Status("The text column accepts printable ASCII characters; type other byte values in the hex column (Tab).");
                return false;
            }
            return WriteAndAdvance(at, [(byte)c]);
        }
        int digit = HexDigit(c);
        if (digit < 0)
        {
            Status("Type hexadecimal digits 0–9 and A–F, or press Tab to type text.");
            return false;
        }
        if (_hex.PendingNibble < 0)
        {
            _hex.PendingNibble = digit;
            UpdateStatus();
            return true;
        }
        byte value = (byte)((_hex.PendingNibble << 4) | digit);
        _hex.PendingNibble = -1;
        return WriteAndAdvance(at, [value]);
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private void CollapseSelectionToStart()
    {
        var sel = _hex.Selection;
        if (sel.Length > 1) _hex.GoTo(sel.Start);
    }

    private bool WriteAndAdvance(long offset, byte[] bytes)
    {
        try
        {
            _overlay.Write(offset, bytes);
            _hex.GoTo(Math.Min(_file.Length - 1, offset + bytes.Length));
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            Status(ex.Message);
            return false;
        }
    }

    private void UndoRedo(bool redo)
    {
        if (!CanEdit) return;
        _hex.PendingNibble = -1;
        HexOverlayChange change;
        bool done = redo ? _overlay.Redo(out change) : _overlay.Undo(out change);
        if (!done)
        {
            Status(redo ? "Nothing to redo." : "Nothing to undo.");
            return;
        }
        _hex.GoTo(change.Offset);
        Status((redo ? "Redid" : "Undid") + $" a change of {Formatters.Plural(change.Bytes?.Length ?? 0, "byte", "bytes")} at 0x{change.Offset:X}.");
    }

    // ---- Clipboard, replace, go to, find --------------------------------------------------------------------------

    private async Task CopyAsync()
    {
        if (Clipboard is null) return;
        var sel = _hex.Selection;
        int count = (int)Math.Min(Math.Max(1, sel.Length), 1024 * 1024);
        var bytes = await Task.Run(() => _hex.ReadSelection(count));
        string text = _hex.TextColumnActive
            ? new string(bytes.Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.').ToArray())
            : string.Join(' ', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, text);
        Status($"Copied {Formatters.Plural(bytes.Length, "byte", "bytes")} as {(_hex.TextColumnActive ? "text" : "hex")}{(sel.Length > count ? " (the first 1 MiB)" : "")}.");
    }

    private async Task PasteAsync()
    {
        if (Clipboard is null || !CanEdit) return;
        var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(Clipboard);
        if (string.IsNullOrEmpty(text)) { Status("The clipboard has no text to paste."); return; }
        byte[]? bytes;
        if (_hex.TextColumnActive)
        {
            if (text.Any(c => c is < ' ' or > '~')) { Status("The text column pastes printable ASCII only; paste other values as hex in the hex column."); return; }
            bytes = s_latin1.GetBytes(text);
        }
        else
        {
            bytes = ContentSearch.ParseHex(text);
            if (bytes is null) { Status("The clipboard does not contain hex bytes such as \"4D 5A 90\"."); return; }
        }
        OverwriteAtCursorOrSelection(bytes, "Pasted");
    }

    /// <summary>Overwrites at the selection (which must have exactly as many bytes) or at the cursor.</summary>
    private void OverwriteAtCursorOrSelection(byte[] bytes, string verb)
    {
        var sel = _hex.Selection;
        long offset = sel.Length > 1 ? sel.Start : _hex.CursorOffset;
        if (sel.Length > 1 && bytes.Length != sel.Length)
        {
            Status($"The selection has {Formatters.Plural((int)Math.Min(sel.Length, int.MaxValue), "byte", "bytes")}; " +
                   $"{Formatters.Plural(bytes.Length, "byte was", "bytes were")} given. Select exactly as many bytes, or clear the selection (Esc).");
            return;
        }
        if (bytes.Length > _file.Length - offset)
        {
            Status($"{Formatters.Plural(bytes.Length, "byte", "bytes")} would run past the end of the file; the length cannot grow.");
            return;
        }
        const int MaxAction = 1024 * 1024;
        if (bytes.Length > MaxAction) { Status("One edit can change at most 1 MiB."); return; }
        _hex.PendingNibble = -1;
        if (WriteAndAdvance(offset, bytes)) Status($"{verb} {Formatters.Plural(bytes.Length, "byte", "bytes")} at 0x{offset:X}.");
    }

    private async Task ReplaceBytesAsync()
    {
        if (!CanEdit) return;
        var sel = _hex.Selection;
        string where = sel.Length > 1 ? $"the {sel.Length:N0} selected bytes" : $"offset 0x{_hex.CursorOffset:X}";
        var result = await _dialogs.PromptAsync(new PromptOptions("Replace bytes", $"Hex bytes to write at {where}. Spaces are allowed; the file length never changes.")
        {
            ConfirmText = "Replace",
            Validate = t => ContentSearch.ParseHex(t) is { } b
                ? sel.Length > 1 && b.Length != sel.Length ? $"Enter exactly {sel.Length:N0} bytes for the selection." : null
                : "Enter hex bytes such as \"4D 5A 90\".",
        });
        if (result is null || ContentSearch.ParseHex(result.Text) is not { } bytes) return;
        OverwriteAtCursorOrSelection(bytes, "Replaced");
    }

    private async Task GoToAsync()
    {
        long max = _file.Length - 1;
        var result = await _dialogs.PromptAsync(new PromptOptions("Go to offset", "Offset: hexadecimal with 0x (0x1F00), decimal (7936), or a percentage (50%).")
        {
            Text = "0x" + _hex.CursorOffset.ToString("X", CultureInfo.InvariantCulture),
            ConfirmText = "Go",
            Validate = t => TryParseOffset(t, out long o) && o >= 0 && o <= max ? null : $"Enter an offset from 0 to {max:N0} (0x{max:X}).",
        });
        if (result is null || !TryParseOffset(result.Text, out long offset)) return;
        _hex.GoTo(offset);
        _hex.Focus();
    }

    private bool TryParseOffset(string text, out long offset)
    {
        var t = text.Trim().Replace(" ", "", StringComparison.Ordinal);
        offset = 0;
        if (t.EndsWith('%') && double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.CurrentCulture, out var pct) && pct is >= 0 and <= 100)
        {
            offset = Math.Min(_file.Length - 1, (long)(_file.Length * (pct / 100)));
            return true;
        }
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out offset);
        return long.TryParse(t, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out offset);
    }

    private async Task FindAsync(bool forward)
    {
        if (_closed) return;
        var pattern = _search.Text ?? "";
        if (pattern.Length == 0) { _search.Focus(); return; }
        byte[]? bytes = _hexSearch.IsChecked == true ? ContentSearch.ParseHex(pattern) : null;
        if (_hexSearch.IsChecked == true && bytes is null) { Status("Hex search expects bytes such as \"4D 5A 90\"."); return; }
        _searchCts?.Cancel();
        using var cts = _searchCts = new CancellationTokenSource();
        var ct = cts.Token;
        var sel = _hex.Selection;
        // F3 again continues after (or before) the match that is still selected; otherwise the search starts at the cursor.
        bool continuing = _lastHit >= 0 && sel.Start == _lastHit && sel.Length == _lastHitLength;
        long from = continuing ? (forward ? _lastHit + 1 : _lastHit) : _hex.CursorOffset;
        bool matchCase = _matchCase.IsChecked == true;
        var reader = _reader;
        Status("Searching…");
        try
        {
            long found = await Task.Run(() =>
            {
                if (bytes is not null)
                    return forward ? ContentSearch.FindBytes(reader, from, bytes, ct) : ContentSearch.FindBytesBackward(reader, from, bytes, ct);
                if (forward) return ContentSearch.FindText(reader, s_latin1, from, pattern, matchCase, ct);
                long start = Math.Max(0, from - 4 * 1024 * 1024), last = -1, p = start;
                while (true)
                {
                    long hit = ContentSearch.FindText(reader, s_latin1, p, pattern, matchCase, ct);
                    if (hit < 0 || hit >= from) break;
                    last = hit;
                    p = hit + 1;
                }
                return last;
            }, ct);
            if (_closed || !ReferenceEquals(_searchCts, cts) || !ReferenceEquals(reader, _reader)) return;
            ct.ThrowIfCancellationRequested();
            if (found < 0)
            {
                Status($"\"{pattern}\" was not found {(forward ? "after" : "before")} the cursor.");
                return;
            }
            _lastHit = found;
            _lastHitLength = bytes?.Length ?? s_latin1.GetByteCount(pattern);
            _hex.GoTo(found, select: true, _lastHitLength);
            _hex.Focus();
            Status($"Found at 0x{found:X}. F3 finds the next match, Shift+F3 the previous one.");
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_searchCts, cts) && ReferenceEquals(reader, _reader)) Status("Search stopped.");
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
        }
    }

    // ---- Saving ----------------------------------------------------------------------------------------------------

    private async Task SaveAsync()
    {
        if (!CanEdit) return;
        int dirty = _overlay.DirtyBytes;
        if (dirty == 0) { Status("No unsaved changes."); return; }
        if (!_saveConfirmed)
        {
            var dontAsk = new CheckBox { Content = "Don't ask again while this editor is open" };
            var body = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 620,
                        Text = $"Write {Formatters.Plural(dirty, "modified byte", "modified bytes")} into {_file.LocalPath}?" },
                    new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 620,
                        Text = "The file keeps its length, identity, permissions, and links; only the modified bytes are written. " +
                               "Several ranges cannot be written atomically, so a recovery journal first records the original and new bytes: " +
                               "a save stopped by a crash or power loss can be finished or rolled back afterwards." },
                    dontAsk,
                },
            };
            if (!ProtectedHexFile.ExcludesWriters)
            {
                // Linux and macOS (D-45): detection instead of exclusion, said before the user chooses it.
                body.Children.Insert(2, new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 620,
                    Classes = { "warning" },
                    Text = "Other programs can write this file while it is open here. FileCat first checks that this is still the same file " +
                           "and that every byte it replaces still holds the value you saw, and saves nothing otherwise; a program writing " +
                           "the same bytes during the save itself could still be overwritten. Save As writes a new file instead.",
                });
            }
            var answer = await _dialogs.ShowCustomAsync("Save in place?", body,
                [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Save in place", "save", IsDefault: true)]);
            if (answer as string != "save") return;
            _saveConfirmed = dontAsk.IsChecked == true;
        }
        SetSaving(true);
        string journals = _services.Paths.HexRecoveryDirectory;
        try
        {
            var file = _file;
            var overlay = _overlay;
            await Task.Run(() => HexSaveJournal.Save(file, overlay, journals));
            Status($"Saved: {Formatters.Plural(dirty, "byte was", "bytes were")} written in place, flushed, and read back.");
        }
        catch (HexSaveInterruptedException ex)
        {
            ShowInterrupted(ex.JournalPath, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            await _dialogs.AlertAsync("Not saved", ex.Message + "\n\nThe file was not changed. Your edits are still open.");
        }
        finally { SetSaving(false); }
    }

    private void SetSaving(bool saving)
    {
        _saving = saving;
        UpdateStatus();
        UnsavedStateChanged?.Invoke();
    }

    /// <summary>A save stopped after its journal became durable: the file may hold a mix of old and new ranges.</summary>
    private void ShowInterrupted(string journalPath, string reason)
    {
        try { _interrupted = HexSaveJournal.Read(journalPath); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // One paragraph: line breaks inside one wrapped text spin Avalonia's headless layout.
            _ = _dialogs.AlertAsync("Save stopped", reason + " Its recovery journal could not be read back: " + ex.Message +
                " Close this editor and use Tools → Recover interrupted hex save.");
            return;
        }
        string state;
        try
        {
            var inspection = HexSaveJournal.Inspect(_interrupted, _file);
            state = inspection.Blocker ?? $"{inspection.ReplacedRanges} of {_interrupted.Ranges.Count} changed ranges have the new bytes" +
                (inspection.MixedRanges > 0 ? $", {inspection.MixedRanges} partly" : "") + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { state = "The file could not be checked: " + ex.Message; }
        _recoveryText.Text = $"The save stopped: {reason} {state} Choose Finish saving or Restore original bytes; until then, editing is paused.";
        _recoveryBar.IsVisible = true;
        UpdateStatus();
        UnsavedStateChanged?.Invoke();
    }

    private async Task RecoverInEditorAsync(bool rollback)
    {
        if (_interrupted is not { } record || _saving) return;
        SetSaving(true);
        try
        {
            var file = _file;
            await Task.Run(() => HexSaveJournal.Recover(record, rollback, file));
            _interrupted = null;
            _recoveryBar.IsVisible = false;
            if (!rollback) _overlay.AcceptSave();
            Status(rollback ? "Original bytes restored and verified. Your edits are still open and unsaved."
                : "Save finished: every modified byte is written, flushed, and read back.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            await _dialogs.AlertAsync("Recovery stopped safely", ex.Message + "\n\nThe recovery journal is kept, so you can try again or decide later from Tools → Recover interrupted hex save.");
        }
        finally { SetSaving(false); }
    }

    private async Task SaveAsAsync()
    {
        if (!CanEdit) return;
        string source = _file.LocalPath!;
        string stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
        var folder = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(source)!);
        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save edited content as a new file",
            SuggestedFileName = $"{stem} (edited){ext}",
            SuggestedStartLocation = folder,
            ShowOverwritePrompt = false,
        });
        string? target = picked?.TryGetLocalPath();
        if (target is null) return;
        if (File.Exists(target) || Directory.Exists(target))
        {
            await _dialogs.AlertAsync("Choose a new name", "Save As creates a new file and never replaces an existing one. Choose a name that does not exist yet.");
            return;
        }
        SetSaving(true);
        _copyStop = new CancellationTokenSource();
        _stopCopy.IsVisible = true;
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            if (_closed) return;
            double pct = p.Total > 0 ? 100.0 * p.Done / p.Total : 100;
            Status($"Creating the new file… {pct:0}% ({Formatters.SizeWithUnit(p.Done)} of {Formatters.SizeWithUnit(p.Total)})");
        });
        try
        {
            var file = _file;
            var overlay = _overlay;
            var token = _copyStop.Token;
            var result = await Task.Run(() => HexSaveAs.CreateNew(file, overlay, target, token, progress));
            string notes = (result.Sparse ? " Unallocated ranges stayed sparse." : "") +
                           " The new file has the default permissions of its folder; alternate data streams other than the download mark were not copied.";
            if (result.OriginMark == HexOriginMark.Lost)
                await _dialogs.AlertAsync("Download mark lost",
                    "The original file is marked as downloaded from the internet, but the new location cannot store that mark (Mark-of-the-Web). Windows will not warn when the new file is opened.");
            SwitchTo(target, notes);
        }
        catch (OperationCanceledException) { Status("Save As stopped; the incomplete new file was removed. Your edits are still open."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await _dialogs.AlertAsync("Save As failed", ex.Message + "\n\nNo new file was created. Your edits are still open.");
        }
        finally
        {
            _copyStop?.Dispose();
            _copyStop = null;
            _stopCopy.IsVisible = false;
            SetSaving(false);
        }
    }

    /// <summary>After Save As, edit the new file (like any editor); the original is released unchanged.</summary>
    private void SwitchTo(string path, string notes)
    {
        ProtectedHexFile next;
        try { next = new ProtectedHexFile(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Status($"Saved as {path}.{notes} It cannot be edited here ({ex.Message}), so the original stays open with its unsaved edits.");
            return;
        }
        long cursor = _hex.CursorOffset;
        _overlay.Changed -= OnOverlayChanged;
        var old = _reader;
        _file = next;
        _overlay = new HexPatchOverlay(next);
        _reader = new PagedReader(_overlay);
        _lastHit = -1;
        Attach();
        old.Dispose();
        _hex.GoTo(Math.Min(cursor, next.Length - 1));
        Status($"Saved as {Path.GetFileName(path)}; you are now editing the new file. The original was not changed.{notes}");
        UnsavedStateChanged?.Invoke();
    }

    private async Task ExportPatchAsync()
    {
        if (!CanEdit || _overlay.DirtyBytes == 0) return;
        var folder = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_file.LocalPath!)!);
        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the changes as a patch",
            SuggestedFileName = Path.GetFileName(_file.LocalPath) + ".filecat-patch.json",
            SuggestedStartLocation = folder,
            DefaultExtension = "json",
            ShowOverwritePrompt = false,
            FileTypeChoices = [new FilePickerFileType("FileCat hex patch") { Patterns = ["*.json"] }],
        });
        string? target = picked?.TryGetLocalPath();
        if (target is null) return;
        SetSaving(true);
        try
        {
            var file = _file;
            var overlay = _overlay;
            int ranges = overlay.SnapshotRanges().Count;
            await Task.Run(() => HexPatchExport.Export(file, overlay, target));
            Status($"Exported {Formatters.Plural(ranges, "changed range", "changed ranges")} to {Path.GetFileName(target)}. The file itself was not changed; the edits are still unsaved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.AlertAsync("Patch export failed", ex.Message);
        }
        finally { SetSaving(false); }
    }

    private async Task ApplyPatchAsync()
    {
        if (!CanEdit) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apply a FileCat hex patch",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("FileCat hex patch") { Patterns = ["*.json"] }],
        });
        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path is null) return;
        HexPatchFile patch;
        try { patch = await Task.Run(() => HexPatchExport.Read(path)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            await _dialogs.AlertAsync("Cannot read the patch", ex.Message);
            return;
        }
        long bytes = patch.Ranges.Sum(r => (long)r.Replacement.Length);
        bool sameFile = patch.SourceIdentity == _file.FileIdentity.ToString();
        if (!await _dialogs.ConfirmAsync("Apply patch?",
                $"Apply {Formatters.Plural(patch.Ranges.Count, "range", "ranges")} ({Formatters.Plural((int)Math.Min(bytes, int.MaxValue), "byte", "bytes")}) from {Path.GetFileName(path)}?\n\n" +
                (sameFile ? "The patch was made from this file." : $"The patch was made from {patch.SourcePath}; it is applied only if every original byte it expects matches this file.") +
                "\n\nThe changes become unsaved edits that you can review and undo before saving.", "Apply"))
            return;
        try
        {
            var (staged, already) = HexPatchExport.Apply(patch, _overlay);
            _hex.GoTo(patch.Ranges[0].Offset);
            Status($"Applied {Formatters.Plural(staged, "range", "ranges")} as unsaved edits" + (already > 0 ? $"; {already} already had the new bytes." : "."));
        }
        catch (HexPatchPartlyAppliedException ex)
        {
            await _dialogs.AlertAsync("Patch applied only in part", ex.Message +
                "\n\nThe file itself was not changed. Undo removes the applied ranges; review them before saving.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            await _dialogs.AlertAsync("Patch not applied", ex.Message + "\n\nNothing was changed.");
        }
    }

    // ---- Closing ---------------------------------------------------------------------------------------------------

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // At application exit the main window has already asked about unsaved editors.
        if (_allowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        if (_saving)
        {
            e.Cancel = true;
            Status("Wait for the save to finish before closing.");
            return;
        }
        if (_interrupted is not null)
        {
            e.Cancel = true;
            if (await _dialogs.ConfirmAsync("Close without deciding?",
                    "The interrupted save is not finished: the file may contain a mix of original and new bytes. " +
                    "The recovery journal is kept, so you can finish or roll back later from Tools → Recover interrupted hex save.",
                    "Close", danger: true))
                CloseNow();
            return;
        }
        if (_overlay.DirtyBytes == 0) return;
        e.Cancel = true;
        var answer = await _dialogs.ShowCustomAsync("Save changes?",
            new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 620,
                Text = $"{Formatters.Plural(_overlay.DirtyBytes, "modified byte is", "modified bytes are")} not saved. The file on disk has not been changed by them.",
            },
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Discard edits", "discard", IsDanger: true),
             new DialogButton("Save", "save", IsDefault: true)]);
        if (answer as string == "discard") CloseNow();
        else if (answer as string == "save")
        {
            await SaveAsync();
            if (!HasUnsavedWork) CloseNow();
        }
    }

    /// <summary>Closes without asking (the user decided, or the application is exiting).</summary>
    public void CloseNow()
    {
        _allowClose = true;
        Close();
    }
}
