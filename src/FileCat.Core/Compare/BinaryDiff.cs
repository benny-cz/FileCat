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
            int na = ReadFull(left, offset, ba);
            int nb = ReadFull(right, offset, bb);
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
                long leftLength = offset + na + (na == Chunk ? Remaining(left, offset + na) : 0);
                long rightLength = offset + nb + (nb == Chunk ? Remaining(right, offset + nb) : 0);
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

    private static long Remaining(IContentSource source, long offset)
    {
        if (source.Length >= 0) return Math.Max(0, source.Length - offset);
        var buffer = new byte[Chunk];
        long total = 0;
        int n;
        while ((n = source.Read(offset + total, buffer)) > 0) total += n;
        return total;
    }

    private static int ReadFull(IContentSource source, long offset, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = source.Read(offset + total, buffer.AsSpan(total));
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
