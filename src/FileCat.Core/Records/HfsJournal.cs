using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

/// <summary>
/// An HFS+ volume's journal as the volume itself describes it (D-56), read through a delegate over the volume (offset,
/// count). The volume header (byte 1,024, big-endian) says whether the volume is journaled and which allocation block
/// holds the journal info block; that block says where the journal is (in this file system or on another device) and
/// how big it is; the journal's own header, in the byte order of the computer that wrote it, says where the
/// transactions it still holds begin and end, its sequence number, and its checksum, read as xnu's vfs_journal.c reads
/// them. Reading changes nothing; damaged or truncated structures end the read with a reason.
/// </summary>
public static class HfsJournal
{
    private const uint JournalMagic = 0x4A4E_4C78; // "JNLx"
    private const uint OldJournalMagic = 0x4A48_4452; // "JHDR": older journals, whose checksum the system does not check
    private const uint EndianMagic = 0x1234_5678;
    private const uint InFileSystem = 0x1, OnOtherDevice = 0x2, NeedsInit = 0x4;
    private const int Checksummed = 44; // the header up to its sequence number (offsetof(journal_header, sequence_num))

    /// <summary>What the volume <paramref name="read"/> reads says about its journal. What the volume holds never throws.</summary>
    public static HfsJournalSummary Read(Func<long, int, byte[]> read)
    {
        var summary = new HfsJournalSummary();
        try { Walk(read, summary); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { summary.Problems.Add("The volume could not be read: " + ex.Message); }
        return summary;
    }

    /// <summary>The "Journal (HFS+)" section of the volume <paramref name="read"/> reads.</summary>
    public static InspectionSection Describe(Func<long, int, byte[]> read) => Section(Read(read));

    /// <summary>xnu's calc_checksum: every byte folded into a shifting sum, which is then inverted.</summary>
    internal static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        foreach (byte b in bytes) sum = (sum << 8) ^ (sum + b);
        return ~sum;
    }

