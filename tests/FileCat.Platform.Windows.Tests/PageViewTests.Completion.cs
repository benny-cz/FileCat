using System.Collections.Concurrent;
using System.Diagnostics;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Html;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class PageViewTests
{
    /// <summary>
    /// Release issue I648 in the real engine: WebView2 sometimes never reported that a Markdown page finished loading,
    /// though both of its requests were answered, and the page stayed blank with Refresh disabled. With WebView2's
    /// completion withheld, the page still counts as drawn once it has been quiet for the view's bound, exactly once.
    /// </summary>
    [Fact]
    public void A_markdown_page_whose_completion_never_comes_counts_as_drawn_within_the_bound()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "withheld")).FullName;
        string readme = Path.Combine(site, "README.md");
        File.WriteAllText(readme, "# Withheld\n\nA page whose completion WebView2 does not report.\n\n![local](pic.png)\n");
        File.WriteAllBytes(Path.Combine(site, "pic.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var served = new ConcurrentQueue<(string Path, bool Found)>();
        var completions = new List<(bool Ok, string? Why, double Seconds)>();
        double lastServed = 0;
        string? title = null, diagnostics = null;
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            try
            {
                using var source = new FileContentSource(readme);
                using var view = new WebView2Page(parent, Path.Combine(_dir, "webview-withheld"));
                var clock = Stopwatch.StartNew();
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.WithholdNavigationCompletedForTests = true;
                view.Served += (path, found) =>
                {
                    served.Enqueue((path, found));
                    lastServed = clock.Elapsed.TotalSeconds;
                };
                view.Loaded += (ok, why) =>
                {
                    completions.Add((ok, why, clock.Elapsed.TotalSeconds));
                    done.TrySetResult();
                };
                view.SetSize(900, 1000);
                view.Show(HtmlPage.ForMarkdown(source, readme));
                // The browser's own start (seconds on a busy runner) and the bound itself.
                var bound = TimeSpan.FromSeconds(45) + WebView2Page.CompletionQuiet;
                try { await done.Task.WaitAsync(bound); }
                catch (TimeoutException error)
                {
                    throw new TimeoutException($"The page did not count as drawn within {bound.TotalSeconds:0} s. " + view.LoadDiagnostics, error);
                }
                // Nothing more is reported: one page, one report.
                await Task.Delay(WebView2Page.CompletionQuiet + TimeSpan.FromSeconds(1));
                title = view.Title;
                diagnostics = view.LoadDiagnostics;
            }
            finally { StaPump.DestroyWindow(parent); }
        }, TimeSpan.FromSeconds(90));

        TestContext.Current.TestOutputHelper?.WriteLine(diagnostics ?? "");
        var (ok, why, seconds) = Assert.Single(completions);
        Assert.True(ok, why);
        // Not before the page went quiet (its last request was answered just after it was served), and not long after.
        double quiet = WebView2Page.CompletionQuiet.TotalSeconds;
        Assert.InRange(seconds - lastServed, quiet - 0.05, quiet + 10);
        Assert.Equal("README.md", title);
        Assert.Contains(served, s => s.Path == "/README.md" && s.Found);
        Assert.Contains(served, s => s.Path == "/pic.png" && s.Found);
        Assert.Contains("(withheld for tests)", diagnostics, StringComparison.Ordinal);
        Assert.Contains("DOM content loaded", diagnostics, StringComparison.Ordinal);
        Assert.Contains("watch: no completion came", diagnostics, StringComparison.Ordinal);
    }

    /// <summary>
    /// Release issue I648, the stall reproduced: page views share their data folder's browser process, and a view started
    /// while that process was exiting, its folder's last view just closed, waited minutes for its controller or failed with
    /// ERROR_BUSY. A view opened right after the last one closed waits for the old browser to exit, then draws its page.
    /// It says when it begins and ends that wait, so the viewer can say what a slow page waits for (I648's follow-up).
    /// </summary>
    [Fact]
    public void A_page_opened_right_after_the_folders_last_one_closed_waits_for_the_old_browser_to_exit()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "exits")).FullName;
        string links = Path.Combine(site, "links.md");
        File.WriteAllText(links, "# Links\n\n[Plan](plan.md)\n");
        string data = Path.Combine(_dir, "webview-exits");
        (string Timeline, List<bool> Waits)? first = null, second = null;
        StaPump.Run(async () =>
        {
            first = await OpenAndClose();
            second = await OpenAndClose();
        }, TimeSpan.FromSeconds(120));

        TestContext.Current.TestOutputHelper?.WriteLine("First view:\n" + first?.Timeline + "\nSecond view:\n" + second?.Timeline);
        Assert.DoesNotContain("waiting for the folder's previous browser process to exit", first!.Value.Timeline, StringComparison.Ordinal);
        Assert.Empty(first.Value.Waits);
        string timeline = second!.Value.Timeline;
        Assert.Contains("waiting for the folder's previous browser process to exit", timeline, StringComparison.Ordinal);
        // On a busy runner the exit may take longer than the view waits for it; the view starts all the same then.
        Assert.True(timeline.Contains("it exited after", StringComparison.Ordinal) ||
            timeline.Contains("it did not exit within", StringComparison.Ordinal), timeline);
        // The viewer hears of the wait as it begins and as it ends (I648: it says what the page waits for), and the log
        // says what a slow page waits for.
        Assert.Equal(new[] { true, false }, second.Value.Waits);
        Assert.Contains("viewer: the page has not finished loading 10.0 s after it was asked for (", timeline, StringComparison.Ordinal);
        Assert.Contains(", while the view waits for its folder's previous browser process to exit", timeline, StringComparison.Ordinal);

        async Task<(string, List<bool>)> OpenAndClose()
        {
            nint parent = StaPump.CreateHiddenWindow();
            try
            {
                using var source = new FileContentSource(links);
                using var view = new WebView2Page(parent, data);
                var waits = new List<bool>();
                view.WaitChanged += () =>
                {
                    waits.Add(view.WaitsForPreviousBrowser);
                    if (view.WaitsForPreviousBrowser) view.ReportSlowLoad(TimeSpan.FromSeconds(10));
                };
                var loaded = new TaskCompletionSource<(bool Ok, string? Why)>(TaskCreationOptions.RunContinuationsAsynchronously);
                view.Loaded += (ok, why) => loaded.TrySetResult((ok, why));
                view.SetSize(640, 480);
                view.Show(HtmlPage.ForMarkdown(source, links));
                var (ok, why) = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(50));
                Assert.True(ok, why + "\n" + view.LoadDiagnostics);
                Assert.Equal("links.md", view.Title);
                Assert.False(view.WaitsForPreviousBrowser);
                return (view.Timeline.Describe(), waits);
            }
            finally { StaPump.DestroyWindow(parent); }
        }
    }
}
