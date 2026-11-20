using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

/// <summary>The schema-1 canonical encoding shared by in-memory and streamed manifests.</summary>
internal sealed class ArchiveManifestDigestBuilder : IDisposable
{
    private readonly IncrementalHash[] hashes = [IncrementalHash.CreateHash(HashAlgorithmName.SHA256),
        IncrementalHash.CreateHash(HashAlgorithmName.SHA256), IncrementalHash.CreateHash(HashAlgorithmName.SHA256)];
    private readonly int expectedMembers;
    private int members;
    private bool finished;

    public ArchiveManifestDigestBuilder(ArchiveWriteProfile profile, int memberCount, ReadOnlySpan<byte> features)
    {
        expectedMembers = memberCount;
        Span<byte> encoded = stackalloc byte[ArchiveWriteProfileEncoding.MaximumBytes];
        int length = ArchiveWriteProfileEncoding.Write(profile, encoded);
        for (int domain = 0; domain < hashes.Length; domain++)
        {
            var hash = hashes[domain];
            hash.AppendData("FCAM"u8);
            Integer(hash, ArchiveWriteManifest.Schema); Integer(hash, domain);
            hash.AppendData(encoded[..length]);
            Integer(hash, memberCount);
            if (domain != 1) Bytes(hash, features);
        }
    }

    public void Append(ArchiveManifestMember member)
    {
        if (finished || members >= expectedMembers) throw ArchiveManifestMember.Invalid();
        byte[] name = ArchiveWritePolicy.Utf8.GetBytes(member.Name);
        byte[] digest = Convert.FromHexString(member.Sha256);
        for (int domain = 0; domain < hashes.Length; domain++)
        {
            var hash = hashes[domain];
            Bytes(hash, name); Integer(hash, (int)member.Type); Integer(hash, member.Occurrence);
            if (domain != 2) { Long(hash, member.Length); Bytes(hash, digest); }
            if (domain != 1) Bytes(hash, member.Metadata);
        }
        members++;
    }

    public (string Full, string Content, string Metadata) Complete()
    {
        if (finished || members != expectedMembers) throw ArchiveManifestMember.Invalid();
        finished = true;
        return (Convert.ToHexString(hashes[0].GetHashAndReset()), Convert.ToHexString(hashes[1].GetHashAndReset()),
            Convert.ToHexString(hashes[2].GetHashAndReset()));
    }

    private static void Integer(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); hash.AppendData(bytes);
    }
    private static void Long(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, value); hash.AppendData(bytes);
    }
    private static void Bytes(IncrementalHash hash, ReadOnlySpan<byte> bytes) { Integer(hash, bytes.Length); hash.AppendData(bytes); }
    public void Dispose() { foreach (var hash in hashes) hash.Dispose(); }
}
