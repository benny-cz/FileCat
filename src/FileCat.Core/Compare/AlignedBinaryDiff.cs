using System.Numerics;
using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

/// <summary>
/// One stretch of an aligned binary comparison, in file order: equal bytes at their two offsets, bytes only one side
/// has (inserted or removed), bytes that differ in place (Changed), or what the work limits left unaligned.
/// </summary>
public sealed record AlignedRange(DiffKind Kind, long LeftOffset, long LeftLength, long RightOffset, long RightLength);

/// <param name="Ranges">In file order; together they cover both inputs completely.</param>
/// <param name="Complete">False when a work limit ended the search: the rest is one Unaligned range (it differs, nothing is paired there).</param>
/// <param name="BlockSize">The size of the blocks matched: shorter equal stretches are not recognized.</param>
public sealed record AlignedBinaryResult(IReadOnlyList<AlignedRange> Ranges, bool Complete, int BlockSize, long LeftLength, long RightLength)
{
    /// <summary>Bytes of the left input that were found again, in order, in the right one.</summary>
    public long EqualBytes => Ranges.Where(r => r.Kind == DiffKind.Equal).Sum(r => r.LeftLength);
}

/// <summary>
/// Aligned binary comparison (plan §16.2, ADR-11): finds content that moved because bytes were inserted or removed, a
/// heuristic view beside the exact same-offset comparison. The left input is indexed in fixed blocks by a rolling hash;
/// the right input is scanned byte by byte for blocks that match (verified byte for byte, then extended both ways), and
/// the matches that keep both inputs' order and cover the most bytes form the alignment. What lies between them is
/// changed, inserted, or removed. Equal stretches shorter than a block, and content repeated or moved within a file, can
/// be paired differently than a person would: the result is labelled heuristic, and only the exact comparison claims
/// that inputs are identical. Memory is bounded by the block and match limits; work by the bytes searched.
/// </summary>
public static class AlignedBinaryDiff
{
    /// <summary>At most this many blocks of the left input are indexed (the block size grows with the input).</summary>
    public const int MaxBlocks = 1 << 19;
    public const int MinBlock = 16;
    /// <summary>At most this many bytes of the right input are searched byte by byte (stretches found equal do not count); the rest is left unaligned.</summary>
    public const long DefaultBudget = 8L << 30;
    /// <summary>At most this many equal stretches are collected (repetitive content matches block by block); the rest is left unaligned.</summary>
    public const int MaxMatches = 1 << 18;
    private const ulong Multiplier = 0x100000001B3;
    /// <summary>Bits of the filter that rules out most windows before the index is asked (2 MiB).</summary>
    private const int FilterBits = 1 << 24;
    /// <summary>The largest single read while extending a match, and the index's read size.</summary>
    private const int MaxStep = 1 << 20;
    /// <summary>Stretches between matches up to this size are looked into for equal bytes.</summary>
    private const int MaxRefine = 1 << 20;
    /// <summary>Inside bytes changed in place, equal runs shorter than this stay part of the change (bytes agree by chance).</summary>
    public const int MinEqualRun = 8;

    /// <param name="progress">Bytes worked through so far, of both inputs' lengths together (the left input is read first).</param>
    public static AlignedBinaryResult Compare(IContentSource left, IContentSource right, CancellationToken ct, Action<long>? progress = null, long budget = DefaultBudget)
    {
        long leftLength = left.Length, rightLength = right.Length;
        if (leftLength < 0 || rightLength < 0)
            return new AlignedBinaryResult([new AlignedRange(DiffKind.Unaligned, 0, Math.Max(0, leftLength), 0, Math.Max(0, rightLength))], false, 0, leftLength, rightLength);
        int block = BlockSize(leftLength);
        var index = Index(left, leftLength, block, ct, progress);
        var matches = new List<Match>();
        Action<long>? scanned = progress is null ? null : at => progress(leftLength + at);
        bool complete = Scan(left, leftLength, right, rightLength, block, index, matches, ct, scanned, budget);
        var chain = LongestChain(matches);
        var ranges = new List<AlignedRange>();
        var x = new byte[Math.Min(MaxRefine, leftLength)];
        var y = new byte[Math.Min(MaxRefine, rightLength)];
        long lp = 0, rp = 0;
        foreach (var m in chain)
        {
            ct.ThrowIfCancellationRequested();
            Gap(ranges, left, lp, m.Left - lp, right, rp, m.Right - rp, x, y);
            AddEqual(ranges, m.Left, m.Right, m.Length);
            lp = m.Left + m.Length;
            rp = m.Right + m.Length;
        }
        if (complete) Gap(ranges, left, lp, leftLength - lp, right, rp, rightLength - rp, x, y);
        else ranges.Add(new AlignedRange(DiffKind.Unaligned, lp, leftLength - lp, rp, rightLength - rp));
        return new AlignedBinaryResult(ranges, complete, block, leftLength, rightLength);
    }

