using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;

namespace SharpCompress.Compressors.LZMA;

internal class Bcj2DecoderStream : DecoderStream2
{
    private const int K_NUM_TOP_BITS = 24;
    private const uint K_TOP_VALUE = (1 << K_NUM_TOP_BITS);

    private class RangeDecoder
    {
        internal readonly Stream _mStream;
        internal uint _range;
        internal uint _code;

        private readonly bool _leaveOpen;
        public RangeDecoder(Stream stream, bool strict, bool leaveOpen)
        {
            _mStream = stream;
            _leaveOpen = leaveOpen;
            _range = 0xFFFFFFFF;
            for (var i = 0; i < 5; i++)
            {
                byte value = ReadByte();
                if (strict && i == 0 && value != 0) throw new InvalidDataException("The BCJ2 range prefix is invalid.");
                _code = (_code << 8) | value;
            }
            if (strict && _code == uint.MaxValue) throw new InvalidDataException("The BCJ2 range prefix is invalid.");
        }

        public byte ReadByte()
        {
            var bt = _mStream.ReadByte();
            if (bt < 0)
            {
                throw new IncompleteArchiveException("Unexpected end of stream.");
            }

            return (byte)bt;
        }

        public void Dispose() { _range = _code = 0; if (!_leaveOpen) _mStream.Dispose(); }
    }

    private class StatusDecoder
    {
        private const int NUM_MOVE_BITS = 5;

        private const int K_NUM_BIT_MODEL_TOTAL_BITS = 11;
        private const uint K_BIT_MODEL_TOTAL = 1u << K_NUM_BIT_MODEL_TOTAL_BITS;

        private uint _prob;

        public StatusDecoder() => _prob = K_BIT_MODEL_TOTAL / 2;

        public uint Decode(RangeDecoder decoder)
        {
            var newBound = (decoder._range >> K_NUM_BIT_MODEL_TOTAL_BITS) * _prob;
            if (decoder._code < newBound)
            {
                decoder._range = newBound;
                _prob += (K_BIT_MODEL_TOTAL - _prob) >> NUM_MOVE_BITS;
                if (decoder._range < K_TOP_VALUE)
                {
                    decoder._code = (decoder._code << 8) | decoder.ReadByte();
                    decoder._range <<= 8;
                }
                return 0;
            }
            decoder._range -= newBound;
            decoder._code -= newBound;
            _prob -= _prob >> NUM_MOVE_BITS;
            if (decoder._range < K_TOP_VALUE)
            {
                decoder._code = (decoder._code << 8) | decoder.ReadByte();
                decoder._range <<= 8;
            }
            return 1;
        }
    }

    private readonly Stream _mMainStream;
    private readonly Stream _mCallStream;
    private readonly Stream _mJumpStream;
    private readonly RangeDecoder _mRangeDecoder;
    private readonly StatusDecoder[] _mStatusDecoder;
    private long _mWritten;
    private readonly IEnumerator<byte> _mIter;
    private bool _mFinished;
    private bool _isDisposed;
    private bool _failed;
    private readonly long? _strictLength;
    private readonly bool _leaveOpen;
    private readonly CancellationToken _ct;

