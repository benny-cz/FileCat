using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveLegacyRarTests : IDisposable
{
    private readonly TempDir _dir = new();
    private static readonly string[] Names = ["Rar2.multi.rar", "Rar2.multi.r00", "Rar2.multi.r01", "Rar2.multi.r02",
        "Rar2.multi.r03", "Rar2.multi.r04", "Rar2.multi.r05"];
    // Complete primary-volume extraction independently verified with 7-Zip 24.01.
    private static readonly Dictionary<string, (int Bytes, string SHA256)> Expected = new(StringComparer.Ordinal)
    {
        ["exe/test.exe"] = (45056, "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106"),
        ["jpg/test.jpg"] = (40372, "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"),
        ["тест.txt"] = (15498, "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9")
    };

    public void Dispose() => _dir.Dispose();

    private string Fixture(int start, params int[] omitted)
    {
        for (int i = 0; i < Names.Length; i++)
            if (!omitted.Contains(i)) File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", Names[i]), Path.Combine(_dir.Path, Names[i]));
        return Path.Combine(_dir.Path, Names[start]);
    }

    private Dictionary<string, string> Hashes() => Directory.GetFiles(_dir.Path).ToDictionary(p => p,
        p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));

    private void CheckUnchanged(Dictionary<string, string> before) => Assert.All(before,
        p => Assert.Equal(p.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key)))));

    private static void CheckContent(IMemberReader reader, MemberInfo member)
    {
        var expected = Expected[member.Path.Replace('\\', '/')];
        Assert.Equal(expected.Bytes, member.Size);
        using var content = reader.Open(member.Index, TestContext.Current.CancellationToken);
        using var bytes = new MemoryStream();
        content.CopyTo(bytes);
        Assert.Equal(expected.Bytes, bytes.Length);
        Assert.Equal(expected.SHA256, Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Every_legacy_volume_opens_the_complete_set_once_in_order(int start)
    {
        string path = Fixture(start);
        var before = Hashes();
        using (var reader = ArchiveFormats.Open(path, ArchiveKind.Rar, Path.GetFileName(path)))
        {
            Assert.Equal("RAR (7 volumes)", reader.Format);
            var warnings = new List<string>();
            var files = reader.List(warnings.Add, TestContext.Current.CancellationToken).Where(m => m.Kind == MemberKind.File).ToList();
            Assert.Empty(warnings);
            Assert.Equal(Expected.Keys.Order(), files.Select(m => m.Path.Replace('\\', '/')).Order());
            foreach (var member in files.AsEnumerable().Reverse().Concat(files)) CheckContent(reader, member);
        }
        CheckUnchanged(before);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(0)]
    public void Missing_legacy_middle_last_or_primary_is_warned_or_refused(int missing)
    {
        string path = Fixture(missing == 0 ? 3 : 0, missing);
        var before = Hashes();
        var warnings = new List<string>();
        var error = Record.Exception(() =>
        {
            using var reader = ArchiveFormats.Open(path, ArchiveKind.Rar, Path.GetFileName(path));
            reader.List(warnings.Add, TestContext.Current.CancellationToken).ToList();
        });
        Assert.True(error is InvalidDataException || warnings.Any(w => w.Contains("Some volumes", StringComparison.Ordinal)));
        CheckUnchanged(before);
    }
}
