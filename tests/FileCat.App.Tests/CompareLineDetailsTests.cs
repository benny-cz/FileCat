using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>FCUX-009: the actual changed glyphs must fit inside both text viewports after user input.</summary>
public sealed class CompareLineDetailsTests
{
    [AvaloniaTheory]
    [InlineData(640, 60)]
    [InlineData(640, 80)]
    [InlineData(640, 200)]
    [InlineData(640, 10_000)]
    [InlineData(980, 60)]
    [InlineData(980, 80)]
    [InlineData(980, 200)]
    [InlineData(980, 10_000)]
    [InlineData(1200, 60)]
    [InlineData(1200, 80)]
    [InlineData(1200, 200)]
    [InlineData(1200, 10_000)]
    public async Task Suffix_change_is_visible_in_both_viewports_through_keyboard_and_pointer(int width, int length)
    {
        string prefix = new('a', length - 1);
        var window = await OpenAsync(prefix + "X\nsame\n" + prefix + "L", prefix + "Y\nsame\n" + prefix + "R", width);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady && window.CurrentDifference == 0);
            window.KeyPress(Key.L, RawInputModifiers.Alt, PhysicalKey.L, "l");
            Layout(window);
            Assert.True(details.LeftText.IsFocused);
            Assert.Equal(prefix + "X", details.LeftText.Text);
            Assert.Equal(prefix + "Y", details.RightText.Text);
            AssertPairVisible(details, "X", "Y");

            window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            await Wait(() => details.IsReady && window.CurrentDifference == 1 && details.LeftText.Text == prefix + "L");
            Layout(window);
            AssertPairVisible(details, "L", "R");

            Click(window, Button(window, "Previous difference"));
            await Wait(() => details.IsReady && window.CurrentDifference == 0 && details.LeftText.Text == prefix + "X");
            Click(window, Button(window, "Focus full-line comparison details"));
            Layout(window);
            AssertPairVisible(details, "X", "Y");

