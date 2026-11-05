using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

public enum ArchiveOriginOperation : byte { Create = 0, Update = 1 }
public enum ArchiveOriginSourceKind { OriginalArchive = 0, File = 1, Directory = 2 }

public sealed record ArchiveOriginEvidenceScope
{
    public Guid Nonce { get; }
    public ArchiveWriteProfile Profile { get; }
    public ArchiveOriginOperation Operation { get; }
    public int Sources { get; }
    public ArchiveOriginEvidenceScope(Guid nonce, ArchiveWriteProfile profile, ArchiveOriginOperation operation, int sources)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (nonce == Guid.Empty || !Enum.IsDefined(operation) || sources < (operation == ArchiveOriginOperation.Update ? 1 : 0) ||
            sources > ArchiveWritePolicy.MaxMembers + (operation == ArchiveOriginOperation.Update ? 1 : 0))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive origin-evidence scope is invalid.");
        Nonce = nonce; Profile = profile; Operation = operation; Sources = sources;
    }
}

/// <summary>Captured source evidence, not a path, provider object, live snapshot guarantee or cleanup authority.</summary>
public sealed record ArchiveOriginEvidenceRecord
{
    internal static readonly string EmptySha256 = Convert.ToHexString(SHA256.HashData([]));
    public ArchiveOriginSourceKind Kind { get; }
    public ArchiveObjectVersion Source { get; }
    public ArchiveOriginSnapshot Origin { get; }
    public ArchiveOriginEvidenceRecord(ArchiveOriginSourceKind kind, ArchiveObjectVersion source, ArchiveOriginSnapshot origin)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(kind) || kind == ArchiveOriginSourceKind.Directory && (source.Length != 0 || source.Sha256 != EmptySha256))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive origin source kind or directory content version is invalid.");
        if (kind == ArchiveOriginSourceKind.File && source.Length > ArchiveWritePolicy.MaxPayloadBytes)
            throw ArchiveWriteBudget.Limit();
        Kind = kind; Source = source; Origin = origin;
    }
}

/// <summary>A checksum of encoded evidence; native file identity, private permissions and durable flush are separate.</summary>
public sealed record ArchiveOriginEvidenceInfo
{
    public long Length { get; }
    public string Sha256 { get; }
    public ArchiveOriginEvidenceInfo(long length, string sha256)
    {
        if (length < ArchiveOriginEvidenceWriter.HeaderBytes + 4 || length > ArchiveWritePolicy.MaxMetadataBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive origin-evidence length is invalid.");
        ArchiveWritePolicy.ValidateDigest(sha256); Length = length; Sha256 = sha256;
    }
}

/// <summary>
/// Stream-only parent evidence. The owner must create an exclusive private native file, journal/retain its identity and
/// durably flush it. This writer never chooses paths, publishes or deletes. Failure poisons it and retains partial IO;
/// Dispose does not close the owner's stream or manufacture completion. Counts/order are fixed by the reviewed plan.
/// </summary>
public sealed class ArchiveOriginEvidenceWriter : IDisposable
{
    internal const int HeaderBytes = 48, RecordBytes = 56, MinFramedRecordBytes = 61;
    internal static ReadOnlySpan<byte> Magic => "FCOEVID1"u8;
    internal static ReadOnlySpan<byte> End => "FCOE"u8;
    private readonly Stream output;
    private readonly CancellationToken cancellationToken;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long written, inputBytes;
    private bool failed, completed, disposed;
    public ArchiveOriginEvidenceScope Scope { get; }
    public int Records { get; private set; }

