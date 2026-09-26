using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public class ContentAndToolTests
{
    [Fact]
    public void Encoding_detection_uses_evidence()
    {
        Assert.Equal(3, TextDecoding.Detect([0xEF, 0xBB, 0xBF, (byte)'a']).PreambleLength);
        Assert.IsType<UnicodeEncoding>(TextDecoding.Detect([0xFF, 0xFE, (byte)'a', 0]).Encoding);
        Assert.True(TextDecoding.Detect([1, 2, 0, 0, 5, 0, 7, 0, 0, 0]).LooksBinary);
        Assert.Contains("UTF-8", TextDecoding.Detect(Encoding.UTF8.GetBytes("Příliš žluťoučký kůň")).Evidence);
        Assert.Contains("ASCII", TextDecoding.Detect("plain text"u8.ToArray()).Evidence);
        var utf16 = Encoding.Unicode.GetBytes("hello world, this is text");
        Assert.Contains("UTF-16 LE", TextDecoding.Detect(utf16).Evidence);
    }

    [Fact]
    public void Search_finds_matches_across_chunk_boundaries()
    {
        var data = new byte[3 * 1024 * 1024];
        new Random(1).NextBytes(data);
        for (int i = 0; i < data.Length; i++) if (data[i] == (byte)'N') data[i] = 0;
        var needle = "NEEDLE-ÄÖ"u8.ToArray();
        long at = 1024 * 1024 - 4; // straddles the first chunk boundary
        needle.CopyTo(data, at);
        using var reader = new PagedReader(new MemoryContentSource("m", data));
        Assert.Equal(at, ContentSearch.FindBytes(reader, 0, needle, CancellationToken.None));
        Assert.Equal(at, ContentSearch.FindText(reader, new UTF8Encoding(false), 0, "needle-äö", false, CancellationToken.None));
        Assert.Equal(-1, ContentSearch.FindText(reader, new UTF8Encoding(false), at + 1, "NEEDLE-ÄÖ", true, CancellationToken.None));
        Assert.Equal(at, ContentSearch.FindBytesBackward(reader, data.Length, needle, CancellationToken.None));
        Assert.Equal(new byte[] { 0x4D, 0x5A, 0x90 }, ContentSearch.ParseHex("4D 5A 90"));
        Assert.Equal(new byte[] { 0x4D, 0x5A }, ContentSearch.ParseHex("0x4d,0x5a"));
        Assert.Null(ContentSearch.ParseHex("zz"));
    }

    [Fact]
    public void Paged_reader_serves_ranges_and_short_reads()
    {
        var data = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        using var reader = new PagedReader(new MemoryContentSource("m", data), maxPages: 4);
        var buf = new byte[100];
        Assert.Equal(100, reader.Read(PagedReader.PageSize - 50, buf));
        Assert.Equal(data[PagedReader.PageSize - 50], buf[0]);
        Assert.Equal(data[PagedReader.PageSize + 49], buf[99]);
        Assert.Equal(40, reader.Read(data.Length - 40, buf));
        Assert.Equal(0, reader.Read(data.Length + 10, buf));
    }

    [Fact]
    public void Tool_launcher_refuses_batch_files_with_metacharacters()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var bat = dir.File("tool.cmd", "@echo %*");
        var evil = dir.File("a&calc.txt");
        var tool = new ToolDefinition { Name = "t", Executable = bat, Arguments = ["{file}"] };
        var ex = Assert.Throws<ToolLaunchException>(() => ToolLauncher.Plan(tool, new ToolContext([evil], dir.Path), dir.Path, out _));
        Assert.Contains("BatBadBut", ex.Message);
        var safe = dir.File("plain.txt");
        var plan = ToolLauncher.Plan(tool, new ToolContext([safe], dir.Path), dir.Path, out _);
        Assert.Single(plan);
        tool.ShellMode = true;
        Assert.Single(ToolLauncher.Plan(tool, new ToolContext([evil], dir.Path), dir.Path, out _));
    }

    [Fact]
    public void Tool_launcher_uses_absolute_paths_list_files_and_splits_long_selections()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var exe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var files = Enumerable.Range(0, 800).Select(i => Path.Combine(dir.Path, $"a-rather-long-file-name-number-{i:0000}.txt")).ToList();
        var split = ToolLauncher.Plan(new ToolDefinition { Name = "n", Executable = exe, Arguments = ["{files}"] }, new ToolContext(files, dir.Path), dir.Path, out var warning);
        Assert.True(split.Count > 1);
        Assert.NotNull(warning);
        Assert.All(split, s => Assert.True(ToolLauncher.CommandLineLength(s.Executable, s.Arguments) <= ToolLauncher.WindowsCommandLineLimit));
        Assert.Equal(files.Count, split.Sum(s => s.Arguments.Count));

        var list = ToolLauncher.Plan(new ToolDefinition { Name = "n", Executable = exe, Arguments = ["/list", "{listfile}"] }, new ToolContext(files, dir.Path), dir.Path, out _);
        var listFile = list[0].Arguments[1];
        Assert.Equal(files, File.ReadAllLines(listFile).Where(l => l.Length > 0));
        Assert.Throws<ToolLaunchException>(() => ToolLauncher.Plan(new ToolDefinition { Name = "x", Executable = @"C:\does\not\exist.exe", Arguments = ["{file}"] }, new ToolContext(files, dir.Path), dir.Path, out _));
    }

    [Theory]
    [InlineData("cmd", "a b.txt", "\"a b.txt\"")]
    [InlineData("cmd", "100%sure%.txt", "\"100\"^%\"sure\"^%\".txt\"")]
    [InlineData("cmd", "plain.txt", "plain.txt")]
    [InlineData("powershell", "O'Brien.txt", "'O''Brien.txt'")]
    [InlineData("powershell", "a\u2019b", "'a\u2019\u2019b'")]
    [InlineData("posix", "it's here", "'it'\\''s here'")]
    [InlineData("posix", "-rf", "'-rf'")]
    public void Command_line_quoting_is_shell_specific(string shell, string name, string expected)
    {
        Assert.Equal(expected, ShellQuoting.Quote(name, shell));
    }
}
