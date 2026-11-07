using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Archives.Writing;

/// <summary>
/// One exact LZMA/LZMA2 coder extent, including legal marked/markerless range finish, every LZMA2 chunk and EOF.
/// The parent retains its source; consumers must read through zero before treating the coder as complete.
/// </summary>
internal sealed class StrictLzmaReadStream : Stream
{
    internal const int MaximumDictionaryBytes = 64 << 20;
    private readonly BufferedInput _input;
    private readonly LzmaStream _decoder;
    private readonly CancellationToken _ct;
    private readonly long _length;
    private readonly byte[] _readBuffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
    private bool _ended, _failed, _disposed;
    internal bool Complete => _ended;
    internal bool FinishedWithMarker => _ended && _decoder.FinishedWithLzmaEndMarker;
    internal long CompressedBytesConsumed => _input.Consumed;

    internal StrictLzmaReadStream(Stream source, ReadOnlySpan<byte> properties, bool lzma2, long packedLength,
        long length, CancellationToken ct, int maximumDictionaryBytes = MaximumDictionaryBytes)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        if (!source.CanRead || packedLength < (lzma2 ? 1 : 5) || length < 0 || maximumDictionaryBytes is < 4096 or > MaximumDictionaryBytes)
            throw Invalid();
        if (packedLength > ArchiveWritePolicy.MaxPhysicalBytes || length > ArchiveWritePolicy.MaxPayloadBytes) throw Limit();
        byte[] admitted = properties.ToArray(); long dictionary;
        if (lzma2)
        {
            if (admitted.Length != 1 || admitted[0] > 40) throw Invalid();
            dictionary = admitted[0] == 40 ? uint.MaxValue : (2L | (admitted[0] & 1)) << ((admitted[0] >> 1) + 11);
        }
        else
        {
            if (admitted.Length != 5 || admitted[0] >= 225) throw Invalid();
            dictionary = Math.Max(4096L, BinaryPrimitives.ReadUInt32LittleEndian(admitted.AsSpan(1)));
            // SDK dictionary semantics use a 4 KiB minimum, including a zero property claim. Retain the source's
            // original property bytes in its catalog; normalize only this decoder's private allocation/input.
            if (dictionary <= maximumDictionaryBytes) BinaryPrimitives.WriteUInt32LittleEndian(admitted.AsSpan(1), (uint)dictionary);
        }
        if (dictionary > maximumDictionaryBytes) throw Limit();
        _ct = ct; _length = length; _input = new(source, packedLength, ct);
        try { _decoder = LzmaStream.CreateStrict(admitted, _input, packedLength, length, lzma2); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or SharpCompressException or ArgumentException)
        { _input.Dispose(); throw Invalid(ex); }
        catch { _input.Dispose(); throw; }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _ct.ThrowIfCancellationRequested();
        _ = buffer.AsSpan(offset, count);
        if (_failed) throw Invalid(); if (count == 0 || _ended) return 0;
        try
        {
            int n = _decoder.Read(buffer, offset, count);
            if (n == 0)
            {
                if (!_decoder.HasValidatedStreamEnd || _decoder.Position != _length) throw Invalid();
                _input.VerifyEnd(); _ended = true;
            }
            return n;
        }
        catch (ArchiveWriteException) { _failed = true; throw; }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or SharpCompressException or ArgumentException)
        { _failed = true; throw Invalid(ex); }
        catch { _failed = true; throw; }
    }

    public override int Read(Span<byte> buffer)
    {
        int n = Read(_readBuffer, 0, Math.Min(buffer.Length, _readBuffer.Length));
        _readBuffer.AsSpan(0, n).CopyTo(buffer); return n;
    }

    private sealed class BufferedInput(Stream source, long length, CancellationToken ct) : Stream
    {
        private readonly byte[] _buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private long _fetched; private int _offset, _count; private bool _disposed;
        internal long Consumed { get; private set; }
        private void Fill()
        {
            ObjectDisposedException.ThrowIf(_disposed, this); ct.ThrowIfCancellationRequested();
            if (_offset != _count || _fetched == length) return;
            _offset = 0; _count = source.Read(_buffer.AsSpan(0, (int)Math.Min(_buffer.Length, length - _fetched)));
            if (_count == 0) throw Invalid(); _fetched += _count;
        }
        public override int ReadByte()
        {
            Fill(); if (_offset == _count) return -1; Consumed++; return _buffer[_offset++];
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty) return 0; Fill(); int n = Math.Min(buffer.Length, _count - _offset);
            _buffer.AsSpan(_offset, n).CopyTo(buffer); _offset += n; Consumed += n; return n;
        }
        internal void VerifyEnd()
        {
            ct.ThrowIfCancellationRequested();
            if (Consumed != length || _fetched != length || _offset != _count || source.ReadByte() != -1) throw Invalid();
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !_disposed) { _disposed = true; CryptographicOperations.ZeroMemory(_buffer); } base.Dispose(disposing); }
        public override bool CanRead => !_disposed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => Consumed; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try { _decoder.Dispose(); } finally { _input.Dispose(); CryptographicOperations.ZeroMemory(_readBuffer); }
        }
        base.Dispose(disposing);
    }
    public override bool CanRead => !_disposed && !_failed; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => _length; public override long Position { get => _decoder.Position; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid(Exception? cause = null) => new(ArchiveWriteReason.InvalidArchive,
        "The LZMA coder is incomplete, damaged, has a wrong declared size or contains trailing input.", cause!);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The LZMA coder dictionary or extent exceeds this verification profile's budget.");
}
