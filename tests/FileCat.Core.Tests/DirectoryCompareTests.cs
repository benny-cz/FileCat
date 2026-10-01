using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Comparing two folders by name (V13: letter-case collisions, no false equality): every item on each side is accounted
/// for once, names pair exactly first and by letter case only where that is unambiguous, and a criterion the listings
/// cannot answer leaves a pair undecided and marked, never "the same".
/// </summary>
public sealed class DirectoryCompareTests
{
    private static readonly long Then = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;

    private static EntryData File(string name, long size = 10, long modified = -1) => new(name, EntryKind.File, size, modified < 0 ? Then : modified);

    private static DirectoryCompareResult Compare(EntryData[] left, EntryData[] right, CompareCriteria criteria, bool caseInsensitive = true,
        Func<string, string, bool?>? content = null) =>
        DirectoryCompare.Compare(left, right, criteria, TimeSpan.FromSeconds(2), content, TestContext.Current.CancellationToken, caseInsensitive);

    [Fact]
    public void Names_pair_exactly_first_and_by_letter_case_only_where_that_is_unambiguous()
    {
        // The right side holds both "A.txt" and "a.txt" (a case-sensitive folder): the second used to be dropped.
        var r = Compare([File("a.txt")], [File("A.txt", size: 99), File("a.txt")], CompareCriteria.Size);
        Assert.Equal((1, 0, 0, 1), (r.Same, r.Different, r.LeftOnly, r.RightOnly));
        Assert.Equal(["A.txt"], r.RightMarks);
        // Two on the left that differ only in case, one on the right in a third case: no guess.
        r = Compare([File("A.txt"), File("a.txt")], [File("a.TXT")], CompareCriteria.Size);
        Assert.Equal((0, 2, 1), (r.Same, r.LeftOnly, r.RightOnly));
        // A case variant alone on each side pairs where names ignore case, and not where they do not.
        r = Compare([File("Readme.md")], [File("README.md")], CompareCriteria.Size);
        Assert.Equal((1, 0, 0), (r.Same, r.LeftOnly, r.RightOnly));
        r = Compare([File("Readme.md")], [File("README.md")], CompareCriteria.Size, caseInsensitive: false);
        Assert.Equal((0, 1, 1), (r.Same, r.LeftOnly, r.RightOnly));
    }

    [Fact]
    public void What_the_listings_cannot_answer_is_marked_never_the_same()
    {
        // A listing without sizes (the Registry, some servers) or without times (some archives, FTP): undecided.
        var r = Compare([File("s", size: -1)], [File("s", size: -1)], CompareCriteria.Size);
        Assert.Equal((0, 0, 1), (r.Same, r.Different, r.Unknown));
        Assert.Equal(["s"], r.LeftMarks);
        Assert.Equal(["s"], r.RightMarks);
        Assert.Contains("1 could not be compared (marked)", r.Describe(CompareCriteria.Size, TimeSpan.Zero));
        r = Compare([File("t", modified: 0)], [File("t")], CompareCriteria.Time);
        Assert.Equal((0, 1), (r.Same, r.Unknown));
        // What can be told apart still is: a known size that differs, beside an unknown time.
        r = Compare([File("u", size: 1, modified: 0)], [File("u", size: 2)], CompareCriteria.Size | CompareCriteria.Time);
        Assert.Equal((1, 0), (r.Different, r.Unknown));
        // The same content answers an unknown size, not an unknown time.
        r = Compare([File("c", size: -1)], [File("c", size: -1)], CompareCriteria.Size | CompareCriteria.Content, content: (_, _) => true);
        Assert.Equal((1, 0), (r.Same, r.Unknown));
        r = Compare([File("c", size: -1, modified: 0)], [File("c", size: -1)], CompareCriteria.Size | CompareCriteria.Time | CompareCriteria.Content, content: (_, _) => true);
        Assert.Equal((0, 1), (r.Same, r.Unknown));
        // Content that cannot be read is undecided too.
        r = Compare([File("x")], [File("x")], CompareCriteria.Content, content: (_, _) => null);
        Assert.Equal((0, 1), (r.Same, r.Unknown));
    }

