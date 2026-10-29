using System.Diagnostics;
using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Hostile Markdown is drawn in time and memory that grow with the file, not with its square (release review findings
/// F-H6 and F-H7). F3 on a Markdown file draws it as a page, so each file here was enough: a table whose short rows were
/// each padded to thousands of columns (a gigabyte of HTML, or no memory left, from 60 KB), and lines that a line pattern,
/// or the comment, link or emphasis parser, read once per character, which took from tens of seconds to hours.
/// </summary>
public sealed class MarkdownBoundsTests
{
    /// <summary>How long a page may take before the test calls it hung; each takes under a second on a desktop, and took
    /// the renderer before the fix ten times as long at the least.</summary>
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(10);

    private static Task<T> OnOwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .WaitAsync(HangLimit, TestContext.Current.CancellationToken);

    /// <summary>The page as the viewer gets it, read, drawn and encoded on a thread of its own.</summary>
    private static async Task<string> Page(string markdown)
    {
        var watch = Stopwatch.StartNew();
        byte[] bytes = await OnOwnThread(() =>
        {
            using var reader = new PagedReader(new MemoryContentSource("hostile.md", Encoding.UTF8.GetBytes(markdown)));
            var page = HtmlPage.ForMarkdown(reader, "hostile.md", TestContext.Current.CancellationToken);
            return page.Resolve("/")?.Bytes ?? throw new InvalidOperationException("The page was not served.");
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{markdown.Length:N0} characters drawn as {bytes.Length:N0} bytes in {watch.Elapsed}");
        return Encoding.UTF8.GetString(bytes);
    }

    private static string Repeat(string text, int times) => new StringBuilder(text.Length * times).Insert(0, text, times).ToString();

    [Theory]
    [InlineData(10_000, 10_000, 1)] // 60 KB: a hundred million cells, 0.9 billion characters of HTML
    [InlineData(1_000, 1_000, 50)] // fifty tables of a million cells: the limit is the page's, not each table's
    public async Task Short_rows_under_wide_headings_draw_a_bounded_number_of_cells(int columns, int rows, int tables)
    {
        var markdown = new StringBuilder();
        for (int t = 0; t < tables; t++)
            markdown.Append(Repeat("|a", columns)).Append("|\n").Append(Repeat("|-", columns)).Append("|\n").Append(Repeat("|\n", rows)).Append('\n');
        string page = await Page(markdown.ToString());
        Assert.InRange(page.Length, 0, 8 << 20);
        Assert.Contains("<table>", page);
        Assert.Contains("more cells than FileCat draws as a page; the Text mode shows", page);
    }

    [Theory]
    [InlineData("```", ' ', 256 * 1024, "`")] // spaces, then a backtick: 3.7 s at 64 KB, four times as long per doubling
    [InlineData("```", 'a', 256 * 1024, "`")] // a long info word, then a backtick
    [InlineData("~~~", '~', 64 * 1024, "`")] // a fence of tildes, then a backtick: 38 s at 4 KB, about eight times as long per doubling
    public async Task Code_fence_lines_are_read_in_linear_time(string fence, char fill, int count, string end)
    {
        string page = await Page(fence + new string(fill, count) + end);
        Assert.Contains("<p>" + fence[..2], page);
        Assert.DoesNotContain("<pre>", page);
    }

    [Fact]
    public async Task Comments_that_never_close_are_read_in_linear_time()
    {
        // Four megabytes of "<!--", none closed: each was read on to the end of the text, 9 s for one megabyte.
        string page = await Page("a" + Repeat("<!--", (Markdown.MaxChars - 1) / 4));
        Assert.Contains("<p>a&lt;!--&lt;!--", page);
    }

    [Fact]
    public async Task Heading_lines_are_read_in_linear_time()
    {
        // The end of a heading was tried after each character of its text, reading the run of spaces each time: 8.5 s
        // for half a megabyte.
        string page = await Page("# a" + new string(' ', 2 << 20) + "b");
        Assert.Contains("<h1 id=\"a-", page);
    }

    [Fact]
    public async Task Reference_definition_lines_are_read_in_linear_time()
    {
        // Spaces shared between a destination and a title: every split of them was tried, 6.7 s for half a megabyte.
        string page = await Page("[a]:" + new string(' ', 2 << 20) + "x y");
        Assert.Contains("<p>[a]:", page);
    }

    [Theory]
    [InlineData("[a](")] // destinations that never close, each read on to the end of the line: 8 s for 128 KB
    [InlineData("[a](<")] // in angle brackets: 12 s for 256 KB
    [InlineData("[a](x (")] // titles that never close: 5 s for half a megabyte
    public async Task Links_that_never_close_are_read_in_linear_time(string link)
    {
        string page = await Page(Repeat(link, Markdown.MaxChars / link.Length));
        Assert.Contains("<p>[a](", page);
        Assert.DoesNotContain("<a ", page);
    }

    [Theory]
    [InlineData("")] // brackets nested by the half million, each label copied and looked up: 8 s for 128 KB
    [InlineData("(u)")] // the same, the outermost a link
    public async Task Nested_brackets_are_read_in_linear_time(string destination)
    {
        string page = await Page(new string('[', 512 * 1024) + "x" + new string(']', 512 * 1024) + destination);
        Assert.Contains("[[[[x]]]]", page);
    }

    [Theory]
    [InlineData('*')] // emphasis nested by the hundred thousand: each pair looked back through all the others, 3.3 s for 128 KB
    [InlineData('_')]
    public async Task Nested_emphasis_is_paired_in_linear_time(char delimiter)
    {
        string page = await Page(Repeat(delimiter + "a ", 170_000) + Repeat("a" + delimiter + " ", 170_000));
        Assert.Contains("<em>a <em>a <em>a", page);
        Assert.Contains("a</em> a</em> a</em>", page);
    }
}
