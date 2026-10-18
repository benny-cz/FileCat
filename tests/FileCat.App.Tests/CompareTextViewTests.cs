using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>FCUX-009: exact source evidence must be visible in the main aligned text surface.</summary>
public sealed class CompareTextViewTests
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
    public async Task Main_rows_reveal_suffix_changes_by_keyboard_pointer_and_aligned_wrap(int width, int length)
    {
        string prefix = new('a', length - 1);
        var window = await Open(prefix + "X\nsame\n" + prefix + "L", prefix + "Y\nsame\n" + prefix + "R", width);
        try
        {
            var view = View(window);
            Assert.Equal(width, window.Bounds.Width);
            Assert.Equal(width, window.ClientSize.Width);
            AssertVisible(window, view, "X", "Y");
            var first = Assert.IsType<CompareTextRow>(view.RealizedRow(window.CurrentRow));
            Assert.Equal(prefix + "X", DrawnText(first.LeftCell!));
            Assert.Equal(prefix + "Y", DrawnText(first.RightCell!));
            Assert.All(first.LeftCell!.SourceLayout!.TextLines, line => Assert.False(line.HasCollapsed));
            Assert.All(first.RightCell!.SourceLayout!.TextLines, line => Assert.False(line.HasCollapsed));

            window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            Layout(window);
            Assert.Equal(1, window.CurrentDifference);
            AssertVisible(window, view, "L", "R");

            Click(window, Button(window, "Previous difference"));
            Layout(window);
            Assert.Equal(0, window.CurrentDifference);
            AssertVisible(window, view, "X", "Y");

            Click(window, window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => AutomationProperties.GetName(c) == "Wrap long source lines in the aligned comparison"));
            Layout(window);
            Assert.True(view.Wrap);
            Assert.False(view.HorizontalBar.IsVisible);
            AssertVisible(window, view, "X", "Y");
            window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            Layout(window);
            AssertVisible(window, view, "L", "R");
            Assert.StartsWith("2 differences", window.Summary);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Shared_horizontal_position_has_a_keyboard_route_and_never_moves_line_numbers()
    {
        string prefix = new('x', 2000);
        var window = await Open(prefix + "X", prefix + "Y", 640);
        try
        {
            var view = View(window);
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single();
            var row = view.RealizedRow(window.CurrentRow)!;
            double numberX = row.Children.OfType<TextBlock>().First().Bounds.X;
            double before = view.HorizontalOffset;
            Assert.True(before > 0);
            FileCat.App.Controls.ListKeys.Focus(rows);
            Assert.Same(rows.ContainerFromIndex(window.CurrentRow), window.FocusManager!.GetFocusedElement());
            Layout(window);
            before = view.HorizontalOffset;
            var cached = row.LeftCell!.SourceLayout;
            window.KeyPress(Key.Left, RawInputModifiers.Alt, PhysicalKey.ArrowLeft, null);
            Layout(window);
            Assert.True(view.HorizontalOffset < before);
            Assert.Same(cached, row.LeftCell.SourceLayout);
            Assert.Equal(row.LeftCell!.TextOrigin.X, row.RightCell!.TextOrigin.X);
            Assert.Equal(-view.HorizontalOffset, row.LeftCell.TextOrigin.X);
            Assert.Equal(numberX, row.Children.OfType<TextBlock>().First().Bounds.X);
            view.HorizontalBar.Value = 0;
            Layout(window);
            window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
            Layout(window);
            AssertVisible(window, view, "X", "Y");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Tab_and_combining_sequences_use_the_actual_layout_source_position()
    {
        string prefix = new string('x', 600) + "\t👩‍💻\t";
        var window = await Open(prefix + "e\u0301", prefix + "e\u0300", 640);
        try
        {
            var view = View(window);
            AssertVisible(window, view, "e\u0301", "e\u0300");
            view.Wrap = true;
            Layout(window);
            AssertVisible(window, view, "e\u0301", "e\u0300");
            var row = view.RealizedRow(window.CurrentRow)!;
            Assert.Equal(prefix.Length, row.Difference.Start);
            Assert.Equal(prefix + "e\u0301", DrawnText(row.LeftCell!));
            Assert.Equal(prefix + "e\u0300", DrawnText(row.RightCell!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Wrapped_unequal_lengths_and_inserted_gaps_share_row_height_and_source_numbers()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows) { Wrap = true };
        var window = new Window { Width = 640, Height = 600, Content = view };
        rows.ItemsSource = new object[]
        {
            new CompareRow(DiffKind.Changed, 11, new string('a', 300), 11, "short"),
            new CompareRow(DiffKind.RightOnly, null, "", 12, new string('b', 300)),
            new CompareRow(DiffKind.Equal, 12, "tail", 13, "tail"),
        };
        window.Show();
        try
        {
            Layout(window);
            var changed = view.RealizedRow(0)!;
            Assert.NotNull(changed);
            Assert.True(changed.LeftCell!.SourceLayout!.Height > changed.RightCell!.SourceLayout!.Height);
            Assert.Equal(changed.LeftCell.Bounds.Y, changed.RightCell.Bounds.Y);
            Assert.Equal(changed.LeftCell.Bounds.Height, changed.RightCell.Bounds.Height);
            var numbers = changed.Children.OfType<TextBlock>().ToArray();
            Assert.Equal(numbers[0].Bounds.Y, numbers[1].Bounds.Y);
            Assert.Equal("Left source line 11", AutomationProperties.GetName(numbers[0]));
            Assert.Equal("Right source line 11", AutomationProperties.GetName(numbers[1]));

            rows.ScrollIntoView(1);
            Layout(window);
            var inserted = view.RealizedRow(1)!;
            Assert.Null(inserted.LeftCell);
            Assert.NotNull(inserted.RightCell);
            var gap = Assert.Single(inserted.Children.OfType<Border>());
            Assert.Equal(inserted.Bounds.Height, gap.Bounds.Height);
            Assert.Contains("no source text", AutomationProperties.GetName(gap));
            Assert.True(inserted.Bounds.Height > 100);
            Assert.Equal("Right source line 12", AutomationProperties.GetName(inserted.Children.OfType<TextBlock>().Last()));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Transient_zero_width_measure_defers_long_line_shaping_until_the_real_pane_exists()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows) { Wrap = true };
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompareSourceCell(view,
            new string('x', CompareTextView.MaximumLineCharacters + 1), null));
        var cell = new CompareSourceCell(view, new string('x', 10_000), null);
        foreach (double width in new[] { 0d, 1d, 20d, 79d })
        {
            cell.Measure(new Size(width, double.PositiveInfinity));
            Assert.Equal(18, cell.DesiredSize.Height - cell.Margin.Top - cell.Margin.Bottom);
            Assert.Null(cell.SourceLayout); // no accidental 10,000-row interim layout
        }
        cell.Measure(new Size(220, double.PositiveInfinity));
        Assert.True(cell.SourceLayout!.Height > 18);
        Assert.InRange(cell.SourceLayout!.TextLines.Count, 2, 700);
    }

    [AvaloniaFact]
    public void Source_peer_exposes_exact_read_only_text_without_shaping_and_detach_releases_the_cache()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows) { Wrap = true };
        const string source = "left\t👩‍💻e\u0301 exact source";
        var cell = new CompareSourceCell(view, source, null);
        AutomationProperties.SetName(cell, "Left source line 42");
        var peer = ControlAutomationPeer.CreatePeerForElement(cell)!;
        var value = Assert.IsAssignableFrom<IValueProvider>(peer);
        Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
        Assert.Equal("Left source line 42", peer.GetName());
        Assert.True(value.IsReadOnly);
        Assert.Equal(source, value.Value);
        Assert.Contains("Alt+L", peer.GetHelpText());
        Assert.Throws<InvalidOperationException>(() => value.SetValue("replacement"));
        Assert.Null(cell.SourceLayout);
        var window = new Window { Width = 240, Height = 200, Content = cell };
        window.Show();
        try
        {
            Layout(window);
            var before = cell.SourceLayout;
            Assert.NotNull(before);
            cell.FontSize *= 1.5;
            Layout(window);
            Assert.NotSame(before, cell.SourceLayout);
            Assert.Equal(source, value.Value);
            window.Content = null; // exercise actual detach even if a backend retains a closed Window's content
            Assert.Null(cell.SourceLayout);
        }
        finally { window.Close(); }
        Assert.Null(cell.SourceLayout);
        Assert.Equal(source, value.Value); // immutable evidence survives a retained peer without a layout graph
    }

    [AvaloniaFact]
    public void Repeated_arrangement_reuses_bounded_layout_and_fragment_geometry_for_many_changes()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows) { Wrap = true };
        string source = string.Concat(Enumerable.Repeat("ab", 5000));
        var changes = Enumerable.Range(0, 5000).Select(i => (i * 2 + 1, 1)).ToArray();
        var cell = new CompareSourceCell(view, source, changes);
        cell.Measure(new Size(220, double.PositiveInfinity));
        cell.Arrange(new Rect(0, 0, 220, cell.DesiredSize.Height));
        var layout = cell.SourceLayout;
        Assert.NotNull(layout);
        int builds = cell.LayoutBuildCount;
        Assert.InRange(cell.UnderlineFragmentCount, 5000, 5500);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            cell.InvalidateArrange();
            cell.Measure(new Size(220, double.PositiveInfinity));
            cell.Arrange(new Rect(0, i % 2, 220, cell.DesiredSize.Height));
        }
        Assert.Same(layout, cell.SourceLayout);
        Assert.Equal(builds, cell.LayoutBuildCount);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1_000_000,
            "Rearranging a cached source cell must not allocate wrapped lines or all-span geometry again.");
        Assert.Equal(source, DrawnText(cell));
        Assert.True(cell.SourceBounds(source.Length - 1).Y > 0);
    }

    [AvaloniaFact]
    public async Task Exceptional_rows_disclose_the_bound_and_open_exact_full_line_parts_by_pointer_and_keyboard()
    {
        string prefix = new('a', 59_999);
        var window = await Open(prefix + "X", prefix + "Y", 640);
        try
        {
            var view = View(window);
            var row = view.RealizedRow(window.CurrentRow)!;
            Assert.True(row.IsExceptional);
            Assert.Null(row.LeftCell);
            Assert.Null(row.RightCell);
            var actions = row.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(2, actions.Length);
            Assert.All(actions, action =>
            {
                Assert.Contains($"{60_000:N0} UTF-16 characters", AutomationProperties.GetName(action));
                Assert.Contains($"{CompareTextView.MaximumLineCharacters:N0}-character main-row shaping limit", AutomationProperties.GetHelpText(action));
            });
            Click(window, actions[1]);
            var detail = window.GetVisualDescendants().OfType<CompareLineDetails>().Single();
            await Wait(() => detail.IsReady);
            Layout(window);
            Assert.True(detail.LeftText.IsFocused);
            Assert.Equal("X", detail.LeftText.SelectedText);
            Assert.Equal("Y", detail.RightText.SelectedText);
            Assert.Contains("Part 4 of 4", detail.Status);
            Assert.True(detail.LeftPartStart > 0);
            Assert.Equal((prefix + "X")[detail.LeftPartStart..], detail.LeftText.Text);
            Assert.InRange(detail.LeftText.Text!.Length, 1, CompareLineDetails.MaximumPartCharacters);
            Assert.All(row.GetVisualDescendants().OfType<TextBlock>(), block => Assert.True(DrawnText(block).Length < 160));
            window.KeyPress(Key.L, RawInputModifiers.Alt, PhysicalKey.L, "l");
            Layout(window);
            Assert.True(detail.LeftText.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Million_row_source_keeps_realized_controls_and_shaped_text_bounded_at_both_ends()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows);
        var source = new VirtualRows(1_000_000);
        rows.ItemsSource = source;
        var window = new Window { Width = 640, Height = 500, Content = view };
        window.Show();
        try
        {
            Layout(window);
            Assert.InRange(view.RealizedRowCount, 1, 64);
            Assert.InRange(source.RequestedCount, 1, 128);
            rows.ScrollIntoView(999_999);
            Layout(window);
            Assert.NotNull(view.RealizedRow(999_999));
            Assert.InRange(view.RealizedRowCount, 1, 64);
            Assert.InRange(source.RequestedCount, 1, 256);
            Assert.InRange(view.GetVisualDescendants().OfType<CompareSourceCell>().Count(), 2, 128);
            Assert.All(view.GetVisualDescendants().OfType<CompareSourceCell>(), cell =>
                Assert.InRange(DrawnText(cell).Length, 1, CompareTextView.MaximumLineCharacters));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Retired_or_closed_row_actions_and_queued_reveal_cannot_change_the_new_source()
    {
        var rows = new ListBox();
        using var view = new CompareTextView(rows);
        var first = new CompareRow(DiffKind.Changed, 1, new string('x', 20_000), 1, new string('y', 20_000));
        rows.ItemsSource = new object[] { first };
        var window = new Window { Width = 640, Height = 500, Content = view };
        window.Show();
        try
        {
            Layout(window);
            var oldContainer = Assert.IsType<ListBoxItem>(rows.ContainerFromIndex(0));
            var oldRow = Assert.IsType<CompareTextRow>(view.RealizedRow(0));
            rows.SelectedIndex = 0;
            view.RevealSelected();
            view.Reset();
            Assert.Null(oldRow.Source);
            ControlRetirement.ClearItems(rows);
            Assert.Null(oldContainer.Content);
            Assert.Null(oldContainer.DataContext);
            Assert.DoesNotContain(oldRow, oldContainer.GetVisualDescendants());
            var current = new CompareRow(DiffKind.Changed, 2, "current-X", 2, "current-Y");
            rows.ItemsSource = new object[] { current };
            rows.SelectedIndex = 0;
            rows.ScrollIntoView(0); // mirror the owning window's explicit realization after result replacement
            int requests = 0;
            view.DetailsRequested += _ => requests++;
            view.OpenDetails(oldRow);
            Layout(window);
            Assert.Equal(0, requests);
            Assert.Same(current, rows.SelectedItem);
            Assert.Equal(0, view.HorizontalOffset);
            Assert.Same(oldContainer, rows.ContainerFromIndex(0));
            var currentRow = Assert.IsType<CompareTextRow>(view.RealizedRow(0));
            Assert.Same(current, currentRow.Source);
            Assert.NotSame(oldRow, currentRow);
            var currentCell = Assert.IsType<CompareSourceCell>(currentRow.LeftCell);
            Assert.Equal("current-X", currentCell.Text);
            Assert.NotNull(currentCell.SourceLayout);
            view.RevealSelected();
            view.Dispose();
            Assert.Null(currentCell.SourceLayout);
            window.UpdateLayout();
            Assert.Null(currentCell.SourceLayout);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            view.OpenDetails(oldRow);
            Assert.Equal(0, requests);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_blocked_reveal_queue_does_not_keep_retired_result_rows_or_the_collection(bool dispose)
    {
        var (view, items, source) = QueueAndRetire(dispose);
        try
        {
            // Deliberately do not dispatch the queued Loaded operation: cancellation must release
            // its model ownership immediately, independently of when that operation can run.
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            Assert.False(items.IsAlive, "A retired reveal must not retain its former entire result collection.");
            Assert.False(source.IsAlive, "A retired reveal must not retain its former selected source row.");
            GC.KeepAlive(view);
        }
        finally { view.Dispose(); Dispatcher.UIThread.RunJobs(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CompareTextView View, WeakReference Items, WeakReference Source) QueueAndRetire(bool dispose)
    {
        var rows = new ListBox();
        var view = new CompareTextView(rows);
        var source = new CompareRow(DiffKind.Changed, 1, "owned left", 1, "owned right");
        var items = new object[] { source };
        rows.ItemsSource = items;
        rows.SelectedIndex = 0;
        view.RevealSelected();
        if (dispose) view.Dispose();
        else view.Reset();
        ControlRetirement.ClearItems(rows);
        return (view, new WeakReference(items), new WeakReference(source));
    }

    private static async Task<CompareWindow> Open(string left, string right, int width)
    {
        var window = new CompareWindow("left.txt", new MemoryContentSource("left", Encoding.UTF8.GetBytes(left)),
            "right.txt", new MemoryContentSource("right", Encoding.UTF8.GetBytes(right)), null, null) { Width = width };
        window.Show();
        await Wait(() => !window.IsComparing && window.CurrentDifference >= 0);
        Layout(window);
        return window;
    }

    private static CompareTextView View(CompareWindow window) => Assert.Single(window.GetVisualDescendants().OfType<CompareTextView>());

    private static void AssertVisible(CompareWindow window, CompareTextView view, string left, string right)
    {
        Layout(window);
        var row = Assert.IsType<CompareTextRow>(view.RealizedRow(window.CurrentRow));
        var source = Assert.IsType<CompareRow>(row.Source);
        Assert.False(row.IsExceptional);
        Assert.Equal(left, source.LeftText.Substring(row.Difference.Start, row.Difference.LeftLength));
        Assert.Equal(right, source.RightText.Substring(row.Difference.Start, row.Difference.RightLength));
        var viewport = Assert.Single(view.GetVisualDescendants().OfType<ScrollContentPresenter>());
        AssertGlyph(row.LeftCell!, row.Difference.Start, viewport);
        AssertGlyph(row.RightCell!, row.Difference.Start, viewport);
    }

    private static void AssertGlyph(CompareSourceCell cell, int offset, ScrollContentPresenter viewport)
    {
        Rect glyph = cell.SourceBounds(offset);
        var local = glyph.TopLeft + (Vector)cell.TextOrigin;
        var visible = cell.TranslatePoint(local, viewport)!.Value;
        Assert.True(cell.Bounds.Width > 100 && viewport.Bounds.Height > 20, "The main comparison must have actual readable viewport area.");
        Assert.True(glyph.Height > 0);
        Assert.True(local.X >= -0.5, $"Changed glyph starts left of its source pane: {local}, offset {cell.TextOrigin.X}.");
        Assert.True(local.X + glyph.Width <= cell.Bounds.Width + 0.5, $"Changed glyph is clipped to the right: {local}, pane {cell.Bounds.Width}.");
        Assert.True(visible.Y >= -0.5, $"Changed glyph starts above the main viewport: {visible}.");
        Assert.True(visible.Y + glyph.Height <= viewport.Bounds.Height + 0.5, $"Changed glyph is below the main viewport: {visible}, viewport {viewport.Bounds.Height}.");
    }

    private static string DrawnText(CompareSourceCell cell) => cell.Text;
    private static string DrawnText(TextBlock block) => block.Inlines is { Count: > 0 } inlines
        ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : block.Text ?? "";
    private static Button Button(Control root, string name) => root.GetVisualDescendants().OfType<Button>()
        .Single(b => AutomationProperties.GetName(b) == name);
    private static void Click(Window window, Control control)
    {
        Layout(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.True(new Rect(window.ClientSize).Contains(point), $"Pointer target {point} is outside the actual window viewport.");
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true,
            $"Pointer target {control.GetType().Name} {AutomationProperties.GetName(control)} at {point} is covered by {hit?.GetType().Name}.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
    private static void Layout(Window window)
    {
        for (int i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }
    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(ready(), "Comparison did not settle.");
    }

    private sealed class VirtualRows(int count) : IList
    {
        private readonly Dictionary<int, CompareRow> _requested = [];
        internal int RequestedCount => _requested.Count;
        public object? this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                if (!_requested.TryGetValue(index, out var row)) _requested.Add(index, row = new CompareRow(DiffKind.Equal, index + 1, $"source-{index}", index + 1, $"source-{index}"));
                return row;
            }
            set => throw new NotSupportedException();
        }
        public int Count => count;
        public bool IsFixedSize => true;
        public bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public int IndexOf(object? value) => value is CompareRow { LeftLine: { } line } ? line - 1 : -1;
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public IEnumerator GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        public void CopyTo(Array array, int index) { foreach (object? item in this) array.SetValue(item, index++); }
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
