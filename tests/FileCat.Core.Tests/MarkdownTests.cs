using System.Diagnostics;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Release issue I25: Markdown files drawn as pages, from untrusted text.</summary>
public sealed class MarkdownTests
{
    private static string Html(string markdown) => Markdown.ToHtml(markdown).Trim();

    [Theory]
    [InlineData("# Title", "<h1 id=\"title\">Title</h1>")]
    [InlineData("### Three ###", "<h3 id=\"three\">Three</h3>")]
    [InlineData("Setext\n===", "<h1 id=\"setext\">Setext</h1>")]
    [InlineData("Second\n---", "<h2 id=\"second\">Second</h2>")]
    [InlineData("#NotAHeading", "<p>#NotAHeading</p>")]
    [InlineData("one\ntwo", "<p>one\ntwo</p>")]
    [InlineData("hard  \nbreak", "<p>hard<br>\nbreak</p>")]
    [InlineData("back\\\nslash", "<p>back<br>\nslash</p>")]
    [InlineData("***", "<hr>")]
    [InlineData("*em* and **strong** and ***both***", "<p><em>em</em> and <strong>strong</strong> and <em><strong>both</strong></em></p>")]
    [InlineData("_em_ and __strong__", "<p><em>em</em> and <strong>strong</strong></p>")]
    [InlineData("snake_case_name stays", "<p>snake_case_name stays</p>")]
    [InlineData("~~gone~~", "<p><del>gone</del></p>")]
    [InlineData("**bold *it* bold**", "<p><strong>bold <em>it</em> bold</strong></p>")]
    [InlineData("2 * 3 * 4", "<p>2 * 3 * 4</p>")]
    [InlineData("\\*not em\\*", "<p>*not em*</p>")]
    [InlineData("`a <b> c`", "<p><code>a &lt;b&gt; c</code></p>")]
    [InlineData("`` code with ` tick ``", "<p><code>code with ` tick</code></p>")]
    [InlineData("&copy; 2026 &amp; co", "<p>© 2026 &amp; co</p>")]
    public void Blocks_and_inlines_read_as_CommonMark_says(string markdown, string html) => Assert.Equal(html, Html(markdown));

    [Fact]
    public void Code_keeps_its_text_exactly_and_names_its_language()
    {
        Assert.Equal("<pre><code class=\"language-cs\">var x = &quot;&lt;tag&gt;&quot;;\n  indented</code></pre>", Html("```cs\nvar x = \"<tag>\";\n  indented\n```"));
        Assert.Equal("<pre><code>four spaces\n  more</code></pre>", Html("    four spaces\n      more"));
        Assert.Equal("<pre><code># not a heading\n*not em*</code></pre>", Html("~~~\n# not a heading\n*not em*\n~~~"));
    }

    [Fact]
    public void Lists_nest_number_and_tick()
    {
        Assert.Equal("<ul>\n<li>one</li>\n<li>two\n<ul>\n<li>inner</li>\n</ul></li>\n</ul>", Html("- one\n- two\n  - inner"));
        Assert.Equal("<ol start=\"3\">\n<li>three</li>\n<li>four</li>\n</ol>", Html("3. three\n4. four"));
        Assert.Equal("<ul>\n<li class=\"task\"><input type=\"checkbox\" disabled checked> done</li>\n<li class=\"task\"><input type=\"checkbox\" disabled> to do</li>\n</ul>",
            Html("- [x] done\n- [ ] to do"));
        // A blank line between items makes the list loose: paragraphs inside.
        Assert.Equal("<ul>\n<li><p>a</p></li>\n<li><p>b</p></li>\n</ul>", Html("- a\n\n- b"));
    }

    [Fact]
    public void Quotes_hold_blocks_and_lazy_lines()
    {
        Assert.Equal("<blockquote>\n<p>quoted\nlazy</p>\n</blockquote>", Html("> quoted\nlazy"));
        Assert.Equal("<blockquote>\n<ul>\n<li>item</li>\n</ul>\n</blockquote>", Html("> - item"));
    }