    /// <summary>The block size for an input: at least <see cref="MinBlock"/>, a power of two, and at most <see cref="MaxBlocks"/> blocks.</summary>
    public static int BlockSize(long length)
    {
        long wanted = Math.Max(MinBlock, (length + MaxBlocks - 1) / MaxBlocks);
        return (int)Math.Min(1 << 20, BitOperations.RoundUpToPowerOf2((ulong)wanted));
    }

    /// <summary>
    /// What lies between two equal stretches. Blocks find equal content only a block at a time, so a stretch up to
    /// <see cref="MaxRefine"/> bytes is looked into: bytes equal at its ends are equal, and where both sides are as long
    /// (bytes changed in place), so are runs of at least <see cref="MinEqualRun"/> equal bytes between the changes.
    /// </summary>
    private static void Gap(List<AlignedRange> ranges, IContentSource left, long lo, long ll, IContentSource right, long ro, long rl, byte[] x, byte[] y)
    {
        if (ll == 0 || rl == 0 || ll > x.Length || rl > y.Length)
        {
            AddDifferent(ranges, lo, ll, ro, rl);
            return;
        }
        var a = x.AsSpan(0, (int)ll);
        var b = y.AsSpan(0, (int)rl);
        ReadFully(left, lo, a);
        ReadFully(right, ro, b);
        int prefix = a.CommonPrefixLength(b);
        int most = Math.Min(a.Length, b.Length) - prefix, suffix = 0;
        while (suffix < most && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        AddEqual(ranges, lo, ro, prefix);
        a = a[prefix..^suffix];
        b = b[prefix..^suffix];
        long ml = lo + prefix, mr = ro + prefix;
        if (a.Length == b.Length)
        {
            int from = 0, i = 0;
            while (i < a.Length)
            {
                if (a[i] != b[i])
                {
                    i++;
                    continue;
                }
                int run = a[i..].CommonPrefixLength(b[i..]);
                if (run >= MinEqualRun)
                {
                    AddDifferent(ranges, ml + from, i - from, mr + from, i - from);
                    AddEqual(ranges, ml + i, mr + i, run);
                    from = i + run;
                }
                i += run;
            }
            AddDifferent(ranges, ml + from, a.Length - from, mr + from, a.Length - from);
        }
        else AddDifferent(ranges, ml, a.Length, mr, b.Length);
        AddEqual(ranges, lo + ll - suffix, ro + rl - suffix, suffix);
    }

    private static void AddDifferent(List<AlignedRange> ranges, long left, long leftLength, long right, long rightLength)
    {
        if (leftLength > 0 && rightLength > 0) ranges.Add(new AlignedRange(DiffKind.Changed, left, leftLength, right, rightLength));
        else if (leftLength > 0) ranges.Add(new AlignedRange(DiffKind.LeftOnly, left, leftLength, right, 0));
        else if (rightLength > 0) ranges.Add(new AlignedRange(DiffKind.RightOnly, left, 0, right, rightLength));
    }

    /// <summary>Adds an equal stretch, joined to the one before it when they touch.</summary>
    private static void AddEqual(List<AlignedRange> ranges, long left, long right, long length)
    {
        if (length <= 0) return;
        if (ranges.Count > 0 && ranges[^1] is { Kind: DiffKind.Equal } last && last.LeftOffset + last.LeftLength == left && last.RightOffset + last.RightLength == right)
            ranges[^1] = last with { LeftLength = last.LeftLength + length, RightLength = last.RightLength + length };
        else ranges.Add(new AlignedRange(DiffKind.Equal, left, length, right, length));
    }

    private readonly record struct Match(long Left, long Right, long Length);

    /// <summary>Hash of each whole block of the left input to the first block with it.</summary>
    private static Dictionary<ulong, long> Index(IContentSource left, long length, int block, CancellationToken ct, Action<long>? progress)
    {
        var index = new Dictionary<ulong, long>();
        // Both are powers of two, so the buffer holds whole blocks.
        var buffer = new byte[Math.Max(MaxStep, block)];
        long whole = length / block * block;
        for (long at = 0; at < whole;)
        {
            ct.ThrowIfCancellationRequested();
            int n = (int)Math.Min(buffer.Length, whole - at);
            ReadFully(left, at, buffer.AsSpan(0, n));
            for (int i = 0; i < n; i += block) index.TryAdd(Hash(buffer.AsSpan(i, block)), at + i);
            at += n;
            progress?.Invoke(at);
        }
        return index;
    }

    private static ulong Hash(ReadOnlySpan<byte> window)
    {
        ulong h = 0;
        foreach (byte b in window) h = h * Multiplier + b + 1;
        return h;
    }

    /// <summary>Scans the right input for indexed blocks, a byte at a time between matches; false when a limit ended the scan early.</summary>
    private static bool Scan(IContentSource left, long leftLength, IContentSource right, long rightLength, int block, Dictionary<ulong, long> index,
        List<Match> matches, CancellationToken ct, Action<long>? progress, long budget)
    {
        if (rightLength < block || index.Count == 0) return true;
        // One bit for each value of a hash's top 24 bits: most windows are ruled out without asking the index.
        var filter = new ulong[FilterBits / 64];
        foreach (ulong key in index.Keys) filter[key >> 46] |= 1UL << (int)(key >> 40 & 63);
        ulong top = 1; // Multiplier^(block-1): what the byte leaving the window contributed
        for (int k = 1; k < block; k++) top *= Multiplier;
        // The window and what follows it; read again from the window on when the scan reaches its end.
        var buffer = new byte[4 * Math.Max(MaxStep, block)];
        var candidate = new byte[block];
        var x = new byte[MaxStep];
        var y = new byte[MaxStep];
        long start = 0, pos = 0, lastEnd = 0, rolled = 0, reported = 0;
        int filled = Fill(right, rightLength, 0, buffer);
        ulong h = Hash(buffer.AsSpan(0, block));
        while (true)
        {
            int i = (int)(pos - start);
            if ((filter[h >> 46] & 1UL << (int)(h >> 40 & 63)) != 0 && index.TryGetValue(h, out long leftAt))
            {
                ReadFully(left, leftAt, candidate);
                if (candidate.AsSpan().SequenceEqual(buffer.AsSpan(i, block)))
                {
                    long forward = Common(left, leftLength, leftAt + block, right, rightLength, pos + block, x, y, ct, progress);
                    long backward = CommonBackward(left, leftAt, right, pos, Math.Min(leftAt, pos - lastEnd), x, y);
                    var m = new Match(leftAt - backward, pos - backward, backward + block + forward);
                    // Repetitive content finds the same left stretch again and again: the earlier find serves any
                    // chain the later one could (nothing between them in the right input), so only it is kept.
                    if (matches.Count == 0 || matches[^1].Left != m.Left || matches[^1].Length != m.Length) matches.Add(m);
                    lastEnd = pos = m.Right + m.Length;
                    if (pos + block > rightLength) return true;
                    if (matches.Count >= MaxMatches) return false;
                    if (pos + block > start + filled)
                    {
                        start = pos;
                        filled = Fill(right, rightLength, start, buffer);
                    }
                    h = Hash(buffer.AsSpan((int)(pos - start), block));
                    if (pos - reported >= MaxStep)
                    {
                        reported = pos;
                        ct.ThrowIfCancellationRequested();
                        progress?.Invoke(pos);
                    }
                    continue;
                }
            }
            if (pos + block >= rightLength) return true;
            if (i + block >= filled)
            {
                start = pos;
                filled = Fill(right, rightLength, start, buffer);
                i = 0;
            }
            // Roll the window one byte on: the first byte leaves, the next one comes in.
            h = (h - (ulong)(buffer[i] + 1) * top) * Multiplier + (ulong)(buffer[i + block] + 1);
            pos++;
            if (++rolled > budget) return false;
            if (pos - reported >= MaxStep)
            {
                reported = pos;
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(pos);
            }
        }
    }

    /// <summary>How many bytes from the two positions on are equal: read in steps that grow while they stay equal.</summary>
    private static long Common(IContentSource a, long aLength, long ap, IContentSource b, long bLength, long bp, byte[] x, byte[] y,
        CancellationToken ct, Action<long>? progress)
    {
        long total = 0;
        int step = 256;
        while (ap + total < aLength && bp + total < bLength)
        {
            int n = (int)Math.Min(step, Math.Min(aLength - ap - total, bLength - bp - total));
            ReadFully(a, ap + total, x.AsSpan(0, n));
            ReadFully(b, bp + total, y.AsSpan(0, n));
            int same = x.AsSpan(0, n).CommonPrefixLength(y.AsSpan(0, n));
            total += same;
            if (same < n) break;
            if (step < x.Length) step *= 2;
            else
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(bp + total);
            }
        }
        return total;
    }