    private static void Walk(Func<long, int, byte[]> read, HfsJournalSummary s)
    {
        byte[] header = read(1024, 512);
        if (header.Length < 512) { s.Problems.Add(Truncated("volume header", header.Length, 512)); return; }
        if (BinaryPrimitives.ReadUInt16BigEndian(header) == 0x4244) // "BD": an HFS volume, which may wrap an HFS+ one
        {
            if (BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(124)) != 0x482B)
            {
                s.Problems.Add("This is an HFS (Mac OS Standard) volume, which keeps no journal.");
                return;
            }
            // The wrapped volume starts at the wrapper's first allocation block (drAlBlSt, in 512-byte sectors) plus its
            // embedded extent's start (drEmbedExtent, in the wrapper's allocation blocks).
            uint wrapperBlock = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20));
            if (wrapperBlock == 0 || wrapperBlock % 512 != 0)
            {
                s.Problems.Add($"The HFS wrapper's allocation block size ({wrapperBlock:N0} bytes) is not a multiple of 512: the wrapper is damaged.");
                return;
            }
            s.VolumeOffset = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(28)) * 512L + BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(126)) * (long)wrapperBlock;
            header = read(s.VolumeOffset + 1024, 512);
            if (header.Length < 512) { s.Problems.Add(Truncated("volume header (inside its HFS wrapper)", header.Length, 512)); return; }
        }
        ushort signature = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (signature is not (0x482B or 0x4858))
        {
            s.Problems.Add($"The volume header's signature is 0x{signature:X4}, not HFS+'s (“H+”) or HFSX's (“HX”).");
            return;
        }
        s.Kind = signature == 0x482B ? "HFS+" : "HFSX";
        s.Version = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
        s.Attributes = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        s.LastMountedVersion = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        s.InfoBlock = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
        s.BlockSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(40));
        s.TotalBlocks = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(44));
        if (s.BlockSize is < 512 or > 1u << 30 || (s.BlockSize & (s.BlockSize - 1)) != 0)
        {
            s.Problems.Add($"The allocation block size ({s.BlockSize:N0} bytes) is not a power of two from 512 bytes: the volume header is damaged.");
            return;
        }
        if (!s.Journaled) return;
        if (s.InfoBlock == 0 || s.InfoBlock >= s.TotalBlocks)
        {
            s.Problems.Add(s.InfoBlock == 0
                ? "The volume is marked journaled, but its header names no journal info block."
                : $"The journal info block (allocation block {s.InfoBlock:N0}) lies past the volume's {s.TotalBlocks:N0} blocks.");
            return;
        }

        // The journal info block: flags, the device's signature (32 bytes), the journal's offset and size, and for a
        // journal on another device, the UUID that names it.
        byte[] info = read(s.VolumeOffset + (long)s.InfoBlock * s.BlockSize, 512);
        if (info.Length < 89) { s.Problems.Add(Truncated("journal info block", info.Length, 89)); return; }
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(info);
        s.InfoFlags = flags;
        ulong offset = BinaryPrimitives.ReadUInt64BigEndian(info.AsSpan(36)), size = BinaryPrimitives.ReadUInt64BigEndian(info.AsSpan(44));
        if ((flags & OnOtherDevice) != 0)
        {
            s.ExternalUuid = Ascii(info.AsSpan(52, 37));
            if (size is > 0 and <= long.MaxValue) s.JournalSize = (long)size;
            return; // its header is on that device
        }
        if ((flags & InFileSystem) == 0)
        {
            s.Problems.Add($"The journal info block says the journal is neither in this file system nor on another device (flags 0x{flags:X8}).");
            return;
        }
        ulong volumeBytes = (ulong)s.TotalBlocks * s.BlockSize;
        if (offset >= volumeBytes || size == 0 || size > volumeBytes - offset)
        {
            s.Problems.Add($"The journal info block puts a journal of {size:N0} bytes at byte {offset:N0}, which does not fit in the volume's {volumeBytes:N0} bytes.");
            return;
        }
        s.JournalOffset = (long)offset;
        s.JournalSize = (long)size;
        if ((flags & NeedsInit) != 0) return; // made anew at the next mount: what its header holds now is not used

        // The journal header, in the byte order of the computer that last wrote it: its magic number says which.
        byte[] raw = read(s.VolumeOffset + (long)offset, 512);
        if (raw.Length < 48) { s.Problems.Add(Truncated("journal header", raw.Length, 48)); return; }
        uint little = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        bool big;
        if (little is JournalMagic or OldJournalMagic) big = false;
        else if (BinaryPrimitives.ReadUInt32BigEndian(raw) is JournalMagic or OldJournalMagic) big = true;
        else
        {
            s.Problems.Add($"The journal header's magic number (bytes {Convert.ToHexString(raw, 0, 4)}) is “JNLx” in neither byte order: it is not a journal header, or it is damaged.");
            return;
        }
        uint U32(int at) => big ? BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at));
        long I64(int at) => big ? BinaryPrimitives.ReadInt64BigEndian(raw.AsSpan(at)) : BinaryPrimitives.ReadInt64LittleEndian(raw.AsSpan(at));
        // The checksum covers the header up to its sequence number as it lies on the disk, with the checksum itself as zero.
        var summed = raw.AsSpan(0, Checksummed).ToArray();
        summed.AsSpan(36, 4).Clear();
        var h = new HfsJournalHeader(big, U32(0), U32(4), I64(8), I64(16), I64(24), (int)U32(32), U32(36), Checksum(summed), (int)U32(40), U32(44));
        s.Header = h;
        if (h.Endian != EndianMagic)
            s.Problems.Add($"The journal header's endian field is 0x{h.Endian:X8}, not 0x12345678 in the byte order its magic number gives.");
        if (h.HeaderSize is < 512 or > 65536 || (h.HeaderSize & (h.HeaderSize - 1)) != 0)
            s.Problems.Add($"The journal header's block size ({h.HeaderSize:N0} bytes) is not a sector size: the header is damaged.");
        if (h.BlockListHeaderSize <= 0 || h.BlockListHeaderSize > h.Size)
            s.Problems.Add($"The journal header's block list header size ({h.BlockListHeaderSize:N0} bytes) does not fit in the journal: the header is damaged.");
        if (h.Size != s.JournalSize)
            s.Problems.Add($"The journal header gives the journal {h.Size:N0} bytes; the journal info block {s.JournalSize:N0}.");
        if (!h.InRange)
            s.Problems.Add($"The journal header's start (byte {h.Start:N0}) or end (byte {h.End:N0}) lies outside the journal past its header block: the header is damaged.");
    }

    /// <summary>The summary as the "Journal (HFS+)" section of a volume's record.</summary>
    public static InspectionSection Section(HfsJournalSummary s)
    {
        var fields = new List<(string, string)>();
        if (s.Kind is not null)
        {
            fields.Add(("Volume", $"{s.Kind} version {s.Version} · allocation blocks of {RecordText.Bytes(s.BlockSize)} · last mounted by {MountedBy(s.LastMountedVersion)}"));
            if (s.VolumeOffset > 0) fields.Add(("Wrapper", $"an HFS volume, with this HFS+ volume in it from byte {s.VolumeOffset:N0}"));
            fields.Add(("Attributes", AttributeText(s.Attributes)));
            fields.Add(("Journaled", s.Journaled ? "yes (volume attribute bit 13)" : "no (volume attribute bit 13 is clear)"));
        }
        if (s.InfoFlags is { } flags)
            fields.Add(("Journal info block", $"allocation block {s.InfoBlock:N0} (byte {s.VolumeOffset + (long)s.InfoBlock * s.BlockSize:N0}) · flags 0x{flags:X8}: {FlagText(flags)}"));
        if (s.InfoFlags is { } where && (where & OnOtherDevice) != 0)
            fields.Add(("Journal", "on another device" + (s.ExternalUuid is { Length: > 0 } uuid ? $" (journal UUID {uuid})" : "")
                + (s.JournalSize is { } external ? $", {RecordText.Bytes(external)}" : "") + ": its header is there, not on this volume"));
        else if (s.JournalOffset is { } offset && s.JournalSize is { } size)
            fields.Add(("Journal", $"byte {s.VolumeOffset + offset:N0} of the volume, {RecordText.Bytes(size)}"
                + ((s.InfoFlags.GetValueOrDefault() & NeedsInit) != 0 ? " · made anew at the next mount, so its header is not read" : "")));
        if (s.Header is { } h)
        {
            string order = h.BigEndian ? "big-endian (written by a PowerPC Mac)" : "little-endian (written by an Intel or Apple silicon Mac)";
            fields.Add(("Header", $"{order} · magic “{(h.OldStyle ? "JHDR" : "JNLx")}” · header block of {h.HeaderSize:N0} bytes · block list headers of {h.BlockListHeaderSize:N0} bytes"));
            if (h.InRange)
                fields.Add(("To replay", h.HasTransactions
                    ? $"{RecordText.Bytes(h.Waiting)} of transactions, from byte {h.Start:N0} to byte {h.End:N0} of the journal{(h.End < h.Start ? " (around its end)" : "")}: not yet marked as written in place, so a mount after a crash would replay them"
                    : $"nothing: the journal is empty (its start and end are both byte {h.Start:N0})"));
            fields.Add(("Sequence number", h.Sequence.ToString("N0", CultureInfo.CurrentCulture)));
            fields.Add(("Checksum", h.OldStyle ? $"0x{h.Checksum:X8}, not checked (an old-style header)"
                : h.ChecksumMatches ? $"0x{h.Checksum:X8}, correct"
                : $"0x{h.Checksum:X8}, wrong: the header's bytes give 0x{h.ComputedChecksum:X8} (it was changed or damaged)"));
        }
        var lines = new List<string>();
        foreach (string problem in s.Problems) lines.AddRange(RecordText.Wrap(problem, 110, "⚠ "));
        if (s.Header is not null)
            lines.AddRange(RecordText.Wrap("The journal keeps each change to the file system's own structures (its catalog, extents, attributes, and allocation bitmap) until the change is written in place; a mount after a crash replays what it still holds. While the volume is mounted, transactions waiting in it are normal."));
        return new InspectionSection("Journal (HFS+)", fields) { Lines = lines };
    }

    private static string Truncated(string what, int got, int needed) => got == 0
        ? $"The {what} could not be read: the volume ends before it."
        : $"Only {got:N0} of the {what}'s {needed:N0} bytes could be read: the volume ends inside it.";

    /// <summary>The four characters of lastMountedVersion, and who writes them.</summary>
    private static string MountedBy(uint version)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, version);
        string text = Ascii(bytes) is { Length: 4 } chars ? $"“{chars}”" : $"0x{version:X8}";
        string? who = version switch
        {
            0x3130_2E30 => "Mac OS X or macOS, not journaled", // "10.0"
            0x4846_534A => "Mac OS X or macOS, journaled", // "HFSJ"
            0x4653_4B21 => "a journal replay that failed", // "FSK!"
            0x6673_636B => "fsck_hfs", // "fsck"
            0x382E_3130 => "Mac OS 8.1 to 9.2.2", // "8.10"
            _ => null,
        };
        return who is null ? text : $"{text} ({who})";
    }

    private static readonly (uint Bit, string Text)[] AttributeNames =
    [
        (1u << 7, "locked by hardware"), (1u << 8, "unmounted cleanly"), (1u << 9, "bad blocks spared"),
        (1u << 10, "needs no cache (a RAM or ROM disk)"), (1u << 11, "boot volume inconsistent"), (1u << 12, "catalog node IDs reused"),
        (1u << 13, "journaled"), (1u << 14, "inconsistent (damage found while it was mounted)"), (1u << 15, "locked by software"),
        (1u << 30, "content protection"), (1u << 31, "unused catalog nodes zeroed"),
    ];

    private static string AttributeText(uint attributes)
    {
        var words = AttributeNames.Where(a => (attributes & a.Bit) != 0).Select(a => a.Text).ToList();
        uint known = AttributeNames.Aggregate(0u, (all, a) => all | a.Bit);
        if ((attributes & ~known) != 0) words.Add($"other bits 0x{attributes & ~known:X}");
        return $"0x{attributes:X8}: {(words.Count > 0 ? string.Join(", ", words) : "none")}";
    }

    private static string FlagText(uint flags)
    {
        var words = new List<string>();
        if ((flags & InFileSystem) != 0) words.Add("the journal is in this file system");
        if ((flags & OnOtherDevice) != 0) words.Add("the journal is on another device");
        if ((flags & NeedsInit) != 0) words.Add("it is to be made anew at the next mount");
        if ((flags & ~7u) != 0) words.Add($"other bits 0x{flags & ~7u:X}");
        return words.Count > 0 ? string.Join(", ", words) : "none";
    }

    /// <summary>Printable ASCII up to the first zero, or null when anything else comes before it.</summary>
    private static string? Ascii(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        var text = end < 0 ? bytes : bytes[..end];
        foreach (byte b in text)
            if (b is < 0x20 or >= 0x7F) return null;
        return Encoding.ASCII.GetString(text);
    }
}

