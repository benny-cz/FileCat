using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class Rar5AssemblyTests
{
    private const string Password = "Public-RAR-Preservation-暗号-42829312";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static IEnumerable<object[]> Cases() => Enumerable.Range(0, 106).Select(index => new object[] { index });
    private sealed record Case(ArchiveWriteProfile Profile, ArchiveRar5Metadata Metadata, ArchiveRar5Features Features, Rar5Compression Method);
    private static Case Fixture(int index)
    {
        int mode = 0, method = 3, host = 1; uint attrs = 0x89a4; uint? basic = 1700000000;
        bool crc = true, blake = true; int cryptoFlags = 3;
        ArchiveRar5Times? times = new(31, new(1700000001, 999999999), new(0, 1), new(uint.MaxValue, 123456789));
        ArchiveRar5Owner? owner = new(15, [0xff, 0, 0x80, 1], [0xfe, 0, 0x81], ulong.MaxValue, 0);
        var features = new ArchiveRar5Features(4, 4);
        if (index < 18) { method = index / 3; mode = index % 3; }
        else if (index < 50)
        {
            int n = index - 18, flags = n % 16; host = n / 16; attrs = host == 0 ? 0x2227U : 0x18fa4U;
            owner = new((ulong)flags, (flags & 1) == 0 ? null : n % 3 == 0 ? [] : [0xff, 0, 0x80],
                (flags & 2) == 0 ? null : n % 4 == 0 ? [] : [0xfe, 0, 0x81], (flags & 4) == 0 ? null : ulong.MaxValue, (flags & 8) == 0 ? null : 0);
            times = null; basic = null; crc = false;
        }
        else if (index < 74)
        {
            int n = index - 50; ulong flags = (ulong)Enumerable.Range(0, 32).Where(value => (value & 16) == 0 || (value & 1) != 0).ElementAt(n);
            bool unix = (flags & 1) != 0, nano = (flags & 16) != 0;
            ArchiveTimestamp t = unix ? new(n % 2 == 0 ? 0 : uint.MaxValue, nano ? 999999999 : 0) : new(n % 2 == 0 ? -11644473599 : 253402300799, 123456700);
            times = new(flags, (flags & 2) != 0 ? t : null, (flags & 4) != 0 ? t : null, (flags & 8) != 0 ? t : null);
            owner = null; blake = false; basic = n % 2 == 0 ? 1700000000 : null;
        }
        else if (index < 90)
        { int n = index - 74; mode = 1; cryptoFlags = n % 4; crc = ((n / 4) & 1) != 0; blake = ((n / 4) & 2) != 0; times = null; owner = null; }
        else
        {
            int n = index - 90; mode = 2;
            features = new((byte)((n & 1) != 0 ? 4 : 0), (byte)((n & 2) != 0 ? 4 : 0), (byte)((n & 4) != 0 ? 4 : 0), true, (n & 8) != 0, 19);
        }
        if (mode == 2 && index < 90) features = new(4, 4, 4, true, true, 19);
        var encryption = mode == 0 ? ArchiveEncryptionOptions.None : new ArchiveEncryptionOptions((ArchiveEncryptionMode)mode);
        var profile = ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)method, encryption);
        // Extra-area presence also distinguishes an explicitly present empty area from its absence.
        ulong header = 6UL | (times is not null || owner is not null || blake || mode != 0 ? 1UL : 0UL);
        var metadata = new ArchiveRar5Metadata(ArchiveMemberType.File, header, attrs, host,
            ((ulong)method << 7) | (index is >= 18 and < 50 && method != 0 ? 9UL << 10 : 0), basic, crc, blake, times, owner,
            mode == 0 ? null : cryptoFlags, mode == 0 ? null : 19);
        return new(profile, metadata, features, (Rar5Compression)method);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Metadata_and_retained_packed_bytes_survive_creation_rename_and_reassembly(int index)
    {
        var f = Fixture(index); byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("RAR preservation binary \0 boundary.\n", 127)));
        byte[] encodedMetadata = f.Metadata.Encode(); Assert.Equal(encodedMetadata, ArchiveRar5Metadata.Decode(encodedMetadata).Encode());
        Assert.Equal(f.Features, ArchiveRar5Features.Decode(f.Features.Encode()));
        var entry = new Rar5AssemblyEntry("file-界.bin", f.Metadata, new(new(0, data.Length, Sha(data)), f.Method));
        using var original = new MemoryStream();
        using (var key = Key(f.Profile))
        {
            var written = Rar5ArchiveAssembler.Create(original, [entry], _ => new MemoryStream(data, false), _ => throw new InvalidOperationException(),
                f.Profile, f.Features, Ct, key);
            Assert.Equal(original.Length, written.PhysicalBytes); Assert.Equal(original.Length, original.Position);
            original.Position = 0; using var password = ReadKey(f.Profile); var catalog = StrictRar5Catalog.Read(original, Ct, password);
            var observed = Rar5SemanticCatalog.Assessment(catalog, f.Profile, Ct);
            Assert.Equal(written.Manifest.Digest, observed.Manifest.Digest); Assert.Equal(Sha(data), Assert.Single(catalog.Members).Sha256);
        }
        original.Position = 0; using var read = ReadKey(f.Profile); var source = StrictRar5Catalog.Read(original, Ct, read);
        var member = Assert.Single(source.Members); var physical = member.Physical;
        Assert.Equal(Sha(original.ToArray().AsSpan((int)physical.DataOffset, (int)physical.PackedLength)), member.PackedSha256);
        var retained = Rar5RetainedPayload.FromVerified(member, 0);
        using var replacement = new MemoryStream(); using var outputKey = Key(f.Profile);
        var result = Rar5ArchiveAssembler.Create(replacement, [new("renamed-界.bin", retained.Metadata, Retained: retained)],
            _ => throw new InvalidOperationException(), _ => new MemoryStream(original.ToArray().AsSpan((int)physical.DataOffset, (int)physical.PackedLength).ToArray(), false),
            f.Profile, f.Features, Ct, outputKey);
        replacement.Position = 0; using var freshPassword = ReadKey(f.Profile); var fresh = StrictRar5Catalog.Read(replacement, Ct, freshPassword);
        var freshMember = Assert.Single(fresh.Members); Assert.Equal(member.Sha256, freshMember.Sha256);
        Assert.Equal(member.PackedSha256, freshMember.PackedSha256); Assert.Equal("renamed-界.bin", freshMember.Physical.Name);
        Assert.Equal(encodedMetadata, Rar5SemanticCatalog.Metadata(freshMember).Encode());
        Assert.Equal(result.Manifest.Digest, Rar5SemanticCatalog.Assessment(fresh, f.Profile, Ct).Manifest.Digest);
        Export(index, f, original.ToArray(), replacement.ToArray(), data);
    }

    [Fact]
    public void Unchanged_duplicate_occurrences_survive_but_one_source_occurrence_cannot_be_inserted_twice()
    {
        byte[][] data = [[1, 2, 3], [4, 5, 6]]; var segments = new List<byte[]>(); byte[]? prefix = null, suffix = null;
        foreach (byte[] bytes in data)
        {
            using var stream = new MemoryStream();
            Rar5ArchiveWriter.Create(stream, [new("f", ArchiveMemberType.File, new(0, 0), 0x1a4, 0)],
                new Dictionary<int, Rar5InputDescriptor> { [0] = new(0, bytes.Length, Sha(bytes)) }, _ => new MemoryStream(bytes, false), Rar5Compression.Store, Ct);
            byte[] archive = stream.ToArray(); stream.Position = 0; var member = Assert.Single(StrictRar5Catalog.Read(stream, Ct).Members).Physical;
            prefix = archive[..(int)member.HeaderOffset]; suffix = archive[(int)(member.DataOffset + member.PackedLength)..];
            segments.Add(archive.AsSpan((int)member.HeaderOffset, (int)(member.DataOffset + member.PackedLength - member.HeaderOffset)).ToArray());
        }
        byte[] original = [.. prefix!, .. segments[0], .. segments[1], .. suffix!]; using var input = new MemoryStream(original, false);
        var catalog = StrictRar5Catalog.Read(input, Ct); Assert.Equal(new[] { 0, 1 }, catalog.Members.Select(member => member.Physical.Occurrence));
        var refs = catalog.Members.Select((member, index) => Rar5RetainedPayload.FromVerified(member, index)).ToArray();
        var entries = refs.Select(member => new Rar5AssemblyEntry("f", member.Metadata, Retained: member)).ToArray();
        var profile = ArchiveWriteProfile.ForRar5(ArchiveRar5Compression.Store); using var stage = new MemoryStream();
        var assembled = Rar5ArchiveAssembler.Create(stage, entries, _ => throw new InvalidOperationException(), Open,
            profile, Rar5SemanticCatalog.Features(catalog.Physical), Ct);
        stage.Position = 0; Assert.Equal(assembled.Manifest.Digest, Rar5SemanticCatalog.Assessment(StrictRar5Catalog.Read(stage, Ct), profile, Ct).Manifest.Digest);
        using var refused = new MemoryStream();
        Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveAssembler.Create(refused, [entries[0], entries[0]], _ => throw new InvalidOperationException(), Open,
            profile, Rar5SemanticCatalog.Features(catalog.Physical), Ct)); Assert.Equal(0, refused.Length);
        Stream Open(int id)
        { var member = catalog.Members[id].Physical; return new MemoryStream(original.AsSpan((int)member.DataOffset, (int)member.PackedLength).ToArray(), false); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Mixed_retained_captured_and_explicit_directory_records_match_the_complete_expected_manifest(int mode)
    {
        var f = Fixture(mode); byte[] first = [1, 2, 3], second = [4, 5, 6]; using var original = new MemoryStream();
        using (var key = Key(f.Profile)) Rar5ArchiveAssembler.Create(original, [new("old", f.Metadata, new(new(0, 3, Sha(first)), f.Method))],
            _ => new MemoryStream(first, false), _ => throw new InvalidOperationException(), f.Profile, f.Features, Ct, key);
        original.Position = 0; using var read = ReadKey(f.Profile); var old = Assert.Single(StrictRar5Catalog.Read(original, Ct, read).Members);
        var retained = Rar5RetainedPayload.FromVerified(old, 0);
        var directory = new ArchiveRar5Metadata(ArchiveMemberType.Directory, 3, 0x41ed, 1, 0, hasBlake2sp: true, times: new(0), owner: new(0));
        using var stage = new MemoryStream(); using var outputKey = Key(f.Profile);
        var result = Rar5ArchiveAssembler.Create(stage, [new("old", retained.Metadata, Retained: retained),
            new("new", f.Metadata, new(new(1, 3, Sha(second)), f.Method)), new("empty-dir", directory)],
            _ => new MemoryStream(second, false), _ => new MemoryStream(original.ToArray().AsSpan((int)old.Physical.DataOffset, (int)old.Physical.PackedLength).ToArray(), false),
            f.Profile, f.Features, Ct, outputKey);
        stage.Position = 0; using var verify = ReadKey(f.Profile); var actual = StrictRar5Catalog.Read(stage, Ct, verify);
        Assert.Equal(3, actual.Members.Count); Assert.Equal(result.Manifest.Digest, Rar5SemanticCatalog.Assessment(actual, f.Profile, Ct).Manifest.Digest);
        Assert.Equal(Sha(first), actual.Members[0].Sha256); Assert.Equal(Sha(second), actual.Members[1].Sha256);
        Assert.Equal(ArchiveMemberType.Directory, actual.Members[2].Physical.Type);
    }

    [Fact]
    public void Optional_native_owner_values_are_frozen_and_presence_is_not_normalized()
    {
        byte[] name = [255, 0, 128]; var owner = new ArchiveRar5Owner(5, name, userId: 0); name[0] = 0;
        var a = new ArchiveRar5Metadata(ArchiveMemberType.File, 3, 0, 0, 0, owner: owner);
        Assert.Equal(new byte[] { 255, 0, 128 }, owner.UserName.ToArray());
        var empty = new ArchiveRar5Metadata(ArchiveMemberType.File, 3, 0, 0, 0, owner: new(1, []));
        var absent = new ArchiveRar5Metadata(ArchiveMemberType.File, 3, 0, 0, 0);
        var emptyRecord = new ArchiveRar5Metadata(ArchiveMemberType.File, 3, 0, 0, 0, owner: new(0));
        Assert.NotEqual(Sha(a.Encode()), Sha(empty.Encode())); Assert.NotEqual(Sha(empty.Encode()), Sha(absent.Encode()));
        Assert.NotEqual(Sha(emptyRecord.Encode()), Sha(absent.Encode()));
    }

    [Fact]
    public void Truncated_extended_metadata_unknown_fields_and_oversized_native_names_are_controlled_refusals()
    {
        byte[] valid = Fixture(0).Metadata.Encode();
        for (int length = 0; length < valid.Length; length++) Assert.Throws<ArchiveWriteException>(() => ArchiveRar5Metadata.Decode(valid.AsSpan(0, length)));
        Assert.Throws<ArchiveWriteException>(() => ArchiveRar5Metadata.Decode([.. valid, 0]));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveRar5Owner(1)); Assert.Throws<ArchiveWriteException>(() => new ArchiveRar5Times(16));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveRar5Times(3, new(0, 1)));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveRar5Times(2, new(-11644473601, 0)));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => new ArchiveRar5Owner(1, new byte[ArchiveWritePolicy.MaxEntryMetadataBytes])).Reason);
        byte[] malformed = (byte[])valid.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(malformed.AsSpan(6), ulong.MaxValue);
        Assert.Throws<ArchiveWriteException>(() => ArchiveRar5Metadata.Decode(malformed));
    }

    [Fact]
    public void Mode_drops_ineffective_passwords_wrong_payload_hashes_and_namespace_collisions_never_complete()
    {
        var f = Fixture(0); byte[] data = [1, 2, 3]; var input = new Rar5CapturedPayload(new(0, data.Length, Sha(data)), f.Method);
        var entry = new Rar5AssemblyEntry("f", f.Metadata, input);
        using var stage = new MemoryStream(); using var wrong = Key(ArchiveWriteProfile.ForRar5(encryption: new(ArchiveEncryptionMode.Data)));
        Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveAssembler.Create(stage, [entry], _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
            ArchiveWriteProfile.ForRar5(encryption: new(ArchiveEncryptionMode.Data)), f.Features, Ct, wrong)); Assert.Equal(0, stage.Length);
        Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveAssembler.Create(stage, [entry, entry], _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), f.Profile, f.Features, Ct));
        Assert.Equal(0, stage.Length);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveAssembler.Create(stage, [entry], _ => new MemoryStream([4, 5, 6], false),
            _ => throw new InvalidOperationException(), f.Profile, f.Features, Ct)).Reason);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Retained_payload_hash_and_exact_length_are_checked_after_every_copy(bool longer)
    {
        byte[] data = [1, 2, 3]; var f = Fixture(0); using var source = new MemoryStream();
        Rar5ArchiveAssembler.Create(source, [new("f", f.Metadata, new(new(0, 3, Sha(data)), f.Method))], _ => new MemoryStream(data, false),
            _ => throw new InvalidOperationException(), f.Profile, f.Features, Ct);
        source.Position = 0; var member = Assert.Single(StrictRar5Catalog.Read(source, Ct).Members); var retained = Rar5RetainedPayload.FromVerified(member, 0);
        byte[] packed = source.ToArray().AsSpan((int)member.Physical.DataOffset, (int)member.Physical.PackedLength).ToArray();
        if (longer) packed = [.. packed, 0]; else packed[0] ^= 1;
        using var stage = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveAssembler.Create(stage, [new("f", retained.Metadata, Retained: retained)],
            _ => throw new InvalidOperationException(), _ => new MemoryStream(packed, false), f.Profile, f.Features, Ct)).Reason);
    }

    private static Rar5EncryptionSession? Key(ArchiveWriteProfile profile) => ArchiveWriterProfileAdapter.Rar5Encryption(profile,
        profile.Encryption.Mode == ArchiveEncryptionMode.None ? null : new(Password), new(), Ct);
    private static Rar5ReadPasswordSession? ReadKey(ArchiveWriteProfile profile) => profile.Encryption.Mode == ArchiveEncryptionMode.None ? null : new(Password, new(), Ct);
    private static void Export(int index, Case f, byte[] source, byte[] rebuilt, byte[] data)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RAR_PRESERVATION_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, index.ToString("D3")); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "source.rar"), source); File.WriteAllBytes(Path.Combine(directory, "rebuilt.rar"), rebuilt);
        File.WriteAllBytes(Path.Combine(directory, "metadata.bin"), f.Metadata.Encode()); File.WriteAllBytes(Path.Combine(directory, "features.bin"), f.Features.Encode());
        File.WriteAllText(Path.Combine(directory, "expected.json"), JsonSerializer.Serialize(new { synthetic_public_vector = true, password = Password,
            source_name = "file-界.bin", rebuilt_name = "renamed-界.bin", length = data.Length, sha256 = Sha(data), mode = (int)f.Profile.Encryption.Mode }));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "runtime-rar-preservation.json"), JsonSerializer.Serialize(new
        { runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() }));
    }
}
