using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// Append-only four-pass BCJ2 folder packing. One codec model is live at a time. Each fresh source lease must match
/// the frozen digest; main/call/jump use the selected codec and the range stream stays raw. Source/CPU/wire admission
/// must charge all FOUR passes. No scratch, paths, host publication or implicit in-memory whole-stream storage.
/// </summary>
internal static class SevenZipBcj2Packing
{
    internal static SevenZipEncodedContent Encode(Func<Stream> openSource, long length, string sourceSha256,
        Stream output, SevenZipCodecOptions options, CancellationToken ct, SevenZipEncryptionSession? encryption)
    {
        ArgumentNullException.ThrowIfNull(openSource); ArgumentNullException.ThrowIfNull(output);
        SevenZipCodecEncoder.Validate(options, allowBcj2: true); ArchiveWritePolicy.ValidateDigest(sourceSha256);
        if (options.Filter != SevenZipFilter.Bcj2 || !output.CanWrite || length is <= 0 or > ArchiveWritePolicy.MaxPayloadBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The BCJ2 folder packing request is invalid.");
        var coders = new List<SevenZipCoder>(); var bindings = new List<SevenZipBinding>();
        var sizes = new List<long>(); var packed = new List<SevenZipPackedStream>();
        int[] leaves = new int[4]; Array.Fill(leaves, -1);
        SevenZipBcj2Content? baseline = null;
        for (int stream = 0; stream < 4; stream++)
        {
            ct.ThrowIfCancellationRequested();
            var lease = openSource() ?? throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A BCJ2 source grant did not open.");
            if (ReferenceEquals(lease, output)) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "BCJ2 output cannot also be a source grant.");
            using var source = lease;
            var measured = new SevenZipCodecEncoder.PackedSink(output, ct);
            var cipherTarget = encryption?.CreateCipher(measured, header: false, ArchiveWritePolicy.MaxPhysicalBytes, ct);
            using ArchiveAesCbcWriteStream? cipher = cipherTarget?.Cipher;
            var codecOutput = cipher is null ? measured : new SevenZipCodecEncoder.PackedSink(cipher, ct);
            SevenZipBcj2Content observed; SevenZipCoder? codec = null;
            Stream encoded = codecOutput;
            if (stream != 3) (encoded, codec) = SevenZipCodecEncoder.CreateCodec(codecOutput, options, ct);
            using (encoded)
            {
                Stream[] targets = [new Discard(), new Discard(), new Discard(), new Discard()]; targets[stream] = encoded;
                try { observed = SevenZipBcj2Encoder.Encode(source, length, targets, ct); }
                catch { if (encoded is StrictZstdWriteStream zstd) zstd.Abort(); throw; }
            }
            if (observed.Sha256 != sourceSha256 || baseline is not null &&
                (observed.Crc32 != baseline.Crc32 || !observed.Streams.SequenceEqual(baseline.Streams)))
                throw new ArchiveWriteException(ArchiveWriteReason.SourceChanged, "The captured BCJ2 source changed between packing passes.");
            baseline ??= observed;
            codecOutput.Finish();
            if (cipher is not null)
            {
                cipher.Finish(); measured.Finish();
                if (cipher.PlaintextBytes != codecOutput.Bytes || cipher.CiphertextBytes != measured.Bytes)
                    throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The BCJ2 cipher changed its declared extent.");
            }
            int previous = -1;
            if (cipherTarget is { } encrypted)
            {
                previous = coders.Count; coders.Add(new([6, 0xf1, 7, 1], encrypted.Properties)); sizes.Add(codecOutput.Bytes);
            }
            if (codec is not null)
            {
                int index = coders.Count; coders.Add(codec); sizes.Add(observed.Streams[stream].Length);
                if (previous >= 0) bindings.Add(new(index, previous)); previous = index;
            }
            leaves[stream] = previous; packed.Add(new(measured.Bytes, measured.Crc32));
        }
        ct.ThrowIfCancellationRequested();
        int bcj2 = coders.Count; coders.Add(new([3, 3, 1, 0x1b], [], 4, 1)); sizes.Add(length);
        for (int stream = 0; stream < 4; stream++) if (leaves[stream] >= 0) bindings.Add(new(bcj2 + stream, leaves[stream]));
        return new(options.Codec, coders.AsReadOnly(), bindings.AsReadOnly(), sizes.AsReadOnly(), packed.AsReadOnly(),
            length, baseline!.Crc32, baseline.Sha256);
    }

    private sealed class Discard : Stream
    {
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { }
        public override void Write(ReadOnlySpan<byte> bytes) { }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long length) => throw new NotSupportedException();
    }
}
