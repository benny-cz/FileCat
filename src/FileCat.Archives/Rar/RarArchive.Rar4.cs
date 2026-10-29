// Header semantics follow rardecode (github.com/nwaples/rardecode v2.4.5, archive15.go), Copyright (c) 2015
// Nicholas Waples, BSD-2-Clause; see licenses/rardecode/LICENSE. The encoded Unicode name scheme is ported from it.

using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Jobs;

namespace FileCat.Archives.Rar;

internal sealed partial class RarArchive
{
    private const byte Rar4Main = 0x73, Rar4File = 0x74, Rar4Comment = 0x75, Rar4Av = 0x76, Rar4OldService = 0x77,
        Rar4Sign = 0x79, Rar4Service = 0x7A, Rar4End = 0x7B;

    private readonly List<int> _rar4VolumeNumbers = [];
    private bool _rar4SolidArchive;   // the archive header's solid flag
    private bool _rar4FileHeaderSeen;

    private void ReadRar4Volume(Stream stream, int volume, CancellationToken ct)
    {
        long length = stream.Length;
        long position = 7;
        int volumeNumber = -1;
        try
        {
            while (position + 7 <= length)
            {
                ct.ThrowIfCancellationRequested();
                byte[] baseHeader = ReadExactly(stream, position, 7);
                ushort crc = BinaryPrimitives.ReadUInt16LittleEndian(baseHeader);
                byte type = baseHeader[2];
                ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(baseHeader.AsSpan(3));
                int size = BinaryPrimitives.ReadUInt16LittleEndian(baseHeader.AsSpan(5));
                if (type == Rar4Main && (flags & 0x0002) != 0)
                {
                    if (size < 13) throw RarData.Corrupt("RAR archive header size");
                    size = 13; // an old comment follows as its own block
                }
                else if (size < 7)
                {
                    throw RarData.Corrupt("RAR header size");
                }
                if (position + size > length) throw RarData.Corrupt("header past the end of the volume");
                byte[] header = ReadExactly(stream, position, size);
                var body = header.AsSpan(7);
                long dataSize = 0;
                if ((flags & 0x8000) != 0)
                {
                    if (body.Length < 4) throw RarData.Corrupt("RAR header size");
                    dataSize = BinaryPrimitives.ReadUInt32LittleEndian(body);
                }
                long dataStart = position + size;

                switch (type)
                {
                    case Rar4Main:
                        CheckRar4Crc(header, crc, header.Length);
                        if ((flags & 0x0080) != 0) throw new InvalidDataException(EncryptedListMessage);
                        _rar4SolidArchive = (flags & 0x0008) != 0;
                        break;
                    case Rar4File:
                    case Rar4Service:
                    {
                        var (member, large, crcLength) = ParseRar4File(header, flags);
                        CheckRar4Crc(header, crc, crcLength);
                        if (large) dataSize = member.PackedSize;
                        if (dataSize > length - dataStart) throw RarData.Corrupt("member data past the end of the volume");
                        member.PackedSize = dataSize;
                        if (type == Rar4File)
                        {
                            // As XADMaster reads RAR 1.5 data, the archive header's solid flag covers every file after
                            // the archive's first; the file header's own solid flag is RAR 2.0's (RAR's technote).
                            if (member.Version is > 0 and < 20) member.SolidContinuation = _rar4SolidArchive && _rar4FileHeaderSeen && member.Method != 0;
                            _rar4FileHeaderSeen = true;
                            if (Members.Count >= RarLimits.MaxMembers) throw new InvalidDataException("The archive has too many members to list.");
                            AddPart(member, volume, -1, dataStart, (flags & 0x0001) != 0, (flags & 0x0002) != 0);
                        }
                        break;
                    }
                    case Rar4Comment:
                        CheckRar4Crc(header, crc, Math.Min(header.Length, 13));
                        break;
                    case Rar4OldService:
                    {
                        var sub = (flags & 0x8000) != 0 ? body[4..] : body;
                        if (sub.Length < 2 || BinaryPrimitives.ReadUInt16LittleEndian(sub) != 0x101) CheckRar4Crc(header, crc, header.Length);
                        break;
                    }
                    case Rar4Av:
                    case Rar4Sign:
                        break;
                    case Rar4End:
                    {
                        CheckRar4Crc(header, crc, header.Length);
                        var end = body;
                        if ((flags & 0x0002) != 0 && end.Length >= 4) end = end[4..]; // data CRC
                        if ((flags & 0x0008) != 0 && end.Length >= 2) volumeNumber = BinaryPrimitives.ReadUInt16LittleEndian(end);
                        return;
                    }
                    default:
                        CheckRar4Crc(header, crc, header.Length);
                        break;
                }
                if (dataSize > length - dataStart) throw RarData.Corrupt("block data past the end of the volume");
                position = dataStart + dataSize;
            }
        }
        finally
        {
            while (_rar4VolumeNumbers.Count < volume) _rar4VolumeNumbers.Add(-1);
            _rar4VolumeNumbers.Add(volumeNumber);
            CheckRar4VolumeOrder(volume);
        }
    }

