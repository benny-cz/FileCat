using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using Xunit;

namespace FileCat.Core.Tests;

/// <summary>Actual local resource requests from rendered Markdown, with owned file and directory links.</summary>
public sealed class MarkdownResourceIntegrationTests(ITestOutputHelper output)
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("ascii", "ascii.png", "ascii.png")]
    [InlineData("space", "screen shot.png", "screen shot.png")]
    [InlineData("bmp", "obrázek.png", "obrázek.png")]
    [InlineData("emoji", "photo📷.png", "photo📷.png")]
    [InlineData("astral-letter", "𐐀.png", "𐐀.png")]
    [InlineData("nested-unicode", "docs/图📷 x.png", "docs/图📷 x.png")]
    [InlineData("already-encoded", "photo📷.png", "photo%F0%9F%93%B7.png")]
    [InlineData("literal-percent", "literal%name.png", "literal%25name.png")]
    public void Rendered_picture_requests_preserve_local_unicode_identity(string mode, string name, string destination)
    {
        using var fixture = new Fixture();
        string image = Path.Combine(fixture.Site, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllBytes(image, Png);
        byte[] markdown = Encoding.UTF8.GetBytes($"# Owned {mode}\n\n![Owned](<{destination}>)\n");
        File.WriteAllBytes(fixture.Main, markdown);
        using var reader = new PagedReader(new FileContentSource(fixture.Main));
        var page = HtmlPage.ForMarkdown(reader, fixture.Main);
        var served = page.Resolve("/");
        Assert.NotNull(served);
        string html = Encoding.UTF8.GetString(served.Value.Bytes);
        string src = WebUtility.HtmlDecode(Regex.Match(html, "<img src=\"([^\"]*)\"").Groups[1].Value);
        string expected = string.Join('/', name.Split('/').Select(Uri.EscapeDataString));
        string request = new Uri(new Uri(page.Address), src).AbsolutePath;
        var resource = page.Resolve(request);
        output.WriteLine(JsonSerializer.Serialize(new { Group = "markdown-resource-integration", Case = mode,
            ActualOwnedFixtureRoot = fixture.Root, ActualMarkdown = Convert.ToBase64String(markdown), ActualRenderedHtml = html,
            ActualImageUrl = src, IndependentlyEscapedExpectedUrl = expected, ActualRequestPath = request,
            ActualResolved = resource is not null, ActualReturnedBytes = resource?.Bytes.Length,
            ActualReturnedSHA256 = Hash(resource?.Bytes), ActualExpectedPNGBase64 = Convert.ToBase64String(Png),
            ActualPNGUnchanged = File.ReadAllBytes(image).SequenceEqual(Png),
            ActualProductionFileSourcePagedReaderMarkdownAndHtmlPage = true, NoNativeBrowserUiOrNetwork = true }));
        Assert.Equal(expected, src);
        Assert.NotNull(resource);
        Assert.Equal("image/png", resource.Value.MimeType);
        Assert.Equal(Png, resource.Value.Bytes);
    }

    [Theory]
    [InlineData("plain-inside", "/inside/local.png", true, false)]
    [InlineData("final-file-outside", "/file-out.png", false, false)]
    [InlineData("intermediate-directory-outside", "/directory-out/outside.png", false, false)]
    [InlineData("two-hop-directory-outside", "/chain/outside.png", false, false)]
    [InlineData("intermediate-directory-inside", "/directory-in/local.png", true, false)]
    [InlineData("escaped-directory-outside", "/%64irectory-out/outside.png", false, false)]
    [InlineData("final-file-inside", "/file-in.png", true, false)]
    [InlineData("link-target-intermediate-outside", "/target-parent/outside.png", false, false)]
    [InlineData("relative-directory-inside", "/relative-in/local.png", true, false)]
    [InlineData("relative-directory-outside", "/relative-out/outside.png", false, false)]
    [InlineData("cyclic-directory", "/loop/local.png", false, false)]
    [InlineData("linked-page-root-inside", "/inside/local.png", true, true)]
    [InlineData("linked-page-root-outside", "/directory-out/outside.png", false, true)]
    [InlineData("linked-page-root-internal-link", "/directory-in/local.png", true, true)]
    public void Page_resources_follow_internal_links_and_refuse_resolved_outside_targets(string mode, string request, bool expected, bool alias)
    {
        using var fixture = new Fixture();
        fixture.MakeLinks();
        string main = alias ? Path.Combine(fixture.Alias, "README.md") : fixture.Main;
        using var reader = new PagedReader(new FileContentSource(main));
        var page = HtmlPage.ForMarkdown(reader, main);
        Assert.NotNull(page.Resolve("/"));
        string actualTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(main)!, Uri.UnescapeDataString(request).TrimStart('/')));
        bool targetBytesConfirmed = mode != "cyclic-directory" && File.ReadAllBytes(actualTarget).SequenceEqual(Png);
        if (mode != "cyclic-directory") Assert.True(targetBytesConfirmed);
        var resource = page.Resolve(request);
        output.WriteLine(JsonSerializer.Serialize(new { Group = "page-resource-directory-boundary", Case = mode,
            ActualOwnedFixtureRoot = fixture.Root, ActualPageLocalPath = main, ActualRequestPath = request,
            ExpectedResolved = expected, ActualResolved = resource is not null,
            ActualReturnedBytes = resource?.Bytes.Length, ActualReturnedSHA256 = Hash(resource?.Bytes),
            ActualExpectedPNGBase64 = Convert.ToBase64String(Png), ActualLinks = fixture.LinkPins(),
            ActualTargetBytesConfirmedBeforeResolve = targetBytesConfirmed, ActualTargetPath = actualTarget,
            ActualOnlyOwnedLocalTargets = true, NoNativeBrowserUiOrNetwork = true }));
        Assert.Equal(expected, resource is not null);
        if (expected) Assert.Equal(Png, resource!.Value.Bytes);
    }

    [Fact]
    public void An_internal_file_link_keeps_the_requested_resource_mime_type()
    {
        using var fixture = new Fixture();
        fixture.MakeMimeLink();
        using var reader = new PagedReader(new FileContentSource(fixture.Main));
        var page = HtmlPage.ForMarkdown(reader, fixture.Main);
        byte[] expected = Encoding.UTF8.GetBytes("body { color: #123456; }\n");
        var resource = page.Resolve("/style.css");
        output.WriteLine(JsonSerializer.Serialize(new { Group = "page-resource-link-mime", Case = "internal-css-to-bin",
            ActualOwnedFixtureRoot = fixture.Root, ActualRequestPath = "/style.css", ActualLinks = fixture.LinkPins(),
            ActualResolved = resource is not null, ActualMimeType = resource?.MimeType,
            ActualReturnedBytes = resource?.Bytes.Length, ActualReturnedSHA256 = Hash(resource?.Bytes),
            ActualExpectedBytesBase64 = Convert.ToBase64String(expected), NoNativeBrowserUiOrNetwork = true }));
        Assert.NotNull(resource);
        Assert.Equal("text/css", resource.Value.MimeType);
        Assert.Equal(expected, resource.Value.Bytes);
    }

    private static string? Hash(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filecat-markdown-integration", Guid.NewGuid().ToString("N"));
        public string Site => Path.Combine(Root, "site");
        public string Main => Path.Combine(Site, "README.md");
        public string Alias => Path.Combine(Root, "site-alias");
        private readonly List<(string Path, bool Directory)> _links = [];
        public Fixture()
        {
            Directory.CreateDirectory(Site);
            File.WriteAllText(Main, "# Owned resources\n", Encoding.UTF8);
        }
        public void MakeLinks()
        {
            string inside = Directory.CreateDirectory(Path.Combine(Site, "inside")).FullName;
            string outside = Directory.CreateDirectory(Path.Combine(Root, "outside")).FullName;
            string deep = Directory.CreateDirectory(Path.Combine(outside, "deep")).FullName;
            File.WriteAllBytes(Path.Combine(inside, "local.png"), Png);
            File.WriteAllBytes(Path.Combine(outside, "outside.png"), Png);
            File.WriteAllBytes(Path.Combine(deep, "outside.png"), Png);
            try
            {
                FileLink("file-out.png", Path.Combine(outside, "outside.png"));
                FileLink("file-in.png", Path.Combine(inside, "local.png"));
                DirLink(Path.Combine(Site, "directory-out"), outside);
                DirLink(Path.Combine(Site, "directory-in"), inside);
                DirLink(Path.Combine(Site, "chain"), Path.Combine(Site, "directory-out"));
                DirLink(Path.Combine(Site, "target-parent"), Path.Combine(Site, "directory-out", "deep"));
                DirLink(Path.Combine(Site, "relative-in"), "inside");
                DirLink(Path.Combine(Site, "relative-out"), Path.Combine("..", "outside"));
                DirLink(Path.Combine(Site, "loop"), Path.Combine(Site, "loop"));
                DirLink(Alias, Site);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { Assert.Skip("Owned symbolic links are unavailable: " + ex.GetType().Name + ": " + ex.Message); }
        }
        public void MakeMimeLink()
        {
            string inside = Directory.CreateDirectory(Path.Combine(Site, "inside")).FullName;
            string target = Path.Combine(inside, "data.bin");
            File.WriteAllBytes(target, Encoding.UTF8.GetBytes("body { color: #123456; }\n"));
            try { FileLink("style.css", target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { Assert.Skip("Owned symbolic links are unavailable: " + ex.GetType().Name + ": " + ex.Message); }
        }
        private void FileLink(string name, string target)
        { string path = Path.Combine(Site, name); File.CreateSymbolicLink(path, target); _links.Add((path, false)); }
        private void DirLink(string path, string target)
        { Directory.CreateSymbolicLink(path, target); _links.Add((path, true)); }
        public object[] LinkPins() => _links.Select(link => (object)new { link.Path,
            Target = link.Directory ? new DirectoryInfo(link.Path).LinkTarget : new FileInfo(link.Path).LinkTarget }).ToArray();
        public void Dispose()
        {
            // Remove link objects first; never traverse their targets during fixture retirement.
            foreach (var link in _links.AsEnumerable().Reverse())
                if (link.Directory) Directory.Delete(link.Path); else File.Delete(link.Path);
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
