using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class CompareTextInteractionTests
{
    [Fact]
    public void Search_respects_source_scope_changed_rows_gaps_and_cancellation()
    {
        object[] rows =
        [
            new CompareRow(DiffKind.Equal, 1, "Needle", 1, "Needle"),
            new CompareRow(DiffKind.LeftOnly, 2, "needle twice needle", null, ""),
            new CompareRow(DiffKind.RightOnly, null, "", 2, "needle"),
        ];
        var both = CompareTextSearch.Search(rows, "needle", 0, false, CancellationToken.None);
        Assert.Equal(5, both.Matches.Count);
        Assert.Equal(new CompareTextMatch(0, 0, 0), both.Matches[0]);
        Assert.Equal(new CompareTextMatch(2, 1, 0), both.Matches[^1]);
        Assert.Equal(2, CompareTextSearch.Search(rows, "needle", 1, true, CancellationToken.None).Matches.Count);
        Assert.Single(CompareTextSearch.Search(rows, "needle", 2, true, CancellationToken.None).Matches);
        Assert.Empty(CompareTextSearch.Search(rows, "absent", 0, false, CancellationToken.None).Matches);
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        Assert.Throws<OperationCanceledException>(() => CompareTextSearch.Search(rows, "needle", 0, false, stopped.Token));
    }

    [AvaloniaFact]
    public async Task Find_reaches_offscreen_match_and_reports_scope_and_navigation_separately()
    {
        string prefix = string.Concat(Enumerable.Range(0, 90).Select(i => $"filler {i}\n"));
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes(prefix + "needle\nleft only\n")),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes(prefix + "needle\n")));
        try
        {
            await Until(() => !window.IsComparing && window.Rows.OfType<CompareRow>().Any(r => r.LeftText == "needle"));
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(c =>
                AutomationProperties.GetName(c) == "Find text in comparison (Ctrl+F)");
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(c =>
                AutomationProperties.GetAutomationId(c) == "Comparison match status");
            var scope = window.GetVisualDescendants().OfType<ComboBox>().Single(c =>
                AutomationProperties.GetName(c) == "Comparison search source scope");
            var view = window.GetVisualDescendants().OfType<CompareTextView>().Single();
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(search.IsVisible);
            Assert.True(search.IsFocused);
            search.Text = "needle";
            await Until(() => status.Text?.StartsWith("2 matches", StringComparison.Ordinal) == true);
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            window.GetVisualDescendants().OfType<Button>().Single(b =>
                AutomationProperties.GetName(b) == "Close comparison Find bar")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(search.IsEffectivelyVisible);
            Assert.Equal(window.CurrentRow, FocusedRow(window, rows)); // I637: the content has the keyboard again
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(search.IsEffectivelyVisible);
            Assert.Equal("needle", search.Text);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(search.IsEffectivelyVisible);
            Assert.Equal(window.CurrentRow, FocusedRow(window, rows));
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(search.IsEffectivelyVisible);
            Assert.Contains("searchFound", search.Classes);
            ButtonNamed(window, "Next match").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.CurrentRow >= 90);
            Assert.Equal("needle", view.SelectedSourceText().Text);
            Assert.Contains("Match 1 of 2", status.Text);
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Assert.Contains("Match 2 of 2", status.Text);
            window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
            Assert.Contains("Match 1 of 2", status.Text);
            window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
            Assert.Contains("Wrapped to the end. Match 2 of 2", status.Text);
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Assert.Contains("Wrapped to the beginning. Match 1 of 2", status.Text);
            scope.SelectedIndex = 1;
            await Until(() => status.Text?.StartsWith("1 match", StringComparison.Ordinal) == true);
            var changed = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Changed rows only");
            changed.IsChecked = true;
            await Until(() => status.Text?.StartsWith("No matches", StringComparison.Ordinal) == true);
            Assert.Contains("searchMissing", search.Classes);
            Assert.Contains("left shown source, changed rows only", status.Text);
            Assert.False(ButtonNamed(window, "Next match").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Find_selects_and_copies_the_long_offscreen_source_suffix()
    {
        string prefix = new('x', 10_000);
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes(prefix + "Needle\n")),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes(prefix + "other!\n")));
        try
        {
            await Until(() => !window.IsComparing && window.Rows.OfType<CompareRow>().Any());
            ButtonNamed(window, "Find…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(c =>
                AutomationProperties.GetName(c) == "Find text in comparison (Ctrl+F)");
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(c =>
                AutomationProperties.GetAutomationId(c) == "Comparison match status");
            var view = window.GetVisualDescendants().OfType<CompareTextView>().Single();
            search.Text = "Needle";
            await Until(() => status.Text?.StartsWith("1 match", StringComparison.Ordinal) == true);
            ButtonNamed(window, "Next match").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => view.HorizontalOffset > 0);
            Assert.Equal("Needle", view.SelectedSourceText().Text);
            ButtonNamed(window, "Copy selection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(async () => await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!) == "Needle");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Difference_navigation_announces_the_current_changed_line()
    {
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt",
                "first LEFT\nequal middle\nthird LEFT\n"u8.ToArray()),
            "right.txt", new MemoryContentSource("right.txt",
                "first RIGHT\nequal middle\nthird RIGHT\n"u8.ToArray()));
        try
        {
            await Until(() => !window.IsComparing && window.Rows.OfType<CompareRow>().Count() == 3);
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(c =>
                AutomationProperties.GetAutomationId(c) == "Comparison navigation and action status");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            window.GoTo(1);
            Assert.StartsWith("Difference 2 of 2:", status.Text);
            Assert.Contains("line 3", status.Text);
            window.GoTo(0);
            Assert.StartsWith("Difference 1 of 2:", status.Text);
            Assert.Contains("line 1", status.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Source_selection_joins_only_real_lines_with_lf_and_retires_on_reset()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows);
        var window = new Window { Content = view, Width = 800, Height = 420 };
        rows.ItemsSource = new object[]
        {
            new CompareRow(DiffKind.Changed, 1, "alpha", 1, "ALPHA"),
            new CompareRow(DiffKind.RightOnly, null, "", 2, "inserted"),
            new CompareRow(DiffKind.Changed, 2, "omega", 3, "OMEGA"),
        };
        window.Show();
        try
        {
            view.SetSelection(1, 0, 2, 2, 3);
            Assert.Equal(("PHA\ninserted\nOME", false), view.SelectedSourceText());
            view.SetSelection(0, 0, 2, 2, 3);
            Assert.Equal(("pha\nome", false), view.SelectedSourceText());
            Assert.Equal((null, true), view.SelectedSourceText(maxCharacters: 3));
            view.SetSelection(0, 0, 1, 0, 4);
            Assert.Equal(("lph", false), view.SelectedSourceText());
            view.Reset();
            Assert.False(view.HasSelection);
            Assert.Equal((null, false), view.SelectedSourceText());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Copy_actions_use_source_text_and_explain_one_sided_gaps()
    {
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes("alpha\nleft only\nomega\n")),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes("ALPHA\nomega\n")));
        try
        {
            await Until(() => !window.IsComparing && window.Rows.OfType<CompareRow>().Any(r => r.LeftText == "left only"));
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            var view = window.GetVisualDescendants().OfType<CompareTextView>().Single();
            int only = window.Rows.ToList().FindIndex(r => r is CompareRow { LeftText: "left only" });
            Assert.True(only >= 0);
            rows.SelectedIndex = only;
            var side = window.GetVisualDescendants().OfType<ComboBox>().Single(c =>
                Avalonia.Automation.AutomationProperties.GetName(c) == "Source side for copying a line");
            side.SelectedIndex = 0;
            var copyLine = ButtonNamed(window, "Copy line");
            copyLine.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(async () => await Clipboard() == "left only");
            Assert.Equal("left only", await Clipboard());

            view.SetSelection(0, only, 5, only, 9);
            ButtonNamed(window, "Copy selection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(async () => await Clipboard() == "only");
            Assert.Equal("only", await Clipboard());
            side.SelectedIndex = 1;
            Assert.False(copyLine.IsEnabled);
            ButtonNamed(window, "Copy difference").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(async () => (await Clipboard())?.Contains("Right: [gap]", StringComparison.Ordinal) == true);
            Assert.Equal("Left: left only\nRight: [gap]", await Clipboard());

            rows.ScrollIntoView(only);
            window.UpdateLayout();
            var cell = view.RealizedRow(only)!.LeftCell!;
            Assert.Contains(cell.ContextMenu!.Items.OfType<MenuItem>(), item => item.Header as string == "Copy selected text");
            Assert.Contains(cell.ContextMenu.Items.OfType<MenuItem>(), item => item.Header as string == "Copy this source line");
        }
        finally { window.Close(); }

        async Task<string?> Clipboard() => await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!);
    }

    [AvaloniaFact]
    public async Task Pointer_drag_and_shift_click_select_source_characters_across_aligned_rows()
    {
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", "alpha\nbeta\ngamma\n"u8.ToArray()),
            "right.txt", new MemoryContentSource("right.txt", "ALPHA\nbeta\nGAMMA\n"u8.ToArray()));
        try
        {
            await Until(() => !window.IsComparing && window.Rows.OfType<CompareRow>().Count() >= 3);
            var view = window.GetVisualDescendants().OfType<CompareTextView>().Single();
            int first = window.Rows.ToList().FindIndex(r => r is CompareRow { LeftText: "alpha" });
            int last = window.Rows.ToList().FindIndex(r => r is CompareRow { LeftText: "gamma" });
            Assert.True(first >= 0 && last > first);
            Point At(CompareSourceCell cell, int offset)
            {
                var rect = cell.SourceBounds(offset);
                return cell.TranslatePoint(new Point(rect.X + cell.TextOrigin.X + 1, rect.Y + rect.Height / 2), window)!.Value;
            }
            // A shaped source line can exist before virtualization and hit testing
            // finish arranging its row. Wait for the actual pointer target used by
            // this test; Linux CI occasionally delivered the drag to an old slot.
            await Until(() =>
            {
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                var firstRow = view.RealizedRow(first);
                var lastRow = view.RealizedRow(last);
                if (firstRow?.LeftCell?.SourceLayout is null || lastRow?.LeftCell?.SourceLayout is null ||
                    !view.IsCurrent(firstRow) || !view.IsCurrent(lastRow)) return false;
                return Hits(firstRow.LeftCell, 1) && Hits(firstRow.LeftCell, 4) &&
                    Hits(lastRow.LeftCell, 2);
            });
            var firstCell = view.RealizedRow(first)!.LeftCell!;
            var lastCell = view.RealizedRow(last)!.LeftCell!;
            bool Hits(CompareSourceCell cell, int offset) =>
                cell.TranslatePoint(new Point(cell.SourceBounds(offset).X + cell.TextOrigin.X + 1,
                    cell.SourceBounds(offset).Y + cell.SourceBounds(offset).Height / 2), view) is { } inView &&
                ReferenceEquals(view.SourceAt(inView), cell) &&
                window.InputHitTest(At(cell, offset)) is Visual hit &&
                hit.GetSelfAndVisualAncestors().Contains(view);
            var start = At(firstCell, 1);
            var end = At(firstCell, 4);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Assert.Equal("lph", view.SelectedSourceText().Text);

            var final = At(lastCell, 2);
            window.MouseMove(final);
            window.MouseDown(final, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(final, MouseButton.Left);
            Assert.Equal("lpha\nbeta\nga", view.SelectedSourceText().Text);
            window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.ArrowRight, null);
            Assert.Equal("lpha\nbeta\ngam", view.SelectedSourceText().Text);
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            await Until(async () => await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard!) == "lpha\nbeta\ngam");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// I637: a comparison opens with the keyboard on its current row (the first difference), not on the window, so Down
    /// moves the current row and Page Down scrolls the comparison.
    /// </summary>
    [AvaloniaFact]
    public async Task Opening_gives_the_keyboard_to_the_current_row()
    {
        var window = OpenThreeHundredLines();
        try
        {
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            await UntilARowHasTheKeyboard(window, rows);
            Assert.Equal(0, window.CurrentDifference);
            Assert.Equal(99, window.CurrentRow); // line 100, the first difference
            Assert.Equal(99, FocusedRow(window, rows));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal((100, 100), (window.CurrentRow, FocusedRow(window, rows)));
            Layout(window);
            double before = rows.Scroll!.Offset.Y;
            window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
            Layout(window);
            Assert.True(rows.Scroll.Offset.Y > before, $"Page Down left the rows at {rows.Scroll.Offset.Y} (was {before}); current row {window.CurrentRow}.");
            Assert.Equal((100, 100), (window.CurrentRow, FocusedRow(window, rows)));
            // Page Down moved the view, not the current row: Down moves on from that row and shows it again.
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Layout(window);
            Assert.Equal((101, 101), (window.CurrentRow, FocusedRow(window, rows)));
            var viewport = rows.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            var current = rows.ContainerFromIndex(101)!;
            Assert.InRange(current.TranslatePoint(default, viewport)!.Value.Y, -0.5, viewport.Bounds.Height - current.Bounds.Height + 0.5);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// I637: a comparison shown before its window became active (another window was active, or the activation came late)
    /// gives the keyboard to its current row when the window is activated.
    /// </summary>
    [AvaloniaFact]
    public async Task A_comparison_shown_before_activation_gives_the_keyboard_to_the_current_row_on_activation()
    {
        var window = ThreeHundredLines();
        window.ShowActivated = false;
        window.Show();
        try
        {
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            await Until(() => !window.IsComparing && window.CurrentDifference == 0);
            Layout(window);
            Assert.False(window.IsActive);
            Assert.Null(window.FocusManager?.GetFocusedElement());
            window.Activate();
            await UntilARowHasTheKeyboard(window, rows);
            Assert.Equal((99, 99), (window.CurrentRow, FocusedRow(window, rows)));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal((100, 100), (window.CurrentRow, FocusedRow(window, rows)));
        }
        finally { window.Close(); }
    }

    /// <summary>I637: activation gives the keyboard to the rows only when nothing in the window has it: a control the user chose keeps it.</summary>
    [AvaloniaFact]
    public async Task Activation_leaves_the_keyboard_with_a_control_that_has_it()
    {
        var window = ThreeHundredLines();
        window.ShowActivated = false;
        window.Show();
        try
        {
            await Until(() => !window.IsComparing && window.CurrentDifference == 0);
            Layout(window);
            var wrap = window.GetVisualDescendants().OfType<CheckBox>().Single(c =>
                AutomationProperties.GetName(c) == "Wrap long source lines in the aligned comparison");
            window.Activate();
            Assert.True(wrap.Focus());
            for (int i = 0; i < 10; i++) { Layout(window); await Task.Delay(20, TestContext.Current.CancellationToken); }
            Assert.True(window.IsActive);
            Assert.Same(wrap, window.FocusManager?.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// I637: closing Find, by Escape or by Close Find, gives the keyboard back to the current row (the row clicked
    /// before Find opened, or the one Find's match went to), and Down moves it.
    /// </summary>
    [AvaloniaFact]
    public async Task Closing_Find_gives_the_keyboard_back_to_the_current_row()
    {
        var window = OpenThreeHundredLines();
        try
        {
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            await Until(() => !window.IsComparing && window.CurrentDifference == 0);
            Layout(window);
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(c =>
                AutomationProperties.GetName(c) == "Find text in comparison (Ctrl+F)");
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(c =>
                AutomationProperties.GetAutomationId(c) == "Comparison match status");
            // A click on a row's line number gives that row the keyboard, as in I637's observation.
            int clicked = window.CurrentRow - 3;
            var container = rows.ContainerFromIndex(clicked)!;
            Click(window, container, new Point(20, container.Bounds.Height / 2));
            Assert.Equal((clicked, clicked), (window.CurrentRow, FocusedRow(window, rows)));

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(search.IsFocused);
            search.Text = "line 150";
            await Until(() => status.Text?.StartsWith("2 matches", StringComparison.Ordinal) == true);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(search.IsEffectivelyVisible);
            Assert.Equal((clicked, clicked), (window.CurrentRow, FocusedRow(window, rows)));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal((clicked + 1, clicked + 1), (window.CurrentRow, FocusedRow(window, rows)));

            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            Assert.True(search.IsFocused);
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Assert.Equal(149, window.CurrentRow); // line 150, Find's first match
            Click(window, window.GetVisualDescendants().OfType<Button>().Single(b =>
                AutomationProperties.GetName(b) == "Close comparison Find bar"));
            Assert.False(search.IsEffectivelyVisible);
            Assert.Equal("line 150", search.Text);
            Assert.Equal((149, 149), (window.CurrentRow, FocusedRow(window, rows)));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal((150, 150), (window.CurrentRow, FocusedRow(window, rows)));
        }
        finally { window.Close(); }
    }

    /// <summary>I637: without a current row (identical files), the first row takes the keyboard and Down moves from it.</summary>
    [AvaloniaFact]
    public async Task Without_a_current_row_the_first_row_takes_the_keyboard()
    {
        string same = string.Concat(Enumerable.Range(1, 50).Select(i => $"line {i}\n"));
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes(same)),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes(same)));
        window.Activate();
        try
        {
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            await UntilARowHasTheKeyboard(window, rows);
            Assert.StartsWith("Identical", window.Summary);
            Assert.Equal(0, FocusedRow(window, rows));
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal((1, 1), (window.CurrentRow, FocusedRow(window, rows)));
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// I637 with long lines wrapped: when Escape gives the keyboard back to a current row taller than the view, Find's
    /// match stays in view (the whole row brought into view would show its top, far from the match).
    /// </summary>
    [AvaloniaFact]
    public async Task Closing_Find_keeps_the_match_of_a_tall_wrapped_row_in_view()
    {
        string words = string.Join(' ', Enumerable.Range(0, 1200).Select(i => $"w{i:D4}"));
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes($"first\n{words} needle left\nlast\n")),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes($"first\n{words} needle right\nlast\n")));
        window.Activate();
        try
        {
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            var view = window.GetVisualDescendants().OfType<CompareTextView>().Single();
            await Until(() => !window.IsComparing && window.CurrentDifference == 0);
            Layout(window);
            window.GetVisualDescendants().OfType<CheckBox>().Single(c =>
                AutomationProperties.GetName(c) == "Wrap long source lines in the aligned comparison").IsChecked = true;
            Layout(window);
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(c =>
                AutomationProperties.GetName(c) == "Find text in comparison (Ctrl+F)");
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(c =>
                AutomationProperties.GetAutomationId(c) == "Comparison match status");
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            search.Text = "needle";
            await Until(() => status.Text?.StartsWith("2 matches", StringComparison.Ordinal) == true);
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Layout(window);
            Assert.Equal(1, window.CurrentRow);
            var cell = view.RealizedRow(1)!.LeftCell!;
            int match = cell.Text.IndexOf("needle", StringComparison.Ordinal);
            var viewport = view.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
            Assert.True(cell.Bounds.Height > 2 * viewport.Bounds.Height, $"The wrapped row is {cell.Bounds.Height} high in a {viewport.Bounds.Height} view.");
            AssertInView(cell, match, viewport, "after Find's match");

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Layout(window);
            Assert.Equal((1, 1), (window.CurrentRow, FocusedRow(window, rows)));
            AssertInView(view.RealizedRow(1)!.LeftCell!, match, viewport, "after Escape closed Find");
        }
        finally { window.Close(); }
    }

    // I637's case, opened as the application opens it, and active as a window just opened is.
    private static CompareWindow OpenThreeHundredLines()
    {
        var (left, right) = ThreeHundredLineFiles();
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", left), "right.txt", new MemoryContentSource("right.txt", right));
        window.Activate();
        return window;
    }

    // I637's case in a window not shown yet.
    private static CompareWindow ThreeHundredLines()
    {
        var (left, right) = ThreeHundredLineFiles();
        return new CompareWindow("left.txt", new MemoryContentSource("left.txt", left), "right.txt", new MemoryContentSource("right.txt", right), null, null);
    }

    // I637's case: two 300-line files that differ in two lines (100 and 200).
    private static (byte[] Left, byte[] Right) ThreeHundredLineFiles()
    {
        var left = new StringBuilder();
        var right = new StringBuilder();
        for (int line = 1; line <= 300; line++)
        {
            left.Append($"line {line}\n");
            right.Append(line is 100 or 200 ? $"line {line} changed\n" : $"line {line}\n");
        }
        return (Encoding.UTF8.GetBytes(left.ToString()), Encoding.UTF8.GetBytes(right.ToString()));
    }

    /// <summary>The index of the row that has the keyboard; -1 when no row has it (the window or another control does).</summary>
    private static int FocusedRow(Window window, ListBox rows) =>
        window.FocusManager?.GetFocusedElement() is ListBoxItem row ? rows.IndexFromContainer(row) : -1;

    private static void AssertInView(CompareSourceCell cell, int offset, ScrollContentPresenter viewport, string when)
    {
        Rect glyph = cell.SourceBounds(offset);
        var shown = cell.TranslatePoint(glyph.TopLeft + (Vector)cell.TextOrigin, viewport)!.Value;
        Assert.True(glyph.Height > 0 && shown.Y >= -0.5 && shown.Y + glyph.Height <= viewport.Bounds.Height + 0.5,
            $"The match is at {shown.Y} in a {viewport.Bounds.Height} high view {when}.");
    }

    // A click where the user makes it: the pointer must land on the target (or a part of it) at that point.
    private static void Click(Window window, Control target, Point? at = null)
    {
        Layout(window);
        var point = target.TranslatePoint(at ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, target) || hit?.GetVisualAncestors().Contains(target) == true,
            $"The click at {point} lands on {hit?.GetType().Name}, not on {target.GetType().Name}.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void Layout(Window window)
    {
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    private static Button ButtonNamed(Window window, string text) => window.GetVisualDescendants()
        .OfType<Button>().Single(b => b.Content as string == text);

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    // The comparison is shown with a row that has the keyboard; says where things stand when that does not happen.
    private static async Task UntilARowHasTheKeyboard(CompareWindow window, ListBox rows)
    {
        bool Ready() => !window.IsComparing && FocusedRow(window, rows) >= 0;
        for (int i = 0; i < 500 && !Ready(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(Ready(), $"Comparing: {window.IsComparing}; active: {window.IsActive}; current row {window.CurrentRow}; " +
            $"keyboard on {window.FocusManager?.GetFocusedElement()?.GetType().Name ?? "nothing"}.");
    }

    private static async Task Until(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 500 && !await condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(await condition());
    }
}
