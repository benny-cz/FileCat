using FileCat.Core.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Archives.Writing;

/// <summary>One exact BCJ2 graph node, including all four input endings and the known output extent.</summary>
internal sealed class StrictBcj2ReadStream : Stream
{
    private readonly StrictCoderInput[] inputs = new StrictCoderInput[4];
    private readonly Bcj2DecoderStream decoder;
    private readonly CancellationToken ct;
    private readonly long length;
    private long position;
    private bool complete, failed, disposed;
    internal bool Complete => complete;
    internal IReadOnlyList<long> CompressedBytesConsumed => inputs.Select(input => input.Consumed).ToArray();
    internal StrictBcj2ReadStream(IReadOnlyList<Stream> sources, IReadOnlyList<long> lengths, long length, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (sources.Count != 4 || lengths.Count != 4 || length < 0 || lengths.Any(value => value < 0) || lengths[3] < 5) throw Invalid();
        if (lengths.Take(3).Any(value => value > ArchiveWritePolicy.MaxPayloadBytes) || lengths[3] > ArchiveWritePolicy.MaxPhysicalBytes ||
            length > ArchiveWritePolicy.MaxPayloadBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The BCJ2 input or output extent exceeds the strict verification budget.");
        if ((lengths[1] & 3) != 0 || (lengths[2] & 3) != 0 || lengths[0] + lengths[1] + lengths[2] != length) throw Invalid();
        this.ct = ct; this.length = length;
        try
        {
            for (int i = 0; i < 4; i++) { if (!sources[i].CanRead) throw Invalid(); inputs[i] = new(sources[i], lengths[i], ct); }
            decoder = new(inputs, length, leaveOpen: true, ct);
        }
        catch (Exception ex)
        {
            foreach (var input in inputs) input?.Dispose();
            if (ex is InvalidDataException or EndOfStreamException or SharpCompressException) throw Invalid(ex); throw;
        }
    }
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
        if (failed) throw Invalid(); if (buffer.IsEmpty || complete) return 0;
        try
        {
            int n = decoder.Read(buffer[..Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes)]); position += n;
            if (n == 0) { if (position != length) throw Invalid(); foreach (var input in inputs) input.VerifyEnd(); complete = true; }
            return n;
        }
        catch (Exception ex)
        { failed = true; if (ex is InvalidDataException or EndOfStreamException or SharpCompressException) throw Invalid(ex); throw; }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed = true; try { decoder.Dispose(); } finally { foreach (var input in inputs) input.Dispose(); } }
        base.Dispose(disposing);
    }
    public override bool CanRead => !disposed && !failed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => length; public override long Position { get => position; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid(Exception? ex = null) => new(ArchiveWriteReason.InvalidArchive, "The BCJ2 coder has invalid sizes, incomplete output, trailing input or unfinished range state.", ex!);
}