    public ArchiveOriginEvidenceWriter(Stream ownedOutput, ArchiveOriginEvidenceScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedOutput); ArgumentNullException.ThrowIfNull(scope);
        output = ownedOutput; Scope = scope; this.cancellationToken = cancellationToken;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!output.CanWrite || !output.CanSeek || output.Position != 0 || output.Length != 0)
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Origin evidence requires an empty seekable owned stream.");
            Span<byte> header = stackalloc byte[HeaderBytes]; header.Clear(); Magic.CopyTo(header);
            BinaryPrimitives.WriteUInt16LittleEndian(header[8..], 1); header[10] = (byte)scope.Operation;
            scope.Nonce.TryWriteBytes(header.Slice(12, 16), bigEndian: true, out _);
            Integer(header, 28, (int)scope.Profile.Format); Integer(header, 32, scope.Profile.Version);
            Integer(header, 36, scope.Profile.CompressionLevel); Integer(header, 40, scope.Profile.DictionaryBytes);
            Integer(header, 44, scope.Sources); Put(header);
        }
        catch { hash.Dispose(); throw; }
    }

    public void Append(ArchiveOriginEvidenceRecord record)
    {
        EnsureLive();
        try
        {
            ArgumentNullException.ThrowIfNull(record); cancellationToken.ThrowIfCancellationRequested();
            CheckOrder(Scope, Records, record.Kind);
            if (record.Kind == ArchiveOriginSourceKind.File)
            {
                inputBytes = checked(inputBytes + record.Source.Length);
                if (inputBytes > ArchiveWritePolicy.MaxLogicalBytes) throw ArchiveWriteBudget.Limit();
            }
            byte[] identity = ArchiveWritePolicy.Utf8.GetBytes(record.Source.Identity);
            int size = checked(RecordBytes + identity.Length + record.Origin.Bytes.Length);
            if (size > ArchiveWritePolicy.MaxEntryMetadataBytes || written + 4L + size + 4 > ArchiveWritePolicy.MaxMetadataBytes)
                throw ArchiveWriteBudget.Limit();
            Span<byte> length = stackalloc byte[4]; Integer(length, 0, size); Put(length);
            Span<byte> prefix = stackalloc byte[RecordBytes]; prefix.Clear();
            Integer(prefix, 0, (int)record.Kind); Integer(prefix, 4, identity.Length);
            BinaryPrimitives.WriteInt64LittleEndian(prefix[8..], record.Source.Length);
            Convert.FromHexString(record.Source.Sha256).CopyTo(prefix[16..]);
            prefix[48] = (byte)record.Origin.Format; prefix[49] = (byte)record.Origin.State; prefix[50] = (byte)record.Origin.Failure;
            Integer(prefix, 52, record.Origin.Bytes.Length); Put(prefix); Put(identity); Put(record.Origin.Bytes); Records++;
        }
        catch { failed = true; throw; }
    }

    public ArchiveOriginEvidenceInfo Complete()
    {
        EnsureLive();
        try
        {
            if (Records != Scope.Sources) throw Invalid();
            Put(End); output.Flush(); cancellationToken.ThrowIfCancellationRequested();
            if (output.Position != written || output.Length != written) throw Invalid();
            var result = new ArchiveOriginEvidenceInfo(written, Convert.ToHexString(hash.GetHashAndReset()));
            completed = true; return result;
        }
        catch { failed = true; throw; }
    }

    private void Put(ReadOnlySpan<byte> bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (output.Position != written || output.Length != written || bytes.Length > ArchiveWritePolicy.MaxMetadataBytes - written) throw Invalid();
        output.Write(bytes); hash.AppendData(bytes); written += bytes.Length;
    }
    private void EnsureLive()
    { ObjectDisposedException.ThrowIf(disposed, this); if (failed || completed) throw Invalid(); }
    internal static void CheckOrder(ArchiveOriginEvidenceScope scope, int index, ArchiveOriginSourceKind kind)
    {
        if (index >= scope.Sources || !Enum.IsDefined(kind) ||
            (kind == ArchiveOriginSourceKind.OriginalArchive) != (scope.Operation == ArchiveOriginOperation.Update && index == 0)) throw Invalid();
    }
    internal static void Integer(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], value);
    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview,
        "The archive origin evidence is incomplete, changed or unsupported; retain it for review.");
    public void Dispose() { if (disposed) return; disposed = true; hash.Dispose(); }
}

/// <summary>
/// Incremental bounded review without retaining the source set. Rows are unverified until complete EOF and checksum
/// validation. No caller can use a prefix as complete provenance. Failure/cancellation poisons the reader; the owned
/// input stays open. This codec preserves unavailable/opaque evidence rather than interpreting it as absence.
/// </summary>
public sealed class ArchiveOriginEvidenceReader : IDisposable
{
    private readonly Stream input;
    private readonly ArchiveOriginEvidenceInfo expected;
    private readonly CancellationToken cancellationToken;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long read, inputBytes;
    private int index;
    private bool failed, disposed;
    public ArchiveOriginEvidenceScope Scope { get; }
    public bool IsComplete { get; private set; }
    public ArchiveOriginEvidenceInfo? VerifiedInfo { get; private set; }