    /// <summary>How many bytes before the two positions are equal, up to <paramref name="max"/>: read backwards in steps that grow.</summary>
    private static long CommonBackward(IContentSource a, long ap, IContentSource b, long bp, long max, byte[] x, byte[] y)
    {
        long total = 0;
        int step = 256;
        while (total < max)
        {
            int n = (int)Math.Min(step, max - total);
            ReadFully(a, ap - total - n, x.AsSpan(0, n));
            ReadFully(b, bp - total - n, y.AsSpan(0, n));
            if (!x.AsSpan(0, n).SequenceEqual(y.AsSpan(0, n)))
            {
                int same = 0;
                while (x[n - 1 - same] == y[n - 1 - same]) same++;
                return total + same;
            }
            total += n;
            step = Math.Min(x.Length, step * 2);
        }
        return total;
    }

    /// <summary>
    /// The matches that keep both inputs' order (in the right input they already do) and cover the most bytes: a
    /// weighted longest increasing chain, in O(n log n) with a Fenwick tree over where matches end in the left input.
    /// </summary>
    private static List<Match> LongestChain(List<Match> matches)
    {
        if (matches.Count == 0) return [];
        var ends = matches.Select(m => m.Left + m.Length).Distinct().Order().ToArray();
        var bestValue = new long[ends.Length + 1];
        var bestIndex = new int[ends.Length + 1];
        Array.Fill(bestIndex, -1);
        var previous = new int[matches.Count];
        var total = new long[matches.Count];
        int winner = -1;
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            // The best chain among matches that end in the left input no later than this one starts.
            int upTo = UpperBound(ends, m.Left);
            long best = 0;
            int from = -1;
            for (int k = upTo; k > 0; k -= k & -k)
                if (bestValue[k] > best)
                {
                    best = bestValue[k];
                    from = bestIndex[k];
                }
            previous[i] = from;
            total[i] = best + m.Length;
            int at = Array.BinarySearch(ends, m.Left + m.Length) + 1;
            for (int k = at; k <= ends.Length; k += k & -k)
                if (total[i] > bestValue[k])
                {
                    bestValue[k] = total[i];
                    bestIndex[k] = i;
                }
            if (winner < 0 || total[i] > total[winner]) winner = i;
        }
        var chain = new List<Match>();
        for (int i = winner; i >= 0; i = previous[i]) chain.Add(matches[i]);
        chain.Reverse();
        return chain;
    }

    /// <summary>How many of the sorted values are at most <paramref name="value"/> (the Fenwick prefix to query).</summary>
    private static int UpperBound(long[] sorted, long value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (sorted[mid] <= value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Reads as much of the input from <paramref name="at"/> on as the buffer holds; returns how much that was.</summary>
    private static int Fill(IContentSource source, long length, long at, byte[] buffer)
    {
        int n = (int)Math.Min(buffer.Length, length - at);
        ReadFully(source, at, buffer.AsSpan(0, n));
        return n;
    }

    private static void ReadFully(IContentSource source, long at, Span<byte> into)
    {
        int done = 0;
        while (done < into.Length)
        {
            int n = source.Read(at + done, into[done..]);
            if (n <= 0) throw new IOException("The content ended before its length.");
            done += n;
        }
    }
}
