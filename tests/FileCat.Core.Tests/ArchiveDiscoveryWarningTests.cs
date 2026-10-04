using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ArchiveDiscoveryWarningTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly ArchiveProvider _archives;
    private readonly ZipProvider _zip;
    private readonly List<string> _files = [];

    public ArchiveDiscoveryWarningTests()
    {
        _archives = new ArchiveProvider(Path.Combine(_dir.Path, "scratch"), _providers);
        _zip = new ZipProvider(Path.Combine(_dir.Path, "scratch")) { OtherArchives = _archives };
        _providers.Register(_archives);
        _providers.Register(_zip);
        _providers.Register(new LocalFileSystemProvider { ContainerDetector = new ContainerDetectors(_zip, _archives) });
    }

    public void Dispose()
    {
        foreach (var file in _files) { _zip.Release(file); _archives.Release(file); }
        _dir.Dispose();
    }

    private string Tar(bool gzip, bool damaged)
    {
        using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (name, size) in new (string, int)[] { ("first.txt", 10), ("dir/dup.txt", 1), ("dir/dup.txt", 12), ("dir/skip.bin", 7) })
            {
                using var data = new MemoryStream(Enumerable.Repeat((byte)'x', size).ToArray());
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = data,
                    ModificationTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                });
            }
        }
        var raw = bytes.ToArray();
        if (damaged)
        {
            // The first 512-byte header and its padded data are intact; the next size is not octal.
            raw[1024 + 124] = (byte)'x';
            using var independent = new TarReader(new MemoryStream(raw));
            Assert.Equal("first.txt", independent.GetNextEntry()!.Name);
            Assert.Throws<InvalidDataException>(() => independent.GetNextEntry());
        }
        string file = Path.Combine(_dir.Path, gzip ? "members.tar.gz" : "members.tar");
        if (gzip)
        {
            using var compressed = new GZipStream(File.Create(file), CompressionMode.Compress);
            compressed.Write(raw);
        }
        else File.WriteAllBytes(file, raw);
        _files.Add(file);
        return file;
    }

    private (ResultSet Result, SearchSession Session) Search(SearchCriteria criteria, List<(ItemRef Item, string Relative)>? within = null)
    {
        Assert.True(criteria.TryBuildQuery(DateTime.UtcNow, [], within, out var query, out var error, new ProviderArchiveMembers(_providers)), error);
        var result = new ResultSet("archive-warning", "archive-warning", "owned archive scope control");
        var session = new SearchSession(query!, result);
        session.Run(TestContext.Current.CancellationToken);
        Assert.True(session.Finished);
        Assert.True(result.IsComplete);
        return (result, session);
    }

    [Fact]
    public void Damage_after_a_valid_TAR_member_is_visible_in_the_initial_search_log()
    {
        string file = Tar(gzip: false, damaged: true);
        var (result, session) = Search(new SearchCriteria { LookIn = _dir.Path, Names = "*.txt", InsideArchives = true });
        Assert.Equal("first.txt", Assert.Single(result.Snapshot()).Item.Name);
        var warning = Assert.Single(session.Log, e => e.Kind == SearchLogKind.Warning);
        Assert.Equal(PathUtil.WithDiskCase(file), warning.Path);
        Assert.Contains("damaged after", warning.Detail, StringComparison.Ordinal);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public void One_archive_warning_is_reported_once_even_when_several_member_folders_are_listed()
    {
        string file = Path.Combine(_dir.Path, "duplicates.zip");
        using (var archive = new ZipArchive(File.Create(file), ZipArchiveMode.Create))
            foreach (string name in new[] { "dup.txt", "dup.txt", "folder/leaf.txt" })
            {
                using var data = archive.CreateEntry(name).Open();
                data.Write("owned member"u8);
            }
        _files.Add(file);
        var (result, session) = Search(new SearchCriteria { LookIn = _dir.Path, Names = "*.txt", InsideArchives = true });
        Assert.Equal(3, result.Count);
        Assert.Equal([0, 1], result.Snapshot().Where(r => r.Item.Name == "dup.txt").Select(r => r.Item.Ordinal).Order());
        var warning = Assert.Single(session.Log, e => e.Kind == SearchLogKind.Warning);
        Assert.Equal(PathUtil.WithDiskCase(file), warning.Path);
        Assert.Contains("duplicate names", warning.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TAR_and_gzip_TAR_results_keep_duplicate_identity_current_metadata_and_content_references(bool gzip)
    {
        Tar(gzip, damaged: false);
        var (original, _) = Search(new SearchCriteria { LookIn = _dir.Path, Names = "*.txt", InsideArchives = true });
        var within = original.Snapshot();
        Assert.Equal(3, within.Count);
        var (narrowed, session) = Search(new SearchCriteria { Names = "*.txt", InsideArchives = false,
            Advanced = new AdvancedSearchCriteria
            {
                SizeAtLeast = 8, SizeAtLeastUnit = SizeUnit.Bytes,
                Modified = new TimeCriterion { Mode = TimeFilterMode.Between, From = new DateTime(2025, 12, 30), To = new DateTime(2026, 1, 3) },
            } }, within);
        Assert.Equal(["dup.txt#1", "first.txt#0"], narrowed.Snapshot().Select(r => r.Item.Name + "#" + r.Item.Ordinal).Order());
        foreach (var (item, relative) in narrowed.Snapshot())
        {
            Assert.Equal(Schemes.Archive, item.Parent.Scheme);
            Assert.Equal(within.Single(r => r.Item.Equals(item)).Relative, relative);
            using var content = _archives.OpenContent(item);
            Assert.NotNull(content);
            var bytes = new byte[item.Size];
            Assert.Equal(bytes.Length, content.Read(0, bytes));
            Assert.All(bytes, b => Assert.Equal((byte)'x', b));
        }
        Assert.All(session.Log, e => Assert.Equal(SearchLogKind.Warning, e.Kind));
    }
}
