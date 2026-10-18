using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.Core.Compare;

namespace FileCat.App.Views;

/// <summary>
/// Complete source-line inspection beside the virtualized aligned comparison. Only the current bounded part is
/// shaped by the text controls; exceptionally long lines remain inspectable through explicit part navigation.
/// The original decoded strings and the comparison's exact/heuristic result are never rewritten.
/// </summary>
internal sealed class CompareLineDetails : Grid, IDisposable
{
    internal const int PartCharacters = 16_384;
    internal const int DifferenceBoundaryExtension = 256;
    internal const int MaximumPartCharacters = PartCharacters + DifferenceBoundaryExtension + 2;
    private static readonly FontFamily Mono = new("Cascadia Mono,Consolas,Menlo,monospace");
    private readonly TextBlock _leftLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _rightLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _previous = new() { Content = "Prev part" };
    private readonly Button _next = new() { Content = "Next part" };
    private readonly Button _reveal = new() { Content = "Reveal change" };
    private readonly CheckBox _wrap = new() { Content = "Wrap lines", VerticalAlignment = VerticalAlignment.Center };
    private readonly string _leftName, _rightName;
    private CompareRow? _row;
    private CancellationTokenSource? _work;
    private ScrollViewer? _leftScroll, _rightScroll;
    private CompareTextEvidence.Difference _difference;
    private int _generation, _part, _leftStart, _rightStart;
    private bool _disposed, _syncScroll, _focusOnReady;
    private TopLevel? _focusRoot;
    private IInputElement? _focusOwner;

    internal TextBox LeftText { get; } = TextField();
    internal TextBox RightText { get; } = TextField();
    internal bool IsReady { get; private set; }
    internal int FirstDifferenceOffset => _difference.Start;
    internal int LeftPartStart => _leftStart;
    internal int RightPartStart => _rightStart;
    internal string Status => _status.Text ?? string.Empty;

