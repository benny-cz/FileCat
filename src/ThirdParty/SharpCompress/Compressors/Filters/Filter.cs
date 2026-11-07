using System;
using System.IO;
using System.Security.Cryptography;

namespace SharpCompress.Compressors.Filters;

internal abstract partial class Filter : Stream
{
    protected bool _isEncoder;
    protected Stream _baseStream;

    private readonly byte[] _tail;
    private readonly byte[] _window;
    private int _transformed;
    private int _read;
    private bool _endReached;
    private bool _isDisposed;
    private readonly byte[]? _encodeBuffer;
    private int _encodeCount;
    private bool _encodeFailed;

    protected Filter(bool isEncoder, Stream baseStream, int lookahead)
    {
        _isEncoder = isEncoder;
        _baseStream = baseStream;
        _tail = new byte[lookahead - 1];
        _window = new byte[_tail.Length * 2];
        if (isEncoder) _encodeBuffer = new byte[(64 << 10) + _tail.Length];
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        try
        {
            // Transform returns the consumed prefix; an incomplete instruction/pair remains byte-exact at EOF.
            if (disposing && _isEncoder && !_encodeFailed && _encodeCount > 0)
                _baseStream.Write(_encodeBuffer!, 0, _encodeCount);
        }
        finally
        {
            try { if (disposing) _baseStream.Dispose(); }
            finally
            {
                CryptographicOperations.ZeroMemory(_tail); CryptographicOperations.ZeroMemory(_window);
                if (_encodeBuffer is not null) CryptographicOperations.ZeroMemory(_encodeBuffer);
                base.Dispose(disposing);
            }
        }
    }

    public override bool CanRead => !_isDisposed && !_isEncoder;

    public override bool CanSeek => false;

    public override bool CanWrite => !_isDisposed && _isEncoder && !_encodeFailed;

    public override void Flush() { if (_isEncoder) _baseStream.Flush(); }

    public override long Length => _baseStream.Length;

    public override long Position
    {
        get => _baseStream.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isEncoder) throw new NotSupportedException();
        var size = 0;

        if (_transformed > 0)
        {
            var copySize = _transformed;
            if (copySize > count)
            {
                copySize = count;
            }
            Buffer.BlockCopy(_tail, 0, buffer, offset, copySize);
            _transformed -= copySize;
            _read -= copySize;
            offset += copySize;
            count -= copySize;
            size += copySize;
            Buffer.BlockCopy(_tail, copySize, _tail, 0, _read);
        }
        if (count == 0)
        {
            return size;
        }

        var inSize = _read;
        if (inSize > count)
        {
            inSize = count;
        }
        Buffer.BlockCopy(_tail, 0, buffer, offset, inSize);
        _read -= inSize;
        Buffer.BlockCopy(_tail, inSize, _tail, 0, _read);
        while (!_endReached && inSize < count)
        {
            var baseRead = _baseStream.Read(buffer, offset + inSize, count - inSize);
            inSize += baseRead;
            if (baseRead == 0)
            {
                _endReached = true;
            }
        }
        while (!_endReached && _read < _tail.Length)
        {
            var baseRead = _baseStream.Read(_tail, _read, _tail.Length - _read);
            _read += baseRead;
            if (baseRead == 0)
            {
                _endReached = true;
            }
        }

        if (inSize > _tail.Length)
        {
            _transformed = Transform(buffer, offset, inSize);
            offset += _transformed;
            count -= _transformed;
            size += _transformed;
            inSize -= _transformed;
            _transformed = 0;
        }

        if (count == 0)
        {
            return size;
        }

        Buffer.BlockCopy(buffer, offset, _window, 0, inSize);
        Buffer.BlockCopy(_tail, 0, _window, inSize, _read);
        if (inSize + _read > _tail.Length)
        {
            _transformed = Transform(_window, 0, inSize + _read);
        }
        else
        {
            _transformed = inSize + _read;
        }
        Buffer.BlockCopy(_window, 0, buffer, offset, inSize);
        Buffer.BlockCopy(_window, inSize, _tail, 0, _read);
        size += inSize;
        _transformed -= inSize;

        return size;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!_isEncoder) throw new NotSupportedException();
        if (_encodeFailed) throw new IOException("The encoding filter was stopped after a refusal.");
        try
        {
            while (!buffer.IsEmpty)
            {
                int count = Math.Min(buffer.Length, _encodeBuffer!.Length - _encodeCount);
                buffer[..count].CopyTo(_encodeBuffer.AsSpan(_encodeCount));
                _encodeCount += count; buffer = buffer[count..];
                int consumed = Transform(_encodeBuffer, 0, _encodeCount);
                if (consumed < 0 || consumed > _encodeCount || _encodeCount - consumed > _tail.Length)
                    throw new InvalidDataException("The encoding filter returned an invalid or non-progressing prefix.");
                if (consumed > 0) _baseStream.Write(_encodeBuffer, 0, consumed);
                _encodeCount -= consumed;
                if (_encodeCount > 0) _encodeBuffer.AsSpan(consumed, _encodeCount).CopyTo(_encodeBuffer);
            }
        }
        catch { _encodeFailed = true; throw; }
    }

    protected abstract int Transform(byte[] buffer, int offset, int count);
}
