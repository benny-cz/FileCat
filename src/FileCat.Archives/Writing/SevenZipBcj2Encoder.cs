// Bounded stream adaptation of Bcj2Enc.c/Bcj2.h from LZMA SDK 26.05,
// commit b88c6bc09339de32f7d18bbab900686b244f42ba. Igor Pavlov: Public domain.
// Exact source identities/individual notices: eng/validation/archive-writing/sevenzip-feasibility/ppmd-filter-sources.json.
using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record SevenZipBcj2Content(long Length, string Sha256, uint Crc32,
    IReadOnlyList<SevenZipPackedStream> Streams, long Markers, long Calls, long Jumps);

/// <summary>
/// Four caller-owned, stream-only BCJ2 outputs: main, calls, jumps, range decisions. No paths, scratch allocation,
/// seeking, publication or container registration. Each fixed buffer fits the numeric proxy's read/write ceiling.
/// Exact file length limits branch conversion; all source bytes are hashed and incomplete instructions are retained.
/// </summary>
internal static class SevenZipBcj2Encoder
{
    private const uint RelativeLimit = 0x0f000000; // The pinned SDK's actual default, not a generic branch threshold.

    internal static SevenZipBcj2Content Encode(Stream source, long length, IReadOnlyList<Stream> outputs, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(outputs);
        if (!source.CanRead || length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes || outputs.Count != 4)
            throw Invalid();
        Stream[] targets = outputs.ToArray();
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] is not { CanWrite: true } || ReferenceEquals(source, targets[i]) ||
                targets.Take(i).Any(previous => ReferenceEquals(previous, targets[i]))) throw Invalid();
        ct.ThrowIfCancellationRequested();
        using var input = new Input(source, length, ct);
        using var main = new Sink(targets[0], length, ct);
        using var calls = new Sink(targets[1], length, ct);
        using var jumps = new Sink(targets[2], length, ct);
        // At most one normalization per marker (probabilities remain 31..2017), followed by five flush shifts.
        using var decisions = new Sink(targets[3], checked(length + 5), ct);
        var range = new RangeEncoder(decisions);
        byte previous = 0; long markers = 0, callCount = 0, jumpCount = 0;
        Span<byte> address = stackalloc byte[4];
        while (input.Ensure(1))
        {
            if ((input.Consumed & 4095) == 0) ct.ThrowIfCancellationRequested();
            byte value = input.TakeByte(); main.Put(value);
            bool marker = value is 0xe8 or 0xe9 || previous == 0x0f && (value & 0xf0) == 0x80;
            if (!marker) { previous = value; continue; }
            markers++;
            int model = value == 0xe8 ? previous + 2 : value == 0xe9 ? 1 : 0;
            bool convert = false; uint relative = 0; long target = 0;
            if (input.Ensure(4))
            {
                relative = BinaryPrimitives.ReadUInt32LittleEndian(input.PeekFour());
                target = input.Consumed + 4 + unchecked((int)relative);
                convert = input.Consumed > (value is 0xe8 or 0xe9 ? 0 : 1) && target >= 0 && target < length &&
                    (unchecked(relative + RelativeLimit) >> 1) < RelativeLimit;
            }
            range.Bit(model, convert);
            if (!convert) { previous = value; continue; }
            previous = (byte)(relative >> 24); input.SkipFour();
            BinaryPrimitives.WriteUInt32BigEndian(address, unchecked((uint)target));
            if (value == 0xe8) { calls.Write(address); callCount++; }
            else { jumps.Write(address); jumpCount++; }
        }
        input.End(); range.Finish();
        main.Finish(); calls.Finish(); jumps.Finish(); decisions.Finish();
        if (main.Bytes + calls.Bytes + jumps.Bytes != length || (calls.Bytes & 3) != 0 || (jumps.Bytes & 3) != 0)
            throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The BCJ2 split changed its byte extents.");
        ct.ThrowIfCancellationRequested();
        return new(length, input.Digest(), input.Crc32,
            Array.AsReadOnly(new[] { main.Record(), calls.Record(), jumps.Record(), decisions.Record() }), markers, callCount, jumpCount);
    }

    private sealed class Input(Stream source, long length, CancellationToken ct) : IDisposable
    {
        private readonly byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes + 4];
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private ArchiveCrc32 crc;
        private long read;
        private int start, count;
        internal long Consumed { get; private set; }
        internal uint Crc32 => crc.Value;
        internal bool Ensure(int wanted)
        {
            while (count < wanted && read < length)
            {
                ct.ThrowIfCancellationRequested();
                if (start != 0)
                {
                    int end = start + count, clear = Math.Max(start, count);
                    buffer.AsSpan(start, count).CopyTo(buffer);
                    if (end > clear) buffer.AsSpan(clear, end - clear).Clear();
                    start = 0;
                }
                int maximum = (int)Math.Min(Math.Min(ArchiveIoAuthority.MaximumReadBytes, buffer.Length - count), length - read);
                int got = source.Read(buffer.AsSpan(count, maximum));
                if (got <= 0 || got > maximum) throw Changed();
                hash.AppendData(buffer, count, got); crc.Append(buffer.AsSpan(count, got));
                count += got; read += got;
            }
            return count >= wanted;
        }
        internal byte TakeByte()
        {
            byte result = buffer[start]; buffer[start++] = 0; count--; Consumed++; return result;
        }
        internal ReadOnlySpan<byte> PeekFour() => buffer.AsSpan(start, 4);
        internal void SkipFour() { buffer.AsSpan(start, 4).Clear(); start += 4; count -= 4; Consumed += 4; }
        internal void End()
        {
            ct.ThrowIfCancellationRequested();
            if (Consumed != length || read != length || count != 0) throw Changed();
            Span<byte> extra = stackalloc byte[1];
            try { if (source.Read(extra) != 0) throw Changed(); }
            finally { CryptographicOperations.ZeroMemory(extra); }
        }
        internal string Digest() => Convert.ToHexString(hash.GetHashAndReset());
        public void Dispose() { hash.Dispose(); CryptographicOperations.ZeroMemory(buffer); }
    }

    private sealed class Sink(Stream output, long maximum, CancellationToken ct) : IDisposable
    {
        private readonly byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private int count;
        private ArchiveCrc32 crc;
        internal long Bytes { get; private set; }
        internal void Put(byte value)
        {
            if (Bytes == maximum) throw Limit();
            buffer[count++] = value; Bytes++;
            if (count == buffer.Length) Finish();
        }
        internal void Write(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > maximum - Bytes) throw Limit();
            foreach (byte value in bytes) Put(value);
        }
        internal void Finish()
        {
            ct.ThrowIfCancellationRequested();
            if (count == 0) return;
            output.Write(buffer.AsSpan(0, count)); crc.Append(buffer.AsSpan(0, count));
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, count)); count = 0;
        }
        internal SevenZipPackedStream Record() => new(Bytes, crc.Value);
        public void Dispose() => CryptographicOperations.ZeroMemory(buffer); // Never finalize a failed stream.
    }

    private sealed class RangeEncoder(Sink output)
    {
        private const uint Top = 1 << 24;
        private readonly ushort[] probabilities = Enumerable.Repeat((ushort)1024, 258).ToArray();
        private uint range = uint.MaxValue;
        private ulong low, cacheSize = 1;
        private byte cache;
        internal void Bit(int model, bool one)
        {
            Normalize();
            uint probability = probabilities[model], bound = (range >> 11) * probability;
            if (one)
            {
                low += bound; range -= bound; probabilities[model] = (ushort)(probability - (probability >> 5));
            }
            else
            {
                range = bound; probabilities[model] = (ushort)(probability + ((2048 - probability) >> 5));
            }
        }
        private void Normalize() { if (range < Top) { ShiftLow(); range <<= 8; } }
        internal void Finish() { Normalize(); for (int i = 0; i < 5; i++) ShiftLow(); }
        private void ShiftLow()
        {
            uint bottom = unchecked((uint)low), carry = (uint)(low >> 32);
            if (bottom < 0xff000000 || carry != 0)
            {
                byte pending = cache;
                do { output.Put(unchecked((byte)(pending + carry))); pending = 0xff; } while (--cacheSize != 0);
                cache = (byte)(bottom >> 24);
            }
            cacheSize++; low = unchecked(bottom << 8);
        }
    }

    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The BCJ2 source or four output grants are invalid.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The captured BCJ2 source changed or ended unexpectedly.");
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The BCJ2 output exceeded its derived stream budget.");
}
