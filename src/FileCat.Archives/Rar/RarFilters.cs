// Filter transforms ported from rardecode (github.com/nwaples/rardecode v2.4.5, filters.go),
// Copyright (c) 2015 Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. FileCat bounds every
// loop by the block length: channel counts and geometry come from the archive and are checked before use.

using System.Buffers.Binary;

namespace FileCat.Archives.Rar;

internal static class RarFilters
{
    private const int E8FileSize = 0x1000000;
    private static readonly int[] s_itaniumMask = [4, 4, 6, 6, 0, 0, 7, 7, 4, 4, 0, 0, 4, 4, 0, 0];

    /// <summary>
    /// Runs <paramref name="filter"/> over the first <paramref name="length"/> bytes of <paramref name="data"/>; returns
    /// the buffer holding the result, which is either <paramref name="data"/> or <paramref name="scratch"/>.
    /// </summary>
    public static byte[] Apply(RarFilter filter, byte[] data, int length, long fileOffset, ref byte[] scratch)
    {
        var block = data.AsSpan(0, length);
        switch (filter.Type)
        {
            case RarFilterType.E8:
                E8(block, fileOffset, 0xE8, filter.Rar5);
                return data;
            case RarFilterType.E8E9:
                E8(block, fileOffset, 0xE9, filter.Rar5);
                return data;
            case RarFilterType.Arm:
                Arm(block, fileOffset);
                return data;
            case RarFilterType.Itanium:
                Itanium(block, fileOffset);
                return data;
            case RarFilterType.Delta:
                Delta(block, Output(ref scratch, length), filter.Channels);
                return scratch;
            case RarFilterType.Rgb:
                Rgb(block, Output(ref scratch, length), filter.Width, filter.PosR);
                return scratch;
            case RarFilterType.Audio:
                Audio(block, Output(ref scratch, length), filter.Channels);
                return scratch;
            default:
                throw RarData.Unsupported("an unknown filter");
        }
    }

    private static Span<byte> Output(ref byte[] scratch, int length)
    {
        if (scratch.Length < length) scratch = new byte[length];
        return scratch.AsSpan(0, length);
    }

    private static void E8(Span<byte> block, long fileOffset, byte second, bool rar5)
    {
        int offset = unchecked((int)fileOffset);
        for (int i = 0; block.Length - i >= 5;)
        {
            byte current = block[i++];
            offset = unchecked(offset + 1);
            if (current != 0xE8 && current != second) continue;
            if (rar5) offset = (int)((uint)offset % E8FileSize);
            var field = block.Slice(i, 4);
            int address = BinaryPrimitives.ReadInt32LittleEndian(field);
            if (address < 0)
            {
                if (unchecked(address + offset) >= 0) BinaryPrimitives.WriteInt32LittleEndian(field, unchecked(address + E8FileSize));
            }
            else if (address < E8FileSize)
            {
                BinaryPrimitives.WriteInt32LittleEndian(field, unchecked(address - offset));
            }
            offset = unchecked(offset + 4);
            i += 4;
        }
    }

    private static void Arm(Span<byte> block, long fileOffset)
    {
        for (int i = 0; block.Length - i > 3; i += 4)
        {
            if (block[i + 3] != 0xEB) continue;
            uint value = block[i] | (uint)block[i + 1] << 8 | (uint)block[i + 2] << 16;
            value = unchecked(value - (uint)(((ulong)fileOffset + (ulong)i) / 4));
            block[i] = (byte)value;
            block[i + 1] = (byte)(value >> 8);
            block[i + 2] = (byte)(value >> 16);
        }
    }

    private static void Itanium(Span<byte> block, long fileOffset)
    {
        uint bundle = unchecked((uint)fileOffset) >> 4;
        for (int start = 0; block.Length - start > 21; start += 16, bundle++)
        {
            var b = block[start..];
            int template = (b[0] & 0x1F) - 0x10;
            if (template < 0) continue;
            int mask = s_itaniumMask[template];
            if (mask == 0) continue;
            for (int slot = 0; slot <= 2; slot++)
            {
                if ((mask & (1 << slot)) == 0) continue;
                int position = slot * 41 + 18;
                if (GetBits(b, position + 24, 4) != 5) continue;
                uint target = unchecked(GetBits(b, position, 20) - bundle);
                SetBits(b, position, 20, target);
            }
        }
    }

