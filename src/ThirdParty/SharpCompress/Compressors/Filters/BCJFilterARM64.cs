// FileCat replacement port of LZMA SDK 26.05 C/Bra.c, BranchConv_ARM64, Igor Pavlov, Public domain.
// Exact source pin: b88c6bc09339de32f7d18bbab900686b244f42ba. Scalar managed loads, bounded spans,
// uint wraparound positions and Filter's chunk-tail handling replace the SDK's pointer/CPU-specific fast paths.
using System;
using System.Buffers.Binary;
using System.IO;

namespace SharpCompress.Compressors.Filters;

internal sealed class BCJFilterARM64(bool isEncoder, Stream stream) : Filter(isEncoder, stream, 4)
{
    private uint _position;

    protected override int Transform(byte[] buffer, int offset, int count)
    {
        int consumed = count & ~3;
        const uint flag = 1 << 20, mask = (1 << 24) - (flag << 1);
        unchecked
        {
            for (int at = 0; at < consumed; at += 4)
            {
                var instruction = buffer.AsSpan(offset + at, 4);
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(instruction), pc = _position + (uint)at;
                if (((value - 0x94000000) & 0xfc000000) == 0)
                {
                    uint delta = pc >> 2;
                    value = _isEncoder ? value + delta : value - delta;
                    BinaryPrimitives.WriteUInt32LittleEndian(instruction, (value & 0x03ffffff) | 0x94000000);
                }
                else
                {
                    value -= 0x90000000;
                    if ((value & 0x9f000000) != 0) continue;
                    value += flag;
                    if ((value & mask) != 0) continue;
                    uint address = (value & 0xffffffe0) | (value >> 26), delta = (pc >> 9) & ~7u;
                    address = _isEncoder ? address + delta : address - delta;
                    value = (value & 0x1f) | 0x90000000 | (address << 26);
                    value |= 0x00ffffe0 & ((address & ((flag << 1) - 1)) - flag);
                    BinaryPrimitives.WriteUInt32LittleEndian(instruction, value);
                }
            }
            _position += (uint)consumed;
        }
        return consumed;
    }
}
