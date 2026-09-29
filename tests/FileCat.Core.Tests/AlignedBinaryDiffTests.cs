using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Aligned binary comparison (plan §16.2): shifted content is found again; the ranges cover both inputs exactly.</summary>
public sealed class AlignedBinaryDiffTests
{
    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static AlignedBinaryResult Align(byte[] left, byte[] right, long budget = AlignedBinaryDiff.DefaultBudget) =>
        AlignedBinaryDiff.Compare(new MemoryContentSource("left", left), new MemoryContentSource("right", right), TestContext.Current.CancellationToken, null, budget);

    /// <summary>The ranges follow each other without gaps or overlaps on both sides, and equal ranges hold equal bytes.</summary>
    private static void Covers(AlignedBinaryResult result, byte[] left, byte[] right)
    {
        long l = 0, r = 0;
        foreach (var range in result.Ranges)
        {
            Assert.Equal(l, range.LeftOffset);
            Assert.Equal(r, range.RightOffset);
            if (range.Kind == DiffKind.Equal)
                Assert.True(left.AsSpan((int)range.LeftOffset, (int)range.LeftLength).SequenceEqual(right.AsSpan((int)range.RightOffset, (int)range.RightLength)));
            l += range.LeftLength;
            r += range.RightLength;
        }
        Assert.Equal(left.Length, l);
        Assert.Equal(right.Length, r);
    }

    [Fact]
    public void Inserted_and_removed_bytes_leave_the_rest_aligned()
    {
        var left = Random(100_000, 1);
        var inserted = left[..50_000].Concat(new byte[37]).Concat(left[50_000..]).ToArray();
        var result = Align(left, inserted);
        Covers(result, left, inserted);
        Assert.True(result.Complete);
        Assert.Equal([DiffKind.Equal, DiffKind.RightOnly, DiffKind.Equal], result.Ranges.Select(r => r.Kind));
        Assert.Equal(37, result.Ranges[1].RightLength);
        Assert.Equal(100_000, result.EqualBytes);

        var removed = left[..20_000].Concat(left[21_000..]).ToArray();
        var gone = Align(left, removed);
        Covers(gone, left, removed);
        Assert.Equal([DiffKind.Equal, DiffKind.LeftOnly, DiffKind.Equal], gone.Ranges.Select(r => r.Kind));
        Assert.Equal(1_000, gone.Ranges[1].LeftLength);
    }

    [Fact]
    public void Bytes_changed_in_place_and_moved_blocks_are_what_differs()
    {
        var left = Random(64_000, 2);
        var changed = (byte[])left.Clone();
        for (int i = 30_000; i < 30_100; i++) changed[i] ^= 0xFF;
        var inPlace = Align(left, changed);
        Covers(inPlace, left, changed);
        var middle = Assert.Single(inPlace.Ranges, r => r.Kind != DiffKind.Equal);
        Assert.Equal(DiffKind.Changed, middle.Kind);
        Assert.Equal((30_000, 100L), (middle.LeftOffset, middle.LeftLength));

        // Changes closer together than a block: the equal bytes between them are found too, unless too few to tell.
        var near = (byte[])left.Clone();
        foreach (int at in new[] { 5_000, 5_020, 5_023, 40_000 }) near[at] ^= 0x5A;
        var close = Align(left, near);
        Covers(close, left, near);
        Assert.Equal([(5_000L, 1L), (5_020, 4), (40_000, 1)], close.Ranges.Where(r => r.Kind != DiffKind.Equal).Select(r => (r.LeftOffset, r.LeftLength)));
        Assert.Equal(left.Length - 6, close.EqualBytes);

        // A block moved to the end: order is kept, so it shows as removed in one place and inserted in the other.
        var moved = left[..10_000].Concat(left[20_000..]).Concat(left[10_000..20_000]).ToArray();
        var shifted = Align(left, moved);
        Covers(shifted, left, moved);
        Assert.Equal(54_000, shifted.EqualBytes);
        Assert.Contains(shifted.Ranges, r => r.Kind is DiffKind.LeftOnly or DiffKind.Changed);
    }

