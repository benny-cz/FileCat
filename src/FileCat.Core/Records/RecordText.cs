using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>How file-system records read as text (D-56): full-precision times, attribute and reparse names, IDs, and bytes.</summary>
public static class RecordText
{
    private static readonly DateTime GregorianStart = new(1582, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A FILETIME with all seven decimals, as NTFS keeps it ("2026-09-30 10:00:01.1234567 UTC"): whole-second times and
    /// times that differ below a second are what timestamp checks look at.
    /// </summary>
    public static string Time(long fileTime)
    {
        if (fileTime == 0) return "not set";
        if (fileTime < 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc()) return $"invalid (0x{fileTime:X16})";
        return DateTime.FromFileTimeUtc(fileTime).ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + " UTC";
    }

    /// <summary>A UTC time with all seven decimals.</summary>
    public static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>A FILETIME to the second, UTC implied (for tables whose header says so): "2026-09-30 10:00:01".</summary>
    public static string TimeSeconds(long fileTime) => ToUtc(fileTime) is { } t ? t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "—";

    /// <summary>The time as a DateTime, or null when it is not set or not a valid time.</summary>
    public static DateTime? ToUtc(long fileTime) =>
        fileTime > 0 && fileTime <= DateTime.MaxValue.ToFileTimeUtc() ? DateTime.FromFileTimeUtc(fileTime) : null;

    /// <summary>Whether a FILETIME lies exactly on a second (its 100-nanosecond part is zero).</summary>
    public static bool IsWholeSecond(long fileTime) => fileTime > 0 && fileTime % 10_000_000 == 0;

    public static string Bytes(long bytes) => bytes switch
    {
        < 0 => "unknown",
        < 1024 => $"{bytes:N0} bytes",
        _ => $"{bytes:N0} bytes ({Units(bytes)})",
    };

    /// <summary>A size in the largest whole unit ("32 MiB"), for sizes where the exact count says nothing more.</summary>
    public static string Short(long bytes) => bytes < 1024 ? $"{bytes:N0} bytes" : Units(bytes);

    private static string Units(long bytes)
    {
        string[] units = ["KiB", "MiB", "GiB", "TiB", "PiB"];
        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value < 10 ? "0.##" : value < 100 ? "0.#" : "0", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static readonly (uint Flag, string Text)[] AttributeNames =
    [
        (0x0000_0001, "read-only"),
        (0x0000_0002, "hidden"),
        (0x0000_0004, "system"),
        (0x0000_0010, "directory"),
        (0x0000_0020, "archive"),
        (0x0000_0040, "device"),
        (0x0000_0080, "normal"),
        (0x0000_0100, "temporary"),
        (0x0000_0200, "sparse"),
        (0x0000_0400, "reparse point"),
        (0x0000_0800, "compressed"),
        (0x0000_1000, "offline"),
        (0x0000_2000, "not content-indexed"),
        (0x0000_4000, "encrypted"),
        (0x0000_8000, "integrity stream"),
        (0x0001_0000, "virtual"),
        (0x0002_0000, "no scrub data"),
        (0x0004_0000, "recall on open (or has EAs)"),
        (0x0008_0000, "pinned (always keep on this device)"),
        (0x0010_0000, "unpinned (online only)"),
        (0x0040_0000, "recall on data access"),
        (0x2000_0000, "strictly sequential"),
    ];

    /// <summary>Windows file attributes, every bit named ("0x00000021: read-only, archive").</summary>
    public static string Attributes(uint attributes) => $"0x{attributes:X8}: {AttributeWords(attributes, AttributeNames)}";

    /// <summary>
    /// The attribute bits NTFS keeps in $STANDARD_INFORMATION and $FILE_NAME: the Windows ones, and in $FILE_NAME the
    /// bits that say the item is a folder (it has a file-name index) or has another index.
    /// </summary>
    public static string NtfsAttributes(uint attributes) => $"0x{attributes:X8}: {AttributeWords(attributes,
        [.. AttributeNames.Where(a => a.Flag is not (0x0004_0000 or 0x2000_0000)), (0x0004_0000, "has extended attributes (EAs)"),
            (0x1000_0000, "folder (has a file-name index)"), (0x2000_0000, "has a view index")])}";

