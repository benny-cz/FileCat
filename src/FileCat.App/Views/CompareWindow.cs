using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Compare;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

/// <summary>One row of the side-by-side view: a line on either side (or a gap), how they relate, and a note row's text.</summary>
public sealed record CompareRow(DiffKind Kind, int? LeftLine, string LeftText, int? RightLine, string RightText, bool EndingDiffers = false, string? Note = null);

/// <summary>One stretch of the aligned binary view: how the two files relate there, and the row's text.</summary>
public sealed record AlignedRow(DiffKind Kind, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// File comparison (plan §16.2, TV-08): text side by side with within-line changes, or exact binary ranges. Both sides
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

    private IContentSource _left, _right;
    private readonly string _leftName, _rightName;
    private readonly Func<(IContentSource Left, IContentSource Right)>? _reopen;
    private readonly ListBox _rows = new();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _changed = new() { TextWrapping = TextWrapping.Wrap, Classes = { "warning" } };
    private readonly Border _changedBanner;
    private readonly CheckBox _ignoreWhitespace = new() { Content = "Ignore whitespace", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _ignoreCase = new() { Content = "Ignore case", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _binary = new() { Content = "Binary", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _collapse = new() { Content = "Hide equal lines", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _align = new() { Content = "Align shifted bytes", VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private readonly Button _previous = new() { Content = "Previous difference" };
    private readonly Button _next = new() { Content = "Next difference" };
    private readonly Button _again = new() { Content = "Compare again" };
    private List<int> _differenceRows = [];
    private CancellationTokenSource? _work;
    private Task _loading = Task.CompletedTask;
    private BinaryDiffResult? _bytes;
    private IReadOnlyList<string> _byteRows = [];
    /// <summary>
    /// The aligned comparison of the contents now shown, once asked for: computed off the UI thread, kept while the
    /// contents stay, and stopped when its view is left before it is ready (large files take a while).
    /// </summary>
    private Task<(AlignedBinaryResult Result, List<object> Rows)>? _aligning;
    private CancellationTokenSource? _aligningStop;
    /// <summary>Every aligning started, stopped ones too: the contents are released only after they stopped reading.</summary>
    private Task _aligningRuns = Task.CompletedTask;
    /// <summary>At most this many aligned stretches are listed; the summary counts them all.</summary>
    private const int MaxAlignedRows = 20_000;
    private TextSide? _leftText, _rightText;
    private string? _textProblem;
    private (ContentRevision? Left, ContentRevision? Right) _revisions;
    private bool _closed, _reopening, _binaryForced, _settingOptions;
    private int _view;

    private static readonly List<CompareWindow> s_open = [];

    /// <summary>Open comparisons, oldest first.</summary>
    public static IReadOnlyList<CompareWindow> OpenWindows => s_open;

    /// <summary>
    /// Opens a comparison of two contents (opened by the caller off the UI thread); the window disposes them.
    /// <paramref name="reopen"/> opens both again for "Compare again" (it runs off the UI thread); without it the window
    /// cannot compare anew.
    /// </summary>
    public static CompareWindow Open(string leftName, IContentSource left, string rightName, IContentSource right,
        Func<(IContentSource Left, IContentSource Right)>? reopen = null)
    {
        var window = new CompareWindow(leftName, left, rightName, right, reopen);
        s_open.Add(window);
        window.Closed += (_, _) => s_open.Remove(window);
        window.Show();
        return window;
    }

    private CompareWindow(string leftName, IContentSource left, string rightName, IContentSource right, Func<(IContentSource, IContentSource)>? reopen)
    {
        _left = left;
        _right = right;
        _leftName = leftName;
        _rightName = rightName;
        _reopen = reopen;
        Title = $"Compare: {Path.GetFileName(leftName.TrimEnd('/', '\\'))} ↔ {Path.GetFileName(rightName.TrimEnd('/', '\\'))}";
        Width = 1200; Height = 760; MinWidth = 640; MinHeight = 320;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        Avalonia.Automation.AutomationProperties.SetName(_rows, "Differences");
        ToolTip.SetTip(_next, "Next difference (Alt+Down or F8)");
        ToolTip.SetTip(_previous, "Previous difference (Alt+Up or Shift+F8)");
        ToolTip.SetTip(_again, "Read both files again and compare them (F5)");
        _again.IsVisible = reopen is not null;
        _rows.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            CompareRow row => BuildRow(row),
            AlignedRow row => new TextBlock { Text = row.Text, FontFamily = Mono, Padding = new Thickness(4, 1), Background = KindBrush(row.Kind) },
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
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 6), Children = { _previous, _next, _again, _binary, _align, _ignoreWhitespace, _ignoreCase, _collapse } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        var summaryBorder = new Border { Child = _summary, Padding = new Thickness(8, 4), Classes = { "banner" } };
        DockPanel.SetDock(summaryBorder, Dock.Top);
        _changedBanner = new Border { Child = _changed, Padding = new Thickness(8, 4), Classes = { "banner" }, IsVisible = false };
        DockPanel.SetDock(_changedBanner, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(summaryBorder);
        root.Children.Add(_changedBanner);
        root.Children.Add(header);
        root.Children.Add(_rows);
        Content = root;

        _next.Click += (_, _) => Go(+1);
        _previous.Click += (_, _) => Go(-1);
        _again.Click += (_, _) => CompareAgain();
        foreach (var box in new[] { _ignoreWhitespace, _ignoreCase, _binary, _collapse, _align }) box.IsCheckedChanged += (_, _) => Recompute();
        ToolTip.SetTip(_align, "Finds content that moved because bytes were inserted or removed, instead of comparing at the same offsets. A heuristic view: whether the files are identical is the exact comparison's answer.");
        KeyDown += OnKey;
        Activated += (_, _) => CheckInputs();
        Closed += (_, _) =>
        {
            _closed = true;
            _work?.Cancel();
            ReleaseWhenIdle(_left, _right);
        };
        _summary.Text = "Comparing…";
        Opened += (_, _) => _loading = LoadAsync();
    }

    public string Summary => _summary.Text ?? "";

    /// <summary>Whether the window says that a file changed after it was compared (tests).</summary>
    public string? ChangedNotice => _changedBanner.IsVisible ? _changed.Text : null;

    /// <summary>Whether the files are still being read or their lines aligned (the rows and summary are not final yet).</summary>
    public bool IsComparing { get; private set; } = true;

    public IReadOnlyList<object> Rows => _rows.ItemsSource as IReadOnlyList<object> ?? [];

    /// <summary>The row the comparison shows as current (the first difference when it opens).</summary>
    public int CurrentRow => _rows.SelectedIndex;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Down when e.KeyModifiers == KeyModifiers.Alt:
            case Key.F8 when e.KeyModifiers == KeyModifiers.None:
                Go(+1);
                break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt:
            case Key.F8 when e.KeyModifiers == KeyModifiers.Shift:
                Go(-1);
                break;
            case Key.F5 when e.KeyModifiers == KeyModifiers.None && _reopen is not null:
            case Key.R when e.KeyModifiers == KeyModifiers.Control && _reopen is not null:
                CompareAgain();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>A comparison opens on its first difference: the equal lines before it are what the user did not come for.</summary>
    private void ShowFirstDifference()
    {
        if (_differenceRows.Count == 0) return;
        int first = _differenceRows[0];
        // After layout, so the list can scroll there.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_differenceRows.Count == 0 || _differenceRows[0] != first) return;
            _rows.SelectedIndex = first;
            _rows.ScrollIntoView(first);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Moves to the next or previous difference after the selected row (wrapping around).</summary>
    public void Go(int direction)
    {
        if (_differenceRows.Count == 0) return;
        int current = _rows.SelectedIndex;
        int target = direction > 0
            ? _differenceRows.FirstOrDefault(r => r > current, _differenceRows[0])
            : _differenceRows.LastOrDefault(r => r < current, _differenceRows[^1]);
        _rows.SelectedIndex = target;
        _rows.ScrollIntoView(target);
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
        var (left, right) = (_left, _right);
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
            // Every byte decides equality; text decoding and alignment are separate, labelled views.
            var loaded = await Task.Run(() =>
            {
                var revisions = (Revision(left), Revision(right));
                long total = Math.Max(left.Length, right.Length);
                var bytes = BinaryDiff.Compare(left, right, ct, done => Progress(done, total));
                var rows = ByteRows(left, right, bytes, ct);
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
                return (bytes, rows, l, r, problem, revisions);
            }, ct);
            if (_closed || ct.IsCancellationRequested) return;
            (_bytes, _byteRows, _leftText, _rightText, _textProblem, _revisions) = loaded;
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

    /// <summary>Contents are disposed once nothing reads them any more (a comparison or aligning stops at its next megabyte).</summary>
    private void ReleaseWhenIdle(IContentSource left, IContentSource right)
    {
        void Release()
        {
            left.Dispose();
            right.Dispose();
        }
        var readers = Task.WhenAll(_loading, _aligningRuns);
        if (readers.IsCompleted) Release();
        else readers.ContinueWith(_ => Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>F5: both files are opened and compared again (after they were edited, say).</summary>
    public async void CompareAgain()
    {
        if (_reopen is null || _closed || _reopening) return;
        _reopening = true;
        _again.IsEnabled = false;
        _work?.Cancel();
        _summary.Text = "Reading the files again…";
        try
        {
            var (left, right) = await Task.Run(_reopen);
            if (_closed)
            {
                left.Dispose();
                right.Dispose();
                return;
            }
            ReleaseWhenIdle(_left, _right);
            (_left, _right) = (left, right);
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
        if (_closed || _reopening || _bytes is null || _changedBanner.IsVisible || _revisions is (null, null)) return;
        var (left, right) = (_left, _right);
        var before = _revisions;
        var now = await Task.Run(() => (Revision(left), Revision(right)));
        if (_closed || !ReferenceEquals(left, _left) || !ReferenceEquals(right, _right)) return;
        var changed = new List<string>();
        if (before.Left is { } l && now.Item1 != l) changed.Add(Path.GetFileName(_leftName.TrimEnd('/', '\\')));
        if (before.Right is { } r && now.Item2 != r) changed.Add(Path.GetFileName(_rightName.TrimEnd('/', '\\')));
        if (changed.Count == 0) return;
        _changed.Text = (changed.Count == 2 ? "Both files changed" : $"\"{changed[0]}\" changed") + " after they were compared: what is shown is the earlier content. " +
                        (_reopen is null ? "Close this window and compare the files again." : "F5 compares them again.");
        _changedBanner.IsVisible = true;
    }

    /// <summary>One row per differing byte range, with the first bytes of each side (read here, off the UI thread).</summary>
    private static List<string> ByteRows(IContentSource left, IContentSource right, BinaryDiffResult bytes, CancellationToken ct)
    {
        var rows = new List<string>(bytes.Ranges.Count);
        foreach (var (offset, length) in bytes.Ranges)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add($"0x{offset:X10}  {length,12:N0} {(length == 1 ? "byte " : "bytes")}   left {Hex(left, offset, length),-HexWidth}   right {Hex(right, offset, length)}");
        }
        return rows;
    }

    /// <summary>Shows the comparison with the current options; lines are aligned off the UI thread (a million take a second).</summary>
    private async void Recompute()
    {
        if (_bytes is not { } bytes || _settingOptions) return;
        int view = ++_view;
        _ignoreWhitespace.IsEnabled = _ignoreCase.IsEnabled = _collapse.IsEnabled = _binary.IsChecked != true;
        bool binary = _binary.IsChecked == true || _leftText is null || _rightText is null;
        _align.IsVisible = binary;
        // Byte rows are wider than most windows; text rows fit their columns to the window.
        ScrollViewer.SetHorizontalScrollBarVisibility(_rows, binary ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
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
        _rows.ItemsSource = shown.Rows;
        _differenceRows = shown.Differences;
        ShowFirstDifference();
        _summary.Text = shown.Summary;
        IsComparing = false;
    }

    private void ShowBinary(BinaryDiffResult bytes)
    {
        _view++;
        var rows = new List<object>(_byteRows);
        var diff = Enumerable.Range(0, rows.Count).ToList();
        _rows.ItemsSource = rows;
        _differenceRows = diff;
        ShowFirstDifference();
        string note = _textProblem is { } p && _leftText is null ? " " + p : "";
        _summary.Text = bytes.Equal
            ? $"Identical: every byte was compared ({bytes.LeftLength:N0} bytes).{note}"
            : $"{bytes.Ranges.Count:N0}{(bytes.RangesTruncated ? "+" : "")} differing byte ranges at the same offsets" +
              (bytes.LeftLength != bytes.RightLength ? $"; lengths {bytes.LeftLength:N0} and {bytes.RightLength:N0} bytes" : "") +
              (bytes.RangesTruncated ? $"; the first {BinaryDiff.MaxRanges:N0} are listed" : "") +
              ". Shifted content shows as differences from the shift on (Align shifted bytes finds it again)." + note;
        IsComparing = false;
    }

    /// <summary>
    /// Binary content aligned where bytes were inserted or removed (plan §16.2): each stretch is a row, every stretch that
    /// is not equal is a difference to go to, and the summary says what was found and that the pairing is heuristic.
    /// </summary>
    private async Task ShowAlignedAsync(int view)
    {
        IsComparing = true;
        var task = _aligning ??= StartAligning();
        if (!task.IsCompleted) _summary.Text = "Aligning shifted bytes…";
        (AlignedBinaryResult Result, List<object> Rows) shown;
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
        var (aligned, rows) = shown;
        _rows.ItemsSource = rows;
        _differenceRows = [.. Enumerable.Range(0, rows.Count).Where(i => rows[i] is AlignedRow { Kind: not DiffKind.Equal })];
        ShowFirstDifference();
        _summary.Text = AlignedSummary(aligned);
        IsComparing = false;
    }

    private Task<(AlignedBinaryResult Result, List<object> Rows)> StartAligning()
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
            return (result, AlignedRows(left, right, result, ct));
        }, ct);
        _aligningRuns = Task.WhenAll(_aligningRuns, run);
        return run;
    }

    /// <summary>One row per aligned stretch, with offsets and the first bytes of what differs (read here, off the UI thread).</summary>
    private static List<object> AlignedRows(IContentSource left, IContentSource right, AlignedBinaryResult aligned, CancellationToken ct)
    {
        int count = Math.Min(aligned.Ranges.Count, MaxAlignedRows);
        var rows = new List<object>(count + 1);
        foreach (var r in aligned.Ranges.Take(count))
        {
            ct.ThrowIfCancellationRequested();
            string at = $"left 0x{r.LeftOffset:X10}  right 0x{r.RightOffset:X10}  ";
            rows.Add(new AlignedRow(r.Kind, r.Kind switch
            {
                DiffKind.Equal => $"= {at}{Bytes(r.LeftLength)} equal",
                DiffKind.Changed => $"≠ {at}{Bytes(r.LeftLength, r.RightLength)} changed   left {Hex(left, r.LeftOffset, r.LeftLength)}   right {Hex(right, r.RightOffset, r.RightLength)}",
                DiffKind.LeftOnly => $"− {at}{Bytes(r.LeftLength)} only left (removed)   {Hex(left, r.LeftOffset, r.LeftLength)}",
                DiffKind.RightOnly => $"+ {at}{Bytes(r.RightLength)} only right (inserted)   {Hex(right, r.RightOffset, r.RightLength)}",
                _ => $"… {at}{r.LeftLength:N0} left and {r.RightLength:N0} right bytes not aligned: the work limit was reached",
            }));
        }
        if (aligned.Ranges.Count > count) rows.Add($"   ⋯ {aligned.Ranges.Count - count:N0} more stretches are not listed");
        return rows;
    }

    /// <summary>"1 byte", "40,000 bytes"; two different counts as "3 → 5 bytes".</summary>
    private static string Bytes(long left, long? right = null) =>
        right is { } r && r != left ? $"{left:N0} → {r:N0} bytes" : left == 1 ? "1 byte" : $"{left:N0} bytes";

    private static string AlignedSummary(AlignedBinaryResult aligned)
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
        if (aligned.Ranges.Count > MaxAlignedRows) summary.Append($" The first {MaxAlignedRows:N0} stretches are listed.");
        summary.Append($" Heuristic: matched in blocks of {aligned.BlockSize:N0} bytes, so shorter equal stretches and moved or repeated content can be paired differently.");
        return summary.ToString();
    }

    /// <summary>The widest <see cref="Hex"/> text: 16 bytes and an ellipsis (columns of rows line up at it).</summary>
    private const int HexWidth = 16 * 3 - 1 + 2;

    private static string Hex(IContentSource source, long offset, long length)
    {
        var buffer = new byte[(int)Math.Min(16, Math.Max(0, length))];
        int n;
        try { n = source.Read(offset, buffer); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException) { return "?"; }
        return n == 0 ? "(end)" : BitConverter.ToString(buffer, 0, n).Replace('-', ' ') + (length > 16 ? " …" : "");
    }

    private static (List<object> Rows, List<int> Differences, string Summary) BuildText(TextSide left, TextSide right, BinaryDiffResult bytes,
        TextDiffOptions options, bool collapse)
    {
        var result = TextDiff.Compare(left.Lines, right.Lines, options);
        var rows = new List<object>();
        var diff = new List<int>();
        int endings = 0, changed = 0, leftOnly = 0, rightOnly = 0;
        foreach (var block in result.Blocks)
        {
            if (block.Kind != DiffKind.Equal) diff.Add(rows.Count);
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
        }
        return (rows, diff, summary.ToString());
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
}
