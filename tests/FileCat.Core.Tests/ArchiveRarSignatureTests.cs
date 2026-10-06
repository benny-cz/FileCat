using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveRarSignatureTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("Rar.rar")]
    [InlineData("Rar5.rar")]
    [InlineData("Rar2.multi.r00")]
    [InlineData("Rar5.multi.part03.rar")]
    public void Real_rar_generations_and_secondary_volumes_are_recognized_by_bytes(string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fixture);
        byte[] before = SHA256.HashData(File.ReadAllBytes(path));
        using (var input = File.OpenRead(path)) Assert.Equal(ArchiveKind.Rar, ArchiveFormats.BySignature(input));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Theory]
    [InlineData("Rar.rar")]
    [InlineData("Rar5.rar")]
    public void A_rar_without_a_known_suffix_opens_and_preserves_independent_member_bytes(string fixture)
    {
        string path = Path.Combine(_dir.Path, "owned.unknown-suffix");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fixture), path);
        byte[] before = SHA256.HashData(File.ReadAllBytes(path));
        Assert.Null(ArchiveFormats.ByName(path));
        ArchiveKind? kind;
        using (var input = File.OpenRead(path)) kind = ArchiveFormats.BySignature(input);
        Assert.Equal(ArchiveKind.Rar, kind);
        using (var reader = ArchiveFormats.Open(path, kind.GetValueOrDefault(), Path.GetFileName(path)))
        {
            var warnings = new List<string>();
            var members = reader.List(warnings.Add, TestContext.Current.CancellationToken).Where(m => m.Kind == MemberKind.File).ToList();
            Assert.Empty(warnings);
            Assert.Equal(3, members.Count);
            var jpg = members.Single(m => m.Path.Replace('\\', '/') == "jpg/test.jpg");
            using var content = reader.Open(jpg.Index, TestContext.Current.CancellationToken);
            using var bytes = new MemoryStream();
            content.CopyTo(bytes);
            Assert.Equal(40372, bytes.Length);
            Assert.Equal("b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53",
                Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant());
        }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    // RARLAB's technote specifies the seven-byte RAR 4 and eight-byte RAR 5 markers.
    [Theory]
    [InlineData("526172211A0700", true)]
    [InlineData("526172211A070100", true)]
    [InlineData("", false)]
    [InlineData("526172211A07", false)]
    [InlineData("526172211A0701", false)]
    [InlineData("526172211A070200", false)]
    [InlineData("526172211A0701FF", false)]
    [InlineData("526172201A0700", false)]
    public void Only_complete_supported_rar_signatures_are_recognized(string hex, bool expected)
    {
        using var input = new MemoryStream(Convert.FromHexString(hex));
        Assert.Equal(expected ? ArchiveKind.Rar : (ArchiveKind?)null, ArchiveFormats.BySignature(input));
    }
}
