using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchivePasswordWritingTests
{
    // Deliberately public synthetic archive vector. Never a user/job secret and never a VM credential.
    private const string PublicPassword = "FileCat-Public-Archive-Vector-暗号-42829303";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ArchiveTimestamp Modified = new(1700000000, 123456700);

    public static IEnumerable<object[]> Profiles()
    {
        for (int method = 0; method < 6; method++) foreach (bool hide in new[] { false, true }) yield return [0, method, hide, false];
        for (int codec = 0; codec < 7; codec++) foreach (bool hide in new[] { false, true }) foreach (bool compressed in new[] { false, true }) yield return [1, codec, hide, compressed];
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Password_profiles_reopen_their_own_output_and_export_public_independent_vectors(int format, int method, bool hide, bool compressed)
    {
        byte[] noise = new byte[131089]; new Random(42829303).NextBytes(noise);
        byte[][] data = [noise, Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Public archive encryption boundary.\n", 1024))), [], [0x65], noise[..65537]];
        string[] names = ["hidden-界-noise.bin", "text.txt", "empty.bin", "empty-dir", "nested", "nested/é.bin", "boundary.bin"];
        int?[] ids = [0, 1, 2, null, null, 3, 4];
        using var stage = new MemoryStream();
        if (format == 0)
        {
            var profile = ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)method,
                new(hide ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
            using var secret = ArchiveWriterProfileAdapter.Rar5Encryption(profile, new(PublicPassword), new(), Ct);
            var entries = names.Select((name, i) => new Rar5WriteEntry(name, ids[i] is null ? ArchiveMemberType.Directory : ArchiveMemberType.File,
                Modified, ids[i] is null ? 0x1ed : 0x1a4, ids[i])).ToArray();
            Rar5ArchiveWriter.Create(stage, entries, data.Select((bytes, id) => new Rar5InputDescriptor(id, bytes.Length, Digest(bytes))).ToDictionary(d => d.Id),
                id => new MemoryStream(data[id], false), ArchiveWriterProfileAdapter.Rar5(profile), Ct, secret);
        }
        else
        {
            var profile = ArchiveWriteProfile.ForSevenZip(new((ArchiveSevenZipCodec)method,
                headerCompression: compressed ? ArchiveSevenZipHeaderCompression.Lzma2 : ArchiveSevenZipHeaderCompression.Raw),
                encryption: new(hide ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
            using var secret = ArchiveWriterProfileAdapter.SevenZipEncryption(profile, new(PublicPassword), new(), Ct);
            var entries = names.Select((name, i) => new SevenZipWriteEntry(name, ids[i] is null ? ArchiveMemberType.Directory : ArchiveMemberType.File,
                Modified, ids[i] is null ? 0x1ed : 0x1a4, ids[i])).ToArray();
            SevenZipArchiveWriter.Create(stage, entries, data.Select((bytes, id) => new SevenZipInputDescriptor(id, bytes.Length, Digest(bytes))).ToDictionary(d => d.Id),
                id => new MemoryStream(data[id], false), ArchiveWriterProfileAdapter.SevenZip(profile), Ct, ArchiveWriterProfileAdapter.SevenZipHeader(profile), secret);
        }
        byte[] archive = stage.ToArray(); Assert.Equal(stage.Length, stage.Position);
        if (hide) Assert.Equal(-1, archive.AsSpan().IndexOf(Encoding.UTF8.GetBytes(names[0])));
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, format == 0 ? "protected.rar" : "protected.7z"); File.WriteAllBytes(path, archive);
        using var rarSecret = format == 0 ? new Rar5ReadPasswordSession(PublicPassword, new(), Ct) : null;
        using var sevenSecret = format == 1 ? new SevenZipReadPasswordSession(PublicPassword, new(), Ct) : null;
        using IMemberReader reader = format == 0 ? RarReader.OpenWithPassword(path, rarSecret!) : SharpArchiveReader.OpenWithPassword(path, sevenSecret!);
        var members = reader.List(message => Assert.Fail(message), Ct).ToArray(); Assert.Equal(names.Length, members.Length);
        for (int i = 0; i < names.Length; i++)
        {
            var member = members[i]; Assert.Equal(names[i], member.Path.TrimEnd('/'));
            Assert.Equal(Modified.ToDateTimeOffset().UtcDateTime, member.ModifiedUtc);
            if (ids[i] is not { } id) { Assert.Equal(MemberKind.Directory, member.Kind); continue; }
            Assert.Equal(MemberKind.File, member.Kind); Assert.Equal(data[id].Length, member.Size);
            using var decoded = reader.Open(member.Index, Ct); Assert.Equal(Digest(data[id]), Convert.ToHexString(SHA256.HashData(decoded)));
        }
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_PASSWORD_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, $"{(format == 0 ? "rar" : "7z")}-{method}-{hide}-{compressed}"); Directory.CreateDirectory(folder);
            using (var output = new FileStream(Path.Combine(folder, format == 0 ? "members.rar" : "members.7z"), FileMode.CreateNew)) stage.WriteTo(output);
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(names.Select((name, i) => new
            {
                name, directory = ids[i] is null, mode = ids[i] is null ? 0x1ed : 0x1a4, length = ids[i] is { } id ? data[id].Length : 0,
                sha256 = Digest(ids[i] is { } input ? data[input] : []), mtime_seconds = Modified.Seconds, mtime_nanos = Modified.Nanoseconds,
            })));
            File.WriteAllText(Path.Combine(folder, "public-vector.json"), JsonSerializer.Serialize(new { synthetic_public_vector = true,
                public_synthetic_password = PublicPassword, format, method, hidden_names = hide, compressed_header = compressed }));
        }
    }

    [Theory]
    [MemberData(nameof(SevenZipFilterEncodingTests.Filters), MemberType = typeof(SevenZipFilterEncodingTests))]
    public void Encrypted_filter_graphs_preserve_exact_content_and_export_public_vectors(int filterId, int distance)
    {
        byte[] bytes = new byte[131089]; new Random(42829305).NextBytes(bytes);
        using var stage = new MemoryStream();
        var profile = ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Lzma2, (ArchiveSevenZipFilter)filterId, deltaDistance: distance),
            encryption: new(ArchiveEncryptionMode.DataAndNames));
        using var secret = ArchiveWriterProfileAdapter.SevenZipEncryption(profile, new(PublicPassword), new(), Ct);
        var entry = new SevenZipWriteEntry("private-filter-界.bin", ArchiveMemberType.File, Modified, 0x1a4, 0);
        SevenZipArchiveWriter.Create(stage, [entry], new Dictionary<int, SevenZipInputDescriptor> { [0] = new(0, bytes.Length, Digest(bytes)) },
            _ => new MemoryStream(bytes, false), ArchiveWriterProfileAdapter.SevenZip(profile), Ct, ArchiveWriterProfileAdapter.SevenZipHeader(profile), secret);
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "filter.7z"); File.WriteAllBytes(path, stage.ToArray());
        using var readSecret = new SevenZipReadPasswordSession(PublicPassword, new(), Ct);
        using var reader = SharpArchiveReader.OpenWithPassword(path, readSecret);
        var member = Assert.Single(reader.List(message => Assert.Fail(message), Ct)); using var decoded = reader.Open(member.Index, Ct);
        Assert.Equal(Digest(bytes), Convert.ToHexString(SHA256.HashData(decoded)));
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_PASSWORD_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, $"7z-filter-{filterId}-{distance}"); Directory.CreateDirectory(folder);
            using (var output = new FileStream(Path.Combine(folder, "members.7z"), FileMode.CreateNew)) stage.WriteTo(output);
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(new[] { new { name = entry.Name, directory = false, mode = entry.Mode,
                length = bytes.Length, sha256 = Digest(bytes), mtime_seconds = Modified.Seconds, mtime_nanos = Modified.Nanoseconds } }));
            File.WriteAllText(Path.Combine(folder, "public-vector.json"), JsonSerializer.Serialize(new { synthetic_public_vector = true,
                public_synthetic_password = PublicPassword, format = 1, method = 2, hidden_names = true, compressed_header = true, filter_id = filterId }));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Hidden_headers_protect_empty_archives_and_reject_a_wrong_password(int format)
    {
        using var stage = new MemoryStream();
        if (format == 0)
        {
            var profile = ArchiveWriteProfile.ForRar5(ArchiveRar5Compression.Store, new(ArchiveEncryptionMode.DataAndNames));
            using var secret = ArchiveWriterProfileAdapter.Rar5Encryption(profile, new(PublicPassword), new(), Ct);
            Rar5ArchiveWriter.Create(stage, [], new Dictionary<int, Rar5InputDescriptor>(), _ => throw new InvalidOperationException(), ArchiveWriterProfileAdapter.Rar5(profile), Ct, secret);
        }
        else
        {
            var profile = ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Copy), encryption: new(ArchiveEncryptionMode.DataAndNames));
            using var secret = ArchiveWriterProfileAdapter.SevenZipEncryption(profile, new(PublicPassword), new(), Ct);
            SevenZipArchiveWriter.Create(stage, [], new Dictionary<int, SevenZipInputDescriptor>(), _ => throw new InvalidOperationException(), ArchiveWriterProfileAdapter.SevenZip(profile), Ct, ArchiveWriterProfileAdapter.SevenZipHeader(profile), secret);
        }
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, format == 0 ? "empty.rar" : "empty.7z"); File.WriteAllBytes(path, stage.ToArray());
        Assert.Throws<InvalidDataException>(() =>
        {
            using IMemberReader reader = format == 0 ? RarReader.Open(path) : SharpArchiveReader.Open(path); reader.List(_ => { }, Ct).ToArray();
        });
        using var wrongRar = format == 0 ? new Rar5ReadPasswordSession("PUBLIC-WRONG-VECTOR", new(), Ct) : null;
        using var wrongSeven = format == 1 ? new SevenZipReadPasswordSession("PUBLIC-WRONG-VECTOR", new(), Ct) : null;
        Assert.ThrowsAny<InvalidDataException>(() =>
        {
            using IMemberReader reader = format == 0 ? RarReader.OpenWithPassword(path, wrongRar!) : SharpArchiveReader.OpenWithPassword(path, wrongSeven!);
            reader.List(_ => { }, Ct).ToArray();
        });
        using var rar = format == 0 ? new Rar5ReadPasswordSession(PublicPassword, new(), Ct) : null;
        using var seven = format == 1 ? new SevenZipReadPasswordSession(PublicPassword, new(), Ct) : null;
        using IMemberReader correct = format == 0 ? RarReader.OpenWithPassword(path, rar!) : SharpArchiveReader.OpenWithPassword(path, seven!);
        Assert.Empty(correct.List(message => Assert.Fail(message), Ct));
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_PASSWORD_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, format == 0 ? "rar-empty-hidden" : "7z-empty-hidden"); Directory.CreateDirectory(folder);
            using (var output = new FileStream(Path.Combine(folder, format == 0 ? "members.rar" : "members.7z"), FileMode.CreateNew)) stage.WriteTo(output);
            File.WriteAllText(Path.Combine(folder, "members.json"), "[]");
            File.WriteAllText(Path.Combine(folder, "public-vector.json"), JsonSerializer.Serialize(new { synthetic_public_vector = true, public_synthetic_password = PublicPassword, format, method = 0, hidden_names = true, compressed_header = format == 1 }));
        }
    }

    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
