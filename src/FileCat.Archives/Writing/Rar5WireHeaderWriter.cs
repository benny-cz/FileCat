using System.Buffers.Binary;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Bounded RAR5 header framing shared by creation and exact-metadata assembly. No paths or publication.</summary>
internal static class Rar5WireHeaderWriter
{
    internal static void Record(Stream output, ReadOnlySpan<byte> body, int kind, long? packed, int extra,
        Rar5EncryptionSession? encryption, CancellationToken ct, ulong? headerFlags = null)
    {
        ct.ThrowIfCancellationRequested();
        if (encryption?.HideNames != true) { Header(output, body, kind, packed, extra, headerFlags); return; }
        var target = encryption.CreateCipher(output, header: true, body.Length + 40L, ct);
        using (target.Cipher)
        { output.Write(target.Iv); Header(target.Cipher, body, kind, packed, extra, headerFlags); target.Cipher.Finish(); }
    }
    internal static void Header(Stream output, ReadOnlySpan<byte> body, int kind, long? packed, int extra, ulong? headerFlags = null)
    {
        ulong flags = headerFlags ?? (ulong)((packed is null ? 0 : 2) | (extra == 0 ? 0 : 1));
        if (kind is not (1 or 2 or 4 or 5) || (flags & ~7UL) != 0 || extra < 0 || extra > body.Length ||
            (flags & 1) == 0 && extra != 0 || packed is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes || (flags & 2) == 0 && packed > 0)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The RAR header fields disagree with their physical extents.");
        Span<byte> prefix = stackalloc byte[40]; int count = 7;
        count += VarInt(prefix[count..], (ulong)kind); count += VarInt(prefix[count..], flags);
        if ((flags & 1) != 0) count += VarInt(prefix[count..], (ulong)extra);
        if ((flags & 2) != 0) count += VarInt(prefix[count..], (ulong)(packed ?? 0));
        int fixedLength = count - 7, sizeValue = checked(fixedLength + body.Length);
        if (sizeValue > ArchiveWritePolicy.MaxEntryMetadataBytes) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The RAR header exceeds its bounded metadata profile.");
        Span<byte> size = stackalloc byte[3]; int sizeBytes = VarInt(size, (ulong)sizeValue);
        var crc = new ArchiveCrc32(); crc.Append(size[..sizeBytes]); crc.Append(prefix.Slice(7, fixedLength)); crc.Append(body);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, crc.Value); size[..sizeBytes].CopyTo(prefix[4..]);
        output.Write(prefix[..(4 + sizeBytes)]); output.Write(prefix.Slice(7, fixedLength)); output.Write(body);
    }
    internal static int VarInt(Span<byte> target, ulong value)
    {
        int count = 0;
        do
        {
            if (count == target.Length) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The RAR header integer exceeded its bound.");
            byte item = (byte)(value & 127); value >>= 7; target[count++] = (byte)(item | (value == 0 ? 0 : 128));
        } while (value != 0);
        return count;
    }
}
