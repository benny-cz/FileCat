using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Input;
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
            ButtonNamed(window, "Find…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(search.IsVisible);
            search.Text = "needle";
            await Until(() => status.Text?.StartsWith("2 matches", StringComparison.Ordinal) == true);
            ButtonNamed(window, "Next match").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.CurrentRow >= 90);
            Assert.Equal("needle", view.SelectedSourceText().Text);
            Assert.Contains("Match 1 of 2", status.Text);
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Assert.Contains("Match 2 of 2", status.Text);
            window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
            Assert.Contains("Match 1 of 2", status.Text);
            scope.SelectedIndex = 1;
            await Until(() => status.Text?.StartsWith("1 match", StringComparison.Ordinal) == true);
            var changed = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Changed rows only");
            changed.IsChecked = true;
            await Until(() => status.Text?.StartsWith("No matches", StringComparison.Ordinal) == true);
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
            window.UpdateLayout();
            await Until(() => view.RealizedRow(first)?.LeftCell?.SourceLayout is not null &&
                view.RealizedRow(last)?.LeftCell?.SourceLayout is not null);
            var firstCell = view.RealizedRow(first)!.LeftCell!;
            var lastCell = view.RealizedRow(last)!.LeftCell!;
            Point At(CompareSourceCell cell, int offset)
            {
                var rect = cell.SourceBounds(offset);
                return cell.TranslatePoint(new Point(rect.X + cell.TextOrigin.X + 1, rect.Y + rect.Height / 2), window)!.Value;
            }
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

    private static Button ButtonNamed(Window window, string text) => window.GetVisualDescendants()
        .OfType<Button>().Single(b => b.Content as string == text);

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private static async Task Until(Func<Task<bool>> condition)
    {
        for (int i = 0; i < 500 && !await condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(await condition());
    }
}
