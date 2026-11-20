using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

public enum ArchiveManifestPurpose : byte { AssessedSource = 1, ExpectedOutput = 2, VerifiedOutput = 3 }

/// <summary>A checksum of encoded evidence, not a file identity, publication receipt or cleanup authorization.</summary>
public sealed record ArchiveManifestSidecarInfo
{
    public long Length { get; }
    public string Sha256 { get; }
    public ArchiveManifestSidecarInfo(long length, string sha256)
    {
        if (length < ArchiveManifestSidecar.FixedBytes || length > ArchiveWritePolicy.MaxMetadataBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive manifest sidecar length is invalid.");
        ArchiveWritePolicy.ValidateDigest(sha256);
        Length = length; Sha256 = sha256;
    }
}

/// <summary>
/// Stream-only storage for parent-owned bounded evidence. The caller must create/open an exclusive private regular
/// file through a reviewed native lease, durably flush it, and retain its object identity. This API chooses no paths,
/// never publishes/deletes, and leaves caller streams open. Its checksum is not proof that a staged archive is valid.
/// </summary>
public static class ArchiveManifestSidecar
{
    internal const int HeaderBytes = 156;
    internal const int FixedBytes = HeaderBytes + 4;
    internal static ReadOnlySpan<byte> Magic => "FCAMSC01"u8;
    internal static ReadOnlySpan<byte> End => "FCME"u8;

    public static ArchiveManifestSidecarInfo Write(Stream ownedOutput, Guid nonce, ArchiveManifestPurpose purpose,
        ArchiveWriteManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedOutput); ArgumentNullException.ThrowIfNull(manifest);
        ValidateScope(nonce, purpose);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ownedOutput.CanWrite || !ownedOutput.CanSeek || ownedOutput.Position != 0 || ownedOutput.Length != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The private manifest output must be an empty seekable stream.");
        long length = EncodedLength(manifest, cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long written = 0;
        void Put(ReadOnlySpan<byte> bytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length > length - written) throw Invalid();
            ownedOutput.Write(bytes); hash.AppendData(bytes); written += bytes.Length;
        }
        void Integer(int value)
        { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); Put(bytes); }

        Put(Magic);
        Span<byte> flags = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(flags, 1); flags[2] = (byte)purpose; flags[3] = 0; Put(flags);
        Span<byte> nonceBytes = stackalloc byte[16]; nonce.TryWriteBytes(nonceBytes, bigEndian: true, out _); Put(nonceBytes);
        Span<byte> profile = stackalloc byte[ArchiveWriteProfileEncoding.MaximumBytes];
        int profileLength = ArchiveWriteProfileEncoding.Write(manifest.Profile, profile); Put(profile[..ArchiveWriteProfileEncoding.LegacyBytes]);
        Integer(manifest.Members.Count);
        Span<byte> logical = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(logical, manifest.LogicalBytes); Put(logical);
        Integer(manifest.Features.Length); Put(profile[ArchiveWriteProfileEncoding.LegacyBytes..profileLength]); Put(manifest.Features);
        Put(Convert.FromHexString(manifest.Digest)); Put(Convert.FromHexString(manifest.ContentDigest)); Put(Convert.FromHexString(manifest.MetadataDigest));
        foreach (var member in manifest.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] record = ArchiveAssessmentWire.Member(member);
            Integer(record.Length); Put(record);
        }
        Put(End);
        if (written != length || ownedOutput.Position != length || ownedOutput.Length != length) throw Invalid();
        ownedOutput.Flush(); // Native lease owner must separately flush the exact file/directory durably.
        cancellationToken.ThrowIfCancellationRequested();
        return new(length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    internal static long EncodedLength(ArchiveWriteManifest manifest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest); ct.ThrowIfCancellationRequested();
        long length = FixedBytes + manifest.Features.Length + ArchiveWriteProfileEncoding.Length(manifest.Profile) - ArchiveWriteProfileEncoding.LegacyBytes;
        foreach (var member in manifest.Members)
        {
            ct.ThrowIfCancellationRequested();
            int recordBytes = checked(58 + ArchiveWritePolicy.Utf8.GetByteCount(member.Name) + member.Metadata.Length);
            if (recordBytes > ArchiveWritePolicy.MaxEntryMetadataBytes) throw ArchiveWriteBudget.Limit();
            length = checked(length + 4 + recordBytes);
            if (length > ArchiveWritePolicy.MaxMetadataBytes) throw ArchiveWriteBudget.Limit();
        }
        return length;
    }

    public static ArchiveManifestSidecarReader OpenRead(Stream ownedInput, ArchiveManifestSidecarInfo expected,
        Guid nonce, ArchiveManifestPurpose purpose, ArchiveWriteProfile profile, CancellationToken cancellationToken = default) =>
        new(ownedInput, expected, nonce, purpose, profile, cancellationToken);

    internal static void ValidateScope(Guid nonce, ArchiveManifestPurpose purpose)
    {
        if (nonce == Guid.Empty || !Enum.IsDefined(purpose))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive manifest transaction scope is invalid.");
    }
    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview, "The archive manifest sidecar is incomplete, changed or unsupported; keep the transaction for review.");
}

