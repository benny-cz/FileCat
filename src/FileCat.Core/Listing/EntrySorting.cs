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
    /// <summary>A metadata field (<see cref="SortSpec.MetadataId"/>); unknown values sort last.</summary>
    Metadata,
}

/// <summary>
/// Sort order. The flags are phrased so that the zero value (<c>default</c> or <c>new SortSpec()</c>) is the
/// normal Commander order: by name, ascending, directories first, natural numbers.
/// </summary>
public readonly record struct SortSpec(SortField Field = SortField.Name, bool Descending = false, bool MixDirectories = false, bool Ordinal = false)
{
    public bool DirectoriesFirst => !MixDirectories;
    public bool Natural => !Ordinal;
    /// <summary>Metadata field id when <see cref="Field"/> is <see cref="SortField.Metadata"/>.</summary>
    public string? MetadataId { get; init; }
}

/// <summary>Supplies comparable metadata keys for store indices (thread-safe; null = unknown).</summary>
public delegate IComparable? MetadataKeyProvider(EntryStore store, int storeIndex, string fieldId);

/// <summary>Typed entry comparisons with a stable identity tie-break (plan §10).</summary>
public static class EntrySorter
{
    public static Comparison<int> CreateComparison(EntryStore store, SortSpec spec, MetadataKeyProvider? metadata = null)
    {
        if (spec.Field == SortField.Metadata && metadata is not null && spec.MetadataId is { } id)
            return (x, y) => CompareMetadata(store, spec, metadata, id, x, y);
        return (x, y) => Compare(store[x], store[y], spec, x, y);
    }

    /// <summary>
    /// A comparison of store indices below <paramref name="count"/>. A spilled store is read in place through
    /// mappings of its spill files, so a sort costs no system call or name string per comparison (a million-entry
    /// spilled sort drops from about a minute to seconds). Dispose the result when the sort is done.
    /// </summary>
    public static EntryComparer CreateComparer(EntryStore store, SortSpec spec, MetadataKeyProvider? metadata, int count) =>
        new(store, spec, metadata, count);

    private static int CompareMetadata(EntryStore store, SortSpec spec, MetadataKeyProvider provider, string id, int x, int y) =>
        CompareMetadata(new EntryView(store[x]), new EntryView(store[y]), store, spec, provider, id, x, y);

    internal static int CompareMetadata(scoped in EntryView a, scoped in EntryView b, EntryStore store, SortSpec spec,
        MetadataKeyProvider provider, string id, int x, int y)
    {
        if (a.Kind == EntryKind.Parent) return b.Kind == EntryKind.Parent ? 0 : -1;
        if (b.Kind == EntryKind.Parent) return 1;
        if (spec.DirectoriesFirst && a.IsContainer != b.IsContainer) return a.IsContainer ? -1 : 1;
        var ka = provider(store, x, id);
        var kb = provider(store, y, id);
        int r;
        // Unknown values are not definite: they sort last in both directions (plan §10).
        if (ka is null || kb is null) r = ka is null && kb is null ? 0 : ka is null ? 1 : -1;
        else
        {
            try { r = ka.CompareTo(kb); }
            catch (ArgumentException) { r = 0; }
            if (spec.Descending) r = -r;
        }
        if (r == 0) r = NaturalCompare.Compare(a.Name, b.Name, spec.Natural);
        return r != 0 ? r : x.CompareTo(y);
    }

    public static int Compare(in EntryData a, in EntryData b, SortSpec spec, int ia, int ib) =>
        Compare(new EntryView(a), new EntryView(b), spec, ia, ib);

