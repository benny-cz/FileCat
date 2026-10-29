using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Html;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// The viewer's page view on Windows (D-51), for real: WebView2 in a hidden window on a thread of its own. The page shows
/// with its own title (its script does not run), the image beside it is served, and the web and files outside its folder
/// are refused.
/// </summary>
public sealed class PageViewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-page-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        // The browser's processes let go of their folder shortly after the view closes.
        for (int i = 0; i < 20; i++)
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
                return;
            }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }

    [Fact]
    public void Clicking_a_local_markdown_link_requests_another_viewer_without_navigating_the_page()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "markdown-links")).FullName;
        string current = Path.Combine(site, "index.md");
        string linked = Path.Combine(site, "next.md");
        File.WriteAllText(current, "# Index\n\n[Next](next.md) [Missing](missing.md)");
        File.WriteAllText(linked, "# Next");
        var requests = new ConcurrentQueue<string>();
        var served = new ConcurrentQueue<string>();
        var completions = new ConcurrentQueue<(bool Ok, string? Why)>();
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(current);
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview-markdown"));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.LinkedMarkdown += (_, path) => requests.Enqueue(path);
            view.Served += (path, found) => { if (found) served.Enqueue(path); };
            view.Loaded += (ok, why) =>
            {
                completions.Enqueue((ok, why));
                if (ok) loaded.TrySetResult();
                else loaded.TrySetException(new InvalidOperationException(why));
            };
            view.Show(HtmlPage.ForMarkdown(source, current));
            await loaded.Task;
            await view.ClickLinkForTestsAsync(0);
            for (int i = 0; i < 100 && requests.IsEmpty; i++) await Task.Delay(50);
            Assert.Equal(new[] { linked }, requests.ToArray());
            Assert.DoesNotContain("/next.md", served);
            await view.ClickLinkForTestsAsync(1);
            for (int i = 0; i < 100 && view.BlockedCount == 0; i++) await Task.Delay(50);
            Assert.Equal(1, view.BlockedCount);
            Assert.Equal(new[] { linked }, requests.ToArray());
            Assert.DoesNotContain("/missing.md", served);
            await Task.Delay(200);
            Assert.DoesNotContain(completions, result => !result.Ok);
            Assert.Equal("index.md", view.Title);
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The rest of what a page can reach for (release issue I25): a style sheet, a web font, a frame and a
    /// navigation of its own, each naming the web. The engine must refuse every one of them, and the page still
    /// draws with its own title.
    /// </summary>
    [Fact]
    public void A_page_asks_the_web_for_styles_fonts_frames_and_its_next_page_in_vain()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "remote-site")).FullName;
        File.WriteAllText(Path.Combine(site, "page.html"),
            "<!doctype html><html><head><title>Own title</title>" +
            "<link rel=stylesheet href=\"https://styles.example.com/theme.css\">" +
            "<style>@font-face { font-family: Far; src: url(https://fonts.example.com/far.woff2); } body { font-family: Far }</style>" +
            "<meta http-equiv=\"refresh\" content=\"0; url=https://next.example.com/elsewhere.html\">" +
            "</head><body><p>Styled from afar.</p>" +
            "<iframe src=\"https://frames.example.com/inner.html\"></iframe>" +
            "<img src=\"https://pictures.example.com/remote.png\">" +
            "</body></html>");

        var blocked = new ConcurrentQueue<string>();
        var served = new ConcurrentQueue<(string Path, bool Found)>();
        var completions = new ConcurrentQueue<(bool Ok, string? Why)>();
        string? title = null;
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(Path.Combine(site, "page.html"));
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview-remote"));
            var done = new TaskCompletionSource();
            view.Served += (path, found) => served.Enqueue((path, found));
            view.Blocked += blocked.Enqueue;
            // The refused refresh completes a navigation of its own, so every completion is kept in order.
            view.Loaded += (ok, why) =>
            {
                completions.Enqueue((ok, why));
                done.TrySetResult();
            };
            view.SetSize(640, 480);
            view.Show(new HtmlPage(source, Path.Combine(site, "page.html")));
            await done.Task;
            // A style sheet, a font, a frame and the refresh all arrive after the document reports itself loaded,
            // and the refresh is the last of them: wait for it by name, not for a count.
            for (int i = 0; i < 300 && !(blocked.Count >= 4 && blocked.Any(b => b.Contains("next.example.com", StringComparison.Ordinal))); i++)
                await Task.Delay(50);
            title = view.Title;
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(90));

        TestContext.Current.TestOutputHelper?.WriteLine("Refused: " + string.Join(", ", blocked));
        TestContext.Current.TestOutputHelper?.WriteLine("Navigations: " + string.Join(", ", completions.Select(c => $"{c.Ok}/{c.Why}")));
        Assert.True(completions.TryPeek(out var first) && first.Ok, $"The page itself did not load: {first.Why}");
        // Any later completion is the refused refresh, which the view cancels rather than follows.
        Assert.All(completions.Skip(1), c => Assert.False(c.Ok, "a navigation away from the page succeeded"));
        // The page is still its own: the refresh did not take it anywhere.
        Assert.Equal("Own title", title);
        Assert.Contains(served, s => s.Path == "/page.html" && s.Found);
        foreach (var host in new[] { "styles.example.com", "fonts.example.com", "frames.example.com", "pictures.example.com" })
            Assert.Contains(blocked, b => b.Contains(host, StringComparison.Ordinal));
        // The navigation the page asked for is refused as well, by address.
        Assert.Contains(blocked, b => b.Contains("next.example.com", StringComparison.Ordinal));
        Assert.DoesNotContain(served, s => s.Found && s.Path.Contains("example.com", StringComparison.Ordinal));
    }

    /// <summary>
    /// A page may not save a file to disk. Scripts are off, so the page asks the only way left to it: it refreshes
    /// to a file beside it that is served as a download. The view cancels it, and nothing is written.
    /// </summary>
    [Fact]
    public void A_page_that_sends_itself_a_download_saves_nothing()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "download-site")).FullName;
        File.WriteAllBytes(Path.Combine(site, "payload.bin"), [1, 2, 3, 4, 5, 6, 7, 8]);
        File.WriteAllText(Path.Combine(site, "page.html"),
            "<!doctype html><html><head><title>Saves nothing</title>" +
            "<meta http-equiv=\"refresh\" content=\"0; url=payload.bin\"></head><body><p>No.</p></body></html>");
        string downloads = Directory.CreateDirectory(Path.Combine(_dir, "downloads")).FullName;

        var served = new ConcurrentQueue<(string Path, bool Found)>();
        var completions = new ConcurrentQueue<(bool Ok, string? Why)>();
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(Path.Combine(site, "page.html"));
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview-download"));
            var done = new TaskCompletionSource();
            view.Served += (path, found) => served.Enqueue((path, found));
            view.Loaded += (ok, why) =>
            {
                completions.Enqueue((ok, why));
                done.TrySetResult();
            };
            view.SetSize(640, 480);
            view.Show(new HtmlPage(source, Path.Combine(site, "page.html")));
            await done.Task;
            // The refresh, the request for the file and the cancelled download all follow the first completion.
            for (int i = 0; i < 200 && !served.Any(s => s.Path == "/payload.bin"); i++) await Task.Delay(50);
            await Task.Delay(500);
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(90));

        TestContext.Current.TestOutputHelper?.WriteLine("Served: " + string.Join(", ", served.Select(s => $"{s.Path}={s.Found}")));
        TestContext.Current.TestOutputHelper?.WriteLine("Navigations: " + string.Join(", ", completions.Select(c => $"{c.Ok}/{c.Why}")));
        Assert.True(completions.TryPeek(out var first) && first.Ok, $"The page itself did not load: {first.Why}");
        // The file was asked for as the page's own resource; what must not happen is a file appearing on disk.
        foreach (var folder in new[] { downloads, _dir, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") })
            if (Directory.Exists(folder))
                Assert.DoesNotContain("payload.bin", Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_page_shows_without_its_scripts_and_gets_nothing_from_outside_its_folder()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "site")).FullName;
        File.WriteAllText(Path.Combine(site, "page.html"),
            "<!doctype html><html><head><title>Plain title</title><link rel=stylesheet href=\"../outside.css\"></head>" +
            "<body><script>document.title = 'the script ran';</script><img src=\"pic.png\"><img src=\"https://example.com/remote.png\"></body></html>");
        File.WriteAllBytes(Path.Combine(site, "pic.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        File.WriteAllText(Path.Combine(_dir, "outside.css"), "body { color: red }");

        var served = new ConcurrentQueue<(string Path, bool Found)>();
        var blocked = new ConcurrentQueue<string>();
        string? title = null;
        (bool Ok, string? Why)? loaded = null;
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(Path.Combine(site, "page.html"));
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview"));
            var done = new TaskCompletionSource();
            view.Served += (path, found) => served.Enqueue((path, found));
            view.Blocked += blocked.Enqueue;
            view.Loaded += (ok, why) =>
            {
                loaded = (ok, why);
                done.TrySetResult();
            };
            view.SetSize(640, 480);
            view.Show(new HtmlPage(source, Path.Combine(site, "page.html")));
            await done.Task;
            // Requests for images may finish just after the page reports it loaded.
            for (int i = 0; i < 100 && !(served.Any(s => s.Path == "/pic.png") && blocked.Any(b => b.Contains("example.com", StringComparison.Ordinal))); i++) await Task.Delay(50);
            title = view.Title;
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(60));

        Assert.True(loaded is { Ok: true }, $"The page did not load: {loaded?.Why}");
        Assert.Equal("Plain title", title);
        Assert.Contains(served, s => s.Path == "/page.html" && s.Found);
        Assert.Contains(served, s => s.Path == "/pic.png" && s.Found);
        // "../outside.css" cannot climb above the page's folder: it asks for /outside.css there, which is not found.
        Assert.Contains(served, s => s.Path == "/outside.css" && !s.Found);
        Assert.Contains(blocked, b => b.StartsWith("https://example.com/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_markdown_file_is_drawn_with_its_pictures_and_asks_the_web_for_nothing()
    {
        // Release issue I25 in the real engine: the file drawn (its title is the file's name, its script is text), the
        // picture beside it served, and the picture on the web never requested. FILECAT_PAGE_CAPTURE keeps a picture of it.
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "notes")).FullName;
        string readme = Path.Combine(site, "README.md");
        File.WriteAllText(readme, """
            # FileCat notes

            Some *emphasis*, **strong** text, `code`, and a [link](https://example.com "Example").

            - [x] done
            - [ ] to do
              1. nested
              2. list

            > A quote with **bold**.

            | Name | Size |
            |:-----|-----:|
            | a.txt | 1 KB |
            | Žluťoučký kůň.txt | 12 MB |

            ```cs
            var x = "<tag>"; // highlighted as code
            ```

            ![local](pic.png) ![remote](https://tracker.example/pixel.gif)

            <script>document.title = 'the script ran';</script>
            """);
        File.WriteAllBytes(Path.Combine(site, "pic.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

        var served = new ConcurrentQueue<(string Path, bool Found)>();
        var blocked = new ConcurrentQueue<string>();
        string? title = null;
        (bool Ok, string? Why)? loaded = null;
        string? capture = Environment.GetEnvironmentVariable("FILECAT_PAGE_CAPTURE");
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(readme);
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview"));
            var done = new TaskCompletionSource();
            view.Served += (path, found) => served.Enqueue((path, found));
            view.Blocked += blocked.Enqueue;
            view.Loaded += (ok, why) =>
            {
                loaded = (ok, why);
                done.TrySetResult();
            };
            view.SetSize(900, 1000);
            view.Show(HtmlPage.ForMarkdown(source, readme));
            await done.Task;
            for (int i = 0; i < 100 && !served.Any(s => s.Path == "/pic.png"); i++) await Task.Delay(50);
            await Task.Delay(500); // anything else the page would ask for
            title = view.Title;
            if (!string.IsNullOrEmpty(capture))
                await using (var png = File.Create(capture)) await view.CaptureAsync(png);
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(60));

        Assert.True(loaded is { Ok: true }, $"The page did not load: {loaded?.Why}");
        Assert.Equal("README.md", title);
        Assert.Contains(served, s => s.Path == "/README.md" && s.Found);
        Assert.Contains(served, s => s.Path == "/pic.png" && s.Found);
        Assert.Empty(blocked);
        Assert.DoesNotContain(served, s => s.Path.Contains("pixel", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_page_search_uses_rendered_text_with_page_scripts_disabled()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "search")).FullName;
        string path = Path.Combine(site, "search.md");
        File.WriteAllText(path, "# Search\n\nAlpha first. Alpha second.\n\n`\");document.title='pwn'//`\n<script>document.title='page script ran'</script>\n");
        bool? first = null, second = null, previous = null, absent = null, quoted = null;
        string? title = null;
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            using var source = new FileContentSource(path);
            using var view = new WebView2Page(parent, Path.Combine(_dir, "webview-search"));
            var done = new TaskCompletionSource<bool>();
            view.Loaded += (ok, _) => done.TrySetResult(ok);
            view.SetSize(800, 600);
            view.Show(HtmlPage.ForMarkdown(source, path));
            Assert.True(await done.Task);
            first = await view.FindAsync("Alpha", true, true, CancellationToken.None);
            second = await view.FindAsync("Alpha", true, true, CancellationToken.None);
            previous = await view.FindAsync("Alpha", true, false, CancellationToken.None);
            absent = await view.FindAsync("not present", false, true, CancellationToken.None);
            quoted = await view.FindAsync("\");document.title='pwn'//", true, true, CancellationToken.None);
            title = view.Title;
            StaPump.DestroyWindow(parent);
        }, TimeSpan.FromSeconds(60));
        Assert.True(first);
        Assert.True(second);
        Assert.True(previous);
        Assert.False(absent);
        Assert.True(quoted);
        Assert.Equal("search.md", title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_loaded_page_keeps_its_document_until_explicit_navigation(bool markdown)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("WebView2 is Windows'.");
        if (WebView2Page.RuntimeVersion is null) Assert.Skip("The WebView2 runtime is not installed here.");
        string site = Directory.CreateDirectory(Path.Combine(_dir, "revision")).FullName;
        string path = Path.Combine(site, markdown ? "owned.md" : "owned.html");
        byte[] original = RevisionDocument(markdown, false), replacement = RevisionDocument(markdown, true);
        File.WriteAllBytes(path, original);
        string? originalTitle = null, cachedTitle = null, refreshedTitle = null;
        byte[] before = [], cached = [], refreshed = [];
        int beforeRequests = -1, cachedRequests = -1, refreshedRequests = -1;
        var served = new ConcurrentQueue<(string Path, bool Found)>();
        ContentRevision? beforeRevision = null, afterRevision = null;
        StaPump.Run(async () =>
        {
            nint parent = StaPump.CreateHiddenWindow();
            try
            {
                using var source = new FileContentSource(path);
                using var reader = new PagedReader(source);
                using var view = new WebView2Page(parent, Path.Combine(_dir, "webview-revision"));
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.Served += (name, found) => served.Enqueue((name, found));
                view.Loaded += (ok, why) =>
                {
                    if (ok) done.TrySetResult();
                    else done.TrySetException(new IOException(why ?? "Owned page failed"));
                };
                view.SetSize(640, 480);
                var page = markdown ? HtmlPage.ForMarkdown(reader, path) : new HtmlPage(reader, path);
                beforeRevision = reader.Revision;
                view.Show(page);
                await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Task.Delay(200);
                before = await RevisionCapture(view);
                originalTitle = view.Title;
                beforeRequests = served.Count(s => s.Path == "/" + page.Name && s.Found);
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, new DateTime(beforeRevision!.Value.ModifiedTicks, DateTimeKind.Utc).AddSeconds(10));
                Assert.True(await Task.Run(() => reader.Refresh()));
                afterRevision = reader.Revision;
                await Task.Delay(200);
                cached = await RevisionCapture(view);
                cachedTitle = view.Title;
                cachedRequests = served.Count(s => s.Path == "/" + page.Name && s.Found);
                Assert.Equal(replacement, File.ReadAllBytes(path));
                done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.Show(page);
                await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await Task.Delay(200);
                refreshed = await RevisionCapture(view);
                refreshedTitle = view.Title;
                refreshedRequests = served.Count(s => s.Path == "/" + page.Name && s.Found);
            }
            finally { Assert.True(StaPump.DestroyWindow(parent)); }
        }, TimeSpan.FromSeconds(60));
        TestContext.Current.TestOutputHelper!.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            markdown, NativeWebView2Runtime = WebView2Page.RuntimeVersion,
            originalTitle, cachedTitle, refreshedTitle, beforeRequests, cachedRequests, refreshedRequests,
            beforeRevision, afterRevision, OriginalBase64 = Convert.ToBase64String(original),
            ReplacementBase64 = Convert.ToBase64String(replacement),
            BeforePNGSHA256 = RevisionHash(before), CachedPNGSHA256 = RevisionHash(cached), RefreshedPNGSHA256 = RevisionHash(refreshed),
            BeforePNGBase64 = Convert.ToBase64String(before), CachedPNGBase64 = Convert.ToBase64String(cached), RefreshedPNGBase64 = Convert.ToBase64String(refreshed),
            ActualSourceSHA256 = RevisionHash(File.ReadAllBytes(path)),
            ActualWindowsWebView2HtmlPageReaderOwnedHiddenWindow = true, NativeDesktopInputHumanReferenceOrCandidateQualified = false
        }));
        Assert.NotEqual(beforeRevision, afterRevision);
        Assert.Equal(1, beforeRequests);
        Assert.Equal(beforeRequests, cachedRequests);
        Assert.Equal(2, refreshedRequests);
        Assert.Equal(originalTitle, cachedTitle);
        Assert.Equal(markdown ? "owned.md" : "Original page marker", originalTitle);
        Assert.Equal(markdown ? "owned.md" : "Replacement page marker", refreshedTitle);
        Assert.Equal(before, cached);
        Assert.NotEqual(RevisionHash(before), RevisionHash(refreshed));
    }

    private static byte[] RevisionDocument(bool markdown, bool replacement) => System.Text.Encoding.UTF8.GetBytes(markdown
        ? replacement ? "# Replacement page marker\n\nNew owned content with a different length.\n" : "# Original page marker\n\nOwned original content.\n"
        : replacement ? "<!doctype html><html><head><title>Replacement page marker</title></head><body><h1>Replacement page marker</h1><p>New owned content with a different length.</p></body></html>"
        : "<!doctype html><html><head><title>Original page marker</title></head><body><h1>Original page marker</h1><p>Owned original content.</p></body></html>");

    private static string RevisionHash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    private static async Task<byte[]> RevisionCapture(WebView2Page view)
    {
        using var stream = new MemoryStream();
        await view.CaptureAsync(stream);
        return stream.ToArray();
    }

    /// <summary>A single-threaded apartment with a message pump and a synchronization context, as a UI thread has.</summary>
    private static class StaPump
    {
        public static void Run(Func<Task> body, TimeSpan timeout)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                var context = new PumpContext();
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var task = body();
                    var deadline = DateTime.UtcNow + timeout;
                    while (!task.IsCompleted)
                    {
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("The page view did not finish in time.");
                        context.RunPending();
                        while (PeekMessageW(out var message, 0, 0, 0, 1))
                        {
                            TranslateMessage(ref message);
                            DispatchMessageW(ref message);
                        }
                        MsgWaitForMultipleObjects(0, 0, false, 20, 0x04FF);
                    }
                    task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw new InvalidOperationException("The page view failed: " + error.Message, error);
        }

        public static nint CreateHiddenWindow()
        {
            nint hwnd = CreateWindowExW(0, "Static", "FileCat page test", 0x00CF0000, 0, 0, 800, 600, 0, 0, 0, 0);
            Assert.NotEqual(0, hwnd);
            return hwnd;
        }

        private sealed class PumpContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

            public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

            public void RunPending()
            {
                while (_queue.TryDequeue(out var item)) item.Callback(item.State);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public nint hwnd;
            public uint message;
            public nint wParam;
            public nint lParam;
            public uint time;
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessageW(out MSG message, nint hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessageW(ref MSG message);

        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjects(uint count, nint handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds, uint wakeMask);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint hwnd);
    }
}
