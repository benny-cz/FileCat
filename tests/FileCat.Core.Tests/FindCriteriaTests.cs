using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

/// <summary>Find's criteria as Salamander has them (plan §11, SEARCH-002).</summary>
public sealed class FindCriteriaTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Write(string relative, string content)
    {
        string path = Path.Combine(_dir.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private (List<string> Found, SearchSession Session) Run(SearchQuery query)
    {
        var set = new ResultSet("t", "t", "t");
        var session = new SearchSession(query, set);
        session.Run(TestContext.Current.CancellationToken);
        Assert.True(set.IsComplete);
        return ([.. set.Snapshot().Select(i => Path.GetRelativePath(_dir.Path, i.Item.FileSystemPath!).Replace('\\', '/')).Order()], session);
    }

    private List<string> Find(SearchQuery query) => Run(query).Found;

    [Fact]
    public void A_mask_without_wildcards_or_a_dot_finds_names_containing_it()
    {
        Assert.True(Mask.TryParse("report", plainMeansContains: true, out var contains, out _));
        Assert.True(contains.IsMatch("Q3 report.pdf"));
        Assert.True(contains.IsMatch("REPORTS"));
        Assert.True(Mask.TryParse("report.pdf", plainMeansContains: true, out var dotted, out _));
        Assert.False(dotted.IsMatch("Q3 report.pdf")); // a dot keeps the mask exact
        Assert.True(Mask.TryParse("\"report\"", plainMeansContains: true, out var quoted, out _));
        Assert.False(quoted.IsMatch("Q3 report.pdf")); // so do quotes
        Assert.True(Mask.TryParse("report;*.txt|draft", plainMeansContains: true, out var mixed, out _));
        Assert.True(mixed.IsMatch("a.txt"));
        Assert.False(mixed.IsMatch("report draft 2")); // exclusions follow the same rule
        Assert.True(Mask.TryParse("report", out var plain, out _));
        Assert.False(plain.IsMatch("Q3 report.pdf")); // elsewhere a mask stays exact
    }

    [Fact]
    public void Hex_patterns_take_pairs_with_or_without_spaces_and_quoted_text()
    {
        Assert.True(HexPattern.TryParse("4D 5A \"MZ\"", out var bytes, out _));
        Assert.Equal(new byte[] { 0x4D, 0x5A, 0x4D, 0x5A }, bytes);
        Assert.True(HexPattern.TryParse("4d5a90", out bytes, out _));
        Assert.Equal(new byte[] { 0x4D, 0x5A, 0x90 }, bytes);
        Assert.True(HexPattern.TryParse("\"a\"\"b\"", out bytes, out _));
        Assert.Equal("a\"b"u8.ToArray(), bytes);
        Assert.False(HexPattern.TryParse("4D5", out _, out var odd));
        Assert.Contains("odd number", odd);
        Assert.False(HexPattern.TryParse("4D \"open", out _, out var open));
        Assert.Contains("not closed", open);
        Assert.False(HexPattern.TryParse("4G", out _, out var bad));
        Assert.Contains("not a hex digit", bad);
        Assert.False(HexPattern.TryParse("  ", out _, out _));
        Assert.Equal("4D 5A 90", HexPattern.Format(new byte[] { 0x4D, 0x5A, 0x90 }));
    }

    [Fact]
    public void Whole_words_skip_the_text_inside_longer_words_even_across_reads()
    {
        Write("inside.txt", "foobar and barfoo");
        Write("word.txt", "a foo here");
        Write("symbol.txt", "x#include <y>");
        // "foo" ends one read and letters begin the next: not a whole word.
        Write("split.txt", new string(' ', 1024 * 1024 - 3) + "foo" + "bar");
        // "foo" at the very end of the file is a whole word.
        Write("end.txt", new string(' ', 1024 * 1024 - 3) + "foo");
        var query = new SearchQuery { Roots = [_dir.Path], Text = "foo", WholeWords = true, IncludeDirectories = false };
        Assert.Equal(["end.txt", "word.txt"], Find(query));
        Assert.Equal(["end.txt", "inside.txt", "split.txt", "word.txt"], Find(new SearchQuery { Roots = [_dir.Path], Text = "foo", IncludeDirectories = false }));
        // A literal that starts with a symbol may follow a letter.
        Assert.Equal(["symbol.txt"], Find(new SearchQuery { Roots = [_dir.Path], Text = "#include", WholeWords = true, IncludeDirectories = false }));
        // Regular expressions match whole words too.
        Assert.Equal(["end.txt", "word.txt"], Find(new SearchQuery { Roots = [_dir.Path], Text = "f.o", Regex = true, WholeWords = true, IncludeDirectories = false }));
    }

    [Fact]
    public void Hex_search_finds_bytes_split_across_two_reads()
    {
        var data = new byte[1024 * 1024 + 10];
        data[1024 * 1024 - 2] = 0xCA;
        data[1024 * 1024 - 1] = 0xFE;
        data[1024 * 1024] = 0xBA;
        data[1024 * 1024 + 1] = 0xBE;
        File.WriteAllBytes(Path.Combine(_dir.Path, "split.bin"), data);
        File.WriteAllBytes(Path.Combine(_dir.Path, "other.bin"), [0xCA, 0xFE, 0x00, 0xBE]);
        Assert.True(HexPattern.TryParse("CA FE BA BE", out var bytes, out _));
        Assert.Equal(["split.bin"], Find(new SearchQuery { Roots = [_dir.Path], Bytes = bytes }));
        Assert.True(HexPattern.TryParse("\"MZ\"", out var mz, out _));
        File.WriteAllText(Path.Combine(_dir.Path, "exe.bin"), "MZ header");
        Assert.Equal(["exe.bin"], Find(new SearchQuery { Roots = [_dir.Path], Bytes = mz }));
    }

    [Fact]
    public void Attributes_find_files_only_folders_only_and_read_only_items()
    {
        Write("docs/a.txt", "a");
        string locked = Write("docs/locked.txt", "b");
        File.SetAttributes(locked, File.GetAttributes(locked) | FileAttributes.ReadOnly);
        try
        {
            var all = new SearchQuery { Roots = [_dir.Path] };
            Assert.Equal(["docs", "docs/a.txt", "docs/locked.txt"], Find(all));
            Assert.Equal(["docs/a.txt", "docs/locked.txt"], Find(new SearchQuery { Roots = [_dir.Path], AttributesClear = FileAttributes.Directory }));
            Assert.Equal(["docs"], Find(new SearchQuery { Roots = [_dir.Path], AttributesSet = FileAttributes.Directory }));
            Assert.Equal(["docs/locked.txt"], Find(new SearchQuery { Roots = [_dir.Path], AttributesSet = FileAttributes.ReadOnly, AttributesClear = FileAttributes.Directory }));
            Assert.False(SearchSession.TryValidate(new SearchQuery { Roots = [_dir.Path], AttributesSet = FileAttributes.Hidden, AttributesClear = FileAttributes.Hidden }, out var error));
            Assert.Contains("both required and excluded", error);
        }
        finally
        {
            File.SetAttributes(locked, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Times_filter_by_modification_and_creation()
    {
        string old = Write("old.txt", "o");
        Write("new.txt", "n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        var within = new AdvancedSearchCriteria { Modified = new TimeCriterion { Mode = TimeFilterMode.Within, Amount = 7, Unit = TimeUnit.Days } };
        Assert.Equal(["new.txt"], Build(new SearchCriteria { LookIn = _dir.Path, Advanced = within }));
        var before = new AdvancedSearchCriteria { Modified = new TimeCriterion { Mode = TimeFilterMode.Between, To = DateTime.Now.AddDays(-7) } };
        Assert.Equal(["old.txt"], Build(new SearchCriteria { LookIn = _dir.Path, Advanced = before }));
        if (OperatingSystem.IsWindows())
        {
            File.SetCreationTimeUtc(old, DateTime.UtcNow.AddYears(-2));
            var created = new AdvancedSearchCriteria { Created = new TimeCriterion { Mode = TimeFilterMode.Within, Amount = 1, Unit = TimeUnit.Years } };
            Assert.Equal(["new.txt"], Build(new SearchCriteria { LookIn = _dir.Path, Advanced = created }));
        }
    }

    private List<string> Build(SearchCriteria criteria)
    {
        Assert.True(criteria.TryBuildQuery(DateTime.UtcNow, [], null, out var query, out var error), error);
        return Find(query!);
    }

    [Fact]
    public void Sizes_take_units_and_impossible_criteria_say_why()
    {
        File.WriteAllBytes(Path.Combine(_dir.Path, "small.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_dir.Path, "big.bin"), new byte[3 * 1024]);
        var atLeast = new AdvancedSearchCriteria { SizeAtLeast = 2, SizeAtLeastUnit = SizeUnit.KB };
        Assert.Equal(["big.bin"], Build(new SearchCriteria { LookIn = _dir.Path, Advanced = atLeast }));
        var atMost = new AdvancedSearchCriteria { SizeAtMost = 100, SizeAtMostUnit = SizeUnit.Bytes };
        Assert.Equal(["small.bin"], Build(new SearchCriteria { LookIn = _dir.Path, Advanced = atMost }));

        string? Why(SearchCriteria c) => c.TryBuildQuery(DateTime.UtcNow, [], null, out _, out var error) ? null : error;
        Assert.Contains("nothing could match", Why(new SearchCriteria { LookIn = _dir.Path, Advanced = new AdvancedSearchCriteria { SizeAtLeast = 2, SizeAtMost = 1 } }));
        Assert.Contains("is not a folder", Why(new SearchCriteria { LookIn = Path.Combine(_dir.Path, "missing") }));
        Assert.Contains("Enter a folder", Why(new SearchCriteria { LookIn = " ; " }));
        Assert.StartsWith("Containing (hex)", Why(new SearchCriteria { LookIn = _dir.Path, Text = "4G", Hex = true }));
        Assert.StartsWith("Names:", Why(new SearchCriteria { LookIn = _dir.Path, Names = "\"open" }));
        Assert.Equal(2, new SearchCriteria { LookIn = $"{_dir.Path}; \"{_dir.Path}\"" }.Roots().Count);
    }

    [Fact]
    public void The_summary_names_every_active_criterion()
    {
        var advanced = new AdvancedSearchCriteria
        {
            AttributesClear = FileAttributes.Directory | FileAttributes.Hidden,
            AttributesSet = FileAttributes.Archive,
            SizeAtLeast = 10,
            SizeAtLeastUnit = SizeUnit.KB,
            Modified = new TimeCriterion { Mode = TimeFilterMode.Within, Amount = 1, Unit = TimeUnit.Weeks },
        };
        Assert.Equal("files only · archive · not hidden · at least 10 KB · modified in the last 1 week", advanced.Summary());
        Assert.True(new AdvancedSearchCriteria().IsEmpty);
        Assert.Equal(string.Empty, new AdvancedSearchCriteria().Summary());
        // A saved search keeps "the last 7 days", not the dates of the day it was saved.
        var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(now.AddMonths(-3), new TimeCriterion { Mode = TimeFilterMode.Within, Amount = 3, Unit = TimeUnit.Months }.Resolve(now).AfterUtc);
    }

    [Fact]
    public void Ignored_folders_skip_names_anywhere_anchored_paths_and_full_paths_and_the_log_says_so()
    {
        Write("node_modules/x.js", "x");
        Write("app/node_modules/y.js", "y");
        Write("build/out.txt", "o");
        Write("app/build/keep.txt", "k");
        Write("docs/private/secret.txt", "s");
        Write("docs/readme.txt", "r");
        string sep = Path.DirectorySeparatorChar.ToString();
        var query = new SearchQuery
        {
            Roots = [_dir.Path],
            AttributesClear = FileAttributes.Directory,
            IgnoredFolders = ["node_modules", sep + "build", Path.Combine(_dir.Path, "docs", "private") + sep],
        };
        var (found, session) = Run(query);
        Assert.Equal(["app/build/keep.txt", "docs/readme.txt"], found);
        var ignored = session.Log.Where(e => e.Kind == SearchLogKind.Ignored).Select(e => Path.GetRelativePath(_dir.Path, e.Path).Replace('\\', '/')).Order().ToList();
        Assert.Equal(["app/node_modules", "build", "docs/private", "node_modules"], ignored);
        Assert.StartsWith("Not searched (on the ignore list)", session.Log.First(e => e.Kind == SearchLogKind.Ignored).Describe());
    }

    [Fact]
    public void Duplicates_group_by_name_size_and_content()
    {
        Write("a/report.txt", "same text");
        Write("b/REPORT.txt", "same text");
        Write("c/report.txt", "other text!");
        Write("d/copy.txt", "same text");
        Write("e/tiny.txt", "xx");
        var items = Directory.EnumerateFiles(_dir.Path, "*", SearchOption.AllDirectories).Select(p => ItemRef.ForFileSystemPath(p, EntryKind.File)).ToList();
        string Names(IReadOnlyList<ItemRef> g) => string.Join(",", g.Select(i => Path.GetRelativePath(_dir.Path, i.FileSystemPath!).Replace('\\', '/')));
        var ct = TestContext.Current.CancellationToken;

        var byContent = DuplicateFinder.Find(items, DuplicateCriteria.Content, ct);
        Assert.Equal(["a/report.txt,b/REPORT.txt,d/copy.txt"], byContent.Groups.Select(Names));

        var byNameAndContent = DuplicateFinder.Find(items, DuplicateCriteria.Name | DuplicateCriteria.Content, ct);
        if (OperatingSystem.IsWindows()) Assert.Equal(["a/report.txt,b/REPORT.txt"], byNameAndContent.Groups.Select(Names));
        else Assert.Empty(byNameAndContent.Groups); // names differ in case, which counts on Linux

        var byName = DuplicateFinder.Find(items, DuplicateCriteria.Name, ct);
        Assert.Equal(OperatingSystem.IsWindows() ? "a/report.txt,b/REPORT.txt,c/report.txt" : "a/report.txt,c/report.txt", Names(Assert.Single(byName.Groups)));

        var bySize = DuplicateFinder.Find(items, DuplicateCriteria.Size, ct);
        Assert.Equal(["a/report.txt,b/REPORT.txt,d/copy.txt"], bySize.Groups.Select(Names));
        Assert.Empty(bySize.Unreadable);
        Assert.Throws<ArgumentException>(() => DuplicateFinder.Find(items, DuplicateCriteria.None, ct));
    }

    [Fact]
    public void Content_compares_all_bytes_of_large_files_with_equal_starts()
    {
        var start = new byte[200 * 1024];
        var one = start.Concat(new byte[] { 1 }).ToArray();
        var two = start.Concat(new byte[] { 2 }).ToArray();
        File.WriteAllBytes(Path.Combine(_dir.Path, "one.bin"), one);
        File.WriteAllBytes(Path.Combine(_dir.Path, "two.bin"), two);
        File.WriteAllBytes(Path.Combine(_dir.Path, "one-copy.bin"), one);
        var items = Directory.EnumerateFiles(_dir.Path).Select(p => ItemRef.ForFileSystemPath(p, EntryKind.File)).ToList();
        var group = Assert.Single(DuplicateFinder.Find(items, DuplicateCriteria.Content, TestContext.Current.CancellationToken).Groups);
        Assert.Equal(["one-copy.bin", "one.bin"], group.Select(i => i.Name).Order());
    }

    [Fact]
    public void Refining_intersects_subtracts_or_appends_found_items()
    {
        var a = (ItemRef.ForFileSystemPath(Path.Combine(_dir.Path, "a"), EntryKind.File), "");
        var b = (ItemRef.ForFileSystemPath(Path.Combine(_dir.Path, "b"), EntryKind.File), "");
        var c = (ItemRef.ForFileSystemPath(Path.Combine(_dir.Path, "c"), EntryKind.File), "");
        List<(ItemRef Item, string Relative)> previous = [a, b];
        List<(ItemRef Item, string Relative)> found = [b, c];
        string Names(List<(ItemRef Item, string Relative)> list) => string.Join(",", list.Select(x => x.Item.Name));
        Assert.Equal("b", Names(Refine.Combine(previous, found, RefineMode.Intersect)));
        Assert.Equal("a", Names(Refine.Combine(previous, found, RefineMode.Subtract)));
        Assert.Equal("a,b,c", Names(Refine.Combine(previous, found, RefineMode.Append)));
        Assert.Equal("b,c", Names(Refine.Combine(previous, found, RefineMode.Replace)));
    }

    [Fact]
    public void Saved_searches_and_the_ignore_list_survive_the_settings_file()
    {
        var settings = new Core.State.AppSettings();
        settings.SavedSearches.Add(new SavedSearch
        {
            Name = "Big logs",
            LoadOnOpen = true,
            Criteria = new SearchCriteria
            {
                Names = "*.log",
                Text = "4D 5A",
                Hex = true,
                Advanced = new AdvancedSearchCriteria
                {
                    AttributesSet = FileAttributes.Archive,
                    AttributesClear = FileAttributes.Directory | FileAttributes.Hidden,
                    SizeAtLeast = 1.5,
                    SizeAtLeastUnit = SizeUnit.MB,
                    Modified = new TimeCriterion { Mode = TimeFilterMode.Within, Amount = 3, Unit = TimeUnit.Weeks },
                    Created = new TimeCriterion { Mode = TimeFilterMode.Between, From = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Local) },
                },
            },
        });
        settings.SearchIgnoredFolders.Add(new IgnoredFolderEntry { Folder = "node_modules", Enabled = false });
        var json = System.Text.Json.JsonSerializer.Serialize(settings, Core.State.StateJsonContext.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, Core.State.StateJsonContext.Default.AppSettings)!;
        var saved = Assert.Single(back.SavedSearches);
        Assert.True(saved.LoadOnOpen);
        Assert.Equal("*.log", saved.Criteria.Names);
        Assert.True(saved.Criteria.Hex);
        Assert.Equal(FileAttributes.Archive, saved.Criteria.Advanced.AttributesSet);
        Assert.Equal(FileAttributes.Directory | FileAttributes.Hidden, saved.Criteria.Advanced.AttributesClear);
        Assert.Equal(1.5, saved.Criteria.Advanced.SizeAtLeast);
        Assert.Equal(SizeUnit.MB, saved.Criteria.Advanced.SizeAtLeastUnit);
        Assert.Equal(TimeUnit.Weeks, saved.Criteria.Advanced.Modified.Unit);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 0), saved.Criteria.Advanced.Created.From);
        Assert.False(Assert.Single(back.SearchIgnoredFolders).Enabled);
        Assert.Equal(saved.Criteria.Advanced.Summary(), settings.SavedSearches[0].Criteria.Advanced.Summary());
    }

    [Fact]
    public void Text_in_hex_mode_and_whole_words_describe_themselves()
    {
        Assert.True(HexPattern.TryParse("CAFE", out var bytes, out _));
        Assert.Contains("bytes CA FE", new SearchQuery { Roots = ["x"], Bytes = bytes }.Describe());
        Assert.Contains("text \"foo\" (whole words)", new SearchQuery { Roots = ["x"], Text = "foo", WholeWords = true }.Describe());
    }
}
