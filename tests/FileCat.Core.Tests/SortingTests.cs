using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public class SortingTests
{
    public SortingTests() => NaturalCompare.SetCulture(CultureInfo.InvariantCulture);

    [Fact]
    public void Natural_order_compares_numbers_numerically()
    {
        var names = new[] { "file10.txt", "file2.txt", "File1.txt", "file02.txt", "a", "B", "_x", "é" };
        var sorted = names.OrderBy(n => n, Comparer<string>.Create((a, b) => NaturalCompare.Compare(a, b))).ToArray();
        Assert.Equal(["_x", "a", "B", "é", "File1.txt", "file02.txt", "file2.txt", "file10.txt"], sorted);
    }

    [Fact]
    public void Ordinal_tiebreak_makes_order_total()
    {
        Assert.NotEqual(0, NaturalCompare.Compare("a", "A"));
        Assert.Equal(0, NaturalCompare.CompareCore("a", "A", true));
        Assert.Equal(-NaturalCompare.Compare("x1", "x01"), NaturalCompare.Compare("x01", "x1"));
    }

    [Fact]
    public void Stable_sort_matches_reference_and_is_stable()
    {
        var rnd = new Random(42);
        for (int round = 0; round < 20; round++)
        {
            int n = rnd.Next(0, 3000);
            var keys = Enumerable.Range(0, n).Select(_ => rnd.Next(0, 50)).ToArray();
            var idx = Enumerable.Range(0, n).ToArray();
            StableSort.Sort(idx, (a, b) => keys[a].CompareTo(keys[b]));
            var expected = Enumerable.Range(0, n).OrderBy(i => keys[i]).ToArray(); // LINQ OrderBy is stable
            Assert.Equal(expected, idx);
        }
    }

    [Fact]
    public void Merge_preserves_order()
    {
        var a = new[] { 1, 3, 5, 7 };
        var b = new[] { 2, 3, 6 };
        Assert.Equal([1, 2, 3, 3, 5, 6, 7], StableSort.Merge(a, b, (x, y) => x.CompareTo(y)));
    }

    [Fact]
    public void Directories_first_and_parent_on_top()
    {
        var store = new EntryStore();
        store.Append(new EntryData("..", EntryKind.Parent));
        store.Append(new EntryData("b.txt", EntryKind.File, 5));
        store.Append(new EntryData("zdir", EntryKind.Directory));
        store.Append(new EntryData("a.txt", EntryKind.File, 50));
        store.Append(new EntryData("adir", EntryKind.Directory));
        var idx = new[] { 0, 1, 2, 3, 4 };
        StableSort.Sort(idx, EntrySorter.CreateComparison(store, new SortSpec(SortField.Size, Descending: true)));
        Assert.Equal(["..", "adir", "zdir", "a.txt", "b.txt"], idx.Select(i => store[i].Name));
        StableSort.Sort(idx, EntrySorter.CreateComparison(store, new SortSpec(SortField.Name, Descending: true)));
        Assert.Equal(["..", "zdir", "adir", "b.txt", "a.txt"], idx.Select(i => store[i].Name));
    }

    /// <summary>
    /// A drive's Size column shows its free space, so sorting by size orders drives by it, not by the capacity their rows
    /// hold as their size. A drive that shows none (not ready, or answered without its capacity) sorts last in both
    /// directions, as unknown values do, by name among such drives: in whatever order the drives were listed.
    /// </summary>
    [Fact]
    public void Drives_sort_by_the_free_space_they_show()
    {
        const long GB = 1L << 30;
        // Capacity: E < C < F. Free space: F < C < E. D: is not ready; G: answered without its capacity.
        EntryData[] drives =
        [
            Drive("C:", 50 * GB, 500 * GB),
            Drive("D:", -1, -1, ready: false),
            Drive("E:", 80 * GB, 100 * GB),
            Drive("F:", 10 * GB, 2000 * GB),
            Drive("G:", -1, -1),
        ];
        foreach (var listed in Permutations(drives))
        {
            using var store = new EntryStore();
            store.Append(listed);
            Assert.Equal(["E:", "C:", "F:", "D:", "G:"], SortedBySize(store, descending: true));
            Assert.Equal(["F:", "C:", "E:", "D:", "G:"], SortedBySize(store, descending: false));
        }
    }

    /// <summary>A drive row as This PC lists it: its size is the capacity (unknown while not ready); its tag says what it shows.</summary>
    private static EntryData Drive(string name, long free, long total, bool ready = true) =>
        new(name, EntryKind.Drive, ready ? total : -1)
        {
            Tag = new DriveTag(name + "\\", null, "Fixed", "NTFS", free, total, ready),
            Flags = ready ? EntryFlags.None : EntryFlags.Unavailable,
        };

    private static string[] SortedBySize(EntryStore store, bool descending)
    {
        var idx = Enumerable.Range(0, store.Count).ToArray();
        StableSort.Sort(idx, EntrySorter.CreateComparison(store, new SortSpec(SortField.Size, descending)));
        return [.. idx.Select(i => store[i].Name)];
    }

    private static IEnumerable<EntryData[]> Permutations(EntryData[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (int i = 0; i < items.Length; i++)
            foreach (var rest in Permutations([.. items[..i], .. items[(i + 1)..]]))
                yield return [items[i], .. rest];
    }

    [Fact]
    public void Mark_set_counts_and_enumerates()
    {
        var m = new MarkSet();
        Assert.True(m.Set(5, true));
        Assert.False(m.Set(5, true));
        Assert.True(m.Set(700, true));
        Assert.Equal(2, m.Count);
        Assert.Equal([5, 700], m.Enumerate());
        Assert.True(m.Set(5, false));
        Assert.Equal(1, m.Count);
        Assert.False(m.Get(5));
        Assert.True(m.Get(700));
    }
}