/// <summary>
/// Single-consumer bounded scan. A member returned before EOF is unverified evidence. Only Complete/ReadNext returning
/// null verifies the entire sidecar, ordered occurrences, all semantic digests and the external checksum. A failure or
/// cancellation poisons this reader; it cannot resume. Disposing a partial reader never turns it into verified data.
/// </summary>
public sealed class ArchiveManifestSidecarReader : IDisposable
{
    private readonly Stream input;
    private readonly ArchiveManifestSidecarInfo expected;
    private readonly CancellationToken cancellationToken;
    private readonly IncrementalHash fileHash;
    private readonly ArchiveWriteBudget budget = new();
    private readonly Dictionary<(string, ArchiveMemberType), int> occurrences = [];
    private ArchiveManifestDigestBuilder? digests;
    private readonly byte[] features;
    private readonly (string Full, string Content, string Metadata) expectedDigests;
    private long read;
    private int index;
    private bool failed;
    private bool disposed;
    public ArchiveWriteProfile Profile { get; }
    public int DeclaredMembers { get; }
    public long DeclaredLogicalBytes { get; }
    public ReadOnlySpan<byte> Features => features;
    public bool IsComplete { get; private set; }
    public string? VerifiedManifestDigest { get; private set; }
    public string? VerifiedContentDigest { get; private set; }
    public string? VerifiedMetadataDigest { get; private set; }

