using System.Security.Cryptography;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    private abstract class SequentialStream : Stream
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { }
    }
    private sealed class Counter(CancellationToken cancellation) : SequentialStream
    {
        internal long Bytes;
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellation.ThrowIfCancellationRequested();
            if (buffer.Length > AnalysisLimits.InventoryBytes - Bytes) throw Invalid("Saved comparison size limit reached.");
            Bytes += buffer.Length;
        }
    }
    private sealed class HashWriter(Stream target, IncrementalHash hash, CancellationToken cancellation) : SequentialStream
    {
        internal long Bytes;
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellation.ThrowIfCancellationRequested();
            if (buffer.Length > AnalysisLimits.InventoryBytes - Bytes) throw Invalid("Saved comparison size limit reached.");
            target.Write(buffer); hash.AppendData(buffer); Bytes += buffer.Length;
        }
    }
    // Each embedded inventory sees its own exact EOF. The reader cannot consume
    // the next side or the outer trailer through a buffered read.
    private sealed class Slice(Stream source, long bytes, IncrementalHash hash, CancellationToken cancellation) : SequentialStream
    {
        internal long Remaining = bytes;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            cancellation.ThrowIfCancellationRequested();
            int take = (int)Math.Min(buffer.Length, Remaining);
            if (take == 0) return 0;
            int count = source.Read(buffer[..take]);
            if (count == 0) throw Invalid("Saved comparison input is truncated.");
            hash.AppendData(buffer[..count]); Remaining -= count; return count;
        }
    }
}
