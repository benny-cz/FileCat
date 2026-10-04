using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ArchiveUnknownSizeSearchTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly ArchiveProvider _archives;
    private readonly ZipProvider _zip;
    private readonly List<string> _files = [];

    public ArchiveUnknownSizeSearchTests()
    {
        _archives = new ArchiveProvider(Path.Combine(_dir.Path, "scratch"), _providers);
        _zip = new ZipProvider(Path.Combine(_dir.Path, "scratch")) { OtherArchives = _archives };
        _providers.Register(_archives);
        _providers.Register(_zip);
        _providers.Register(new LocalFileSystemProvider { ContainerDetector = new ContainerDetectors(_zip, _archives) });
    }

    public void Dispose()
    {
        foreach (string path in _files) { _archives.Release(path); _zip.Release(path); }
        _dir.Dispose();
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (string format in new[] { "gz", "bz2", "xz" })
        foreach (bool narrowed in new[] { false, true })
        foreach (string criterion in new[] { "minimum", "maximum", "none" })
            yield return [format, narrowed, criterion];
    }

    private (ResultSet Results, SearchSession Session) Search(string criterion,
        List<(ItemRef Item, string Relative)>? original)
    {
        var criteria = new SearchCriteria
        {
            LookIn = _dir.Path, Names = "*.txt", InsideArchives = original is null,
            Advanced = new AdvancedSearchCriteria
            {
                SizeAtLeast = criterion == "minimum" ? 100 : null, SizeAtLeastUnit = SizeUnit.Bytes,
                SizeAtMost = criterion == "maximum" ? 1 : null, SizeAtMostUnit = SizeUnit.Bytes
            }
        };
        Assert.True(criteria.TryBuildQuery(DateTime.UtcNow, [], original, out var query, out var error,
            new ProviderArchiveMembers(_providers)), error);
        var result = new ResultSet("unknown-size", "unknown-size", "owned independent archive fixture");
        var session = new SearchSession(query!, result);
        session.Run(TestContext.Current.CancellationToken);
        Assert.True(session.Finished);
        Assert.True(result.IsComplete);
        return (result, session);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unknown_lengths_are_excluded_and_explained_only_when_a_size_criterion_is_active(
        string format, bool narrowed, string criterion)
    {
        // Independent Python gzip/bz2/lzma encoders produced these fixtures; each decodes to the five bytes "alpha".
        byte[] compressed = Convert.FromBase64String(format switch
        {
            "gz" => "H4sIAAAAAAAC/0vMKchIBABqOeDQBQAAAA==",
            "bz2" => "QlpoOTFBWSZTWVnVmPwAAACBgCBEQAAgACGaaDNNAR4u5IpwoSCzqzH4",
            _ => "/Td6WFoAAATm1rRGAgAhARYAAAB0L+WjAQAEYWxwaGEAAAAAWCfV6URuhJkAAR0FuC2Arx+2830BAAAAAARZWg=="
        });
        string path = Path.Combine(_dir.Path, "notes.txt." + format);
        File.WriteAllBytes(path, compressed);
        _files.Add(path);
        var members = new ProviderArchiveMembers(_providers).List(path, TestContext.Current.CancellationToken)
            .Select(i => (Item: i, Relative: "original relative folder")).ToList();
        var original = Assert.Single(members).Item;
        Assert.Equal(-1, original.Size);
        var (result, session) = Search(criterion, narrowed ? members : null);
        if (criterion == "none")
        {
            var match = Assert.Single(result.Snapshot());
            Assert.Equal(original, match.Item);
            Assert.Equal(-1, match.Item.Size);
            if (narrowed) Assert.Equal("original relative folder", match.Relative);
            Assert.Empty(session.Log);
        }
        else
        {
            Assert.Empty(result.Snapshot());
            var exclusion = Assert.Single(session.Log);
            Assert.Equal(SearchLogKind.Inaccessible, exclusion.Kind);
            Assert.Equal(original.Parent, exclusion.Parent);
            Assert.Equal(original.Name, exclusion.Name);
            Assert.Equal(original.ToString(), exclusion.Path);
            Assert.Contains("size is unknown", exclusion.Detail, StringComparison.Ordinal);
            Assert.Single(result.Issues);
        }
        Assert.Single(members);
        Assert.Equal(compressed, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "scratch")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Known_lengths_still_filter_exactly_including_zero_byte_members(bool narrowed)
    {
        string path = Path.Combine(_dir.Path, "known.zip");
        using (var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
            foreach (var (name, size) in new[] { ("empty.txt", 0), ("small.txt", 5), ("big.txt", 150) })
            {
                using var data = archive.CreateEntry(name).Open();
                data.Write(new byte[size]);
            }
        _files.Add(path);
        var members = new ProviderArchiveMembers(_providers).List(path, TestContext.Current.CancellationToken)
            .Select(i => (Item: i, Relative: "original known scope")).ToList();
        foreach (string criterion in new[] { "minimum", "maximum" })
        {
            var (result, session) = Search(criterion, narrowed ? members : null);
            Assert.Equal(criterion == "minimum" ? "big.txt" : "empty.txt", Assert.Single(result.Snapshot()).Item.Name);
            Assert.Empty(session.Log);
        }
        Assert.Equal(3, members.Count);
    }
}
