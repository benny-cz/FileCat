using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;

namespace FileCat.Core.Tests;

public sealed class ArchiveUdfRevisionTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    private static string MemberPath(ItemRef item) => (item.Parent.Path.TrimEnd('/') + "/" + item.Name).TrimStart('/');

    [Theory]
    [InlineData("1.02")]
    [InlineData("1.50")]
    [InlineData("2.00")]
    [InlineData("2.01")]
    [InlineData("2.50")]
    [InlineData("2.60")]
    public void Pure_udf_revisions_preserve_native_names_bytes_empty_folders_and_search_results(string revision)
    {
        string data = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "PureUdfRevisions.json")));
        var fixture = manifest.RootElement.GetProperty("Fixtures").EnumerateArray().Single(f => f.GetProperty("Revision").GetString() == revision);
        string image = fixture.GetProperty("Image").GetString()!;
        string path = PathUtil.WithDiskCase(Path.Combine(_dir.Path, image));
        using (var zip = ZipFile.OpenRead(Path.Combine(data, "PureUdfRevisions.zip")))
            zip.GetEntry(image)!.ExtractToFile(path);
        string expectedHash = fixture.GetProperty("SHA256").GetString()!;
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        using (var input = File.OpenRead(path))
        {
            Assert.Equal(ArchiveKind.DiscImage, ArchiveFormats.BySignature(input));
            input.Position = 0x8000;
            var descriptors = new byte[16 * 2048];
            input.ReadExactly(descriptors);
            Assert.Equal("BEA01"u8.ToArray(), descriptors.AsSpan(1, 5).ToArray());
            for (int offset = 0; offset < descriptors.Length; offset += 2048)
                Assert.False(descriptors.AsSpan(offset + 1, 5).SequenceEqual("CD001"u8));
        }
        var expected = fixture.GetProperty("Files").EnumerateArray().ToDictionary(f => f.GetProperty("Path").GetString()!,
            f => (Bytes: f.GetProperty("Bytes").GetInt64(), SHA256: f.GetProperty("SHA256").GetString()!), StringComparer.Ordinal);
        var directories = fixture.GetProperty("Directories").EnumerateArray().Select(d => d.GetString()!).Order().ToArray();
        var providers = new ProviderRegistry();
        string scratch = Path.Combine(_dir.Path, "scratch");
        var archives = new ArchiveProvider(scratch, providers);
        providers.Register(archives);
        providers.Register(new LocalFileSystemProvider { ContainerDetector = archives });
        var members = new ProviderArchiveMembers(providers);
        var warnings = new List<string>();
        try
        {
            var all = members.List(path, warnings.Add, TestContext.Current.CancellationToken).ToList();
            Assert.Empty(warnings);
            Assert.Equal(directories, all.Where(i => i.IsContainer).Select(MemberPath).Order());
            var files = all.Where(i => !i.IsContainer).ToList();
            Assert.Equal(expected.Keys.Order(), files.Select(MemberPath).Order());
            foreach (var item in files.AsEnumerable().Reverse().Concat(files))
            {
                var wanted = expected[MemberPath(item)];
                Assert.Equal(wanted.Bytes, item.Size);
                using var content = archives.OpenContent(item);
                Assert.NotNull(content);
                byte[] bytes = new byte[checked((int)wanted.Bytes + 1)];
                int total = 0, n;
                while ((n = content.Read(total, bytes.AsSpan(total))) > 0) total += n;
                Assert.Equal(wanted.Bytes, total);
                Assert.Equal(wanted.SHA256, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, total))).ToLowerInvariant());
            }
            var frozen = files.OrderBy(MemberPath, StringComparer.Ordinal).Skip(1).Select(i => (Item: i, Relative: "frozen " + MemberPath(i))).ToList();
            foreach (bool narrow in new[] { false, true })
            foreach ((long? min, long? max) in new (long?, long?)[] { (null, null), (100, null), (null, 1) })
            {
                var query = new SearchQuery { Roots = [_dir.Path], Recursive = false, Names = Mask.Parse("*.txt"), IncludeDirectories = false,
                    Archives = narrow ? null : members, ResultArchives = members, WithinResults = narrow ? frozen : null, MinSize = min, MaxSize = max };
                var results = new ResultSet("owned", "owned", "pure UDF native-byte corpus");
                var session = new SearchSession(query, results);
                session.Run(TestContext.Current.CancellationToken);
                Assert.True(session.Finished && results.IsComplete);
                Assert.Empty(session.Log);
                var eligible = narrow ? frozen.Select(i => i.Item) : files;
                var wanted = eligible.Where(i => i.Name.EndsWith(".txt", StringComparison.Ordinal) && (min is null || i.Size >= min) && (max is null || i.Size <= max));
                var actual = results.Snapshot().Where(r => r.Item.Parent.Container?.Path == path).ToList();
                Assert.Equal(wanted.Select(MemberPath).Order(), actual.Select(r => MemberPath(r.Item)).Order());
                if (narrow) Assert.All(actual, r => Assert.Equal(frozen.Single(s => s.Item.Equals(r.Item)).Relative, r.Relative));
            }
        }
        finally { archives.Release(path); }
        Assert.False(Directory.Exists(scratch));
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }
}
