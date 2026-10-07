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
/// A content check reads both files. A pair the criteria cannot decide (content that cannot be read, a size or time a
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
        => CompareAsync(left, right, criteria, tolerance, contentEqual is null ? null : (l, r) => Task.FromResult(contentEqual(l, r)), ct, caseInsensitiveNames).GetAwaiter().GetResult();

    public static async Task<DirectoryCompareResult> CompareAsync(
        IReadOnlyList<EntryData> left, IReadOnlyList<EntryData> right, CompareCriteria criteria, TimeSpan tolerance,
        Func<string, string, Task<bool?>>? contentEqual, CancellationToken ct, bool caseInsensitiveNames = true)
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
            switch (await DecideAsync(le, re, criteria, tolerance, contentEqual).ConfigureAwait(false))
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
        => DecideAsync(l, r, criteria, tolerance, contentEqual is null ? null : (a, b) => Task.FromResult(contentEqual(a, b))).GetAwaiter().GetResult();

    private static async Task<bool?> DecideAsync(EntryData l, EntryData r, CompareCriteria criteria, TimeSpan tolerance, Func<string, string, Task<bool?>>? contentEqual)
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
        if ((criteria & CompareCriteria.Content) != 0)
        {
            if (sizesKnown && l.Size != r.Size) return false;
            if (contentEqual is null) return null;
            if (await contentEqual(l.Name, r.Name).ConfigureAwait(false) is not { } equal) return null;
            if (!equal) return false;
            sizeOpen = false; // the same content is the same size
        }
        return sizeOpen || timeOpen ? null : true;
    }

    /// <summary>Streams two contents; null for unavailable bytes, inconsistent reads or changed revision evidence.</summary>
    public static bool? ContentEqual(IContentSource a, IContentSource b, CancellationToken ct)
        => ContentEqualAsync(() => Task.FromResult(ReadState(a, ct)), () => Task.FromResult(ReadState(b, ct)),
            (offset, buffer, start) => Task.FromResult(a.Read(offset, buffer.AsSpan(start))),
            (offset, buffer, start) => Task.FromResult(b.Read(offset, buffer.AsSpan(start))), ct).GetAwaiter().GetResult();

    internal readonly record struct ContentState(long Length, ContentRevision? Revision, bool Unavailable)
    {
        public bool Consistent => Revision is not { } revision || Length < 0 || revision.Length == Length;
    }

    internal static ContentState ReadState(IContentSource source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        long length = source.Length;
        ct.ThrowIfCancellationRequested();
        var revision = source.GetRevision();
        ct.ThrowIfCancellationRequested();
        bool unavailable = false;
        if (source is IPartialContent partial)
        {
            unavailable = partial.MissingRanges.Count > 0;
            ct.ThrowIfCancellationRequested();
            if (!unavailable) unavailable = partial.Caveat is not null;
            ct.ThrowIfCancellationRequested();
        }
        return new(length, revision, unavailable);
    }

    internal static async Task<bool?> ContentEqualAsync(Func<Task<ContentState>> leftState, Func<Task<ContentState>> rightState,
        Func<long, byte[], int, Task<int>> leftRead, Func<long, byte[], int, Task<int>> rightRead, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var a = await leftState().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var b = await rightState().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (a.Unavailable || b.Unavailable || !a.Consistent || !b.Consistent) return null;

            async Task<bool?> Finish(bool result)
            {
                ct.ThrowIfCancellationRequested();
                var afterA = await leftState().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var afterB = await rightState().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                // Null revisions are supported, but gaining or losing evidence during a read is not stability.
                return afterA == a && afterB == b ? result : null;
            }

            if (a.Length >= 0 && b.Length >= 0 && a.Length != b.Length) return await Finish(false).ConfigureAwait(false);
            var ba = new byte[1 << 20];
            var bb = new byte[1 << 20];
            long offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int na = await ReadFull(leftRead, offset, ba, ct).ConfigureAwait(false);
                int nb = await ReadFull(rightRead, offset, bb, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                // A known length is a promise: an early ending or bytes past it are unavailable content, not a difference.
                static bool BadEnd(long length, long offset, int count, int capacity)
                    => length >= 0 && (offset > length || count > length - offset || count < capacity && offset + count != length);
                if (BadEnd(a.Length, offset, na, ba.Length) || BadEnd(b.Length, offset, nb, bb.Length)) return null;
                if (na != nb) return await Finish(false).ConfigureAwait(false);
                if (na == 0) return await Finish(true).ConfigureAwait(false);
                if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return await Finish(false).ConfigureAwait(false);
                offset += na;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static async Task<int> ReadFull(Func<long, byte[], int, Task<int>> read, long offset, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n = await read(offset + total, buffer, total).ConfigureAwait(false);
            if (n < 0 || n > buffer.Length - total) throw new InvalidDataException("Content returned an invalid byte count.");
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
