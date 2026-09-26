using System.Globalization;

namespace FileCat.Core.Listing;

/// <summary>
/// Presentation-only name ordering (plan §8.1: never used for identity). Digit runs compare
/// numerically, letters case-insensitively; ASCII takes a fast table path and other text falls back to
/// the current culture. Ties break ordinally so the order is total and deterministic.
/// </summary>
public static class NaturalCompare
{
    private const CompareOptions CultureOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;
    private static readonly ushort[] AsciiWeight = BuildWeights();
    private static CompareInfo _culture = CultureInfo.CurrentCulture.CompareInfo;

    /// <summary>Overrides the culture used for non-ASCII text (tests, user preference).</summary>
    public static void SetCulture(CultureInfo culture) => _culture = culture.CompareInfo;

    private static ushort[] BuildWeights()
    {
        var w = new ushort[128];
        for (int c = 0; c < 128; c++)
        {
            if (c is >= 'a' and <= 'z') w[c] = (ushort)(1000 + (c - 'a'));
            else if (c is >= 'A' and <= 'Z') w[c] = (ushort)(1000 + (c - 'A'));
            else if (c is >= '0' and <= '9') w[c] = (ushort)(500 + (c - '0'));
            else w[c] = (ushort)c;
        }
        return w;
    }

    public static int Compare(string? a, string? b, bool natural = true)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int r = CompareCore(a, b, natural);
        return r != 0 ? r : string.CompareOrdinal(a, b);
    }

    /// <summary>Comparison without the ordinal tie-break ("a" equals "A").</summary>
    public static int CompareCore(string a, string b, bool natural)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            char ca = a[i], cb = b[j];
            if (natural && char.IsAsciiDigit(ca) && char.IsAsciiDigit(cb))
            {
                int si = i; while (si < a.Length && a[si] == '0') si++;
                int sj = j; while (sj < b.Length && b[sj] == '0') sj++;
                int ei = si; while (ei < a.Length && char.IsAsciiDigit(a[ei])) ei++;
                int ej = sj; while (ej < b.Length && char.IsAsciiDigit(b[ej])) ej++;
                int la = ei - si, lb = ej - sj;
                if (la != lb) return la < lb ? -1 : 1;
                for (int k = 0; k < la; k++)
                {
                    if (a[si + k] != b[sj + k]) return a[si + k] < b[sj + k] ? -1 : 1;
                }
                i = ei;
                j = ej;
                continue;
            }
            if (ca < 128 && cb < 128)
            {
                if (ca != cb)
                {
                    int wa = AsciiWeight[ca], wb = AsciiWeight[cb];
                    if (wa != wb) return wa < wb ? -1 : 1;
                }
                i++;
                j++;
                continue;
            }
            int ra = i; while (ra < a.Length && !(natural && char.IsAsciiDigit(a[ra]))) ra++;
            int rb = j; while (rb < b.Length && !(natural && char.IsAsciiDigit(b[rb]))) rb++;
            int c = _culture.Compare(a.AsSpan(i, ra - i), b.AsSpan(j, rb - j), CultureOptions);
            if (c != 0) return c < 0 ? -1 : 1;
            i = ra;
            j = rb;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
