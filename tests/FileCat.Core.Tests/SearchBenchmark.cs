using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Search;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

/// <summary>
/// Plan §21.2 ("search: throughput, first results, memory, cancellation, completeness accuracy"): recursive search by
/// name and by content over 50,000 small files and 200 files of 1 MiB. Set FILECAT_SEARCH_BENCH=1;
/// docs/validation/TV-08.md records the results with the comparison measurements.
/// </summary>
public sealed class SearchBenchmark : IDisposable
{
    private const int Folders = 500, FilesPerFolder = 100;
    private readonly TempDir _dir = new();
    private readonly List<string> _results = [];

    public void Dispose() => _dir.Dispose();

    private (ResultSet Results, TimeSpan Elapsed, TimeSpan FirstResult, long Allocated) Run(string name, SearchQuery query, int expected)
    {
        var set = new ResultSet("bench", name, name);
        var session = new SearchSession(query, set);
        GC.Collect();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        TimeSpan first = TimeSpan.Zero;
        set.Changed += () =>
        {
            if (first == TimeSpan.Zero && set.Count > 0) first = clock.Elapsed;
        };
        session.Run(TestContext.Current.CancellationToken);
        var elapsed = clock.Elapsed;
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        _results.Add($"{name}: {elapsed.TotalMilliseconds:F0} ms, first result after {first.TotalMilliseconds:F0} ms, {set.Count:N0} matches, " +
                     $"{session.FilesExamined:N0} files read, {allocated / (1024 * 1024.0):F0} MiB allocated.");
        Assert.True(set.IsComplete);
        Assert.Equal(expected, set.Count);
        return (set, elapsed, first, allocated);
    }

    [Fact]
    public void Searching_by_name_and_content_is_fast_complete_and_cancelable()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_SEARCH_BENCH") is not { Length: > 0 }) Assert.Skip("Set FILECAT_SEARCH_BENCH=1 to measure search.");
        string small = _dir.Dir("small");
        for (int f = 0; f < Folders; f++)
        {
            string folder = Directory.CreateDirectory(Path.Combine(small, $"folder-{f:000}")).FullName;
            for (int i = 0; i < FilesPerFolder; i++)
            {
                // Every tenth file is a .log; every hundredth mentions the needle.
                string text = i % 100 == 42 ? $"note {f}-{i}: the Needle is here\n" : $"note {f}-{i}: nothing to see\n";
                File.WriteAllText(Path.Combine(folder, i % 10 == 0 ? $"file-{i:000}.log" : $"file-{i:000}.txt"), string.Concat(Enumerable.Repeat(text, 20)));
            }
        }
        string large = _dir.Dir("large");
        var line = "an ordinary line of text that goes on for a while without the word we look for\n"u8.ToArray();
        for (int n = 0; n < 200; n++)
        {
            using var file = File.Create(Path.Combine(large, $"big-{n:000}.txt"));
            for (int written = 0; written < 1024 * 1024; written += line.Length) file.Write(line);
            if (n % 50 == 7) file.Write("the Needle at the very end\n"u8);
        }

        Run("By name (*.log), 50,000 files in 500 folders", new SearchQuery { Roots = [small], Names = Mask.Parse("*.log"), IncludeDirectories = false }, Folders * FilesPerFolder / 10);
        var ignoreCase = Run("Content \"needle\" (ignoring case), 50,000 small files", new SearchQuery { Roots = [small], Text = "needle", IncludeDirectories = false }, Folders);
        Run("Content \"Needle\" (matching case), 50,000 small files", new SearchQuery { Roots = [small], Text = "Needle", MatchCase = true, IncludeDirectories = false }, Folders);
        var bulk = Run("Content \"needle\" (ignoring case), 200 files of 1 MiB", new SearchQuery { Roots = [large], Text = "needle", IncludeDirectories = false }, 4);
        Run("Content \"Needle\" (matching case), 200 files of 1 MiB", new SearchQuery { Roots = [large], Text = "Needle", MatchCase = true, IncludeDirectories = false }, 4);
        Run("Content by regular expression \"Need+le\", 200 files of 1 MiB", new SearchQuery { Roots = [large], Text = "Need+le", Regex = true, MatchCase = true, IncludeDirectories = false }, 4);

        // Cancellation: a content search over everything, stopped 100 ms in.
        var set = new ResultSet("cancel", "cancel", "cancel");
        var session = new SearchSession(new SearchQuery { Roots = [_dir.Path], Text = "absent words", IncludeDirectories = false }, set);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var clock = Stopwatch.StartNew();
        session.Run(cancel.Token);
        var stoppedAfter = clock.Elapsed - TimeSpan.FromMilliseconds(100);
        Assert.False(set.IsComplete); // an interrupted search never claims to be complete
        _results.Add($"Cancel a content search 100 ms in: stopped {Math.Max(0, stoppedAfter.TotalMilliseconds):F0} ms after the request; the result set says it is incomplete.");

        foreach (var result in _results) TestContext.Current.TestOutputHelper?.WriteLine(result);
        // Budgets (docs/validation/TV-08.md): generous for shared machines.
        Assert.True(stoppedAfter < TimeSpan.FromMilliseconds(250), $"Canceling took {stoppedAfter}.");
        Assert.True(bulk.Elapsed < TimeSpan.FromSeconds(2), $"Searching 200 MiB ignoring case took {bulk.Elapsed}.");
        Assert.True(ignoreCase.Elapsed < TimeSpan.FromSeconds(20), $"Searching 50,000 small files took {ignoreCase.Elapsed}.");
        Assert.True(ignoreCase.Allocated < 512L * 1024 * 1024, $"Searching small files allocated {ignoreCase.Allocated:N0} bytes.");
    }
}
