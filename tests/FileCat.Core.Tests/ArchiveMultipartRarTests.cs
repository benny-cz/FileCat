using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

public sealed class ArchiveMultipartRarTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly ArchiveProvider _archives;
    private readonly string _scratch;
    private readonly List<string> _files = [];
    // Independently extracted and hashed with 7-Zip 24.01, rather than another FileCat reader.
    private static readonly Dictionary<string, (int Bytes, string SHA256)> Expected = new(StringComparer.Ordinal)
    {
        ["exe/test.exe"] = (45056, "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106"),
        ["jpg/test.jpg"] = (40372, "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"),
        ["тест.txt"] = (15498, "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9")
    };

    public ArchiveMultipartRarTests()
    {
        _scratch = Path.Combine(_dir.Path, "scratch");
        _archives = new ArchiveProvider(_scratch, _providers);
        var zip = new ZipProvider(_scratch) { OtherArchives = _archives };
        _providers.Register(_archives);
        _providers.Register(zip);
        _providers.Register(new LocalFileSystemProvider { ContainerDetector = new ContainerDetectors(zip, _archives) });
    }

    public void Dispose()
    {
        foreach (string path in _files) _archives.Release(path);
        _dir.Dispose();
    }

    private string Fixture(int start, params int[] omitted)
    {
        for (int part = 1; part <= 6; part++)
        {
            if (omitted.Contains(part)) continue;
            string name = $"Rar5.multi.part{part:00}.rar";
            string path = Path.Combine(_dir.Path, name);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", name), path);
            _files.Add(path);
        }
        return PathUtil.WithDiskCase(Path.Combine(_dir.Path, $"Rar5.multi.part{start:00}.rar"));
    }

    private static string MemberPath(ItemRef item) => (item.Parent.Path.TrimEnd('/') + "/" + item.Name).TrimStart('/');
    private Dictionary<string, string> InputHashes() => _files.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    private void CheckInputs(Dictionary<string, string> before)
    {
        Assert.All(before, p => Assert.Equal(p.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))));
        Assert.False(Directory.Exists(_scratch));
    }

    private void CheckContent(ItemRef item)
    {
        var expected = Expected[MemberPath(item)];
        Assert.Equal(expected.Bytes, item.Size);
        using var content = _archives.OpenContent(item);
        Assert.NotNull(content);
        Assert.Equal(expected.Bytes, content.Length);
        byte[] bytes = new byte[expected.Bytes + 1];
        int total = 0, n;
        while ((n = content.Read(total, bytes.AsSpan(total))) > 0) total += n;
        Assert.Equal(expected.Bytes, total);
        Assert.Equal(expected.SHA256, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, total))).ToLowerInvariant());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Every_part_of_a_complete_set_has_the_same_members_and_independent_bytes(int start)
    {
        string archive = Fixture(start);
        var before = InputHashes();
        var warnings = new List<string>();
        var items = new ProviderArchiveMembers(_providers).List(archive, warnings.Add, TestContext.Current.CancellationToken)
            .Where(i => !i.IsContainer).ToList();
        Assert.Empty(warnings);
        Assert.Equal(Expected.Keys.Order(), items.Select(MemberPath).Order());
        Assert.All(items, i => { Assert.Equal(archive, i.Parent.Container!.Path); Assert.Equal(0, i.Ordinal); });
        Assert.False(Directory.Exists(_scratch));
        foreach (var item in items.AsEnumerable().Reverse().Concat(items)) CheckContent(item);
        CheckInputs(before);
    }

    [Fact]
    public void A_missing_middle_part_is_reported_before_content_and_in_initial_and_narrowed_searches()
    {
        string archive = Fixture(1, 3);
        var before = InputHashes();
        var members = new ProviderArchiveMembers(_providers);
        var warnings = new List<string>();
        var items = members.List(archive, warnings.Add, TestContext.Current.CancellationToken).Where(i => !i.IsContainer).ToList();
        Assert.Equal(3, items.Count);
        Assert.Contains(warnings, w => w.Contains("Some volumes", StringComparison.Ordinal));
        var subset = items.Where(i => MemberPath(i) != "exe/test.exe").Select(i => (Item: i, Relative: "frozen " + MemberPath(i))).ToList();
        foreach (bool narrow in new[] { false, true })
        foreach ((long? min, long? max) in new (long?, long?)[] { (null, null), (100, null), (null, 1) })
        {
            var query = new SearchQuery { Roots = [_dir.Path], Recursive = false, Names = Mask.Parse("*"), IncludeDirectories = false,
                Archives = narrow ? null : members, ResultArchives = members, WithinResults = narrow ? subset : null, MinSize = min, MaxSize = max };
            var results = new ResultSet("owned", "owned", "incomplete numbered RAR control");
            var session = new SearchSession(query, results);
            session.Run(TestContext.Current.CancellationToken);
            Assert.True(session.Finished);
            Assert.Contains(session.Log, e => e.Kind == SearchLogKind.Warning && e.Detail!.Contains("Some volumes", StringComparison.Ordinal));
            Assert.Contains(results.Issues, w => w.Contains("Some volumes", StringComparison.Ordinal));
            var eligible = narrow ? subset.Select(i => i.Item) : items;
            var expected = eligible.Where(i => (min is null || i.Size >= min) && (max is null || i.Size <= max)).Select(MemberPath).Order();
            var actual = results.Snapshot().Where(r => r.Item.Parent.Container?.Path == archive).ToList();
            Assert.Equal(expected, actual.Select(r => MemberPath(r.Item)).Order());
            if (narrow) Assert.All(actual, r => Assert.Equal(subset.Single(s => s.Item.Equals(r.Item)).Relative, r.Relative));
        }
        Assert.False(Directory.Exists(_scratch));
        foreach (var item in items)
        {
            if (MemberPath(item) != "jpg/test.jpg") CheckContent(item);
            else Assert.Throws<InvalidDataException>(() => CheckContent(item));
        }
        CheckInputs(before);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(1)]
    [InlineData(0)]
    public void Missing_last_first_or_all_later_parts_retain_an_explicit_warning_or_refusal(int missing)
    {
        string archive = missing == 0 ? Fixture(1, 2, 3, 4, 5, 6) : Fixture(missing == 1 ? 3 : 1, missing);
        var before = InputHashes();
        var warnings = new List<string>();
        var error = Record.Exception(() => new ProviderArchiveMembers(_providers).List(archive, warnings.Add, TestContext.Current.CancellationToken).ToList());
        Assert.True(error is InvalidDataException || warnings.Any(w => w.Contains("damaged", StringComparison.Ordinal) || w.Contains("Some volumes", StringComparison.Ordinal)));
        CheckInputs(before);
    }
}