/// <summary>
/// What an HFS+ volume says about its journal (<see cref="HfsJournal.Read"/>). A value is unset where reading did not
/// get that far, and <see cref="Problems"/> says why.
/// </summary>
public sealed class HfsJournalSummary
{
    /// <summary>"HFS+", or "HFSX" (its case-sensitive variant); null when no HFS+ volume header was read.</summary>
    public string? Kind { get; internal set; }

    public int Version { get; internal set; }

    public uint Attributes { get; internal set; }

    /// <summary>Four characters naming what last mounted the volume ("HFSJ", "10.0", "fsck").</summary>
    public uint LastMountedVersion { get; internal set; }

    public uint BlockSize { get; internal set; }

    public uint TotalBlocks { get; internal set; }

    /// <summary>Where the HFS+ volume starts on its device: 0, or past an HFS wrapper.</summary>
    public long VolumeOffset { get; internal set; }

    /// <summary>Volume attribute bit 13 (kHFSVolumeJournaledBit).</summary>
    public bool Journaled => (Attributes & 0x2000) != 0;

    /// <summary>The allocation block that holds the journal info block.</summary>
    public uint InfoBlock { get; internal set; }

    /// <summary>The journal info block's flags: 1 in this file system, 2 on another device, 4 to be made anew at the next mount.</summary>
    public uint? InfoFlags { get; internal set; }