    public ArchiveOriginEvidenceReader(Stream ownedInput, ArchiveOriginEvidenceInfo expected, ArchiveOriginEvidenceScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedInput); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(scope);
        input = ownedInput; this.expected = expected; Scope = scope; this.cancellationToken = cancellationToken;
        try
        {
            if (!input.CanRead || !input.CanSeek || input.Position != 0 || input.Length != expected.Length) throw ArchiveOriginEvidenceWriter.Invalid();
            Span<byte> header = stackalloc byte[ArchiveOriginEvidenceWriter.HeaderBytes]; Take(header);
            if (!header[..8].SequenceEqual(ArchiveOriginEvidenceWriter.Magic) || BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) != 1 ||
                header[10] != (byte)scope.Operation || header[11] != 0 || new Guid(header.Slice(12, 16), bigEndian: true) != scope.Nonce ||
                Integer(header, 28) != (int)scope.Profile.Format || Integer(header, 32) != scope.Profile.Version ||
                Integer(header, 36) != scope.Profile.CompressionLevel || Integer(header, 40) != scope.Profile.DictionaryBytes ||
                Integer(header, 44) != scope.Sources || ArchiveOriginEvidenceWriter.MinFramedRecordBytes * (long)scope.Sources + 4 > expected.Length - read)
                throw ArchiveOriginEvidenceWriter.Invalid();
        }
        catch { hash.Dispose(); throw; }
    }

    public ArchiveOriginEvidenceRecord? ReadNext()
    {
        EnsureLive(); if (IsComplete) return null;
        try
        {
            if (index == Scope.Sources) { Complete(); return null; }
            Span<byte> size = stackalloc byte[4]; Take(size); int length = Integer(size, 0);
            long remainingFraming = ArchiveOriginEvidenceWriter.MinFramedRecordBytes * (long)(Scope.Sources - index - 1) + 4;
            if (length is < 57 or > ArchiveWritePolicy.MaxEntryMetadataBytes || length + remainingFraming > expected.Length - read)
                throw ArchiveOriginEvidenceWriter.Invalid();
            Span<byte> prefix = stackalloc byte[ArchiveOriginEvidenceWriter.RecordBytes]; Take(prefix);
            var kind = (ArchiveOriginSourceKind)Integer(prefix, 0); int identityBytes = Integer(prefix, 4), markBytes = Integer(prefix, 52);
            var format = (ArchiveOriginFormat)prefix[48]; var state = (ArchiveOriginState)prefix[49]; var failure = (ArchiveOriginFailure)prefix[50];
            if (identityBytes is < 1 or > 4096 * 4 || markBytes is < 0 or > ArchiveOriginSnapshot.MaxBytes || prefix[51] != 0 ||
                !Enum.IsDefined(format) || !Enum.IsDefined(state) || !Enum.IsDefined(failure) ||
                state != ArchiveOriginState.Present && markBytes != 0 ||
                (state == ArchiveOriginState.Unavailable) != (failure != ArchiveOriginFailure.None) ||
                ArchiveOriginEvidenceWriter.RecordBytes + identityBytes + markBytes != length) throw ArchiveOriginEvidenceWriter.Invalid();
            ArchiveOriginEvidenceWriter.CheckOrder(Scope, index, kind);
            long sourceLength = BinaryPrimitives.ReadInt64LittleEndian(prefix[8..]); string sha256 = Convert.ToHexString(prefix.Slice(16, 32));
            // Validate numeric source facts before allocating variable fields.
            if (sourceLength < 0 || sourceLength > ArchiveWritePolicy.MaxPhysicalBytes ||
                kind == ArchiveOriginSourceKind.File && sourceLength > ArchiveWritePolicy.MaxPayloadBytes ||
                kind == ArchiveOriginSourceKind.Directory && (sourceLength != 0 || sha256 != ArchiveOriginEvidenceRecord.EmptySha256))
                throw ArchiveOriginEvidenceWriter.Invalid();
            if (kind == ArchiveOriginSourceKind.File)
            {
                inputBytes = checked(inputBytes + sourceLength);
                if (inputBytes > ArchiveWritePolicy.MaxLogicalBytes) throw ArchiveWriteBudget.Limit();
            }
            byte[] identity = new byte[identityBytes]; Take(identity); byte[] mark = new byte[markBytes]; Take(mark);
            ArchiveOriginEvidenceRecord result;
            try
            {
                var version = new ArchiveObjectVersion(ArchiveWritePolicy.Utf8.GetString(identity), sourceLength, sha256);
                var origin = state switch
                {
                    ArchiveOriginState.Absent => ArchiveOriginSnapshot.Absent(format),
                    ArchiveOriginState.Present => ArchiveOriginSnapshot.Present(format, mark),
                    _ => ArchiveOriginSnapshot.Unavailable(format, failure),
                };
                result = new(kind, version, origin);
            }
            catch (Exception ex) when (ex is ArgumentException or ArchiveWriteException) { throw ArchiveOriginEvidenceWriter.Invalid(); }
            index++; return result;
        }
        catch { failed = true; throw; }
    }

    public void Complete()
    {
        EnsureLive(); if (IsComplete) return;
        try
        {
            if (index != Scope.Sources) throw ArchiveOriginEvidenceWriter.Invalid();
            Span<byte> end = stackalloc byte[4]; Take(end);
            if (!end.SequenceEqual(ArchiveOriginEvidenceWriter.End) || read != expected.Length || input.Position != read || input.Length != expected.Length ||
                input.ReadByte() != -1 || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expected.Sha256)))
                throw ArchiveOriginEvidenceWriter.Invalid();
            cancellationToken.ThrowIfCancellationRequested(); VerifiedInfo = expected; IsComplete = true;
        }
        catch { failed = true; throw; }
    }

    private void Take(Span<byte> bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > expected.Length - read) throw ArchiveOriginEvidenceWriter.Invalid();
        try { input.ReadExactly(bytes); } catch (EndOfStreamException) { throw ArchiveOriginEvidenceWriter.Invalid(); }
        hash.AppendData(bytes); read += bytes.Length;
    }
    private static int Integer(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
    private void EnsureLive() { ObjectDisposedException.ThrowIf(disposed, this); if (failed) throw ArchiveOriginEvidenceWriter.Invalid(); }
    public void Dispose() { if (disposed) return; disposed = true; hash.Dispose(); }
}