    [Fact]
    public void Unrelated_empty_and_tiny_inputs_still_cover_everything()
    {
        var a = Random(5_000, 3);
        var b = Random(7_000, 4);
        var unrelated = Align(a, b);
        Covers(unrelated, a, b);
        Assert.Equal(0, unrelated.EqualBytes);

        Covers(Align([], b), [], b);
        Covers(Align(a, []), a, []);
        Covers(Align([1, 2, 3], [1, 2, 3, 4]), [1, 2, 3], [1, 2, 3, 4]);
        var same = Align(a, a);
        Assert.Equal([DiffKind.Equal], same.Ranges.Select(r => r.Kind));
    }

    [Fact]
    public void Repetitive_content_finishes_and_says_what_was_inserted()
    {
        // Every 16 bytes of the right input match the whole left input: one stretch pairs, the rest is inserted.
        var zeros = Align(new byte[16], new byte[16 << 20]);
        Assert.True(zeros.Complete);
        Assert.Equal([DiffKind.Equal, DiffKind.RightOnly], zeros.Ranges.Select(r => r.Kind));
        Assert.Equal((16 << 20) - 16, zeros.Ranges[1].RightLength);

        // Two patterns taking turns match two left stretches in turn: the match limit ends the search, and says so.
        var left = Random(48, 7);
        var turns = new byte[(AlignedBinaryDiff.MaxMatches + 10) * 16];
        for (int i = 0; i < turns.Length; i += 16) left.AsSpan(i / 16 % 2 * 32, 16).CopyTo(turns.AsSpan(i));
        var limited = Align(left, turns);
        Covers(limited, left, turns);
        Assert.False(limited.Complete);
        Assert.Equal(DiffKind.Unaligned, limited.Ranges[^1].Kind);
    }

    [Fact]
    public void Scattered_pieces_are_read_a_few_times_at_most()
    {
        // Shuffled 4 KiB pieces: each find is verified and extended with reads that start small.
        var left = Random(8 << 20, 8);
        int pieces = left.Length / 4096;
        var order = Enumerable.Range(0, pieces).OrderBy(i => (i * 7919) % pieces).ToArray();
        var right = new byte[left.Length];
        for (int i = 0; i < pieces; i++) left.AsSpan(order[i] * 4096, 4096).CopyTo(right.AsSpan(i * 4096));
        var l = new CountingSource(left);
        var r = new CountingSource(right);
        var result = AlignedBinaryDiff.Compare(l, r, TestContext.Current.CancellationToken);
        Covers(result, left, right);
        Assert.True(l.Read + r.Read < 8L * (left.Length + right.Length), $"{l.Read + r.Read:N0} bytes read");
    }

    private sealed class CountingSource(byte[] bytes) : IContentSource
    {
        public long Read { get; private set; }
        public string DisplayName => "counting";
        public long Length => bytes.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        int IContentSource.Read(long offset, Span<byte> buffer)
        {
            int n = (int)Math.Min(buffer.Length, bytes.Length - offset);
            if (n <= 0) return 0;
            bytes.AsSpan((int)offset, n).CopyTo(buffer);
            Read += n;
            return n;
        }
        public ContentRevision? GetRevision() => null;
        public void Dispose() { }
    }

    [Fact]
    public void A_budget_leaves_the_rest_unaligned_and_cancellation_stops()
    {
        var left = Random(4 << 20, 5);
        var right = Random(4 << 20, 6);
        var partial = Align(left, right, budget: 1 << 20);
        Assert.False(partial.Complete);
        Assert.Equal(DiffKind.Unaligned, partial.Ranges[^1].Kind);
        Covers(partial, left, right);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            AlignedBinaryDiff.Compare(new MemoryContentSource("l", left), new MemoryContentSource("r", right), canceled.Token));
    }
}