    /// <summary>Where the journal starts, in bytes from the start of the HFS+ volume (the journal info block's offset).</summary>
    public long? JournalOffset { get; internal set; }

    public long? JournalSize { get; internal set; }

    /// <summary>The UUID the journal info block names for a journal on another device (or null when it is not printable).</summary>
    public string? ExternalUuid { get; internal set; }

    public HfsJournalHeader? Header { get; internal set; }

    /// <summary>What stopped the reading, or what is wrong in what was read.</summary>
    public List<string> Problems { get; } = [];
}

/// <summary>An HFS+ journal's header (vfs_journal.c's journal_header) in the byte order it was written in.</summary>
public sealed record HfsJournalHeader(bool BigEndian, uint Magic, uint Endian, long Start, long End, long Size,
    int BlockListHeaderSize, uint Checksum, uint ComputedChecksum, int HeaderSize, uint Sequence)
{
    /// <summary>The older "JHDR" magic, whose checksum the system does not check.</summary>
    public bool OldStyle => Magic == 0x4A48_4452;

    public bool ChecksumMatches => Checksum == ComputedChecksum;

    /// <summary>Whether transactions wait in the journal to be replayed: its start and end differ.</summary>
    public bool HasTransactions => Start != End;

    /// <summary>Whether its start and end lie in the journal past the header block.</summary>
    public bool InRange => HeaderSize > 0 && Start >= HeaderSize && Start <= Size && End >= HeaderSize && End <= Size;

    /// <summary>The bytes from its start to its end; when they wrap, around the journal's end and back past the header block.</summary>
    public long Waiting => End >= Start ? End - Start : Size - Start + (End - HeaderSize);
}