    /// <summary>A part continuing from the previous volume must come from the very next volume number.</summary>
    private void CheckRar4VolumeOrder(int volume)
    {
        if (volume == 0) return;
        int previous = _rar4VolumeNumbers[volume - 1], current = _rar4VolumeNumbers[volume];
        if (previous < 0 || current < 0 || current == previous + 1) return;
        _missingParts = true;
        foreach (var member in Members)
        {
            for (int i = 1; i < member.Segments.Count; i++)
            {
                if (member.Segments[i].Volume == volume && member.Segments[i - 1].Volume == volume - 1)
                    member.HasGap = true; // spliced across a missing volume: never decode it
            }
        }
    }

    private static void CheckRar4Crc(byte[] header, ushort expected, int length)
    {
        if ((ushort)Crc32.HashToUInt32(header.AsSpan(2, length - 2)) != expected) throw RarData.Corrupt("header checksum");
    }

    private static (RarMember Member, bool Large, int CrcLength) ParseRar4File(byte[] header, ushort flags)
    {
        var reader = new RarHeaderReader(header.AsSpan(7));
        long packed = (flags & 0x8000) != 0 ? reader.UInt32() : 0;
        long unpacked = reader.UInt32();
        reader.Byte(); // host OS
        uint crc = reader.UInt32();
        uint dosTime = reader.UInt32();
        int version = reader.Byte();
        int method = reader.Byte() - 0x30;
        int nameSize = reader.UInt16();
        uint attributes = reader.UInt32();
        bool large = (flags & 0x0100) != 0;
        bool unknownSize = false;
        if (large)
        {
            packed |= (long)reader.UInt32() << 32;
            long high = reader.UInt32();
            unpacked |= high << 32;
            if (high == 0xFFFFFFFF) unknownSize = true;
            if (packed < 0) throw RarData.Corrupt("member size");
        }
        else if (unpacked == 0xFFFFFFFF)
        {
            unknownSize = true;
        }
        string name = DecodeRar4Name(reader.Bytes(nameSize), (flags & 0x0200) != 0);
        if ((flags & 0x0800) != 0)
        {
            // A file version is stored as ";n" after the name.
            int semicolon = name.LastIndexOf(';');
            if (semicolon > 0 && int.TryParse(name.AsSpan(semicolon + 1), System.Globalization.NumberStyles.None, null, out _)) name = name[..semicolon];
        }
        if ((flags & 0x0400) != 0) reader.Bytes(8); // salt
        DateTime? modified = DosTime(dosTime);
        if ((flags & 0x1000) != 0) modified = ReadExtendedTime(ref reader, modified);
        int crcLength = header.Length;
        if ((flags & 0x0008) != 0) crcLength = header.Length - reader.Remaining; // a trailing old comment is not covered

        bool directory = (flags & 0x00E0) == 0x00E0;
        if (method is < 0 or > 5) throw RarData.Corrupt("compression method");
        var member = new RarMember
        {
            Name = name,
            Kind = directory ? MemberKind.Directory : MemberKind.File,
            UnpackedSize = unknownSize ? -1 : unpacked,
            PackedSize = packed,
            ModifiedUtc = modified,
            Encrypted = (flags & 0x0004) != 0,
            Method = method,
            Version = method == 0 ? 0 : version == 36 ? 29 : version,
            Dictionary = directory ? 0 : 0x10000L << ((flags & 0x00E0) >> 5),
            SolidContinuation = (flags & 0x0010) != 0 && method != 0,
            Crc32 = crc,
        };
        // A Unix symbolic link stores its target as its data.
        if (!directory && (attributes & 0xF000) == 0xA000 && member.UnpackedSize is >= 0 and < 4096) member.Kind = MemberKind.SymbolicLink;
        return (member, large, crcLength);
    }

