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

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Line_index_finds_far_lines_from_checkpoints_and_reports_short_files(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var preamble = encoding.GetPreamble();
        var text = string.Join("\r\n", Enumerable.Range(1, 10_000).Select(i => $"line {i} ž")) + "\r\n";
        var data = preamble.Concat(encoding.GetBytes(text)).ToArray();
        var index = new LineIndex(new MemoryContentSource("m", data), encoding, preamble.Length);
        long? scannedMax = null;
        var progress = new SynchronousProgress(b => scannedMax = b);

        string LineAt(long offset)
        {
            int end = data.AsSpan((int)offset).IndexOf(encoding.GetBytes("\r\n"));
            return encoding.GetString(data, (int)offset, end);
        }
        Assert.Equal(preamble.Length, index.FindLineStart(1, progress, TestContext.Current.CancellationToken));
        Assert.Equal("line 2 ž", LineAt(index.FindLineStart(2, progress, TestContext.Current.CancellationToken)!.Value));
        Assert.Equal("line 9000 ž", LineAt(index.FindLineStart(9000, progress, TestContext.Current.CancellationToken)!.Value));
        Assert.Equal("line 4097 ž", LineAt(index.FindLineStart(LineIndex.Stride + 1, null, TestContext.Current.CancellationToken)!.Value)); // a checkpoint
        Assert.Equal("line 4500 ž", LineAt(index.FindLineStart(4500, null, TestContext.Current.CancellationToken)!.Value));
        Assert.Null(index.FindLineStart(20_000, progress, TestContext.Current.CancellationToken));
        Assert.Equal(10_001, index.TotalLines); // the empty line after the final break
        Assert.NotNull(scannedMax);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new LineIndex(new MemoryContentSource("m", data), encoding, preamble.Length).FindLineStart(9000, null, canceled.Token));
    }

    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
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

    /// <summary>Content whose second page is damaged (an archive member found broken part way).</summary>
    private sealed class DamagedSecondPage(byte[] data) : IContentSource
    {
        public int Reads;
        public string DisplayName => "damaged";
        public long Length => data.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => null;
        public void Dispose() { }

        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref Reads);
            if (offset >= PagedReader.PageSize) throw new InvalidDataException("The member is damaged: its checksum does not match its content.");
            int n = (int)Math.Min(buffer.Length, PagedReader.PageSize - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
    }

    [Fact]
    public async Task Paged_reader_shows_content_ending_at_a_damaged_page_and_says_why()
    {
        var source = new DamagedSecondPage(new byte[3 * PagedReader.PageSize]);
        using var reader = new PagedReader(source);
        var loaded = new SemaphoreSlim(0);
        reader.PageLoaded += () => loaded.Release();
        var buffer = new byte[2 * PagedReader.PageSize];
        // The first view asks for both pages; the second one fails.
        Assert.False(reader.TryRead(0, buffer, out _));
        await loaded.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(reader.TryRead(0, buffer, out _));
        await loaded.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Contains("checksum", reader.ReadError);
        // Now the content ends where it could not be read, instead of loading forever and asking on every render.
        int reads = source.Reads;
        for (int i = 0; i < 10; i++)
        {
            Assert.True(reader.TryRead(0, buffer, out int n));
            Assert.Equal(PagedReader.PageSize, n);
        }
        Assert.Equal(reads, source.Reads);
    }

    [Fact]
    public void Tool_launcher_refuses_batch_files_with_metacharacters()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Batch files and cmd.exe's metacharacters are Windows'.");
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

    /// <summary>
    /// Release plan V24: a tool the user starts receives exactly the names FileCat meant, whatever those names are.
    /// The oracle is a recording program — the system's own shell, writing down the argument vector it was handed —
    /// and the names are the ones a shell or a command line would otherwise read as something else.
    /// </summary>
    [Fact]
    public void A_started_tool_receives_exactly_the_names_it_was_given()
    {
        using var dir = new TempDir();
        string recorded = Path.Combine(dir.Path, "recorded.txt");
        // Names that mean something to a shell, to cmd.exe, or to an option parser. (A quote, '<', '>' and '|' cannot
        // be in a Windows file name at all; on Unix they can, and are included there.)
        var names = new List<string> { "a b.txt", "a&calc.txt", "a;id.txt", "a%PATH%.txt", "a$(id).txt", "a'quote.txt", "-rf.txt", "a`tick.txt", "a!bang.txt", "a^caret.txt" };
        if (!OperatingSystem.IsWindows()) names.AddRange(["a\"quote.txt", "a|pipe.txt", "a>redirect.txt", "a\nnewline.txt"]);
        var files = names.Select(n => dir.File(n)).ToList();

        // Each record ends with a separator that cannot be inside a name: a zero byte on Unix, where a name may hold a
        // newline, and a newline on Windows, where it may not.
        char separator = OperatingSystem.IsWindows() ? '\n' : '\0';
        var recorders = new List<(string What, ToolDefinition Tool)>();
        if (OperatingSystem.IsWindows())
        {
            // Two of them, because a machine may forbid one: the Windows Script Host answers to no execution policy,
            // and PowerShell is there even where scripting is switched off.
            string js = dir.File("recorder.js", "var fso = new ActiveXObject('Scripting.FileSystemObject');\nvar out = fso.CreateTextFile(WScript.Arguments.Item(0), true, true);\nfor (var i = 1; i < WScript.Arguments.length; i++) out.WriteLine(WScript.Arguments.Item(i));\nout.Close();\n");
            string ps1 = dir.File("recorder.ps1", "[IO.File]::WriteAllLines($args[0], $args[1..($args.Count-1)])\n");
            string cscript = Path.Combine(Environment.SystemDirectory, "cscript.exe");
            string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(cscript)) recorders.Add(("the Windows Script Host", new ToolDefinition { Name = "recorder", Executable = cscript, Arguments = ["//nologo", "//B", js, recorded, "{files}"] }));
            if (File.Exists(powershell)) recorders.Add(("Windows PowerShell", new ToolDefinition { Name = "recorder", Executable = powershell, Arguments = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ps1, recorded, "{files}"] }));
        }
        else
        {
            string script = dir.File("recorder.sh", "#!/bin/sh\nout=$1\nshift\nprintf '%s\\0' \"$@\" > \"$out\"\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            recorders.Add(("/bin/sh", new ToolDefinition { Name = "recorder", Executable = "/bin/sh", Arguments = [script, recorded, "{files}"] }));
        }

        string[] got = [];
        var tried = new List<string>();
        foreach (var (what, tool) in recorders)
        {
            try { File.Delete(recorded); } catch (IOException) { }
            var result = ToolLauncher.Launch(tool, new ToolContext(files, dir.Path), dir.Path);
            Assert.Single(result.Invocations);
            for (int i = 0; i < 300; i++) // a cold shell on a loaded machine can take a while to start
            {
                // A Windows recorder ends its lines with CRLF, and no Windows name can hold a carriage return; a Unix
                // name can, so there the zero byte alone separates them and nothing is trimmed.
                try
                {
                    if (File.Exists(recorded))
                    {
                        got = File.ReadAllText(recorded).Split(separator, StringSplitOptions.RemoveEmptyEntries);
                        if (OperatingSystem.IsWindows()) got = [.. got.Select(r => r.TrimEnd('\r'))];
                    }
                }
                catch (IOException) { }
                if (got.Length >= files.Count) break;
                Thread.Sleep(100);
            }
            tried.Add($"{what}: {got.Length} records");
            if (got.Length > 0) break;
        }
        // Where no recording program can run at all, this machine cannot answer the question; where one did, it must
        // have been handed the names unchanged.
        if (got.Length == 0) { Assert.Skip("No recording program ran here — " + string.Join("; ", tried)); return; }

        // Every name, once, unchanged: nothing split at a space, expanded, swallowed as an option or run as a command.
        Assert.Equal(files, got);
    }

    [Fact]
    public void Programs_are_found_by_full_path_never_through_a_relative_PATH_entry()
    {
        // Release plan I16: a relative PATH entry ("." and the like) is resolved against the current directory, where a
        // program could have been planted; tools, shells, terminals and openers are looked for in absolute entries only.
        string name = "fc-planted-" + Guid.NewGuid().ToString("N")[..8];
        string file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        string folder = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, name)).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, file), "");
            string relative = Path.GetRelativePath(Environment.CurrentDirectory, folder);
            Assert.False(Path.IsPathFullyQualified(relative));
            Assert.Null(ToolLauncher.FindOnPath(name, relative));
            Assert.Equal(Path.Combine(folder, file), ToolLauncher.FindOnPath(name, relative + Path.PathSeparator + "\"" + folder + "\""));
            Assert.Throws<System.ComponentModel.Win32Exception>(() => FileCat.Core.Platform.PortableShellServices.Resolve(name));
            if (!OperatingSystem.IsWindows()) Assert.True(Path.IsPathFullyQualified(FileCat.Core.Platform.PortableShellServices.Resolve("sh")));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Tool_launcher_uses_absolute_paths_list_files_and_splits_long_selections()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' command-line length limit and list files.");
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
