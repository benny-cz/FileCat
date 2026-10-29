using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Archives;

namespace FileCat.Core.Tests;

/// <summary>
/// FileCat checks each 7z member's CRC-32 as the member is read; SharpCompress's entry streams do not. A damaged archive is
/// refused, never read as if whole. FileCat.Methods.*.7z are 7-Zip 24.01 archives of the same three members, one per
/// coder (TestData/Archives/README.md); the LZMA2 and PPMd fixtures come from the other sets.
/// </summary>
public sealed class SevenZipCrcTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    [Theory]
    // Coders that check nothing themselves: a damaged stream that still decodes is caught by the member's CRC-32.
    [InlineData("FileCat.Methods.Copy.7z", true)]
    [InlineData("FileCat.Methods.LZMA.7z", true)]
    [InlineData("FileCat.Methods.Deflate.7z", true)]
    [InlineData("7Zip.LZMA2.7z", true)]
    [InlineData("FileCat.PPMd.o2.m64k.7z", true)]
    // BZip2 checks its own blocks' CRCs, so it usually refuses first; nothing may still read as if whole.
    [InlineData("FileCat.Methods.BZip2.7z", false)]
    public void Damaged_members_are_refused_never_read_as_if_whole(string fixture, bool crcRefusesSome)
    {
        byte[] original = File.ReadAllBytes(Path.Combine(Fixtures, fixture));
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "damaged.7z");
        File.WriteAllBytes(path, original);
        var intact = ReadAll(path, out var failure);
        Assert.Null(failure);
        Assert.NotEmpty(intact);
        // The packed streams lie between the 32-byte signature header and the end header it points to.
        long packedEnd = 32 + BinaryPrimitives.ReadInt64LittleEndian(original.AsSpan(12));
        Assert.InRange(packedEnd, 33, original.Length);
        var random = new Random(fixture.Aggregate(17, (h, c) => h * 31 + c));
        var outcomes = new Dictionary<string, int>();
        for (int round = 0; round < 300; round++)
        {
            var damaged = (byte[])original.Clone();
            damaged[32 + random.NextInt64(packedEnd - 32)] ^= (byte)(1 << random.Next(8));
            File.WriteAllBytes(path, damaged);
            var read = ReadAll(path, out failure);
            string outcome = failure switch
            {
                null => read.SequenceEqual(intact) ? "same" : "read as if whole, but wrong",
                InvalidDataException { Message: var message } when message.Contains("CRC-32", StringComparison.Ordinal) => "refused by its CRC-32",
                InvalidDataException or IOException or NotSupportedException => "refused",
                _ => $"failed with {failure.GetType().FullName}: {failure.Message}",
            };
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{fixture}: {string.Join("; ", outcomes.Select(o => $"{o.Key} {o.Value}"))}");
        Assert.DoesNotContain("read as if whole, but wrong", outcomes.Keys);
        // Every refusal is one: SharpCompress's own exception types end as InvalidDataException at the member's stream.
        Assert.DoesNotContain(outcomes.Keys, o => o.StartsWith("failed with", StringComparison.Ordinal));
        if (crcRefusesSome) Assert.Contains("refused by its CRC-32", outcomes.Keys);
    }

    [Fact]
    public void A_coder_FileCat_does_not_read_is_refused_as_unsupported()
    {
        // 7-Zip can pack with Deflate64; SharpCompress reads that coder only in ZIP files, which FileCat's build leaves out.
        var hashes = ReadAll(Path.Combine(Fixtures, "FileCat.Methods.Deflate64.7z"), out var failure);
        Assert.Empty(hashes);
        var refusal = Assert.IsType<InvalidDataException>(failure);
        Assert.IsType<NotSupportedException>(refusal.InnerException);
    }

    [Fact]
    public void A_member_read_only_in_part_is_not_judged()
    {
        // Only a member read to its end can be checked; reading its start (a preview) must not be refused.
        using var reader = SharpArchiveReader.Open(Path.Combine(Fixtures, "FileCat.Methods.Copy.7z"));
        var member = reader.List(_ => { }, CancellationToken.None).First(m => m.Kind == MemberKind.File);
        using var content = reader.Open(member.Index, CancellationToken.None);
        var start = new byte[100];
        content.ReadExactly(start);
    }

    /// <summary>Each file member's SHA-256, in listing order, or the first failure.</summary>
    private static List<string> ReadAll(string path, out Exception? failure)
    {
        var hashes = new List<string>();
        failure = null;
        try
        {
            using var reader = SharpArchiveReader.Open(path);
            foreach (var member in reader.List(_ => { }, CancellationToken.None).Where(m => m.Kind == MemberKind.File))
            {
                using var content = reader.Open(member.Index, CancellationToken.None);
                hashes.Add($"{member.Path}:{Convert.ToHexStringLower(SHA256.HashData(content))}");
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        return hashes;
    }
}
