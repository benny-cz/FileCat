using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

public sealed class PanelRetirementLifetimeTests(ITestOutputHelper output)
{
    public static TheoryData<bool, bool, int> Cases => new()
    {
        {false, false, 1}, {false, true, 1}, {true, false, 1}, {true, true, 1},
        {false, false, 4}, {false, true, 4}, {true, false, 4}, {true, true, 4},
        {false, false, 12}, {false, true, 12}, {true, false, 12}, {true, true, 12},
    };
    private sealed record Observation(bool SourceDetached, bool ReaderRetired, bool RequestRetired, bool OutsideWindow,
        bool ReaderClosed, string? ReaderCloseError, bool SourceStillOwned, bool SourceTabStillOwned, bool RemovedContextCleared);

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Removing_a_quick_view_panel_retires_its_source_subscription_and_reader(bool hiddenFirst, bool binary, int repetitions)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var observations = new List<Observation>();
        var retiredViews = new List<PanelView>();
        try
        {
            var source = vm.Workspace.Panels[0];
            var sourceTab = source.ActiveTab!;
            string name = binary ? "owned.bin" : "a.txt";
            if (binary) File.WriteAllBytes(Path.Combine(root, "files", name), Enumerable.Range(0, 65537).Select(i => (byte)(i * 31)).ToArray());
            sourceTab.Refresh();
            await Until(() => sourceTab.Listing.State == ListingState.Complete && sourceTab.Listing.FocusName(name));
            byte[] before = File.ReadAllBytes(Path.Combine(root, "files", name));
            for (int i = 0; i < repetitions; i++)
            {
                var panel = vm.Workspace.AddPanel(sourceTab.Location!);
                Layout(window);
                var retired = Assert.Single(window.GetVisualDescendants().OfType<PanelView>(), v => ReferenceEquals(v.DataContext, panel));
                retiredViews.Add(retired);
                panel.QuickViewSource = source;
                var pane = retired.FindControl<QuickViewPane>("QuickView")!;
                await Load(pane);
                Assert.Same(sourceTab, Field(pane, "_source"));
                var reader = Assert.IsType<PagedReader>(Field(pane, "_reader"));
                byte[] read = new byte[before.Length];
                Assert.Equal(read.Length, reader.Read(0, read));
                Assert.Equal(before, read);
                reader.WithSource(_ => { }); // positively open before retirement
                if (hiddenFirst)
                {
                    vm.Workspace.Activate(source);
                    vm.Workspace.ToggleMaximize();
                    Layout(window);
                    Assert.Null(TopLevel.GetTopLevel(retired));
                }
                vm.Workspace.RemovePanel(panel);
                Layout(window);
                Exception? closeError = Record.Exception(() => reader.WithSource(_ => { }));
                observations.Add(new(Field(pane, "_source") is null, Field(pane, "_reader") is null, Field(pane, "_request") is null,
                    TopLevel.GetTopLevel(retired) is null, closeError is ObjectDisposedException, closeError?.GetType().FullName,
                    vm.Workspace.Panels.Contains(source), source.Tabs.Contains(sourceTab), retired.DataContext is null));
                // Retain the control deliberately, separating resource closure from garbage collection.
                // Clean the unchanged baseline after recording it so repeated controls remain bounded.
                retired.DataContext = null;
                await Until(() => Record.Exception(() => reader.WithSource(_ => { })) is ObjectDisposedException);
                Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "files", name)));
            }
            output.WriteLine("PANEL_RETIREMENT " + JsonSerializer.Serialize(new
            {
                hiddenFirst, binary, repetitions, observations, exactBytes = before.Length,
                expectedSHA256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(before)),
                survivingPanels = vm.Workspace.Panels.Count, actualFullHeadlessWindowAndOwnedFiles = true,
                controlsDeliberatelyHeldDuringClosureObservation = true, nativeDesktopInput = false,
            }));
            Assert.Equal(2, vm.Workspace.Panels.Count);
            Assert.All(observations, v => Assert.True(v.OutsideWindow && v.SourceStillOwned && v.SourceTabStillOwned));
            Assert.All(observations, v => Assert.True(v.SourceDetached && v.ReaderRetired && v.RequestRetired && v.ReaderClosed && v.RemovedContextCleared,
                "A removed quick-view panel must retire bindings and preview ownership even while its control is held."));
        }
        finally
        {
            foreach (var view in retiredViews) view.DataContext = null;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Maximize_and_restore_reuses_a_live_quick_view_panel(bool activatePreview)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var source = vm.Workspace.Panels[0];
            var sourceTab = source.ActiveTab!;
            await Until(() => sourceTab.Listing.State == ListingState.Complete && sourceTab.Listing.FocusName("a.txt"));
            var panel = vm.Workspace.Panels[1];
            var view = Assert.Single(window.GetVisualDescendants().OfType<PanelView>(), v => ReferenceEquals(v.DataContext, panel));
            panel.QuickViewSource = source;
            var pane = view.FindControl<QuickViewPane>("QuickView")!;
            await Load(pane);
            var reader = Assert.IsType<PagedReader>(Field(pane, "_reader"));
            vm.Workspace.Activate(activatePreview ? panel : source);
            vm.Workspace.ToggleMaximize(); Layout(window);
            vm.Workspace.ToggleMaximize(); Layout(window);
            Assert.Same(view, Assert.Single(window.GetVisualDescendants().OfType<PanelView>(), v => ReferenceEquals(v.DataContext, panel)));
            Assert.Same(panel, view.DataContext); Assert.Same(sourceTab, Field(pane, "_source")); Assert.Same(reader, Field(pane, "_reader"));
            reader.WithSource(_ => { });
            panel.QuickViewSource = null;
            Assert.Null(Field(pane, "_reader"));
            Assert.IsType<ObjectDisposedException>(Record.Exception(() => reader.WithSource(_ => { })));
            output.WriteLine("PANEL_REUSE " + JsonSerializer.Serialize(new { activatePreview, sameControlAndReaderAcrossLayout = true, retiredAfterExplicitToggle = true, nativeDesktopInput = false }));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static async Task Load(QuickViewPane pane)
    {
        ((DispatcherTimer)Field(pane, "_debounce")!).Stop();
        await (Task)typeof(QuickViewPane).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pane, null)!;
        // The debounce may already have started the same-key request. A second LoadAsync then returns before
        // that request publishes its reader; observe the positive checkpoint before testing retirement.
        await Until(() => Field(pane, "_reader") is PagedReader);
    }
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(ready(), "The owned panel-retirement fixture did not reach its positive checkpoint.");
    }
}
