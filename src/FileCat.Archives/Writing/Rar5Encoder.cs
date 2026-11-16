using System.Numerics;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

// Original FileCat encoder. Wire facts checked against the existing BSD-2-Clause rardecode port and public RAR5
// container specification; no UnRAR source or proprietary encoder derivation. Not a clean-room claim. See
// docs/validation/archive-writing/RAR5_FEASIBILITY.md and its exact source/knowledge ledger.
internal enum Rar5Compression { Store = 0, Fastest = 1, Fast = 2, Normal = 3, Good = 4, Best = 5 }

internal sealed record Rar5EncodedContent(long Length, long PackedLength, string Sha256, uint Crc32,
    long Matches, long Comparisons, int Method);

/// <summary>
/// Fixed-memory RAR5 algorithm-version-0 encoder. Complete canonical tables and independently reset 32 KiB LZ
/// blocks fit the advertised 128 KiB dictionary. Presets change bounded hash-chain search effort, not just a header.
/// No paths, publication or deletion authority. All profiles remain unregistered pending full qualification.
/// </summary>
internal static class Rar5Encoder
{
    internal const int BlockBytes = 32 << 10;
    // 80 code-length-table bits + 430 four-bit code lengths + at most nine bits per literal, plus block framing.
    internal const int MaxPackedBlockBytes = (1800 + 9 * BlockBytes + 7) / 8 + 4;
    private const int HashSlots = 1 << 16;
    private const int MaxMatch = 4097;

