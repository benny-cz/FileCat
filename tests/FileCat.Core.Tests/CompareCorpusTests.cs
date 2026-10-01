using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// V13's comparison corpus: generated pairs of every kind the plan names (identical, shifted, repetitive, unrelated,
/// giant-line, truncated, edited, moved) checked against references written here, not against FileCat's own answers:
/// no false equality, every "equal" claim true byte for byte, every difference reachable, "approximate" exactly when a
/// region was left unaligned. FILECAT_COMPARE_CORPUS_CASES (default 2,000) and FILECAT_COMPARE_CORPUS_SEED vary it.
/// </summary>
public sealed class CompareCorpusTests
{
    private static int Cases => int.TryParse(Environment.GetEnvironmentVariable("FILECAT_COMPARE_CORPUS_CASES"), out int n) && n > 0 ? n : 2000;

    private static int Seed => int.TryParse(Environment.GetEnvironmentVariable("FILECAT_COMPARE_CORPUS_SEED"), out int s) ? s : 1309;

    /// <summary>One generated pair of line lists, and what made it (for the failure message).</summary>
    private sealed record TextCase(string Kind, List<string> Left, List<string> Right, TextDiffOptions Options);

    private static TextCase NextTextCase(Random rng, int index)
    {
        // A small vocabulary makes repeated lines likely, a large one unique lines; both matter to the anchors.
        int vocabulary = rng.Next(3) switch { 0 => 3, 1 => 40, _ => 100_000 };
        string Line() => rng.Next(20) == 0 ? "" : "line " + rng.Next(vocabulary);
        List<string> Lines(int count) => [.. Enumerable.Range(0, count).Select(_ => Line())];
        var options = rng.Next(4) switch
        {
            0 => new TextDiffOptions(IgnoreWhitespace: true),
            1 => new TextDiffOptions(IgnoreCase: true),
            // A tight budget forces regions to stay unaligned, which must then be labelled.
            2 => new TextDiffOptions { RegionBudget = rng.Next(1, 400) },
            _ => new TextDiffOptions(),
        };
        var left = Lines(rng.Next(0, 160));
        string kind = (index % 8) switch { 0 => "identical", 1 => "shifted", 2 => "repetitive", 3 => "unrelated", 4 => "truncated", 5 => "case and spaces", 6 => "giant line", _ => "edited" };
        List<string> right;
        switch (kind)
        {
            case "identical":
                right = [.. left];
                break;
            case "shifted":
                right = [.. Lines(rng.Next(1, 30)), .. left];
                break;
            case "repetitive":
                left = [.. Enumerable.Range(0, rng.Next(1, 200)).Select(_ => "same " + rng.Next(2))];
                right = [.. left];
                Edit(rng, right, Line, rng.Next(1, 6));
                break;
            case "unrelated":
                right = [.. Enumerable.Range(0, rng.Next(0, 160)).Select(_ => "other " + rng.Next(1000))];
                break;
            case "truncated":
                right = [.. left.Take(rng.Next(0, left.Count + 1))];
                break;
            case "case and spaces":
                right = [.. left.Select(l => rng.Next(3) switch { 0 => l.ToUpperInvariant(), 1 => " " + l.Replace(" ", "  ") + "\t", _ => l })];
                break;
            case "giant line":
                // Far longer than within-line detail goes (InlineDiff.MaxLineLength): still compared whole.
                string giant = new('x', InlineDiff.MaxLineLength + rng.Next(1, 50_000));
                left.Insert(rng.Next(left.Count + 1), giant);
                right = [.. left];
                int at = right.IndexOf(giant);
                right[at] = rng.Next(2) == 0 ? giant : giant[..^1] + "y";
                break;
            default:
                right = [.. left];
                Edit(rng, right, Line, rng.Next(1, 12));
                break;
        }
        return new TextCase(kind, left, right, options);
    }

