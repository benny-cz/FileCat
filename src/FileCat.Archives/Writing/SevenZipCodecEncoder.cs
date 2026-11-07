using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Compressors.PPMd;
using SharpCompress.Compressors.Filters;
using System.Buffers.Binary;

namespace FileCat.Archives.Writing;

internal enum SevenZipCodec { Copy, Lzma, Lzma2, Bzip2, Deflate, Zstandard, Ppmd7 }
internal enum SevenZipFilter { None, Delta, X86, Arm, ArmThumb, Arm64, Ppc, Ia64, Sparc, RiscV, Swap2, Swap4, Bcj2 }
internal sealed record SevenZipCodecOptions(SevenZipCodec Codec, int Level = 0, int DictionaryBytes = 8 << 20,
    int ModelOrder = 0, int ModelMemoryBytes = 0, SevenZipFilter Filter = SevenZipFilter.None, int DeltaDistance = 1);
internal sealed record SevenZipCoder(byte[] MethodId, byte[] Properties, int Inputs = 1, int Outputs = 1);
internal sealed record SevenZipBinding(int Input, int Output);
internal sealed record SevenZipPackedStream(long Length, uint Crc32);
internal sealed record SevenZipEncodedContent(SevenZipCodec? Codec, IReadOnlyList<SevenZipCoder> Coders,
    IReadOnlyList<SevenZipBinding> Bindings, IReadOnlyList<long> UnpackedSizes, IReadOnlyList<SevenZipPackedStream> PackedStreams,
    long Length, uint Crc32, string Sha256)
{
    internal long PackedLength => PackedStreams.Sum(stream => stream.Length);
}

