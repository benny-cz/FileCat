using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Common.SevenZip;

namespace FileCat.Archives.Writing;

/// <summary>
/// Complete admitted physical/header observations, not plaintext payload verification or permission to rewrite.
/// AES properties stay job-local; this is not a persistent journal or settings object.
/// </summary>
internal sealed record SevenZipPhysicalCatalog(string Revision, long Length, byte MinorVersion,
    long NextHeaderOffset, long NextHeaderLength, SevenZipRawStreams? EncodedHeader, SevenZipRawStreams Streams,
    SevenZipRawFiles Files, bool MainStreamsPresent, long RetainedHeapBytes)
{
    internal bool EncodedHeaderEndVerified { get; init; }
}

/// <summary>
/// Bounded stream-only 7z framing, exact metadata and graph observer. Complete member checksums/SHA, codec endings,
/// filter/preservation admission and publication require the separate logical catalog and transaction path.
/// </summary>
internal static class StrictSevenZipFraming
{
    internal const int MaximumHeaderDictionaryBytes = 16 << 20;

    internal static SevenZipPhysicalCatalog Read(Stream input, CancellationToken ct, SevenZipReadPasswordSession? password = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek || input.Position != 0 || input.Length < 32) throw Invalid("The 7z framing source grant is invalid.");
        if (input.Length > ArchiveWritePolicy.MaxPhysicalBytes) throw Limit();
        long length = input.Length; var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, 256 << 10);
        byte[] signature = new byte[32]; ReadExactly(input, signature, ct);
        if (!signature.AsSpan(0, 6).SequenceEqual((ReadOnlySpan<byte>)[0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c]))
            throw Unsupported("This source is not an unprefixed 7z container.");
        if (signature[6] != 0 || signature[7] > 4) throw Unsupported("This 7z container version needs a separately qualified profile.");
        if (ArchiveCrc32.Compute(signature.AsSpan(12)) != BinaryPrimitives.ReadUInt32LittleEndian(signature.AsSpan(8)))
            throw Invalid("The 7z start-header checksum does not match.");
        ulong relative = BinaryPrimitives.ReadUInt64LittleEndian(signature.AsSpan(12));
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(signature.AsSpan(20));
        if (relative > (ulong)(length - 32) || size > (ulong)(length - 32) - relative || relative + size != (ulong)(length - 32))
            throw Invalid("The 7z next-header extent does not account for the complete source.");
        if (size > (ulong)ArchiveWritePolicy.MaxMetadataBytes) throw Limit();
        long headerOffset = 32 + (long)relative;
        string revision = Scan(input, length, [], ct);
        input.Position = headerOffset;
        using var raw = HeaderBuffer.Read(input, (long)size, budget, ct);
        if (ArchiveCrc32.Compute(raw.Bytes) != BinaryPrimitives.ReadUInt32LittleEndian(signature.AsSpan(28)))
            throw Invalid("The 7z next-header checksum does not match.");
        SevenZipRawStreams? encoded = null; HeaderBuffer? decoded = null;
        try
        {
            ReadOnlySpan<byte> header = raw.Bytes;
            var outer = new SevenZipStrictHeaderReader(header);
            if (!header.IsEmpty && outer.Tag() == 23)
            {
                encoded = SevenZipStrictMetadata.Streams(ref outer, budget, ct); outer.End();
                if (encoded.Folders.Count != 1) throw Unsupported("Multiple encoded 7z header folders need a separately qualified profile.");
                CheckPackedExtents(encoded.Packed, headerOffset);
                var folder = encoded.Folders[0];
                if (folder.Encrypted && password is null) throw Unsupported("A password is required to assess encrypted 7z headers.");
                foreach (var pack in encoded.Packed) CheckPacked(input, pack, ct);
                long packedLength = encoded.Packed.Sum(pack => pack.Length);
                if (folder.Length > 64L << 20 && folder.Length / Math.Max(1, packedLength) > 1000) throw Limit();
                using var range = new HeaderRange(input, encoded.Packed[0].Offset, packedLength, ct);
                using var decoder = new StrictSevenZipHeaderDecoder(range, packedLength, folder, password, budget, ct);
                decoded = HeaderBuffer.Read(decoder, folder.Length, budget, ct);
                if (decoder.ReadByte() != -1) throw Invalid("The decoded 7z header exceeds its declared size.");
                if (encoded.MemberCrc32[0] is { } expected && ArchiveCrc32.Compute(decoded.Bytes) != expected)
                    throw Invalid("The decoded 7z header checksum or password does not match.");
                header = decoded.Bytes;
                var inner = new SevenZipStrictHeaderReader(header);
                if (!header.IsEmpty && inner.Tag() == 23) throw Unsupported("Nested encoded 7z metadata needs a separately qualified decoder profile.");
            }
            var observations = header.IsEmpty
                ? (Streams: SevenZipRawStreams.Empty, Files: new SevenZipRawFiles([], 0, 0), MainStreamsPresent: false)
                : SevenZipStrictMetadata.Header(header, budget, ct);
            var all = observations.Streams.Packed.Concat(encoded?.Packed ?? []).OrderBy(pack => pack.Offset).ToArray();
            long covered = 32;
            foreach (var pack in all)
            {
                if (pack.Offset != covered || pack.Length > headerOffset - covered)
                    throw Invalid("The 7z packed streams overlap, leave unaccounted data or extend into the next header.");
                covered += pack.Length;
            }
            if (covered != headerOffset) throw Unsupported("Unreferenced 7z data cannot be preserved by this profile.");
            if (Scan(input, length, all, ct) != revision) throw Changed();
            return new(revision, length, signature[7], headerOffset, (long)size, encoded, observations.Streams,
                observations.Files, observations.MainStreamsPresent, budget.RetainedHeapBytes) { EncodedHeaderEndVerified = true };
        }
        catch (ArchiveWriteException) { throw; }
        catch (InvalidDataException ex) when (ex.InnerException is ArchiveWriteException) { throw (ArchiveWriteException)ex.InnerException; }
        catch (OperationCanceledException) { throw; }
        catch (NotSupportedException ex)
        { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The 7z header decoder does not support this graph or its properties.", ex); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or SharpCompress.Common.SharpCompressException or ArgumentException)
        { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The 7z header graph, decoded metadata or password is invalid.", ex); }
        finally { decoded?.Dispose(); }
    }

    internal static CFolder ToDecoderFolder(SevenZipRawFolder folder) => new()
    {
        _coders = folder.Coders.Select(coder => new CCoderInfo { _methodId = new(SevenZipStrictMetadata.Method(coder)),
            _props = coder.Properties, _numInStreams = coder.Inputs, _numOutStreams = coder.Outputs }).ToList(),
        _bindPairs = folder.Bindings.Select(binding => new CBindPair { _inIndex = binding.Input, _outIndex = binding.Output }).ToList(),
        _packStreams = folder.PackedInputs.ToList(), _unpackSizes = folder.UnpackedSizes.ToList(), _unpackCrc = folder.Crc32,
    };

    private static void CheckPackedExtents(IReadOnlyList<SevenZipRawPack> packs, long before)
    {
        long next = packs.Count == 0 ? before : packs[0].Offset;
        foreach (var pack in packs)
        {
            if (pack.Offset != next || pack.Offset < 32 || pack.Offset > before || pack.Length > before - pack.Offset)
                throw Invalid("The encoded 7z header packed extents are invalid.");
            next += pack.Length;
        }
    }
    private static void CheckPacked(Stream input, SevenZipRawPack pack, CancellationToken ct)
    {
        if (pack.Crc32 is not { } expected) return;
        input.Position = pack.Offset; var crc = new ArchiveCrc32(); byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        try
        {
            long left = pack.Length;
            while (left > 0)
            {
                ct.ThrowIfCancellationRequested(); int n = input.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, left)));
                if (n <= 0) throw Invalid("The packed 7z header ends early.");
                crc.Append(buffer.AsSpan(0, n)); left -= n;
            }
            if (crc.Value != expected) throw Invalid("The packed 7z header checksum does not match.");
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static string Scan(Stream input, long length, IReadOnlyList<SevenZipRawPack> packs, CancellationToken ct)
    {
        input.Position = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes]; long position = 0; int packIndex = 0; var crc = new ArchiveCrc32();
        try
        {
            while (position < length)
            {
                ct.ThrowIfCancellationRequested();
                while (packIndex < packs.Count && position == packs[packIndex].Offset + packs[packIndex].Length)
                {
                    if (packs[packIndex].Crc32 is { } expected && crc.Value != expected) throw Invalid("A packed 7z stream checksum does not match.");
                    packIndex++; crc = new();
                }
                var pack = packIndex < packs.Count ? packs[packIndex] : null;
                long end = pack is null ? length : position < pack.Offset ? pack.Offset : pack.Offset + pack.Length;
                int n = input.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, end - position)));
                if (n <= 0) throw Invalid("The 7z source ends before its declared extent.");
                hash.AppendData(buffer, 0, n); if (pack is not null && position >= pack.Offset) crc.Append(buffer.AsSpan(0, n));
                position += n;
            }
            Span<byte> extra = stackalloc byte[1];
            if (input.Read(extra) != 0 || input.Length != length) throw Changed();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static void ReadExactly(Stream input, Span<byte> buffer, CancellationToken ct)
    {
        while (!buffer.IsEmpty)
        {
            ct.ThrowIfCancellationRequested(); int n = input.Read(buffer[..Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes)]);
            if (n <= 0) throw Invalid("The 7z source or header ends early."); buffer = buffer[n..];
        }
    }
    private sealed class HeaderBuffer : IDisposable
    {
        private byte[] _buffer = []; private int _length;
        internal ReadOnlySpan<byte> Bytes => _buffer.AsSpan(0, _length);
        internal static HeaderBuffer Read(Stream source, long length, ArchiveWriteBudget budget, CancellationToken ct)
        {
            if (length < 0 || length > ArchiveWritePolicy.MaxMetadataBytes) throw Limit();
            var result = new HeaderBuffer();
            try
            {
                while (result._length < length)
                {
                    ct.ThrowIfCancellationRequested();
                    if (result._buffer.Length == result._length)
                    {
                        int capacity = (int)Math.Min(length, Math.Max(1 << 20, 2L * result._buffer.Length));
                        budget.AddMetadata(0, capacity); byte[] grown = new byte[capacity];
                        result.Bytes.CopyTo(grown); CryptographicOperations.ZeroMemory(result._buffer); result._buffer = grown;
                    }
                    int wanted = (int)Math.Min(ArchiveIoAuthority.MaximumReadBytes, result._buffer.Length - result._length);
                    int n = source.Read(result._buffer.AsSpan(result._length, wanted));
                    if (n <= 0) throw Invalid("The 7z metadata stream ends early.");
                    budget.AddMetadata(n, 0); result._length += n;
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(_buffer); _buffer = []; _length = 0; }
    }
    private sealed class HeaderRange(Stream source, long offset, long length, CancellationToken ct) : Stream
    {
        private long _position;
        public override int Read(Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested(); source.Position = offset + _position;
            int n = source.Read(buffer[..(int)Math.Min(Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes), length - _position)]);
            _position += n; return n;
        }
        public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));
        public override long Length => length;
        public override long Position { get => _position; set { ct.ThrowIfCancellationRequested(); if (value < 0 || value > length) throw Invalid("The 7z decoder exceeded its header grant."); _position = value; } }
        public override long Seek(long value, SeekOrigin origin) { Position = checked(origin switch { SeekOrigin.Begin => value, SeekOrigin.Current => _position + value, SeekOrigin.End => length + value, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }); return Position; }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();
    }
    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    private static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The 7z metadata or header decoder exceeds this write profile's resource budget.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The 7z source changed during complete framing assessment.");
}
