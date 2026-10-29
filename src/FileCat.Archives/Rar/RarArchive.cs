using FileCat.Core.Jobs;

namespace FileCat.Archives.Rar;

/// <summary>
/// The members of a RAR archive or volume set, read from its headers only. Every header is checksummed, bounded and
/// checked against the volume's length; damage after some members ends the list with a warning instead of failing it.
/// </summary>
internal sealed partial class RarArchive
{
    public const string EncryptedListMessage = "The archive's file list is encrypted; FileCat does not ask for archive passwords.";

    private static ReadOnlySpan<byte> Rar5Signature => "Rar!\x1A\x07\x01\x00"u8;
    private static ReadOnlySpan<byte> Rar4Signature => "Rar!\x1A\x07\x00"u8;

    public List<RarMember> Members { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool IsRar5 { get; private set; }
    public bool Solid => Members.Exists(m => m.SolidContinuation);

    private RarMember? _split;          // a member continuing in the next volume
    private int _splitVolumeNumber = -1;
    private bool _missingParts;

    public static RarArchive Read(IReadOnlyList<Stream> volumes, CancellationToken ct)
    {
        var archive = new RarArchive();
        Span<byte> signature = stackalloc byte[8];
        for (int volume = 0; volume < volumes.Count; volume++)
        {
            ct.ThrowIfCancellationRequested();
            var stream = volumes[volume];
            stream.Position = 0;
            int read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
            bool rar5 = read >= 8 && signature.SequenceEqual(Rar5Signature);
            bool rar4 = !rar5 && read >= 7 && signature[..7].SequenceEqual(Rar4Signature);
            if (!rar5 && !rar4)
            {
                if (volume == 0) throw new InvalidDataException("This is not a RAR archive.");
                archive.Warnings.Add($"Volume {volume + 1} is not part of a RAR archive; it is ignored.");
                archive._missingParts = true;
                continue;
            }
            if (volume == 0) archive.IsRar5 = rar5;
            else if (rar5 != archive.IsRar5) throw new InvalidDataException("The volumes of this archive mix RAR formats.");
            try
            {
                if (rar5) archive.ReadRar5Volume(stream, volume, ct);
                else archive.ReadRar4Volume(stream, volume, ct);
            }
            catch (InvalidDataException ex) when (RarData.IsDamage(ex) && (archive.Members.Count > 0 || volume > 0))
            {
                archive.Warnings.Add("The archive is damaged after its first members: " + ex.Message);
                archive._missingParts |= archive._split is not null;
                break;
            }
            if (archive.Members.Count > RarLimits.MaxMembers) throw new InvalidDataException("The archive has too many members to list.");
        }
        if (archive._split is not null) archive._missingParts = true;
        if (archive._missingParts || archive.Members.Exists(m => !m.Complete)) archive.Warnings.Add(RarArchiveWarnings.MissingVolumes);
        return archive;
    }

    /// <summary>Adds one part of a member: a new member, or the next part of the member split across volumes.</summary>
    private void AddPart(RarMember part, int volume, int volumeNumber, long offset, bool splitBefore, bool splitAfter)
    {
        var segment = new RarSegment(volume, offset, part.PackedSize);
        if (splitBefore)
        {
            if (_split is not null && _split.Name == part.Name)
            {
                // The member's next part. After a missing volume it is still this member (listed once), but its
                // bytes have a hole: it is never decoded.
                if (volumeNumber >= 0 && _splitVolumeNumber >= 0 && volumeNumber != _splitVolumeNumber + 1)
                {
                    _split.HasGap = true;
                    _missingParts = true;
                }
                var member = _split;
                member.Segments.Add(segment);
                member.PackedSize = checked(member.PackedSize + part.PackedSize);
                // The last part's header carries the checksum of the whole member.
                member.Crc32 = part.Crc32;
                member.Blake2sp = part.Blake2sp;
                member.HasLastPart = !splitAfter;
                _split = splitAfter ? member : null;
                _splitVolumeNumber = volumeNumber;
                return;
            }
            // A part whose beginning is in no volume here: list it once, but it cannot be decoded.
            _missingParts = true;
            part.HasFirstPart = false;
            part.HasLastPart = !splitAfter;
            part.Segments.Add(segment);
            Members.Add(part);
            _split = splitAfter ? part : null;
            _splitVolumeNumber = volumeNumber;
            return;
        }
        if (_split is not null)
        {
            _missingParts = true; // the previous member never got its continuation
            _split = null;
        }
        part.HasFirstPart = true;
        part.HasLastPart = !splitAfter;
        part.Segments.Add(segment);
        Members.Add(part);
        if (splitAfter)
        {
            _split = part;
            _splitVolumeNumber = volumeNumber;
        }
    }

    private static byte[] ReadExactly(Stream stream, long position, int count)
    {
        var buffer = new byte[count];
        stream.Position = position;
        if (stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false) != count) throw RarData.Corrupt("header past the end of the volume");
        return buffer;
    }

