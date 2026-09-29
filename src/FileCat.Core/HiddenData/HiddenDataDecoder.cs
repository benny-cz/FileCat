using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FileCat.Core.HiddenData;

/// <summary>
/// What a stream or attribute says, decoded where its layout is known, documented or not (D-55): Mark-of-the-Web,
/// SmartScreen, WOF compression, Mac data on SMB shares, WSL's metadata, the kernel's caches, and catalog hints on
/// Windows; origins, SELinux labels, capabilities, and ACLs on Linux; quarantine, download origins and dates, Finder
/// information, file-system compression, provenance, and last use on macOS. Anything else is shown as its text or its
/// first bytes, never hidden. Every read is bounds-checked: the bytes come from the file, not from FileCat.
/// </summary>
public static class HiddenDataDecoder
{
    /// <summary>A one-line summary (for the list) and, where there is more, the lines behind it (for the viewer).</summary>
    public static (string Summary, IReadOnlyList<string> Details) Describe(HiddenItem item, ReadOnlySpan<byte> value)
    {
        try
        {
            return Decode(item, value) ?? Generic(item, value);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or FormatException or OverflowException)
        {
            return Generic(item, value);
        }
    }

    private static (string, IReadOnlyList<string>)? Decode(HiddenItem item, ReadOnlySpan<byte> v)
    {
        string name = item.Name;
        switch (item.Kind)
        {
            case HiddenKind.ResourceFork:
                return ($"Resource fork, {Bytes(item.Size)}", []);
            case HiddenKind.Stream:
                return name.ToLowerInvariant() switch
                {
                    "zone.identifier" => MarkOfTheWeb(v),
                    "smartscreen" => ($"SmartScreen checked it{(Text(v) is { Length: > 0 } by ? $" ({by})" : "")}", []),
                    "wofcompresseddata" => ($"Compressed data (Windows Overlay Filter, compact.exe), {Bytes(item.Size)}", []),
                    "afp_afpinfo" => AfpInfo(v),
                    "afp_resource" => ($"A Mac resource fork kept on an SMB share, {Bytes(item.Size)}", []),
                    "encryptable" => ("Marked for encryption by Windows (Protected Data)", []),
                    "com.dropbox.attrs" or "com.dropbox.attributes" => ($"Dropbox's attributes, {Bytes(item.Size)}", []),
                    "favicon" => ($"A web page's icon, {Bytes(item.Size)}", []),
                    _ => null,
                };
            case HiddenKind.NtfsAttribute:
                return NtfsAttribute(name, v, item.Size);
            default:
                return OperatingSystem.IsMacOS() || name.StartsWith("com.apple.", StringComparison.Ordinal) ? MacAttribute(name, v, item.Size) : LinuxAttribute(name, v);
        }
    }

    // ---- Windows ----------------------------------------------------------------------------------------------

