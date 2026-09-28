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

/// <summary>
/// File comparison (plan §16.2, TV-08): text side by side with within-line changes, or exact binary ranges. Both sides
/// scroll together because each row holds both. The summary claims "identical" only when every byte matched; a region
/// that could not be aligned within limits is shown and labelled, never presented as exact.
/// </summary>
public sealed class CompareWindow : Window
{
    private static readonly IBrush LeftOnlyBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xE0, 0x40, 0x40));
    private static readonly IBrush RightOnlyBrush = new SolidColorBrush(Color.FromArgb(0x38, 0x40, 0xC0, 0x40));
    private static readonly IBrush ChangedBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xB0, 0x30));
    private static readonly IBrush UnalignedBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x90, 0x90, 0xA0));
    private static readonly IBrush InlineBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xE0, 0x90, 0x20));
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,monospace");

    private readonly IContentSource _left, _right;
    private readonly string _leftName, _rightName;
    private readonly ListBox _rows = new();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _ignoreWhitespace = new() { Content = "Ignore whitespace", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _ignoreCase = new() { Content = "Ignore case", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _binary = new() { Content = "Binary", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _collapse = new() { Content = "Hide equal lines", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous = new() { Content = "Previous difference" };
    private readonly Button _next = new() { Content = "Next difference" };
    private List<int> _differenceRows = [];
    private CancellationTokenSource? _work;
    private BinaryDiffResult? _bytes;
    private TextSide? _leftText, _rightText;
    private string? _textProblem;
    private bool _closed;

    private static readonly List<CompareWindow> s_open = [];

    /// <summary>Open comparisons, oldest first.</summary>
    public static IReadOnlyList<CompareWindow> OpenWindows => s_open;

    /// <summary>Opens a comparison of two contents (opened by the caller off the UI thread); the window disposes them.</summary>
    public static CompareWindow Open(string leftName, IContentSource left, string rightName, IContentSource right)
    {
        var window = new CompareWindow(leftName, left, rightName, right);
        s_open.Add(window);
        window.Closed += (_, _) => s_open.Remove(window);
        window.Show();
        return window;
    }

    private CompareWindow(string leftName, IContentSource left, string rightName, IContentSource right)
    {
        _left = left;
        _right = right;
        _leftName = leftName;
        _rightName = rightName;
        Title = $"Compare: {Path.GetFileName(leftName.TrimEnd('/', '\\'))} ↔ {Path.GetFileName(rightName.TrimEnd('/', '\\'))}";
        Width = 1200; Height = 760; MinWidth = 640; MinHeight = 320;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileCat/Assets/filecat.ico"))); } catch (Exception) { }
        Avalonia.Automation.AutomationProperties.SetName(_rows, "Differences");
        ToolTip.SetTip(_next, "Next difference (Alt+Down or F8)");
        ToolTip.SetTip(_previous, "Previous difference (Alt+Up or Shift+F8)");
        _rows.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            CompareRow row => BuildRow(row),
            string text => new TextBlock { Text = text, FontFamily = Mono, Margin = new Thickness(4, 1) },
            _ => new TextBlock(),
        });
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(8, 4) };
        header.Children.Add(new TextBlock { Text = leftName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold });
        var rightHeader = new TextBlock { Text = rightName, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(rightHeader, 1);
        header.Children.Add(rightHeader);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 6), Children = { _previous, _next, _binary, _ignoreWhitespace, _ignoreCase, _collapse } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        var summaryBorder = new Border { Child = _summary, Padding = new Thickness(8, 4), Classes = { "banner" } };
        DockPanel.SetDock(summaryBorder, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(summaryBorder);
        root.Children.Add(header);
        root.Children.Add(_rows);
        Content = root;

        _next.Click += (_, _) => Go(+1);
        _previous.Click += (_, _) => Go(-1);
        foreach (var box in new[] { _ignoreWhitespace, _ignoreCase, _binary, _collapse }) box.IsCheckedChanged += (_, _) => Recompute();
        KeyDown += OnKey;
        Closed += (_, _) =>
        {
            _closed = true;
            _work?.Cancel();
            _left.Dispose();
            _right.Dispose();
        };
        _summary.Text = "Comparing…";
        Opened += (_, _) => _ = LoadAsync();
    }

    public string Summary => _summary.Text ?? "";

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
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Moves to the next or previous difference after the selected row.</summary>
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

    private async Task LoadAsync()
    {
        _work = new CancellationTokenSource();
        var ct = _work.Token;
        try
        {
            // Every byte decides equality; text decoding and alignment are separate, labelled views.
            (_bytes, _leftText, _rightText, _textProblem) = await Task.Run(() =>
            {
                var bytes = BinaryDiff.Compare(_left, _right, ct);
                TextSide? l = null, r = null;
                string? problem = null;
                try
                {
                    l = TextSide.Load(_left, ct);
                    r = TextSide.Load(_right, ct);
                    if (l.Encoding.LooksBinary || r.Encoding.LooksBinary)
                    {
                        problem = "At least one file looks binary, so it is compared byte by byte.";
                        l = r = null;
                    }
                }
                catch (InvalidDataException ex)
                {
                    problem = ex.Message;
                }
                return (bytes, l, r, problem);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _summary.Text = "The files could not be read: " + ex.Message;
            return;
        }
        if (_closed) return;
        if (_leftText is null) _binary.IsChecked = true;
        Recompute();
    }

    private void Recompute()
    {
        if (_bytes is null) return;
        _ignoreWhitespace.IsEnabled = _ignoreCase.IsEnabled = _collapse.IsEnabled = _binary.IsChecked != true;
        if (_binary.IsChecked == true || _leftText is null || _rightText is null) ShowBinary(_bytes);
        else ShowText(_leftText, _rightText, _bytes);
    }

    private void ShowBinary(BinaryDiffResult bytes)
    {
        var rows = new List<object>();
        var diff = new List<int>();
        foreach (var (offset, length) in bytes.Ranges)
        {
            diff.Add(rows.Count);
            rows.Add($"0x{offset:X10}  {length,12:N0} bytes   left {Hex(_left, offset, length)}   right {Hex(_right, offset, length)}");
        }
        _rows.ItemsSource = rows;
        _differenceRows = diff;
        ShowFirstDifference();
        string note = _textProblem is { } p && _leftText is null ? " " + p : "";
        _summary.Text = bytes.Equal
            ? $"Identical: every byte was compared ({bytes.LeftLength:N0} bytes).{note}"
            : $"{bytes.Ranges.Count:N0}{(bytes.RangesTruncated ? "+" : "")} differing byte ranges at the same offsets" +
              (bytes.LeftLength != bytes.RightLength ? $"; lengths {bytes.LeftLength:N0} and {bytes.RightLength:N0} bytes" : "") +
              (bytes.RangesTruncated ? $"; the first {BinaryDiff.MaxRanges:N0} are listed" : "") + ". Shifted content shows as differences from the shift on." + note;
    }

    private static string Hex(IContentSource source, long offset, long length)
    {
        var buffer = new byte[(int)Math.Min(16, Math.Max(0, length))];
        int n;
        try { n = source.Read(offset, buffer); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "?"; }
        return n == 0 ? "(end)" : Convert.ToHexString(buffer, 0, n) + (length > 16 ? "…" : "");
    }

    private void ShowText(TextSide left, TextSide right, BinaryDiffResult bytes)
    {
        var options = new TextDiffOptions(_ignoreWhitespace.IsChecked == true, _ignoreCase.IsChecked == true);
        var result = TextDiff.Compare(left.Lines, right.Lines, options);
        var rows = new List<object>();
        var diff = new List<int>();
        int endings = 0, changed = 0, leftOnly = 0, rightOnly = 0;
        bool collapse = _collapse.IsChecked == true;
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
        _rows.ItemsSource = rows;
        _differenceRows = diff;
        ShowFirstDifference();
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
        _summary.Text = summary.ToString();
    }

    private static Control BuildRow(CompareRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,56,*") };
        grid.Background = row.Kind switch
        {
            DiffKind.Changed => ChangedBrush,
            DiffKind.LeftOnly => LeftOnlyBrush,
            DiffKind.RightOnly => RightOnlyBrush,
            DiffKind.Unaligned => UnalignedBrush,
            _ => Brushes.Transparent,
        };
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
