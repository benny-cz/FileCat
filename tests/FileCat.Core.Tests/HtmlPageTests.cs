using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>The page view serves the page and the files in its own folder, and nothing else (D-51).</summary>
public sealed class HtmlPageTests
{
    [Fact]
    public void A_page_on_disk_gets_the_files_in_its_folder_and_nothing_outside_it()
    {
        using var root = new TempDir();
        string site = Directory.CreateDirectory(Path.Combine(root.Path, "site")).FullName;
        Directory.CreateDirectory(Path.Combine(site, "img"));
        File.WriteAllText(Path.Combine(site, "my page.html"), "<html><body><img src=img/a.png></body></html>");
        File.WriteAllBytes(Path.Combine(site, "img", "a.png"), [0x89, (byte)'P', (byte)'N', (byte)'G']);
        File.WriteAllText(Path.Combine(root.Path, "secret.txt"), "secret");
        using var source = new FileContentSource(Path.Combine(site, "my page.html"));
        var page = new HtmlPage(source, Path.Combine(site, "my page.html"));

        Assert.True(page.ServesFolder);
        Assert.Equal("https://filecat-page.example/my%20page.html", page.Address);
        Assert.Equal("text/html", page.Resolve("/my%20page.html")!.Value.MimeType);
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(page.Resolve("/my%20page.html")!.Value.Bytes));
        Assert.Equal(("image/png", 4), (page.Resolve("/img/a.png")!.Value.MimeType, page.Resolve("/img/a.png?v=2")!.Value.Bytes.Length));
        // Out of the folder, however it is written: refused.
        foreach (var escape in new[] { "/../secret.txt", "/%2E%2E/secret.txt", "/img/../../secret.txt", "/" + Uri.EscapeDataString(Path.Combine(root.Path, "secret.txt")) })
            Assert.Null(page.Resolve(escape));
        Assert.Null(page.Resolve("/missing.css"));
        Assert.Null(page.Resolve("/img"));
    }

    [Fact]
    public void A_closed_viewer_page_refuses_its_main_file_and_adjacent_files()
    {
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "index.html");
        File.WriteAllText(path, "<html></html>");
        File.WriteAllText(Path.Combine(root.Path, "style.css"), "body {}");
        using var reader = new PagedReader(new FileContentSource(path));
        var page = new HtmlPage(reader, path);
        Assert.NotNull(page.Resolve("/"));
        Assert.NotNull(page.Resolve("/style.css"));
        reader.Dispose();
        Assert.Null(page.Resolve("/"));
        Assert.Null(page.Resolve("/style.css"));
    }

    [Fact]
    public void A_canceled_markdown_page_refuses_further_requests_while_its_reader_is_open()
    {
        using var reader = new PagedReader(new MemoryContentSource("notes.md", Encoding.UTF8.GetBytes("# Notes")));
        using var cts = new CancellationTokenSource();
        var page = HtmlPage.ForMarkdown(reader, "notes.md", cts.Token);
        Assert.NotNull(page.Resolve("/"));
        cts.Cancel();
        Assert.Null(page.Resolve("/"));
        Assert.True(reader.Read(0, new byte[10]) > 0);
    }

    [Fact]
    public void A_link_inside_the_folder_that_leads_out_is_not_followed()
    {
        using var root = new TempDir();
        string site = Directory.CreateDirectory(Path.Combine(root.Path, "site")).FullName;
        File.WriteAllText(Path.Combine(site, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(root.Path, "secret.txt"), "secret");
        try { File.CreateSymbolicLink(Path.Combine(site, "link.txt"), Path.Combine(root.Path, "secret.txt")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Links cannot be made here: " + ex.Message); }
        using var source = new FileContentSource(Path.Combine(site, "index.html"));
        Assert.Null(new HtmlPage(source, "index.html").Resolve("/link.txt"));
    }

    [Fact]
    public void A_page_that_is_not_a_file_on_disk_gets_only_itself()
    {
        var page = new HtmlPage(new MemoryContentSource("inside.html", Encoding.UTF8.GetBytes("<html>archived</html>")), "archive.zip/docs/inside.html");
        Assert.False(page.ServesFolder);
        Assert.Equal("inside.html", page.Name);
        Assert.Equal("<html>archived</html>", Encoding.UTF8.GetString(page.Resolve("/inside.html")!.Value.Bytes));
        Assert.Null(page.Resolve("/style.css"));
    }

    [Theory]
    [InlineData("a.html", "", true)]
    [InlineData("A.HTM", "", true)]
    [InlineData("page.xhtml", "", true)]
    [InlineData("readme", "﻿  <!DOCTYPE html><html>", true)]
    [InlineData("export.dat", "\n<html lang=en>", true)]
    [InlineData("notes.txt", "hello <html>", false)]
    [InlineData("data.xml", "<?xml version='1.0'?>", false)]
    public void Pages_are_known_by_extension_or_beginning(string name, string start, bool expected) =>
        Assert.Equal(expected, HtmlPage.IsHtml(name, Encoding.UTF8.GetBytes(start)));
}
