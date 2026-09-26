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
    int ContentUnknown)
{
    public string Describe(CompareCriteria criteria, TimeSpan tolerance)
    {
        var how = new List<string> { "name" };
        if ((criteria & CompareCriteria.Size) != 0) how.Add("size");
        if ((criteria & CompareCriteria.Time) != 0) how.Add(tolerance > TimeSpan.Zero ? $"time ±{tolerance.TotalSeconds:0.#} s" : "time");
        if ((criteria & CompareCriteria.Content) != 0) how.Add("content");
        var s = $"Comparison ({string.Join(", ", how)}): {Different} differ · {LeftOnly} only left · {RightOnly} only right · {Same} same";
        if (ContentUnknown > 0) s += $" · {ContentUnknown} could not be read (marked)";
        return s;
    }
}

/// <summary>
/// Two-panel compare-and-mark (plan §16.2, OPS-008): non-recursive; folders compare by presence; files by
/// name plus the chosen criteria with timestamp tolerance at the coarser filesystem's known precision.
/// A content check hashes both files; unreadable items count as different so they are never hidden.
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
        var comparer = caseInsensitiveNames ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var rightByName = new Dictionary<string, EntryData>(comparer);
        foreach (var r in right)
        {
            if (r.Kind == EntryKind.Parent) continue;
            rightByName.TryAdd(r.Name, r);
        }
        var leftMarks = new HashSet<string>(StringComparer.Ordinal);
        var rightMarks = new HashSet<string>(StringComparer.Ordinal);
        var matchedRight = new HashSet<string>(comparer);
        int same = 0, diff = 0, leftOnly = 0, unknown = 0;
        foreach (var l in left)
        {
            ct.ThrowIfCancellationRequested();
            if (l.Kind == EntryKind.Parent) continue;
            if (!rightByName.TryGetValue(l.Name, out var r) || r.IsContainer != l.IsContainer)
            {
                leftMarks.Add(l.Name);
                leftOnly++;
                continue;
            }
            matchedRight.Add(r.Name);
            if (l.IsContainer)
            {
                same++;
                continue;
            }
            bool differs = false;
            if ((criteria & CompareCriteria.Size) != 0 && l.Size != r.Size) differs = true;
            if (!differs && (criteria & CompareCriteria.Time) != 0 && Math.Abs(l.Modified - r.Modified) > tolerance.Ticks) differs = true;
            if (!differs && (criteria & CompareCriteria.Content) != 0 && contentEqual is not null)
            {
                if (l.Size != r.Size) differs = true;
                else
                {
                    var eq = contentEqual(l.Name, r.Name);
                    if (eq is null)
                    {
                        unknown++;
                        differs = true;
                    }
                    else differs = !eq.Value;
                }
            }
            if (differs)
            {
                diff++;
                leftMarks.Add(l.Name);
                rightMarks.Add(r.Name);
            }
            else same++;
        }
        int rightOnly = 0;
        foreach (var r in rightByName.Values)
        {
            if (matchedRight.Contains(r.Name)) continue;
            rightOnly++;
            rightMarks.Add(r.Name);
        }
        return new DirectoryCompareResult(leftMarks, rightMarks, same, diff, leftOnly, rightOnly, unknown);
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
