namespace FileCat.Core.State;

/// <summary>Release version comparison for the opt-in update check ("v1.2.0", "0.1.0-preview", "1.0.0+build").</summary>
public static class ReleaseVersion
{
    /// <summary>
    /// Whether a release's tag reads as one: "1.2.3" or "v1.2.3", up to four numbers, with an optional pre-release of SemVer's
    /// characters ("-rc.1"). An update check shows only such a tag, never whatever text an answer carries.
    /// </summary>
    public static bool IsReleaseTag(string tag) =>
        System.Text.RegularExpressions.Regex.IsMatch(tag, @"^[vV]?[0-9]{1,9}(\.[0-9]{1,9}){1,3}(-[0-9A-Za-z][0-9A-Za-z.-]{0,63})?\z");

    /// <summary>True when <paramref name="candidate"/> is a newer release than <paramref name="current"/>.</summary>
    public static bool IsNewer(string current, string candidate)
    {
        if (!TryParse(current, out var cv, out var cpre) || !TryParse(candidate, out var nv, out var npre)) return false;
        int c = nv.CompareTo(cv);
        if (c != 0) return c > 0;
        // Same numbers: a final release is newer than a pre-release. Numeric pre-release identifiers
        // compare as numbers, so preview.10 follows preview.9 rather than preceding it.
        if (cpre is null) return false;
        if (npre is null) return true;
        return ComparePreRelease(npre, cpre) > 0;
    }

    private static int ComparePreRelease(string left, string right)
    {
        var l = left.Split('.');
        var r = right.Split('.');
        for (int i = 0; i < Math.Min(l.Length, r.Length); i++)
        {
            bool ln = l[i].All(ch => ch is >= '0' and <= '9');
            bool rn = r[i].All(ch => ch is >= '0' and <= '9');
            if (ln != rn) return ln ? -1 : 1;
            int c;
            if (ln)
            {
                var a = l[i].TrimStart('0');
                var b = r[i].TrimStart('0');
                c = a.Length.CompareTo(b.Length);
                if (c == 0) c = string.CompareOrdinal(a, b);
            }
            else c = string.CompareOrdinal(l[i], r[i]);
            if (c != 0) return c;
        }
        return l.Length.CompareTo(r.Length);
    }

    public static bool TryParse(string text, out Version version, out string? preRelease)
    {
        version = new Version(0, 0);
        preRelease = null;
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        int plus = t.IndexOf('+');
        if (plus >= 0) t = t[..plus];
        int dash = t.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = t[(dash + 1)..];
            t = t[..dash];
        }
        if (!Version.TryParse(t.Contains('.') ? t : t + ".0", out var parsed)) return false;
        version = new Version(parsed.Major, Math.Max(0, parsed.Minor), Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}