    internal static Rar5EncodedContent Encode(Stream source, long length, Stream output,
        Rar5Compression compression, CancellationToken ct)
    {
        if (!Enum.IsDefined(compression) || length < 0 || length > ArchiveWritePolicy.MaxPayloadBytes ||
            !source.CanRead || !output.CanWrite || ReferenceEquals(source, output))
            throw Invalid("The RAR payload stream, length or compression preset is invalid.");
        ct.ThrowIfCancellationRequested();
        int method = length == 0 ? 0 : (int)compression;
        if (length == 0)
        {
            if (source.ReadByte() != -1) throw Changed();
            return new(0, 0, Convert.ToHexString(SHA256.HashData([])), 0, 0, 0, 0);
        }
        // Allocations have constant bounds, independent of a claimed member length or dictionary property.
        byte[] input = new byte[BlockBytes];
        byte[]? packed = method == 0 ? null : new byte[MaxPackedBlockBytes];
        int[]? heads = method == 0 ? null : new int[HashSlots];
        int[]? previous = method == 0 ? null : new int[BlockBytes];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var crc = new ArchiveCrc32();
        long remaining = length, written = 0, matches = 0, comparisons = 0;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int wanted = (int)Math.Min(BlockBytes, remaining), offset = 0;
            while (offset < wanted)
            {
                ct.ThrowIfCancellationRequested();
                int read = source.Read(input.AsSpan(offset, wanted - offset));
                if (read <= 0 || read > wanted - offset) throw Changed();
                offset += read;
            }
            var data = input.AsSpan(0, wanted);
            hash.AppendData(data); crc.Append(data);
            remaining -= wanted;
            if (method == 0)
            {
                output.Write(data); written += wanted;
            }
            else
            {
                var block = EncodeBlock(data, packed!, heads!, previous!, remaining == 0, 1 << (method - 1), ct);
                if (block.Bytes > ArchiveWritePolicy.MaxPhysicalBytes - written) throw Limit();
                output.Write(packed.AsSpan(0, block.Bytes));
                written += block.Bytes; matches += block.Matches; comparisons += block.Comparisons;
            }
        }
        ct.ThrowIfCancellationRequested();
        if (source.ReadByte() != -1) throw Changed();
        return new(length, written, Convert.ToHexString(hash.GetHashAndReset()), crc.Value, matches, comparisons, method);
    }

    private static (int Bytes, int Matches, int Comparisons) EncodeBlock(ReadOnlySpan<byte> data, byte[] output,
        int[] heads, int[] previous, bool last, int depth, CancellationToken ct)
    {
        Array.Fill(heads, -1);
        // Reserve the largest framing prefix. Payload begins at offset four, then is shifted for one-byte sizes.
        var bits = new BitWriter(output, 4);
        for (int symbol = 0; symbol < 20; symbol++) bits.Write((uint)(symbol < 12 ? 4 : 5), 4);
        for (int symbol = 0; symbol < 306; symbol++) CodeLength(ref bits, symbol < 206 ? 8 : 9);
        for (int symbol = 0; symbol < 64; symbol++) CodeLength(ref bits, 6);
        for (int symbol = 0; symbol < 16; symbol++) CodeLength(ref bits, 4);
        for (int symbol = 0; symbol < 44; symbol++) CodeLength(ref bits, symbol < 20 ? 5 : 6);
        int position = 0, matches = 0, comparisons = 0;
        int comparisonLimit = checked(4 * depth * data.Length);
        while (position < data.Length)
        {
            ct.ThrowIfCancellationRequested();
            int bestLength = 0, bestDistance = 0;
            if (position + 2 < data.Length)
            {
                int candidate = heads[Hash(data, position)], maximum = Math.Min(MaxMatch, data.Length - position);
                for (int trial = 0; candidate >= 0 && trial < depth; trial++, candidate = previous[candidate])
                {
                    int distance = position - candidate;
                    if (distance <= 0 || distance > BlockBytes) throw Invalid("The RAR match chain is invalid.");
                    int length = 0;
                    while (length < maximum)
                    {
                        if (++comparisons > comparisonLimit) throw Limit();
                        if ((comparisons & 1023) == 0) ct.ThrowIfCancellationRequested();
                        if (data[candidate + length] != data[position + length]) break;
                        length++;
                    }
                    int adjustment = (distance > 256 ? 1 : 0) + (distance > 8192 ? 1 : 0);
                    if (length > bestLength && length >= Math.Max(3, 2 + adjustment))
                    {
                        bestLength = length; bestDistance = distance;
                        if (length == maximum) break;
                    }
                }
            }
            int consumed = Math.Max(1, bestLength);
            if (bestLength != 0)
            {
                int adjusted = bestLength - (bestDistance > 256 ? 1 : 0) - (bestDistance > 8192 ? 1 : 0);
                var (slot, extra, extraBits) = LengthSlot(adjusted);
                MainSymbol(ref bits, 262 + slot); bits.Write((uint)extra, extraBits);
                (slot, extra, extraBits) = DistanceSlot(bestDistance);
                bits.Write((uint)slot, 6);
                // With this complete four-bit low-distance table the canonical code equals the raw low nibble.
                bits.Write((uint)extra, extraBits);
                matches++;
            }
            else MainSymbol(ref bits, data[position]);
            for (int at = position, end = Math.Min(position + consumed, data.Length - 2); at < end; at++)
            {
                int bucket = Hash(data, at);
                previous[at] = heads[bucket]; heads[bucket] = at;
            }
            position += consumed;
        }
        int used = ((bits.TotalBits - 1) & 7) + 1;
        int payload = bits.Finish() - 4;
        int sizeBytes = payload <= 255 ? 1 : 2;
        byte flags = (byte)(0x80 | (last ? 0x40 : 0) | ((sizeBytes - 1) << 3) | (used - 1));
        if (sizeBytes == 1) output.AsSpan(4, payload).CopyTo(output.AsSpan(3));
        output[0] = flags; output[1] = (byte)(0x5a ^ flags);
        for (int index = 0; index < sizeBytes; index++)
        {
            output[2 + index] = (byte)(payload >> (index * 8));
            output[1] ^= output[2 + index];
        }
        return (payload + sizeBytes + 2, matches, comparisons);
    }

    private static void MainSymbol(ref BitWriter writer, int symbol)
    {
        if (symbol < 206) writer.Write((uint)symbol, 8);
        else writer.Write((uint)(412 + symbol - 206), 9);
    }

    private static void CodeLength(ref BitWriter writer, int length) => writer.Write((uint)length, 4);

    private static (int Slot, int Extra, int Bits) LengthSlot(int length)
    {
        if (length is >= 2 and <= 9) return (length - 2, 0, 0);
        for (int slot = 8; slot < 44; slot++)
        {
            int count = slot / 4 - 1, start = ((4 | (slot & 3)) << count) + 2;
            if (length >= start && length < start + (1 << count)) return (slot, length - start, count);
        }
        throw Invalid("The RAR match length is out of range.");
    }

    private static (int Slot, int Extra, int Bits) DistanceSlot(int distance)
    {
        if (distance <= 4) return (distance - 1, 0, 0);
        int value = distance - 1, count = BitOperations.Log2((uint)value) - 1, odd = (value >> count) & 1;
        return (2 * (count + 1) + odd, value - ((2 | odd) << count), count);
    }

    private static int Hash(ReadOnlySpan<byte> bytes, int at) => ((bytes[at] * 251 + bytes[at + 1]) * 251 + bytes[at + 2]) & 65535;

    private ref struct BitWriter(byte[] buffer, int start)
    {
        private readonly Span<byte> _buffer = buffer;
        private int _position = start;
        private ulong _cache;
        private int _pending;
        internal int TotalBits { get; private set; }

        internal void Write(uint value, int count)
        {
            if (count < 0 || count > 32 || (ulong)value >= 1UL << count) throw Invalid("The RAR bit field is invalid.");
            TotalBits = checked(TotalBits + count);
            _cache = (_cache << count) | value; _pending += count;
            while (_pending >= 8)
            {
                _pending -= 8;
                Byte((byte)(_cache >> _pending));
                _cache &= (1UL << _pending) - 1;
            }
        }

        internal int Finish()
        {
            if (_pending > 0) Byte((byte)(_cache << (8 - _pending)));
            return _position;
        }

        private void Byte(byte value)
        {
            if (_position >= _buffer.Length) throw Limit();
            _buffer[_position++] = value;
        }
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The captured RAR input changed or ended unexpectedly.");
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The bounded RAR encoding budget was exceeded.");
}