    private static string AttributeWords(uint attributes, (uint Flag, string Text)[] names)
    {
        if (attributes == 0) return "none";
        var parts = new List<string>();
        uint known = 0;
        foreach (var (flag, text) in names)
        {
            known |= flag;
            if ((attributes & flag) != 0) parts.Add(text);
        }
        if ((attributes & ~known) != 0) parts.Add($"unknown bits 0x{attributes & ~known:X}");
        return string.Join(", ", parts);
    }

    /// <summary>What a reparse tag is. Microsoft's own are named; others show as their number.</summary>
    public static string ReparseTag(uint tag)
    {
        string? name = tag switch
        {
            0xA000_0003 => "mount point (junction)",
            0xA000_000C => "symbolic link",
            0xC000_0004 => "hierarchical storage (HSM)",
            0x8000_0006 => "hierarchical storage (HSM2)",
            0x8000_0007 => "single-instance storage (SIS)",
            0x8000_0008 => "WIM image mount",
            0x8000_0009 => "cluster shared volume",
            0x8000_000A => "DFS",
            0x8000_0012 => "DFS Replication",
            0x8000_0013 => "Data Deduplication (its data is in the chunk store)",
            0x8000_0014 => "NFS (a link or special file made over NFS)",
            0x8000_0015 => "file placeholder (Windows 8.1 OneDrive)",
            0x8000_0017 => "Windows Overlay Filter (compressed by compact.exe, or backed by a WIM)",
            0x8000_0018 => "Windows Container Isolation",
            0x9000_1018 => "Windows Container Isolation (layer)",
            0xA000_0019 => "global reparse point",
            0x8000_001B => "app execution alias (Store app launcher)",
            0x9000_001C => "Projected File System placeholder (VFS for Git and others)",
            0xA000_001D => "WSL symbolic link",
            0x8000_001E => "Azure File Sync",
            0xA000_001F => "Windows Container Isolation tombstone",
            0x8000_0020 => "unhandled",
            0x8000_0021 => "OneDrive",
            0xA000_0022 => "Projected File System tombstone",
            0x8000_0023 => "Unix-domain socket (AF_UNIX)",
            0x8000_0024 => "WSL FIFO",
            0x8000_0025 => "WSL character device",
            0x8000_0026 => "WSL block device",
            0xA000_0027 => "Windows Container Isolation link",
            0xA000_1027 => "Windows Container Isolation link",
            0xA000_0028 => "dataless CIM",
            0x9000_0027 => "Azure File Sync folder",
            _ => null,
        };
        if (name is null && (tag & 0xFFFF_0FFF) == 0x9000_001A) name = "Cloud Files placeholder (OneDrive, iCloud, and other sync apps)";
        string bits = (tag & 0x8000_0000) != 0 ? "Microsoft" : "third-party";
        if ((tag & 0x2000_0000) != 0) bits += ", name surrogate (points elsewhere)";
        if ((tag & 0x1000_0000) != 0) bits += ", can be a folder";
        return $"0x{tag:X8} {name ?? "(not a tag FileCat knows)"} · {bits}";
    }

