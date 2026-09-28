using System.Buffers.Binary;

namespace FileCat.Recovery;

/// <summary>
/// LZNT1, NTFS's compression ([MS-XCA] 2.5): a compression unit is a series of chunks, each a two-byte header (size in
/// bits 0–11 as length minus 3, bit 15 set when compressed) and up to 4096 bytes of output. Compressed chunks mix literal
/// bytes and back-references whose offset and length split depends on how far into the chunk the output is.
/// </summary>
public static class Lznt1
{
    private const int ChunkSize = 4096;

    /// <summary>
    /// Decompresses <paramref name="input"/> into <paramref name="output"/>; returns the bytes produced. Damaged input
    /// (a reference before the chunk's start) throws <see cref="InvalidDataException"/>; nothing reads or writes out of bounds.
    /// </summary>
    public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int inPos = 0, outPos = 0;
        while (inPos + 2 <= input.Length && outPos < output.Length)
        {
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(input[inPos..]);
            if (header == 0) break; // the end of the unit's data
            int chunkEnd = Math.Min(inPos + (header & 0x0FFF) + 3, input.Length);
            inPos += 2;
            int chunkStart = outPos;
            if ((header & 0x8000) == 0)
            {
                // Stored uncompressed: the chunk is its own bytes.
                int n = Math.Min(Math.Min(chunkEnd - inPos, ChunkSize), output.Length - outPos);
                input.Slice(inPos, n).CopyTo(output[outPos..]);
                outPos += n;
                inPos = chunkEnd;
                continue;
            }
            while (inPos < chunkEnd && outPos < output.Length && outPos - chunkStart < ChunkSize)
            {
                byte flags = input[inPos++];
                for (int bit = 0; bit < 8 && inPos < chunkEnd && outPos < output.Length && outPos - chunkStart < ChunkSize; bit++)
                {
                    if ((flags & 1 << bit) == 0)
                    {
                        output[outPos++] = input[inPos++];
                        continue;
                    }
                    if (inPos + 2 > chunkEnd) throw new InvalidDataException("A compressed chunk ends inside a back-reference.");
                    ushort token = BinaryPrimitives.ReadUInt16LittleEndian(input[inPos..]);
                    inPos += 2;
                    int position = outPos - chunkStart;
                    int lengthBits = 12;
                    for (int i = position - 1; i >= 0x10; i >>= 1) lengthBits--;
                    int offset = (token >> lengthBits) + 1;
                    int length = (token & ((1 << lengthBits) - 1)) + 3;
                    if (offset > position) throw new InvalidDataException("A back-reference points before the start of its chunk.");
                    // Byte by byte: a reference may overlap what it produces (a repeated run).
                    for (int k = 0; k < length && outPos < output.Length && outPos - chunkStart < ChunkSize; k++, outPos++)
                        output[outPos] = output[outPos - offset];
                }
            }
            inPos = chunkEnd;
        }
        return outPos;
    }
}
