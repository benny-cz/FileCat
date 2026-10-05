using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class EmptySevenZipTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();
    private readonly ArchiveProvider _archives;
    private readonly string _scratch;
    private readonly List<string> _files = [];

    public EmptySevenZipTests()
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

    private string Fixture(string name)
    {
        string path = Path.Combine(_dir.Path, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", name), path);
        _files.Add(path);
        return path;
    }

    [Theory]
    [InlineData("EmptyEntries.7z", false)]
    [InlineData("EmptyEntries.solid.7z", false)]
    [InlineData("EmptyEntries.encrypted.7z", true)]
    public void Empty_stream_members_open_as_empty_and_real_encrypted_streams_stay_protected(string name, bool encrypted)
    {
        string path = Fixture(name);
        byte[] before = File.ReadAllBytes(path);
        var members = new ProviderArchiveMembers(_providers).List(path, TestContext.Current.CancellationToken)
            .Where(i => i.Kind == EntryKind.File).ToList();
        Assert.Equal(4, members.Count);
        Assert.False(Directory.Exists(_scratch)); // metadata enumeration must not extract contents
        foreach (var member in members)
        {
            bool empty = member.Name.EndsWith("-empty.txt", StringComparison.Ordinal);
            Assert.Equal(encrypted && !empty, member.Flags.HasFlag(EntryFlags.Protected));
            using var content = _archives.OpenContent(member);
            if (encrypted && !empty) { Assert.Null(content); continue; }
            Assert.NotNull(content);
            byte[] expected = empty ? [] : member.Name == "payload.txt" ? "alpha"u8.ToArray() : "beta beta"u8.ToArray();
            Assert.Equal(expected.Length, member.Size);
            Assert.Equal(expected.Length, content.Length);
            var actual = new byte[expected.Length + 1];
            Assert.Equal(expected.Length, content.Read(0, actual));
            Assert.Equal(expected, actual[..expected.Length]);
            Assert.Equal(0, content.Read(expected.Length, actual));
        }
        Assert.False(Directory.Exists(_scratch));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Encrypted_headers_still_refuse_listing_without_a_password()
    {
        string path = Fixture("EmptyEntries.encryptedHeader.7z");
        byte[] before = File.ReadAllBytes(path);
        var error = Assert.Throws<InvalidDataException>(() => new ProviderArchiveMembers(_providers)
            .List(path, TestContext.Current.CancellationToken).ToList());
        Assert.Contains("password", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(_scratch));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
