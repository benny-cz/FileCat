using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task PointerSelectedAnalysisFolderDrillsWithEnter()
    {
        foreach (int pointerX in new[] { 20, 65 })
        {
            using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
            await model.RebuildAsync(true); var prepared = model.Tree;
            var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); window.Show();
            try
            {
                // Opening attaches the session and publishes a fresh projection. Render its actual
                // frame before observing a point; an old renderer hit tree can miss a newer row.
                await Until(() => !ReferenceEquals(model.Tree, prepared));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationLabel(l) == "Ranked analysis entries");
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
                var point = row.TranslatePoint(new Point(pointerX, row.Bounds.Height / 2), window)!.Value;
                var hit = window.InputHitTest(point) as Visual;
                Assert.True(hit == row || hit?.GetVisualAncestors().Contains(row) == true,
                    $"Pointer {point} missed row {row.Bounds} in list {list.Bounds}/client {window.ClientSize}; hit {hit?.GetType().Name}.");
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Assert.Equal("n:1", model.Selection.Focus); Assert.True(list.IsKeyboardFocusWithin);
                Assert.Equal(pointerX == 20, window.FocusManager?.GetFocusedElement() is CheckBox);
                bool marked = model.Selection.Marks.Contains(1ul);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Assert.Equal(!marked, model.Selection.Marks.Contains(1ul));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Until(() => model.Selection.Scope == "n:1");
            }
            finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        }
    }
    [AvaloniaFact]
    public void TableOrderingKeepsExactIntegersMissingLastAndBoundedOwnership()
    {
        var seed = new AnalysisTreeItem("a", "scope", "A", 1, AnalysisKind.File, false, true, 0, 0, 1, false, false, "file", [], []);
        var zero = seed with { Key = "zero", Name = "Zero" };
        var missing = seed with { Key = "missing", Name = "Missing", Unknown = true, LogicalUnknown = true };
        var huge = seed with { Key = "huge", Name = "Huge", Value = UInt128.MaxValue, LogicalSize = UInt128.MaxValue };
        var adjacent = seed with { Key = "adjacent", Name = "Adjacent", Value = UInt128.MaxValue - 1, LogicalSize = UInt128.MaxValue - 1 };
        ImmutableArray<AnalysisTreeItem> rows = [missing, huge, zero, adjacent];
        foreach (var field in new[] { AnalysisTableSort.ActiveMetric, AnalysisTableSort.FileSize })
        {
            using var up = AnalysisTableOrder.Apply(rows, field, false);
            using var down = AnalysisTableOrder.Apply(rows, field, true);
            Assert.Equal(new[] { "zero", "adjacent", "huge", "missing" }, up.Rows.Select(r => r.Key));
            Assert.Equal(new[] { "huge", "adjacent", "zero", "missing" }, down.Rows.Select(r => r.Key));
        }
        using var names = AnalysisTableOrder.Apply(rows, AnalysisTableSort.Name, false);
        Assert.Equal(new[] { "adjacent", "huge", "missing", "zero" }, names.Rows.Select(r => r.Key));
        names.Dispose(); Assert.Throws<ObjectDisposedException>(() => names.Rows);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => AnalysisTableOrder.Apply(rows, AnalysisTableSort.Name, cancellation: cancel.Token));
        Assert.Throws<InvalidOperationException>(() => AnalysisTableOrder.Apply(Enumerable.Repeat(seed, 1_400_000).ToImmutableArray(), AnalysisTableSort.Name));
        using var recovery = AnalysisTableOrder.Apply(rows, AnalysisTableSort.Name);
        Assert.Equal(4, recovery.Rows.Length);
        var excluded = seed with { Key = "excluded", Name = "Excluded", IncludedByPolicy = false };
        foreach (bool descending in new[] { true, false })
        {
            using var ordered = AnalysisTableOrder.Apply([excluded, huge, zero], AnalysisTableSort.ActiveMetric, descending);
            Assert.Equal("excluded", ordered.Rows[^1].Key);
        }
    }

    [AvaloniaFact]
    public async Task SortAndBreadcrumbNavigationPreserveCapturedFocusMarksAndBasket()
    {
        using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
        await model.RebuildAsync(true); model.Focus("n:2", open: true); await Until(() => model.Selection.Scope == "n:2");
        model.Focus("n:4", toggleMark: true); model.StageSelection();
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var sort = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationLabel(c) == "Analysis table sort");
            var direction = window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationLabel(c) == "Analysis table sort direction");
            sort.SelectedIndex = (int)AnalysisTableSort.Name; direction.SelectedIndex = 0;
            await model.RebuildAsync(true); await Until(() => model.Rows.Length == 2 && model.Rows[0].Key == "n:6");
            direction.SelectedIndex = 1; await model.RebuildAsync(true); await Until(() => model.Rows[0].Key == "n:4");
            sort.SelectedIndex = (int)AnalysisTableSort.ActiveMetric; await model.RebuildAsync(true);
            await Until(() => model.Rows[0].Key == "n:4" && model.Rows[1].Key == "n:6");
            Assert.Equal("n:4", model.Selection.Focus); Assert.Equal(new ulong[] { 4 }, model.Selection.Marks); Assert.Equal(new ulong[] { 4 }, model.Selection.Basket);
            Assert.Equal(new[] { "All locations", "Fixture", "Projects" }, model.Breadcrumbs.Select(p => p.Label));
            model.NavigateToScope("n:3"); Assert.Equal("n:2", model.Selection.Scope);
            var parent = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationLabel(b) == "Go to captured location: Fixture");
            parent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => model.Selection.Scope == "n:1" && model.Rows.Any(r => r.Key == "n:2"));
            model.Back(); await Until(() => model.Selection.Scope == "n:2" && model.Rows.Any(r => r.Key == "n:4"));
            model.Forward(); await Until(() => model.Selection.Scope == "n:1" && model.Rows.Any(r => r.Key == "n:2"));
            Assert.Equal("n:4", model.Selection.Focus); Assert.Contains(4ul, model.Selection.Marks); Assert.Contains(4ul, model.Selection.Basket);
            Assert.Equal((UInt128)98304, model.Tree!.ScopeValue(AnalysisTree.RootKey));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task DeepBreadcrumbsFoldAncestorsIntoAnExplicitInertMenu()
    {
        using var styles = new ProductStyles();
        var snapshot = Fixture() with { Nodes = Enumerable.Range(1, 9).Select(i => new AnalysisNode((ulong)i, "r", (ulong)(i - 1), "Level " + i, AnalysisKind.Directory)).ToImmutableArray(), Objects = [], Facts = [], Issues = [] };
        using var model = new DiskSpaceViewModel(snapshot); await model.RebuildAsync(true); model.Focus("n:9", open: true);
        await Until(() => model.Selection.Scope == "n:9");
        var window = new DiskSpaceWindow { Width = 380, Height = 430 }; window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var ancestors = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Ancestors…");
            var entries = ancestors.ContextMenu!.Items.OfType<MenuItem>().ToArray(); Assert.Equal(7, entries.Length);
            Assert.Equal("Level 1", entries[0].Header); Assert.Equal("Level 7", entries[^1].Header);
            entries[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => model.Selection.Scope == "n:5");
            Assert.Equal("Level 5", model.Breadcrumbs[^1].Label); Assert.Equal("n:9", model.Selection.Focus);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
    // The default wait is 20 seconds: on the 4-processor Ubuntu and 8-processor Windows test VMs, views waiting on native
    // indexed work (exploring, marking, saving, opening and comparing inventories) were still working after 5 seconds in
    // the native runs of 2026-11-23, a different test each time. A passing test returns as soon as its state appears.
    private static async Task Until(Func<bool> predicate, Func<string>? diagnostic = null, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (!predicate() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.True(predicate(), "The requested view state was not published." + (diagnostic is null ? "" : " " + diagnostic()));
    }
}
