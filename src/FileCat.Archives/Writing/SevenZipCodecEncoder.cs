using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Archives.Writing;

internal enum SevenZipCodec { Copy, Lzma, Lzma2, Bzip2, Deflate, Zstandard, Ppmd7 }
internal sealed record SevenZipCodecOptions(SevenZipCodec Codec, int Level = 0, int DictionaryBytes = 8 << 20);
internal sealed record SevenZipEncodedContent(SevenZipCodec Codec, byte[] MethodId, byte[] Properties,
    long Length, long PackedLength, uint Crc32, uint PackedCrc32, string Sha256);

/// <summary>
/// Stream-only codec adapter for the 7z container foundation. Reuses the existing permitted codecs, with validated
/// parameters and actual preset mappings. No paths, publication, secrets or registration. PPMd/filter/encryption
/// coverage remains explicit work; an enum member is not encoder support or profile qualification.
/// </summary>
internal static class SevenZipCodecEncoder
{
    internal static SevenZipEncodedContent Encode(Stream source, long length, Stream output,
        SevenZipCodecOptions options, CancellationToken ct)
    {
        Validate(options);
        if (!source.CanRead || !output.CanWrite || ReferenceEquals(source, output) || length < 0 || length > ArchiveWritePolicy.MaxPayloadBytes)
            throw Invalid("The 7z codec streams or input length are invalid.");
        ct.ThrowIfCancellationRequested();
        var measured = new PackedSink(output, ct);
        byte[] properties = [];
        byte[] methodId;
        Stream encoded;
        int effort = options.Level == 0 ? 2 : options.Level;
        switch (options.Codec)
        {
            case SevenZipCodec.Copy:
                methodId = [0]; encoded = measured; break;
            case SevenZipCodec.Lzma:
            {
                var encoder = LzmaStream.Create(new LzmaEncoderProperties(eos: true, options.DictionaryBytes, FastBytes(effort)), false, measured);
                properties = (byte[])encoder.Properties.Clone();
                methodId = [3, 1, 1]; encoded = encoder; break;
            }
            case SevenZipCodec.Lzma2:
            {
                var encoder = new Lzma2EncoderStream(measured, options.DictionaryBytes, FastBytes(effort));
                properties = (byte[])encoder.Properties.Clone();
                methodId = [0x21]; encoded = encoder; break;
            }
            case SevenZipCodec.Bzip2:
                methodId = [4, 2, 2];
                encoded = new CBZip2OutputStream(measured, effort switch { 1 => 1, 2 => 5, _ => 9 }, leaveOpen: true); break;
            case SevenZipCodec.Deflate:
                methodId = [4, 1, 8];
                encoded = new DeflateStream(measured, effort switch
                { 1 => CompressionLevel.Fastest, 2 => CompressionLevel.Optimal, _ => CompressionLevel.SmallestSize }, leaveOpen: true); break;
            case SevenZipCodec.Zstandard:
                // This is FileCat's already decoded 7z extension method, not an LZMA stream with another label.
                methodId = [4, 0xf7, 0x11, 1]; encoded = new StrictZstdWriteStream(measured, options.Level, ct); break;
            default:
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The required PPMd7 encoder is not implemented yet.");
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var crc = new ArchiveCrc32();
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        using (encoded)
        {
            try
            {
                long remaining = length;
                while (remaining > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int wanted = (int)Math.Min(buffer.Length, remaining), read = source.Read(buffer.AsSpan(0, wanted));
                    if (read <= 0 || read > wanted) throw Changed();
                    hash.AppendData(buffer, 0, read); crc.Append(buffer.AsSpan(0, read));
                    encoded.Write(buffer, 0, read); remaining -= read;
                }
                ct.ThrowIfCancellationRequested();
                if (source.ReadByte() != -1) throw Changed();
            }
            catch
            {
                // Stop before Dispose finalization when this codec exposes an explicit abort. Other codec-internal
                // finalization/loops also need the shared worker's hard deadline beyond cooperative callbacks.
                if (encoded is StrictZstdWriteStream zstd) zstd.Abort();
                throw;
            }
        }
        measured.Finish();
        ct.ThrowIfCancellationRequested();
        return new(options.Codec, methodId, properties, length, measured.Bytes, crc.Value, measured.Crc32,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    internal static void Validate(SevenZipCodecOptions options)
    {
        if (options is null || !Enum.IsDefined(options.Codec) || options.Level is < 0 or > 3 ||
            options.Codec == SevenZipCodec.Copy && options.Level != 0 ||
            options.DictionaryBytes < 1 << 20 || options.DictionaryBytes > 16 << 20 ||
            !BitOperations.IsPow2((uint)options.DictionaryBytes) ||
            options.Codec is not (SevenZipCodec.Lzma or SevenZipCodec.Lzma2) && options.DictionaryBytes != 8 << 20)
            throw Invalid("These 7z codec parameters are unsupported or ineffective.");
        if (options.Codec == SevenZipCodec.Ppmd7)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "PPMd7 needs its separately reviewed encoder/model properties and qualification.");
    }

    private static int FastBytes(int effort) => effort switch { 1 => 16, 2 => 32, _ => 64 };

    private sealed class PackedSink(Stream output, CancellationToken ct) : Stream
    {
        private ArchiveCrc32 _crc;
        private readonly byte[] _pending = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private int _pendingBytes;
        internal long Bytes { get; private set; }
        internal uint Crc32 => _crc.Value;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Bytes; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            if (buffer.Length > ArchiveWritePolicy.MaxPhysicalBytes - Bytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The 7z packed stream exceeded its physical-byte budget.");
            _crc.Append(buffer); Bytes += buffer.Length;
            while (!buffer.IsEmpty)
            {
                int count = Math.Min(buffer.Length, _pending.Length - _pendingBytes);
                buffer[..count].CopyTo(_pending.AsSpan(_pendingBytes));
                _pendingBytes += count; buffer = buffer[count..];
                if (_pendingBytes == _pending.Length) Finish();
            }
        }
        public override void WriteByte(byte value)
        {
            Span<byte> one = stackalloc byte[1]; one[0] = value; Write(one);
        }
        internal void Finish()
        {
            ct.ThrowIfCancellationRequested();
            if (_pendingBytes == 0) return;
            output.Write(_pending.AsSpan(0, _pendingBytes)); _pendingBytes = 0;
        }
        public override void Flush() { Finish(); output.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The captured 7z input changed or ended unexpectedly.");
}