    /// <summary>What a reparse point's data says, for the tags whose layout is known; null for the others.</summary>
    public static string? ReparseTarget(uint tag, ReadOnlySpan<byte> data)
    {
        try
        {
            switch (tag)
            {
                case 0xA000_000C when data.Length >= 12:
                {
                    bool relative = (BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) & 1) != 0;
                    string target = LinkName(data, 12);
                    return (relative ? "relative: " : "") + target;
                }
                case 0xA000_0003 when data.Length >= 8:
                    return LinkName(data, 8);
                case 0x8000_001B when data.Length >= 4:
                {
                    // Version 3: package, app user model ID, target, and app type, each ending in a NUL.
                    var parts = Encoding.Unicode.GetString(data[4..][..(data.Length - 4 & ~1)]).Split('\0');
                    var named = new List<string>();
                    if (parts.Length > 0 && parts[0].Length > 0) named.Add("package " + parts[0]);
                    if (parts.Length > 1 && parts[1].Length > 0) named.Add("app " + parts[1]);
                    if (parts.Length > 2 && parts[2].Length > 0) named.Add("runs " + parts[2]);
                    return named.Count > 0 ? string.Join(" · ", named) : null;
                }
                case 0xA000_001D when data.Length > 4:
                    return Encoding.UTF8.GetString(data[4..]);
                case 0x8000_0017 when data.Length >= 8:
                {
                    uint provider = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
                    if (provider == 2 && data.Length >= 16)
                    {
                        uint algorithm = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
                        string name = algorithm switch { 0 => "XPRESS4K", 1 => "LZX", 2 => "XPRESS8K", 3 => "XPRESS16K", _ => $"algorithm {algorithm}" };
                        return $"compressed by the file provider with {name}; Windows decompresses it when read";
                    }
                    return provider == 1 ? "backed by a WIM image (WIMBoot)" : $"provider {provider}";
                }
            }
        }
        catch (ArgumentOutOfRangeException) { }
        return null;
    }

    /// <summary>A symbolic link's or junction's print name (or its substitute name when it has none).</summary>
    private static string LinkName(ReadOnlySpan<byte> data, int buffer)
    {
        int subOffset = BinaryPrimitives.ReadUInt16LittleEndian(data), subLength = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        int printOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]), printLength = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        string print = buffer + printOffset + printLength <= data.Length ? Encoding.Unicode.GetString(data.Slice(buffer + printOffset, printLength & ~1)) : "";
        string substitute = buffer + subOffset + subLength <= data.Length ? Encoding.Unicode.GetString(data.Slice(buffer + subOffset, subLength & ~1)) : "";
        return print.Length > 0 ? print : substitute;
    }

    /// <summary>
    /// A version-1 GUID's time and node: Windows gives objects version-1 IDs (UuidCreateSequential) for link tracking, so
    /// a birth object ID says when its ID was made, and on which network card, unless the node is random.
    /// </summary>
    public static string? GuidOrigin(Guid guid)
    {
        Span<byte> b = stackalloc byte[16];
        guid.TryWriteBytes(b);
        int version = BinaryPrimitives.ReadUInt16LittleEndian(b[6..]) >> 12;
        if (version != 1 || b[8] >> 6 != 2) return null;
        ulong ticks = (ulong)(BinaryPrimitives.ReadUInt16LittleEndian(b[6..]) & 0x0FFF) << 48
                      | (ulong)BinaryPrimitives.ReadUInt16LittleEndian(b[4..]) << 32
                      | BinaryPrimitives.ReadUInt32LittleEndian(b);
        if (ticks > (ulong)(DateTime.MaxValue - GregorianStart).Ticks) return null;
        var made = GregorianStart.AddTicks((long)ticks);
        string node = string.Join(":", b[10..].ToArray().Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
        bool random = (b[10] & 1) != 0;
        return $"version 1: made {Time(made)} " + (random ? $"with a random node {node}" : $"on a computer with network address {node}");
    }

    /// <summary>
    /// Text broken at spaces into lines of at most <paramref name="width"/> characters: the first starts with
    /// <paramref name="first"/> ("• "), the others are indented to line up with it.
    /// </summary>
    public static List<string> Wrap(string text, int width = 110, string first = "")
    {
        var lines = new List<string>();
        string indent = new(' ', first.Length);
        var line = new StringBuilder(first);
        int start = first.Length;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > start && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear().Append(indent);
                start = indent.Length;
            }
            else if (line.Length > start) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > start || lines.Count == 0) lines.Add(line.ToString());
        return lines;
    }

    /// <summary>Up to <paramref name="max"/> bytes as hex and text lines ("0000  4D 5A 90 00 …  MZ..").</summary>
    public static IReadOnlyList<string> HexLines(ReadOnlySpan<byte> data, int max = 256)
    {
        var lines = new List<string>();
        int shown = Math.Min(data.Length, max);
        for (int at = 0; at < shown; at += 16)
        {
            var row = data.Slice(at, Math.Min(16, shown - at));
            var sb = new StringBuilder();
            sb.Append(at.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");
            for (int i = 0; i < 16; i++) sb.Append(i < row.Length ? row[i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
            sb.Append(' ');
            foreach (byte x in row) sb.Append(x is >= 0x20 and < 0x7F ? (char)x : '.');
            lines.Add(sb.ToString().TrimEnd());
        }
        if (data.Length > shown) lines.Add($"… {data.Length - shown:N0} more bytes");
        return lines;
    }
}
