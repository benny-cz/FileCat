using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
    private static Func<string> TraceTableGesture(Window window, DiskSpaceViewModel model)
    {
        var events = new List<string>();
        int Interaction() => (int)typeof(DiskSpaceViewModel).GetField("_interaction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(model)!;
        string State(PointerEventArgs args)
        {
            var source = args.Source as Visual;
            var row = source as ListBoxItem ?? source?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            return $"pointer={args.Pointer.Id} source={args.Source?.GetType().Name} row={(row?.Content as AnalysisTreeItem)?.Key} interaction={Interaction()} rows={model.Rows.Length} marks={string.Join(',', model.Selection.Marks.Order())}";
        }
        window.AddHandler(InputElement.PointerPressedEvent, (_, args) => events.Add("down " + State(args)), RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, (_, args) => events.Add("up " + State(args)), RoutingStrategies.Bubble, handledEventsToo: true);
        return () => $"Final interaction={Interaction()} rows={model.Rows.Length} scope={model.Selection.Scope}; " + string.Join(" | ", events.TakeLast(12));
    }
    private static async Task ShowRangeWindow(Window window, DiskSpaceViewModel model)
    {
        var prepared = model.Tree; window.Show();
        // Attaching starts a new projection. Observe its publication and render its hit tree
        // before retaining a container: the preceding headless frame can target an old row.
        await Until(() => !ReferenceEquals(model.Tree, prepared)); Frame(window);
    }
    private static Point RangeRowPoint(Window window, ListBoxItem row, double x = 65)
    {
        Frame(window);
        var point = row.TranslatePoint(new Point(x, row.Bounds.Height / 2), window)!.Value;
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(hit == row || hit?.GetVisualAncestors().Contains(row) == true,
            $"Point {point} missed retained row {row.Bounds}; hit {hit?.GetType().Name}.");
        return point;
    }
    private static AnalysisSnapshot RangeFixture()
    {
        var snapshot = Fixture();
        for (int i = 7; i <= 9; i++) snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Add(new((ulong)i, "r", 2, ((char)('a' + i - 7)) + ".txt", AnalysisKind.File, "range-" + i)),
            Objects = snapshot.Objects.Add(new("range-" + i, "range-" + i, AnalysisMeasurement.Known((ulong)i), AnalysisMeasurement.Known(1)))
        };
        return snapshot;
    }

    private static async Task<DiskSpaceViewModel> RangeModel()
    {
        var model = new DiskSpaceViewModel(RangeFixture()) { Metric = AnalysisMetric.FileSize, Chart = "Table", Sort = AnalysisTableSort.Name, SortDescending = false };
        await model.RebuildAsync(true); model.Focus("n:2", open: true); await Until(() => model.Selection.Scope == "n:2");
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt", "source.cs", "Unknown\\u202E.bin" }, model.Rows.Select(r => r.Name));
        return model;
    }

    [AvaloniaFact]
    public async Task InitialTextNotificationCannotReplaceAnExplicitProjectionWithTheUneditedQuery()
    {
        using var styles = new ProductStyles(); using var model = await RangeModel();
        var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); window.Show();
        try
        {
            model.Query = "name:b.txt"; await model.RebuildAsync(true);
            await Until(() => model.Rows.Length == 1);
            // Cross the actual debounce interval while dispatching the initial queued notification.
            await Task.Delay(600, TestContext.Current.CancellationToken); Frame(window);
            Assert.Equal("name:b.txt", model.Query); Assert.Equal("n:8", Assert.Single(model.Rows).Key);
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == "Filter observations");
            query.Text = "name:c.txt";
            await Until(() => model.Query == "name:c.txt" && model.Rows.Length == 1 && model.Rows[0].Key == "n:9");
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task TableRangeShrinksOverTheCapturedOrderAndPreservesOtherMarksAndBasket()
    {
        using var model = await RangeModel(); model.Selection.MarkCaptured([5]); model.Selection.StageCaptured([5]);
        model.Focus("n:7"); Assert.True(model.FocusTableRange("n:6")); Assert.Equal(new ulong[] { 4, 5, 6, 7, 8, 9 }, model.Selection.Marks.Order());
        await model.RebuildAsync(true); // An equivalent publication must not turn the previous range into unrelated marks.
        Assert.True(model.FocusTableRange("n:8")); Assert.Equal(new ulong[] { 5, 7, 8 }, model.Selection.Marks.Order());
        Assert.Equal("n:8", model.Selection.Focus); Assert.Equal(new ulong[] { 5 }, model.Selection.Basket);
        model.SortDescending = true; await model.RebuildAsync(true); model.Focus("n:6");
        Assert.True(model.FocusTableRange("n:9")); Assert.Equal(new ulong[] { 4, 5, 6, 7, 8, 9 }, model.Selection.Marks.Order());
        model.Selection.ClearMarks(); Assert.True(model.FocusTableRange("n:4")); Assert.Equal(new ulong[] { 4, 6 }, model.Selection.Marks.Order());
    }

    [AvaloniaFact]
    public async Task PendingFilterCannotBroadenOrRetargetTheDisplayedRangeAndMissingAnchorStartsAtTarget()
    {
        using var model = await RangeModel(); model.Focus("n:7");
        model.Interaction(true); model.Query = "name:b.txt"; await model.RebuildAsync(true);
        Assert.Equal(5, model.Rows.Length); Assert.True(model.FocusTableRange("n:9")); Assert.Equal(new ulong[] { 7, 8, 9 }, model.Selection.Marks.Order());
        model.Interaction(false); await Until(() => model.Rows.Length == 1);
        Assert.Equal("n:8", Assert.Single(model.Rows).Key); model.Selection.ClearMarks();
        Assert.True(model.FocusTableRange("n:8")); Assert.Equal(new ulong[] { 8 }, model.Selection.Marks);
        Assert.False(model.FocusTableRange("n:7")); Assert.Equal("n:8", model.Selection.Focus); Assert.Equal(new ulong[] { 8 }, model.Selection.Marks);
    }

    [AvaloniaFact]
    public async Task TableRangeDoesNotSilentlyExpandSyntheticSharedGroups()
    {
        var snapshot = Fixture(); snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Add(new(7, "r", 3, "source-alias.cs", AnalysisKind.File, "a")),
            Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LinkCount = AnalysisMeasurement.Known(2) })
        };
        using var model = new DiskSpaceViewModel(snapshot) { Sort = AnalysisTableSort.Name, SortDescending = false };
        await model.RebuildAsync(true); model.Focus("n:1", open: true); await Until(() => model.Selection.Scope == "n:1");
        var group = Assert.Single(model.Rows, r => r.Synthetic && r.Name == "Shared hard-linked files");
        model.Focus("n:3"); Assert.True(model.FocusTableRange(group.Key));
        Assert.Equal(new ulong[] { 2, 3 }, model.Selection.Marks.Order()); Assert.Contains("Accounting groups not expanded: 1", model.Status);
        Assert.Empty(model.Selection.Basket); model.MarkCurrentMembers(); Assert.Equal(new ulong[] { 2, 3, 4, 7 }, model.Selection.Marks.Order());
    }

    [AvaloniaFact]
    public async Task RangeLimitRefusalRetainsTheEntirePreviousSelectionAndFocus()
    {
        using var model = await RangeModel(); model.Selection.MarkCaptured(Enumerable.Range(100, AnalysisLimits.ReviewPaths).Select(i => (ulong)i));
        model.Focus("n:7"); var previous = model.Selection.Marks;
        Assert.False(model.FocusTableRange("n:9")); Assert.Same(previous, model.Selection.Marks); Assert.Equal("n:7", model.Selection.Focus);
        Assert.Contains("100,000", model.Status); Assert.Empty(model.Selection.Basket);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShiftArrowWithoutAVisibleFocusStartsAtTheCorrespondingBoundary(bool forward)
    {
        using var styles = new ProductStyles(); using var model = await RangeModel();
        Assert.Equal("n:2", model.Selection.Focus);
        var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Ranked analysis entries");
            Assert.Null(list.SelectedItem); list.Focus();
            var key = forward ? Key.Down : Key.Up; var physical = forward ? PhysicalKey.ArrowDown : PhysicalKey.ArrowUp;
            window.KeyPress(key, RawInputModifiers.Shift, physical, null); window.KeyRelease(key, RawInputModifiers.Shift, physical, null);
            ulong expected = forward ? 7ul : 6ul;
            Assert.Equal("n:" + expected, model.Selection.Focus); Assert.Equal(new[] { expected }, model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta)]
    public async Task ModifiedPointerAndKeyboardRangesMarkOnceAndPreserveCheckboxSpace(RawInputModifiers toggle)
    {
        using var styles = new ProductStyles(); using var model = await RangeModel();
        var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); await ShowRangeWindow(window, model); var diagnostic = TraceTableGesture(window, model);
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Ranked analysis entries");
            void Click(int index, RawInputModifiers modifiers, double x = 65)
            {
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index)); var point = RangeRowPoint(window, row, x);
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left, modifiers); window.MouseUp(point, MouseButton.Left, modifiers);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            Click(0, toggle); Assert.Equal(new ulong[] { 7 }, model.Selection.Marks);
            Click(0, toggle); Assert.Empty(model.Selection.Marks); // Same selected row must still toggle.
            Click(0, RawInputModifiers.None); Click(4, RawInputModifiers.Shift, x: 20);
            Assert.Equal(new ulong[] { 4, 6, 7, 8, 9 }, model.Selection.Marks.Order());
            Click(1, RawInputModifiers.Shift); Assert.True(new ulong[] { 7, 8 }.SequenceEqual(model.Selection.Marks.Order()), diagnostic());
            Assert.Equal("n:8", model.Selection.Focus); Assert.Equal("n:2", model.Selection.Scope); Assert.Empty(model.Selection.Basket);
            window.KeyPress(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null); window.KeyRelease(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null);
            Assert.Equal(new ulong[] { 4, 6, 7, 8, 9 }, model.Selection.Marks.Order());
            window.KeyPress(Key.Home, RawInputModifiers.Shift, PhysicalKey.Home, null); window.KeyRelease(Key.Home, RawInputModifiers.Shift, PhysicalKey.Home, null);
            Assert.Equal(new ulong[] { 7 }, model.Selection.Marks); Assert.Equal("n:7", model.Selection.Focus);
            window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
            Assert.Equal(new ulong[] { 7, 8 }, model.Selection.Marks.Order());
            window.KeyPress(Key.Up, RawInputModifiers.Shift, PhysicalKey.ArrowUp, null); window.KeyRelease(Key.Up, RawInputModifiers.Shift, PhysicalKey.ArrowUp, null);
            Assert.Equal(new ulong[] { 7 }, model.Selection.Marks);
            window.KeyPress(Key.PageDown, RawInputModifiers.Shift, PhysicalKey.PageDown, null); window.KeyRelease(Key.PageDown, RawInputModifiers.Shift, PhysicalKey.PageDown, null);
            Assert.Equal(new ulong[] { 4, 6, 7, 8, 9 }, model.Selection.Marks.Order());
            window.KeyPress(Key.PageUp, RawInputModifiers.Shift, PhysicalKey.PageUp, null); window.KeyRelease(Key.PageUp, RawInputModifiers.Shift, PhysicalKey.PageUp, null);
            Assert.Equal(new ulong[] { 7 }, model.Selection.Marks);
            Click(0, RawInputModifiers.None, x: 20); Assert.Empty(model.Selection.Marks);
            Assert.IsType<CheckBox>(window.FocusManager!.GetFocusedElement());
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.Equal(new ulong[] { 7 }, model.Selection.Marks);
            var heldRow = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(2)); var heldPoint = RangeRowPoint(window, heldRow);
            window.MouseMove(heldPoint); window.MouseDown(heldPoint, MouseButton.Left, RawInputModifiers.Shift);
            model.Query = "name:b.txt"; await model.RebuildAsync(true); Assert.Equal(5, model.Rows.Length);
            window.MouseUp(RangeRowPoint(window, heldRow), MouseButton.Left, RawInputModifiers.Shift); await Until(() => model.Rows.Length == 1, diagnostic);
            Assert.Equal(new ulong[] { 7, 8, 9 }, model.Selection.Marks.Order()); Assert.Empty(model.Selection.Basket);
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task TableReleaseOutsideTheMovedListRetiresItsCapturedPublicationFreeze()
    {
        using var styles = new ProductStyles(); using var model = await RangeModel();
        var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); await ShowRangeWindow(window, model);
        try
        {
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetName(control) == "Ranked analysis entries");
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var point = RangeRowPoint(window, row);
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            model.Query = "name:b.txt"; await model.RebuildAsync(true); Assert.Equal(5, model.Rows.Length);
            list.RenderTransform = new Avalonia.Media.TranslateTransform(0, 300); Frame(window);
            window.MouseUp(new Point(window.ClientSize.Width - 10, 10), MouseButton.Left, RawInputModifiers.Shift);
            await Until(() => model.Rows.Length == 1);
            Assert.Equal(new ulong[] { 7 }, model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        }
        finally { window.ForceClose(); }
    }

    [AvaloniaFact]
    public async Task HandledRowReleaseRetiresTheTableFreezeAfterItsCapturedAction()
    {
        using var styles = new ProductStyles(); using var model = await RangeModel();
        var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); await ShowRangeWindow(window, model); var diagnostic = TraceTableGesture(window, model);
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetName(control) == "Ranked analysis entries");
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            bool handled = false;
            row.AddHandler(InputElement.PointerReleasedEvent, (_, args) => { handled = true; args.Handled = true; }, RoutingStrategies.Bubble, handledEventsToo: true);
            var point = RangeRowPoint(window, row);
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left);
            model.Query = "name:b.txt"; await model.RebuildAsync(true); Assert.Equal(5, model.Rows.Length);
            window.MouseUp(RangeRowPoint(window, row), MouseButton.Left); Assert.True(handled, diagnostic()); await Until(() => model.Rows.Length == 1, diagnostic);
            Assert.Empty(model.Selection.Marks); Assert.Empty(model.Selection.Basket);
        }
        finally { window.ForceClose(); }
    }
}
