using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class PlaceBarRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<bool, bool, int> Cases => new()
    {
        { false, false, 16 }, { false, true, 16 }, { true, false, 16 }, { true, true, 16 },
        { false, false, 64 }, { false, true, 64 }, { true, false, 64 }, { true, true, 64 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Removed_panels_release_place_controls_and_borrowed_icons(bool hiddenFirst, bool queuedUpdate, int bookmarks)
    {
        var fixture = Open(bookmarks);
        try
        {
            await Ready(fixture.Vm);
            var observation = Remove(fixture, hiddenFirst, queuedUpdate, bookmarks);
            await CollectUntil(() => observation.Bitmaps.All(r => !Alive(r)));
            var survivingView = View(fixture.Window, fixture.Vm.Workspace.Panels[0]);
            VerifyBorrowers(survivingView, bookmarks);
            fixture.Native.Publish(); Layout(fixture.Window);
            VerifyBorrowers(survivingView, bookmarks);
            int controlsAlive = observation.Controls.Count(Alive), bitmapsAlive = observation.Bitmaps.Count(Alive);
            int retainedChildren = Bar(observation.View).Children.Count;
            int retainedPayloads = RetainedPayloads(observation.Controls);
            output.WriteLine("PLACE_BAR_REMOVAL " + JsonSerializer.Serialize(new
            {
                hiddenFirst, queuedUpdate, bookmarks, controlsAlive, bitmapsAlive, retainedChildren, retainedPayloads,
                observedControls = observation.Controls.Count, observedBitmaps = observation.Bitmaps.Count,
                retiredLogicalPixelBytes = (long)bitmapsAlive * 64 * 64 * 4,
                contextCleared = observation.View.DataContext is null,
                outsideWindow = TopLevel.GetTopLevel(observation.View) is null,
                remainingPanels = fixture.Vm.Workspace.Panels.Count,
                survivingBorrowerExtentsCheckedBeforeAndAfterPublication = true, pixelBytesVerified = PixelBytesAvailable, renderer = Renderer,
                retiredViewDeliberatelyHeld = true, headlessWithoutNativeDesktopInput = true,
            }));
            Assert.Null(observation.View.DataContext);
            Assert.Null(TopLevel.GetTopLevel(observation.View));
            Assert.Equal(2, fixture.Vm.Workspace.Panels.Count);
            Assert.Equal(0, retainedChildren);
            Assert.Equal(0, retainedPayloads); // Retired compositor controls may remain, but no longer own Place/icon payloads.
            Assert.Equal(0, bitmapsAlive);
            GC.KeepAlive(observation.View);
        }
        finally { AccessibilityTests.Close(fixture.Services, fixture.Window, fixture.Root); }
    }

    [AvaloniaTheory]
    [InlineData(false, 16)]
    [InlineData(true, 16)]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    public async Task Hidden_live_panels_keep_places_and_rebuild_them_after_restore(bool publishWhileHidden, int bookmarks)
    {
        var fixture = Open(bookmarks);
        try
        {
            await Ready(fixture.Vm);
            var panel = fixture.Vm.Workspace.Panels[1];
            var view = View(fixture.Window, panel);
            var before = Borrowers(view, bookmarks);
            fixture.Vm.Workspace.Activate(fixture.Vm.Workspace.Panels[0]);
            fixture.Vm.Workspace.ToggleMaximize(); Layout(fixture.Window);
            Assert.Null(TopLevel.GetTopLevel(view));
            VerifyBorrowers(view, bookmarks);
            if (publishWhileHidden) { fixture.Native.Publish(); Layout(fixture.Window); }
            Assert.Same(panel, view.DataContext);
            Assert.Equal(before, Borrowers(view, bookmarks));
            fixture.Vm.Workspace.ToggleMaximize(); Layout(fixture.Window);
            Assert.Same(view, View(fixture.Window, panel));
            VerifyBorrowers(view, bookmarks);
            Assert.NotSame(before[0], Borrowers(view, bookmarks)[0]); // Attach rebuilds the live Places bar.
            output.WriteLine("PLACE_BAR_REUSE " + JsonSerializer.Serialize(new { publishWhileHidden, bookmarks, hiddenBorrowersPreserved = true, sameViewRestored = true, pixelBytesVerified = PixelBytesAvailable, renderer = Renderer, headlessWithoutNativeDesktopInput = true }));
            GC.KeepAlive(before);
        }
        finally { AccessibilityTests.Close(fixture.Services, fixture.Window, fixture.Root); }
    }

    [AvaloniaTheory]
    [InlineData(false, 16)]
    [InlineData(true, 16)]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    public async Task Cleared_context_cannot_be_repopulated_by_icon_updates_and_can_be_rebound(bool queuedUpdate, int bookmarks)
    {
        var fixture = Open(bookmarks);
        try
        {
            await Ready(fixture.Vm);
            var panel = fixture.Vm.Workspace.Panels[1];
            var view = View(fixture.Window, panel);
            VerifyBorrowers(view, bookmarks);
            if (queuedUpdate) fixture.Native.Publish();
            view.DataContext = null; Layout(fixture.Window);
            fixture.Native.Publish(); Layout(fixture.Window);
            int clearedChildren = Bar(view).Children.Count;
            view.DataContext = panel; Layout(fixture.Window);
            VerifyBorrowers(view, bookmarks);
            output.WriteLine("PLACE_BAR_CONTEXT " + JsonSerializer.Serialize(new { queuedUpdate, bookmarks, clearedChildren, sameViewRebound = true, pixelBytesVerified = PixelBytesAvailable, renderer = Renderer, headlessWithoutNativeDesktopInput = true }));
            Assert.Equal(0, clearedChildren);
        }
        finally { AccessibilityTests.Close(fixture.Services, fixture.Window, fixture.Root); }
    }

    [AvaloniaTheory]
    [InlineData(16)]
    [InlineData(64)]
    public async Task Rebound_place_button_navigates_the_live_panel(int bookmarks)
    {
        var fixture = Open(bookmarks);
        try
        {
            await Ready(fixture.Vm);
            var panel = fixture.Vm.Workspace.Panels[1];
            var view = View(fixture.Window, panel);
            view.DataContext = null; Layout(fixture.Window);
            view.DataContext = panel; Layout(fixture.Window);
            string destination = Directory.CreateDirectory(Path.Combine(fixture.Root, "owned-folder-0000")).FullName;
            Buttons(view)[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Ready(fixture.Vm);
            Assert.Equal(destination, panel.ActiveTab!.Location!.Path);
            Assert.Same(panel, fixture.Vm.Workspace.ActivePanel);
            output.WriteLine("PLACE_BAR_NAVIGATION " + JsonSerializer.Serialize(new { bookmarks, reboundButtonNavigates = true, samePanelActivated = true, headlessWithoutNativeDesktopInput = true }));
        }
        finally { AccessibilityTests.Close(fixture.Services, fixture.Window, fixture.Root); }
    }

    private const string Renderer = "Default Headless; actual pixels qualified separately in native observer";
    private const bool PixelBytesAvailable = false;
    private sealed record Fixture(AppServices Services, MainViewModel Vm, MainWindow Window, string Root, NativeIcons Native);
    private sealed record Observation(PanelView View, List<WeakReference<Button>> Controls, List<WeakReference<IImage>> Bitmaps);

    private static Fixture Open(int bookmarks)
    {
        string root = Directory.CreateTempSubdirectory("filecat-place-retirement-").FullName;
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var native = new NativeIcons(); services.Icons.Native = native;
        for (int i = 0; i < bookmarks; i++) services.History.Bookmarks.Add(new BookmarkEntry { Name = $"owned-place-{i:D4}", Location = Location.FileSystem(Path.Combine(root, $"owned-folder-{i:D4}")) });
        var vm = new MainViewModel(services);
        var window = new MainWindow(vm, null) { Width = 1200, Height = 800 };
        vm.Initialize(null);
        foreach (var panel in vm.Workspace.Panels) panel.ActiveTab!.Navigate(Location.FileSystem(root));
        window.Show(); Layout(window);
        return new(services, vm, window, root, native);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Observation Remove(Fixture fixture, bool hiddenFirst, bool queuedUpdate, int bookmarks)
    {
        var panel = fixture.Vm.Workspace.AddPanel(Location.FileSystem(fixture.Root)); Layout(fixture.Window);
        var view = View(fixture.Window, panel);
        VerifyBorrowers(view, bookmarks);
        var controls = Buttons(view).Select(c => new WeakReference<Button>(c)).ToList();
        var bitmaps = Borrowers(view, bookmarks).Select(c => new WeakReference<IImage>(c)).ToList();
        Assert.All(controls, r => Assert.True(Alive(r)));
        Assert.All(bitmaps, r => Assert.True(Alive(r)));
        if (hiddenFirst)
        {
            fixture.Vm.Workspace.Activate(fixture.Vm.Workspace.Panels[0]);
            fixture.Vm.Workspace.ToggleMaximize(); Layout(fixture.Window);
        }
        if (queuedUpdate) fixture.Native.Publish(); // The deferred callback has not run when the panel retires.
        fixture.Vm.Workspace.RemovePanel(panel); Layout(fixture.Window);
        return new(view, controls, bitmaps);
    }

    private static PanelView View(MainWindow window, PanelViewModel panel) => Assert.Single(window.GetVisualDescendants().OfType<PanelView>(), v => ReferenceEquals(v.DataContext, panel));
    private static PlaceBarPanel Bar(PanelView view) => view.FindControl<PlaceBarPanel>("DriveButtonsPanel")!;
    private static Button[] Buttons(PanelView view) => Bar(view).Children.OfType<Button>().Where(b => b.Tag is Place p && p.Title.Contains("owned-place-", StringComparison.Ordinal)).ToArray();
    private static IImage[] Borrowers(PanelView view, int bookmarks)
    {
        var buttons = Buttons(view); Assert.Equal(bookmarks, buttons.Length);
        return buttons.Select(b => Assert.Single(b.GetVisualDescendants().OfType<Image>()).Source!).ToArray();
    }
    private static void VerifyBorrowers(PanelView view, int bookmarks)
    {
        foreach (var image in Borrowers(view, bookmarks))
        {
            var bitmap = Assert.IsType<WriteableBitmap>(image);
            Assert.Equal(new PixelSize(64, 64), bitmap.PixelSize);

        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RetainedPayloads(List<WeakReference<Button>> references) => references.Count(r => r.TryGetTarget(out var button) && (button.Content is not null || button.Tag is not null || ToolTip.GetTip(button) is not null));
    private static async Task Ready(MainViewModel vm)
    {
        for (int i = 0; i < 500 && vm.Workspace.Panels.Any(p => p.ActiveTab!.Listing.State != FileCat.Core.Listing.ListingState.Complete); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.All(vm.Workspace.Panels, p => Assert.Equal(FileCat.Core.Listing.ListingState.Complete, p.ActiveTab!.Listing.State));
    }
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    [MethodImpl(MethodImplOptions.NoInlining)] private static bool Alive<T>(WeakReference<T> r) where T : class => r.TryGetTarget(out _);
    private static async Task CollectUntil(Func<bool> ready)
    {
        for (int i = 0; i < 20 && !ready(); i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
    private sealed class NativeIcons : INativeIconSource
    {
        public event Action? IconsLoaded;
        public IImage GetIcon(in EntryData entry, Location? folder = null)
        {
            return new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96));
        }
        public void Publish() => IconsLoaded?.Invoke();
    }
}
