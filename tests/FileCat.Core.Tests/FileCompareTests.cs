using System.Text;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class FileCompareTests
{
    private sealed class AdvertisedContent(byte[] bytes, long advertisedLength) : IContentSource
    {
        public string DisplayName => "owned compare input";
        public long Length => advertisedLength;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => null;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset >= bytes.Length) return 0;
            int n = Math.Min(257, Math.Min(buffer.Length, bytes.Length - (int)offset));
            bytes.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
        public void Dispose() { }
    }

    private sealed class ShrinkingFileContent(string path, long shortenedLength) : IContentSource
    {
        private readonly FileContentSource _source = new(path);
        private bool _shortened;
        public string DisplayName => _source.DisplayName;
        public long Length => _source.Length;
        public bool CanSeek => true;
        public string? LocalPath => _source.LocalPath;
        public ContentRevision? GetRevision() => _source.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            if (!_shortened)
            {
                _shortened = true;
                using var writer = new FileStream(LocalPath!, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                writer.SetLength(shortenedLength);
            }
            return _source.Read(offset, buffer);
        }
        public void Dispose() => _source.Dispose();
    }

    [Fact]
    public void Two_local_files_shrinking_during_comparison_are_not_reported_equal()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-compare-early-end-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string a = Path.Combine(root, "a.txt"), b = Path.Combine(root, "b.txt");
        byte[] original = Encoding.UTF8.GetBytes(new string('C', 8192));
        File.WriteAllBytes(a, original);
        File.WriteAllBytes(b, original);
        try
        {
            using var left = new ShrinkingFileContent(a, 4096);
            using var right = new ShrinkingFileContent(b, 4096);
            Assert.Throws<IOException>(() => BinaryDiff.Compare(left, right, TestContext.Current.CancellationToken));
            Assert.Equal(4096, new FileInfo(a).Length);
            Assert.Equal(4096, new FileInfo(b).Length);
            TestContext.Current.TestOutputHelper?.WriteLine($"originalLength={original.Length} finalLength={new FileInfo(a).Length} leftHash={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a)))} rightHash={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(b)))}");
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void Known_length_binary_compare_refuses_matching_truncated_prefixes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('A', 4096));
        using var left = new AdvertisedContent(bytes, bytes.Length + 512);
        using var right = new AdvertisedContent(bytes, bytes.Length + 512);
        var ct = TestContext.Current.CancellationToken;
        Assert.Throws<IOException>(() => BinaryDiff.Compare(left, right, ct));
    }

    [Fact]
    public void Known_length_binary_compare_refuses_a_truncated_longer_tail()
    {
        byte[] shortBytes = new byte[4096];
        byte[] longPrefix = new byte[1 << 20];
        using var left = new AdvertisedContent(shortBytes, shortBytes.Length);
        using var right = new AdvertisedContent(longPrefix, longPrefix.Length + 512);
        Assert.Throws<IOException>(() => BinaryDiff.Compare(left, right, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Known_length_text_compare_refuses_a_truncated_prefix()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('A', 4096));
        using var left = new AdvertisedContent(bytes, bytes.Length + 512);
        var ct = TestContext.Current.CancellationToken;
        Assert.Throws<IOException>(() => TextSide.Load(left, ct));
    }

    [Fact]
    public void Next_difference_refuses_truncated_known_length_content()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('B', 4096));
        using var left = new AdvertisedContent(bytes, bytes.Length + 512);
        using var right = new AdvertisedContent(bytes, bytes.Length + 512);
        var ct = TestContext.Current.CancellationToken;
        Assert.Throws<IOException>(() => BinaryDiff.NextDifference(left, right, left.Length, right.Length, 0, ct));
    }

    [Fact]
    public void Previous_difference_refuses_truncated_known_length_content()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('B', 4096));
        using var left = new AdvertisedContent(bytes, bytes.Length + 512);
        using var right = new AdvertisedContent(bytes, bytes.Length + 512);
        var ct = TestContext.Current.CancellationToken;
        Assert.Throws<IOException>(() => BinaryDiff.PreviousDifference(left, right, left.Length, right.Length, left.Length, ct));
    }

    [Fact]
    public void Unknown_length_compare_accepts_a_real_end_of_content()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("a\nb\n");
        using var left = new AdvertisedContent(bytes, -1);
        using var right = new AdvertisedContent(bytes, -1);
        Assert.True(BinaryDiff.Compare(left, right, TestContext.Current.CancellationToken).Equal);
        Assert.Equal(["a", "b"], TextSide.Load(left, TestContext.Current.CancellationToken).Lines);
    }

    [Fact]
    public void Text_comparison_refuses_to_materialize_more_than_one_million_lines()
    {
        string text = new('\n', 1_000_001);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Exception? result = Record.Exception(() => TextSide.FromText(text));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"inputChars={text.Length} allocatedBytes={allocated} result={result?.GetType().Name ?? "accepted"}");
        Assert.IsType<InvalidDataException>(result);
    }

    [Fact]
    public void Loaded_text_comparison_refuses_more_than_one_million_lines()
    {
        byte[] bytes = Enumerable.Repeat((byte)'\n', 1_000_001).ToArray();
        using var source = new MemoryContentSource("owned newline input", bytes);
        Assert.Throws<InvalidDataException>(() => TextSide.Load(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Text_comparison_accepts_the_one_million_line_boundary()
    {
        var side = TextSide.FromText(new string('\n', 1_000_000));
        Assert.Equal(1_000_000, side.Lines.Count);
        Assert.Equal(string.Empty, side.Lines[0]);
        Assert.Equal(string.Empty, side.Lines[^1]);
    }

    private static TextDiffResult Diff(string left, string right, TextDiffOptions? options = null) =>
        TextDiff.Compare(TextSide.FromText(left).Lines, TextSide.FromText(right).Lines, options, TestContext.Current.CancellationToken);

    /// <summary>Applying the blocks to the left side must give the right side: the result is a real edit script.</summary>
    private static void AssertValid(IReadOnlyList<string> a, IReadOnlyList<string> b, TextDiffResult result)
    {
        int la = 0, lb = 0;
        foreach (var block in result.Blocks)
        {
            Assert.Equal(la, block.LeftStart);
            Assert.Equal(lb, block.RightStart);
            if (block.Kind == DiffKind.Equal)
                for (int i = 0; i < block.LeftCount; i++) Assert.Equal(a[la + i], b[lb + i]);
            la += block.LeftCount;
            lb += block.RightCount;
        }
        Assert.Equal(a.Count, la);
        Assert.Equal(b.Count, lb);
    }

    [Fact]
    public void Identical_texts_have_no_differences_and_line_endings_are_reported_not_normalized()
    {
        var crlf = TextSide.FromText("one\r\ntwo\r\nthree");
        var lf = TextSide.FromText("one\ntwo\nthree");
        var result = TextDiff.Compare(crlf.Lines, lf.Lines, ct: TestContext.Current.CancellationToken);
        Assert.True(result.Identical);
        Assert.True(TextDiff.TerminatorDiffers(crlf, 0, lf, 0));
        Assert.False(TextDiff.TerminatorDiffers(crlf, 2, lf, 2)); // the last line has none on either side
        Assert.Equal(["one", "two", "three"], crlf.Lines);
    }

    [Fact]
    public void Insertions_deletions_and_changes_are_aligned_minimally()
    {
        var a = "a\nb\nc\nd\ne\nf".Split('\n');
        var b = "a\nX\nc\nd\nnew\ne\nf".Split('\n');
        var result = TextDiff.Compare(a, b, ct: TestContext.Current.CancellationToken);
        AssertValid(a, b, result);
        Assert.Equal(2, result.Differences);
        Assert.Contains(result.Blocks, bl => bl is { Kind: DiffKind.Changed, LeftStart: 1, LeftCount: 1, RightStart: 1 });
        Assert.Contains(result.Blocks, bl => bl is { Kind: DiffKind.RightOnly, RightStart: 4, RightCount: 1 });
        Assert.False(result.Approximate);
    }

    [Fact]
    public void Shifted_and_repeated_blocks_stay_valid_and_options_ignore_whitespace_or_case()
    {
        var rng = new Random(7);
        for (int round = 0; round < 200; round++)
        {
            // Small alphabets make many repeated lines, the case patience anchors cannot help with.
            var a = Enumerable.Range(0, rng.Next(0, 60)).Select(_ => "L" + rng.Next(0, 6)).ToList();
            var b = new List<string>(a);
            for (int e = rng.Next(0, 10); e > 0; e--)
            {
                int at = rng.Next(0, b.Count + 1);
                if (rng.Next(3) == 0 && b.Count > 0 && at < b.Count) b.RemoveAt(at);
                else b.Insert(at, "L" + rng.Next(0, 8));
            }
            AssertValid(a, b, TextDiff.Compare(a, b, ct: TestContext.Current.CancellationToken));
        }
        Assert.True(Diff("A  b\nc", "a b\nC", new TextDiffOptions(IgnoreWhitespace: true, IgnoreCase: true)).Identical);
        Assert.False(Diff("A  b", "a b").Identical);
    }

    [Fact]
    public void A_region_over_budget_is_labelled_unaligned_never_guessed()
    {
        var a = Enumerable.Range(0, 400).Select(i => "x" + (i % 3)).ToList();
        var b = Enumerable.Range(0, 400).Select(i => "y" + (i % 3)).Concat(a.Take(10)).ToList();
        var result = TextDiff.Compare(a, b, new TextDiffOptions { RegionBudget = 50 }, TestContext.Current.CancellationToken);
        Assert.True(result.Approximate);
        Assert.Contains(result.Blocks, bl => bl.Kind == DiffKind.Unaligned);
        AssertValid(a, b, result);
        Assert.False(TextDiff.Compare(a, b, ct: TestContext.Current.CancellationToken).Approximate);
    }

    [Fact]
    public void Within_line_changes_are_words_and_never_split_surrogates_or_combining_marks()
    {
        var spans = InlineDiff.Compare("the quick brown fox", "the quick red fox")!.Value;
        Assert.Equal([(10, 5)], spans.Left);
        Assert.Equal([(10, 3)], spans.Right);
        // "é" as e + combining acute stays one unit with its word; an emoji (surrogate pair) is one token.
        var tokens = InlineDiff.Tokens("café 😀!");
        Assert.Equal(["café", " ", "😀", "!"], tokens.Select(t => "café 😀!".Substring(t.Start, t.Length)));
        var emoji = InlineDiff.Compare("a 😀 b", "a 😁 b")!.Value;
        Assert.Equal([(2, 2)], emoji.Left);
        Assert.Null(InlineDiff.Compare(new string('x', InlineDiff.MaxLineLength + 1), "y"));
    }

    [Fact]
    public void Binary_comparison_is_exact_with_ranges_and_length_differences()
    {
        var left = new byte[3 * (1 << 20) + 5];
        new Random(1).NextBytes(left);
        var right = (byte[])left.Clone();
        Assert.True(Compare(left, right).Equal);
        right[10] ^= 1;
        right[11] ^= 1;
        right[(1 << 20) - 1] ^= 1; // a run across a chunk boundary
        right[1 << 20] ^= 1;
        var result = Compare(left, right);
        Assert.False(result.Equal);
        Assert.Equal([(10L, 2L), ((1L << 20) - 1, 2L)], result.Ranges);
        var longer = right.Concat(new byte[7]).ToArray();
        var withTail = Compare(left, longer);
        Assert.Equal((left.Length, (long)longer.Length), (withTail.LeftLength, withTail.RightLength));
        Assert.Equal((left.LongLength, 7L), withTail.Ranges[^1]);
        Assert.True(Compare([], []).Equal);
        Assert.Equal([(0L, 3L)], Compare([], [1, 2, 3]).Ranges);

        static BinaryDiffResult Compare(byte[] a, byte[] b) =>
            BinaryDiff.Compare(new MemoryContentSource("a", a), new MemoryContentSource("b", b), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Binary_ranges_match_a_byte_by_byte_reference_across_chunk_boundaries()
    {
        var rng = new Random(11);
        for (int round = 0; round < 20; round++)
        {
            int length = rng.Next(1, 3 * 1024 * 1024);
            var a = new byte[length];
            rng.NextBytes(a);
            var b = (byte[])a.Clone();
            for (int e = rng.Next(0, 50); e > 0; e--)
            {
                int start = rng.Next(length), run = Math.Min(length - start, rng.Next(1, 3000));
                for (int i = start; i < start + run; i++) b[i] ^= (byte)rng.Next(1, 256);
            }
            if (length > (1 << 20) + 10) for (int i = (1 << 20) - 5; i < (1 << 20) + 5; i++) b[i] ^= 0xFF; // across the read chunks
            var expected = new List<(long Offset, long Length)>();
            long runStart = -1;
            for (int i = 0; i < length; i++)
            {
                if (a[i] != b[i])
                {
                    if (runStart < 0) runStart = i;
                }
                else if (runStart >= 0)
                {
                    expected.Add((runStart, i - runStart));
                    runStart = -1;
                }
            }
            if (runStart >= 0) expected.Add((runStart, length - runStart));
            var result = BinaryDiff.Compare(new MemoryContentSource("a", a), new MemoryContentSource("b", b), TestContext.Current.CancellationToken);
            Assert.Equal(expected, result.Ranges);
            Assert.Equal(expected.Count == 0, result.Equal);
        }
    }

    [Fact]
    public void Next_and_previous_differences_walk_the_comparisons_runs_both_ways()
    {
        var ct = TestContext.Current.CancellationToken;
        var rng = new Random(12);
        foreach (int extra in new[] { 0, 1000, -700 })
        {
            var a = new byte[(5 << 19) + 3];
            rng.NextBytes(a);
            var b = (byte[])a.Clone();
            // Runs of all lengths, some across the 1 MiB reads, one at the very start and one at the common end.
            for (int i = 0; i < 300; i++)
            {
                int at = rng.Next(a.Length), length = rng.Next(1, 40);
                for (int j = at; j < Math.Min(a.Length, at + length); j++) b[j] ^= 0x5A;
            }
            foreach (int at in new[] { 0, (1 << 20) - 2, 1 << 20, (2 << 20) + 5, a.Length - 1 })
                for (int j = at; j < Math.Min(a.Length, at + 4); j++) b[j] ^= 0x33;
            b = extra >= 0 ? [.. b, .. new byte[extra]] : b[..^(-extra)];
            var left = new MemoryContentSource("a", a);
            var right = new MemoryContentSource("b", b);
            var expected = BinaryDiff.Compare(left, right, ct).Ranges;

            var forward = new List<(long, long)>();
            for (long from = 0; BinaryDiff.NextDifference(left, right, a.Length, b.Length, from, ct) is { } run; from = run.Offset + run.Length) forward.Add(run);
            Assert.Equal(expected, forward);

            var backward = new List<(long, long)>();
            for (long before = Math.Max(a.Length, b.Length); BinaryDiff.PreviousDifference(left, right, a.Length, b.Length, before, ct) is { } run; before = run.Offset) backward.Add(run);
            backward.Reverse();
            Assert.Equal(expected, backward);
        }
    }

    [Fact]
    public void Large_similar_and_dissimilar_inputs_finish_quickly()
    {
        var rng = new Random(3);
        var a = Enumerable.Range(0, 200_000).Select(i => $"line {i} {rng.Next()}").ToList();
        var b = new List<string>(a);
        for (int e = 0; e < 200; e++) b[rng.Next(b.Count)] = "edited " + e;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var similar = TextDiff.Compare(a, b, ct: TestContext.Current.CancellationToken);
        Assert.InRange(similar.Differences, 1, 200);
        Assert.False(similar.Approximate);
        var unrelated = Enumerable.Range(0, 20_000).Select(i => "other " + (i % 50)).ToList();
        var dissimilar = TextDiff.Compare(a.Take(20_000).Select(l => l[..6]).ToList(), unrelated, ct: TestContext.Current.CancellationToken);
        Assert.True(dissimilar.Differences > 0);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}");
    }
}
