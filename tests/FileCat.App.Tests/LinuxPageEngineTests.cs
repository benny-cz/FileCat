using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// D-51 on Linux: WebKitGTK behind the viewer's page view. Needs an X display and WebKitGTK; CI installs both and sets
/// FILECAT_REQUIRE_WEBKIT so that a missing engine fails instead of skipping.
/// </summary>
public sealed class LinuxPageEngineTests
{
    [Theory]
    [InlineData("1\n", "unconfined\n", true)]
    [InlineData("1", null, true)]
    [InlineData("0", "unconfined\n", false)]
    [InlineData("1", "filecat (unconfined)\n", false)]
    [InlineData("1", "docker-default (enforce)\n", false)]
    public void Restricted_AppArmor_requires_a_named_application_profile(string restriction, string? profile, bool required)
    {
        Assert.Equal(required, LinuxPageEngine.NeedsAppArmorUserNamespaceProfile(restriction, profile));
    }

    [Fact]
    public void Pages_are_refused_where_WebKit_would_start_them_without_its_sandbox()
    {
        // Review finding R-M7: FileCat read back the sandbox flag it had set, which WebKitGTK reports as enabled also where
        // it starts the page process without bubblewrap.
        static string? Here(string[] files, params (string Name, string Value)[] variables) =>
            LinuxPageEngine.SandboxSkippedHere(files.Contains, name => variables.FirstOrDefault(v => v.Name == name).Value);
        Assert.Null(Here([]));
        Assert.Contains("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", Here([], ("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "1")));
        Assert.Null(Here([], ("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "0")));
        Assert.Contains("Docker", Here(["/.dockerenv"]));
        Assert.Contains("Snap", Here([], ("SNAP", "/snap/filecat/1"), ("SNAP_NAME", "filecat"), ("SNAP_REVISION", "1")));
        Assert.Null(Here([], ("SNAP", "/home/user/snap")));
        Assert.Null(Here(["/.flatpak-info"]));
    }

    [Fact]
    public async Task WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("WebKitGTK is Linux's.");
            return;
        }
        bool required = Environment.GetEnvironmentVariable("FILECAT_REQUIRE_WEBKIT") == "1";
        if (!required && Environment.GetEnvironmentVariable("DISPLAY") is null)
        {
            Assert.Skip("No X display.");
            return;
        }
        if (LinuxPageEngine.Unavailable is { } why)
        {
            if (required) Assert.Fail(why);
            Assert.Skip(why);
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "site")).FullName;
            File.WriteAllBytes(Path.Combine(root, "outside.png"), Convert.FromBase64String(Png));
            File.WriteAllBytes(Path.Combine(folder, "pic.png"), Convert.FromBase64String(Png));
            File.WriteAllText(Path.Combine(folder, "index.html"), """
                <!doctype html><html><head><meta charset="utf-8"><title>FileCat test page</title></head>
                <body><h1>Drawn by WebKitGTK</h1>
                <img src="pic.png"><img src="../outside.png"><img src="https://example.com/remote.png">
                <script>document.title = 'SCRIPT RAN';</script>
                </body></html>
                """);
            var page = new HtmlPage(new FileContentSource(Path.Combine(folder, "index.html")), "index.html");
            using var engine = LinuxPageEngine.Create(offscreen: true, out string? unavailable);
            Assert.True(engine is not null, unavailable);
            Assert.True(engine.SandboxEnabled, "WebKitGTK must report its native web-process sandbox before FileCat loads the page.");
            var loaded = new TaskCompletionSource<(bool Ok, string? Why)>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Loaded += (ok, reason) => loaded.TrySetResult((ok, reason));
            engine.Show(page);
            var (ok, reason) = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            Assert.True(ok, reason);

            // Its own title: the script that would have changed it did not run.
            for (int i = 0; i < 100 && engine.Title is null; i++) await Task.Delay(50, ct);
            Assert.Equal("FileCat test page", engine.Title);
            // The image from the web and the one outside the page's folder were refused; the one beside it was not.
            for (int i = 0; i < 100 && engine.BlockedCount < 2; i++) await Task.Delay(50, ct);
            Assert.Equal(2, engine.BlockedCount);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task WebKitGTK_draws_a_markdown_file_and_asks_the_web_for_nothing()
    {
        // Release issue I25 in WebKitGTK: the file drawn (its title is the file's name; its script is text), the picture
        // beside it loaded, and the picture on the web never requested.
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("WebKitGTK is Linux's.");
            return;
        }
        bool required = Environment.GetEnvironmentVariable("FILECAT_REQUIRE_WEBKIT") == "1";
        if (!required && Environment.GetEnvironmentVariable("DISPLAY") is null)
        {
            Assert.Skip("No X display.");
            return;
        }
        if (LinuxPageEngine.Unavailable is { } why)
        {
            if (required) Assert.Fail(why);
            Assert.Skip(why);
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(root, "pic.png"), Convert.FromBase64String(Png));
            string readme = Path.Combine(root, "README.md");
            File.WriteAllText(readme, "# Notes\n\n![local](pic.png) ![remote](https://example.com/remote.png)\n\n<script>document.title = 'SCRIPT RAN';</script>\n");
            using var engine = LinuxPageEngine.Create(offscreen: true, out string? unavailable);
            Assert.True(engine is not null, unavailable);
            var loaded = new TaskCompletionSource<(bool Ok, string? Why)>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Loaded += (ok, reason) => loaded.TrySetResult((ok, reason));
            engine.Show(HtmlPage.ForMarkdown(new FileContentSource(readme), readme));
            var (ok, reason) = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            Assert.True(ok, reason);
            for (int i = 0; i < 100 && engine.Title is null; i++) await Task.Delay(50, ct);
            Assert.Equal("README.md", engine.Title);
            await Task.Delay(1000, ct); // anything the page would still ask for
            Assert.Equal(0, engine.BlockedCount);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task WebKitGTK_finds_rendered_markdown_text_without_enabling_page_scripts()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("WebKitGTK is Linux's."); return; }
        bool required = Environment.GetEnvironmentVariable("FILECAT_REQUIRE_WEBKIT") == "1";
        if (!required && Environment.GetEnvironmentVariable("DISPLAY") is null) { Assert.Skip("No X display."); return; }
        if (LinuxPageEngine.Unavailable is { } why)
        {
            if (required) Assert.Fail(why);
            Assert.Skip(why);
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-search", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string path = Path.Combine(root, "README.md");
            File.WriteAllText(path, "# Search\n\nAlpha one. Alpha two.\n<script>document.title='SCRIPT RAN'</script>\n");
            using var source = new FileContentSource(path);
            using var engine = LinuxPageEngine.Create(offscreen: true, out string? unavailable);
            Assert.NotNull(engine);
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.Loaded += (ok, _) => loaded.TrySetResult(ok);
            engine.Show(HtmlPage.ForMarkdown(source, path));
            Assert.True(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60), ct));
            Assert.Equal(new PageFindResult(true, 2, true), await engine.FindAsync("Alpha", true, true, ct).WaitAsync(TimeSpan.FromSeconds(15), ct));
            Assert.Equal(new PageFindResult(true, 2, true), await engine.FindAsync("Alpha", true, true, ct).WaitAsync(TimeSpan.FromSeconds(15), ct));
            Assert.Equal(new PageFindResult(true, 2, true), await engine.FindAsync("Alpha", true, false, ct).WaitAsync(TimeSpan.FromSeconds(15), ct));
            Assert.Equal(new PageFindResult(false, 0, true), await engine.FindAsync("absent phrase", false, true, ct).WaitAsync(TimeSpan.FromSeconds(15), ct));
            Assert.Equal("README.md", engine.Title);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void WebKit_match_limit_is_never_displayed_as_an_exact_total()
    {
        Assert.Equal(new PageFindResult(true, 1025, false), LinuxPageEngine.FindResultFromCount(uint.MaxValue));
        Assert.Equal(new PageFindResult(true, 1024, true), LinuxPageEngine.FindResultFromCount(1024));
        Assert.Equal(new PageFindResult(true), LinuxPageEngine.FindResultFromCount(0));
    }

    [Fact]
    public void The_page_is_sized_in_GTK_units_at_its_scale()
    {
        // At 200% the viewer's 1,040 by 791 pixels are 520 by 396 GTK units; given as units, GTK drew a 2,080 by
        // 1,582 pixel page whose prose ran past the window's edges.
        Assert.Equal((520, 396), (LinuxPageEngine.ToGtkUnits(1040, 2), LinuxPageEngine.ToGtkUnits(791, 2)));
        Assert.Equal(1041, LinuxPageEngine.ToGtkUnits(1041, 1));
        Assert.Equal(347, LinuxPageEngine.ToGtkUnits(1040, 3));
        Assert.Equal(1040, LinuxPageEngine.ToGtkUnits(1040, 0));
    }

    /// <summary>A 1×1 PNG.</summary>
    private const string Png ="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
}
