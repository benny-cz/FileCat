using System.Diagnostics;
using System.Globalization;
using System.Text;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// TV-08 (plan §22) and P7's exit ("measure dissimilar/huge input and worker memory"): time, peak managed memory, and
/// cancellation of text and binary comparison on identical, shifted, similar, unrelated, repetitive, and giant-line
/// inputs. Set FILECAT_COMPARE_BENCH=1; docs/validation/TV-08.md records the results.
/// </summary>
public sealed class CompareBenchmark : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly List<string> _results = [];
    private readonly Dictionary<string, TimeSpan> _times = [];

    public void Dispose() => _dir.Dispose();

    /// <summary>Runs <paramref name="work"/> and reports its time and the peak of the managed heap above where it started.</summary>
    private T Measure<T>(string name, Func<T> work, Func<T, string> describe)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long start = GC.GetTotalMemory(forceFullCollection: true);
        long peak = start;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(false));
                try { await Task.Delay(5, sampling.Token); }
                catch (OperationCanceledException) { }
            }
        });
        var clock = Stopwatch.StartNew();
        var result = work();
        var elapsed = clock.Elapsed;
        _times[name] = elapsed;
        sampling.Cancel();
        sampler.Wait();
        peak = Math.Max(peak, GC.GetTotalMemory(false));
        _results.Add($"{name}: {elapsed.TotalMilliseconds:F0} ms, peak managed memory +{(peak - start) / (1024 * 1024.0):F0} MiB; {describe(result)}");
        return result;
    }

    private static string Lines(int count, Func<int, string> line)
    {
        var sb = new StringBuilder(count * 40);
        for (int i = 0; i < count; i++) sb.Append(line(i)).Append('\n');
        return sb.ToString();
    }

    private static string Describe(TextDiffResult r) =>
        $"{r.Differences:N0} differences in {r.Blocks.Count:N0} blocks{(r.Approximate ? ", approximate" : "")}{(r.Heuristic ? ", heuristic" : "")}" +
        (r.Blocks.Any(b => b.Kind == DiffKind.Unaligned) ? $", {r.Blocks.Where(b => b.Kind == DiffKind.Unaligned).Sum(b => b.LeftCount + b.RightCount):N0} lines left unaligned" : "");

    private TextDiffResult Text(string name, string left, string right) => Measure(name, () =>
    {
        var a = TextSide.FromText(left);
        var b = TextSide.FromText(right);
        return TextDiff.Compare(a.Lines, b.Lines, ct: TestContext.Current.CancellationToken);
    }, Describe);

    [Fact]
    public void Comparison_stays_truthful_bounded_and_cancelable()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_COMPARE_BENCH") is not { Length: > 0 }) Assert.Skip("Set FILECAT_COMPARE_BENCH=1 to measure comparison.");
        var rng = new Random(8);
        const int Million = 1_000_000;
        string source = Lines(Million, i => $"{i:D7} the quick brown fox {rng.Next():x8}");
        var lines = source.Split('\n');

        var identical = Text("Identical, 1,000,000 lines (46 MB)", source, source);
        Assert.Equal(0, identical.Differences);

        // One line inserted at the top and one removed at the end: everything else shifted by one.
        string shiftedText = "inserted\n" + source[..source.LastIndexOf('\n', source.Length - 2)] + "\n";
        var shifted = Text("Shifted by one line, 1,000,000 lines", source, shiftedText);
        Assert.Equal(2, shifted.Differences);
        Assert.False(shifted.Heuristic); // split on unique lines, and provably the best: not labelled

        var edited = (string[])lines.Clone();
        for (int e = 0; e < 1000; e++) edited[rng.Next(Million)] = "edited " + e;
        var similar = Text("1,000 scattered edits, 1,000,000 lines", source, string.Join('\n', edited));
        Assert.InRange(similar.Differences, 900, 1000);
        Assert.False(similar.Heuristic);
        Assert.DoesNotContain(similar.Blocks, b => b.Kind == DiffKind.Unaligned);

        // Unrelated: no line in common. Never reported equal; whatever is not aligned is labelled so.
        string other = Lines(200_000, i => $"other {rng.Next():x8} {rng.Next():x8}");
        var unrelated = Text("Unrelated, 200,000 lines each", string.Join('\n', lines.Take(200_000)), other);
        Assert.True(unrelated.Differences > 0);
        Assert.DoesNotContain(unrelated.Blocks, b => b.Kind == DiffKind.Equal && b.LeftCount > 0);

        // Repetitive: ten distinct lines in different orders, so no line is unique to anchor on.
        string repeatA = Lines(200_000, i => "repeat " + rng.Next(10));
        string repeatB = Lines(200_000, i => "repeat " + rng.Next(10));
        var repetitive = Text("Repetitive (10 distinct lines), 200,000 lines each", repeatA, repeatB);
        Assert.True(repetitive.Differences > 0);

        // A giant line: 32 MB without a line break, one character changed in the middle.
        var giant = new StringBuilder(32 * 1024 * 1024).Append('x', 32 * 1024 * 1024);
        string giantA = giant.ToString();
        giant[16 * 1024 * 1024] = 'y';
        var giantLine = Text("One 32 MB line, one character changed", giantA, giant.ToString());
        Assert.Equal(1, giantLine.Differences);
        Assert.Null(InlineDiff.Compare(giantA, giant.ToString())); // within-line changes are not computed beyond 20,000 characters

        // Cancellation: an unrelated 1,000,000-line comparison is stopped 100 ms in.
        string unrelatedMillion = Lines(Million, i => $"elsewhere {rng.Next():x8}");
        var left = TextSide.FromText(source).Lines;
        var right = TextSide.FromText(unrelatedMillion).Lines;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var clock = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => TextDiff.Compare(left, right, ct: cancel.Token));
        var stoppedAfter = clock.Elapsed - TimeSpan.FromMilliseconds(100);
        _results.Add($"Cancel an unrelated 1,000,000-line comparison 100 ms in: stopped {Math.Max(0, stoppedAfter.TotalMilliseconds):F0} ms after the request.");

        // Binary: 256 MiB identical, then with 20,000 single-byte changes (more than the 10,000 ranges listed).
        var bytes = new byte[256 * 1024 * 1024];
        rng.NextBytes(bytes);
        string pathA = Path.Combine(_dir.Path, "a.bin"), pathB = Path.Combine(_dir.Path, "b.bin");
        File.WriteAllBytes(pathA, bytes);
        File.WriteAllBytes(pathB, bytes);
        var same = Measure("Binary, 256 MiB identical (files)", () => Binary(pathA, pathB), r => r.Equal ? "equal" : "different");
        Assert.True(same.Equal);
        for (int e = 0; e < 20_000; e++) bytes[rng.Next(bytes.Length)] ^= 0x5A;
        File.WriteAllBytes(pathB, bytes);
        var scattered = Measure("Binary, 256 MiB with 20,000 changed bytes", () => Binary(pathA, pathB),
            r => $"{r.Ranges.Count:N0} ranges listed{(r.RangesTruncated ? ", more not listed (said so)" : "")}");
        Assert.False(scattered.Equal);
        Assert.True(scattered.RangesTruncated);

        // Aligned: 100 bytes inserted near the start shift everything after them; the aligned mode finds it all again,
        // each changed byte on its own.
        var inserted = new byte[bytes.Length + 100];
        bytes.AsSpan(0, 1_000_000).CopyTo(inserted);
        bytes.AsSpan(1_000_000).CopyTo(inserted.AsSpan(1_000_100));
        File.WriteAllBytes(pathB, inserted);
        var aligned = Measure("Aligned binary, 256 MiB with 20,000 changed bytes and 100 inserted", () => Aligned(pathA, pathB),
            r => $"{r.Ranges.Count:N0} ranges, {r.EqualBytes * 100.0 / bytes.Length:F2}% of the left matched, blocks of {r.BlockSize} bytes");
        Assert.True(aligned.Complete);
        Assert.Contains(aligned.Ranges, r => r.Kind == DiffKind.RightOnly && r.RightLength == 100);
        Assert.True(aligned.EqualBytes > bytes.Length - 40_000);

        // Unrelated: every byte of the right file is searched, the slowest case short of the work limit.
        rng.NextBytes(inserted);
        File.WriteAllBytes(pathB, inserted);
        var unrelatedBytes = Measure("Aligned binary, 256 MiB unrelated", () => Aligned(pathA, pathB),
            r => $"{r.EqualBytes * 100.0 / bytes.Length:F2}% of the left matched{(r.Complete ? "" : ", work limit reached")}");
        Assert.True(unrelatedBytes.Complete);
        Assert.Equal(0, unrelatedBytes.EqualBytes);

        foreach (var line in _results) TestContext.Current.TestOutputHelper?.WriteLine(line);
        Assert.True(_times["Aligned binary, 256 MiB with 20,000 changed bytes and 100 inserted"] < TimeSpan.FromSeconds(30), "Aligned binary comparison slowed down.");
        Assert.True(_times["Aligned binary, 256 MiB unrelated"] < TimeSpan.FromSeconds(30), "Aligned binary search slowed down.");
        // Budgets (docs/validation/TV-08.md): generous for shared machines, tight enough to catch the regressions found here.
        Assert.True(stoppedAfter < TimeSpan.FromMilliseconds(250), $"Canceling took {stoppedAfter}.");
        Assert.True(_times["Binary, 256 MiB identical (files)"] < TimeSpan.FromSeconds(2), "Binary comparison slowed down.");
        Assert.True(_times["1,000 scattered edits, 1,000,000 lines"] < TimeSpan.FromSeconds(10), "Text comparison slowed down.");
    }

    private static BinaryDiffResult Binary(string a, string b)
    {
        using var left = new FileContent(a);
        using var right = new FileContent(b);
        return BinaryDiff.Compare(left, right, TestContext.Current.CancellationToken);
    }

    private static AlignedBinaryResult Aligned(string a, string b)
    {
        using var left = new FileContent(a);
        using var right = new FileContent(b);
        return AlignedBinaryDiff.Compare(left, right, TestContext.Current.CancellationToken);
    }

    private sealed class FileContent(string path) : IContentSource
    {
        private readonly FileStream _stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.RandomAccess);
        public string DisplayName => path;
        public long Length => _stream.Length;
        public bool CanSeek => true;
        public string? LocalPath => path;
        public int Read(long offset, Span<byte> buffer) => RandomAccess.Read(_stream.SafeFileHandle, buffer, offset);
        public ContentRevision? GetRevision() => null;
        public void Dispose() => _stream.Dispose();
    }
}
