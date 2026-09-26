using FileCat.Core.Resources;

namespace FileCat.Core.Listing;

public enum SortField
{
    Name,
    Extension,
    Modified,
    Size,
    Created,
    Attributes,
    /// <summary>Enumeration order.</summary>
    None,
}

/// <summary>
/// Sort order. The flags are phrased so that the zero value (<c>default</c> or <c>new SortSpec()</c>) is the
/// normal Commander order: by name, ascending, directories first, natural numbers.
/// </summary>
public readonly record struct SortSpec(SortField Field = SortField.Name, bool Descending = false, bool MixDirectories = false, bool Ordinal = false)
{
    public bool DirectoriesFirst => !MixDirectories;
    public bool Natural => !Ordinal;
}

/// <summary>Typed entry comparisons with a stable identity tie-break (plan §10).</summary>
public static class EntrySorter
{
    public static Comparison<int> CreateComparison(EntryStore store, SortSpec spec) =>
        (x, y) => Compare(ref store.GetRef(x), ref store.GetRef(y), spec, x, y);

    public static int Compare(ref EntryData a, ref EntryData b, SortSpec spec, int ia, int ib)
    {
        if (a.Kind == EntryKind.Parent) return b.Kind == EntryKind.Parent ? ia.CompareTo(ib) : -1;
        if (b.Kind == EntryKind.Parent) return 1;
        if (spec.DirectoriesFirst)
        {
            bool ca = a.IsContainer, cb = b.IsContainer;
            if (ca != cb) return ca ? -1 : 1;
        }
        int r = spec.Field switch
        {
            SortField.Name => NaturalCompare.CompareCore(a.Name, b.Name, spec.Natural),
            SortField.Extension => CompareExtension(ref a, ref b, spec.Natural),
            SortField.Modified => a.Modified.CompareTo(b.Modified),
            SortField.Created => a.Created.CompareTo(b.Created),
            SortField.Size => CompareSize(ref a, ref b),
            SortField.Attributes => a.Attributes.CompareTo(b.Attributes),
            _ => 0,
        };
        if (spec.Descending) r = -r;
        if (r == 0 && spec.Field != SortField.None) r = NaturalCompare.Compare(a.Name, b.Name, spec.Natural);
        return r != 0 ? r : ia.CompareTo(ib);
    }

    private static int CompareExtension(ref EntryData a, ref EntryData b, bool natural)
    {
        if (a.IsContainer || b.IsContainer) return 0;
        return NaturalCompare.CompareCore(NameParts.GetExtension(a.Name), NameParts.GetExtension(b.Name), natural);
    }

    private static int CompareSize(ref EntryData a, ref EntryData b)
    {
        // Directories without an explicitly computed size keep name order (TC/Salamander behavior).
        if (a.IsContainer && b.IsContainer && (a.Size < 0 || b.Size < 0)) return 0;
        return a.Size.CompareTo(b.Size);
    }
}

/// <summary>
/// Stable merge sort over index arrays. Unlike <see cref="Array.Sort{T}(T[], Comparison{T})"/> it never
/// throws when a concurrently updated key (e.g. a computed directory size) makes comparisons inconsistent.
/// </summary>
public static class StableSort
{
    private const int RunLength = 32;

    public static void Sort(int[] items, Comparison<int> cmp) => Sort(items, 0, items.Length, cmp);

    public static void Sort(int[] items, int start, int length, Comparison<int> cmp)
    {
        if (length < 2) return;
        int end = start + length;
        for (int lo = start; lo < end; lo += RunLength) InsertionSort(items, lo, Math.Min(lo + RunLength, end), cmp);
        if (length <= RunLength) return;
        var src = items;
        var dst = new int[items.Length];
        bool inItems = true;
        for (int width = RunLength; width < length; width *= 2)
        {
            for (int lo = start; lo < end; lo += 2 * width)
            {
                int mid = Math.Min(lo + width, end), hi = Math.Min(lo + 2 * width, end);
                MergeRuns(src, lo, mid, hi, dst, cmp);
            }
            (src, dst) = (dst, src);
            inItems = !inItems;
        }
        if (!inItems) Array.Copy(src, start, items, start, length);
    }

    private static void InsertionSort(int[] a, int lo, int hi, Comparison<int> cmp)
    {
        for (int i = lo + 1; i < hi; i++)
        {
            int v = a[i];
            int j = i - 1;
            while (j >= lo && cmp(a[j], v) > 0)
            {
                a[j + 1] = a[j];
                j--;
            }
            a[j + 1] = v;
        }
    }

    private static void MergeRuns(int[] src, int lo, int mid, int hi, int[] dst, Comparison<int> cmp)
    {
        int i = lo, j = mid, k = lo;
        while (i < mid && j < hi) dst[k++] = cmp(src[j], src[i]) < 0 ? src[j++] : src[i++];
        while (i < mid) dst[k++] = src[i++];
        while (j < hi) dst[k++] = src[j++];
    }

    /// <summary>Merges two individually sorted arrays into a new sorted array.</summary>
    public static int[] Merge(int[] a, int[] b, Comparison<int> cmp)
    {
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        var r = new int[a.Length + b.Length];
        int i = 0, j = 0, k = 0;
        while (i < a.Length && j < b.Length) r[k++] = cmp(b[j], a[i]) < 0 ? b[j++] : a[i++];
        while (i < a.Length) r[k++] = a[i++];
        while (j < b.Length) r[k++] = b[j++];
        return r;
    }
}
