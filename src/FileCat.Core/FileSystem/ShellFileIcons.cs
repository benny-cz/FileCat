using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>
/// What a Windows shortcut, Internet shortcut, or folder customization names for its icon, read without the Shell:
/// the icon's resource file and index, or else the target whose icon stands for it. Nothing named here is opened by
/// these readers; callers decide what may be (plan §8.2: paths inside such files can point at servers).
/// </summary>
/// <param name="IconFile">A resource file (DLL, EXE, ICO) with the icon, environment variables expanded.</param>
/// <param name="TargetPath">What a shortcut points to, when it names a file-system path.</param>
/// <param name="KnownFolder">A shortcut to a known folder (Documents, say), by its ID.</param>
public sealed record ShellFileIcon(string? IconFile, int IconIndex, string? TargetPath, bool TargetIsDirectory, Guid? KnownFolder = null);

public static class ShellFileIcons
{
    /// <summary>Shortcuts are small; larger files are not read as one.</summary>
    public const int MaxBytes = 64 * 1024;

    private static readonly Guid LinkClsid = new("00021401-0000-0000-c000-000000000046");

    private const uint HasLinkTargetIdList = 0x1, HasLinkInfo = 0x2, HasName = 0x4, HasRelativePath = 0x8, HasWorkingDir = 0x10,
        HasArguments = 0x20, HasIconLocation = 0x40, IsUnicode = 0x80, HasExpString = 0x200, HasExpIcon = 0x4000;

    private const uint EnvironmentBlock = 0xA0000001, IconEnvironmentBlock = 0xA0000007, KnownFolderBlock = 0xA000000B;

