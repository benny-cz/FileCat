using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Archives.Rar;
using Xunit;

namespace FileCat.Core.Tests;

/// <summary>
/// FileCat's own RAR reader against the retired SharpCompress path on every RAR fixture: the same members, the same
/// refusals and byte-identical content (I14 / REL-RAR-LICENSE).
/// </summary>
public sealed class RarReaderTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives");

    public static TheoryData<string> Rar5Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(Fixtures, "Rar5*.rar").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            var part = System.Text.RegularExpressions.Regex.Match(name, @"\.part(\d+)\.rar$");
            if (part.Success && part.Groups[1].Value.TrimStart('0') != "1") continue;
            data.Add(name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Rar5Fixtures))]
    public void Rar5_members_and_bytes_match_the_reference_reader(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Equal(reference.Error is null, ours.Error is null);
        if (reference.Error is not null)
        {
            Assert.IsType<InvalidDataException>(ours.Error);
            return;
        }
        Assert.Equal(reference.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted)),
            ours.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted)));
        Assert.Equal(reference.Contents, ours.Contents);
    }

    public static TheoryData<string> Rar4Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.EnumerateFiles(Fixtures).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!name.StartsWith("Rar", StringComparison.Ordinal) || name.StartsWith("Rar5", StringComparison.Ordinal)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.part0*([2-9]|\d\d+)\.rar$|\.r\d\d$|\.\d\d\d$")) continue;
            if (!name.EndsWith(".rar", StringComparison.Ordinal) && !name.EndsWith(".001", StringComparison.Ordinal)) continue;
            data.Add(name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Rar4Fixtures))]
    public void Rar4_listings_match_the_reference_reader(string fixture)
    {
        string path = Path.Combine(Fixtures, fixture);
        var reference = Listing(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Listing(() => OpenOurs(path));
        Assert.Equal(reference.Error is null, ours.Error is null);
        if (reference.Error is not null) return;
        Assert.Equal(reference.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted, m.Kind == MemberKind.Directory ? 0 : m.Size)),
            ours.Members.Select(m => (m.Path.Replace('\\', '/'), m.Kind == MemberKind.Directory, m.Encrypted, m.Kind == MemberKind.Directory ? 0 : m.Size)));
    }

    [Fact]
    public void Rar4_stored_members_match_the_reference_bytes()
    {
        string path = Path.Combine(Fixtures, "Rar.none.rar");
        var reference = Snapshot(() => SharpArchiveReader.Open(path, rar: true));
        var ours = Snapshot(() => OpenOurs(path));
        Assert.Null(ours.Error);
        Assert.NotEmpty(ours.Contents);
        Assert.Equal(reference.Contents, ours.Contents);
    }

    private static (List<MemberInfo> Members, Exception? Error) Listing(Func<IMemberReader> open)
    {
        try
        {
            using var reader = open();
            return (reader.List(_ => { }, CancellationToken.None).ToList(), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return ([], ex);
        }
    }

    [Theory]
    // Fixtures that must decode, with their content counts: a refusal on both sides must not pass silently.
    [InlineData("Rar5.rar", 3)]
    [InlineData("Rar5.none.rar", 3)]
    [InlineData("Rar5.solid.rar", 3)]
    [InlineData("Rar5.crc_blake2.rar", 3)]
    [InlineData("Rar5.multi.part01.rar", 3)]
    [InlineData("Rar5.comment.rar", 3)]
    public void Rar5_fixtures_decode_completely(string fixture, int files)
    {
        var ours = Snapshot(() => OpenOurs(Path.Combine(Fixtures, fixture)));
        Assert.Null(ours.Error);
        Assert.Equal(files, ours.Contents.Count);
    }

    [Fact]
    public void Rar5_blake2sp_member_hashes_verify()
    {
        var snapshot = Snapshot(() => OpenOurs(Path.Combine(Fixtures, "Rar5.crc_blake2.rar")));
        Assert.Null(snapshot.Error);
        Assert.NotEmpty(snapshot.Contents);
    }

    [Theory]
    // Independent values: Python's hashlib.blake2s with BLAKE2sp's tree parameters (fanout 8, depth 2, inner length 32,
    // leaf i at node offset i, the last leaf and the root as last nodes), input byte i = (7i + 3) mod 256.
    [InlineData(0, "dd0e891776933f43c7d032b08a917e25741f8aa9a12c12e1cac8801500f2ca4f")]
    [InlineData(1, "456da349adc8eaa1ee5fd8397d9c181c21a8e9173ad90f22613fd34e1d576d44")]
    [InlineData(63, "fe713f6ccd82f0c81ccd7a7d0c8ef83c631e2e394217859d4af5025202923578")]
    [InlineData(64, "e375bba69fb1c84c7f202f06e71090695f81786b7413da0cd0126e15a0f80181")]
    [InlineData(65, "11a6af420901450dd27b0babec0c484c4f7710330040136a17e79df6c54106ae")]
    [InlineData(511, "63a8cc6d628b88d471d4f81035b367f62b8a14dbf55bd9cca0d945dae8e226cf")]
    [InlineData(512, "201135254f94f0cf5307da6ae28daf819258fd08df8249684ffbbded6c23e0e5")]
    [InlineData(513, "4524c7cfa3a7cde180691ba1a3c0d4b9fc20ced214f27e65f5c76dbb0ff44c91")]
    [InlineData(1024, "c172db3b0b7e122c3e1a5071348ce3bf39dab1772d9d3c1744e722c1eb0aed84")]
    [InlineData(1025, "3479ff3bda494d6fbdbd024d2ac68f71b140deb159e119e7a83b45eb59840f76")]
    [InlineData(5000, "797108ac66f04cf7f19cb474888ebdc6d60b2e5d34097bee42b6993e96518839")]
    public void Blake2sp_matches_independent_values_in_any_chunking(int length, string expected)
    {
        byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 3)).ToArray();
        var whole = new Blake2sp();
        whole.Append(data);
        Assert.Equal(expected, Convert.ToHexStringLower(whole.Finish()));
        foreach (int chunk in new[] { 1, 7, 64, 100, 511, 512, 513 })
        {
            var pieces = new Blake2sp();
            for (int i = 0; i < data.Length; i += chunk) pieces.Append(data.AsSpan(i, Math.Min(chunk, data.Length - i)));
            Assert.Equal(expected, Convert.ToHexStringLower(pieces.Finish()));
        }
    }

    internal static RarReader OpenOurs(string path)
    {
        var volumes = new List<FileStream>();
        foreach (string volume in SharpArchiveReader.RarVolumes(path)) volumes.Add(ArchiveFormats.OpenShared(volume));
        return RarReader.Open(volumes, numberedVolumeGap: false);
    }

    private sealed record ArchiveSnapshot(List<MemberInfo> Members, List<(string Path, string Hash)> Contents, Exception? Error);

    private static ArchiveSnapshot Snapshot(Func<IMemberReader> open)
    {
        var members = new List<MemberInfo>();
        var contents = new List<(string, string)>();
        try
        {
            using var reader = open();
            members.AddRange(reader.List(_ => { }, CancellationToken.None));
            foreach (var member in members)
            {
                if (member.Kind != MemberKind.File || member.Encrypted) continue;
                using var content = reader.Open(member.Index, CancellationToken.None);
                contents.Add((member.Path.Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(content))));
            }
            return new ArchiveSnapshot(members, contents, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return new ArchiveSnapshot(members, contents, ex);
        }
    }
}
