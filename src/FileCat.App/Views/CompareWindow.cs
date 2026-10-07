using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Compare;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

/// <summary>One row of the side-by-side view: a line on either side (or a gap), how they relate, and a note row's text.</summary>
public sealed record CompareRow(DiffKind Kind, int? LeftLine, string LeftText, int? RightLine, string RightText, bool EndingDiffers = false, string? Note = null);

/// <summary>
/// File comparison (plan §16.2, TV-08): text side by side with within-line changes, or both files' bytes side by side
/// (after Salamander's File Comparator). Every difference is listed and can be gone to, first to last. Both sides
/// scroll together because each row holds both. The summary claims "identical" only when every byte matched; a region
/// that could not be aligned within limits is shown and labelled, never presented as exact. A file that changes after
/// it was compared is reported when the window is activated again, and F5 compares anew.
/// </summary>
public sealed class CompareWindow : Window
{
    private static readonly IBrush LeftOnlyBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xE0, 0x40, 0x40));
    private static readonly IBrush RightOnlyBrush = new SolidColorBrush(Color.FromArgb(0x38, 0x40, 0xC0, 0x40));
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xB0, 0x30));
    private static readonly IBrush UnalignedBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x90, 0x90, 0xA0));
    private static readonly IBrush InlineBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xE0, 0x90, 0x20));
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,monospace");
    /// <summary>At most this many differences are listed to choose from; the arrows go through all of them.</summary>
    public const int MaxListed = 20_000;
    private const string BytesHint = "Drag over bytes to select them; Ctrl+C copies them as hex. Clicking a difference makes it the current one.";

    private IContentSource _left, _right;
    private ViewSource _leftView, _rightView;
    private readonly string _leftName, _rightName;
    private readonly Func<(IContentSource Left, IContentSource Right)>? _reopen;
    private readonly Func<Task<(IContentSource Left, IContentSource Right)>>? _reopenAsync;
    private bool CanReopen => _reopen is not null || _reopenAsync is not null;
    private readonly ListBox _rows = new();
    private readonly HexCompareView _hex = new() { IsVisible = false };
    private readonly ComboBox _differenceBox = new() { MinWidth = 320, MaxWidth = 560, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _changed = new() { TextWrapping = TextWrapping.Wrap, Classes = { "warning" } };
    private readonly TextBlock _status = new() { TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "muted" } };
    private readonly Border _changedBanner, _statusBar;
    private readonly CheckBox _ignoreWhitespace = new() { Content = "Ignore whitespace", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _ignoreCase = new() { Content = "Ignore case", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _binary = new() { Content = "Binary", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _collapse = new() { Content = "Hide equal lines", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _align = new() { Content = "Align shifted bytes", VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly Button _first, _previous, _next, _last;
    private readonly Button _again = new() { Content = "Compare again", VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _work;
    private Task _loading = Task.CompletedTask;
    private BinaryDiffResult? _bytes;
    private (PagedReader Left, PagedReader Right)? _pages;
    /// <summary>
    /// The aligned comparison of the contents now shown, once asked for: computed off the UI thread, kept while the
    /// contents stay, and stopped when its view is left before it is ready (large files take a while).
    /// </summary>
    private Task<(AlignedBinaryResult Result, List<AlignedRange> Differences)>? _aligning;
    private CancellationTokenSource? _aligningStop;
    /// <summary>Every comparison, aligning and search for differences started, stopped ones too: the contents are released only after they stopped reading.</summary>
    private Task _runs = Task.CompletedTask;
    private TextSide? _leftText, _rightText;
    private string? _textProblem;
    private (ContentRevision? Left, ContentRevision? Right) _revisions;
    private bool _checkingInputs;
    private bool _closed, _reopening, _binaryForced, _settingOptions, _settingDifference, _searching, _contentFocused;
    private int _view;
    private Shown _shown;

    /// <summary>What the window shows: the lines, the bytes at the same offsets, or the bytes aligned.</summary>
    private enum Shown { Nothing, Text, Bytes, Aligned }

    // The differences of the view shown. Text: the row each starts on. Bytes: the differing runs listed, in file order
    // (at the same offsets, the first ones found; more are searched for past them when the comparison had too many to
    // list). Aligned: every stretch that is not equal.
    private List<int> _differenceRows = [];
    private List<AlignedRange> _differences = [];
    private List<string> _descriptions = [];
    private bool _moreDifferences;
    /// <summary>The current difference: its index among those listed, or -1 (none, or one found past them: <see cref="_pastListed"/>).</summary>
    private int _current = -1;
    private AlignedRange? _pastListed;

    private static readonly List<CompareWindow> s_open = [];

    /// <summary>Open comparisons, oldest first.</summary>
    public static IReadOnlyList<CompareWindow> OpenWindows => s_open;

    /// <summary>
    /// Opens a comparison of two contents (opened by the caller off the UI thread); the window disposes them.
    /// <paramref name="reopen"/> opens both again off the UI thread. <paramref name="reopenAsync"/> instead lets a provider
    /// factory retain its own asynchronous admission and ownership boundaries. Without either the window cannot compare anew.
    /// </summary>
    public static CompareWindow Open(string leftName, IContentSource left, string rightName, IContentSource right,
        Func<(IContentSource Left, IContentSource Right)>? reopen = null,
        Func<Task<(IContentSource Left, IContentSource Right)>>? reopenAsync = null)
    {
        var window = new CompareWindow(leftName, left, rightName, right, reopen, reopenAsync);
        s_open.Add(window);
        window.Closed += (_, _) => s_open.Remove(window);
        window.Show();
        return window;
    }

    private CompareWindow(string leftName, IContentSource left, string rightName, IContentSource right,
        Func<(IContentSource, IContentSource)>? reopen, Func<Task<(IContentSource, IContentSource)>>? reopenAsync)
    {
        _left = left;
        _right = right;
        _leftView = new ViewSource(left);
        _rightView = new ViewSource(right);
        _leftName = leftName;
        _rightName = rightName;
        _reopen = reopen;
        _reopenAsync = reopenAsync;
        Title = $"Compare: {Path.GetFileName(leftName.TrimEnd('/', '\\'))} ↔ {Path.GetFileName(rightName.TrimEnd('/', '\\'))}";
        Width = 1200; Height = 760; MinWidth = 640; MinHeight = 320;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        _first = NavigationButton("compare.first", "First difference", "Alt+Home", GoFirst);
        _previous = NavigationButton("compare.previous", "Previous difference", "Alt+Up or Shift+F8", () => Go(-1));
        _next = NavigationButton("compare.next", "Next difference", "Alt+Down or F8", () => Go(+1));
        _last = NavigationButton("compare.last", "Last difference", "Alt+End", GoLast);
        Avalonia.Automation.AutomationProperties.SetName(_rows, "Differences");
        Avalonia.Automation.AutomationProperties.SetName(_hex, "Bytes of both files");
        Avalonia.Automation.AutomationProperties.SetName(_differenceBox, "Difference");
        ToolTip.SetTip(_differenceBox, "Go to a difference (Alt+D)");
        ToolTip.SetTip(_again, "Read both files again and compare them (F5)");
        _again.IsVisible = CanReopen;
        _rows.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            CompareRow row => BuildRow(row),
            string text => new TextBlock { Text = text, FontFamily = Mono, Margin = new Thickness(4, 1) },
            _ => new TextBlock(),
        });
        // Long paths lose their middle, not the file name at their end.
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(8, 4) };
        var leftHeader = new TextBlock { Text = leftName, TextTrimming = TextTrimming.PrefixCharacterEllipsis, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        var rightHeader = new TextBlock { Text = rightName, TextTrimming = TextTrimming.PrefixCharacterEllipsis, FontWeight = FontWeight.SemiBold };
        ToolTip.SetTip(leftHeader, leftName);
        ToolTip.SetTip(rightHeader, rightName);
        Grid.SetColumn(rightHeader, 1);
        header.Children.Add(leftHeader);
        header.Children.Add(rightHeader);
        var bar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6, 8, 2),
            Children = { _first, _previous, _next, _last, _differenceBox, _again, _binary, _align, _ignoreWhitespace, _ignoreCase, _collapse },
        };
        foreach (var child in bar.Children.OfType<Control>()) child.Margin = new Thickness(0, 0, child is Button { Content: Image } ? 2 : 10, 4);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        var summaryBorder = new Border { Child = _summary, Padding = new Thickness(8, 4), Classes = { "banner" } };
        DockPanel.SetDock(summaryBorder, Dock.Top);
        _changedBanner = new Border { Child = _changed, Padding = new Thickness(8, 4), Classes = { "banner" }, IsVisible = false };
        DockPanel.SetDock(_changedBanner, Dock.Top);
        _statusBar = new Border { Child = _status, Padding = new Thickness(8, 3), IsVisible = false };
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(summaryBorder);
        root.Children.Add(_changedBanner);
        root.Children.Add(header);
        root.Children.Add(_statusBar);
        var body = new Panel { Children = { _rows, _hex } };
        root.Children.Add(body);
        Content = ThemeLayers.Over(root, body);

        _again.Click += (_, _) => CompareAgain();
        foreach (var box in new[] { _ignoreWhitespace, _ignoreCase, _binary, _collapse, _align }) box.IsCheckedChanged += (_, _) => Recompute();
        ToolTip.SetTip(_align, "Finds content that moved because bytes were inserted or removed, instead of comparing at the same offsets. A heuristic view: whether the files are identical is the exact comparison's answer.");
        _differenceBox.SelectionChanged += (_, _) =>
        {
            if (!_settingDifference && _differenceBox.SelectedIndex >= 0) GoTo(_differenceBox.SelectedIndex);
        };
        _rows.SelectionChanged += (_, _) => FollowRow();
        _hex.DifferenceClicked += index => ShowDifference(index);
        _hex.StatusChanged += () => _status.Text = _hex.Status.Length > 0 ? _hex.Status : BytesHint;
        _status.Text = BytesHint;
        // Navigation keys work wherever the focus is (the list and the difference box have their own uses for arrows).
        AddHandler(KeyDownEvent, OnNavigationKey, RoutingStrategies.Tunnel);
        KeyDown += OnKey;
        Activated += (_, _) => CheckInputs();
        ThemeManager.ThemeChanged += RefreshIcons;
        Closed += (_, _) =>
        {
            _closed = true;
            ThemeManager.ThemeChanged -= RefreshIcons;
            _work?.Cancel();
            ReleaseWhenIdle(_left, _right, _leftView, _rightView);
            DisposePages(_pages);
        };
        _summary.Text = "Comparing…";
        Opened += (_, _) => _loading = LoadAsync();
    }

    private static Button NavigationButton(string icon, string title, string keys, Action run)
    {
        var button = new Button
        {
            Content = new Image { Source = CommandIcons.Get(icon), Width = 16, Height = 16 },
            Tag = icon,
            Padding = new Thickness(6, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, $"{title} ({keys})");
        Avalonia.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => run();
        return button;
    }

    private void RefreshIcons()
    {
        foreach (var button in new[] { _first, _previous, _next, _last })
            if (button is { Tag: string icon, Content: Image image }) image.Source = CommandIcons.Get(icon);
    }

    public string Summary => _summary.Text ?? "";

    /// <summary>Whether the window says that a file changed after it was compared (tests).</summary>
    public string? ChangedNotice => _changedBanner.IsVisible ? _changed.Text : null;

    /// <summary>Whether the files are still being read or their lines aligned (the rows and summary are not final yet).</summary>
    public bool IsComparing { get; private set; } = true;

    /// <summary>Whether a difference past the listed ones is being searched for.</summary>
    public bool IsSearching => _searching;

    /// <summary>The text view's rows.</summary>
    public IReadOnlyList<object> Rows => _rows.ItemsSource as IReadOnlyList<object> ?? [];

    /// <summary>The row the text comparison shows as current (the first difference when it opens).</summary>
    public int CurrentRow => _rows.SelectedIndex;

    /// <summary>The differences listed to choose from, as the difference box says them.</summary>
    public IReadOnlyList<string> DifferenceTexts => _descriptions;

    /// <summary>The current difference's index among those listed; -1 when there is none or it was found past them.</summary>
    public int CurrentDifference => _current;

    /// <summary>What the difference box says about the current difference.</summary>
    public string CurrentDifferenceText =>
        _current >= 0 && _current < _descriptions.Count ? _descriptions[_current] : _differenceBox.PlaceholderText ?? "";

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        Close();
        e.Handled = true;
    }

    private void OnNavigationKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Home when e.KeyModifiers == KeyModifiers.Alt:
                GoFirst();
                break;
            case Key.End when e.KeyModifiers == KeyModifiers.Alt:
                GoLast();
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.Alt:
            case Key.F8 when e.KeyModifiers == KeyModifiers.None:
                Go(+1);
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
            case Key.F8 when e.KeyModifiers == KeyModifiers.Shift:
                Go(-1);
                break;
            case Key.D when e.KeyModifiers == KeyModifiers.Alt:
                _differenceBox.Focus();
                _differenceBox.IsDropDownOpen = _differenceBox.ItemCount > 0;
                break;
            case Key.F5 when e.KeyModifiers == KeyModifiers.None && CanReopen:
            case Key.R when e.KeyModifiers == KeyModifiers.Control && CanReopen:
                CompareAgain();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>A comparison opens on its first difference: the equal lines or bytes before it are what the user did not come for.</summary>
    private void ShowFirstDifference()
    {
        int view = _view;
        // After layout, so the view can scroll there.
        Dispatcher.UIThread.Post(() =>
        {
            if (view != _view || _closed) return;
            // Unless a difference was chosen meanwhile: the user's choice stands.
            if (_current < 0 && _pastListed is null && (_shown == Shown.Text ? _differenceRows.Count > 0 : _differences.Count > 0)) GoTo(0);
            if (!_contentFocused && IsActive)
            {
                // The first view takes the keys (arrows and Page Down scroll); later ones leave the focus where it is.
                _contentFocused = true;
                (_shown == Shown.Text ? (Control)_rows : _hex).Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Goes to the listed difference <paramref name="index"/> (the difference box's choice).</summary>
    public void GoTo(int index)
    {
        if (_shown == Shown.Text)
        {
            if (index < 0 || index >= _differenceRows.Count) return;
            int row = _differenceRows[index];
            _rows.SelectedIndex = row;
            _rows.ScrollIntoView(row);
            SetCurrent(index, null);
        }
        else ShowDifference(index);
    }

    public void GoFirst()
    {
        if (_shown == Shown.Text || _differences.Count > 0) GoTo(0);
    }

    /// <summary>
    /// The last difference. When more differences exist than were listed, the last one is found by reading backwards
    /// from the end of the files (it is not numbered then: the ones between were not counted).
    /// </summary>
    public void GoLast()
    {
        if (_shown == Shown.Text) GoTo(_differenceRows.Count - 1);
        else if (_moreDifferences && _bytes is { } bytes) Search(forward: false, Math.Max(bytes.LeftLength, bytes.RightLength));
        else GoTo(_differences.Count - 1);
    }

    /// <summary>Moves to the next or previous difference (wrapping around at either end).</summary>
    public void Go(int direction)
    {
        if (_shown == Shown.Text)
        {
            if (_differenceRows.Count == 0) return;
            int current = _rows.SelectedIndex;
            int target = direction > 0
                ? _differenceRows.FindIndex(r => r > current)
                : _differenceRows.FindLastIndex(r => r < current);
            GoTo(target >= 0 ? target : direction > 0 ? 0 : _differenceRows.Count - 1);
            return;
        }
        if (_shown is not (Shown.Bytes or Shown.Aligned) || _differences.Count == 0 && !_moreDifferences) return;
        if (direction > 0)
        {
            if (_current >= 0 && _current + 1 < _differences.Count) ShowDifference(_current + 1);
            else if (_current < 0 && _pastListed is null) GoFirst();
            else if (_moreDifferences && _bytes is not null)
            {
                var from = _pastListed ?? _differences[^1];
                Search(forward: true, from.LeftOffset + Math.Max(from.LeftLength, from.RightLength));
            }
            else GoFirst();
        }
        else
        {
            if (_current > 0) ShowDifference(_current - 1);
            else if (_pastListed is { } past) Search(forward: false, past.LeftOffset);
            else GoLast();
        }
    }

    /// <summary>Shows the listed binary difference <paramref name="index"/> as the current one.</summary>
    private void ShowDifference(int index)
    {
        if (index < 0 || index >= _differences.Count) return;
        SetCurrent(index, null);
        _hex.ShowDifference(_differences[index]);
    }

    /// <summary>The difference box shows the current difference: a listed one, or one found past them.</summary>
    private void SetCurrent(int index, string? pastListed)
    {
        _current = index;
        _settingDifference = true;
        try
        {
            bool listed = index >= 0 && index < _differenceBox.ItemCount;
            _differenceBox.SelectedIndex = listed ? index : -1;
            _differenceBox.PlaceholderText = listed ? null : index >= 0 ? _descriptions[index] : pastListed;
        }
        finally
        {
            _settingDifference = false;
        }
        if (index >= 0) _pastListed = null;
    }

    /// <summary>A row chosen in the text view makes the difference it is in (or the last one before it) current.</summary>
    private void FollowRow()
    {
        if (_shown != Shown.Text || _settingDifference) return;
        int row = _rows.SelectedIndex;
        int index = _differenceRows.FindLastIndex(r => r <= row);
        if (index != _current && index >= 0) SetCurrent(index, null);
    }

    /// <summary>
    /// Past the listed differences (a comparison with more than <see cref="BinaryDiff.MaxRanges"/>), the next or previous
    /// one is found by reading on from <paramref name="from"/>, off the UI thread. One found right after the last listed
    /// one is listed too; others are shown without a number.
    /// </summary>
    private async void Search(bool forward, long from)
    {
        if (_searching || _bytes is not { } bytes || _closed) return;
        _searching = true;
        int view = _view;
        var (left, right) = (_left, _right);
        var ct = _work?.Token ?? CancellationToken.None;
        bool afterListed = forward && (_current == _differences.Count - 1 && _current >= 0);
        _status.Text = forward ? "Looking for the next difference…" : "Looking for the previous difference…";
        var run = Task.Run(() => forward
            ? BinaryDiff.NextDifference(left, right, bytes.LeftLength, bytes.RightLength, from, ct)
            : BinaryDiff.PreviousDifference(left, right, bytes.LeftLength, bytes.RightLength, from, ct), ct);
        _runs = Task.WhenAll(_runs, run);
        (long Offset, long Length)? found;
        try
        {
            found = await run;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            if (!_closed && view == _view) _status.Text = ex is OperationCanceledException ? BytesHint : "The files could not be read: " + ex.Message;
            return;
        }
        finally
        {
            _searching = false;
        }
        if (_closed || view != _view) return;
        _status.Text = _hex.Status.Length > 0 ? _hex.Status : BytesHint;
        if (found is not { } f)
        {
            // Nothing further that way: the listed ones were all when nothing follows the last of them.
            if (afterListed)
            {
                _moreDifferences = false;
                SetSummary(bytes);
            }
            if (forward) GoFirst();
            else if (_differences.Count > 0) ShowDifference(_differences.Count - 1);
            return;
        }
        var difference = SameOffsetDifference(f, bytes);
        int index = _differences.FindIndex(d => d.LeftOffset == difference.LeftOffset);
        if (index < 0 && afterListed)
        {
            // Right after the last listed one: it is the next in order, so it is listed (and numbered) too.
            _differences.Add(difference);
            _descriptions.Add(Describe(difference, _differences.Count - 1));
            SetDifferences(keepCurrent: true);
            index = _differences.Count - 1;
        }
        if (index >= 0)
        {
            ShowDifference(index);
            return;
        }
        SetCurrent(-1, Describe(difference, -1));
        _pastListed = difference;
        _hex.ShowDifference(difference);
    }

    /// <summary>A differing run of the exact comparison: changed in place, or past the shorter file's end, only in the longer one.</summary>
    private static AlignedRange SameOffsetDifference((long Offset, long Length) run, BinaryDiffResult bytes)
    {
        long common = Math.Min(bytes.LeftLength, bytes.RightLength);
        if (run.Offset >= common && bytes.LeftLength != bytes.RightLength)
            return bytes.LeftLength > bytes.RightLength
                ? new AlignedRange(DiffKind.LeftOnly, run.Offset, run.Length, run.Offset, 0)
                : new AlignedRange(DiffKind.RightOnly, run.Offset, 0, run.Offset, run.Length);
        return new AlignedRange(DiffKind.Changed, run.Offset, run.Length, run.Offset, run.Length);
    }

    /// <summary>How the difference box says a binary difference ("2: 1 byte changed at offset 0x2AAC4").</summary>
    private string Describe(AlignedRange d, int index)
    {
        string number = index >= 0 ? $"{index + 1:N0}: " : "Past the listed ones: ";
        string text = d.Kind switch
        {
            DiffKind.Changed when d.LeftOffset == d.RightOffset && d.LeftLength == d.RightLength => $"{Bytes(d.LeftLength)} changed at offset 0x{d.LeftOffset:X}",
            DiffKind.Changed => $"{Bytes(d.LeftLength, d.RightLength)} changed at 0x{d.LeftOffset:X} ↔ 0x{d.RightOffset:X}",
            DiffKind.LeftOnly when _shown == Shown.Bytes => $"{Bytes(d.LeftLength)} only in the left file, from offset 0x{d.LeftOffset:X}",
            DiffKind.RightOnly when _shown == Shown.Bytes => $"{Bytes(d.RightLength)} only in the right file, from offset 0x{d.RightOffset:X}",
            DiffKind.LeftOnly => $"{Bytes(d.LeftLength)} removed (only left) at 0x{d.LeftOffset:X}",
            DiffKind.RightOnly => $"{Bytes(d.RightLength)} inserted (only right) at 0x{d.RightOffset:X}",
            _ => $"{d.LeftLength:N0} ↔ {d.RightLength:N0} bytes not aligned at 0x{d.LeftOffset:X} ↔ 0x{d.RightOffset:X}",
        };
        return number + text;
    }

    /// <summary>The difference box lists the view's differences (the first <see cref="MaxListed"/>).</summary>
    private void SetDifferences(bool keepCurrent = false)
    {
        int current = _current;
        _settingDifference = true;
        try
        {
            _differenceBox.ItemsSource = _descriptions.Count > MaxListed ? _descriptions.GetRange(0, MaxListed) : _descriptions.ToList();
            _differenceBox.IsEnabled = _descriptions.Count > 0 || _moreDifferences;
            _differenceBox.PlaceholderText = _descriptions.Count == 0 ? "No differences" : null;
        }
        finally
        {
            _settingDifference = false;
        }
        foreach (var button in new[] { _first, _previous, _next, _last }) button.IsEnabled = _descriptions.Count > 0;
        if (keepCurrent && current >= 0) SetCurrent(current, null);
        else _current = -1;
        _pastListed = null;
    }

    /// <summary>
    /// Compares the contents the window holds now. Everything that reads them runs off the UI thread: servers and
    /// phones answer slowly, and a large file takes a while (the summary shows how far it got).
    /// </summary>
    private async Task LoadAsync()
    {
        _work?.Cancel();
        var work = new CancellationTokenSource();
        _work = work;
        var ct = work.Token;
        var (left, right, leftView, rightView) = (_left, _right, _leftView, _rightView);
        IsComparing = true;
        _summary.Text = "Comparing…";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long shown = 0;
        void Progress(long done, long total)
        {
            // Every megabyte reports; the window shows it a few times a second.
            if (clock.ElapsedMilliseconds - shown < 250) return;
            shown = clock.ElapsedMilliseconds;
            string text = total > 0
                ? $"Comparing… {Formatters.SizeWithUnit(done)} of {Formatters.SizeWithUnit(total)} ({Math.Min(100, done * 100 / total)}%)"
                : $"Comparing… {Formatters.SizeWithUnit(done)}";
            Dispatcher.UIThread.Post(() =>
            {
                if (!ct.IsCancellationRequested) _summary.Text = text;
            });
        }
        try
        {
            // Every byte decides equality; text decoding and alignment are separate, labelled views. The reading counts
            // among the runs the contents wait for, so closing releases them once it stopped, whatever this thread does.
            var reading = Task.Run(() =>
            {
                var revisions = (Revision(left), Revision(right));
                long total = Math.Max(left.Length, right.Length);
                var bytes = BinaryDiff.Compare(left, right, ct, done => Progress(done, total));
                // The byte view reads through pages of its own, a page at a time as it is scrolled.
                var pages = (new PagedReader(leftView, 64), new PagedReader(rightView, 64));
                TextSide? l = null, r = null;
                string? problem = null;
                try
                {
                    l = TextSide.Load(left, ct);
                    r = TextSide.Load(right, ct);
                    if (l.Encoding.LooksBinary || r.Encoding.LooksBinary)
                    {
                        problem = "At least one file looks binary, so it is compared byte by byte.";
                        l = r = null;
                    }
                }
                catch (InvalidDataException ex)
                {
                    problem = ex.Message;
                    l = r = null;
                }
                return (bytes, pages, l, r, problem, revisions);
            }, ct);
            _runs = Task.WhenAll(_runs, reading);
            var loaded = await reading;
            if (_closed || ct.IsCancellationRequested)
            {
                DisposePages(loaded.pages);
                return;
            }
            var replaced = _pages;
            (_bytes, _pages, _leftText, _rightText, _textProblem, _revisions) = loaded;
            DisposePages(replaced);
            _aligning = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            if (_closed || ct.IsCancellationRequested) return;
            _summary.Text = "The files could not be read: " + ex.Message;
            IsComparing = false;
            return;
        }
        // Without text on both sides only the bytes can be shown; a later comparison with text shows text again.
        bool text = _leftText is not null && _rightText is not null;
        _binary.IsEnabled = text;
        _settingOptions = true;
        if (!text)
        {
            _binaryForced |= _binary.IsChecked != true;
            _binary.IsChecked = true;
        }
        else if (_binaryForced)
        {
            _binaryForced = false;
            _binary.IsChecked = false;
        }
        _settingOptions = false;
        Recompute();
    }

    private static ContentRevision? Revision(IContentSource source)
    {
        try { return source.GetRevision(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or NotSupportedException) { return null; }
    }

    /// <summary>
    /// Contents are disposed once nothing reads them any more: a comparison, aligning or search stops at its next
    /// megabyte, and the byte view's page reads are let finish (and later ones refused). Only the readers count, not
    /// what this window's thread still has to do with their results: until the contents are disposed, Windows refuses
    /// to replace the files (release issue I22: a Synchronize right after closing a comparison failed).
    /// </summary>
    private void ReleaseWhenIdle(IContentSource left, IContentSource right, ViewSource leftView, ViewSource rightView)
    {
        void Release()
        {
            left.Dispose();
            right.Dispose();
        }
        var readers = Task.WhenAll(_runs, leftView.CloseAsync(), rightView.CloseAsync());
        if (readers.IsCompleted) Release();
        else readers.ContinueWith(_ => Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// The byte view's page readers count against the shared content budget until disposed. Disposing them leaves the
    /// contents to <see cref="ReleaseWhenIdle"/>: their sources here are views whose Dispose does nothing.
    /// </summary>
    private static void DisposePages((PagedReader Left, PagedReader Right)? pages)
    {
        pages?.Left.Dispose();
        pages?.Right.Dispose();
    }

    /// <summary>F5: both files are opened and compared again (after they were edited, say).</summary>
    public async void CompareAgain()
    {
        if (!CanReopen || _closed || _reopening) return;
        _reopening = true;
        _again.IsEnabled = false;
        _work?.Cancel();
        _summary.Text = "Reading the files again…";
        try
        {
            var (left, right) = await (_reopenAsync is not null ? _reopenAsync() : Task.Run(_reopen!));
            if (_closed)
            {
                left.Dispose();
                right.Dispose();
                return;
            }
            ReleaseWhenIdle(_left, _right, _leftView, _rightView);
            (_left, _right) = (left, right);
            (_leftView, _rightView) = (new ViewSource(left), new ViewSource(right));
            _changedBanner.IsVisible = false;
            _loading = LoadAsync();
            await _loading;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or OperationCanceledException)
        {
            if (!_closed) _summary.Text = "The files could not be read again: " + (ex is OperationCanceledException ? "canceled." : ex.Message);
        }
        finally
        {
            _reopening = false;
            _again.IsEnabled = true;
        }
    }

    /// <summary>
    /// Plan §16.2: a comparison never passes off files that changed since as compared. Coming back to the window
    /// (from the editor, say) checks both, and a change is said above the rows until the files are compared again.
    /// </summary>
    private async void CheckInputs()
    {
        if (_closed || _reopening || _checkingInputs || _bytes is null || _changedBanner.IsVisible || _revisions is (null, null)) return;
        _checkingInputs = true;
        try
        {
            var (left, right) = (_leftView, _rightView);
            var before = _revisions;
            var now = await Task.Run(() => (Revision(left), Revision(right)));
            if (_closed || !ReferenceEquals(left, _leftView) || !ReferenceEquals(right, _rightView)) return;
            if (before.Left is not null && now.Item1 is null || before.Right is not null && now.Item2 is null)
            {
                _changed.Text = "The files' current state could not be checked: what is shown is the earlier comparison. " +
                                (!CanReopen ? "Close this window and compare the files again." : "F5 compares them again.");
                _changedBanner.IsVisible = true;
                return;
            }
            var changed = new List<string>();
            if (before.Left is { } l && now.Item1 != l) changed.Add(Path.GetFileName(_leftName.TrimEnd('/', '\\')));
            if (before.Right is { } r && now.Item2 != r) changed.Add(Path.GetFileName(_rightName.TrimEnd('/', '\\')));
            if (changed.Count == 0) return;
            _changed.Text = (changed.Count == 2 ? "Both files changed" : $"\"{changed[0]}\" changed") + " after they were compared: what is shown is the earlier content. " +
                            (!CanReopen ? "Close this window and compare the files again." : "F5 compares them again.");
            _changedBanner.IsVisible = true;
        }
        finally
        {
            _checkingInputs = false;
        }
    }

    /// <summary>Shows the comparison with the current options; lines are aligned off the UI thread (a million take a second).</summary>
    private async void Recompute()
    {
        if (_bytes is not { } bytes || _settingOptions) return;
        int view = ++_view;
        bool binary = _binary.IsChecked == true || _leftText is null || _rightText is null;
        // Options show where they apply: aligning to bytes, the rest to lines.
        _align.IsVisible = binary;
        _ignoreWhitespace.IsVisible = _ignoreCase.IsVisible = _collapse.IsVisible = !binary;
        // Identical files need no aligning: the exact comparison says so.
        bool aligned = binary && _align.IsChecked == true && !bytes.Equal;
        if (!aligned && _aligning is { IsCompleted: false })
        {
            _aligningStop?.Cancel();
            _aligning = null;
        }
        if (aligned)
        {
            await ShowAlignedAsync(view);
            return;
        }
        if (binary || _leftText is not { } left || _rightText is not { } right)
        {
            ShowBinary(bytes);
            return;
        }
        var options = new TextDiffOptions(_ignoreWhitespace.IsChecked == true, _ignoreCase.IsChecked == true);
        bool collapse = _collapse.IsChecked == true;
        IsComparing = true;
        if (_summary.Text?.StartsWith("Comparing", StringComparison.Ordinal) == true) _summary.Text = "Comparing lines…";
        var shown = await Task.Run(() => BuildText(left, right, bytes, options, collapse));
        // Options changed meanwhile, or the files were compared again: a newer view is on its way.
        if (view != _view || _closed) return;
        Show(Shown.Text);
        _rows.ItemsSource = shown.Rows;
        _differenceRows = shown.Differences;
        _descriptions = shown.Descriptions;
        _moreDifferences = false;
        SetDifferences();
        ShowFirstDifference();
        _summary.Text = shown.Summary;
        IsComparing = false;
    }

    /// <summary>The text rows, or the bytes (with the status line under them).</summary>
    private void Show(Shown shown)
    {
        _shown = shown;
        bool text = shown == Shown.Text;
        _rows.IsVisible = text;
        _hex.IsVisible = !text;
        _statusBar.IsVisible = !text;
        if (text) _hex.SetContent(null, null, null, []);
        else _rows.ItemsSource = null;
    }

    private void ShowBinary(BinaryDiffResult bytes)
    {
        _view++;
        Show(Shown.Bytes);
        _differences = [.. bytes.Ranges.Select(r => SameOffsetDifference(r, bytes))];
        _descriptions = [.. _differences.Select(Describe)];
        _moreDifferences = bytes.RangesTruncated;
        SetDifferences();
        _hex.SetContent(_pages?.Left, _pages?.Right, null, _differences);
        ShowFirstDifference();
        SetSummary(bytes);
        IsComparing = false;
    }

    private void SetSummary(BinaryDiffResult bytes)
    {
        string note = _textProblem is { } p && _leftText is null ? " " + p : "";
        _summary.Text = bytes.Equal
            ? $"Identical: every byte was compared ({bytes.LeftLength:N0} bytes).{note}"
            : $"{_differences.Count:N0}{(_moreDifferences ? "+" : "")} differing byte ranges at the same offsets" +
              (bytes.LeftLength != bytes.RightLength ? $"; lengths {bytes.LeftLength:N0} and {bytes.RightLength:N0} bytes" : "") +
              (_moreDifferences ? $"; the first {_differences.Count:N0} are listed, and Next difference finds the rest" : "") +
              ". Shifted content shows as differences from the shift on (Align shifted bytes finds it again)." + note;
    }

    /// <summary>
    /// Binary content aligned where bytes were inserted or removed (plan §16.2): the stretches side by side, every
    /// stretch that is not equal is a difference to go to, and the summary says what was found and that the pairing is
    /// heuristic.
    /// </summary>
    private async Task ShowAlignedAsync(int view)
    {
        IsComparing = true;
        var task = _aligning ??= StartAligning();
        if (!task.IsCompleted) _summary.Text = "Aligning shifted bytes…";
        (AlignedBinaryResult Result, List<AlignedRange> Differences) shown;
        try
        {
            shown = await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Asking again tries again.
            if (ReferenceEquals(task, _aligning)) _aligning = null;
            if (view != _view || _closed) return;
            _summary.Text = "The files could not be read for aligning: " + ex.Message;
            IsComparing = false;
            return;
        }
        if (view != _view || _closed) return;
        var (aligned, differences) = shown;
        Show(Shown.Aligned);
        _differences = differences;
        _descriptions = [.. differences.Select(Describe)];
        _moreDifferences = false;
        SetDifferences();
        _hex.SetContent(_pages?.Left, _pages?.Right, aligned.Ranges, differences);
        ShowFirstDifference();
        _summary.Text = AlignedSummary(aligned, differences.Count);
        IsComparing = false;
    }

    private Task<(AlignedBinaryResult Result, List<AlignedRange> Differences)> StartAligning()
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_work?.Token ?? CancellationToken.None);
        _aligningStop = stop;
        var ct = stop.Token;
        var (left, right) = (_left, _right);
        long total = Math.Max(1, left.Length + right.Length);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long shown = 0;
        var run = Task.Run(() =>
        {
            var result = AlignedBinaryDiff.Compare(left, right, ct, done =>
            {
                // Reported every megabyte; shown a few times a second.
                if (clock.ElapsedMilliseconds - shown < 250) return;
                shown = clock.ElapsedMilliseconds;
                string text = $"Aligning shifted bytes… {Math.Min(100, done * 100 / total)}%";
                Dispatcher.UIThread.Post(() =>
                {
                    if (!ct.IsCancellationRequested && !_closed) _summary.Text = text;
                });
            });
            return (result, result.Ranges.Where(r => r.Kind != DiffKind.Equal).ToList());
        }, ct);
        _runs = Task.WhenAll(_runs, run);
        return run;
    }

    /// <summary>"1 byte", "40,000 bytes"; two different counts as "3 → 5 bytes".</summary>
    private static string Bytes(long left, long? right = null) =>
        right is { } r && r != left ? $"{left:N0} → {r:N0} bytes" : left == 1 ? "1 byte" : $"{left:N0} bytes";

    private static string AlignedSummary(AlignedBinaryResult aligned, int differences)
    {
        static string Stretches(IReadOnlyList<AlignedRange> ranges, DiffKind kind, string what)
        {
            var these = ranges.Where(r => r.Kind == kind).ToList();
            if (these.Count == 0) return "none " + what;
            long left = these.Sum(r => r.LeftLength), right = these.Sum(r => r.RightLength);
            string bytes = kind switch
            {
                DiffKind.LeftOnly => Bytes(left),
                DiffKind.RightOnly => Bytes(right),
                _ => Bytes(left, right),
            };
            return $"{these.Count:N0} {(these.Count == 1 ? "stretch" : "stretches")} {what} ({bytes})";
        }
        var summary = new StringBuilder("Aligned: ")
            .Append(Stretches(aligned.Ranges, DiffKind.RightOnly, "inserted")).Append(", ")
            .Append(Stretches(aligned.Ranges, DiffKind.LeftOnly, "removed")).Append(", ")
            .Append(Stretches(aligned.Ranges, DiffKind.Changed, "changed"));
        if (aligned.LeftLength > 0)
        {
            // Never rounded up to "all" when a byte is missing.
            double found = aligned.EqualBytes == aligned.LeftLength ? 100 : Math.Min(99.99, Math.Floor(aligned.EqualBytes * 10_000.0 / aligned.LeftLength) / 100);
            summary.Append($"; {found:0.##}% of the left file was found again in order");
        }
        summary.Append('.');
        if (!aligned.Complete) summary.Append(" The work limit was reached: the rest is not aligned.");
        if (differences > MaxListed) summary.Append($" The first {MaxListed:N0} differences are listed; the arrows go through all of them.");
        summary.Append($" Heuristic: matched in blocks of {aligned.BlockSize:N0} bytes, so shorter equal stretches and moved or repeated content can be paired differently.");
        return summary.ToString();
    }

    private static (List<object> Rows, List<int> Differences, List<string> Descriptions, string Summary) BuildText(TextSide left, TextSide right, BinaryDiffResult bytes,
        TextDiffOptions options, bool collapse)
    {
        var result = TextDiff.Compare(left.Lines, right.Lines, options);
        var rows = new List<object>();
        var diff = new List<int>();
        var descriptions = new List<string>();
        int endings = 0, changed = 0, leftOnly = 0, rightOnly = 0;
        static string Lines(int n) => n == 1 ? "1 line" : $"{n:N0} lines";
        foreach (var block in result.Blocks)
        {
            if (block.Kind != DiffKind.Equal)
            {
                diff.Add(rows.Count);
                int l = block.LeftStart + 1, r = block.RightStart + 1;
                descriptions.Add($"{diff.Count:N0}: " + block.Kind switch
                {
                    DiffKind.Changed => $"{Lines(block.LeftCount)} changed at line {l:N0}" + (r != l ? $" (right {r:N0})" : ""),
                    DiffKind.LeftOnly => $"{Lines(block.LeftCount)} only left, at line {l:N0}",
                    DiffKind.RightOnly => $"{Lines(block.RightCount)} only right, at line {r:N0}",
                    _ => $"{block.LeftCount:N0} left and {block.RightCount:N0} right lines not aligned, at line {l:N0}",
                });
            }
            switch (block.Kind)
            {
                case DiffKind.Equal:
                    for (int i = 0; i < block.LeftCount; i++)
                    {
                        bool ending = TextDiff.TerminatorDiffers(left, block.LeftStart + i, right, block.RightStart + i);
                        if (ending) endings++;
                        if (collapse && !ending && block.LeftCount > 7 && i >= 3 && i < block.LeftCount - 3)
                        {
                            if (i == 3) rows.Add($"   ⋯ {block.LeftCount - 6:N0} equal lines");
                            continue;
                        }
                        rows.Add(new CompareRow(DiffKind.Equal, block.LeftStart + i + 1, left.Lines[block.LeftStart + i], block.RightStart + i + 1,
                            right.Lines[block.RightStart + i], ending));
                    }
                    break;
                case DiffKind.Changed:
                    changed += block.LeftCount;
                    for (int i = 0; i < block.LeftCount; i++)
                        rows.Add(new CompareRow(DiffKind.Changed, block.LeftStart + i + 1, left.Lines[block.LeftStart + i], block.RightStart + i + 1, right.Lines[block.RightStart + i]));
                    break;
                case DiffKind.LeftOnly:
                    leftOnly += block.LeftCount;
                    for (int i = 0; i < block.LeftCount; i++) rows.Add(new CompareRow(DiffKind.LeftOnly, block.LeftStart + i + 1, left.Lines[block.LeftStart + i], null, ""));
                    break;
                case DiffKind.RightOnly:
                    rightOnly += block.RightCount;
                    for (int i = 0; i < block.RightCount; i++) rows.Add(new CompareRow(DiffKind.RightOnly, null, "", block.RightStart + i + 1, right.Lines[block.RightStart + i]));
                    break;
                case DiffKind.Unaligned:
                    rows.Add($"   Not aligned line by line (too large to align within limits): {block.LeftCount:N0} left lines, then {block.RightCount:N0} right lines");
                    for (int i = 0; i < block.LeftCount; i++) rows.Add(new CompareRow(DiffKind.Unaligned, block.LeftStart + i + 1, left.Lines[block.LeftStart + i], null, ""));
                    for (int i = 0; i < block.RightCount; i++) rows.Add(new CompareRow(DiffKind.Unaligned, null, "", block.RightStart + i + 1, right.Lines[block.RightStart + i]));
                    break;
            }
        }
        var summary = new StringBuilder();
        if (bytes.Equal) summary.Append($"Identical: every byte was compared ({bytes.LeftLength:N0} bytes).");
        else if (result.Identical)
        {
            // Same text, different bytes: say what differs rather than calling the files identical.
            summary.Append("The text is the same, but the files differ in bytes");
            var reasons = new List<string>();
            if (endings > 0) reasons.Add($"line endings on {endings:N0} lines");
            if (left.Encoding.Encoding.WebName != right.Encoding.Encoding.WebName) reasons.Add($"encoding ({left.Encoding.Encoding.WebName} and {right.Encoding.Encoding.WebName})");
            if (left.Encoding.PreambleLength != right.Encoding.PreambleLength) reasons.Add("a byte-order mark");
            if (options.IgnoreWhitespace || options.IgnoreCase) reasons.Add("ignored whitespace or case");
            summary.Append(reasons.Count > 0 ? ": " + string.Join(", ", reasons) + "." : ". Switch to Binary to see where.");
        }
        else
        {
            summary.Append($"{result.Differences:N0} {(result.Differences == 1 ? "difference" : "differences")}: {changed:N0} changed, {leftOnly:N0} only left, {rightOnly:N0} only right");
            if (endings > 0) summary.Append($"; line endings differ on {endings:N0} other lines");
            summary.Append('.');
            if (result.Approximate) summary.Append(" Some regions were too large to align line by line; they are shown unaligned, not paired.");
            if (result.Heuristic) summary.Append(" Large stretches were matched on lines that occur once on each side, which can show more differences than the fewest possible.");
        }
        return (rows, diff, descriptions, summary.ToString());
    }

    private static IBrush KindBrush(DiffKind kind) => kind switch
    {
        DiffKind.Changed => ChangedBrush,
        DiffKind.LeftOnly => LeftOnlyBrush,
        DiffKind.RightOnly => RightOnlyBrush,
        DiffKind.Unaligned => UnalignedBrush,
        _ => Brushes.Transparent,
    };

    private static Control BuildRow(CompareRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,56,*") };
        grid.Background = KindBrush(row.Kind);
        (IReadOnlyList<(int, int)> Left, IReadOnlyList<(int, int)> Right)? spans = row.Kind == DiffKind.Changed ? InlineDiff.Compare(row.LeftText, row.RightText) : null;
        AddCell(grid, 0, row.LeftLine?.ToString("N0") ?? "", null, number: true);
        AddCell(grid, 1, row.LeftText + (row.EndingDiffers ? "  ⏎≠" : ""), spans?.Left, number: false);
        AddCell(grid, 2, row.RightLine?.ToString("N0") ?? "", null, number: true);
        AddCell(grid, 3, row.RightText, spans?.Right, number: false);
        if (row.EndingDiffers) ToolTip.SetTip(grid, "Same text; the line endings differ (for example CRLF and LF)");
        return grid;
    }

    private static void AddCell(Grid grid, int column, string text, IReadOnlyList<(int Start, int Length)>? spans, bool number)
    {
        var block = new TextBlock
        {
            FontFamily = Mono,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(number ? 0 : 6, 1, number ? 6 : 0, 1),
            HorizontalAlignment = number ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
            Classes = { number ? "muted" : "" },
        };
        if (spans is { Count: > 0 })
        {
            int at = 0;
            var inlines = new InlineCollection();
            foreach (var (start, length) in spans)
            {
                if (start > at) inlines.Add(new Run(text[at..start]));
                inlines.Add(new Run(text.Substring(start, length)) { Background = InlineBrush });
                at = start + length;
            }
            if (at < text.Length) inlines.Add(new Run(text[at..]));
            block.Inlines = inlines;
        }
        else block.Text = text;
        if (!number && text.Length > 80) ToolTip.SetTip(block, text);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    /// <summary>
    /// The byte view and revision checks borrow the content, so it is disposed after active calls finish and further
    /// calls are refused once the window lets go of it (the view's page loads run on pool threads).
    /// </summary>
    private sealed class ViewSource(IContentSource inner) : IContentSource
    {
        private readonly object _lock = new();
        private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCalls;
        private bool _closed;

        public string DisplayName => inner.DisplayName;
        public long Length
        {
            get
            {
                BeginCall();
                try { return inner.Length; }
                finally { EndCall(); }
            }
        }
        public bool CanSeek => inner.CanSeek;
        public string? LocalPath => inner.LocalPath;

        public int Read(long offset, Span<byte> buffer)
        {
            BeginCall();
            try
            {
                return inner.Read(offset, buffer);
            }
            finally
            {
                EndCall();
            }
        }

        public ContentRevision? GetRevision()
        {
            BeginCall();
            try { return inner.GetRevision(); }
            finally { EndCall(); }
        }

        private void BeginCall()
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                _activeCalls++;
            }
        }

        private void EndCall()
        {
            lock (_lock)
            {
                if (--_activeCalls == 0 && _closed) _idle.TrySetResult();
            }
        }

        /// <summary>Refuses further content calls; completes when the ones under way finished.</summary>
        public Task CloseAsync()
        {
            lock (_lock)
            {
                _closed = true;
                if (_activeCalls == 0) _idle.TrySetResult();
            }
            return _idle.Task;
        }

        /// <summary>The window disposes the content itself.</summary>
        public void Dispose() { }
    }
}