    private static void Edit(Random rng, List<string> lines, Func<string> line, int edits)
    {
        for (int e = 0; e < edits; e++)
        {
            int at = rng.Next(lines.Count + 1);
            switch (rng.Next(3))
            {
                case 0: lines.Insert(at, line()); break;
                case 1 when at < lines.Count: lines.RemoveAt(at); break;
                default: if (at < lines.Count) lines[at] = line(); break;
            }
        }
    }

    /// <summary>How TextDiff's options make two lines the same, written again here from their meaning: whitespace left
    /// out, letters compared without their case, character by character.</summary>
    private static string Normal(string line, TextDiffOptions options) =>
        string.Concat(line.Where(c => !(options.IgnoreWhitespace && char.IsWhiteSpace(c))).Select(c => options.IgnoreCase ? char.ToLowerInvariant(c) : c));

    /// <summary>The longest common subsequence's length, by the textbook table (a reference with nothing in common with TextDiff).</summary>
    private static int Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var previous = new int[b.Count + 1];
        var current = new int[b.Count + 1];
        for (int i = 1; i <= a.Count; i++)
        {
            for (int j = 1; j <= b.Count; j++)
                current[j] = a[i - 1] == b[j - 1] ? previous[j - 1] + 1 : Math.Max(previous[j], current[j - 1]);
            (previous, current) = (current, previous);
        }
        return previous[b.Count];
    }

    [Fact]
    public void Text_comparison_never_claims_equality_that_is_not_there_and_labels_what_it_did_not_align()
    {
        var ct = TestContext.Current.CancellationToken;
        var rng = new Random(Seed);
        int shorter = 0, approximate = 0, heuristic = 0, worst = 0, worstExact = 0;
        var shorterKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < Cases; index++)
        {
            var c = NextTextCase(rng, index);
            var result = TextDiff.Compare(c.Left, c.Right, c.Options, ct);
            string what = $"case {index} ({c.Kind}, seed {Seed}, {c.Left.Count} against {c.Right.Count} lines, {c.Options})";
            var a = c.Left.Select(l => Normal(l, c.Options)).ToList();
            var b = c.Right.Select(l => Normal(l, c.Options)).ToList();
            // The blocks cover both sides in order, each of the shape its kind says, and equal blocks are equal.
            int la = 0, lb = 0, equal = 0;
            foreach (var block in result.Blocks)
            {
                Assert.True(block.LeftStart == la && block.RightStart == lb, $"{what}: a block starts at {block.LeftStart}/{block.RightStart}, not {la}/{lb}");
                switch (block.Kind)
                {
                    case DiffKind.Equal:
                        Assert.True(block.LeftCount == block.RightCount, $"{what}: an equal block of {block.LeftCount} and {block.RightCount} lines");
                        for (int i = 0; i < block.LeftCount; i++)
                            Assert.True(a[la + i] == b[lb + i], $"{what}: lines {la + i} and {lb + i} called equal: \"{c.Left[la + i]}\" and \"{c.Right[lb + i]}\"");
                        equal += block.LeftCount;
                        break;
                    case DiffKind.LeftOnly:
                        Assert.True(block.LeftCount > 0 && block.RightCount == 0, $"{what}: a left-only block of {block.LeftCount}/{block.RightCount}");
                        break;
                    case DiffKind.RightOnly:
                        Assert.True(block.LeftCount == 0 && block.RightCount > 0, $"{what}: a right-only block of {block.LeftCount}/{block.RightCount}");
                        break;
                    case DiffKind.Changed:
                        Assert.True(block.LeftCount > 0 && block.RightCount > 0, $"{what}: a changed block of {block.LeftCount}/{block.RightCount}");
                        break;
                    default:
                        Assert.True(block.LeftCount + block.RightCount > 0, $"{what}: an empty unaligned block");
                        break;
                }
                la += block.LeftCount;
                lb += block.RightCount;
            }
            Assert.True(la == a.Count && lb == b.Count, $"{what}: the blocks cover {la}/{lb} lines");
            // No false equality, and the count of differences is the count of blocks that are not equal.
            Assert.True(result.Identical == a.SequenceEqual(b), $"{what}: identical {result.Identical}");
            int notEqual = result.Blocks.Count(x => x.Kind != DiffKind.Equal);
            Assert.True(notEqual == result.Differences, $"{what}: {result.Differences} differences for {notEqual} blocks that are not equal: {string.Join(", ", result.Blocks.Select(x => $"{x.Kind} {x.LeftStart}+{x.LeftCount}/{x.RightStart}+{x.RightCount}"))}");
            Assert.True(result.Approximate == result.Blocks.Any(x => x.Kind == DiffKind.Unaligned), $"{what}: approximate {result.Approximate}");
            // Never more lines called equal than the sides have in common, and exactly as many unless labelled: a result
            // neither approximate nor heuristic shows the fewest differences possible.
            int lcs = Lcs(a, b);
            Assert.True(equal <= lcs, $"{what}: {equal} lines called equal, {lcs} in common");
            Assert.True(equal == lcs || result.Approximate || result.Heuristic, $"{what}: {equal} lines aligned of {lcs} in common, and not labelled");
            if (result.Heuristic) heuristic++;
            if (equal < lcs)
            {
                shorter++;
                string key = c.Kind + (result.Approximate ? ", approximate" : "") + (result.Heuristic ? ", heuristic" : "");
                shorterKinds[key] = shorterKinds.GetValueOrDefault(key) + 1;
                worst = Math.Max(worst, lcs - equal);
                if (!result.Approximate && !result.Heuristic && lcs - equal > worstExact)
                {
                    worstExact = lcs - equal;
                    // FILECAT_COMPARE_CORPUS_DUMP: the worst such pair as two files, to look at with other tools.
                    if (Environment.GetEnvironmentVariable("FILECAT_COMPARE_CORPUS_DUMP") is { Length: > 0 } dump)
                    {
                        Directory.CreateDirectory(dump);
                        File.WriteAllLines(Path.Combine(dump, "left.txt"), c.Left);
                        File.WriteAllLines(Path.Combine(dump, "right.txt"), c.Right);
                        File.WriteAllLines(Path.Combine(dump, "case.txt"), [$"{what}: {equal} lines aligned, {lcs} in common",
                            .. result.Blocks.Select(x => $"{x.Kind} {x.LeftStart}+{x.LeftCount}/{x.RightStart}+{x.RightCount}")]);
                    }
                }
            }
            if (result.Approximate) approximate++;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{Cases} text cases (seed {Seed}): {approximate} approximate and {heuristic} heuristic (labelled), {shorter} aligned fewer lines than they have in common " +
            $"(at most {worst} fewer, {worstExact} where nothing was left unaligned or split on unique lines; {string.Join(", ", shorterKinds.Select(k => $"{k.Key} {k.Value}"))})");
    }

    [Fact]
    public void Every_difference_can_be_reached_beyond_the_ranges_listed()
    {
        // Twice as many differing runs as the comparison lists: the rest are reached with next and previous.
        var ct = TestContext.Current.CancellationToken;
        var rng = new Random(Seed + 1);
        var a = new byte[3 * 1024 * 1024 + 17];
        rng.NextBytes(a);
        var b = (byte[])a.Clone();
        var expected = new List<(long Offset, long Length)>();
        for (long at = 3; at < a.Length - 4 && expected.Count < 2 * BinaryDiff.MaxRanges; at += rng.Next(40, 140))
        {
            int length = rng.Next(1, 4);
            for (long i = at; i < at + length; i++) b[i] ^= 0xA5;
            expected.Add((at, length));
        }
        var left = new MemoryContentSource("a", a);
        var right = new MemoryContentSource("b", b);
        var result = BinaryDiff.Compare(left, right, ct);
        Assert.False(result.Equal);
        Assert.True(result.RangesTruncated);
        Assert.Equal(expected.Take(BinaryDiff.MaxRanges), result.Ranges);
        var forward = new List<(long, long)>();
        for (long from = 0; BinaryDiff.NextDifference(left, right, a.Length, b.Length, from, ct) is { } run; from = run.Offset + run.Length) forward.Add(run);
        Assert.Equal(expected, forward);
        var backward = new List<(long, long)>();
        for (long before = a.Length; BinaryDiff.PreviousDifference(left, right, a.Length, b.Length, before, ct) is { } run; before = run.Offset) backward.Add(run);
        backward.Reverse();
        Assert.Equal(expected, backward);
    }

    [Fact]
    public void Aligned_binary_comparison_covers_both_sides_and_its_equal_ranges_are_equal()
    {
        var ct = TestContext.Current.CancellationToken;
        var rng = new Random(Seed + 2);
        int cases = Math.Max(1, Cases / 10), complete = 0, moved = 0;
        for (int index = 0; index < cases; index++)
        {
            var a = new byte[rng.Next(0, 200_000)];
            if (index % 3 == 0) for (int i = 0; i < a.Length; i++) a[i] = (byte)(i % 7); // repetitive
            else rng.NextBytes(a);
            var b = new List<byte>(a);
            string kind = (index % 5) switch { 0 => "identical", 1 => "shifted", 2 => "truncated", 3 => "moved", _ => "edited" };
            switch (kind)
            {
                case "shifted":
                    b.InsertRange(0, Enumerable.Range(0, rng.Next(1, 5000)).Select(_ => (byte)rng.Next(256)));
                    break;
                case "truncated":
                    int keep = rng.Next(b.Count + 1);
                    b.RemoveRange(keep, b.Count - keep);
                    break;
                case "moved" when b.Count > 20:
                    int from = rng.Next(b.Count / 2), length = rng.Next(1, b.Count / 2);
                    var piece = b.GetRange(from, length);
                    b.RemoveRange(from, length);
                    b.InsertRange(rng.Next(b.Count + 1), piece);
                    moved++;
                    break;
                case "edited":
                    for (int e = rng.Next(1, 20); e > 0; e--)
                    {
                        int at = rng.Next(b.Count + 1);
                        switch (rng.Next(3))
                        {
                            case 0: b.InsertRange(at, Enumerable.Range(0, rng.Next(1, 300)).Select(_ => (byte)rng.Next(256))); break;
                            case 1 when at < b.Count: b.RemoveRange(at, Math.Min(b.Count - at, rng.Next(1, 300))); break;
                            default: if (at < b.Count) b[at] ^= 0xFF; break;
                        }
                    }
                    break;
            }
            var right = b.ToArray();
            string what = $"aligned case {index} ({kind}, seed {Seed + 2}, {a.Length} against {right.Length} bytes)";
            var result = AlignedBinaryDiff.Compare(new MemoryContentSource("a", a), new MemoryContentSource("b", right), ct);
            long la = 0, lb = 0;
            foreach (var range in result.Ranges)
            {
                Assert.True(range.LeftOffset == la && range.RightOffset == lb, $"{what}: a range at {range.LeftOffset}/{range.RightOffset}, not {la}/{lb}");
                if (range.Kind == DiffKind.Equal)
                {
                    Assert.True(range.LeftLength == range.RightLength, $"{what}: an equal range of {range.LeftLength} and {range.RightLength} bytes");
                    Assert.True(a.AsSpan((int)la, (int)range.LeftLength).SequenceEqual(right.AsSpan((int)lb, (int)range.RightLength)), $"{what}: bytes at {la} and {lb} called equal");
                }
                la += range.LeftLength;
                lb += range.RightLength;
            }
            Assert.True(la == a.Length && lb == right.Length, $"{what}: the ranges cover {la}/{lb} bytes");
            bool same = a.AsSpan().SequenceEqual(right);
            Assert.True(same == result.Ranges.All(r => r.Kind == DiffKind.Equal), $"{what}: {(same ? "the same bytes shown with differences" : "different bytes shown as equal")}");
            if (result.Complete) complete++;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{cases} aligned cases (seed {Seed + 2}): {complete} complete, {moved} with a moved block");
    }
}