    private static DateTime? DosTime(uint value)
    {
        int second = (int)(value & 0x1F) * 2, minute = (int)(value >> 5) & 0x3F, hour = (int)(value >> 11) & 0x1F;
        int day = (int)(value >> 16) & 0x1F, month = (int)(value >> 21) & 0x0F, year = (int)(value >> 25) + 1980;
        try { return new DateTime(year, month, day, hour, minute, Math.Min(second, 59), DateTimeKind.Unspecified); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>RAR 2.9+'s extended times: only the modification time's sub-second part matters here.</summary>
    private static DateTime? ReadExtendedTime(ref RarHeaderReader reader, DateTime? modified)
    {
        if (reader.Remaining < 2) return modified;
        int flags = reader.UInt16();
        for (int i = 0; i < 4; i++)
        {
            int n = flags >> ((3 - i) * 4);
            if ((n & 0x8) == 0) continue;
            DateTime? time = modified;
            if (i != 0)
            {
                if (reader.Remaining < 4) return modified;
                time = DosTime(reader.UInt32());
            }
            if ((n & 0x4) != 0 && time is { } t) time = t.AddSeconds(1);
            int extra = n & 0x3;
            long units = 0;
            if (extra > reader.Remaining) return modified;
            for (int j = 3 - extra; j < 3; j++) units |= (long)reader.Byte() << (j * 8);
            if (i == 0 && time is { } m) modified = m.AddTicks(units);
        }
        return modified;
    }

    /// <summary>A RAR 1.5–4.x name: plain bytes (taken as UTF-8), or, with the Unicode flag, a byte name plus RAR's compact UTF-16 encoding.</summary>
    internal static string DecodeRar4Name(ReadOnlySpan<byte> bytes, bool unicode)
    {
        if (bytes.Length > RarLimits.MaxNameBytes) throw RarData.Corrupt("name too long");
        int nul = bytes.IndexOf((byte)0);
        if (!unicode || nul < 0) return Encoding.UTF8.GetString(nul >= 0 ? bytes[..nul] : bytes);
        var plain = bytes[..nul];
        var encoded = bytes[(nul + 1)..];
        if (encoded.Length < 2) return Encoding.UTF8.GetString(plain);
        int position = 0;
        int high = encoded[position++] << 8;
        int flags = encoded[position++];
        int flagBits = 8;
        var chars = new StringBuilder(plain.Length);
        while (chars.Length < plain.Length && position < encoded.Length)
        {
            if (flagBits == 0)
            {
                flags = encoded[position++];
                flagBits = 8;
                if (position >= encoded.Length) break;
            }
            switch (flags >> 6)
            {
                case 0:
                    chars.Append((char)encoded[position++]);
                    break;
                case 1:
                    chars.Append((char)(encoded[position++] | high));
                    break;
                case 2:
                    if (encoded.Length - position < 2) { position = encoded.Length; break; }
                    chars.Append((char)BinaryPrimitives.ReadUInt16LittleEndian(encoded[position..]));
                    position += 2;
                    break;
                default:
                {
                    int n = encoded[position++];
                    int count = Math.Min((n & 0x7F) + 2, plain.Length - chars.Length);
                    if ((n & 0x80) != 0)
                    {
                        if (position >= encoded.Length) { position = encoded.Length; break; }
                        byte correction = encoded[position++];
                        for (int i = 0; i < count; i++) chars.Append((char)(((plain[chars.Length] + correction) & 0xFF) | high));
                    }
                    else
                    {
                        for (int i = 0; i < count; i++) chars.Append((char)plain[chars.Length]);
                    }
                    break;
                }
            }
            flags = (flags << 2) & 0xFF;
            flagBits -= 2;
        }
        return chars.ToString();
    }
}
