using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

public sealed class P3FeatureTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Search_streams_matches_with_provenance_and_content_filter()
    {
        _dir.File("a/one.cs", "class One { }");
        _dir.File("a/b/two.cs", "class Two { // needle }");
        _dir.File("a/b/three.txt", "needle");
        _dir.File("a/c/four.cs", new string('x', 3 * 1024 * 1024) + "needle"); // beyond the first chunk
        var set = new ResultSet("s", "t", "p");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], Names = Mask.Parse("*.cs") }, set).Run(CancellationToken.None);
        Assert.Equal(3, set.Count);
        Assert.True(set.IsComplete);
        var withText = new ResultSet("s2", "t", "p");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], Names = Mask.Parse("*.cs"), Text = "NEEDLE" }, withText).Run(CancellationToken.None);
        var names = withText.Snapshot().Select(s => s.Item.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["four.cs", "two.cs"], names);
        var rel = withText.Snapshot().Single(s => s.Item.Name == "two.cs").Relative;
        Assert.Equal(Path.Combine("a", "b"), rel);
        var regex = new ResultSet("s3", "t", "p");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], Text = @"class\s+T\w+", Regex = true }, regex).Run(CancellationToken.None);
        Assert.Equal("two.cs", Assert.Single(regex.Snapshot()).Item.Name);
    }

    [Fact]
    public void Searching_within_results_narrows_the_set_and_reports_vanished_items()
    {
        _dir.File("a/one.cs", "class One { }");
        _dir.File("a/b/two.cs", "class Two { // needle }");
        _dir.File("a/b/gone.cs", "needle");
        _dir.File("a/three.txt", "needle");
        var all = new ResultSet("all", "t", "p");
        new SearchSession(new SearchQuery { Roots = [_dir.Path], Names = Mask.Parse("*.cs") }, all).Run(CancellationToken.None);
        Assert.Equal(3, all.Count);
        File.Delete(Path.Combine(_dir.Path, "a", "b", "gone.cs"));

        var narrowed = new ResultSet("narrow", "t", "p");
        var query = new SearchQuery { Roots = [], Text = "needle", WithinResults = all.Snapshot() };
        Assert.Contains("within 3 earlier results", query.Describe());
        new SearchSession(query, narrowed).Run(CancellationToken.None);
        // three.txt contains the text too, but it was never among the results.
        var hit = Assert.Single(narrowed.Snapshot());
        Assert.Equal("two.cs", hit.Item.Name);
        Assert.Equal(Path.Combine("a", "b"), hit.Relative);
        Assert.True(narrowed.IsComplete);
        Assert.Contains(narrowed.Issues, i => i.StartsWith("No longer exists", StringComparison.Ordinal) && i.EndsWith("gone.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_marks_differences_on_both_sides_with_time_tolerance()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;
        EntryData F(string n, long size, long ticks) => new(n, EntryKind.File, size, ticks);
        var left = new List<EntryData> { F("same.txt", 10, t), F("diff.txt", 10, t), F("left.txt", 1, t), F("fat.txt", 5, t), new("dir", EntryKind.Directory) };
        var right = new List<EntryData> { F("SAME.txt", 10, t), F("diff.txt", 11, t), F("right.txt", 1, t), F("fat.txt", 5, t + TimeSpan.FromSeconds(1.5).Ticks), new("dir", EntryKind.Directory) };
        var r = DirectoryCompare.Compare(left, right, CompareCriteria.Size | CompareCriteria.Time, TimeSpan.FromSeconds(2), null, CancellationToken.None);
        Assert.Equal(["diff.txt", "left.txt"], r.LeftMarks.OrderBy(x => x));
        Assert.Equal(["diff.txt", "right.txt"], r.RightMarks.OrderBy(x => x));
        Assert.Equal(3, r.Same);
        var strict = DirectoryCompare.Compare(left, right, CompareCriteria.Time, TimeSpan.Zero, null, CancellationToken.None);
        Assert.Contains("fat.txt", strict.LeftMarks);
        var content = DirectoryCompare.Compare(left, right, CompareCriteria.Content, TimeSpan.Zero, (a, b) => a == "same.txt", CancellationToken.None);
        Assert.Contains("fat.txt", content.LeftMarks);
        Assert.DoesNotContain("same.txt", content.LeftMarks);
    }

    private string MakeZip()
    {
        var path = Path.Combine(_dir.Path, "test.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Add("readme.txt", "hello zip");
        Add("docs/guide.md", "# guide");
        Add("docs/deep/nested.txt", "deep");
        Add("dup.txt", "first");
        Add("dup.txt", "second");
        Add("../evil.txt", "should never escape");
        zip.CreateEntry("emptydir/");
        return path;
    }

    [Fact]
    public async Task Zip_browses_members_distinguishes_duplicates_and_reads_content()
    {
        var zipPath = MakeZip();
        var provider = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        Assert.True(provider.IsContainer("x.ZIP"));
        var root = provider.GetContainerLocation(zipPath)!;
        var entries = await Enumerate(provider, root);
        Assert.Contains(entries, e => e.Name == "docs" && e.Kind == EntryKind.Directory);
        Assert.Contains(entries, e => e.Name == "emptydir" && e.Kind == EntryKind.Directory);
        var dups = entries.Where(e => e.Name == "dup.txt").ToList();
        Assert.Equal(2, dups.Count);
        var refs = dups.Select(d => provider.GetItemRef(root, d)).ToList();
        Assert.NotEqual(refs[0], refs[1]);
        var contents = refs.Select(r => ReadAll(provider.OpenContent(r)!)).OrderBy(s => s).ToArray();
        Assert.Equal(["first", "second"], contents);
        var evil = Assert.Single(entries, e => e.Name == "evil.txt");
        Assert.True(evil.Has(EntryFlags.Unavailable));
        var docs = provider.GetChildLocation(root, entries.First(e => e.Name == "docs"))!;
        Assert.Equal("docs", docs.Path);
        Assert.Equal(zipPath + Path.DirectorySeparatorChar + "docs", provider.GetDisplayPath(docs));
        var sub = await Enumerate(provider, docs);
        Assert.Contains(sub, e => e.Name == "guide.md");
        Assert.Equal(Location.FileSystem(Path.GetDirectoryName(zipPath)!), provider.GetParent(root));
        Assert.True(provider.TryParse(Path.Combine(zipPath, "docs", "deep"), null, out var parsed));
        Assert.Equal("docs/deep", parsed!.Path);
        provider.Release(zipPath);
    }

    [Fact]
    public async Task Zip_extraction_refuses_escaping_names_and_propagates_origin()
    {
        var zipPath = MakeZip();
        var providers = new ProviderRegistry();
        var fsOps = new MarkingOps();
        providers.Register(new LocalFileSystemProvider());
        var zip = new ZipProvider(Path.Combine(_dir.Path, "tmp"));
        providers.Register(zip);
        fsOps.Marks[zipPath] = "[ZoneTransfer]\nZoneId=3";
        var manager = new JobManager(fsOps, providers, Path.Combine(_dir.Path, "journal"));
        var root = zip.GetContainerLocation(zipPath)!;
        var entries = await Enumerate(zip, root);
        var dest = _dir.Dir("out");
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.Extract,
            Sources = entries.Select(e => zip.GetItemRef(root, e)).ToList(),
            Destination = Location.FileSystem(dest),
            Options = new TransferOptions { Conflicts = ConflictPolicy.KeepBothRenameIncoming },
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal("hello zip", File.ReadAllText(Path.Combine(dest, "readme.txt")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(dest, "docs", "deep", "nested.txt")));
        Assert.Equal(2, Directory.GetFiles(dest, "dup*.txt").Length);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "evil.txt")));
        Assert.Contains(job.Issues, i => i.Path.Contains("evil"));
        // Every extracted file carries the archive's origin mark.
        Assert.Contains(Path.Combine(dest, "docs", "deep", "nested.txt"), fsOps.Marks.Keys.Select(k => k.Replace(JournalRecovery.StagedPrefix, "")), StringComparer.OrdinalIgnoreCase);
        zip.Release(zipPath);
    }

    private sealed class MarkingOps : PortableFileOperations
    {
        public readonly Dictionary<string, string> Marks = new(StringComparer.OrdinalIgnoreCase);

        public override string? ReadOriginMark(string path) => Marks.GetValueOrDefault(path);

        public override bool WriteOriginMark(string path, string mark)
        {
            Marks[path] = mark;
            return true;
        }

        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            base.Move(source, destination, replaceExisting, writeThrough);
            if (Marks.Remove(source, out var m)) Marks[destination] = m;
        }
    }

    private static async Task<List<EntryData>> Enumerate(ResourceProvider provider, Location location)
    {
        var list = new List<EntryData>();
        await provider.EnumerateAsync(location, new Sink(list), CancellationToken.None);
        return list;
    }

    private static string ReadAll(IContentSource s)
    {
        using (s)
        {
            var buf = new byte[s.Length];
            int n = s.Read(0, buf);
            return Encoding.UTF8.GetString(buf, 0, n);
        }
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }
}
