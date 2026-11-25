using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Release issue I648: WebView2 sometimes never reported that a Markdown page finished loading. The viewer then kept
/// Refresh disabled and the page blank for good. Through an engine that withholds its completion: Refresh stays
/// available while the page loads, the status line says the page has not finished, and a completion that belongs to
/// the page before the one now shown does not count for it.
/// </summary>
public sealed class PageCompletionRecoveryTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaFact]
    public async Task A_page_whose_completion_never_comes_can_still_be_refreshed()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-withheld-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        int reopened = 0;
        Task<IContentSource?> Reopen(CancellationToken _) => Task.FromResult<IContentSource?>(
            new MemoryContentSource("links.md", Encoding.UTF8.GetBytes($"# Links {++reopened}\n\n[Plan](plan.md)\n")));
        var viewer = new ViewerWindow(services, new MemoryContentSource("links.md", "# Links\n\n[Plan](plan.md)\n"u8.ToArray()),
            "links.md", hex: false, deviceKey: "page-withheld", preparedReader: null, reopen: Reopen);
        var engine = new WithholdingEngine();
        var pageView = Field<PageView>(viewer, "_pageView");
        pageView.InstallEngineForTests(engine);
        try
        {
            viewer.Show();
            await Until(() => engine.Pages.Count == 1 && viewer.IsPageShown);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            // The page never reports that it finished, and the reader is not left without a way on.
            await Until(() => Field<Button>(viewer, "_refresh").IsEnabled);
            Assert.False(pageView.PageLoaded);

            Task refreshing = viewer.RefreshAsync();
            await Until(() => engine.Pages.Count == 2);
            Assert.Contains("Links 1", Encoding.UTF8.GetString(engine.Pages[1].Resolve("/links.md")!.Value.Bytes));
            engine.Complete(true);
            await refreshing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(pageView.PageLoaded);
            Assert.True(viewer.IsPageShown);
            Assert.False(Field<Border>(viewer, "_staleBanner").IsVisible);
            Assert.True(Field<Button>(viewer, "_refresh").IsEnabled);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_page_that_has_not_finished_loading_says_so_and_how_to_get_past_it()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-slow-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new MemoryContentSource("links.md", "# Links\n"u8.ToArray()),
            "links.md", hex: false, deviceKey: "page-slow", preparedReader: null,
            reopen: _ => Task.FromResult<IContentSource?>(new MemoryContentSource("links.md", "# Links\n"u8.ToArray())));
        var engine = new WithholdingEngine();
        var pageView = Field<PageView>(viewer, "_pageView");
        pageView.SlowLoadNotice = TimeSpan.FromMilliseconds(300);
        pageView.InstallEngineForTests(engine);
        try
        {
            viewer.Show();
            await Until(() => engine.Pages.Count == 1 && viewer.IsPageShown);
            Assert.DoesNotContain("has not finished loading", viewer.StatusText, StringComparison.Ordinal);
            await Until(() => viewer.StatusText.StartsWith("The page has not finished loading; Refresh (F5) tries again. ", StringComparison.Ordinal));
            Assert.Contains("F4 shows its text", viewer.StatusText, StringComparison.Ordinal);
            engine.Complete(true);
            await Until(() => pageView.PageLoaded && !viewer.StatusText.Contains("has not finished loading", StringComparison.Ordinal));
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// I648 on the pre-rc3 build in the qualification VM: 7 of 20 Pages drew 10.1 to 11.9 s after F3, and none of them
    /// said in the status line that it had not finished loading. The notice counted its 10 s from when the page view had
    /// the page, not from when the reader asked for it: the wait for the viewer itself (here a slow first read) went
    /// unsaid. Then the engine waits for the browser of the pages closed just before to end, and the page loads at once
    /// after it. The status line says what the page waits for once the notice's time has passed since the viewer was
    /// asked for, then that it is loading, and the page counts as loaded once.
    /// </summary>
    [AvaloniaFact]
    public async Task A_page_says_it_has_not_finished_loading_counting_from_when_it_was_asked_for()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-asked-" + Guid.NewGuid().ToString("N"))).FullName;
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var opening = TimeSpan.FromSeconds(2);
        var notice = TimeSpan.FromSeconds(3);
        var asked = System.Diagnostics.Stopwatch.StartNew();
        var viewer = new ViewerWindow(services, new SlowFirstReadSource("links.md", "# Links\n\n[Plan](plan.md)\n"u8.ToArray(), opening),
            "links.md", hex: false, deviceKey: "page-asked", preparedReader: null,
            reopen: _ => Task.FromResult<IContentSource?>(new MemoryContentSource("links.md", "# Links\n"u8.ToArray())));
        // The previous browser is still ending when the engine gets the page.
        var engine = new WithholdingEngine { WaitsForPreviousBrowser = true };
        var pageView = Field<PageView>(viewer, "_pageView");
        pageView.SlowLoadNotice = notice;
        pageView.InstallEngineForTests(engine);
        var completions = new List<bool>();
        pageView.LoadCompleted += (ok, _) => completions.Add(ok);
        try
        {
            viewer.Show();
            await Until(() => engine.Pages.Count == 1 && viewer.IsPageShown);
            double given = asked.Elapsed.TotalSeconds;
            Assert.True(given >= opening.TotalSeconds - 0.1, $"The page reached the engine {given:0.00} s after the viewer was asked for.");
            Assert.DoesNotContain("has not finished loading", viewer.StatusText, StringComparison.Ordinal);

            await Until(() => viewer.StatusText.Contains("has not finished loading", StringComparison.Ordinal));
            double said = asked.Elapsed.TotalSeconds;
            // Not before the notice's time since the viewer was asked for, and well before that time since the engine had
            // the page (where the old count would have put it).
            Assert.True(said >= notice.TotalSeconds - 0.1 && said < given + notice.TotalSeconds - 0.5,
                $"Said {said:0.00} s after the viewer was asked for; the engine had the page after {given:0.00} s.");
            Assert.StartsWith("The page has not finished loading: it waits for the previous page's browser to close. ", viewer.StatusText, StringComparison.Ordinal);
            Assert.Contains("F4 shows its text", viewer.StatusText, StringComparison.Ordinal);
            Assert.True(engine.SlowReports is [var report] && report >= notice - TimeSpan.FromMilliseconds(100), "The engine heard of the notice once.");

            // The previous browser ended: the page is loading now, and Refresh can try again.
            engine.EndWait();
            await Until(() => viewer.StatusText.StartsWith("The page has not finished loading; Refresh (F5) tries again. ", StringComparison.Ordinal));
            engine.Complete(true);
            await Until(() => pageView.PageLoaded && !viewer.StatusText.Contains("has not finished loading", StringComparison.Ordinal));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.Equal(new[] { true }, completions);
            Assert.Single(engine.SlowReports);
        }
        finally
        {
            viewer.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_page_asked_for_before_the_view_had_it_says_so_sooner_and_a_refresh_counts_anew()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-earlier-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var view = new PageView(folder) { SlowLoadNotice = TimeSpan.FromSeconds(2) };
            var engine = new WithholdingEngine();
            view.InstallEngineForTests(engine);
            var asked = System.Diagnostics.Stopwatch.StartNew();
            // The reader asked 1.5 s before the view had the page: half a second of the notice's time is left.
            view.Show(new HtmlPage(new MemoryContentSource("old.html", "<p>old</p>"u8.ToArray()), "old.html"), Environment.TickCount64 - 1500);
            Assert.False(view.LoadIsSlow);
            await Until(() => view.LoadIsSlow);
            Assert.InRange(asked.Elapsed.TotalSeconds, 0.4, 1.5);

            // A Refresh is asked for now: its notice counts the whole time again.
            asked.Restart();
            view.Show(new HtmlPage(new MemoryContentSource("new.html", "<p>new</p>"u8.ToArray()), "new.html"));
            Assert.False(view.LoadIsSlow);
            await Until(() => view.LoadIsSlow);
            Assert.True(asked.Elapsed.TotalSeconds >= 1.9, $"Said after {asked.Elapsed.TotalSeconds:0.00} s.");
            engine.Complete(true);
            await Until(() => view.PageLoaded && !view.LoadIsSlow);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_completion_reported_before_the_next_page_was_asked_for_does_not_count_for_it()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-late-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var view = new PageView(folder);
            var engine = new WithholdingEngine();
            view.InstallEngineForTests(engine);
            var completions = new List<bool>();
            view.LoadCompleted += (ok, _) => completions.Add(ok);
            view.Show(new HtmlPage(new MemoryContentSource("old.html", "<p>old</p>"u8.ToArray()), "old.html"));
            // The old page's completion is on its way to the UI thread when Refresh asks for the new page.
            engine.Complete(true);
            view.Show(new HtmlPage(new MemoryContentSource("new.html", "<p>new</p>"u8.ToArray()), "new.html"));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.False(view.PageLoaded);
            Assert.Empty(completions);

            engine.Complete(true);
            await Until(() => view.PageLoaded);
            Assert.Equal(new[] { true }, completions);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static T Field<T>(ViewerWindow viewer, string name) where T : class =>
        Assert.IsType<T>(typeof(ViewerWindow).GetField(name, Private)!.GetValue(viewer));

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>
    /// An engine that shows pages and reports their completion only when the test says so; it may first wait, as WebView2
    /// does for the browser of the pages closed just before to end.
    /// </summary>
    private sealed class WithholdingEngine : IPageEngine
    {
        public List<HtmlPage> Pages { get; } = [];
        public List<TimeSpan> SlowReports { get; } = [];
        public IPlatformHandle Handle { get; } = new PlatformHandle(0, "withholding-page");
        public string? Title => null;
        public int BlockedCount => 0;
        public bool WaitsForPreviousBrowser { get; set; }
        public event Action? Changed;
        public event Action<bool, string?>? Loaded;
        public Func<Key, KeyModifiers, bool>? KeyRequested { get; set; }
        public void Show(HtmlPage page) => Pages.Add(page);
        public void Complete(bool ok, string? why = null) => Loaded?.Invoke(ok, why);
        public void ReportSlowLoad(TimeSpan sinceAsked) => SlowReports.Add(sinceAsked);

        public void EndWait()
        {
            WaitsForPreviousBrowser = false;
            Changed?.Invoke();
        }

        public void SetSize(int width, int height) { }
        public void Focus() { }
        public void Dispose() { }
    }

    /// <summary>Content whose first read takes a while (a slow disk or share): the viewer opens on the page only then.</summary>
    private sealed class SlowFirstReadSource(string name, byte[] data, TimeSpan firstRead) : IContentSource
    {
        private readonly MemoryContentSource _inner = new(name, data);
        private int _reads;

        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public void Dispose() => _inner.Dispose();

        public int Read(long offset, Span<byte> buffer)
        {
            if (Interlocked.Increment(ref _reads) == 1) Thread.Sleep(firstRead);
            return _inner.Read(offset, buffer);
        }
    }
}