    [Fact]
    public void Tables_align_and_keep_escaped_pipes()
    {
        string html = Html("| Name | Size |\n|:-----|-----:|\n| a\\|b | `x|y` |\n| c | 2 |");
        Assert.Contains("<th style=\"text-align:left\">Name</th><th style=\"text-align:right\">Size</th>", html);
        Assert.Contains("<td style=\"text-align:left\">a|b</td><td style=\"text-align:right\"><code>x|y</code></td>", html);
        Assert.Contains("<td style=\"text-align:left\">c</td>", html);
    }

    [Fact]
    public void Links_lead_to_the_web_mail_or_this_page_and_nowhere_else()
    {
        Assert.Equal("<p><a href=\"https://example.com/a?b=1&amp;c=2\" title=\"Home — https://example.com/a?b=1&amp;c=2\">site</a></p>",
            Html("[site](https://example.com/a?b=1&c=2 \"Home\")"));
        Assert.Equal("<p>see <a href=\"https://example.com/\" title=\"https://example.com/\">ref</a></p>", Html("see [ref][r]\n\n[r]: https://example.com/"));
        Assert.Equal("<p><a href=\"#install\" title=\"#install\">jump</a></p>", Html("[jump](#install)"));
        Assert.Equal("<p><a href=\"mailto:me@example.com\" title=\"mailto:me@example.com\">me@example.com</a></p>", Html("<me@example.com>"));
        Assert.Equal("<p>go <a href=\"https://www.example.com\" title=\"https://www.example.com\">www.example.com</a>.</p>", Html("go www.example.com."));
        // Scripts, data, files and other places are shown as text with the address they named, never as links.
        foreach (string url in new[] { "javascript:alert(1)", "JavaScript:alert(1)", "data:text/html,x", "file:///etc/passwd", "other.md", "../up.html" })
        {
            string html = Html($"[click]({url})");
            Assert.DoesNotContain("<a ", html);
            Assert.Contains("<span class=\"link\"", html);
        }
    }