    private static (string, IReadOnlyList<string>) MarkOfTheWeb(ReadOnlySpan<byte> v)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in Text(v).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (line.IndexOf('=') is > 0 and var eq) fields[line[..eq]] = line[(eq + 1)..];
        string zone = fields.TryGetValue("ZoneId", out var id) && int.TryParse(id, out int z)
            ? z switch { 0 => "this computer", 1 => "the local intranet", 2 => "a trusted site", 3 => "the Internet", 4 => "a restricted site", _ => $"zone {z}" }
            : "an unknown zone";
        string summary = $"Mark of the Web: from {zone}";
        if (fields.TryGetValue("HostUrl", out var host)) summary += " · " + host;
        var details = fields.Select(f => f.Key switch
        {
            "ZoneId" => $"Zone: {zone} ({f.Value})",
            "HostUrl" => $"Downloaded from: {f.Value}",
            "ReferrerUrl" => $"Linked from: {f.Value}",
            "LastWriterPackageFamilyName" => $"Written by the app: {f.Value}",
            "AppZoneId" => $"App zone: {f.Value}",
            _ => $"{f.Key}: {f.Value}",
        }).ToList();
        return (summary, details);
    }

    /// <summary>AFP_AfpInfo (Samba and Windows keep a Mac's Finder information in it): 'AFP\0', version, …, FinderInfo at 16.</summary>
    private static (string, IReadOnlyList<string>)? AfpInfo(ReadOnlySpan<byte> v)
    {
        if (v.Length < 48 || !v[..4].SequenceEqual("AFP\0"u8)) return null;
        var (summary, details) = FinderInfo(v.Slice(16, 32));
        return ("A Mac's Finder information on an SMB share: " + summary, details);
    }

    private static (string, IReadOnlyList<string>)? NtfsAttribute(string name, ReadOnlySpan<byte> v, long size)
    {
        string upper = name.ToUpperInvariant();
        switch (upper)
        {
            case "$LXUID" when v.Length >= 4:
                return ($"WSL: owner uid {BinaryPrimitives.ReadUInt32LittleEndian(v)}", []);
            case "$LXGID" when v.Length >= 4:
                return ($"WSL: group gid {BinaryPrimitives.ReadUInt32LittleEndian(v)}", []);
            case "$LXMOD" when v.Length >= 4:
                uint mode = BinaryPrimitives.ReadUInt32LittleEndian(v);
                return ($"WSL: mode {Convert.ToString(mode, 8)} ({UnixMode(mode)})", []);
            case "$LXDEV" when v.Length >= 8:
                return ($"WSL: device {BinaryPrimitives.ReadUInt32LittleEndian(v)}, {BinaryPrimitives.ReadUInt32LittleEndian(v[4..])}", []);
            case "LXATTRB" when v.Length >= 56:
                // WSL 1's file attributes: flags, version, mode, uid, gid, rdev, then three nanosecond fields and three times.
                uint lxMode = BinaryPrimitives.ReadUInt32LittleEndian(v[4..]);
                uint uid = BinaryPrimitives.ReadUInt32LittleEndian(v[8..]), gid = BinaryPrimitives.ReadUInt32LittleEndian(v[12..]);
                return ($"WSL 1: mode {Convert.ToString(lxMode, 8)} ({UnixMode(lxMode)}), uid {uid}, gid {gid}",
                [
                    $"Accessed: {UnixTime(BinaryPrimitives.ReadInt64LittleEndian(v[32..]), BinaryPrimitives.ReadUInt32LittleEndian(v[20..]))}",
                    $"Modified: {UnixTime(BinaryPrimitives.ReadInt64LittleEndian(v[40..]), BinaryPrimitives.ReadUInt32LittleEndian(v[24..]))}",
                    $"Changed: {UnixTime(BinaryPrimitives.ReadInt64LittleEndian(v[48..]), BinaryPrimitives.ReadUInt32LittleEndian(v[28..]))}",
                ]);
            case "$CI.CATALOGHINT":
                // Code Integrity's hint of the catalog that signs the file: a small header, then the catalog's name.
                string catalog = Printable(v.Length > 4 ? v[4..] : v).Trim();
                return (catalog.Length > 0 ? $"Code Integrity: signed by the catalog {catalog}" : $"Code Integrity catalog hint, {Bytes(size)}", []);
        }
        if (upper.StartsWith("$KERNEL.PURGE.", StringComparison.Ordinal) || upper.StartsWith("$KERNEL.SMARTLOCKER.", StringComparison.Ordinal))
            return ($"Kept by the Windows kernel ({name[8..]}): Smart App Control and AppLocker cache their verdicts here (undocumented), {Bytes(size)}", [HexLine(v)]);
        return null;
    }

    // ---- Linux ------------------------------------------------------------------------------------------------

    private static (string, IReadOnlyList<string>)? LinuxAttribute(string name, ReadOnlySpan<byte> v) => name switch
    {
        "user.xdg.origin.url" => ($"Downloaded from {Text(v)}", []),
        "user.xdg.referrer.url" => ($"Linked from {Text(v)}", []),
        "user.xdg.comment" => ($"Comment: {Text(v)}", []),
        "user.xdg.publisher" => ($"Published by {Text(v)}", []),
        "security.selinux" => ($"SELinux label {Text(v)}", []),
        "security.apparmor" => ($"AppArmor profile {Text(v)}", []),
        "security.SMACK64" => ($"Smack label {Text(v)}", []),
        "security.capability" => Capabilities(v),
        "system.posix_acl_access" => PosixAcl(v, "Access list"),
        "system.posix_acl_default" => PosixAcl(v, "Default list for new items"),
        "security.ima" => ($"IMA measurement (integrity), {Bytes(v.Length)}", [HexLine(v)]),
        "security.evm" => ($"EVM signature (integrity), {Bytes(v.Length)}", [HexLine(v)]),
        "system.data" => ($"Data kept in the inode (ext4 inline data), {Bytes(v.Length)}", []),
        _ => null,
    };

    private static readonly string[] CapabilityNames =
    [
        "cap_chown", "cap_dac_override", "cap_dac_read_search", "cap_fowner", "cap_fsetid", "cap_kill", "cap_setgid", "cap_setuid",
        "cap_setpcap", "cap_linux_immutable", "cap_net_bind_service", "cap_net_broadcast", "cap_net_admin", "cap_net_raw", "cap_ipc_lock",
        "cap_ipc_owner", "cap_sys_module", "cap_sys_rawio", "cap_sys_chroot", "cap_sys_ptrace", "cap_sys_pacct", "cap_sys_admin",
        "cap_sys_boot", "cap_sys_nice", "cap_sys_resource", "cap_sys_time", "cap_sys_tty_config", "cap_mknod", "cap_lease",
        "cap_audit_write", "cap_audit_control", "cap_setfcap", "cap_mac_override", "cap_mac_admin", "cap_syslog", "cap_wake_alarm",
        "cap_block_suspend", "cap_audit_read", "cap_perfmon", "cap_bpf", "cap_checkpoint_restore",
    ];

    /// <summary>vfs_cap_data (revision 2 or 3): the file's capabilities, written as getcap writes them.</summary>
    internal static (string, IReadOnlyList<string>)? Capabilities(ReadOnlySpan<byte> v)
    {
        if (v.Length < 20) return null;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(v);
        uint revision = magic & 0xFF000000;
        if (revision is not (0x02000000 or 0x03000000)) return null;
        bool effective = (magic & 1) != 0;
        ulong permitted = BinaryPrimitives.ReadUInt32LittleEndian(v[4..]) | (ulong)BinaryPrimitives.ReadUInt32LittleEndian(v[12..]) << 32;
        ulong inheritable = BinaryPrimitives.ReadUInt32LittleEndian(v[8..]) | (ulong)BinaryPrimitives.ReadUInt32LittleEndian(v[16..]) << 32;
        string Names(ulong bits) => string.Join(",", Enumerable.Range(0, 64).Where(i => (bits >> i & 1) != 0)
            .Select(i => i < CapabilityNames.Length ? CapabilityNames[i] : $"cap_{i}"));
        var parts = new List<string>();
        ulong both = permitted & inheritable;
        if (both != 0) parts.Add($"{Names(both)}={(effective ? "e" : "")}ip");
        if ((permitted & ~both) != 0) parts.Add($"{Names(permitted & ~both)}={(effective ? "e" : "")}p");
        if ((inheritable & ~both) != 0) parts.Add($"{Names(inheritable & ~both)}=i");
        string text = parts.Count > 0 ? string.Join(" ", parts) : "none";
        if (revision == 0x03000000 && v.Length >= 24) text += $" [rootid={BinaryPrimitives.ReadUInt32LittleEndian(v[20..])}]";
        return ($"Capabilities: {text}", []);
    }

    /// <summary>A POSIX ACL as the kernel stores it: version 2, then tag, permissions, and id per entry.</summary>
    internal static (string, IReadOnlyList<string>)? PosixAcl(ReadOnlySpan<byte> v, string what)
    {
        if (v.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(v) != 2) return null;
        var entries = new List<string>();
        for (int at = 4; at + 8 <= v.Length; at += 8)
        {
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(v[at..]);
            ushort perm = BinaryPrimitives.ReadUInt16LittleEndian(v[(at + 2)..]);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(v[(at + 4)..]);
            string rwx = $"{((perm & 4) != 0 ? 'r' : '-')}{((perm & 2) != 0 ? 'w' : '-')}{((perm & 1) != 0 ? 'x' : '-')}";
            entries.Add(tag switch
            {
                0x01 => $"user::{rwx}",
                0x02 => $"user:{id}:{rwx}",
                0x04 => $"group::{rwx}",
                0x08 => $"group:{id}:{rwx}",
                0x10 => $"mask::{rwx}",
                0x20 => $"other::{rwx}",
                _ => $"tag {tag}:{id}:{rwx}",
            });
        }
        return ($"{what}: {string.Join(",", entries)}", entries);
    }

    // ---- macOS ------------------------------------------------------------------------------------------------

    private static (string, IReadOnlyList<string>)? MacAttribute(string name, ReadOnlySpan<byte> v, long size)
    {
        switch (name)
        {
            case "com.apple.quarantine":
                return Quarantine(Text(v));
            case "com.apple.metadata:kMDItemWhereFroms":
                var froms = BinaryPlist.Strings(v);
                return froms.Count > 0 ? ($"Downloaded from {froms[0]}", froms.Select((f, i) => (i == 0 ? "From: " : "Via: ") + f).ToList()) : null;
            case "com.apple.metadata:kMDItemDownloadedDate":
                var dates = BinaryPlist.Dates(v);
                return dates.Count > 0 ? ($"Downloaded {dates[0].ToLocalTime():g}", []) : null;
            case "com.apple.FinderInfo" when v.Length >= 32:
                return FinderInfo(v[..32]);
            case "com.apple.ResourceFork":
                return ($"Resource fork, {Bytes(size)}", []);
            case "com.apple.decmpfs":
                return Decmpfs(v);
            case "com.apple.lastuseddate#PS" when v.Length >= 16:
                return ($"Last opened {UnixTime(BinaryPrimitives.ReadInt64LittleEndian(v), (uint)BinaryPrimitives.ReadUInt64LittleEndian(v[8..])):g}", []);
            case "com.apple.provenance":
                // Undocumented (macOS 13 and later): a version header, then an 8-byte provenance identifier.
                return v.Length >= 11
                    ? ($"Provenance: which app brought it here (undocumented), id {BinaryPrimitives.ReadUInt64LittleEndian(v[3..]):X16}", [HexLine(v)])
                    : ($"Provenance (undocumented), {Bytes(size)}", [HexLine(v)]);
            case "com.apple.macl":
                // Undocumented: the apps macOS let open this file through a user's choice, as UUIDs after a 2-byte header each.
                var apps = new List<string>();
                for (int at = 0; at + 18 <= v.Length; at += 18)
                    if (v.Slice(at + 2, 16).IndexOfAnyExcept((byte)0) >= 0) apps.Add(new Guid(v.Slice(at + 2, 16), bigEndian: true).ToString().ToUpperInvariant());
                return ($"Apps granted access by the user (undocumented): {(apps.Count == 0 ? "none" : apps.Count.ToString(CultureInfo.InvariantCulture))}", apps);
            case "com.apple.rootless":
                return ($"Protected by System Integrity Protection{(Text(v) is { Length: > 0 } label ? $" ({label})" : "")}", []);
            case "com.apple.TextEncoding":
                return ($"Text encoding {Text(v).Split(';')[0]}", []);
            case "com.apple.lastuseddate":
                return ($"Last opened (old form), {Bytes(size)}", []);
        }
        return name.StartsWith("com.apple.metadata:", StringComparison.Ordinal) && BinaryPlist.Strings(v) is { Count: > 0 } strings
            ? ($"{name["com.apple.metadata:".Length..]}: {string.Join(", ", strings)}", strings)
            : null;
    }

    /// <summary>"flags;hex seconds;agent;event UUID".</summary>
    internal static (string, IReadOnlyList<string>) Quarantine(string text)
    {
        var parts = text.Split(';');
        var details = new List<string>();
        string summary = "Quarantined";
        if (parts.Length > 2 && parts[2].Length > 0) summary += $": downloaded by {parts[2]}";
        if (parts.Length > 1 && long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long seconds))
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
            summary += $" on {when:g}";
            details.Add($"When: {when:F}");
        }
        if (parts.Length > 0 && uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint flags))
            details.Add($"Flags: 0x{flags:X4}{((flags & 0x40) != 0 ? " (the user approved opening it)" : "")}");
        if (parts.Length > 3 && parts[3].Length > 0) details.Add($"Event: {parts[3]}");
        return (summary, details);
    }

    /// <summary>The classic Finder information: type and creator codes, and flags.</summary>
    private static (string, IReadOnlyList<string>) FinderInfo(ReadOnlySpan<byte> v)
    {
        if (v.IndexOfAnyExcept((byte)0) < 0) return ("Finder information (empty)", []);
        string Code(ReadOnlySpan<byte> c) => c.IndexOfAnyExcept((byte)0) < 0 ? "" : Encoding.Latin1.GetString(c);
        string type = Code(v[..4]), creator = Code(v[4..8]);
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(v[8..]);
        var named = new List<string>();
        if ((flags & 0x8000) != 0) named.Add("alias");
        if ((flags & 0x4000) != 0) named.Add("invisible");
        if ((flags & 0x2000) != 0) named.Add("has bundle");
        if ((flags & 0x1000) != 0) named.Add("name locked");
        if ((flags & 0x0800) != 0) named.Add("stationery");
        if ((flags & 0x0400) != 0) named.Add("custom icon");
        if ((flags & 0x000E) != 0) named.Add($"color label {(flags & 0x000E) >> 1}");
        var parts = new List<string>();
        if (type.Length > 0) parts.Add($"type '{type}'");
        if (creator.Length > 0) parts.Add($"creator '{creator}'");
        parts.AddRange(named);
        return ($"Finder information: {(parts.Count > 0 ? string.Join(", ", parts) : $"flags 0x{flags:X4}")}", []);
    }

    /// <summary>HFS+ and APFS transparent compression: 'fpmc', the method, and the size uncompressed.</summary>
    private static (string, IReadOnlyList<string>)? Decmpfs(ReadOnlySpan<byte> v)
    {
        if (v.Length < 16 || !v[..4].SequenceEqual("fpmc"u8)) return null;
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(v[4..]);
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(v[8..]);
        string method = type switch
        {
            3 => "zlib, in the attribute", 4 => "zlib, in the resource fork",
            7 => "LZVN, in the attribute", 8 => "LZVN, in the resource fork",
            9 => "stored, in the attribute", 10 => "stored, in the resource fork",
            11 => "LZFSE, in the attribute", 12 => "LZFSE, in the resource fork",
            13 => "LZBITMAP, in the attribute", 14 => "LZBITMAP, in the resource fork",
            _ => $"method {type}",
        };
        return ($"Compressed by the file system ({method}), {Bytes((long)size)} uncompressed", []);
    }

    // ---- Anything else ----------------------------------------------------------------------------------------

    private static (string, IReadOnlyList<string>) Generic(HiddenItem item, ReadOnlySpan<byte> v)
    {
        if (item.Size == 0 || v.Length == 0) return ("Empty", []);
        string? text = LooksLikeText(v) ? Text(v) : null;
        if (text is { Length: > 0 })
        {
            string first = text.Split('\n')[0].Trim();
            return (first.Length > 160 ? first[..160] + "…" : first, text.Split('\n').Take(50).Select(l => l.TrimEnd('\r')).ToList());
        }
        return ($"{Bytes(item.Size)} of binary data", [HexLine(v)]);
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> v)
    {
        var sample = v[..Math.Min(v.Length, 4096)];
        if (sample.TrimEnd((byte)0).IndexOf((byte)0) >= 0) return false;
        try
        {
            string s = new UTF8Encoding(false, true).GetString(sample.TrimEnd((byte)0));
            return s.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t');
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static string Text(ReadOnlySpan<byte> v)
    {
        var bytes = v[..Math.Min(v.Length, 64 * 1024)].TrimEnd((byte)0);
        if (bytes.StartsWith("﻿"u8)) bytes = bytes[3..];
        return Encoding.UTF8.GetString(bytes).Trim();
    }

    private static string Printable(ReadOnlySpan<byte> v)
    {
        var text = new StringBuilder();
        foreach (byte b in v[..Math.Min(v.Length, 1024)])
            if (b is >= 0x20 and < 0x7F) text.Append((char)b);
        return text.ToString();
    }

    private static string HexLine(ReadOnlySpan<byte> v) =>
        Convert.ToHexString(v[..Math.Min(v.Length, 64)]).ToLowerInvariant() is var hex && v.Length > 64 ? hex + "…" : Convert.ToHexString(v).ToLowerInvariant();

    private static string UnixMode(uint mode)
    {
        char Kind() => (mode & 0xF000) switch { 0x4000 => 'd', 0xA000 => 'l', 0x2000 => 'c', 0x6000 => 'b', 0x1000 => 'p', 0xC000 => 's', _ => '-' };
        var s = new StringBuilder().Append(Kind());
        for (int shift = 6; shift >= 0; shift -= 3)
        {
            uint bits = mode >> shift & 7;
            s.Append((bits & 4) != 0 ? 'r' : '-').Append((bits & 2) != 0 ? 'w' : '-').Append((bits & 1) != 0 ? 'x' : '-');
        }
        return s.ToString();
    }

    private static DateTime UnixTime(long seconds, uint nanoseconds) =>
        DateTime.UnixEpoch.AddSeconds(Math.Clamp(seconds, -62135596800L, 253402300799L - 1)).AddTicks(Math.Min(nanoseconds, 999_999_999u) / 100).ToLocalTime();

    private static string Bytes(long size) => size switch
    {
        < 0 => "size unknown",
        1 => "1 byte",
        < 1024 => $"{size.ToString(CultureInfo.CurrentCulture)} bytes",
        < 1024 * 1024 => $"{size / 1024.0:0.#} KiB",
        < 1024L * 1024 * 1024 => $"{size / 1048576.0:0.#} MiB",
        _ => $"{size / 1073741824.0:0.##} GiB",
    };
}

/// <summary>Just enough of Apple's binary property lists to read strings and dates (bounded, no recursion limit abuse).</summary>
internal static class BinaryPlist
{
    public static List<string> Strings(ReadOnlySpan<byte> v)
    {
        var result = new List<string>();
        Walk(v, (kind, value) => { if (kind == 's') result.Add((string)value); });
        return result;
    }

    public static List<DateTime> Dates(ReadOnlySpan<byte> v)
    {
        var result = new List<DateTime>();
        Walk(v, (kind, value) => { if (kind == 'd') result.Add((DateTime)value); });
        return result;
    }

    private static void Walk(ReadOnlySpan<byte> v, Action<char, object> found)
    {
        if (v.Length < 40 || !v[..8].SequenceEqual("bplist00"u8)) return;
        var trailer = v[^32..];
        int offsetSize = trailer[6], refSize = trailer[7];
        long count = BinaryPrimitives.ReadInt64BigEndian(trailer[8..]), top = BinaryPrimitives.ReadInt64BigEndian(trailer[16..]);
        long table = BinaryPrimitives.ReadInt64BigEndian(trailer[24..]);
        if (offsetSize is < 1 or > 8 || refSize is < 1 or > 8 || count is < 1 or > 4096 || top >= count || table < 8 || table + count * offsetSize > v.Length - 32) return;
        var bytes = v.ToArray();
        long Read(int at, int size)
        {
            long n = 0;
            for (int i = 0; i < size; i++) n = n << 8 | bytes[at + i];
            return n;
        }
        long OffsetOf(long obj) => Read((int)(table + obj * offsetSize), offsetSize);
        var visiting = new HashSet<long>();
        void Visit(long obj, int depth)
        {
            if (depth > 8 || obj < 0 || obj >= count || !visiting.Add(obj)) return;
            int at = (int)OffsetOf(obj);
            if (at < 8 || at >= table) return;
            int marker = bytes[at], high = marker >> 4, low = marker & 0xF;
            int length = low, start = at + 1;
            if (high is 0x5 or 0x6 or 0xA && low == 0xF)
            {
                int intSize = 1 << (bytes[at + 1] & 0xF);
                length = (int)Read(at + 2, intSize);
                start = at + 2 + intSize;
            }
            switch (high)
            {
                case 0x5 when start + length <= table:
                    found('s', Encoding.ASCII.GetString(bytes, start, length));
                    break;
                case 0x6 when start + 2 * length <= table:
                    found('s', Encoding.BigEndianUnicode.GetString(bytes, start, 2 * length));
                    break;
                case 0x3 when marker == 0x33 && at + 9 <= table:
                    double seconds = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(at + 1)));
                    if (double.IsFinite(seconds) && Math.Abs(seconds) < 1e11) found('d', new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds));
                    break;
                case 0xA when start + length * refSize <= table:
                    for (int i = 0; i < length && i < 256; i++) Visit(Read(start + i * refSize, refSize), depth + 1);
                    break;
            }
        }
        Visit(top, 0);
    }
}
