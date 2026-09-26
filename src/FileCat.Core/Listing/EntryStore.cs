using FileCat.Core.Resources;

namespace FileCat.Core.Listing;

/// <summary>
/// Append-only paged entry storage: one writer (the enumeration thread) and any number of readers.
/// Readers may access indices below <see cref="Count"/> at any time. Pages avoid large-object copies
/// while growing and keep per-entry overhead to the struct plus its name (plan §8.1).
/// </summary>
public sealed class EntryStore
{
    private const int PageShift = 12;
    private const int PageSize = 1 << PageShift;
    private const int PageMask = PageSize - 1;

    private EntryData[][] _pages = new EntryData[4][];
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public EntryData this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return Volatile.Read(ref _pages)[index >> PageShift][index & PageMask];
        }
    }

    /// <summary>Reference to an entry; valid for indices below <see cref="Count"/>.</summary>
    public ref EntryData GetRef(int index) => ref Volatile.Read(ref _pages)[index >> PageShift][index & PageMask];

    public void Append(in EntryData entry) => Append(new ReadOnlySpan<EntryData>(in entry));

    public void Append(ReadOnlySpan<EntryData> entries)
    {
        int n = _count;
        foreach (ref readonly var e in entries)
        {
            EnsurePage(n >> PageShift);
            _pages[n >> PageShift][n & PageMask] = e;
            n++;
        }
        Volatile.Write(ref _count, n);
    }

    private void EnsurePage(int page)
    {
        var pages = _pages;
        if (page >= pages.Length)
        {
            var bigger = new EntryData[Math.Max(pages.Length * 2, page + 1)][];
            Array.Copy(pages, bigger, pages.Length);
            Volatile.Write(ref _pages, bigger);
            pages = bigger;
        }
        pages[page] ??= new EntryData[PageSize];
    }

    /// <summary>Approximate managed memory used by entries and names (for budgets and diagnostics).</summary>
    public long EstimateBytes()
    {
        long total = (long)_pages.Length * 8;
        int count = Count;
        int pages = (count + PageSize - 1) >> PageShift;
        total += (long)pages * PageSize * 56;
        for (int i = 0; i < count; i++) total += 22 + 2L * (GetRef(i).Name?.Length ?? 0);
        return total;
    }
}