    // ---- RAR 5 (technote "RAR 5.0 archive format") ----

    private const int Rar5Main = 1, Rar5File = 2, Rar5Service = 3, Rar5Encryption = 4, Rar5End = 5;

    private void ReadRar5Volume(Stream stream, int volume, CancellationToken ct)
    {
        long length = stream.Length;
        long position = 8;
        int volumeNumber = volume == 0 ? 0 : -1;
        Span<byte> prefix = stackalloc byte[7];
        while (position < length)
        {
            ct.ThrowIfCancellationRequested();
            // CRC32, then the header size as a variable-length number of at most three bytes (2 MiB).
            stream.Position = position;
            int got = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
            if (got < 6) throw RarData.Corrupt("header past the end of the volume");
            uint crc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            long size = 0;
            int sizeBytes = 0;
            for (; sizeBytes < 3; sizeBytes++)
            {
                if (4 + sizeBytes >= got) throw RarData.Corrupt("header past the end of the volume");
                byte b = prefix[4 + sizeBytes];
                size |= (long)(b & 0x7F) << (7 * sizeBytes);
                if ((b & 0x80) == 0) break;
            }
            if (sizeBytes == 3) throw RarData.Corrupt("RAR 5 header size");
            sizeBytes++;
            if (size < 2 || size > RarLimits.MaxHeaderBytes) throw RarData.Corrupt("RAR 5 header size");
            long headerStart = position + 4 + sizeBytes;
            if (headerStart + size > length) throw RarData.Corrupt("header past the end of the volume");
            byte[] header = ReadExactly(stream, position + 4, sizeBytes + (int)size);
            if (Crc32.HashToUInt32(header) != crc) throw RarData.Corrupt("header checksum");
            var reader = new RarHeaderReader(header.AsSpan(sizeBytes));
            int type = reader.Int(int.MaxValue);
            ulong flags = reader.VarInt();
            long extraSize = (flags & 0x0001) != 0 ? reader.Size() : 0;
            long dataSize = (flags & 0x0002) != 0 ? reader.Size() : 0;
            if (extraSize > reader.Remaining) throw RarData.Corrupt("RAR 5 extra area");
            var body = reader.Bytes(reader.Remaining - extraSize);
            var extra = reader.Rest();
            long dataStart = headerStart + size;
            if (dataSize > length - dataStart) throw RarData.Corrupt("member data past the end of the volume");
            switch (type)
            {
                case Rar5Main:
                {
                    var main = new RarHeaderReader(body);
                    ulong archiveFlags = main.VarInt();
                    if ((archiveFlags & 0x0002) != 0) volumeNumber = main.Int(int.MaxValue);
                    break;
                }
                case Rar5Encryption:
                    throw new InvalidDataException(EncryptedListMessage);
                case Rar5File:
                {
                    if (Members.Count >= RarLimits.MaxMembers) throw new InvalidDataException("The archive has too many members to list.");
                    var member = ParseRar5File(body, extra, dataSize);
                    AddPart(member, volume, volumeNumber, dataStart, (flags & 0x0008) != 0, (flags & 0x0010) != 0);
                    break;
                }
                case Rar5End:
                    return; // the volume's own end; the next volume (if any) continues
                case Rar5Service:
                default:
                    break;
            }
            position = dataStart + dataSize;
        }
    }