    [Fact]
    public void Pictures_come_only_from_the_files_folder()
    {
        Assert.Equal("<p><img src=\"docs/screen%20shot.png\" alt=\"Screen\"></p>", Html("![Screen](<docs/screen shot.png>)"));
        Assert.Equal("<p><img src=\"docs/screen%20shot.png\" alt=\"Screen\"></p>", Html("![Screen](docs/screen%20shot.png)"));
        Assert.Equal("<p><img src=\"obr%C3%A1zek.png\" alt=\"\"></p>", Html("![](obrázek.png)"));
        foreach (string url in new[] { "https://tracker.example/pixel.gif", "//cdn.example/x.png", "/etc/x.png", "C:/x.png", "data:image/png;base64,AAAA", "javascript:x" })
        {
            string html = Html($"![badge]({url})");
            Assert.DoesNotContain("<img", html);
            Assert.Contains("remote-picture", html);
        }
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<b onclick=\"alert(1)\">x</b>")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<svg/onload=alert(1)>")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("![x](\"onerror=alert(1))")]
    [InlineData("<div>\n<script>alert(1)</script>\n</div>")]
    public void Html_in_the_file_never_reaches_the_page(string markdown)
    {
        // The text may be shown; no element of it may exist: only the renderer's own tags, without event handlers, with
        // links only to the web, mail or this page, and pictures only from the file's folder.
        string html = Markdown.ToHtml(markdown);
        foreach (System.Text.RegularExpressions.Match tag in System.Text.RegularExpressions.Regex.Matches(html, "<([a-zA-Z][a-zA-Z0-9]*)([^>]*)>"))
        {
            string name = tag.Groups[1].Value.ToLowerInvariant(), attributes = tag.Groups[2].Value;
            Assert.Contains(name, new[] { "p", "span", "br", "a", "img", "code", "em", "strong", "del", "kbd" });
            Assert.DoesNotMatch("(?i)\\bon[a-z]+\\s*=", attributes);
            if (System.Text.RegularExpressions.Regex.Match(attributes, "href=\"([^\"]*)\"") is { Success: true } href)
                Assert.Matches("^(https?:|mailto:|#)", href.Groups[1].Value);
            if (System.Text.RegularExpressions.Regex.Match(attributes, "src=\"([^\"]*)\"") is { Success: true } src)
                Assert.DoesNotMatch("^[a-zA-Z][a-zA-Z0-9+.-]*:|^/", src.Groups[1].Value);
        }
    }

    [Fact]
    public void Plain_formatting_tags_and_comments_behave_as_on_GitHub()
    {
        Assert.Equal("<p>Press <kbd>Ctrl</kbd>+<kbd>C</kbd><br>done</p>", Html("Press <kbd>Ctrl</kbd>+<kbd>C</kbd><br>done"));
        Assert.Equal("<p>visible  text</p>", Html("visible <!-- hidden --> text"));
        Assert.Equal("<p>after</p>", Html("<!--\nhidden\nblock\n-->\nafter"));
    }

    [Fact]
    public void Headings_get_anchors_that_stay_unique()
    {
        string html = Html("# Install\n## Install\n## Use & *Enjoy*");
        Assert.Contains("id=\"install\"", html);
        Assert.Contains("id=\"install-1\"", html);
        Assert.Contains("id=\"use--enjoy\"", html);
    }

    [Theory]
    [InlineData('*')]
    [InlineData('_')]
    [InlineData('[')]
    [InlineData('`')]
    [InlineData('>')]
    [InlineData('<')]
    public void Hostile_runs_are_drawn_in_moments(char c)
    {
        // Pathological inputs for naive Markdown parsers: deep nesting, unmatched delimiters by the thousand.
        string input = string.Concat(Enumerable.Repeat(c + "a", 50_000)) + "\n" + new string(c, 50_000) + "\n" + string.Concat(Enumerable.Repeat("- ", 2_000)) + "x";
        var clock = Stopwatch.StartNew();
        string html = Markdown.ToHtml(input);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"{c}: {clock.Elapsed}");
        Assert.NotEmpty(html);
    }

    [Fact]
    public void The_page_forbids_scripts_and_the_web_and_names_itself()
    {
        string page = Markdown.ToPage("# Hi", "notes <1>.md");
        Assert.Contains("Content-Security-Policy\" content=\"default-src 'none'; img-src 'self'; style-src 'unsafe-inline'\"", page);
        Assert.Contains("<title>notes &lt;1&gt;.md</title>", page);
        Assert.Contains("more than FileCat draws as a page", Markdown.ToPage(new string('a', Markdown.MaxChars + 1), "big.md"));
    }

    private sealed class Bytes(byte[] data, string? local) : IContentSource
    {
        public string DisplayName => "doc.md";
        public long Length => data.Length;
        public bool CanSeek => true;
        public string? LocalPath => local;
        public ContentRevision? GetRevision() => null;

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= data.Length) return 0;
            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }

        public void Dispose() { }
    }

    [Fact]
    public void A_markdown_file_is_served_as_its_page_with_pictures_from_its_folder()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-md-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "pic.png"), [0x89, 0x50, 0x4E, 0x47]);
            string md = Path.Combine(folder, "README.md");
            byte[] text = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("# Žluťoučký\n\n![pic](pic.png)\n")];
            File.WriteAllBytes(md, text);
            var page = HtmlPage.ForMarkdown(new Bytes(text, md), md);
            var (bytes, mime) = page.Resolve("/README.md")!.Value;
            Assert.Equal("text/html", mime);
            string html = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains("<h1 id=\"žluťoučký\">Žluťoučký</h1>", html);
            Assert.Contains("<img src=\"pic.png\" alt=\"pic\">", html);
            Assert.Equal("image/png", page.Resolve("/pic.png")!.Value.MimeType);
            Assert.Null(page.Resolve("/../outside.txt"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
