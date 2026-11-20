// FileCat addition: encoding through the existing bounded BSD-2-Clause rardecode PPMd-H model, with the 7z range
// encoding grammar checked against LZMA SDK 26.05 C/Ppmd7Enc.c (Igor Pavlov, Public domain; based on Dmitry Shkarin's
// public-domain PPMdH). No UnRAR-derived model is restored. See licenses/rardecode and the FileCat source ledger.
using System;
using System.IO;
using System.Threading;

namespace SharpCompress.Compressors.PPMd;

/// <summary>Bounded managed PPMd7/7z encoding. Model and stream are local to one independently encoded member.</summary>
internal sealed class Ppmd7zEncoderStream : Stream
{
    private readonly Stream _output;
    private readonly CancellationToken _ct;
    private readonly PpmdModelH<Ppmd7zEncodingRange> _model = new();
    private readonly Ppmd7zEncodingState _range;
    private bool _disposed, _failed;

    internal Ppmd7zEncoderStream(Stream output, int order, int memoryBytes, CancellationToken ct)
    {
        if (!output.CanWrite || order is < 2 or > 64 || memoryBytes < 1 << 11 || memoryBytes > 32 << 20)
            throw new ArgumentException("PPMd7 requires bounded model memory/order and a writable stream.");
        ct.ThrowIfCancellationRequested(); _output = output; _ct = ct;
        _range = new(output, ct); _model.Init(order, memoryBytes);
        _model.Coder = new(_range); _model.EncodingHints = _range;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new IOException("The PPMd7 encoder was stopped after a refusal.");
        try
        {
            for (int index = 0; index < buffer.Length; index++)
            {
                if ((index & 255) == 0) _ct.ThrowIfCancellationRequested();
                _model.EncodeSymbol(buffer[index]);
            }
        }
        catch { _failed = true; throw; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try { if (!_failed) _range.Finish(); } // known size: normal range finalization, no end mark
            finally { _model.Clear(); }
        }
        base.Dispose(disposing);
    }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed && !_failed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _output.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal readonly struct Ppmd7zEncodingRange(Ppmd7zEncodingState state) : IPpmdRangeDecoder
{
    public uint GetThreshold(uint total) => state.Scale(total);
    public void Decode(uint start, uint size) => state.Commit(start, size);
    public uint DecodeBit(uint size0, int totalBits) => state.CommitBit(size0, totalBits);
}

/// <summary>Encoding interval sink plus symbol-selection hints; reference state avoids boxing mutable coders.</summary>
internal sealed class Ppmd7zEncodingState(Stream output, CancellationToken ct) : IPpmdEncodingHints
{
    private const uint Top = 1u << 24;
    private ulong _low, _cacheSize = 1;
    private uint _range = uint.MaxValue, _selected, _bit;
    private byte _cache;
    public int Symbol { get; set; }
    public void SelectThreshold(uint count) => _selected = count;
    public void SelectBit(uint bit) => _bit = bit;

    internal uint Scale(uint total)
    {
        if (total == 0 || _range < total || _selected >= total) throw PpmdModelH.Damaged("PPMd encoding interval");
        _range /= total; return _selected;
    }
    internal void Commit(uint start, uint size)
    {
        ulong range = (ulong)_range * size;
        if (range == 0 || range > uint.MaxValue) throw PpmdModelH.Damaged("PPMd encoding interval");
        _low += (ulong)start * _range; _range = (uint)range; Normalize();
    }
    internal uint CommitBit(uint size0, int bits)
    {
        if (bits is < 1 or > 24 || size0 == 0 || size0 >= 1u << bits || _bit > 1)
            throw PpmdModelH.Damaged("PPMd encoding bit interval");
        uint bound = (_range >> bits) * size0;
        if (bound == 0 || bound >= _range) throw PpmdModelH.Damaged("PPMd encoding bit interval");
        if (_bit == 0) _range = bound;
        else { _low += bound; _range -= bound; }
        Normalize(); return _bit;
    }
    internal void Finish() { ct.ThrowIfCancellationRequested(); for (int index = 0; index < 5; index++) ShiftLow(); }
    private void Normalize()
    {
        if (_range == 0) throw PpmdModelH.Damaged("PPMd encoding range");
        while (_range < Top) { _range <<= 8; ShiftLow(); }
    }
    private void ShiftLow()
    {
        ct.ThrowIfCancellationRequested();
        uint low = (uint)_low, carry = (uint)(_low >> 32);
        if (carry > 1 || _cacheSize == 0 || _cacheSize > 80UL << 30) throw PpmdModelH.Damaged("PPMd encoding carry");
        if (low < 0xff000000 || carry != 0)
        {
            byte value = _cache;
            do { output.WriteByte(unchecked((byte)(value + carry))); value = 255; } while (--_cacheSize != 0);
            _cache = (byte)(low >> 24);
        }
        _cacheSize++; _low = unchecked(low << 8);
    }
}