    private static uint GetBits(Span<byte> b, int position, int count)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(position / 8, 4)) >> (position & 7);
        return value & (uint.MaxValue >> (32 - count));
    }

    private static void SetBits(Span<byte> b, int position, int count, uint bits)
    {
        uint mask = (uint.MaxValue >> (32 - count)) << (position & 7);
        var field = b.Slice(position / 8, 4);
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(field);
        value = (value & ~mask) | ((bits << (position & 7)) & mask);
        BinaryPrimitives.WriteUInt32LittleEndian(field, value);
    }

    private static void Delta(ReadOnlySpan<byte> input, Span<byte> output, int channels)
    {
        int used = Math.Min(channels, input.Length);
        int source = 0;
        for (int channel = 0; channel < used; channel++)
        {
            byte value = 0;
            for (int i = channel; i < output.Length; i += channels)
            {
                value = unchecked((byte)(value - input[source++]));
                output[i] = value;
            }
        }
    }

    private static void Rgb(ReadOnlySpan<byte> input, Span<byte> output, int width, int posR)
    {
        int source = 0;
        for (int color = 0; color < 3; color++)
        {
            int previous = 0;
            for (int i = color; i < output.Length; i += 3)
            {
                int predicted;
                int upper = i - width;
                if (upper >= 3)
                {
                    int upperByte = output[upper];
                    int upperLeft = output[upper - 3];
                    predicted = previous + upperByte - upperLeft;
                    int pa = Math.Abs(predicted - previous);
                    int pb = Math.Abs(predicted - upperByte);
                    int pc = Math.Abs(predicted - upperLeft);
                    predicted = pa <= pb && pa <= pc ? previous : pb <= pc ? upperByte : upperLeft;
                }
                else
                {
                    predicted = previous;
                }
                previous = (predicted - input[source++]) & 0xFF;
                output[i] = (byte)previous;
            }
        }
        for (int i = posR; i < output.Length - 2; i += 3)
        {
            byte green = output[i + 1];
            output[i] = unchecked((byte)(output[i] + green));
            output[i + 2] = unchecked((byte)(output[i + 2] + green));
        }
    }

    private static void Audio(ReadOnlySpan<byte> input, Span<byte> output, int channels)
    {
        int used = Math.Min(channels, input.Length);
        int source = 0;
        Span<int> diff = stackalloc int[7];
        Span<int> d = stackalloc int[3];
        Span<int> k = stackalloc int[3];
        for (int channel = 0; channel < used; channel++)
        {
            int previous = 0;
            int byteCount = 0;
            diff.Clear();
            d.Clear();
            k.Clear();
            for (int i = channel; i < output.Length; i += channels)
            {
                int predicted = (previous << 3) + k[0] * d[0] + k[1] * d[1] + k[2] * d[2];
                predicted = unchecked((sbyte)(predicted >> 3));
                int current = unchecked((sbyte)input[source++]);
                predicted -= current;
                output[i] = unchecked((byte)predicted);

                int dd = current << 3;
                diff[0] += Math.Abs(dd);
                diff[1] += Math.Abs(dd - d[0]);
                diff[2] += Math.Abs(dd + d[0]);
                diff[3] += Math.Abs(dd - d[1]);
                diff[4] += Math.Abs(dd + d[1]);
                diff[5] += Math.Abs(dd - d[2]);
                diff[6] += Math.Abs(dd + d[2]);

                int delta = unchecked((sbyte)(byte)(predicted - previous));
                previous = predicted;
                d[2] = d[1];
                d[1] = delta - d[0];
                d[0] = delta;

                if ((byteCount & 0x1F) == 0)
                {
                    int min = diff[0];
                    int best = 0;
                    diff[0] = 0;
                    for (int j = 1; j < diff.Length; j++)
                    {
                        if (diff[j] < min)
                        {
                            min = diff[j];
                            best = j;
                        }
                        diff[j] = 0;
                    }
                    best--;
                    if (best >= 0)
                    {
                        int m = best / 2;
                        if (best % 2 == 0) { if (k[m] >= -16) k[m]--; }
                        else if (k[m] < 16) k[m]++;
                    }
                }
                byteCount++;
            }
        }
    }
}
