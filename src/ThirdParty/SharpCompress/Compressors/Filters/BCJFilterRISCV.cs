// FileCat replacement scalar port of LZMA SDK 26.05 C/Bra.c, RISCV encode/decode, Igor Pavlov, Public domain.
// Exact source pin: b88c6bc09339de32f7d18bbab900686b244f42ba. Bounded managed loads and two-byte scanning
// replace pointer/CPU fast paths; Filter preserves the incomplete trailing instruction across writes and at EOF.
using System;
using System.Buffers.Binary;
using System.IO;

namespace SharpCompress.Compressors.Filters;

internal sealed class BCJFilterRISCV(bool isEncoder, Stream stream) : Filter(isEncoder, stream, 8)
{
    private uint _position;

    protected override int Transform(byte[] buffer, int offset, int count)
    {
        int aligned = count & ~1;
        if (aligned <= 6) return 0;
        int at = 0, limit = aligned - 6;
        unchecked
        {
            while (at < limit)
            {
                Span<byte> instruction = buffer.AsSpan(offset + at, 8);
                uint scan = (BinaryPrimitives.ReadUInt16LittleEndian(instruction) ^ 0x10u) + 1;
                if ((scan & 0x77) != 0) { at += 2; continue; }
                uint value = BinaryPrimitives.ReadUInt32LittleEndian(instruction), pc = _position + (uint)at;
                if ((scan & 8) == 0)
                {
                    if (_isEncoder)
                    {
                        if (((scan - 0x100) & 0xd80) != 0) { at += 2; continue; }
                        uint address = ((value & (1u << 31)) >> 11) | ((value & (0x3ffu << 21)) >> 20) |
                            ((value & (1u << 20)) >> 9) | (value & (0xffu << 12));
                        address += pc;
                        instruction[1] = (byte)(((address >> 13) & 0xf0) | ((value >> 8) & 15));
                        instruction[2] = (byte)(address >> 9); instruction[3] = (byte)(address >> 1);
                    }
                    else
                    {
                        scan -= 0x81;
                        if ((scan & 0xd80) != 0) { at += 2; continue; }
                        uint original = (scan + 0x70) & 0xfff;
                        uint address = ((uint)instruction[3] << 1) | ((uint)instruction[2] << 9) | ((scan & 0xf000) << 5);
                        address -= pc;
                        value = original | ((address << 11) & (1u << 31)) | ((address << 20) & (0x3ffu << 21)) |
                            ((address << 9) & (1u << 20)) | (address & (0xffu << 12));
                        BinaryPrimitives.WriteUInt32LittleEndian(instruction, value);
                    }
                    at += 4; continue;
                }
                if (_isEncoder)
                {
                    uint next = BinaryPrimitives.ReadUInt32LittleEndian(instruction[4..]);
                    if ((scan & 0xe80) != 0)
                    {
                        if (!Pair(scan, next)) { at += 6; continue; }
                        BinaryPrimitives.WriteUInt32LittleEndian(instruction, (next << 12) | 0x117);
                        uint address = (value & 0xfffff000) + (uint)((int)next >> 20) + pc;
                        BinaryPrimitives.WriteUInt32BigEndian(instruction[4..], address);
                    }
                    else
                    {
                        uint register = value >> 27;
                        if (!Special(scan, register)) { at += 4; continue; }
                        uint first = (register << 7) + 0x17 + (next & 0xfffff000);
                        uint second = (value >> 12) | (next << 20);
                        BinaryPrimitives.WriteUInt32LittleEndian(instruction, first);
                        BinaryPrimitives.WriteUInt32LittleEndian(instruction[4..], second);
                    }
                }
                else if ((scan & 0xe80) == 0)
                {
                    uint register = value >> 27;
                    if (!Special(scan, register)) { at += 4; continue; }
                    uint address = BinaryPrimitives.ReadUInt32BigEndian(instruction[4..]) - pc;
                    uint second = (value >> 12) | (address << 20);
                    uint first = (register << 7) + 0x17 + ((address + 0x800) & 0xfffff000);
                    BinaryPrimitives.WriteUInt32LittleEndian(instruction, first);
                    BinaryPrimitives.WriteUInt32LittleEndian(instruction[4..], second);
                }
                else
                {
                    uint next = BinaryPrimitives.ReadUInt32LittleEndian(instruction[4..]);
                    if (!Pair(scan, next)) { at += 6; continue; }
                    uint second = (value & 0xfffff000) | (next >> 20);
                    uint first = (next << 12) | 0x117;
                    BinaryPrimitives.WriteUInt32LittleEndian(instruction, first);
                    BinaryPrimitives.WriteUInt32LittleEndian(instruction[4..], second);
                }
                at += 8;
            }
            _position += (uint)at;
        }
        return at;
    }

    private static bool Pair(uint first, uint second) => unchecked((((second - 3) ^ (first << 8)) & 0xf8003) == 0);
    private static bool Special(uint instruction, uint register) => unchecked(((instruction - 0x3108) << 18) < (register & 0x1d));
}
