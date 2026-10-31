using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class OperationsDrawerLayoutTests
{
    [AvaloniaFact]
    public async Task Open_drawer_follows_newest_work_and_scrolls_history_instead_of_occupying_the_workspace()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 800);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            Layout(window);
            var folder = Directory.CreateDirectory(Path.Combine(root, "operations-target")).FullName;
            Job Submit(int number) => services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.CreateDirectory,
                Destination = Location.FileSystem(folder),
                NewName = $"folder-{number}",
            });

            var first = Submit(0);
            for (int i = 0; i < 300 && !(first.State.IsFinished() && center.Jobs.Count == 1); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Layout(window);
            Assert.Same(first, center.Selected?.Job);

            var newer = Enumerable.Range(1, 5).Select(Submit).ToArray();
            for (int i = 0; i < 300 && !(newer.All(job => job.State.IsFinished()) && center.Jobs.Count == 6); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Layout(window);
            Assert.Equal(6, center.Jobs.Count);
            Assert.Same(newer[^1], center.Selected?.Job);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            var history = view.FindControl<ListBox>("HistoryList")!;
            var scroll = history.GetVisualDescendants().OfType<ScrollViewer>().First();
            var details = view.FindControl<ScrollViewer>("DetailsScroll")!;
            Assert.True(details.IsEffectivelyVisible);
            Assert.True(drawer.Bounds.Height <= 324, $"Six short jobs consumed {drawer.Bounds.Height} DIP.");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1,
                $"History must scroll: extent {scroll.Extent.Height}, viewport {scroll.Viewport.Height} DIP.");
            Assert.Contains(scroll.GetVisualDescendants().OfType<ScrollBar>(), bar =>
                bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.IsEffectivelyVisible);
            history.ScrollIntoView(5);
            Layout(window);
            Assert.True(history.ContainerFromIndex(5)?.TranslatePoint(new Point(0, 0), history) is { } position &&
                        position.Y < history.Bounds.Height,
                "The oldest retained operation must be reachable by scrolling.");

            center.Selected = center.Jobs.Single(row => row.Job == first);
            var last = Submit(6);
            for (int i = 0; i < 300 && !(last.State.IsFinished() && center.Jobs.Count == 7); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Layout(window);
            Assert.Same(first, center.Selected?.Job); // A deliberate older selection stays put.

            services.Jobs.Remove(first);
            for (int i = 0; i < 300 && center.Jobs.Count != 6; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Layout(window);
            Assert.Same(last, center.Selected?.Job);
            Assert.Same(center.Selected, history.SelectedItem);
            Assert.True(details.IsEffectivelyVisible);
            center.Selected = center.Jobs.Single(row => row.Job == newer[0]);
            center.IsOpen = false;
            center.IsOpen = true;
            Layout(window);
            Assert.Same(last, center.Selected?.Job);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void Recovery_section_stays_bounded_and_last_item_actions_are_scrollable(int count)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            for (int i = 0; i < count; i++)
                center.Interrupted.Add(new InterruptedJobViewModel(new InterruptedJob(
                    Path.Combine(root, $"journal-{i}"), nameof(JobKind.Copy), $"Interrupted {i}", DateTime.UtcNow,
                    [Path.Combine(root, $"source-{i}")], Path.Combine(root, "target"), [], [], 0, 1)));
            Layout(window);

            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            var recovery = view.FindControl<ScrollViewer>("RecoveryScroll")!;
            var expander = view.GetVisualDescendants().OfType<Expander>()
                .Single(e => AutomationProperties.GetName(e) == "Interrupted operations needing review");
            expander.IsExpanded = true;
            Layout(window);

            Assert.Equal(count, center.InterruptedCount);
            Assert.True(center.HasInterrupted);
            Assert.True(recovery.Bounds.Height <= drawer.Bounds.Height * .34 + 2);
            Assert.True(vm.Operations.SelectedInterrupted is not null);
            var selector = view.GetVisualDescendants().OfType<ListBox>()
                .Single(e => AutomationProperties.GetName(e) == "Choose an interrupted operation");
            Assert.Equal(count, selector.ItemCount);
            selector.ScrollIntoView(0);
            Layout(window);
            Assert.True(selector.ContainerFromIndex(0)!.Focus(), "The interrupted item must accept keyboard focus.");
            for (int i = 0; i < count; i++)
            {
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            }
            Layout(window);
            Assert.Same(center.Interrupted[^1], center.SelectedInterrupted);

            recovery.Offset = new Vector(0, recovery.Extent.Height);
            Layout(window);
            string[] actions = ["Clean up partial files", "Review report", "Mark reviewed…", "Hide for now"];
            foreach (string action in actions)
                Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.IsVisible && b.Content as string == action);
            Assert.True(recovery.Extent.Height <= 1000, "Only the selected interrupted item should render its action details.");
            center.Interrupted.RemoveAt(count - 1);
            Layout(window);
            Assert.Equal(count - 1, center.InterruptedCount);
            Assert.Equal(count > 1, center.HasInterrupted);
            Assert.Same(center.Interrupted.FirstOrDefault(), center.SelectedInterrupted);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(640, 420)]
    [InlineData(980, 600)]
    [InlineData(1200, 800)]
    public void Drawer_respects_client_height_and_keyboard_resize_is_persisted(int width, int height)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: width, height: height);
        try
        {
            vm.Operations.IsOpen = true;
            Layout(window);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            var handle = view.FindControl<Border>("DrawerResizeHandle")!;
            Assert.InRange(drawer.Bounds.Height, 140, 720);
            Assert.True(window.WorkspaceViewport!.Value.Height >= 90,
                $"The workspace is only {window.WorkspaceViewport.Value.Height} DIP tall; drawer {drawer.Bounds.Height} DIP, client {window.ClientSize.Height} DIP.");
            double before = drawer.Bounds.Height;
            if (height > 420)
            {
                handle.Focus();
                bool atMinimum = before < 156;
                var key = atMinimum ? Key.Up : Key.Down;
                var physical = atMinimum ? PhysicalKey.ArrowUp : PhysicalKey.ArrowDown;
                window.KeyPress(key, RawInputModifiers.None, physical, null);
                window.KeyRelease(key, RawInputModifiers.None, physical, null);
                Layout(window);
                Assert.True(atMinimum ? drawer.Bounds.Height >= before + 15 : drawer.Bounds.Height <= before - 15);
                Assert.Equal(drawer.Bounds.Height, services.Settings.OperationsDrawerHeight, 1);
                var saved = JsonFileStore.Load(services.Paths.SettingsFile, StateJsonContext.Default.AppSettings,
                    AppSettings.CurrentSchema, () => new AppSettings(), out var status);
                Assert.Equal(StateLoadStatus.Loaded, status);
                Assert.Equal(drawer.Bounds.Height, saved.OperationsDrawerHeight, 1);
            }
            Assert.True(window.WorkspaceViewport!.Value.Height >= 90);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Completed_history_fits_its_content_and_grows_until_the_user_resizes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 800);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            var first = Finished(0);
            center.Jobs.Add(first);
            center.Selected = first;
            Layout(window);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            double one = drawer.Bounds.Height;
            Assert.InRange(one, 140, 230);

            for (int i = 1; i < 6; i++) center.Jobs.Add(Finished(i));
            Layout(window);
            double several = drawer.Bounds.Height;
            Assert.InRange(several, one, one + 90);
            var history = view.FindControl<ListBox>("HistoryList")!;
            var historyScroll = history.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(historyScroll.Extent.Height > historyScroll.Viewport.Height + 1,
                $"Six history rows should scroll in a compact drawer: one {one} DIP, six {several} DIP.");
            Assert.True(window.WorkspaceViewport!.Value.Height >= 90);

            var handle = view.FindControl<Border>("DrawerResizeHandle")!;
            handle.Focus();
            window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
            window.KeyRelease(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
            Layout(window);
            double chosen = drawer.Bounds.Height;
            Assert.True(chosen >= several + 63);
            center.Jobs.RemoveAt(5);
            Layout(window);
            Assert.Equal(chosen, drawer.Bounds.Height, 1);
            Assert.Equal(chosen, services.Settings.OperationsDrawerHeight, 1);

            var fit = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Fit height");
            fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (center.Jobs.Count > 1) center.Jobs.RemoveAt(center.Jobs.Count - 1);
            Layout(window);
            Assert.InRange(drawer.Bounds.Height, 140, 230);
            Assert.Equal(300, services.Settings.OperationsDrawerHeight);
        }
        finally { AccessibilityTests.Close(services, window, root); }

        static JobViewModel Finished(int index)
        {
            var job = new Job(new JobRequest { Kind = JobKind.Delete }, $"Deleted item {index}", "local", [], []);
            job.SetState(JobState.Completed);
            var view = new JobViewModel(job);
            view.Refresh();
            return view;
        }
    }

    [AvaloniaFact]
    public void Mouse_drag_resizes_drawer_and_keeps_the_workspace_usable()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 800);
        try
        {
            vm.Operations.IsOpen = true;
            Layout(window);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            var handle = view.FindControl<Border>("DrawerResizeHandle")!;
            double before = drawer.Bounds.Height;
            window.CaptureRenderedFrame()?.Dispose();
            var point = handle.TranslatePoint(new Point(32, handle.Bounds.Height / 2), window)!.Value;
            Assert.True(((Visual)window.InputHitTest(point)!).GetSelfAndVisualAncestors().Contains(handle),
                $"Resize handle bounds {handle.Bounds}, visible {handle.IsEffectivelyVisible}, background {handle.Background}, point {point}; pointer chain " +
                string.Join(" > ", ((Visual)window.InputHitTest(point)!).GetSelfAndVisualAncestors().Select(v =>
                    v.GetType().FullName + ":" + (v as Control)?.Name + ":" + v.Bounds + ":hit=" + (v as InputElement)?.IsHitTestVisible)) +
                "; handle chain " + string.Join(" > ", handle.GetSelfAndVisualAncestors().Select(v =>
                    v.GetType().Name + ":" + (v as Control)?.Name + ":" + v.Bounds)));
            window.MouseDown(point, MouseButton.Left);
            for (int i = 1; i <= 8; i++)
                window.MouseMove(new Point(point.X, point.Y - i * 12), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(point.X, point.Y - 96), MouseButton.Left);
            Layout(window);
            Assert.True(drawer.Bounds.Height > before + 80,
                $"Drag changed drawer from {before} to {drawer.Bounds.Height} DIP; workspace {window.WorkspaceViewport?.Height} DIP.");
            Assert.Equal(drawer.Bounds.Height, services.Settings.OperationsDrawerHeight, 1);
            Assert.True(window.WorkspaceViewport!.Value.Height >= 90);
            double preferred = drawer.Bounds.Height;
            window.Height = 420;
            Layout(window);
            Assert.True(window.WorkspaceViewport!.Value.Height >= 90);
            Assert.True(drawer.Bounds.Height < preferred);
            window.Height = 800;
            Layout(window);
            Assert.Equal(preferred, drawer.Bounds.Height, 1);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Down_key_shrinks_the_drawer_from_its_clamped_visible_height()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1200, height: 900);
        try
        {
            vm.Operations.IsOpen = true;
            Layout(window);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            var handle = view.FindControl<Border>("DrawerResizeHandle")!;
            handle.Focus();
            window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            window.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
            Layout(window);
            double preferred = drawer.Bounds.Height;
            window.Height = 700;
            Layout(window);
            double clamped = drawer.Bounds.Height;
            Assert.True(preferred > clamped + 16);

            handle.Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Layout(window);
            Assert.InRange(clamped - drawer.Bounds.Height, 15, 17);
            Assert.Equal(drawer.Bounds.Height, services.Settings.OperationsDrawerHeight, 1);
            Assert.True(window.WorkspaceViewport!.Value.Height >= 90);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void Busy_recovery_reveals_stop_inside_the_bounded_drawer_at_small_size()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            var item = new InterruptedJobViewModel(new InterruptedJob(
                Path.Combine(root, "journal"), nameof(JobKind.Copy), "Interrupted copy", DateTime.UtcNow,
                [Path.Combine(root, "source")], Path.Combine(root, "target"), [], [], 0, 1));
            center.Interrupted.Add(item);
            Layout(window);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var recovery = view.FindControl<ScrollViewer>("RecoveryScroll")!;
            item.SetActionPhase(RecoveryActionPhase.Reviewing, "Reviewing recovery sources…");
            Layout(window);
            Assert.True(recovery.Extent.Height > recovery.Viewport.Height + 1,
                "The fixture must exercise a clipped recovery card.");

            view.RevealRecoveryReview(item);
            Layout(window);
            var stop = view.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == "Stop review" && b.IsVisible);
            var top = stop.TranslatePoint(new Point(0, 0), recovery)!.Value.Y;
            var bottom = stop.TranslatePoint(new Point(0, stop.Bounds.Height), recovery)!.Value.Y;
            double visibleHeight = Math.Min(bottom, recovery.Bounds.Height) - Math.Max(top, 0);
            Assert.True(visibleHeight >= 24 && visibleHeight >= stop.Bounds.Height * .6,
                $"Stop must have a reachable pointer target: visible {visibleHeight} of {stop.Bounds.Height} DIP in {recovery.Bounds.Height} DIP viewport.");
            Assert.InRange((top + bottom) / 2, 0, recovery.Bounds.Height);
            var status = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == "Reviewing recovery sources…" && t.IsVisible);
            var statusTop = status.TranslatePoint(new Point(0, 0), recovery)!.Value.Y;
            var statusBottom = status.TranslatePoint(new Point(0, status.Bounds.Height), recovery)!.Value.Y;
            Assert.True(statusTop < recovery.Bounds.Height && statusBottom > 0,
                $"Reviewing status must share Stop's visible recovery row: {statusTop}..{statusBottom} in {recovery.Bounds.Height} DIP.");
            Assert.Equal(RecoveryActionPhase.Reviewing, item.ActionPhase);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stop_stays_in_view_when_the_recovery_area_changes_after_a_review_starts(bool revealedByContinue)
    {
        // Hosted macOS once found Stop 15 DIP short of the bounded recovery viewport (14 of 29 DIP visible): the reveal
        // scrolled to the end once, and the area changed after it. Only Continue revealed Stop at all, and it scrolled to
        // the end of the area, which the reviewed reports follow.
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            var item = new InterruptedJobViewModel(new InterruptedJob(
                Path.Combine(root, "journal"), nameof(JobKind.Copy), "Interrupted copy", DateTime.UtcNow,
                [Path.Combine(root, "source")], Path.Combine(root, "target"), [], [], 0, 1));
            center.Interrupted.Add(item);
            Layout(window);
            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var recovery = view.FindControl<ScrollViewer>("RecoveryScroll")!;
            // As a cleanup or a new transfer starts its review: the phase changes, with no explicit reveal.
            item.SetActionPhase(RecoveryActionPhase.Reviewing, "Reviewing recovery sources…");
            if (revealedByContinue) view.RevealRecoveryReview(item);
            Layout(window);
            AssertStopVisible(view, recovery, "after the review started");

            // The review announces its progress above everything in the area, as each phase does.
            center.AnnounceRecovery("Reviewing staged files and source changes; nothing is changed before you approve the reviewed plan.");
            Layout(window);
            Assert.True(recovery.Extent.Height > recovery.Viewport.Height + 1, "The fixture must exercise a clipped recovery card.");
            AssertStopVisible(view, recovery, "after an announcement above it");
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static void AssertStopVisible(OperationsView view, ScrollViewer recovery, string when)
    {
        var stop = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Stop review" && b.IsVisible);
        var top = stop.TranslatePoint(new Point(0, 0), recovery)!.Value.Y;
        var bottom = stop.TranslatePoint(new Point(0, stop.Bounds.Height), recovery)!.Value.Y;
        double visibleHeight = Math.Min(bottom, recovery.Bounds.Height) - Math.Max(top, 0);
        Assert.True(visibleHeight >= stop.Bounds.Height - 1,
            $"Stop must be in view {when}: visible {visibleHeight} of {stop.Bounds.Height} DIP in {recovery.Bounds.Height} DIP viewport.");
    }

    [AvaloniaTheory]
    [InlineData(640, 420)]
    [InlineData(1200, 800)]
    [InlineData(2800, 1193)]
    public async Task Speed_history_is_a_compact_optional_disclosure_and_expanded_graph_remains_scrollable(int width, int height)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: width, height: height);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.CreateDirectory,
                Destination = Location.FileSystem(root),
                NewName = "speed-history-fixture",
            });
            for (int i = 0; i < 300 && !(job.State.IsFinished() && center.Jobs.Count > 0); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(job.State.IsFinished());
            center.Selected = center.Jobs.Single(row => row.Job == job);
            center.Selected.HasSpeedHistory = true;
            center.Selected.SpeedHistory = [new Point(1, 10), new Point(2, 0), new Point(3, 20)];
            center.Selected.GraphCaption = "Last 90 s · peak 20 items/s";
            Layout(window);

            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var drawer = view.FindControl<Border>("DetailsDrawer")!;
            var details = view.FindControl<ScrollViewer>("DetailsScroll")!;
            var disclosure = view.FindControl<Button>("SpeedHistoryDisclosure")!;
            var graphPanel = view.FindControl<StackPanel>("SpeedHistoryGraph")!;
            Assert.True(disclosure.IsEffectivelyVisible);
            Assert.False(graphPanel.IsEffectivelyVisible);
            Assert.Equal("▸ Speed history", disclosure.Content);
            Assert.Equal("Show speed history", AutomationProperties.GetName(disclosure));
            Assert.True(disclosure.Bounds.Height >= 28 && disclosure.Bounds.Height <= 36,
                $"Collapsed speed history consumed {disclosure.Bounds.Height} DIP.");
            Assert.True(disclosure.Focus(), "The disclosure must be keyboard reachable.");

            double collapsedHeight = drawer.Bounds.Height;
            disclosure.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout(window);
            Assert.Equal("▾ Speed history", disclosure.Content);
            Assert.Equal("Hide speed history", AutomationProperties.GetName(disclosure));
            var graph = graphPanel.GetVisualDescendants().OfType<SpeedGraph>().Single();
            Assert.True(graph.IsEffectivelyVisible && graph.Bounds.Width >= 140 && graph.Bounds.Height is >= 40 and <= 48);
            Assert.True(graph.Bounds.Width <= details.Viewport.Width + 1,
                $"Graph width {graph.Bounds.Width} exceeds the report viewport {details.Viewport.Width} DIP.");
            if (width >= 2800)
                Assert.InRange(graph.Bounds.Width, 800, 841);
            if (height >= 800)
                Assert.True(drawer.Bounds.Height > collapsedHeight + 20,
                    $"Drawer did not grow for the expanded graph: {collapsedHeight} to {drawer.Bounds.Height} DIP.");
            Assert.True(drawer.Bounds.Height <= window.Bounds.Height - 96,
                $"Expanded speed history consumed too much workspace: {drawer.Bounds.Height} of {window.Bounds.Height} DIP.");
            Assert.Equal(ScrollBarVisibility.Auto, details.VerticalScrollBarVisibility);
            if (details.Extent.Height > details.Viewport.Height + 1)
            {
                double graphInContent = graph.TranslatePoint(new Point(0, 0), details)!.Value.Y + details.Offset.Y;
                details.Offset = new Vector(0, Math.Clamp(graphInContent, 0, details.Extent.Height - details.Viewport.Height));
                Layout(window);
                double top = graph.TranslatePoint(new Point(0, 0), details)!.Value.Y;
                Assert.True(top >= -1 && top < details.Viewport.Height,
                    $"Expanded graph is not reachable within the report scroller: top {top}, viewport {details.Viewport.Height} DIP.");
            }
            center.Selected.HasSpeedHistory = false;
            Layout(window);
            Assert.False(disclosure.IsEffectivelyVisible);
            Assert.False(graphPanel.IsEffectivelyVisible);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task Small_window_keeps_an_operation_row_and_full_report_action(int interruptions)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            var center = vm.Operations;
            center.IsOpen = true;
            for (int i = 0; i < interruptions; i++)
                center.Interrupted.Add(new InterruptedJobViewModel(new InterruptedJob(
                    Path.Combine(root, $"journal-{i}"), nameof(JobKind.Copy), $"Interrupted {i}", DateTime.UtcNow,
                    [Path.Combine(root, $"source-{i}")], Path.Combine(root, "target"), [], [], 0, 1)));
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(Path.Combine(root, "missing.txt"), EntryKind.File)],
                Destination = Location.FileSystem(root),
            });
            for (int i = 0; i < 300 && !(job.State.IsFinished() && center.Jobs.Count > 0); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(job.State.IsFinished());
            center.Selected = center.Jobs.Single(j => j.Job == job);
            Layout(window);

            var view = window.GetVisualDescendants().OfType<OperationsView>().Single();
            var history = view.GetVisualDescendants().OfType<ListBox>()
                .Single(e => AutomationProperties.GetName(e) == "Operations history");
            var report = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Open report");
            var details = view.FindControl<ScrollViewer>("DetailsScroll")!;
            Assert.True(history.Bounds.Height >= 45, $"Operation history height was {history.Bounds.Height} DIP.");
            Assert.True(report.Bounds.Height >= 20 && report.IsEffectivelyVisible);
            Assert.True(report.TranslatePoint(new Point(0, report.Bounds.Height), view)!.Value.Y <= view.Bounds.Height + 1,
                "The report action must stay within the drawer.");
            Assert.True(details.Extent.Height > details.Viewport.Height + 1,
                $"The selected report needs scrolling: extent {details.Extent.Height}, viewport {details.Viewport.Height} DIP.");
            Assert.Equal(ScrollBarVisibility.Auto, details.VerticalScrollBarVisibility);
            Assert.Contains(details.GetVisualDescendants().OfType<ScrollBar>(), bar =>
                bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.IsEffectivelyVisible);
            var save = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save report…");
            double saveInContent = save.TranslatePoint(new Point(0, 0), details)!.Value.Y + details.Offset.Y;
            details.Offset = new Vector(0, Math.Clamp(saveInContent, 0, details.Extent.Height - details.Viewport.Height));
            Layout(window);
            var saveTop = save.TranslatePoint(new Point(0, 0), details)!.Value.Y;
            var saveBottom = save.TranslatePoint(new Point(0, save.Bounds.Height), details)!.Value.Y;
            Assert.True(saveTop >= -1 && saveBottom <= details.Viewport.Height + 1,
                $"Save report is outside the scrollable details viewport: {saveTop}..{saveBottom} in {details.Viewport.Height} DIP.");
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
