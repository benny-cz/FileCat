using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

/// <param name="Ranges">Differing byte runs at the same offsets; a length difference is a final range past the shorter end.</param>
/// <param name="RangesTruncated">More ranges exist than were kept (<see cref="BinaryDiff.MaxRanges"/>); equality is still exact.</param>
public sealed record BinaryDiffResult(bool Equal, long LeftLength, long RightLength, IReadOnlyList<(long Offset, long Length)> Ranges, bool RangesTruncated);

/// <summary>
/// Exact same-offset comparison (plan §16.2): every byte of both inputs is read, so "equal" is a whole-content claim,
/// never a sample. Shifted content shows as differences from the shift on; aligned comparison is a separate mode.
/// </summary>
public static class BinaryDiff
{
    public const int MaxRanges = 10_000;
    private const int Chunk = 1 << 20;

    public static BinaryDiffResult Compare(IContentSource left, IContentSource right, CancellationToken ct, Action<long>? progress = null)
    {
        long expectedLeft = left.Length, expectedRight = right.Length;
        var ranges = new List<(long Offset, long Length)>();
        bool truncated = false;
        var ba = new byte[Chunk];
        var bb = new byte[Chunk];
        long offset = 0;
        long runStart = -1;
        void Close(long end)
        {
            if (runStart < 0) return;
            if (ranges.Count < MaxRanges) ranges.Add((runStart, end - runStart));
            else truncated = true;
            runStart = -1;
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int wantA = expectedLeft < 0 ? ba.Length : (int)Math.Min(ba.Length, Math.Max(0, expectedLeft - offset));
            int wantB = expectedRight < 0 ? bb.Length : (int)Math.Min(bb.Length, Math.Max(0, expectedRight - offset));
            int na = ReadFull(left, offset, ba.AsSpan(0, wantA));
            int nb = ReadFull(right, offset, bb.AsSpan(0, wantB));
            if ((expectedLeft >= 0 && na != wantA) || (expectedRight >= 0 && nb != wantB))
                throw new IOException("Comparison content ended before its reported length.");
            int common = Math.Min(na, nb);
            var sa = ba.AsSpan(0, common);
            var sb = bb.AsSpan(0, common);
            for (int i = 0; i < common;)
            {
                if (runStart < 0)
                {
                    // Equal bytes are skipped a vector at a time; the first difference starts a run.
                    i += sa[i..].CommonPrefixLength(sb[i..]);
                    if (i < common) runStart = offset + i;
                }
                else
                {
                    while (i < common && sa[i] != sb[i]) i++;
                    if (i < common) Close(offset + i);
                }
            }
            if (na != nb || na == 0)
            {
                Close(offset + common);
                // Past the shorter end, the rest of the longer input is one difference.
                long leftLength = offset + na + (na == Chunk ? Remaining(left, offset + na, expectedLeft, ct) : 0);
                long rightLength = offset + nb + (nb == Chunk ? Remaining(right, offset + nb, expectedRight, ct) : 0);
                if ((expectedLeft >= 0 && left.Length != expectedLeft) || (expectedRight >= 0 && right.Length != expectedRight))
                    throw new IOException("Comparison content changed length while it was being read.");
                if (leftLength != rightLength)
                {
                    long from = Math.Min(leftLength, rightLength);
                    if (ranges.Count < MaxRanges) ranges.Add((from, Math.Abs(leftLength - rightLength)));
                    else truncated = true;
                }
                return new BinaryDiffResult(ranges.Count == 0 && !truncated, leftLength, rightLength, ranges, truncated);
            }
            offset += common;
            progress?.Invoke(offset);
        }
    }

    /// <summary>
    /// The first differing run at or after <paramref name="from"/> (a run as <see cref="Compare"/> lists them: equal bytes
    /// end it; past the shorter end the rest is one run), or null when none follows. For going on past the listed ranges
    /// of a comparison, whose lengths are given.
    /// </summary>
    public static (long Offset, long Length)? NextDifference(IContentSource left, IContentSource right, long leftLength, long rightLength, long from, CancellationToken ct)
    {
        if ((left.Length >= 0 && left.Length != leftLength) || (right.Length >= 0 && right.Length != rightLength))
            throw new IOException("Comparison content changed length since it was compared.");
        long common = Math.Min(leftLength, rightLength);
        var ba = new byte[Chunk];
        var bb = new byte[Chunk];
        long runStart = -1;
        for (long offset = Math.Max(0, from); offset < common;)
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(Chunk, common - offset);
            int na = ReadFull(left, offset, ba.AsSpan(0, want)), nb = ReadFull(right, offset, bb.AsSpan(0, want));
            if (na != want || nb != want) throw new IOException("Comparison content ended before its reported length.");
            int n = want;
            var sa = ba.AsSpan(0, n);
            var sb = bb.AsSpan(0, n);
            int i = 0;
            if (runStart < 0)
            {
                i = sa.CommonPrefixLength(sb);
                if (i < n) runStart = offset + i;
            }
            if (runStart >= 0)
            {
                while (i < n && sa[i] != sb[i]) i++;
                if (i < n) return (runStart, offset + i - runStart);
            }
            offset += n;
        }
        if (runStart >= 0) return (runStart, common - runStart);
        return leftLength != rightLength && Math.Max(from, common) < Math.Max(leftLength, rightLength)
            ? (common, Math.Abs(leftLength - rightLength))
            : null;
    }

    /// <summary>The last differing run that starts before <paramref name="before"/>, read backwards; null when none does.</summary>
    public static (long Offset, long Length)? PreviousDifference(IContentSource left, IContentSource right, long leftLength, long rightLength, long before, CancellationToken ct)
    {
        if ((left.Length >= 0 && left.Length != leftLength) || (right.Length >= 0 && right.Length != rightLength))
            throw new IOException("Comparison content changed length since it was compared.");
        long common = Math.Min(leftLength, rightLength);
        // Past the shorter end, the rest is one run.
        if (leftLength != rightLength && before > common) return (common, Math.Abs(leftLength - rightLength));
        var ba = new byte[Chunk];
        var bb = new byte[Chunk];
        long runEnd = -1;
        for (long end = Math.Min(before, common); end > 0;)
        {
            ct.ThrowIfCancellationRequested();
            int want = (int)Math.Min(Chunk, end);
            long start = end - want;
            if (ReadFull(left, start, ba.AsSpan(0, want)) < want || ReadFull(right, start, bb.AsSpan(0, want)) < want)
                throw new IOException("Comparison content ended before its reported length.");
            int i = want - 1;
            if (runEnd < 0)
            {
                while (i >= 0 && ba[i] == bb[i]) i--;
                if (i >= 0) runEnd = start + i + 1;
            }
            if (runEnd >= 0)
            {
                while (i >= 0 && ba[i] != bb[i]) i--;
                if (i >= 0) return (start + i + 1, runEnd - (start + i + 1));
            }
            end = start;
        }
        return runEnd >= 0 ? (0, runEnd) : null;
    }

    private static int ReadFull(IContentSource source, long offset, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = source.Read(offset + total, buffer[total..]);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static long Remaining(IContentSource source, long offset, long expectedLength, CancellationToken ct)
    {
        var buffer = new byte[Chunk];
        long total = 0;
        while (expectedLength < 0 || offset + total < expectedLength)
        {
            ct.ThrowIfCancellationRequested();
            int want = expectedLength < 0 ? buffer.Length : (int)Math.Min(buffer.Length, expectedLength - offset - total);
            int n = ReadFull(source, offset + total, buffer.AsSpan(0, want));
            if (n < want && expectedLength >= 0)
                throw new IOException("Comparison content ended before its reported length.");
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