    internal ArchiveManifestSidecarReader(Stream input, ArchiveManifestSidecarInfo expected, Guid nonce,
        ArchiveManifestPurpose purpose, ArchiveWriteProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(profile);
        ArchiveManifestSidecar.ValidateScope(nonce, purpose);
        this.input = input; this.expected = expected; this.cancellationToken = cancellationToken;
        fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!input.CanRead || !input.CanSeek || input.Position != 0 || input.Length != expected.Length) throw ArchiveManifestSidecar.Invalid();
            Span<byte> header = stackalloc byte[60]; Take(header);
            if (!header[..8].SequenceEqual(ArchiveManifestSidecar.Magic) || BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) != 1 ||
                header[10] != (byte)purpose || header[11] != 0 || new Guid(header.Slice(12, 16), bigEndian: true) != nonce)
                throw ArchiveManifestSidecar.Invalid();
            try
            {
                Span<byte> encoded = stackalloc byte[ArchiveWriteProfileEncoding.MaximumBytes];
                header.Slice(28, ArchiveWriteProfileEncoding.LegacyBytes).CopyTo(encoded);
                int length = Int(header, 32) switch { 1 => ArchiveWriteProfileEncoding.LegacyBytes, 2 => ArchiveWriteProfileEncoding.MaximumBytes, _ => throw ArchiveManifestSidecar.Invalid() };
                Take(encoded[ArchiveWriteProfileEncoding.LegacyBytes..length]); Profile = ArchiveWriteProfileEncoding.Decode(encoded[..length]);
            }
            catch (ArchiveWriteException) { throw ArchiveManifestSidecar.Invalid(); }
            DeclaredMembers = Int(header, 44); DeclaredLogicalBytes = BinaryPrimitives.ReadInt64LittleEndian(header[48..]);
            int featureLength = Int(header, 56);
            if (Profile != profile || DeclaredMembers is < 0 or > ArchiveWritePolicy.MaxMembers ||
                DeclaredLogicalBytes is < 0 or > ArchiveWritePolicy.MaxLogicalBytes ||
                featureLength is < 0 or > ArchiveWritePolicy.MaxDataFrameBytes ||
                featureLength + 96L + 4L + 62L * DeclaredMembers > expected.Length - read) throw ArchiveManifestSidecar.Invalid();
            budget.AddMetadata(featureLength, featureLength);
            features = new byte[featureLength]; Take(features);
            Span<byte> hashes = stackalloc byte[96]; Take(hashes);
            expectedDigests = (Convert.ToHexString(hashes[..32]), Convert.ToHexString(hashes.Slice(32, 32)), Convert.ToHexString(hashes[64..]));
            digests = new(Profile, DeclaredMembers, features);
        }
        catch { digests?.Dispose(); fileHash.Dispose(); throw; }
    }

    public ArchiveManifestMember? ReadNext()
    {
        EnsureLive();
        if (IsComplete) return null;
        try
        {
            if (index == DeclaredMembers) { Complete(); return null; }
            Span<byte> size = stackalloc byte[4]; Take(size);
            int length = BinaryPrimitives.ReadInt32LittleEndian(size);
            // Reserve all remaining framing and bound the current record before allocating it.
            if (length is < 58 or > ArchiveWritePolicy.MaxEntryMetadataBytes ||
                length + 4L + 62L * (DeclaredMembers - index - 1) > expected.Length - read) throw ArchiveManifestSidecar.Invalid();
            byte[] record = new byte[length]; Take(record);
            ArchiveManifestMember member;
            try { member = ArchiveAssessmentWire.ReadMember(record, budget); }
            catch (ArchiveWriteException ex) when (ex.Reason != ArchiveWriteReason.BudgetExceeded) { throw ArchiveManifestSidecar.Invalid(); }
            var key = (member.Name, member.Type);
            int occurrence = occurrences.GetValueOrDefault(key);
            if (member.Occurrence != occurrence || budget.LogicalBytes > DeclaredLogicalBytes) throw ArchiveManifestSidecar.Invalid();
            occurrences[key] = occurrence + 1;
            digests!.Append(member); index++;
            return member;
        }
        catch { failed = true; throw; }
    }

    public void Complete()
    {
        EnsureLive();
        if (IsComplete) return;
        try
        {
            if (index != DeclaredMembers || budget.LogicalBytes != DeclaredLogicalBytes) throw ArchiveManifestSidecar.Invalid();
            Span<byte> end = stackalloc byte[4]; Take(end);
            if (!end.SequenceEqual(ArchiveManifestSidecar.End) || read != expected.Length || input.Position != read ||
                input.Length != expected.Length || input.ReadByte() != -1) throw ArchiveManifestSidecar.Invalid();
            var observed = digests!.Complete();
            if (observed != expectedDigests || !CryptographicOperations.FixedTimeEquals(fileHash.GetHashAndReset(), Convert.FromHexString(expected.Sha256)))
                throw ArchiveManifestSidecar.Invalid();
            cancellationToken.ThrowIfCancellationRequested();
            (VerifiedManifestDigest, VerifiedContentDigest, VerifiedMetadataDigest) = observed;
            IsComplete = true;
        }
        catch { failed = true; throw; }
    }

    private void Take(Span<byte> bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > expected.Length - read) throw ArchiveManifestSidecar.Invalid();
        try { input.ReadExactly(bytes); }
        catch (EndOfStreamException) { throw ArchiveManifestSidecar.Invalid(); }
        fileHash.AppendData(bytes); read += bytes.Length;
    }
    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private void EnsureLive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (failed) throw ArchiveManifestSidecar.Invalid();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; digests?.Dispose(); fileHash.Dispose();
    }
}
