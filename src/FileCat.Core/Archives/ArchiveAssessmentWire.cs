using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Archives;

public sealed record ArchiveAssessmentRequest(ArchiveWriteProfile Profile, long SourceLength, string SourceSha256);
public sealed record ArchiveAssessment(ArchiveWriteManifest Manifest, string CatalogRevision, long SourceLength, string SourceSha256);
public sealed record ArchiveCatalogEnvelope(ArchiveWriteProfile Profile, int Members, long LogicalBytes, string CatalogRevision,
    string SourceSha256, string ManifestDigest, string ContentDigest, string MetadataDigest, byte[] Features);

/// <summary>Exact bounded schema for assessment. It carries numeric stream grants and semantic member data, never paths.</summary>
public static class ArchiveAssessmentWire
{
    public static ReadOnlySpan<byte> Hello => [1, 0, 1, 0, 0, 0, 0, 0]; // protocol 1, record schema 1, reserved 0

    public static void Validate(ArchiveAssessmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArchiveWritePolicy.ValidateDigest(request.SourceSha256);
        if (request.SourceLength is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The assessed source length is invalid.");
    }

    public static byte[] Request(ArchiveAssessmentRequest request)
    {
        Validate(request);
        return Encode(writer => { writer.Write(ArchiveWriteManifest.Schema); Profile(writer, request.Profile);
            writer.Write(request.SourceLength); Digest(writer, request.SourceSha256); });
    }

    public static ArchiveAssessmentRequest ReadRequest(ReadOnlySpan<byte> data)
    {
        try
        {
            var reader = new Packet(data); reader.Schema();
            var profile = reader.Profile(); long length = reader.Long(); string digest = reader.Digest(); reader.End();
            var request = new ArchiveAssessmentRequest(profile, length, digest); Validate(request); return request;
        }
        catch (ArchiveWriteException) { throw Fault(); }
    }

    public static byte[] Catalog(ArchiveAssessment result)
    {
        var manifest = result.Manifest;
        ArchiveWritePolicy.ValidateDigest(result.CatalogRevision);
        ArchiveWritePolicy.ValidateDigest(result.SourceSha256);
        return Encode(writer =>
        {
            writer.Write(ArchiveWriteManifest.Schema); Profile(writer, manifest.Profile);
            writer.Write(manifest.Members.Count); writer.Write(manifest.LogicalBytes);
            Digest(writer, result.CatalogRevision); Digest(writer, result.SourceSha256);
            Digest(writer, manifest.Digest); Digest(writer, manifest.ContentDigest); Digest(writer, manifest.MetadataDigest);
            writer.Write(manifest.Features.Length); writer.Write(manifest.Features);
        });
    }

    public static ArchiveCatalogEnvelope ReadCatalog(ReadOnlySpan<byte> data, ArchiveAssessmentRequest expected, ArchiveWriteBudget budget)
    {
        try
        {
            var reader = new Packet(data); reader.Schema();
            var profile = reader.Profile();
            int count = reader.Integer(); long logical = reader.Long();
            if (profile != expected.Profile || count is < 0 or > ArchiveWritePolicy.MaxMembers ||
                logical is < 0 or > ArchiveWritePolicy.MaxLogicalBytes) throw Fault();
            string revision = reader.Digest(), source = reader.Digest(), manifest = reader.Digest(), content = reader.Digest(), metadata = reader.Digest();
            if (source != expected.SourceSha256 || profile.Format == ArchiveWriteFormat.Tar && revision != source) throw Fault();
            var features = reader.Blob(ArchiveWritePolicy.MaxDataFrameBytes);
            budget.AddMetadata(features.Length, features.Length);
            byte[] frozen = features.ToArray(); reader.End();
            return new(profile, count, logical, revision, source, manifest, content, metadata, frozen);
        }
        catch (ArchiveWriteException ex) when (ex.Reason != ArchiveWriteReason.BudgetExceeded) { throw Fault(); }
    }

    public static byte[] Member(ArchiveManifestMember member)
    {
        int nameBytes = ArchiveWritePolicy.Utf8.GetByteCount(member.Name);
        int length = checked(2 + 4 + nameBytes + 4 + 4 + 8 + 32 + 4 + member.Metadata.Length);
        if (length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw ArchiveWriteBudget.Limit();
        return Encode(writer =>
        {
            writer.Write(ArchiveWriteManifest.Schema); writer.Write(nameBytes); writer.Write(ArchiveWritePolicy.Utf8.GetBytes(member.Name));
            writer.Write((int)member.Type); writer.Write(member.Occurrence); writer.Write(member.Length); Digest(writer, member.Sha256);
            writer.Write(member.Metadata.Length); writer.Write(member.Metadata);
        }, length);
    }

    public static ArchiveManifestMember ReadMember(ReadOnlySpan<byte> data, ArchiveWriteBudget budget)
    {
        try
        {
            if (data.Length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw Fault();
            var reader = new Packet(data); reader.Schema();
            var nameBytes = reader.Blob(ArchiveWritePolicy.MaxNameBytes);
            int chars;
            try { chars = ArchiveWritePolicy.Utf8.GetCharCount(nameBytes); }
            catch (DecoderFallbackException) { throw Fault(); }
            if (chars is <= 0 or > ArchiveWritePolicy.MaxNameChars) throw Fault();
            var type = (ArchiveMemberType)reader.Integer(); int occurrence = reader.Integer();
            long length = reader.Long(); string digest = reader.Digest();
            if (!Enum.IsDefined(type) || occurrence is < 0 or >= ArchiveWritePolicy.MaxMembers ||
                length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes || type == ArchiveMemberType.Directory && length != 0) throw Fault();
            var metadata = reader.Blob(ArchiveWritePolicy.MaxEntryMetadataBytes); reader.End();
            ArchiveWriteManifest.Reserve(budget, chars, nameBytes.Length, metadata.Length, length);
            string name = ArchiveWritePolicy.Utf8.GetString(nameBytes);
            return new(name, type, occurrence, length, digest, metadata);
        }
        catch (ArchiveWriteException ex) when (ex.Reason != ArchiveWriteReason.BudgetExceeded) { throw Fault(); }
    }

    public static void VerifyComplete(ArchiveCatalogEnvelope envelope, ArchiveWriteManifest observed)
    {
        if (envelope.Members != observed.Members.Count || envelope.LogicalBytes != observed.LogicalBytes ||
            envelope.ManifestDigest != observed.Digest || envelope.ContentDigest != observed.ContentDigest ||
            envelope.MetadataDigest != observed.MetadataDigest) throw Fault();
    }

    private static byte[] Encode(Action<BinaryWriter> write, int capacity = 256)
    {
        using var stream = new MemoryStream(capacity);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) write(writer);
        if (stream.Length > ArchiveWritePolicy.MaxEntryMetadataBytes) throw ArchiveWriteBudget.Limit();
        return stream.ToArray();
    }
    private static void Profile(BinaryWriter writer, ArchiveWriteProfile profile)
    { writer.Write((int)profile.Format); writer.Write(profile.Version); writer.Write(profile.CompressionLevel); writer.Write(profile.DictionaryBytes); }
    private static void Digest(BinaryWriter writer, string digest)
    { ArchiveWritePolicy.ValidateDigest(digest); writer.Write(Convert.FromHexString(digest)); }
    internal static ArchiveWriteException Fault() => new(ArchiveWriteReason.ProtocolViolation, "The archive worker's assessment contract is invalid.");

    private ref struct Packet(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;
        public ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _data.Length) throw Fault();
            var result = _data[..count]; _data = _data[count..]; return result;
        }
        public int Integer() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public long Long() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public void Schema() { if (BinaryPrimitives.ReadUInt16LittleEndian(Take(2)) != ArchiveWriteManifest.Schema) throw Fault(); }
        public string Digest() => Convert.ToHexString(Take(32));
        public ArchiveWriteProfile Profile() => new((ArchiveWriteFormat)Integer(), Integer(), Integer(), Integer());
        public ReadOnlySpan<byte> Blob(int maximum)
        { int length = Integer(); if (length < 0 || length > maximum) throw Fault(); return Take(length); }
        public void End() { if (_data.Length != 0) throw Fault(); }
    }
}
