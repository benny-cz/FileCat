using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>The page view serves the page and the files in its own folder, and nothing else (D-51).</summary>
public sealed class HtmlPageTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

    [Fact]
    public void Local_markdown_links_open_only_contained_markdown_files_in_another_viewer()
    {
        using var root = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(root.Path, "docs")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(folder, "more")).FullName;
        string current = Path.Combine(folder, "index.md");
        string linked = Path.Combine(nested, "next file.md");
        File.WriteAllText(current, "[next](more/next%20file.md#heading)");
        File.WriteAllText(linked, "# Next");
        File.WriteAllText(Path.Combine(root.Path, "outside.md"), "# Outside");
        using var source = new FileContentSource(current);
        var page = HtmlPage.ForMarkdown(source, current);

        Assert.True(page.TryLinkedMarkdownPath(page.Address + "?filecat-open=more%2Fnext%2520file.md%23heading", out string? path));
        // macOS can return /private/var for a temp file created through /var.
        // Check the target and its contents while retaining the refusal cases below.
        Assert.EndsWith(Path.Combine("docs", "more", "next file.md"), path, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(linked), File.ReadAllText(path!));
        Assert.True(page.TryLinkedMarkdownPath("filecat://page/index.md?filecat-open=more%2Fnext%2520file.md%23heading", out path));
        Assert.EndsWith(Path.Combine("docs", "more", "next file.md"), path, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(linked), File.ReadAllText(path!));
        foreach (string address in new[]
        {
            page.Address + "?filecat-open=..%2Foutside.md",
            page.Address + "?filecat-open=%252e%252e%2Foutside.md",
            page.Address + "?filecat-open=more%2Fnext%20file.txt",
            page.Address + "?filecat-open=https%3A%2F%2Fexample.com%2Fx.md",
            page.Address + "?filecat-open=%2Foutside.md",
            page.Address + "?filecat-open=more%2Fnext%2520file.md&extra=1",
            "https://example.com/index.md?filecat-open=more%2Fnext%2520file.md",
            page.Address + "?filecat-open=missing.md",
        })
            Assert.False(page.TryLinkedMarkdownPath(address, out _), address);
    }

    [Fact]
    public void Parent_relative_markdown_link_needs_one_time_review_of_its_resolved_path()
    {
        using var root = new TempDir();
        string validation = Directory.CreateDirectory(Path.Combine(root.Path, "docs", "validation")).FullName;
        string release = Directory.CreateDirectory(Path.Combine(root.Path, "docs", "release")).FullName;
        string current = Path.Combine(validation, "results.md");
        string linked = Path.Combine(release, "status.md");
        File.WriteAllText(current, "[plan](plan.md) [release dashboard](../release/status.md)");
        File.WriteAllText(Path.Combine(validation, "plan.md"), "# Plan");
        File.WriteAllText(linked, "# Release status");
        using var source = new FileContentSource(current);
        var page = HtmlPage.ForMarkdown(source, current);
        string address = page.Address + "?filecat-open=..%2Frelease%2Fstatus.md";

        Assert.False(page.TryLinkedMarkdownPath(address, out _)); // No automatic escape.
        Assert.True(page.TryLinkedMarkdownTarget(address, out var target));
        Assert.True(target.RequiresConfirmation);
        Assert.Equal(address, target.RequestAddress);
        // macOS may canonicalize /var to /private/var while resolving the existing target.
        Assert.EndsWith(Path.Combine("docs", "release", "status.md"), target.Path, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(linked), File.ReadAllText(target.Path));
        Assert.True(page.TryLinkedMarkdownTarget(page.Address + "?filecat-open=plan.md", out var contained));
        Assert.False(contained.RequiresConfirmation);
        Assert.False(page.TryLinkedMarkdownTarget(page.Address + "?filecat-open=file%3A%2F%2F%2Fetc%2Fpasswd", out _));
    }

    [Fact]
    public void A_markdown_link_through_a_symlink_outside_its_folder_is_refused()
    {
        using var root = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(root.Path, "docs")).FullName;
        string current = Path.Combine(folder, "index.md");
        string outside = Path.Combine(root.Path, "outside.md");
        File.WriteAllText(current, "[outside](linked.md)");
        File.WriteAllText(outside, "# Outside");
        try { File.CreateSymbolicLink(Path.Combine(folder, "linked.md"), outside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Links cannot be made here: " + ex.Message); }
        using var source = new FileContentSource(current);
        var page = HtmlPage.ForMarkdown(source, current);
        Assert.False(page.TryLinkedMarkdownPath(page.Address + "?filecat-open=linked.md", out _));
    }

    [Fact]
    public void A_page_does_not_open_an_adjacent_fifo_waiting_for_a_writer()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix FIFO control."); return; }
        using var root = new TempDir();
        string pagePath = Path.Combine(root.Path, "index.html");
        string pipePath = Path.Combine(root.Path, "owned.pipe");
        File.WriteAllText(pagePath, "<html></html>");
        Assert.Equal(0, MakeFifo(pipePath, 384));
        using var source = new FileContentSource(pagePath);
        var page = new HtmlPage(source, pagePath);
        var elapsed = Stopwatch.StartNew();

        Assert.Null(page.Resolve("/owned.pipe"));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"FIFO refusal took {elapsed.Elapsed}.");
        Assert.Equal("<html></html>", Encoding.UTF8.GetString(page.Resolve("/index.html")!.Value.Bytes));
    }

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
        Assert.True(reader.Read(0, new byte[10], TestContext.Current.CancellationToken) > 0);
    }

    [Theory]
    [InlineData(256 * 1024)]
    [InlineData(1024 * 1024)]
    public async Task Viewer_callback_bounds_malformed_table_delimiters(int spaces)
    {
        // A dash, then spaces, then text: the pattern the delimiter row was read with until f134ba37 tried every split of
        // the spaces, 3.2 s at 64 KB and 19 s at 128 KB. (Without the dash, "|" + spaces + "x|", it refused the line at
        // once.)
        string markdown = "| a | b |\n| --- " + new string(' ', spaces) + "x |\n";
        using var reader = new PagedReader(new MemoryContentSource("hostile.md", Encoding.UTF8.GetBytes(markdown)));
        var page = HtmlPage.ForMarkdown(reader, "hostile.md", TestContext.Current.CancellationToken);
        var (response, elapsed, allocated) = await Task.Factory.StartNew(() =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var served = page.Resolve("/");
            return (served, watch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper?.WriteLine($"Markdown viewer callback: whitespace={spaces}, elapsed={elapsed}, thread-allocated={allocated:N0} bytes");
        Assert.NotNull(response);
        Assert.Equal("text/html", response.Value.MimeType);
        Assert.Contains("<p>| a | b |", Encoding.UTF8.GetString(response.Value.Bytes));
        Assert.True(allocated < 128L * markdown.Length, $"{spaces}: {allocated} bytes allocated");
    }

    [Fact]
    public void Viewer_callback_renders_valid_aligned_table_and_refuses_after_close()
    {
        const string markdown = "# Readme\n\n| a | b |\n| :--- | ---: |\n| 1 | 2 |\n";
        using var reader = new PagedReader(new MemoryContentSource("README.md", Encoding.UTF8.GetBytes(markdown)));
        var page = HtmlPage.ForMarkdown(reader, "README.md", TestContext.Current.CancellationToken);
        var response = page.Resolve("/");
        Assert.NotNull(response);
        string html = Encoding.UTF8.GetString(response.Value.Bytes);
        Assert.Contains("<h1", html);
        Assert.Contains("<table>", html);
        reader.Dispose();
        Assert.Null(page.Resolve("/"));
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

    [Fact]
    public void Archived_markdown_does_not_offer_an_unopenable_local_file_link()
    {
        using var source = new MemoryContentSource("inside.md", Encoding.UTF8.GetBytes("[next](next.md)"));
        var page = HtmlPage.ForMarkdown(source, "archive.zip/docs/inside.md");
        string html = Encoding.UTF8.GetString(page.Resolve("/inside.md")!.Value.Bytes);
        Assert.Contains("<span class=\"link\" title=\"next.md\">next</span>", html);
        Assert.DoesNotContain("?filecat-open=", html);
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