/// <summary>
/// Stream-only codec adapter for the 7z container foundation. Reuses the existing permitted codecs, with validated
/// parameters and actual preset mappings. No paths, publication or registration. BCJ2 uses its separate fresh-view
/// four-pass packing path; codec/graph implementation alone does not establish production profile qualification.
/// </summary>
internal static class SevenZipCodecEncoder
{
    internal static SevenZipEncodedContent Encode(Stream source, long length, Stream output,
        SevenZipCodecOptions options, CancellationToken ct, SevenZipEncryptionSession? encryption = null)
    {
        Validate(options);
        if (!source.CanRead || !output.CanWrite || ReferenceEquals(source, output) || length < 0 || length > ArchiveWritePolicy.MaxPayloadBytes)
            throw Invalid("The 7z codec streams or input length are invalid.");
        ct.ThrowIfCancellationRequested();
        var measured = new PackedSink(output, ct);
        var cipherTarget = encryption?.CreateCipher(measured, header: false, ArchiveWritePolicy.MaxPhysicalBytes, ct);
        using ArchiveAesCbcWriteStream? cipher = cipherTarget?.Cipher;
        var codecOutput = cipher is null ? measured : new PackedSink(cipher, ct);
        var (encoded, codec) = CreateCodec(codecOutput, options, ct);
        FilterInputSink? filtered = null;
        SevenZipCoder? filter = null;
        if (options.Filter != SevenZipFilter.None)
        {
            filtered = new(encoded, length, ct);
            try { (encoded, filter) = MakeFilter(filtered, options); }
            catch { filtered.Dispose(); throw; }
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
        codecOutput.Finish();
        if (cipher is not null)
        {
            cipher.Finish(); measured.Finish();
            if (cipher.PlaintextBytes != codecOutput.Bytes || cipher.CiphertextBytes != measured.Bytes)
                throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The 7z cipher changed its declared extent.");
        }
        ct.ThrowIfCancellationRequested();
        if (filtered is not null && filtered.Bytes != length)
            throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The 7z filter changed its declared byte extent.");
        // Use decoding order for interoperability: AES, compression, optional filter. Without AES this preserves
        // the codec-first ordering required by the secondary libarchive reader; py7zr also consumes list order.
        var coders = new List<SevenZipCoder>(); var bindings = new List<SevenZipBinding>(); var sizes = new List<long>();
        if (cipherTarget is { } encrypted)
        {
            coders.Add(new([6, 0xf1, 7, 1], encrypted.Properties)); sizes.Add(codecOutput.Bytes);
        }
        int codecIndex = coders.Count; coders.Add(codec); sizes.Add(length);
        if (codecIndex != 0) bindings.Add(new(codecIndex, 0));
        if (filter is not null) { int filterIndex = coders.Count; coders.Add(filter); bindings.Add(new(filterIndex, codecIndex)); sizes.Add(length); }
        return new(options.Codec, coders.AsReadOnly(), bindings.AsReadOnly(), sizes.AsReadOnly(), [new(measured.Bytes, measured.Crc32)], length, crc.Value,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    // Caller validates options once before opening grants or touching a stage. No filter is applied here.
    internal static (Stream Stream, SevenZipCoder Coder) CreateCodec(Stream codecOutput, SevenZipCodecOptions options, CancellationToken ct)
    {
        byte[] properties = [];
        byte[] methodId;
        Stream encoded;
        int effort = options.Level == 0 ? 2 : options.Level;
        switch (options.Codec)
        {
            case SevenZipCodec.Copy:
                methodId = [0]; encoded = codecOutput; break;
            case SevenZipCodec.Lzma:
            {
                var encoder = LzmaStream.Create(new LzmaEncoderProperties(eos: true, options.DictionaryBytes, FastBytes(effort)), false, codecOutput);
                properties = (byte[])encoder.Properties.Clone();
                methodId = [3, 1, 1]; encoded = encoder; break;
            }
            case SevenZipCodec.Lzma2:
            {
                var encoder = new Lzma2EncoderStream(codecOutput, options.DictionaryBytes, FastBytes(effort));
                properties = (byte[])encoder.Properties.Clone();
                methodId = [0x21]; encoded = encoder; break;
            }
            case SevenZipCodec.Bzip2:
                methodId = [4, 2, 2];
                encoded = new CBZip2OutputStream(codecOutput, effort switch { 1 => 1, 2 => 5, _ => 9 }, leaveOpen: true); break;
            case SevenZipCodec.Deflate:
                methodId = [4, 1, 8];
                encoded = new CompleteDeflateWriteStream(codecOutput, effort switch
                { 1 => CompressionLevel.Fastest, 2 => CompressionLevel.Optimal, _ => CompressionLevel.SmallestSize }, ct); break;
            case SevenZipCodec.Zstandard:
                // This is FileCat's already decoded 7z extension method, not an LZMA stream with another label.
                // Standard extension metadata: producer version 1.5, actual level, two reserved zero bytes.
                // Some independent readers require these properties even though libarchive ignores them.
                properties = [1, 5, (byte)(effort switch { 1 => 1, 2 => 3, _ => 9 }), 0, 0];
                methodId = [4, 0xf7, 0x11, 1]; encoded = new StrictZstdWriteStream(codecOutput, options.Level, ct); break;
            case SevenZipCodec.Ppmd7:
            {
                var (order, memory) = PpmdSettings(options);
                properties = new byte[5]; properties[0] = (byte)order;
                BinaryPrimitives.WriteUInt32LittleEndian(properties.AsSpan(1), (uint)memory);
                methodId = [3, 4, 1]; encoded = new Ppmd7zEncoderStream(codecOutput, order, memory, ct); break;
            }
            default:
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This 7z codec is not implemented.");
        }
        return (encoded, new(methodId, properties));
    }

    internal static void Validate(SevenZipCodecOptions options, bool allowBcj2 = false)
    {
        if (options is null || !Enum.IsDefined(options.Codec) || !Enum.IsDefined(options.Filter) || options.Level is < 0 or > 3 ||
            options.Codec == SevenZipCodec.Copy && options.Level != 0 ||
            options.DictionaryBytes < 1 << 20 || options.DictionaryBytes > 16 << 20 ||
            !BitOperations.IsPow2((uint)options.DictionaryBytes) ||
            options.Codec is not (SevenZipCodec.Lzma or SevenZipCodec.Lzma2) && options.DictionaryBytes != 8 << 20 ||
            options.Codec != SevenZipCodec.Ppmd7 && (options.ModelOrder != 0 || options.ModelMemoryBytes != 0) ||
            options.DeltaDistance is < 1 or > 256 || options.Filter != SevenZipFilter.Delta && options.DeltaDistance != 1)
            throw Invalid("These 7z codec parameters are unsupported or ineffective.");
        if (options.Filter == SevenZipFilter.Bcj2 && !allowBcj2)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "BCJ2 needs the bounded four-pass packing route and fresh source grants.");
        if (options.Codec == SevenZipCodec.Ppmd7)
        {
            var (order, memory) = PpmdSettings(options);
            if (order is < 2 or > 64 || memory < 1 << 11 || memory > 32 << 20 ||
                (options.ModelOrder == 0) != (options.ModelMemoryBytes == 0))
                throw Invalid("These PPMd7 model parameters are invalid or exceed the bounded encoder profile.");
        }
    }

    private static (int Order, int Memory) PpmdSettings(SevenZipCodecOptions options)
    {
        if (options.ModelOrder != 0 || options.ModelMemoryBytes != 0) return (options.ModelOrder, options.ModelMemoryBytes);
        return options.Level switch { 1 => (4, 4 << 20), 3 => (8, 16 << 20), _ => (6, 8 << 20) };
    }

    private static int FastBytes(int effort) => effort switch { 1 => 16, 2 => 32, _ => 64 };

    private static (Stream Encoder, SevenZipCoder Coder) MakeFilter(Stream output, SevenZipCodecOptions options) => options.Filter switch
    {
        SevenZipFilter.Delta => (new DeltaFilter(true, output, [(byte)(options.DeltaDistance - 1)]), new([3], [(byte)(options.DeltaDistance - 1)])),
        SevenZipFilter.X86 => (new BCJFilter(true, output), new([3, 3, 1, 3], [])),
        SevenZipFilter.Arm => (new BCJFilterARM(true, output), new([3, 3, 5, 1], [])),
        SevenZipFilter.ArmThumb => (new BCJFilterARMT(true, output), new([3, 3, 7, 1], [])),
        SevenZipFilter.Arm64 => (new BCJFilterARM64(true, output), new([10], [])),
        SevenZipFilter.Ppc => (new BCJFilterPPC(true, output), new([3, 3, 2, 5], [])),
        SevenZipFilter.Ia64 => (new BCJFilterIA64(true, output), new([3, 3, 4, 1], [])),
        SevenZipFilter.Sparc => (new BCJFilterSPARC(true, output), new([3, 3, 8, 5], [])),
        SevenZipFilter.RiscV => (new BCJFilterRISCV(true, output), new([11], [])),
        SevenZipFilter.Swap2 => (new SwapFilter(true, output, 2), new([2, 3, 2], [])),
        SevenZipFilter.Swap4 => (new SwapFilter(true, output, 4), new([2, 3, 4], [])),
        _ => throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The selected 7z filter is not implemented."),
    };

    private sealed class FilterInputSink(Stream codec, long expectedLength, CancellationToken ct) : Stream
    {
        internal long Bytes { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Bytes; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            if (buffer.Length > expectedLength - Bytes)
                throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The 7z filter exceeded its byte extent.");
            codec.Write(buffer); Bytes += buffer.Length;
        }
        protected override void Dispose(bool disposing) { if (disposing) codec.Dispose(); base.Dispose(disposing); }
        public override void Flush() => codec.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal sealed class PackedSink(Stream output, CancellationToken ct) : Stream
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