    internal CompareLineDetails(string leftName, string rightName)
    {
        _leftName = leftName;
        _rightName = rightName;
        RowDefinitions = RowDefinitions.Parse("Auto,*,Auto");
        RowSpacing = 4;
        Margin = new Thickness(8, 4);
        AutomationProperties.SetName(this, "Full-line comparison details");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(new TextBlock { Text = "Full-line detail", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_wrap);
        tools.Children.Add(_reveal);
        tools.Children.Add(_previous);
        tools.Children.Add(_next);
        foreach (var control in tools.Children) control.Margin = new Thickness(0, 0, 8, 2);
        AutomationProperties.SetName(_wrap, "Wrap full-line details");
        AutomationProperties.SetName(_previous, "Previous part of both full lines");
        AutomationProperties.SetName(_next, "Next part of both full lines");
        AutomationProperties.SetName(_reveal, "Reveal the first differing text in both full lines");
        var pair = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,8,*"), RowDefinitions = RowDefinitions.Parse("Auto,*"), RowSpacing = 3 };
        SetColumn(_rightLabel, 2);
        SetColumn(RightText, 2);
        Grid.SetRow(LeftText, 1);
        Grid.SetRow(RightText, 1);
        pair.Children.Add(_leftLabel);
        pair.Children.Add(_rightLabel);
        pair.Children.Add(LeftText);
        pair.Children.Add(RightText);
        Grid.SetRow(pair, 1);
        Grid.SetRow(_status, 2);
        Children.Add(tools);
        Children.Add(pair);
        Children.Add(_status);
        _previous.Click += (_, _) => ShowPart(Math.Max(0, _part - 1), reveal: false);
        _next.Click += (_, _) => ShowPart(_part + 1, reveal: false);
        _reveal.Click += (_, _) => RevealDifference();
        _wrap.IsCheckedChanged += (_, _) =>
        {
            foreach (var field in new[] { LeftText, RightText })
            {
                field.TextWrapping = _wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
                ScrollViewer.SetHorizontalScrollBarVisibility(field, _wrap.IsChecked == true ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            }
            QueueReveal();
        };
        LeftText.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(AttachScrollers, DispatcherPriority.Loaded);
        RightText.TemplateApplied += (_, _) => Dispatcher.UIThread.Post(AttachScrollers, DispatcherPriority.Loaded);
        SetComparisonRow(null);
    }

    private static TextBox TextField()
    {
        var field = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = Mono,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(field, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(field, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        return field;
    }

    internal void SetComparisonRow(CompareRow? row)
    {
        if (_disposed) return;
        _generation++;
        ClearFocusRequest();
        _work?.Cancel();
        _work = null;
        _row = row;
        IsReady = false;
        _difference = new CompareTextEvidence.Difference(-1, 0, 0);
        _previous.IsEnabled = _next.IsEnabled = _reveal.IsEnabled = false;
        _previous.IsVisible = _next.IsVisible = false;
        LeftText.Text = RightText.Text = string.Empty;
        LeftText.PlaceholderText = RightText.PlaceholderText = null;
        LeftText.IsEnabled = row?.LeftLine is not null;
        RightText.IsEnabled = row?.RightLine is not null;
        _leftLabel.Text = Label(row?.LeftLine, "Left", row?.LeftText.Length ?? 0);
        _rightLabel.Text = Label(row?.RightLine, "Right", row?.RightText.Length ?? 0);
        AutomationProperties.SetName(LeftText, "Left full line, " + Formatters.SafeName(_leftName));
        AutomationProperties.SetName(RightText, "Right full line, " + Formatters.SafeName(_rightName));
        if (row is null)
        {
            _status.Text = "Choose a text row to inspect both complete lines. F8 selects the next difference; Alt+L focuses these details.";
            return;
        }
        _status.Text = "Preparing full-line details…";
        var work = new CancellationTokenSource();
        _work = work;
        _ = PrepareAsync(row, _generation, work);
    }

    private async Task PrepareAsync(CompareRow row, int generation, CancellationTokenSource work)
    {
        try
        {
            var difference = await Task.Run(() => CompareTextEvidence.FirstDifference(row.LeftText, row.RightText, work.Token), work.Token);
            if (_disposed || generation != _generation || work.IsCancellationRequested) return;
            _difference = difference;
            IsReady = true;
            int start = difference.Start >= 0 ? difference.Start : row.EndingDiffers ? Math.Max(row.LeftText.Length, row.RightText.Length) : 0;
            ShowPart(start / PartCharacters, reveal: true);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_work, work)) _work = null;
            work.Dispose();
        }
    }

    private void ShowPart(int part, bool reveal)
    {
        if (!IsReady || _row is not { } row || _disposed) return;
        int maximum = Math.Max(0, (Math.Max(row.LeftText.Length, row.RightText.Length) - 1) / PartCharacters);
        _part = Math.Clamp(part, 0, maximum);
        int start = _part * PartCharacters;
        (LeftText.Text, _leftStart) = Slice(row.LeftText, start, _difference.Start, _difference.LeftLength);
        (RightText.Text, _rightStart) = Slice(row.RightText, start, _difference.Start, _difference.RightLength);
        _leftLabel.Text = PartLabel(row.LeftLine, "Left", row.LeftText.Length, _leftStart, LeftText.Text?.Length ?? 0);
        _rightLabel.Text = PartLabel(row.RightLine, "Right", row.RightText.Length, _rightStart, RightText.Text?.Length ?? 0);
        LeftText.PlaceholderText = row.LeftLine is null ? "No corresponding line" : row.LeftText.Length == 0 ? "Empty line" : LeftText.Text?.Length == 0 ? "This line ends before this part" : null;
        RightText.PlaceholderText = row.RightLine is null ? "No corresponding line" : row.RightText.Length == 0 ? "Empty line" : RightText.Text?.Length == 0 ? "This line ends before this part" : null;
        _previous.IsVisible = _next.IsVisible = maximum > 0;
        _previous.IsEnabled = _part > 0;
        _next.IsEnabled = _part < maximum;
        _reveal.IsEnabled = _difference.Start >= 0 || row.EndingDiffers;
        bool partialDifference = PartiallyContainsDifference(_leftStart, LeftText.Text?.Length ?? 0, _difference.LeftLength) ||
            PartiallyContainsDifference(_rightStart, RightText.Text?.Length ?? 0, _difference.RightLength);
        _status.Text = Description(row) + (maximum > 0 ? $" Part {_part + 1:N0} of {maximum + 1:N0}; Previous/Next part reaches all source text." : "") +
            (partialDifference ? " This part shows only part of the differing text element; use Previous/Next part to inspect its remaining source text." : "");
        // Keep the visible status about this source/part. Repeating the general controls in the
        // footer can consume the entire field viewport at a narrow width, especially after wrapping.
        const string usage = " Select text and Ctrl+C to copy; Alt+L focuses a line. Reveal changed text returns to the first differing location in both lines.";
        AutomationProperties.SetHelpText(LeftText, _leftLabel.Text + ". " + _status.Text + usage);
        AutomationProperties.SetHelpText(RightText, _rightLabel.Text + ". " + _status.Text + usage);
        if (reveal) QueueReveal();
        else
        {
            LeftText.CaretIndex = RightText.CaretIndex = 0;
            AttachScrollers();
            if (_leftScroll is not null) _leftScroll.Offset = default;
            if (_rightScroll is not null) _rightScroll.Offset = default;
        }
    }

    private static (string Text, int Start) Slice(string source, int start, int differenceStart, int differenceLength)
    {
        start = Math.Min(start, source.Length);
        int end = start + Math.Min(PartCharacters, source.Length - start);
        int differenceEnd = differenceStart + differenceLength;
        // Reveal must include the decisive combining mark when its text element straddles a
        // normal part boundary. Extend only this known element, using its off-thread result;
        // never scan or shape an unbounded combining sequence on the UI thread.
        if (differenceStart >= start && differenceStart < end && differenceEnd > end &&
            differenceEnd - end <= DifferenceBoundaryExtension)
            end = differenceEnd;
        // A part may overlap its neighbour by one UTF-16 code unit so a surrogate pair is never torn in half.
        if (start > 0 && start < source.Length && char.IsLowSurrogate(source[start]) && char.IsHighSurrogate(source[start - 1])) start--;
        if (end > 0 && end < source.Length && char.IsHighSurrogate(source[end - 1]) && char.IsLowSurrogate(source[end])) end++;
        return (source[start..end], start);
    }

    private bool PartiallyContainsDifference(int start, int shown, int differenceLength)
    {
        if (_difference.Start < 0 || differenceLength == 0 || shown == 0) return false;
        int end = start + shown, differenceEnd = _difference.Start + differenceLength;
        return _difference.Start < end && differenceEnd > start &&
            (_difference.Start < start || differenceEnd > end);
    }

    private static string Label(int? line, string side, int length) => line is { } number
        ? $"{side} line {number:N0} · {length:N0} UTF-16 characters" : side + " gap · no corresponding line";

    private static string PartLabel(int? line, string side, int length, int start, int shown) =>
        Label(line, side, length) + (line is not null && length > PartCharacters ? $" · showing {start + (shown > 0 ? 1 : 0):N0}–{start + shown:N0}" : string.Empty);

    private string Description(CompareRow row)
    {
        if (row.Kind == DiffKind.Unaligned) return "Unaligned source line; no line-by-line match was claimed.";
        if (row.LeftLine is null || row.RightLine is null) return row.LeftLine is null ? "Only the right file has this line." : "Only the left file has this line.";
        if (_difference.Start < 0) return row.EndingDiffers ? "The text matches; line endings differ. Binary view exposes the exact ending bytes." : "Both source lines contain the same text.";
        string prefix = row.Kind == DiffKind.Equal ? "The comparison options treat this row as equal. " : string.Empty;
        if (_difference.LeftLength == 0) return prefix + $"The left line ends here; additional right text starts at character {_difference.Start + 1:N0} (UTF-16).";
        if (_difference.RightLength == 0) return prefix + $"The right line ends here; additional left text starts at character {_difference.Start + 1:N0} (UTF-16).";
        return prefix + $"First differing text starts at character {_difference.Start + 1:N0} (UTF-16).";
    }

    internal void FocusText()
    {
        _focusRoot = TopLevel.GetTopLevel(this);
        _focusOwner = _focusRoot?.FocusManager?.GetFocusedElement();
        _focusOnReady = _focusRoot is not null;
        if (IsReady) RevealDifference();
    }

    private void ClearFocusRequest()
    {
        _focusOnReady = false;
        _focusRoot = null;
        _focusOwner = null;
    }

    internal void RevealCurrentDifference() => RevealDifference();

    private void RevealDifference()
    {
        if (!IsReady || _row is not { } row) return;
        int start = _difference.Start >= 0 ? _difference.Start : row.EndingDiffers ? Math.Max(row.LeftText.Length, row.RightText.Length) : 0;
        ShowPart(start / PartCharacters, reveal: true);
    }

    private void QueueReveal()
    {
        int generation = _generation, part = _part;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != _generation || part != _part || !IsReady || _row is not { } row) return;
            AttachScrollers();
            _syncScroll = true;
            try
            {
                if (_focusOnReady)
                {
                    var root = _focusRoot;
                    bool stillOwned = root is not null && ReferenceEquals(TopLevel.GetTopLevel(this), root) &&
                        ReferenceEquals(root.FocusManager?.GetFocusedElement(), _focusOwner) &&
                        (root is not Window window || window.IsActive);
                    ClearFocusRequest();
                    // A delayed detail calculation/layout must not override a newer deliberate focus.
                    if (stillOwned) (row.LeftLine is not null ? LeftText : RightText).Focus();
                }
                int start = _difference.Start >= 0 ? _difference.Start : row.EndingDiffers ? Math.Max(row.LeftText.Length, row.RightText.Length) : 0;
                // Wrapping can change the text viewport after the queued reveal. Measure both fields at
                // their final width before asking the presenter to bring the decisive glyph into view.
                LeftText.UpdateLayout();
                RightText.UpdateLayout();
                Reveal(LeftText, start - _leftStart, _difference.LeftLength);
                Reveal(RightText, start - _rightStart, _difference.RightLength);
            }
            finally { _syncScroll = false; }
        }, DispatcherPriority.Loaded);
    }

    private static void Reveal(TextBox field, int at, int length)
    {
        at = Math.Clamp(at, 0, field.Text?.Length ?? 0);
        field.CaretIndex = at;
        field.SelectionStart = at;
        field.SelectionEnd = Math.Min(field.Text?.Length ?? 0, at + length);
        if (field.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } presenter)
        {
            var bounds = presenter.TextLayout.HitTestTextPosition(at);
            presenter.BringIntoView(bounds.Inflate(8));
        }
    }

    private void AttachScrollers()
    {
        if (_disposed) return;
        void Attach(TextBox field, ref ScrollViewer? existing)
        {
            var found = field.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (ReferenceEquals(found, existing)) return;
            if (existing is not null) existing.ScrollChanged -= SynchronizeScroll;
            existing = found;
            if (existing is not null) existing.ScrollChanged += SynchronizeScroll;
        }
        Attach(LeftText, ref _leftScroll);
        Attach(RightText, ref _rightScroll);
    }

    private void SynchronizeScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncScroll || _disposed || _wrap.IsChecked == true || e.OffsetDelta.X == 0 || sender is not ScrollViewer source) return;
        var target = ReferenceEquals(source, _leftScroll) ? _rightScroll : _leftScroll;
        if (target is null) return;
        _syncScroll = true;
        try { target.Offset = new Vector(source.Offset.X, target.Offset.Y); }
        finally { _syncScroll = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        ClearFocusRequest();
        _work?.Cancel();
        _work = null;
        if (_leftScroll is not null) _leftScroll.ScrollChanged -= SynchronizeScroll;
        if (_rightScroll is not null) _rightScroll.ScrollChanged -= SynchronizeScroll;
        _leftScroll = _rightScroll = null;
        _row = null;
        LeftText.Text = RightText.Text = string.Empty;
        IsReady = false;
    }

}
