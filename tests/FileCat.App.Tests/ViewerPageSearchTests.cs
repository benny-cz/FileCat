using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerPageSearchTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task Typing_in_page_finds_after_a_pause_and_colors_only_a_known_result()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var search = Field<TextBox>(fixture.Viewer, "_search");
        search.Text = "needle";
        Assert.DoesNotContain("searchFound", search.Classes);
        await WaitFor(() => fixture.Engine.Requests.Count == 1);
        Assert.True(fixture.Engine.WrapRequests[0]);
        fixture.Engine.Requests[0].Done.SetResult(new PageFindResult(true));
        await WaitFor(() => search.Classes.Contains("searchFound"));
        search.Text = "absent";
        Assert.DoesNotContain("searchFound", search.Classes);
        await WaitFor(() => fixture.Engine.Requests.Count == 2);
        Assert.True(fixture.Engine.WrapRequests[1]);
        fixture.Engine.Requests[1].Done.SetResult(new PageFindResult(false));
        await WaitFor(() => search.Classes.Contains("searchMissing"));
    }

    [AvaloniaFact]
    public async Task Page_find_controls_wrap_forward_and_backward_then_stop()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var engine = fixture.Engine;
        Assert.Equal("Mode: Page", viewer.GetVisualDescendants().OfType<TextBlock>().Single(t =>
            AutomationProperties.GetName(t) == "Current viewer mode").Text);
        var search = viewer.GetVisualDescendants().OfType<TextBox>()
            .Single(c => AutomationProperties.GetName(c) == "Find in file");
        Assert.False(search.IsEffectivelyVisible, "Find stays compact until requested");
        Assert.False(Field<CheckBox>(viewer, "_hexSearch").IsVisible);
        var page = Field<PageView>(viewer, "_pageView");
        Assert.True(page.KeyRequested!(Key.F, KeyModifiers.Control));
        Dispatcher.UIThread.RunJobs();
        Assert.True(search.IsEffectivelyVisible);
        Assert.True(search.IsFocused);
        search.Text = "needle";
        Dispatcher.UIThread.RunJobs();
        viewer.GetVisualDescendants().OfType<Button>().Single(b =>
            AutomationProperties.GetName(b) == "Close viewer Find bar")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(search.IsEffectivelyVisible);
        Assert.Equal("needle", search.Text);
        Assert.True(page.KeyRequested(Key.F, KeyModifiers.Control));
        Dispatcher.UIThread.RunJobs();
        Assert.True(search.IsEffectivelyVisible);
        Assert.True(search.IsFocused);
        var next = Field<Button>(viewer, "_nextMatch");
        var previousButton = Field<Button>(viewer, "_previousMatch");
        var matchCount = Field<TextBlock>(viewer, "_matchCount");
        Assert.True(next.IsVisible);
        Assert.True(previousButton.IsVisible);
        Assert.True(next.IsEnabled);
        Assert.Equal("Press Next or Previous to find", matchCount.Text);

        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => engine.Requests.Count == 1);
        Assert.Equal(("needle", false, true), engine.Requests[0].Query);
        Assert.False(engine.WrapRequests[0]);
        engine.Requests[0].Done.SetResult(new PageFindResult(true, 2, true));
        await WaitFor(() => matchCount.Text == "2 matches");
        Assert.Contains("counted all matches", AutomationProperties.GetHelpText(matchCount));
        Assert.Contains("Found \"needle\"", viewer.StatusText);

        Assert.True(page.KeyRequested(Key.F3, KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        await WaitFor(() => engine.Requests.Count == 2);
        engine.Requests[1].Done.SetResult(new PageFindResult(false));
        await WaitFor(() => engine.Requests.Count == 3);
        Assert.True(engine.WrapRequests[2]);
        engine.Requests[2].Done.SetResult(new PageFindResult(true));
        await WaitFor(() => viewer.StatusText.Contains("Wrapped to the page start", StringComparison.Ordinal));
        Assert.Equal("Wrapped to the page start. Found \"needle\".", Field<TextBlock>(viewer, "_wrapNotice").Text);
        typeof(ViewerWindow).GetMethod("UpdateStatus", Private)!.Invoke(viewer, null);
        Assert.Contains("Wrapped to the page start", viewer.StatusText);
        Assert.Equal("2 matches", matchCount.Text);

        previousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => engine.Requests.Count == 4);
        Assert.Equal(("needle", false, false), engine.Requests[3].Query);
        Assert.False(engine.WrapRequests[3]);
        engine.Requests[3].Done.SetResult(new PageFindResult(true, 2, true));
        await WaitFor(() => viewer.StatusText == "Found \"needle\" in the page.");

        previousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => engine.Requests.Count == 5);
        engine.Requests[4].Done.SetResult(new PageFindResult(false));
        await WaitFor(() => engine.Requests.Count == 6);
        Assert.True(engine.WrapRequests[5]);
        engine.Requests[5].Done.SetResult(new PageFindResult(true));
        await WaitFor(() => viewer.StatusText.Contains("Wrapped to the page end", StringComparison.Ordinal));
        typeof(ViewerWindow).GetMethod("UpdateStatus", Private)!.Invoke(viewer, null);
        Assert.Contains("Wrapped to the page end", viewer.StatusText);

        var stopped = Find(viewer, forward: true);
        await WaitFor(() => engine.Requests.Count == 7);
        var stop = Field<Button>(viewer, "_stopSearch");
        Assert.True(stop.IsVisible);
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await stopped;
        Assert.Contains("canceled", viewer.StatusText);
        Assert.False(stop.IsVisible);
    }

    [AvaloniaFact]
    public async Task Page_find_count_uses_whole_page_wrap_or_explicit_native_count()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        Field<TextBox>(viewer, "_search").Text = "missing";
        var noMatch = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 1);
        fixture.Engine.Requests[0].Done.SetResult(new PageFindResult(false));
        await WaitFor(() => fixture.Engine.Requests.Count == 2);
        Assert.True(fixture.Engine.WrapRequests[1]);
        fixture.Engine.Requests[1].Done.SetResult(new PageFindResult(false));
        await noMatch;
        Assert.Equal("0 matches in page", Field<TextBlock>(viewer, "_matchCount").Text);
        Assert.Contains("searchMissing", Field<TextBox>(viewer, "_search").Classes);
        Field<TextBox>(viewer, "_search").Text = "needle";
        var found = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 3);
        fixture.Engine.Requests[2].Done.SetResult(new PageFindResult(true));
        await found;
        Assert.Contains("At least 1 match", Field<TextBlock>(viewer, "_matchCount").Text);
        var limited = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 4);
        fixture.Engine.Requests[3].Done.SetResult(new PageFindResult(true, 1025, false));
        await limited;
        Assert.Equal($"At least {1025:N0} matches · count limited", Field<TextBlock>(viewer, "_matchCount").Text);
        Field<TextBox>(viewer, "_search").Text = "absent";
        var completeZero = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 5);
        fixture.Engine.Requests[4].Done.SetResult(new PageFindResult(false, 0, true));
        await completeZero;
        Assert.Equal("0 matches", Field<TextBlock>(viewer, "_matchCount").Text);
        Field<ToggleButton>(viewer, "_modeHex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain("At least 1 match", Field<TextBlock>(viewer, "_matchCount").Text);
    }

    [AvaloniaFact]
    public async Task Page_find_results_retire_after_mode_switch_or_source_revision()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var search = viewer.GetVisualDescendants().OfType<TextBox>()
            .Single(c => AutomationProperties.GetName(c) == "Find in file");
        search.Text = "old";
        Dispatcher.UIThread.RunJobs();
        var pending = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 1);
        typeof(ViewerWindow).GetMethod("SetMode", Private)!.Invoke(viewer, [false]);
        fixture.Engine.Requests[0].Done.TrySetResult(new PageFindResult(true));
        await pending;
        Assert.False(viewer.IsPageShown);
        Assert.DoesNotContain("Found \"old\"", viewer.StatusText);

        viewer.ShowPage();
        var page = Field<PageView>(viewer, "_pageView");
        typeof(PageView).GetField("<PageLoaded>k__BackingField", Private)!.SetValue(page, true);
        long sourceGeneration = Field<long>(viewer, "_sourceGeneration");
        typeof(ViewerWindow).GetField("_sourceGeneration", Private)!.SetValue(viewer, sourceGeneration + 1);
        await Find(viewer, forward: true);
        Assert.Single(fixture.Engine.Requests);
        Assert.Contains("file changed", viewer.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static Task Find(ViewerWindow viewer, bool forward) =>
        (Task)typeof(ViewerWindow).GetMethod("FindAsync", Private)!.Invoke(viewer, [forward])!;

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "Page search request did not reach the engine.");
    }

    [AvaloniaFact]
    public async Task Escape_from_Find_returns_the_keyboard_to_the_page()
    {
        // On Linux and macOS the page's host takes focus with the page: Orca said nothing at all when the page took the
        // keyboard (UXBUG-021), and VoiceOver was left with no focused element.
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var page = Field<PageView>(viewer, "_pageView");
        var search = viewer.GetVisualDescendants().OfType<TextBox>()
            .Single(c => AutomationProperties.GetName(c) == "Find in file");
        Assert.True(page.KeyRequested!(Key.F7, KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        Assert.True(search.IsFocused);
        // The Find control must take the native keyboard too; on Windows the explicit handoff
        // can follow Avalonia's logical focus notification when WebView2 still owns Win32 focus.
        int returned = fixture.Engine.ReturnCount;
        Assert.True(returned >= 1);
        int before = fixture.Engine.FocusCount;
        viewer.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, fixture.Engine.FocusCount);
        Assert.Equal(returned, fixture.Engine.ReturnCount);
        Assert.False(search.IsEffectivelyVisible); // Escape closes Find as well as returning to the page.
        Assert.Equal(PageView.ManagesKeyboard, page.IsFocused);
        Assert.Equal(PageView.ManagesKeyboard ? PageView.ReaderHelp : null, AutomationProperties.GetHelpText(page));
        if (OperatingSystem.IsLinux())
        {
            // Not Avalonia's interop child, which AT-SPI showed as an unnamed, unknown node: the host is the page.
            var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(page);
            Assert.Equal(("Web page", PageView.ReaderHelp), (peer.GetName(), peer.GetHelpText()));
            Assert.True(peer.HasKeyboardFocus());
            Assert.Empty(peer.GetChildren());
        }
        if (PageView.ManagesKeyboard)
        {
            // Reopen Find before using Tab: Escape now hides the bar, so an invisible search box cannot take focus.
            Assert.True(page.KeyRequested!(Key.F7, KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Assert.True(search.IsFocused);
            Assert.Equal(returned + 1, fixture.Engine.ReturnCount);
            int focused = fixture.Engine.FocusCount;
            page.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(focused + 1, fixture.Engine.FocusCount);
        }
    }

    [AvaloniaFact]
    public async Task A_page_opened_before_its_native_engine_is_ready_keeps_only_its_current_focus_request()
    {
        using (var fixture = new Fixture(installEngine: false))
        {
            await fixture.OpenAsync();
            Assert.Equal(0, fixture.Engine.FocusCount);
            Field<PageView>(fixture.Viewer, "_pageView").InstallEngineForTests(fixture.Engine);
            Assert.True(fixture.Engine.FocusCount > 0);
        }

        using (var fixture = new Fixture(installEngine: false))
        {
            await fixture.OpenAsync();
            fixture.Viewer.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Field<TextBox>(fixture.Viewer, "_search").IsFocused);
            Field<PageView>(fixture.Viewer, "_pageView").InstallEngineForTests(fixture.Engine);
            Assert.Equal(0, fixture.Engine.FocusCount);
        }
    }

    [AvaloniaFact]
    public async Task Windows_page_keys_received_by_the_viewer_scroll_only_when_the_native_page_lacks_focus()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var page = Field<PageView>(viewer, "_pageView");
        Assert.True(page.IsEffectivelyVisible);

        viewer.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        viewer.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
        Assert.Equal([1, -1], fixture.Engine.ScrollDirections);

        fixture.Engine.NativeHasFocus = true;
        viewer.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Assert.Equal([1, -1], fixture.Engine.ScrollDirections);

        fixture.Engine.NativeHasFocus = false;
        viewer.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Field<TextBox>(viewer, "_search").IsFocused);
        viewer.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
        Assert.Equal([1, -1], fixture.Engine.ScrollDirections);
    }

    [AvaloniaFact]
    public async Task A_click_in_the_page_or_Tab_past_its_ends_moves_the_keys_with_focus()
    {
        // Linux: the keys went to the page whenever the pointer rested on it, and Tab never left it. macOS: a click in
        // the page left Avalonia's focus, and the screen reader, on Find.
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        Assert.True(viewer.IsActive);
        var page = Field<PageView>(viewer, "_pageView");
        Assert.True(page.KeyRequested!(Key.F7, KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Field<TextBox>(viewer, "_search").IsFocused);
        int focused = fixture.Engine.FocusCount;
        fixture.Engine.Click();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PageView.ManagesKeyboard, page.IsFocused);
        Assert.Equal(focused + (PageView.ManagesKeyboard ? 1 : 0), fixture.Engine.FocusCount);
        if (!PageView.ManagesKeyboard) return;

        var focus = TopLevel.GetTopLevel(page)!.FocusManager!;
        int returned = fixture.Engine.ReturnCount;
        fixture.Engine.TabOut(backward: false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(page.IsFocused);
        Assert.Same(Field<ToggleButton>(viewer, "_modePage"), focus.GetFocusedElement());
        Assert.Equal(returned + 1, fixture.Engine.ReturnCount);
        // Shift+Tab past the page's first item goes back past its host, which would only give the page the keys again,
        // and past Find's Previous and Next buttons, which cannot take focus with nothing to find.
        page.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        fixture.Engine.TabOut(backward: true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(page.IsFocused);
        Assert.Same(Field<CheckBox>(viewer, "_matchCase"), focus.GetFocusedElement());
        Assert.Equal(returned + 2, fixture.Engine.ReturnCount);
    }

    [AvaloniaFact]
    public async Task Each_page_find_outcome_is_spoken_but_not_the_search_before_it()
    {
        // A page does not say which match is selected: Orca heard nothing after F3 and Shift+F3 on a page.
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var status = Field<TextBlock>(viewer, "_status");
        Field<TextBox>(viewer, "_search").Text = "needle";
        foreach (bool forward in new[] { true, false })
        {
            var finding = Find(viewer, forward);
            await WaitFor(() => fixture.Engine.Requests.Count == (forward ? 1 : 2));
            Assert.StartsWith($"Searching page {(forward ? "forward" : "backward")}", status.Text);
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(status));
            fixture.Engine.Requests[^1].Done.SetResult(new PageFindResult(true, 3, true));
            await finding;
            Assert.Equal(("Found \"needle\" in the page.", AutomationLiveSetting.Polite),
                (status.Text, AutomationProperties.GetLiveSetting(status)));
        }
    }

    [AvaloniaFact]
    public async Task F4_from_a_page_shows_its_text_also_from_Find()
    {
        // The page's status line says F4 shows its text; with the keyboard in Find it showed hex instead.
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        Assert.True(Field<PageView>(viewer, "_pageView").KeyRequested!(Key.F7, KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        Assert.True(Field<TextBox>(viewer, "_search").IsFocused);
        viewer.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewer.IsPageShown);
        Assert.Equal("Mode: Text", Field<TextBlock>(viewer, "_modeLabel").Text);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-page-search-" + Guid.NewGuid().ToString("N"));
        private readonly AppServices _services;
        private readonly FileContentSource _source;
        public ViewerWindow Viewer { get; }
        public ControlledEngine Engine { get; } = new();

        public Fixture(bool installEngine = true)
        {
            Directory.CreateDirectory(_root);
            string path = Path.Combine(_root, "readme.md");
            File.WriteAllText(path, "# Search\n\nA needle here.");
            _services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(_root, "state")));
            _source = new FileContentSource(path);
            Viewer = new ViewerWindow(_services, _source, path, hex: false);
            if (installEngine) Field<PageView>(Viewer, "_pageView").InstallEngineForTests(Engine);
        }

        public async Task OpenAsync()
        {
            Viewer.Show();
            // As a viewer opened from the main window is: an inactive window does not take the keyboard (Linux).
            Viewer.Activate();
            Dispatcher.UIThread.RunJobs();
            await WaitFor(() => Field<ToggleButton>(Viewer, "_modePage").IsVisible);
            Viewer.ShowPage();
            typeof(PageView).GetField("<PageLoaded>k__BackingField", Private)!
                .SetValue(Field<PageView>(Viewer, "_pageView"), true);
        }

        public void Dispose()
        {
            Viewer.Close();
            Engine.Dispose();
            _source.Dispose();
            _services.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ControlledEngine : IPageEngine
    {
        public List<((string Pattern, bool Case, bool Forward) Query, TaskCompletionSource<PageFindResult?> Done)> Requests { get; } = [];
        public List<bool> WrapRequests { get; } = [];
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "page-search-test");
        public string? Title => "Search page";
        public int BlockedCount => 0;
        public event Action? Changed { add { } remove { } }
        public event Action<bool, string?>? Loaded { add { } remove { } }
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page) { }
        public void SetSize(int width, int height) { }
        public int FocusCount { get; private set; }
        public bool NativeHasFocus { get; set; }
        public bool HasKeyboardFocus => NativeHasFocus;
        public List<int> ScrollDirections { get; } = [];
        public Task<bool> ScrollByPageAsync(int direction)
        {
            ScrollDirections.Add(direction);
            return Task.FromResult(true);
        }
        public void Focus() => FocusCount++;
        public int ReturnCount { get; private set; }
        public void ReturnFocus(nint window) => ReturnCount++;
        public event Action? FocusRequested;
        public event Action<bool>? FocusLeft;
        public void Click() => FocusRequested?.Invoke();
        public void TabOut(bool backward) => FocusLeft?.Invoke(backward);
        public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct)
            => FindAsync(text, matchCase, forward, ct, wrap: false);
        public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct, bool wrap)
        {
            var done = new TaskCompletionSource<PageFindResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(((text, matchCase, forward), done));
            WrapRequests.Add(wrap);
            return done.Task.WaitAsync(ct);
        }
        public void Dispose() { }
    }
}
