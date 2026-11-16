using System.Buffers.Binary;
using FileCat.Core.Archives;
using SharpCompress.Compressors.PPMd;

namespace FileCat.Archives.Writing;

/// <summary>Exact known-size PPMd7 coder, including real-byte input, terminal range code and complete consumption.</summary>
internal sealed class StrictPpmdReadStream : Stream
{
    internal const int MaximumModelBytes = 32 << 20;
    private readonly StrictCoderInput input;
    private readonly PpmdStream decoder;
    private readonly CancellationToken ct;
    private readonly long length;
    private bool ended, failed, disposed;
    internal bool Complete => ended;
    internal long CompressedBytesConsumed => input.Consumed;
    internal StrictPpmdReadStream(Stream source, ReadOnlySpan<byte> properties, long packedLength, long length, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (properties.Length != 5 || properties[0] is < 2 or > 64 || packedLength < 5 || length < 0 || !source.CanRead) throw Invalid();
        uint memory = BinaryPrimitives.ReadUInt32LittleEndian(properties[1..]);
        if (memory < 1 << 11) throw Invalid();
        if (memory > MaximumModelBytes || packedLength > ArchiveWritePolicy.MaxPhysicalBytes || length > ArchiveWritePolicy.MaxPayloadBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The PPMd model or extent exceeds the strict verification budget.");
        this.ct = ct; this.length = length; input = new(source, packedLength, ct);
        try { decoder = PpmdStream.CreateStrict(new(properties), input, length, ct); }
        catch (Exception ex) { input.Dispose(); if (ex is InvalidDataException or ArgumentException or EndOfStreamException) throw Invalid(ex); throw; }
    }
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (failed) throw Invalid(); if (buffer.IsEmpty || ended) return 0;
        try
        {
            int n = decoder.Read(buffer[..Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes)]);
            if (n == 0)
            { if (!decoder.HasValidatedKnownSizeEnd || decoder.Position != length) throw Invalid(); input.VerifyEnd(); ended = true; }
            return n;
        }
        catch (Exception ex) { failed = true; if (ex is InvalidDataException or EndOfStreamException or ArgumentException) throw Invalid(ex); throw; }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    protected override void Dispose(bool disposing)
    { if (disposing && !disposed) { disposed = true; try { decoder.Dispose(); } finally { input.Dispose(); } } base.Dispose(disposing); }
    public override bool CanRead => !disposed && !failed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => length; public override long Position { get => decoder.Position; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid(Exception? ex = null) => new(ArchiveWriteReason.InvalidArchive, "The PPMd coder is incomplete, damaged, has a wrong declared size or contains trailing input.", ex!);
}
