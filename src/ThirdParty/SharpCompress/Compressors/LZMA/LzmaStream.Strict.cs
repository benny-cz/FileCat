// FileCat strict verification entrypoint (MIT). LZMA's marked/markerless finish and LZMA2 chunk checks follow
// the pinned public-domain SDK 26.05 LzmaDec/Lzma2Dec contracts; see archive-writing validation references.
using System;
using System.Buffers;
using System.IO;

namespace SharpCompress.Compressors.LZMA;

public partial class LzmaStream
{
    private bool _strict;
    private bool _strictRangeClosed;
    private bool _strictValidated;
    private byte[]? _strictByte;

    internal bool HasValidatedStreamEnd => _strict && _strictValidated;
    internal bool FinishedWithLzmaEndMarker => HasValidatedStreamEnd && !_isLzma2 && _decoder!.HasEndMarker;

    /// <summary>Exact-size synchronous decoder for a verifier that already admitted properties and memory costs.</summary>
    internal static LzmaStream CreateStrict(byte[] properties, Stream input, long packedSize, long outputSize, bool lzma2)
    {
        if (packedSize < 0 || outputSize < 0) throw new ArgumentOutOfRangeException(nameof(packedSize));
        var value = new LzmaStream(properties, input, packedSize, outputSize, lzma2, leaveOpen: true) { _strict = true };
        value._outWindow.ClearOnReturn = true;
        value._rangeDecoder.RequireValidInput = true;
        try
        {
            if (!lzma2) value._rangeDecoder.Init(input);
            return value;
        }
        catch { value.Dispose(); throw; }
    }

    private int ReadStrict(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _ = buffer.AsSpan(offset, count);
        if (count == 0) return 0;
        if (_endReached) { ValidateStrictEnd(); return 0; }
        int total = 0;
        while (total < count)
        {
            if (_availableBytes == 0)
            {
                if (_isLzma2) DecodeStrictChunkHeader();
                else { FinishStrictRange(); _endReached = true; }
                if (_endReached) { ValidateStrictEnd(); break; }
            }
            int wanted = (int)Math.Min(count - total, _availableBytes);
            _outWindow.SetLimit(wanted);
            bool marker = false;
            if (_uncompressedChunk) _inputPosition += _outWindow.CopyStream(_inputStream, wanted);
            else marker = _decoder!.Code(_dictionarySize, _outWindow, _rangeDecoder, strict: true);
            if (marker && (_isLzma2 || _outWindow.Total != _outputSize)) throw new DataErrorException();
            int n = _outWindow.Read(buffer, offset, wanted);
            if (n <= 0) throw new DataErrorException();
            total += n; offset += n; _position += n; _availableBytes -= n;
            if (_availableBytes == 0 && !_uncompressedChunk) FinishStrictRange();
        }
        return total;
    }

    private int ReadStrictByte()
    {
        _strictByte ??= new byte[1];
        return ReadStrict(_strictByte, 0, 1) == 0 ? -1 : _strictByte[0];
    }

    private int ReadStrictMemory(Memory<byte> buffer)
    {
        byte[] copy = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length, 64 << 10));
        try { int n = ReadStrict(copy, 0, Math.Min(buffer.Length, copy.Length)); copy.AsMemory(0, n).CopyTo(buffer); return n; }
        finally { ArrayPool<byte>.Shared.Return(copy, clearArray: true); }
    }

    private void FinishStrictRange()
    {
        if (_strictRangeClosed) return;
        if (_outWindow.HasPending || _isLzma2 && _decoder!.HasEndMarker) throw new DataErrorException();
        if (!_decoder!.HasEndMarker && (!_rangeDecoder.IsFinished || _rangeDecoder._total != _rangeDecoderLimit))
        {
            // LZMA2 chunks never contain an LZMA end marker. Ordinary LZMA may carry one, or finish without
            // one when its range code is zero at exactly the declared input/output boundary.
            if (_isLzma2) throw new DataErrorException();
            _outWindow.SetLimit(1);
            if (!_decoder.Code(_dictionarySize, _outWindow, _rangeDecoder, strict: true) || !_decoder.HasEndMarker)
                throw new DataErrorException();
            if (_outWindow.AvailableBytes != 0) throw new DataErrorException();
        }
        if (!_rangeDecoder.IsFinished || _rangeDecoder._total != _rangeDecoderLimit || _outWindow.HasPending)
            throw new DataErrorException();
        _inputPosition += _rangeDecoder._total;
        _rangeDecoder.ReleaseStream(); _strictRangeClosed = true;
    }

    private int StrictChunkByte()
    {
        int value = _inputStream!.ReadByte();
        if (value < 0) throw new EndOfStreamException("The LZMA2 chunk header ends early.");
        _inputPosition++; return value;
    }

    private void DecodeStrictChunkHeader()
    {
        int control = StrictChunkByte();
        if (control == 0) { _endReached = true; return; }
        if (control >= 0xe0 || control == 1)
        {
            _needProps = true; _needDictReset = false; _outWindow.Reset();
        }
        else if (_needDictReset) throw new DataErrorException();
        if (control >= 0x80)
        {
            _uncompressedChunk = false;
            _availableBytes = ((control & 31) << 16) + (StrictChunkByte() << 8) + StrictChunkByte() + 1;
            _rangeDecoderLimit = (StrictChunkByte() << 8) + StrictChunkByte() + 1;
            if (_rangeDecoderLimit < 5) throw new DataErrorException();
            if (control >= 0xc0)
            {
                int property = StrictChunkByte();
                if (property >= 225 || property % 9 + property / 9 % 5 > 4) throw new DataErrorException();
                _needProps = false; Properties[0] = (byte)property;
                _decoder = new Decoder(); _decoder.SetDecoderProperties(Properties);
            }
            else if (_needProps) throw new DataErrorException();
            else if (control >= 0xa0) { _decoder = new Decoder(); _decoder.SetDecoderProperties(Properties); }
            if (_availableBytes > _outputSize - _position) throw new DataErrorException();
            _strictRangeClosed = false; _rangeDecoder.Init(_inputStream);
        }
        else
        {
            if (control != 1 && control != 2) throw new DataErrorException();
            _uncompressedChunk = true;
            _availableBytes = (StrictChunkByte() << 8) + StrictChunkByte() + 1;
            if (_availableBytes > _outputSize - _position) throw new DataErrorException();
        }
    }

    private void ValidateStrictEnd()
    {
        if (_availableBytes != 0 || _outWindow.HasPending || _inputPosition != _inputSize || _position != _outputSize)
            throw new DataErrorException();
        _strictValidated = true;
    }
}