    /// <summary>The rules again, written from their documentation: pairs, then what each pair is.</summary>
    private static (int Same, int Different, int Unknown, int LeftOnly, int RightOnly, HashSet<string> LeftMarks, HashSet<string> RightMarks) Reference(
        List<EntryData> left, List<EntryData> right, CompareCriteria criteria, bool caseInsensitive, Func<string, string, bool?> content)
    {
        var pairedLeft = new HashSet<int>();
        var pairedRight = new HashSet<int>();
        var pairs = new List<(int L, int R)>();
        for (int i = 0; i < left.Count; i++)
        {
            int j = Enumerable.Range(0, right.Count).FirstOrDefault(k => !pairedRight.Contains(k) && right[k].Name == left[i].Name, -1);
            if (j < 0) continue;
            pairs.Add((i, j));
            pairedLeft.Add(i);
            pairedRight.Add(j);
        }
        if (caseInsensitive)
        {
            var ls = Enumerable.Range(0, left.Count).Where(i => !pairedLeft.Contains(i)).ToList();
            var rs = Enumerable.Range(0, right.Count).Where(j => !pairedRight.Contains(j)).ToList();
            foreach (int i in ls)
            {
                var sameLeft = ls.Where(x => string.Equals(left[x].Name, left[i].Name, StringComparison.OrdinalIgnoreCase)).ToList();
                var sameRight = rs.Where(x => string.Equals(right[x].Name, left[i].Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (sameLeft.Count != 1 || sameRight.Count != 1) continue;
                pairs.Add((i, sameRight[0]));
                pairedLeft.Add(i);
                pairedRight.Add(sameRight[0]);
            }
        }
        int same = 0, different = 0, unknown = 0, leftOnly = left.Count - pairedLeft.Count, rightOnly = right.Count - pairedRight.Count;
        var leftMarks = Enumerable.Range(0, left.Count).Where(i => !pairedLeft.Contains(i)).Select(i => left[i].Name).ToHashSet(StringComparer.Ordinal);
        var rightMarks = Enumerable.Range(0, right.Count).Where(j => !pairedRight.Contains(j)).Select(j => right[j].Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (i, j) in pairs)
        {
            var (l, r) = (left[i], right[j]);
            string? verdict;
            if (l.IsContainer != r.IsContainer) verdict = "kinds";
            else if (l.IsContainer) verdict = "same";
            else
            {
                bool sizeKnown = l.Size >= 0 && r.Size >= 0, timeKnown = l.Modified > 0 && r.Modified > 0;
                bool sizeSel = (criteria & CompareCriteria.Size) != 0, timeSel = (criteria & CompareCriteria.Time) != 0, contentSel = (criteria & CompareCriteria.Content) != 0;
                if (sizeSel && sizeKnown && l.Size != r.Size || timeSel && timeKnown && Math.Abs(l.Modified - r.Modified) > TimeSpan.FromSeconds(2).Ticks
                    || contentSel && sizeKnown && l.Size != r.Size) verdict = "different";
                else if (contentSel && content(l.Name, r.Name) is not { } equal) verdict = "unknown";
                else if (contentSel && !content(l.Name, r.Name)!.Value) verdict = "different";
                else if (sizeSel && !sizeKnown && !contentSel || timeSel && !timeKnown) verdict = "unknown";
                else verdict = "same";
            }
            switch (verdict)
            {
                case "kinds":
                    leftOnly++;
                    rightOnly++;
                    leftMarks.Add(l.Name);
                    rightMarks.Add(r.Name);
                    break;
                case "same":
                    same++;
                    break;
                default:
                    if (verdict == "different") different++;
                    else unknown++;
                    leftMarks.Add(l.Name);
                    rightMarks.Add(r.Name);
                    break;
            }
        }
        return (same, different, unknown, leftOnly, rightOnly, leftMarks, rightMarks);
    }

    [Fact]
    public void Every_item_is_accounted_for_once_and_the_counts_match_a_reference()
    {
        var rng = new Random(1314);
        string[] names = ["a.txt", "A.txt", "a.TXT", "b", "B", "readme.md", "README.md", "x y", "Ü.bin", "ü.bin", "dir", "Dir"];
        for (int round = 0; round < 3000; round++)
        {
            List<EntryData> Side() => [.. names.Where(_ => rng.Next(3) == 0).Select(n => rng.Next(6) == 0
                ? new EntryData(n, EntryKind.Directory)
                : new EntryData(n, EntryKind.File, rng.Next(5) == 0 ? -1 : rng.Next(3), rng.Next(5) == 0 ? 0 : Then + rng.Next(3) * TimeSpan.TicksPerSecond * 3))];
            var left = Side();
            var right = Side();
            var criteria = (CompareCriteria)rng.Next(0, 8);
            bool caseInsensitive = rng.Next(2) == 0;
            var verdicts = new Dictionary<(string, string), bool?>();
            bool? Content(string l, string r) => verdicts.TryGetValue((l, r), out var v) ? v : verdicts[(l, r)] = rng.Next(4) switch { 0 => null, 1 => false, _ => true };
            var expected = Reference(left, right, criteria, caseInsensitive, Content);
            var actual = Compare([.. left], [.. right], criteria, caseInsensitive, Content);
            string what = $"round {round}: [{string.Join(", ", left.Select(Show))}] against [{string.Join(", ", right.Select(Show))}], {criteria}, case-insensitive {caseInsensitive}";
            Assert.True((expected.Same, expected.Different, expected.Unknown, expected.LeftOnly, expected.RightOnly) == (actual.Same, actual.Different, actual.Unknown, actual.LeftOnly, actual.RightOnly),
                $"{what}: expected {(expected.Same, expected.Different, expected.Unknown, expected.LeftOnly, expected.RightOnly)}, got {(actual.Same, actual.Different, actual.Unknown, actual.LeftOnly, actual.RightOnly)}");
            Assert.True(expected.LeftMarks.SetEquals(actual.LeftMarks) && expected.RightMarks.SetEquals(actual.RightMarks), $"{what}: marks differ");
            // Nothing dropped: each item on each side is counted once (a file against a folder as one-sided on both).
            Assert.True(left.Count == actual.Same + actual.Different + actual.Unknown + actual.LeftOnly, $"{what}: left items counted otherwise");
            Assert.True(right.Count == actual.Same + actual.Different + actual.Unknown + actual.RightOnly, $"{what}: right items counted otherwise");
        }

        static string Show(EntryData e) => e.IsContainer ? e.Name + "/" : $"{e.Name}({e.Size},{(e.Modified > 0 ? "t" : "?")})";
    }

    /// <summary>Two folders as a listing gives them, sizes and times included or not.</summary>
    private sealed class ListedProvider(Dictionary<string, EntryData[]> folders) : ResourceProvider
    {
        public override string Scheme => "listed";
        public override string GetDisplayPath(Location location) => "listed:" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;

        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch(folders[location.Path]);
            return Task.CompletedTask;
        }

        public override IContentSource? OpenContent(ItemRef item) => null;
    }

    [Fact]
    public void A_tree_comparison_keeps_case_variants_apart_and_leaves_unknown_times_undecided()
    {
        var providers = new ProviderRegistry();
        providers.Register(new ListedProvider(new()
        {
            ["L"] = [File("a.txt"), File("t.log", modified: 0), File("s.bin", size: -1)],
            ["R"] = [File("A.txt"), File("a.txt"), File("t.log"), File("s.bin", size: -1)],
        }));
        var result = TreeCompare.Compare(providers, new Location("listed", "L"), new Location("listed", "R"), CompareCriteria.Size | CompareCriteria.Time,
            TimeSpan.FromSeconds(2), caseInsensitiveNames: true, TestContext.Current.CancellationToken);
        var byName = result.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
        Assert.Equal(TreeDiffKind.Same, byName["a.txt"].Kind);
        Assert.Equal(TreeDiffKind.RightOnly, byName["A.txt"].Kind); // used to be dropped
        Assert.Equal(TreeDiffKind.Unknown, byName["t.log"].Kind);
        Assert.Equal("The time is not known on both sides", byName["t.log"].Detail);
        Assert.Equal(TreeDiffKind.Unknown, byName["s.bin"].Kind);
        Assert.Equal("The size is not known on both sides", byName["s.bin"].Detail);
    }
}
