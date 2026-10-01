using FileCat.Core.Resources;

namespace FileCat.Core.Compare;

[Flags]
public enum CompareCriteria
{
    None = 0,
    Size = 1,
    Time = 2,
    Content = 4,
}

public sealed record DirectoryCompareResult(
    IReadOnlySet<string> LeftMarks,
    IReadOnlySet<string> RightMarks,
    int Same,
    int Different,
    int LeftOnly,
    int RightOnly,
    int Unknown)
{
    public string Describe(CompareCriteria criteria, TimeSpan tolerance)
    {
        var how = new List<string> { "name" };
        if ((criteria & CompareCriteria.Size) != 0) how.Add("size");
        if ((criteria & CompareCriteria.Time) != 0) how.Add(tolerance > TimeSpan.Zero ? $"time ±{tolerance.TotalSeconds:0.#} s" : "time");
        if ((criteria & CompareCriteria.Content) != 0) how.Add("content");
        var s = $"Comparison ({string.Join(", ", how)}): {Different} differ · {LeftOnly} only left · {RightOnly} only right · {Same} same";
        if (Unknown > 0) s += $" · {Unknown} could not be compared (marked)";
        return s;
    }
}

/// <summary>
/// Two-panel compare-and-mark (plan §16.2, OPS-008): non-recursive; folders compare by presence; files by
/// name plus the chosen criteria with timestamp tolerance at the coarser filesystem's known precision.
/// A content check hashes both files. A pair the criteria cannot decide (content that cannot be read, a size or time a
/// listing does not give) is marked and counted as not compared, never as the same; names pair as
/// <see cref="NamePairing"/> says.
/// </summary>
public static class DirectoryCompare
{
    public static DirectoryCompareResult Compare(
        IReadOnlyList<EntryData> left,
        IReadOnlyList<EntryData> right,
        CompareCriteria criteria,
        TimeSpan tolerance,
        Func<string, string, bool?>? contentEqual,
        CancellationToken ct,
        bool caseInsensitiveNames = true)
    {
        var leftMarks = new HashSet<string>(StringComparer.Ordinal);
        var rightMarks = new HashSet<string>(StringComparer.Ordinal);
        int same = 0, diff = 0, leftOnly = 0, rightOnly = 0, unknown = 0;
        foreach (var (l, r) in NamePairing.Pair(left, right, caseInsensitiveNames))
        {
            ct.ThrowIfCancellationRequested();
            if (r is not { } re)
            {
                leftMarks.Add(l!.Value.Name);
                leftOnly++;
                continue;
            }
            if (l is not { } le)
            {
                rightMarks.Add(re.Name);
                rightOnly++;
                continue;
            }
            if (le.IsContainer != re.IsContainer)
            {
                // A file on one side and a folder on the other: neither has its match.
                leftMarks.Add(le.Name);
                leftOnly++;
                rightMarks.Add(re.Name);
                rightOnly++;
                continue;
            }
            if (le.IsContainer)
            {
                same++;
                continue;
            }
            switch (Decide(le, re, criteria, tolerance, contentEqual))
            {
                case true:
                    same++;
                    break;
                case false:
                    diff++;
                    leftMarks.Add(le.Name);
                    rightMarks.Add(re.Name);
                    break;
                default:
                    unknown++;
                    leftMarks.Add(le.Name);
                    rightMarks.Add(re.Name);
                    break;
            }
        }
        return new DirectoryCompareResult(leftMarks, rightMarks, same, diff, leftOnly, rightOnly, unknown);
    }

    /// <summary>
    /// Two files by the chosen criteria: the same (true), different (false), or undecided (null), when a criterion the
    /// listings cannot answer (a size or time a listing does not give, content that cannot be read) is left and nothing
    /// else told them apart. Undecided used to count as the same (V13: no false equality).
    /// </summary>
    internal static bool? Decide(EntryData l, EntryData r, CompareCriteria criteria, TimeSpan tolerance, Func<string, string, bool?>? contentEqual)
    {
        bool sizesKnown = l.Size >= 0 && r.Size >= 0;
        bool sizeOpen = false, timeOpen = false;
        if ((criteria & CompareCriteria.Size) != 0)
        {
            if (!sizesKnown) sizeOpen = true;
            else if (l.Size != r.Size) return false;
        }
        if ((criteria & CompareCriteria.Time) != 0)
        {
            int? newer = EntryTimes.Compare(l, r, tolerance);
            if (newer is null) timeOpen = true;
            else if (newer != 0) return false;
        }
        if ((criteria & CompareCriteria.Content) != 0 && contentEqual is not null)
        {
            if (sizesKnown && l.Size != r.Size) return false;
            if (contentEqual(l.Name, r.Name) is not { } equal) return null;
            if (!equal) return false;
            sizeOpen = false; // the same content is the same size
        }
        return sizeOpen || timeOpen ? null : true;
    }

    /// <summary>Streams two contents and compares bytes; null when either cannot be read.</summary>
    public static bool? ContentEqual(IContentSource a, IContentSource b, CancellationToken ct)
    {
        try
        {
            if (a.Length >= 0 && b.Length >= 0 && a.Length != b.Length) return false;
            var ba = new byte[1 << 20];
            var bb = new byte[1 << 20];
            long offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int na = ReadFull(a, offset, ba);
                int nb = ReadFull(b, offset, bb);
                if (na != nb) return false;
                if (na == 0) return true;
                if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
                offset += na;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static int ReadFull(IContentSource s, long offset, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = s.Read(offset + total, buffer.AsSpan(total));
            if (n <= 0) break;
            total += n;
        }
        return total;
    }
}