    private static RarMember ParseRar5File(ReadOnlySpan<byte> body, ReadOnlySpan<byte> extraArea, long dataSize)
    {
        var reader = new RarHeaderReader(body);
        ulong fileFlags = reader.VarInt();
        long unpacked = reader.Size();
        reader.VarInt(); // attributes
        DateTime? modified = null;
        if ((fileFlags & 0x0002) != 0) modified = RarHeaderReader.UnixTime(reader.UInt32());
        uint? crc = (fileFlags & 0x0004) != 0 ? reader.UInt32() : null;
        ulong compression = reader.VarInt();
        reader.VarInt(); // host OS
        int nameLength = reader.Int(RarLimits.MaxNameBytes);
        string name = RarHeaderReader.Utf8(reader.Bytes(nameLength));

        int algorithm = (int)(compression & 0x3F);
        int method = (int)((compression >> 7) & 7);
        int dictionaryBits = (int)((compression >> 10) & 0x1F);
        int fraction = (int)((compression >> 15) & 0x1F);
        bool v5Compatible = (compression & 0x100000) != 0;
        int version = algorithm switch
        {
            0 => 50,
            1 => v5Compatible ? 50 : 70,
            _ => 0,
        };
        long dictionary = 0;
        if (method != 0 && version != 0)
        {
            if (dictionaryBits > (version == 50 ? 15 : 19)) throw RarData.Corrupt("dictionary size");
            dictionary = 0x20000L << dictionaryBits;
            if (version == 70) dictionary += dictionary / 32 * fraction;
        }
        var member = new RarMember
        {
            Name = name,
            Kind = (fileFlags & 0x0001) != 0 ? MemberKind.Directory : MemberKind.File,
            UnpackedSize = (fileFlags & 0x0008) != 0 ? -1 : unpacked,
            PackedSize = dataSize,
            ModifiedUtc = modified,
            Method = method,
            Version = method == 0 ? 0 : version,
            Dictionary = dictionary,
            SolidContinuation = (compression & 0x40) != 0 && method != 0,
            Crc32 = crc,
        };

        var extras = new RarHeaderReader(extraArea);
        while (extras.Remaining > 0)
        {
            long recordSize = extras.Size();
            var record = new RarHeaderReader(extras.Bytes(recordSize));
            switch (record.Int(int.MaxValue))
            {
                case 1: // encryption
                    member.Encrypted = true;
                    break;
                case 2: // hash
                    if (record.Int(int.MaxValue) == 0) member.Blake2sp = record.Bytes(32).ToArray();
                    break;
                case 3: // high-precision times
                {
                    ulong timeFlags = record.VarInt();
                    bool unix = (timeFlags & 0x01) != 0;
                    if ((timeFlags & 0x02) != 0)
                    {
                        if (unix)
                        {
                            long seconds = record.UInt32();
                            long nanoseconds = 0;
                            int skipped = ((timeFlags & 0x04) != 0 ? 4 : 0) + ((timeFlags & 0x08) != 0 ? 4 : 0);
                            if ((timeFlags & 0x10) != 0)
                            {
                                record.Bytes(skipped);
                                nanoseconds = record.UInt32() & 0x3FFFFFFF;
                                if (nanoseconds >= 1_000_000_000) throw RarData.Corrupt("time record");
                            }
                            member.ModifiedUtc = RarHeaderReader.UnixTime(seconds, nanoseconds);
                        }
                        else
                        {
                            member.ModifiedUtc = RarHeaderReader.FileTime(record.UInt64());
                        }
                    }
                    break;
                }
                case 5: // redirection
                {
                    int linkType = record.Int(int.MaxValue);
                    record.VarInt(); // flags
                    int targetLength = record.Int(RarLimits.MaxNameBytes);
                    string target = RarHeaderReader.Utf8(record.Bytes(targetLength));
                    member.LinkTarget = target;
                    member.Kind = linkType switch
                    {
                        1 or 2 or 3 => MemberKind.SymbolicLink,
                        4 or 5 => MemberKind.HardLink,
                        _ => MemberKind.Special,
                    };
                    break;
                }
            }
        }
        return member;
    }
}
