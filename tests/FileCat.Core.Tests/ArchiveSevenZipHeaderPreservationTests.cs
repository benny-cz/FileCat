using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Core.Tests;

public sealed class ArchiveSevenZipHeaderPreservationTests
{
    private const string Password = "Public-7z-worker-暗号-42829324";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public static IEnumerable<object[]> Headers()
    {
        for (int codec = 0; codec < 3; codec++)
        {
            yield return [codec, false, false, 0, 19];
            yield return [codec, true, false, 0, 19];
            yield return [codec, true, true, 4, 20];
        }
        yield return [3, true, false, 0, 19]; // AES-only encoded header.
    }

    [Theory] [MemberData(nameof(Headers))]
    public void Raw_optional_fields_unsigned_times_header_properties_and_reordered_coders_survive_updates(
        int codec, bool encrypted, bool reordered, int saltLength, int power)
    {
        byte[] payload = "retained exact pack"u8.ToArray(), replacement = "new content"u8.ToArray();
        byte[] archive = Fixture(payload, codec, encrypted, reordered, saltLength, power);
        var profile = ArchiveWriteProfile.ForSevenZip(encryption: encrypted ? new(ArchiveEncryptionMode.DataAndNames) : null);
        var before = Read(archive); var assessment = SevenZipSemanticCatalog.Assessment(before, profile, Ct);
        var edits = new ArchiveTransformEdit[]
        {
            new(ArchiveChangeKind.Rename, new(assessment.CatalogRevision, "file", ArchiveMemberType.File, 0), "renamed"),
            new(ArchiveChangeKind.Replace, new(assessment.CatalogRevision, "empty", ArchiveMemberType.File, 0), inputId: 7),
        };
        var inputs = new ArchiveTransformInput[] { new(7, replacement.Length, Sha(replacement), new ArchiveTimestamp(1700000000, 0)) };
        var plan = ArchiveSevenZipTransformPlan.Create(profile, assessment, inputs, edits, [], Ct);
        using var source = new MemoryStream(archive, false); using var output = new MemoryStream();
        SevenZipRewriteEngine.Rebuild(source, assessment.SourceSha256, profile, output, edits, [], inputs,
            _ => new MemoryStream(replacement, false), Ct, encrypted ? Password : default);
        var after = Read(output.ToArray()); var observed = SevenZipSemanticCatalog.Assessment(after, profile, Ct);
        Assert.Equal(plan.Expected.Digest, observed.Manifest.Digest);
        Assert.Equal(assessment.Manifest.Features, observed.Manifest.Features);
        Assert.Equal(before.Members[0].Packed[0].Sha256, after.Members[0].Packed[0].Sha256);
        Assert.Equal(new[] { "renamed", "empty", "directory" }, after.Members.Select(m => m.Physical.Name));
        for (int i = 0; i < 3; i++)
        {
            var old = ArchiveSevenZipMetadata.Decode(assessment.Manifest.Members[i].Metadata);
            var current = ArchiveSevenZipMetadata.Decode(observed.Manifest.Members[i].Metadata);
            Assert.Equal(old.Attributes, current.Attributes); Assert.Equal(old.CreatedFileTime, current.CreatedFileTime);
            Assert.Equal(old.AccessedFileTime, current.AccessedFileTime); Assert.Equal(old.ModifiedFileTime, current.ModifiedFileTime);
        }
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_WORKER_EXPORT_ROOT") is { } export)
        {
            string directory = Path.Combine(export, $"preserved-header-{codec}-{encrypted}-{reordered}-{saltLength}-{power}");
            Write("source", archive, assessment.Manifest); Write("rebuilt", output.ToArray(), observed.Manifest);
            void Write(string leaf, byte[] bytes, ArchiveWriteManifest manifest)
            {
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, leaf + ".7z"), bytes);
                File.WriteAllText(Path.Combine(directory, leaf + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                {
                    synthetic_public_vector = true, password = Password,
                    features = Convert.ToHexString(manifest.Features),
                    members = manifest.Members.Select(m => new { name = m.Name, type = (int)m.Type, length = m.Length, sha256 = m.Sha256, metadata = Convert.ToHexString(m.Metadata) }),
                }));
            }
        }
        SevenZipLogicalCatalog Read(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false); using var password = encrypted ? new SevenZipReadPasswordSession(Password, new(), Ct) : null;
            return StrictSevenZipCatalog.Read(stream, Ct, password);
        }
    }

    private static byte[] Fixture(byte[] payload, int codec, bool encrypted, bool reordered, int saltLength, int power)
    {
        // Independently laid-out records exercise presence bits that the ordinary creation writer does not emit.
        using var raw = new MemoryStream(); raw.WriteByte(1); raw.WriteByte(4);
        Streams(raw, 0, payload, payload.Length, ArchiveCrc32.Compute(payload), [new([0], [])], [], [payload.Length], false, false);
        raw.Write([5, 3, 14, 1, 0x60, 15, 1, 0x80, 16, 1, 0]);
        byte[] names = Encoding.Unicode.GetBytes("file\0empty\0directory\0"); raw.WriteByte(17); Number(raw, (ulong)names.Length + 1); raw.WriteByte(0); raw.Write(names);
        raw.Write([18, 11, 0, 0x80, 0]); U64(raw, ulong.MaxValue);
        raw.Write([19, 11, 0, 0x20, 0]); U64(raw, 0);
        raw.Write([20, 19, 0, 0xc0, 0]); U64(raw, 1); U64(raw, ulong.MaxValue);
        raw.Write([21, 11, 0, 0xa0, 0]); U32(raw, 0x81a48020); U32(raw, 0x41ed8010);
        raw.Write([25, 5, 0, 0, 0, 0, 0, 0, 0]); byte[] header = raw.ToArray();
        using var compressed = new MemoryStream(); SevenZipCoder? compression = null;
        if (codec == 1)
        {
            using (var encoder = LzmaStream.Create(new LzmaEncoderProperties(true, 1 << 20, 32, 1, 2, 1), false, compressed)) encoder.Write(header);
            compression = new([3, 1, 1], [56, 0, 0, 16, 0]);
        }
        else if (codec == 2)
        { using (var encoder = new Lzma2EncoderStream(compressed, 1 << 20, 32)) encoder.Write(header); compression = new([0x21], [16]); }
        else { compressed.Write(header); if (codec == 0) compression = new([0], []); }
        byte[] packed = compressed.ToArray(); var coders = new List<SevenZipCoder>(); var sizes = new List<long>();
        if (encrypted)
        {
            using var session = new SevenZipEncryptionSession(Password, true, new(), Ct, headerCountPower: power, headerSaltLength: saltLength);
            session.ClaimArchive(); using var ciphertext = new MemoryStream(); var cipher = session.CreateCipher(ciphertext, true, ArchiveWritePolicy.MaxMetadataBytes, Ct);
            using (cipher.Cipher) { cipher.Cipher.Write(packed); cipher.Cipher.Finish(); }
            coders.Add(new([6, 0xf1, 7, 1], cipher.Properties)); sizes.Add(packed.Length); packed = ciphertext.ToArray();
        }
        if (compression is not null) { coders.Add(compression); sizes.Add(header.Length); }
        var bindings = coders.Count == 2 ? new[] { new SevenZipBinding(1, 0) } : [];
        if (reordered) { coders.Reverse(); sizes.Reverse(); bindings = [new(0, 1)]; }
        using var outer = new MemoryStream(); outer.WriteByte(23);
        Streams(outer, payload.Length, packed, header.Length, ArchiveCrc32.Compute(header), coders.ToArray(), bindings, sizes.ToArray(), true, true);
        byte[] next = outer.ToArray(); using var result = new MemoryStream(); result.Write(new byte[32]); result.Write(payload); result.Write(packed); result.Write(next);
        byte[] bytes = result.ToArray(); ((ReadOnlySpan<byte>)[0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c]).CopyTo(bytes); bytes[7] = 3;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), (ulong)(payload.Length + packed.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(20), (ulong)next.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), ArchiveCrc32.Compute(next));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), ArchiveCrc32.Compute(bytes.AsSpan(12, 20))); return bytes;
    }
    private static void Streams(Stream stream, long position, byte[] packed, long length, uint crc, SevenZipCoder[] coders,
        SevenZipBinding[] bindings, long[] sizes, bool packCrc, bool folderCrc)
    {
        stream.WriteByte(6); Number(stream, (ulong)position); stream.Write([1, 9]); Number(stream, (ulong)packed.Length);
        if (packCrc) { stream.Write([10, 1]); U32(stream, ArchiveCrc32.Compute(packed)); } stream.Write([0, 7, 11, 1, 0]);
        Number(stream, (ulong)coders.Length);
        foreach (var coder in coders)
        {
            stream.WriteByte((byte)(coder.MethodId.Length | (coder.Properties.Length == 0 ? 0 : 32))); stream.Write(coder.MethodId);
            if (coder.Properties.Length != 0) { Number(stream, (ulong)coder.Properties.Length); stream.Write(coder.Properties); }
        }
        foreach (var binding in bindings) { Number(stream, (ulong)binding.Input); Number(stream, (ulong)binding.Output); }
        stream.WriteByte(12); foreach (long size in sizes) Number(stream, (ulong)size);
        if (folderCrc) { stream.Write([10, 1]); U32(stream, crc); } stream.Write([0, 8, 13, 1, 9, 10, 1]);
        if (!folderCrc) U32(stream, crc); stream.Write([0, 0]);
        _ = length;
    }
    private static void U32(Stream stream, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes); }
    private static void U64(Stream stream, ulong value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); stream.Write(bytes); }
    private static void Number(Stream stream, ulong value)
    {
        int count = 0; while (count < 8 && value >= 1UL << (7 * (count + 1))) count++;
        byte prefix = count == 8 ? (byte)255 : (byte)((255 << (8 - count)) | (byte)(value >> (8 * count)));
        stream.WriteByte(prefix); for (int i = 0; i < count; i++) stream.WriteByte((byte)(value >> (8 * i)));
    }
}