    public Bcj2DecoderStream(Stream[] streams, long? outputLength = null, bool leaveOpen = false, CancellationToken ct = default)
    {
        if (streams.Length != 4)
        {
            throw new NotSupportedException();
        }
        if (outputLength < 0) throw new InvalidDataException("The BCJ2 output extent is invalid.");
        _strictLength = outputLength; _leaveOpen = leaveOpen; _ct = ct; ct.ThrowIfCancellationRequested();

        _mMainStream = streams[0];
        _mCallStream = streams[1];
        _mJumpStream = streams[2];
        try
        {
            _mRangeDecoder = new RangeDecoder(streams[3], outputLength is not null, leaveOpen);
            _mStatusDecoder = new StatusDecoder[256 + 2];
            for (var i = 0; i < _mStatusDecoder.Length; i++)
            {
                _mStatusDecoder[i] = new StatusDecoder();
            }

            _mIter = Run().GetEnumerator();
        }
        catch
        {
            // Construction has accepted ownership. A short/failing range stream must also release every
            // branch/decryptor already created, retaining the original construction error if cleanup fails.
            foreach (Stream stream in leaveOpen ? Array.Empty<Stream>() : streams)
            {
                try { stream.Dispose(); } catch { }
            }
            throw;
        }
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
            if (disposing)
            {
                try { _mIter.Dispose(); }
                finally
                {
                    try { if (!_leaveOpen) _mMainStream.Dispose(); }
                    finally
                    {
                        try { if (!_leaveOpen) _mCallStream.Dispose(); }
                        finally { try { if (!_leaveOpen) _mJumpStream.Dispose(); } finally { _mRangeDecoder.Dispose(); } }
                    }
                }
            }
        }
        finally { base.Dispose(disposing); }
    }

    private static bool IsJcc(byte b0, byte b1) => b0 == 0x0F && (b1 & 0xF0) == 0x80;

    private static bool IsJ(byte b0, byte b1) => (b1 & 0xFE) == 0xE8 || IsJcc(b0, b1);

    private static int GetIndex(byte b0, byte b1)
    {
        if (b1 == 0xE8)
        {
            return b0;
        }
        if (b1 == 0xE9)
        {
            return 256;
        }
        return 257;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this); _ct.ThrowIfCancellationRequested();
        if (_failed) throw new InvalidDataException("The BCJ2 decoder has failed.");
        if (buffer.IsEmpty || _mFinished)
        {
            return 0;
        }

        try
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (!_mIter.MoveNext()) { _mFinished = true; return i; }
                buffer[i] = _mIter.Current;
            }
            return buffer.Length;
        }
        catch { _failed = true; throw; }
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Bcj2DecoderStream uses complex state machine with multiple streams
        return Task.FromResult(Read(buffer, offset, count));
    }

    public override int ReadByte()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this); _ct.ThrowIfCancellationRequested();
        if (_failed) throw new InvalidDataException("The BCJ2 decoder has failed.");
        if (_mFinished)
        {
            return -1;
        }

        try
        {
            if (!_mIter.MoveNext()) { _mFinished = true; return -1; }
            return _mIter.Current;
        }
        catch { _failed = true; throw; }
    }

    private void RecordByte()
    {
        if (_strictLength is { } length && _mWritten >= length) throw new InvalidDataException("The BCJ2 output exceeds its declared size.");
        if ((_mWritten & 255) == 0) _ct.ThrowIfCancellationRequested(); _mWritten++;
    }

    public IEnumerable<byte> Run()
    {
        const uint kBurstSize = (1u << 18);

        byte prevByte = 0;
        uint processedBytes = 0;
        for (; ; )
        {
            byte b = 0;
            uint i;
            for (i = 0; i < kBurstSize; i++)
            {
                var tmp = _mMainStream.ReadByte();
                if (tmp < 0)
                {
                    if (_strictLength is { } length && (_mWritten != length || _mCallStream.ReadByte() != -1 ||
                        _mJumpStream.ReadByte() != -1 || _mRangeDecoder._code != 0 || _mRangeDecoder._mStream.ReadByte() != -1))
                        throw new InvalidDataException("The BCJ2 coder has incomplete output, unfinished range state or trailing branches.");
                    yield break;
                }

                b = (byte)tmp;
                RecordByte();
                yield return b;
                if (IsJ(prevByte, b))
                {
                    break;
                }

                prevByte = b;
            }

            processedBytes += i;
            if (i == kBurstSize)
            {
                continue;
            }

            if (_mStatusDecoder[GetIndex(prevByte, b)].Decode(_mRangeDecoder) == 1)
            {
                var s = (b == 0xE8) ? _mCallStream : _mJumpStream;

                uint src = 0;
                for (i = 0; i < 4; i++)
                {
                    var b0 = s.ReadByte();
                    if (b0 < 0)
                    {
                        throw new IncompleteArchiveException("Unexpected end of stream.");
                    }

                    src <<= 8;
                    src |= (uint)b0;
                }

                var dest = src - (uint)(_mWritten + 4);
                RecordByte();
                yield return (byte)dest;
                RecordByte();
                yield return (byte)(dest >> 8);
                RecordByte();
                yield return (byte)(dest >> 16);
                RecordByte();
                yield return (byte)(dest >> 24);
                prevByte = (byte)(dest >> 24);
                processedBytes += 4;
            }
            else
            {
                prevByte = b;
            }
        }
    }
}