            var wrap = details.GetVisualDescendants().OfType<CheckBox>().Single();
            Click(window, wrap);
            Layout(window);
            Assert.True(wrap.IsChecked);
            AssertPairVisible(details, "X", "Y");
            var leftOrigin = details.LeftText.TranslatePoint(default, details)!.Value;
            var rightOrigin = details.RightText.TranslatePoint(default, details)!.Value;
            Assert.Equal(leftOrigin.Y, rightOrigin.Y); // the paired line/gap stays on the same row while wrapping
            Assert.Empty(AccessibilityTests.Unnamed(window));
            Assert.StartsWith("2 differences", window.Summary); // presentation does not alter comparison semantics
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Exceptional_lines_are_complete_through_bounded_parts_and_summary_rows_stay_small()
    {
        string prefix = new('a', 59_999);
        string left = prefix + "X", right = prefix + "Y";
        var window = await OpenAsync(left, right, 640);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady);
            Layout(window);
            AssertPairVisible(details, "X", "Y");
            Assert.True(details.LeftPartStart > 0);
            var leftParts = new SortedDictionary<int, string>();
            var rightParts = new SortedDictionary<int, string>();
            var previous = Button(details, "Previous part of both full lines");
            while (true)
            {
                leftParts[details.LeftPartStart] = details.LeftText.Text!;
                rightParts[details.RightPartStart] = details.RightText.Text!;
                Assert.InRange(details.LeftText.Text!.Length, 1, CompareLineDetails.PartCharacters + 2);
                Assert.InRange(details.RightText.Text!.Length, 1, CompareLineDetails.PartCharacters + 2);
                if (!previous.IsEnabled) break;
                previous.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Layout(window);
            }
            Assert.Equal(left, string.Concat(leftParts.Values));
            Assert.Equal(right, string.Concat(rightParts.Values));
            Click(window, Button(details, "Reveal the first differing text in both full lines"));
            Layout(window);
            AssertPairVisible(details, "X", "Y");
            Assert.Contains("Part 4 of 4", details.Status);
            Assert.All(window.Rows.OfType<CompareRow>(), row => Assert.True(row.ToString().Length < 100));
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.All(rows.GetVisualDescendants().OfType<TextBlock>(), block =>
                Assert.InRange(block.Text?.Length ?? 0, 0, 550));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Horizontal_scrolling_of_unwrapped_details_keeps_both_sides_at_the_same_offset()
    {
        var window = await OpenAsync(new string('a', 1000) + "X", new string('a', 1000) + "Y", 640);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady);
            Layout(window);
            var left = Assert.Single(details.LeftText.GetVisualDescendants().OfType<ScrollViewer>());
            var right = Assert.Single(details.RightText.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(left.Offset.X > 0);
            left.Offset = new Vector(50, 0);
            Layout(window);
            Assert.Equal(left.Offset.X, right.Offset.X);
            right.Offset = new Vector(100, 0);
            Layout(window);
            Assert.Equal(right.Offset.X, left.Offset.X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Difference_selection_keeps_surrogate_pairs_and_combining_marks_intact()
    {
        string prefix = "unchanged 👩‍💻";
        var window = await OpenAsync(prefix + "e\u0301", prefix + "e\u0300", 640);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.Equal(prefix.Length, details.FirstDifferenceOffset);
            AssertPairVisible(details, "e\u0301", "e\u0300");
            Assert.Equal(prefix + "e\u0301", details.LeftText.Text);
            Assert.Equal(prefix + "e\u0300", details.RightText.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Combining_difference_at_a_part_boundary_reveals_the_decisive_mark_in_both_viewports()
    {
        string prefix = new('x', 16_383);
        string left = prefix + "e\u0301", right = prefix + "e\u0300";
        var window = await OpenAsync(left, right, 640);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.Equal(prefix.Length, details.FirstDifferenceOffset);
            Assert.Equal(left, details.LeftText.Text);
            Assert.Equal(right, details.RightText.Text);
            AssertPairVisible(details, "e\u0301", "e\u0300");
            Assert.DoesNotContain("only part of the differing text element", details.Status);
            Assert.InRange(details.LeftText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);
            Assert.InRange(details.RightText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);

            // Returning from a later source part must restore the whole changed evidence.
            Click(window, Button(details, "Next part of both full lines"));
            Click(window, Button(details, "Reveal the first differing text in both full lines"));
            Layout(window);
            AssertPairVisible(details, "e\u0301", "e\u0300");
            Click(window, details.GetVisualDescendants().OfType<CheckBox>().Single());
            Layout(window);
            AssertPairVisible(details, "e\u0301", "e\u0300");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Oversized_boundary_element_stays_bounded_and_discloses_its_complete_paging_path()
    {
        string prefix = new('x', 16_383);
        string left = prefix + "e" + new string('\u0301', 257);
        string right = prefix + "e" + new string('\u0300', 257);
        var window = await OpenAsync(left, right, 640);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.Contains("only part of the differing text element", details.Status);
            Assert.Contains("Previous/Next part", details.Status);
            Assert.Contains("only part of the differing text element", AutomationProperties.GetHelpText(details.LeftText));
            Assert.Contains("only part of the differing text element", AutomationProperties.GetHelpText(details.RightText));
            Assert.Equal(prefix + "e", details.LeftText.Text);
            Assert.Equal(prefix + "e", details.RightText.Text);
            Assert.InRange(details.LeftText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);
            Assert.InRange(details.RightText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);
            string leftFirst = details.LeftText.Text!, rightFirst = details.RightText.Text!;

            var next = Button(details, "Next part of both full lines");
            Assert.True(next.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Layout(window);
            Assert.Contains("only part of the differing text element", details.Status);
            Assert.Equal(left, leftFirst + details.LeftText.Text);
            Assert.Equal(right, rightFirst + details.RightText.Text);
            Assert.InRange(details.LeftText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);
            Assert.InRange(details.RightText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);
            Assert.False(next.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Gaps_empty_lines_ignored_differences_and_line_endings_have_honest_details()
    {
        using var details = new CompareLineDetails("left", "right");
        var window = new Window { Width = 640, Height = 350, Content = details };
        window.Show();
        try
        {
            details.SetComparisonRow(new CompareRow(DiffKind.RightOnly, null, "", 7, ""));
            await Wait(() => details.IsReady);
            Assert.False(details.LeftText.IsEnabled);
            Assert.True(details.RightText.IsEnabled);
            Assert.Equal("", details.RightText.Text);
            Assert.Equal("Empty line", details.RightText.PlaceholderText);
            Assert.Contains("Only the right", details.Status);

            details.SetComparisonRow(new CompareRow(DiffKind.Equal, 1, "line", 1, "line", EndingDiffers: true));
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.Equal(-1, details.FirstDifferenceOffset);
            Assert.Contains("line endings differ", details.Status);
            Assert.Equal("line", details.LeftText.Text);
            Assert.Equal("line", details.RightText.Text);
            Assert.Equal(0, Math.Abs(details.LeftText.SelectionEnd - details.LeftText.SelectionStart));

            details.SetComparisonRow(new CompareRow(DiffKind.Equal, 2, "a", 2, "A"));
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.Contains("options treat this row as equal", details.Status);
            AssertPairVisible(details, "a", "A");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Queued_detail_focus_does_not_override_newer_focus_or_survive_a_new_row()
    {
        var window = await OpenAsync("left-X", "left-Y", 640);
        try
        {
            var details = Details(window);
            await Wait(() => details.IsReady);
            Layout(window);
            var newer = Button(window, "Focus full-line comparison details");
            details.RightText.Focus();
            details.FocusText(); // focus is deliberately applied after the next layout
            newer.Focus();
            Layout(window);
            Assert.True(newer.IsFocused);
            AssertPairVisible(details, "X", "Y");

            // If the selected row changes before the queued work, its old request cannot focus
            // the newly prepared row even when the requesting control still holds focus.
            details.FocusText();
            details.SetComparisonRow(new CompareRow(DiffKind.Changed, 2, "new-L", 2, "new-R"));
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.True(newer.IsFocused);
            AssertPairVisible(details, "L", "R");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task New_row_and_close_retire_old_detail_work_and_release_source_text()
    {
        using var details = new CompareLineDetails("left", "right");
        var window = new Window { Width = 640, Height = 350, Content = details };
        window.Show();
        try
        {
            string large = new('x', 2_000_000);
            details.SetComparisonRow(new CompareRow(DiffKind.Changed, 1, large + "a", 1, large + "b"));
            details.SetComparisonRow(new CompareRow(DiffKind.Changed, 2, "current-X", 2, "current-Y"));
            await Wait(() => details.IsReady);
            Layout(window);
            Assert.Equal("current-X", details.LeftText.Text);
            AssertPairVisible(details, "X", "Y");
            details.SetComparisonRow(new CompareRow(DiffKind.Changed, 3, large + "c", 3, large + "d"));
            details.Dispose();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Layout(window);
            Assert.False(details.IsReady);
            Assert.Equal("", details.LeftText.Text);
            Assert.Equal("", details.RightText.Text);
        }
        finally { window.Close(); }
    }

    private static async Task<CompareWindow> OpenAsync(string left, string right, int width)
    {
        // Initial size must precede Show: changing Width later does not resize every headless host.
        var window = new CompareWindow("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes(left)),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes(right)), null, null) { Width = width };
        window.Show();
        await Wait(() => !window.IsComparing);
        Layout(window);
        Assert.Equal(width, window.Bounds.Width);
        Assert.Equal(width, window.ClientSize.Width);
        var details = Details(window);
        Assert.InRange(details.Bounds.Width, width - 32, width);
        Assert.InRange(details.LeftText.Bounds.Width, width / 2 - 40, width / 2);
        Assert.InRange(details.RightText.Bounds.Width, width / 2 - 40, width / 2);
        return window;
    }

    private static CompareLineDetails Details(CompareWindow window) => Assert.Single(window.GetVisualDescendants().OfType<CompareLineDetails>());

    private static Button Button(Control root, string name) => root.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == name);

    private static void Click(Window window, Control control)
    {
        Layout(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void AssertPairVisible(CompareLineDetails details, string left, string right)
    {
        Assert.Equal(left, details.LeftText.SelectedText);
        Assert.Equal(right, details.RightText.SelectedText);
        AssertChangedGlyphInViewport(details.LeftText);
        AssertChangedGlyphInViewport(details.RightText);
    }

    private static void AssertChangedGlyphInViewport(TextBox field)
    {
        var presenter = Assert.Single(field.GetVisualDescendants().OfType<TextPresenter>());
        var viewport = Assert.Single(field.GetVisualDescendants().OfType<ScrollContentPresenter>());
        var glyph = presenter.TextLayout.HitTestTextPosition(field.SelectionStart);
        var point = presenter.TranslatePoint(glyph.TopLeft, viewport)!.Value;
        Assert.True(viewport.Bounds.Width > 20 && viewport.Bounds.Height > 10, "The detail text viewport must have actual readable area.");
        Assert.True(glyph.Height > 0, "The changed glyph must have been laid out.");
        Assert.True(point.X >= -0.5 && point.Y >= -0.5, $"Changed glyph starts outside the viewport: {point}.");
        Assert.True(point.X + glyph.Width <= viewport.Bounds.Width + 0.5, "The changed glyph is clipped horizontally.");
        Assert.True(point.Y + glyph.Height <= viewport.Bounds.Height + 0.5,
            $"The changed glyph is clipped vertically: glyph {point.Y}+{glyph.Height}, viewport {viewport.Bounds.Height}, field {field.Bounds}, details {field.FindAncestorOfType<CompareLineDetails>()?.Bounds}, children {string.Join("; ", field.FindAncestorOfType<CompareLineDetails>()?.Children.Select(c => c.GetType().Name + " " + c.Bounds) ?? [])}, wrap {field.TextWrapping}.");
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "Comparison details did not settle.");
    }
}
