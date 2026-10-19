using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        public void Focus() { }
        public Task<PageFindResult?> FindAsync(string text, bool matchCase, bool forward, CancellationToken ct)
        {
            var done = new TaskCompletionSource<PageFindResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(((text, matchCase, forward), done));
            return done.Task.WaitAsync(ct);
        }
        public void Dispose() { }
    }
}