    /// <summary>A shell link (.lnk, MS-SHLLINK); null when it is not one or is damaged.</summary>
    /// <param name="folder">The shortcut's folder, for a target named relative to it.</param>
    public static ShellFileIcon? ReadShortcut(ReadOnlySpan<byte> data, string folder)
    {
        try
        {
            return Shortcut(data, folder);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static ShellFileIcon? Shortcut(ReadOnlySpan<byte> data, string folder)
    {
        if (data.Length < 0x4C || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x4C || new Guid(data.Slice(4, 16)) != LinkClsid) return null;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x14..]);
        uint targetAttributes = BinaryPrimitives.ReadUInt32LittleEndian(data[0x18..]);
        int iconIndex = BinaryPrimitives.ReadInt32LittleEndian(data[0x38..]);
        int at = 0x4C;
        if ((flags & HasLinkTargetIdList) != 0)
        {
            at = Checked(at + 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[at..]), data.Length);
        }
        string? linkInfoPath = null;
        if ((flags & HasLinkInfo) != 0)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
            var info = data.Slice(at, Checked(size, data.Length - at));
            linkInfoPath = LinkInfoPath(info);
            at += size;
        }
        bool unicode = (flags & IsUnicode) != 0;
        string? relativePath = null, iconLocation = null;
        foreach (uint field in new[] { HasName, HasRelativePath, HasWorkingDir, HasArguments, HasIconLocation })
        {
            if ((flags & field) == 0) continue;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
            int bytes = unicode ? count * 2 : count;
            var text = data.Slice(at + 2, Checked(bytes, data.Length - at - 2));
            string value = unicode ? Encoding.Unicode.GetString(text) : Encoding.Latin1.GetString(text);
            if (field == HasRelativePath) relativePath = value;
            if (field == HasIconLocation) iconLocation = value;
            at += 2 + bytes;
        }
        string? environmentTarget = null, environmentIcon = null;
        Guid? knownFolder = null;
        while (at + 8 <= data.Length)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
            if (size < 8) break;
            var block = data.Slice(at, Checked(size, data.Length - at));
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
            if (signature is EnvironmentBlock or IconEnvironmentBlock && block.Length >= 8 + 260 + 520)
            {
                string value = ZeroTerminated(Encoding.Unicode.GetString(block.Slice(8 + 260, 520)));
                if (value.Length == 0) value = ZeroTerminated(Encoding.Latin1.GetString(block.Slice(8, 260)));
                if (signature == EnvironmentBlock) environmentTarget = value;
                else environmentIcon = value;
            }
            else if (signature == KnownFolderBlock && block.Length >= 8 + 16)
            {
                knownFolder = new Guid(block.Slice(8, 16));
            }
            at += size;
        }

        string? iconFile = (flags & HasExpIcon) != 0 && !string.IsNullOrWhiteSpace(environmentIcon) ? environmentIcon
            : (flags & HasIconLocation) != 0 && !string.IsNullOrWhiteSpace(iconLocation) ? iconLocation : null;
        string? target = (flags & HasExpString) != 0 && !string.IsNullOrWhiteSpace(environmentTarget) ? environmentTarget
            : linkInfoPath ?? (string.IsNullOrWhiteSpace(relativePath) ? null : Path.GetFullPath(Path.Combine(folder, relativePath)));
        return new ShellFileIcon(Expand(iconFile), iconIndex, Expand(target), (targetAttributes & 0x10) != 0, knownFolder);
    }

    /// <summary>The target a LinkInfo structure names: a local path, or a share and the path within it.</summary>
    private static string? LinkInfoPath(ReadOnlySpan<byte> info)
    {
        if (info.Length < 0x1C) return null;
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
        uint infoFlags = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        int localOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[16..]);
        int networkOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[20..]);
        int suffixOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[24..]);
        bool unicodeFields = headerSize >= 0x24 && info.Length >= 0x24;
        string suffix = unicodeFields && BinaryPrimitives.ReadUInt32LittleEndian(info[32..]) is var suffixU and > 0
            ? UnicodeZ(info, (int)suffixU) : AnsiZ(info, suffixOffset);
        if ((infoFlags & 1) != 0)
        {
            string local = unicodeFields && BinaryPrimitives.ReadUInt32LittleEndian(info[28..]) is var localU and > 0
                ? UnicodeZ(info, (int)localU) : AnsiZ(info, localOffset);
            if (local.Length > 0) return suffix.Length == 0 ? local : Path.Combine(local, suffix);
        }
        if ((infoFlags & 2) != 0 && networkOffset > 0 && networkOffset + 0x14 <= info.Length)
        {
            var network = info[networkOffset..];
            int nameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(network[8..]);
            string share = nameOffset > 0x14 && network.Length >= 0x1C && BinaryPrimitives.ReadUInt32LittleEndian(network[0x14..]) is var nameU and > 0
                ? UnicodeZ(network, (int)nameU) : AnsiZ(network, nameOffset);
            if (share.Length > 0) return suffix.Length == 0 ? share : share.TrimEnd('\\') + "\\" + suffix;
        }
        return null;
    }

    /// <summary>An Internet shortcut (.url): its IconFile and IconIndex, if any.</summary>
    public static ShellFileIcon? ReadInternetShortcut(string text, string folder)
    {
        var section = Ini(text, "InternetShortcut");
        if (section is null) return null;
        string? iconFile = section.GetValueOrDefault("IconFile");
        int index = int.TryParse(section.GetValueOrDefault("IconIndex"), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int i) ? i : 0;
        // A web address for an icon is never fetched: only a file path counts.
        if (iconFile is not null && (iconFile.Contains("://", StringComparison.Ordinal) || iconFile.StartsWith("//", StringComparison.Ordinal))) iconFile = null;
        return new ShellFileIcon(Resolve(iconFile, folder), index, null, false);
    }

    /// <summary>
    /// A folder's customization file (desktop.ini): the icon from [.ShellClassInfo] IconResource, or the older
    /// IconFile and IconIndex. Null when it names none.
    /// </summary>
    public static ShellFileIcon? ReadFolderIcon(string text, string folder)
    {
        var section = Ini(text, ".ShellClassInfo");
        if (section is null) return null;
        if (section.GetValueOrDefault("IconResource") is { Length: > 0 } resource)
        {
            int comma = resource.LastIndexOf(',');
            int index = 0;
            if (comma > 0 && int.TryParse(resource.AsSpan(comma + 1).Trim(), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                index = parsed;
                resource = resource[..comma];
            }
            return Resolve(resource, folder) is { } file ? new ShellFileIcon(file, index, null, true) : null;
        }
        if (section.GetValueOrDefault("IconFile") is { Length: > 0 } iconFile)
        {
            int index = int.TryParse(section.GetValueOrDefault("IconIndex"), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int i) ? i : 0;
            return Resolve(iconFile, folder) is { } file ? new ShellFileIcon(file, index, null, true) : null;
        }
        return null;
    }

    /// <summary>Text of a small INI-like file: UTF-16 with a byte order mark, UTF-8 with one, or the ANSI code page.</summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return Encoding.UTF8.GetString(bytes[3..]);
        return Encoding.Latin1.GetString(bytes);
    }

    private static Dictionary<string, string>? Ini(string text, string sectionName)
    {
        Dictionary<string, string>? section = null;
        bool inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inSection = line.TrimEnd(']').Equals("[" + sectionName, StringComparison.OrdinalIgnoreCase);
                if (inSection) section ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || line.StartsWith(';')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0) section![line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
        }
        return section;
    }

    private static string? Resolve(string? path, string folder)
    {
        if (Expand(path) is not { Length: > 0 } expanded) return null;
        return Path.IsPathFullyQualified(expanded) ? expanded : Path.GetFullPath(Path.Combine(folder, expanded));
    }

    private static string? Expand(string? path) =>
        string.IsNullOrWhiteSpace(path) || path.Contains('\0') ? null : Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    private static int Checked(int value, int limit) =>
        value < 0 || value > limit ? throw new ArgumentOutOfRangeException(nameof(value)) : value;

    private static string ZeroTerminated(string s) => s.IndexOf('\0') is var z and >= 0 ? s[..z] : s;

    private static string AnsiZ(ReadOnlySpan<byte> data, int offset)
    {
        if (offset <= 0 || offset >= data.Length) return string.Empty;
        var rest = data[offset..];
        int end = rest.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? rest : rest[..end]);
    }

    private static string UnicodeZ(ReadOnlySpan<byte> data, int offset)
    {
        if (offset <= 0 || offset >= data.Length - 1) return string.Empty;
        var rest = data[offset..];
        int end = 0;
        while (end + 1 < rest.Length && (rest[end] != 0 || rest[end + 1] != 0)) end += 2;
        return Encoding.Unicode.GetString(rest[..end]);
    }
}