    internal static int Compare(scoped in EntryView a, scoped in EntryView b, SortSpec spec, int ia, int ib)
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
            SortField.Extension => CompareExtension(a, b, spec.Natural),
            SortField.Modified => a.Modified.CompareTo(b.Modified),
            SortField.Created => a.Created.CompareTo(b.Created),
            SortField.Size => CompareSize(a, b),
            SortField.Attributes => a.Attributes.CompareTo(b.Attributes),
            _ => 0,
        };
        if (spec.Descending) r = -r;
        if (r == 0 && spec.Field != SortField.None) r = NaturalCompare.Compare(a.Name, b.Name, spec.Natural);
        return r != 0 ? r : ia.CompareTo(ib);
    }

    private static int CompareExtension(scoped in EntryView a, scoped in EntryView b, bool natural)
    {
        if (a.IsContainer || b.IsContainer) return 0;
        return NaturalCompare.CompareCore(NameParts.GetExtension(a.Name), NameParts.GetExtension(b.Name), natural);
    }

    private static int CompareSize(scoped in EntryView a, scoped in EntryView b)
    {
        // Directories without an explicitly computed size keep name order (TC/Salamander behavior).
        if (a.IsContainer && b.IsContainer && (a.Size < 0 || b.Size < 0)) return 0;
        return a.Size.CompareTo(b.Size);
    }
}

/// <summary>
/// A sort in progress over store indices below a count. Once the store has spilled (also when it spills while
/// the sort runs) entries are read in place through mappings of the spill files; dispose it when the sort is done.
/// </summary>
public sealed class EntryComparer : IDisposable
{
    private readonly EntryStore _store;
    private readonly SortSpec _spec;
    private readonly MetadataKeyProvider? _metadata;
    private readonly string? _metadataId;
    private readonly int _count;
    private EntryStore.SpillReader? _reader;

    internal EntryComparer(EntryStore store, SortSpec spec, MetadataKeyProvider? metadata, int count)
    {
        _store = store;
        _spec = spec;
        _count = count;
        if (spec.Field == SortField.Metadata && metadata is not null && spec.MetadataId is { } id)
        {
            _metadata = metadata;
            _metadataId = id;
        }
        Comparison = Compare;
    }

    public Comparison<int> Comparison { get; }

    private int Compare(int x, int y)
    {
        var reader = _reader;
        if (reader is null && _store.HasSpilled) reader = _reader = _store.OpenSpillReader(_count);
        if (_metadata is not null)
        {
            return reader is not null
                ? EntrySorter.CompareMetadata(reader.Get(x), reader.Get(y), _store, _spec, _metadata, _metadataId!, x, y)
                : EntrySorter.CompareMetadata(new EntryView(_store[x]), new EntryView(_store[y]), _store, _spec, _metadata, _metadataId!, x, y);
        }
        return reader is not null
            ? EntrySorter.Compare(reader.Get(x), reader.Get(y), _spec, x, y)
            : EntrySorter.Compare(new EntryView(_store[x]), new EntryView(_store[y]), _spec, x, y);
    }

    public void Dispose() => Interlocked.Exchange(ref _reader, null)?.Dispose();
}

/// <summary>The fields sorting and filtering read, with the name as a span (spilled names stay in place).</summary>
internal readonly ref struct EntryView
{
    public EntryView(scoped in EntryData e)
    {
        Name = e.Name;
        Kind = e.Kind;
        Flags = e.Flags;
        Size = e.Size;
        Modified = e.Modified;
        Created = e.Created;
        Attributes = e.Attributes;
    }

    public EntryView(ReadOnlySpan<char> name, EntryKind kind, EntryFlags flags, long size, long modified, long created, uint attributes)
    {
        Name = name;
        Kind = kind;
        Flags = flags;
        Size = size;
        Modified = modified;
        Created = created;
        Attributes = attributes;
    }

    public ReadOnlySpan<char> Name { get; }
    public EntryKind Kind { get; }
    public EntryFlags Flags { get; }
    public long Size { get; }
    public long Modified { get; }
    public long Created { get; }
    public uint Attributes { get; }

    public bool IsContainer => Kind is EntryKind.Directory or EntryKind.Parent or EntryKind.Drive
        or EntryKind.Server or EntryKind.Share or EntryKind.RegistryKey;

    public bool Has(EntryFlags flag) => (Flags & flag) != 0;
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
