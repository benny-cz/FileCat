using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Html;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class PageViewTests
{
    /// <summary>
    /// Release issue I648, again and again: Markdown pages opened one after another in real WebView2, sharing one data
    /// folder as the viewers do: alone, two at once, and right after three others closed. Every page must report that it
    /// finished loading. One that does not is reported with its timeline and the size of a picture of what it drew, and
    /// is navigated once more (as Refresh would) to see whether that recovers it. A page the view had to count as drawn
    /// itself, or navigate again, because WebView2 reported no completion is counted and its timeline kept.
    /// FILECAT_PAGE_REPEAT: rounds (each opens one to four pages). FILECAT_PAGE_REPEAT_WAIT: seconds a page may take
    /// (default 20). FILECAT_PAGE_REPEAT_FRESH=1: a data folder of its own for every page, as the other page tests use.
    /// FILECAT_PAGE_REPEAT_DATA: one data folder for every page, kept between runs, as FileCat's profile keeps its own.
    /// FILECAT_PAGE_REPEAT_REPORT: a folder for the results and the pictures.
    /// </summary>
    [Fact]
    public void Markdown_pages_opened_again_and_again_each_finish_loading()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_PAGE_REPEAT"), out int rounds) || rounds < 1)
            Assert.Skip("Set FILECAT_PAGE_REPEAT to a number of rounds to open Markdown pages again and again (release issue I648).");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        var wait = TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("FILECAT_PAGE_REPEAT_WAIT"), out int seconds) && seconds > 0 ? seconds : 20);
        bool fresh = Environment.GetEnvironmentVariable("FILECAT_PAGE_REPEAT_FRESH") == "1";
        string? reportFolder = Environment.GetEnvironmentVariable("FILECAT_PAGE_REPEAT_REPORT") is { Length: > 0 } report
            ? Directory.CreateDirectory(report).FullName : null;
        string sharedData = Environment.GetEnvironmentVariable("FILECAT_PAGE_REPEAT_DATA") is { Length: > 0 } kept
            ? kept : Path.Combine(_dir, "repeat-data");

        string site = Directory.CreateDirectory(Path.Combine(_dir, "repeat")).FullName;
        string links = Path.Combine(site, "links.md");
        File.WriteAllText(links, "# Links\n\nThe documents beside this one:\n\n- [Plan](plan.md)\n- [Results](results.md)\n" +
            "- [Dashboard](dashboard.md)\n- [Notes](README.md)\n- [Checklist](checklist.md)\n- [Log](log.md)\n- [Web](https://example.com/)\n");
        string picture = Path.Combine(site, "README.md");
        File.WriteAllText(picture, "# Notes\n\nSome *emphasis* and a picture beside it:\n\n![local](pic.png) ![remote](https://tracker.example/pixel.gif)\n");
        File.WriteAllBytes(Path.Combine(site, "pic.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        string large = Path.Combine(site, "results.md");
        var text = new StringBuilder();
        for (int section = 0; text.Length < 318_000; section++)
        {
            text.Append(CultureInfo.InvariantCulture, $"## Check {section:D4}\n\nThe check ran with **care**, `flags`, and a [link](links.md).\n\n| Step | Result | Time |\n|:--|:--|--:|\n");
            for (int row = 0; row < 6; row++) text.Append(CultureInfo.InvariantCulture, $"| step {row} | passed, nothing refused | {section * 7 + row} ms |\n");
            text.Append("\n```text\nexit 0, 2 requests served, 0 refused\n```\n\n");
        }
        File.WriteAllText(large, text.ToString());

        var times = new Dictionary<string, List<double>>();
        var stalls = new List<string>();
        var failures = new List<string>();
        var slow = new List<string>();
        var references = new HashSet<string>();
        var watched = new List<string>();
        int opened = 0, recovered = 0;
        int folders = 0;

        StaPump.Run(async () =>
        {
            for (int round = 0; round < rounds; round++)
            {
                int scenario = round % 5;
                switch (scenario)
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        var (kind, path) = scenario switch { 0 => ("links", links), 1 => ("picture", picture), _ => ("large", large) };
                        var page = Start(kind, path);
                        await Finish(page, round, "alone");
                        page.Close();
                        break;
                    }
                    case 3:
                    {
                        var first = Start("large", large);
                        var second = Start("links", links);
                        await Finish(first, round, "two at once");
                        await Finish(second, round, "two at once");
                        first.Close();
                        second.Close();
                        break;
                    }
                    default:
                    {
                        var open = new[] { Start("picture", picture), Start("large", large), Start("links", links) };
                        foreach (var page in open) await Finish(page, round, "three open");
                        foreach (var page in open) page.Close();
                        var next = Start("links", links);
                        await Finish(next, round, "right after three closed");
                        next.Close();
                        break;
                    }
                }
                await Task.Delay(100);
            }
        }, TimeSpan.FromSeconds(rounds * 4 * (2 * wait.TotalSeconds + 10) + 60));

        var summary = new StringBuilder();
        summary.Append(CultureInfo.InvariantCulture,
            $"WebView2 {WebView2Page.RuntimeVersion}; {rounds} rounds, {opened} pages, " +
            $"{(fresh ? "a data folder per page" : sharedData.StartsWith(_dir, StringComparison.Ordinal) ? "one shared data folder" : "one data folder kept between runs")}; " +
            $"{stalls.Count} without completion within {wait.TotalSeconds:0} s, {recovered} of them completed after one more navigation; {failures.Count} failed; " +
            $"{watched.Count} went on without WebView2's completion.\n");
        foreach (var (kind, list) in times.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            list.Sort();
            summary.Append(CultureInfo.InvariantCulture,
                $"{kind}: {list.Count} completed; median {Percentile(list, 0.5):0} ms, 95th {Percentile(list, 0.95):0} ms, 99th {Percentile(list, 0.99):0} ms, longest {list[^1]:0} ms.\n");
        }
        foreach (var entry in slow.Take(10)) summary.Append(entry);
        foreach (var entry in watched) summary.Append(entry);
        foreach (var stall in stalls) summary.Append(stall);
        foreach (var failure in failures) summary.Append(failure);
        TestContext.Current.TestOutputHelper?.WriteLine(summary.ToString());
        if (reportFolder is not null) File.WriteAllText(Path.Combine(reportFolder, "summary.txt"), summary.ToString());
        Assert.True(stalls.Count == 0 && failures.Count == 0, summary.ToString());

        RepeatPage Start(string kind, string path)
        {
            opened++;
            string data = fresh ? Path.Combine(_dir, "repeat-data-" + folders++.ToString(CultureInfo.InvariantCulture)) : sharedData;
            nint parent = StaPump.CreateHiddenWindow();
            var source = new FileContentSource(path);
            var view = new WebView2Page(parent, data);
            var page = new RepeatPage(kind, parent, source, view, HtmlPage.ForMarkdown(source, path));
            view.Loaded += page.Report;
            view.SetSize(900, 1000);
            view.Show(page.Page);
            return page;
        }

        async Task Finish(RepeatPage page, int round, string scenario)
        {
            if (await Task.WhenAny(page.Completion.Task, Task.Delay(wait)) == page.Completion.Task)
            {
                var (ok, why, ms) = page.Reports[0];
                if (!ok)
                {
                    failures.Add(Keep($"\nRound {round} ({scenario}), {page.Kind}: the page failed after {ms:0} ms: {why}\n{page.View.Timeline.Describe()}"));
                    return;
                }
                if (!times.TryGetValue(page.Kind, out var list)) times[page.Kind] = list = [];
                list.Add(ms);
                string timeline = page.View.Timeline.Describe();
                if (timeline.Contains("watch:", StringComparison.Ordinal))
                {
                    // WebView2 reported no completion, and the view went on without it (I648): what did the page show?
                    string picture = await Capture(page, reportFolder is null ? null : Path.Combine(reportFolder, $"watched-{round}-{page.Kind}.png"));
                    watched.Add(Keep($"\nRound {round} ({scenario}), {page.Kind}: the view went on without WebView2's completion after {ms:0} ms. {picture}\n{timeline}"));
                }
                else if (ms > 3000)
                    slow.Add($"Round {round} ({scenario}), {page.Kind}: completed after {ms:0} ms.\n{timeline}\n");
                if (reportFolder is not null && references.Add(page.Kind))
                    await Capture(page, Path.Combine(reportFolder, $"reference-{page.Kind}.png"));
                return;
            }
            var stall = new StringBuilder();
            stall.Append(CultureInfo.InvariantCulture, $"\nRound {round} ({scenario}), {page.Kind}: no completion within {wait.TotalSeconds:0} s.\n");
            stall.Append(await Capture(page, reportFolder is null ? null : Path.Combine(reportFolder, $"stall-{round}-{page.Kind}.png"))).Append('\n');
            stall.Append(page.View.LoadDiagnostics).Append('\n');
            // What Refresh would do: the same page once more.
            page.Again();
            try
            {
                page.View.Show(page.Page);
                if (await Task.WhenAny(page.Completion.Task, Task.Delay(wait)) == page.Completion.Task)
                {
                    recovered++;
                    stall.Append(CultureInfo.InvariantCulture, $"One more navigation completed: {page.Reports[^1].Ok} {page.Reports[^1].Why}.\n");
                }
                else stall.Append("One more navigation did not complete either.\n");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                stall.Append(CultureInfo.InvariantCulture, $"One more navigation could not start: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}\n");
            }
            stall.Append("Timeline after it:\n").Append(page.View.Timeline.Describe());
            stalls.Add(Keep(stall.ToString()));
        }

        // Kept as it happens, so a run that ends early still says what it saw.
        string Keep(string entry)
        {
            if (reportFolder is not null) File.AppendAllText(Path.Combine(reportFolder, "events.txt"), entry);
            return entry;
        }
    }

    private static async Task<string> Capture(RepeatPage page, string? file)
    {
        using var png = new MemoryStream();
        try
        {
            var capture = page.View.CaptureAsync(png);
            if (await Task.WhenAny(capture, Task.Delay(5000)) != capture) return "A picture of the page was not taken within 5 s.";
            await capture;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            return $"A picture of the page failed: {ex.GetType().Name} 0x{ex.HResult:X8}.";
        }
        if (file is not null) File.WriteAllBytes(file, png.ToArray());
        return $"A picture of the page: {png.Length} bytes of PNG.";
    }

    private static double Percentile(List<double> sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Count) - 1, 0, sorted.Count - 1)];

    private sealed class RepeatPage(string kind, nint parent, FileContentSource source, WebView2Page view, HtmlPage page)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public string Kind => kind;
        public WebView2Page View => view;
        public HtmlPage Page => page;
        public List<(bool Ok, string? Why, double Ms)> Reports { get; } = [];
        public TaskCompletionSource Completion { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Report(bool ok, string? why)
        {
            Reports.Add((ok, why, _clock.Elapsed.TotalMilliseconds));
            Completion.TrySetResult();
        }

        public void Again()
        {
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _clock.Restart();
        }

        public void Close()
        {
            view.Dispose();
            source.Dispose();
            StaPump.DestroyWindow(parent);
        }
    }
}
