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
    public async Task Page_find_controls_search_forward_backward_report_no_match_and_stop()
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
        engine.Requests[0].Done.SetResult(new PageFindResult(true, 2, true));
        await WaitFor(() => matchCount.Text == "2 matches");
        Assert.Contains("counted all matches", AutomationProperties.GetHelpText(matchCount));
        Assert.Contains("Found \"needle\"", viewer.StatusText);

        Assert.True(page.KeyRequested(Key.F3, KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        await WaitFor(() => engine.Requests.Count == 2);
        engine.Requests[1].Done.SetResult(new PageFindResult(false));
        await WaitFor(() => viewer.StatusText.Contains("was not found", StringComparison.Ordinal));
        Assert.Contains("was not found", viewer.StatusText);
        Assert.Equal("2 matches", matchCount.Text);

        previousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => engine.Requests.Count == 3);
        Assert.Equal(("needle", false, false), engine.Requests[2].Query);
        engine.Requests[2].Done.SetResult(new PageFindResult(true, 2, true));
        await WaitFor(() => viewer.StatusText.Contains("Found \"needle\"", StringComparison.Ordinal));
        Assert.Contains("Found \"needle\"", viewer.StatusText);

        var stopped = Find(viewer, forward: true);
        await WaitFor(() => engine.Requests.Count == 4);
        var stop = Field<Button>(viewer, "_stopSearch");
        Assert.True(stop.IsVisible);
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await stopped;
        Assert.Contains("canceled", viewer.StatusText);
        Assert.False(stop.IsVisible);
    }

    [AvaloniaFact]
    public async Task Page_find_count_never_claims_an_exact_total_from_a_directional_browser_result()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        Field<TextBox>(viewer, "_search").Text = "missing";
        var noMatch = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 1);
        fixture.Engine.Requests[0].Done.SetResult(new PageFindResult(false));
        await noMatch;
        Assert.Contains("total unavailable", Field<TextBlock>(viewer, "_matchCount").Text);
        Assert.Contains("0 matches from here", Field<TextBlock>(viewer, "_matchCount").Text);
        Field<TextBox>(viewer, "_search").Text = "needle";
        var found = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 2);
        fixture.Engine.Requests[1].Done.SetResult(new PageFindResult(true));
        await found;
        Assert.Contains("At least 1 match", Field<TextBlock>(viewer, "_matchCount").Text);
        var limited = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 3);
        fixture.Engine.Requests[2].Done.SetResult(new PageFindResult(true, 1025, false));
        await limited;
        Assert.Equal($"At least {1025:N0} matches · count limited", Field<TextBlock>(viewer, "_matchCount").Text);
        Field<TextBox>(viewer, "_search").Text = "absent";
        var completeZero = Find(viewer, forward: true);
        await WaitFor(() => fixture.Engine.Requests.Count == 4);
        fixture.Engine.Requests[3].Done.SetResult(new PageFindResult(false, 0, true));
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
        // On Linux the page's host takes focus first: Orca cannot reach inside the embedded GTK page (UXBUG-021), and
        // it said nothing at all when the page took the keyboard.
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var viewer = fixture.Viewer;
        var page = Field<PageView>(viewer, "_pageView");
        var search = viewer.GetVisualDescendants().OfType<TextBox>()
            .Single(c => AutomationProperties.GetName(c) == "Find in file");
        Assert.True(page.KeyRequested!(Key.F7, KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        Assert.True(search.IsFocused);
        // Linux: the X keyboard focus left the page too, or the typed text kept going into it.
        Assert.Equal(OperatingSystem.IsLinux() ? 1 : 0, fixture.Engine.ReturnCount);
        int before = fixture.Engine.FocusCount;
        viewer.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before + 1, fixture.Engine.FocusCount);
        Assert.Equal(OperatingSystem.IsLinux() ? 1 : 0, fixture.Engine.ReturnCount);
        Assert.Equal(OperatingSystem.IsLinux(), page.IsFocused);
        Assert.Equal(OperatingSystem.IsLinux() ? PageView.LinuxReaderHelp : null, AutomationProperties.GetHelpText(page));
        if (OperatingSystem.IsLinux())
        {
            // Not Avalonia's interop child, which AT-SPI showed as an unnamed, unknown node: the host is the page.
            var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(page);
            Assert.Equal(("Web page", PageView.LinuxReaderHelp), (peer.GetName(), peer.GetHelpText()));
            Assert.True(peer.HasKeyboardFocus());
            Assert.Empty(peer.GetChildren());
            // Tab back onto the host from Find hands the keys to the page as well, so the next Tab moves into it.
            search.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, fixture.Engine.ReturnCount);
            int focused = fixture.Engine.FocusCount;
            page.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(focused + 1, fixture.Engine.FocusCount);
        }
    }

    [AvaloniaFact]
    public async Task A_click_in_the_page_or_Tab_past_its_ends_moves_the_keys_with_focus()
    {
        // Linux: the keys went to the page whenever the pointer rested on it, and Tab never left it.
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
        Assert.Equal(OperatingSystem.IsLinux(), page.IsFocused);
        Assert.Equal(focused + (OperatingSystem.IsLinux() ? 1 : 0), fixture.Engine.FocusCount);
        if (!OperatingSystem.IsLinux()) return;

        var focus = TopLevel.GetTopLevel(page)!.FocusManager!;
        int returned = fixture.Engine.ReturnCount;
        fixture.Engine.TabOut(backward: false);
        Dispatcher.UIThread.RunJobs();
        var after = focus.GetFocusedElement();
        Assert.False(page.IsFocused);
        Assert.NotNull(after);
        Assert.Equal(returned + 1, fixture.Engine.ReturnCount);
        // Shift+Tab past the page's first item goes back past its host, which would only give the page the keys again.
        page.Focus();
        Dispatcher.UIThread.RunJobs();
        fixture.Engine.TabOut(backward: true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(page.IsFocused);
        Assert.NotNull(focus.GetFocusedElement());
        Assert.NotSame(after, focus.GetFocusedElement());
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

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            string path = Path.Combine(_root, "readme.md");
            File.WriteAllText(path, "# Search\n\nA needle here.");
            _services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(_root, "state")));
            _source = new FileContentSource(path);
            Viewer = new ViewerWindow(_services, _source, path, hex: false);
            var page = Field<PageView>(Viewer, "_pageView");
            typeof(PageView).GetField("_engine", Private)!.SetValue(page, Engine);
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
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "page-search-test");
        public string? Title => "Search page";
        public int BlockedCount => 0;
        public event Action? Changed { add { } remove { } }
        public event Action<bool, string?>? Loaded { add { } remove { } }
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page) { }
        public void SetSize(int width, int height) { }
        public int FocusCount { get; private set; }
        public void Focus() => FocusCount++;
        public int ReturnCount { get; private set; }
        public void ReturnFocus(nint window) => ReturnCount++;
        public event Action? FocusRequested;
        public event Action<bool>? FocusLeft;
        public void Click() => FocusRequested?.Invoke();
        public void TabOut(bool backward) => FocusLeft?.Invoke(backward);
        public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct)
        {
            var done = new TaskCompletionSource<PageFindResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(((text, matchCase, forward), done));
            return done.Task.WaitAsync(ct);
        }
        public void Dispose() { }
    }
}
