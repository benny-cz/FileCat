using System.Diagnostics;
using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Resources;

// The page engine against a test page: its title (a script would change it), the file beside it served, the file
// outside its folder refused, and the web never asked. Exits 0 when all held.
if (!OperatingSystem.IsMacOS())
{
    Console.WriteLine("WKWebView is macOS's; nothing to do here.");
    return 0;
}
if (MacPageEngine.Unavailable is { } why)
{
    Console.WriteLine("FAIL: " + why);
    return 1;
}
string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-page-smoke", Guid.NewGuid().ToString("N"))).FullName;
try
{
    const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    string site = Directory.CreateDirectory(Path.Combine(root, "site")).FullName;
    File.WriteAllBytes(Path.Combine(root, "outside.png"), Convert.FromBase64String(png));
    File.WriteAllBytes(Path.Combine(site, "pic.png"), Convert.FromBase64String(png));
    File.WriteAllText(Path.Combine(site, "index.html"), """
        <!doctype html><html><head><meta charset="utf-8"><title>FileCat test page</title></head>
        <body><h1>Drawn by WKWebView</h1>
        <img src="pic.png"><img src="../outside.png"><img src="https://example.com/remote.png">
        <script>document.title = 'SCRIPT RAN';</script>
        </body></html>
        """);
    MacPageEngine.StartApplication();
    var engine = MacPageEngine.Create(Path.Combine(root, "data"), out string? unavailable);
    if (engine is null)
    {
        Console.WriteLine("FAIL: " + unavailable);
        return 1;
    }
    engine.ShowInWindow();
    bool? loaded = null;
    string? reason = null;
    engine.Loaded += (ok, why) => (loaded, reason) = (ok, why);
    engine.Show(new HtmlPage(new FileContentSource(Path.Combine(site, "index.html")), "index.html"));
    var clock = Stopwatch.StartNew();
    while (loaded is null && clock.Elapsed < TimeSpan.FromSeconds(60)) MacPageEngine.RunLoop(TimeSpan.FromMilliseconds(100));
    // The refused request and the title can both come after the navigation finished (the title is observed).
    var settle = Stopwatch.StartNew();
    while ((engine.BlockedCount < 1 || string.IsNullOrEmpty(engine.Title)) && settle.Elapsed < TimeSpan.FromSeconds(10)) MacPageEngine.RunLoop(TimeSpan.FromMilliseconds(100));
    Console.WriteLine($"loaded: {loaded} ({reason}); title: {engine.Title}; refused: {engine.BlockedCount}");
    bool passed = loaded == true && engine.Title == "FileCat test page" && engine.BlockedCount >= 1;
    engine.Dispose();

    // Release issue I25: a Markdown file drawn (its title is the file's name, its script text), the picture beside it
    // loaded, and the picture on the web never requested.
    string readme = Path.Combine(site, "README.md");
    File.WriteAllText(readme, "# Notes\n\n![local](pic.png) ![remote](https://example.com/remote.png)\n\n<script>document.title = 'SCRIPT RAN';</script>\n");
    var markdown = MacPageEngine.Create(Path.Combine(root, "data-md"), out unavailable);
    if (markdown is null)
    {
        Console.WriteLine("FAIL: " + unavailable);
        return 1;
    }
    markdown.ShowInWindow();
    loaded = null;
    markdown.Loaded += (ok, why) => (loaded, reason) = (ok, why);
    markdown.Show(HtmlPage.ForMarkdown(new FileContentSource(readme), readme));
    clock.Restart();
    while (loaded is null && clock.Elapsed < TimeSpan.FromSeconds(60)) MacPageEngine.RunLoop(TimeSpan.FromMilliseconds(100));
    settle.Restart();
    while (settle.Elapsed < TimeSpan.FromSeconds(2) || string.IsNullOrEmpty(markdown.Title) && settle.Elapsed < TimeSpan.FromSeconds(10)) MacPageEngine.RunLoop(TimeSpan.FromMilliseconds(100));
    Console.WriteLine($"markdown loaded: {loaded} ({reason}); title: {markdown.Title}; refused: {markdown.BlockedCount}");
    passed &= loaded == true && markdown.Title == "README.md" && markdown.BlockedCount == 0;
    markdown.Dispose();
    Console.WriteLine(passed ? "PASS" : "FAIL");
    return passed ? 0 : 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch (IOException) { }
}
