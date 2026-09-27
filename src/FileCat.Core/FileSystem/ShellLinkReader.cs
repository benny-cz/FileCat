using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>The target a Windows shortcut records, and whether the target was a folder when the link was made.</summary>
public sealed record ShellLinkTarget(string Path, bool IsDirectory);

/// <summary>
/// Reads the target of a Windows shortcut (.lnk, [MS-SHLLINK]) without the Shell: the raw recorded path, never
/// "resolved", so nothing searches the network or runs Shell extensions. Links to virtual items (Control Panel,
/// This PC) have no file-system path and are not read.
/// </summary>
public static class ShellLinkReader
{
    private const int HeaderSize = 0x4C;
    private const int MaxBytes = 256 * 1024;
    private static readonly Guid LinkClsid = new("00021401-0000-0000-c000-000000000046");

    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint HasName = 0x4;
    private const uint HasRelativePath = 0x8;
    private const uint HasWorkingDir = 0x10;
    private const uint HasArguments = 0x20;
    private const uint HasIconLocation = 0x40;
    private const uint IsUnicode = 0x80;
    private const uint ForceNoLinkInfo = 0x100;
    private const uint HasExpString = 0x200;
    private const uint FileAttributeDirectory = 0x10;
    private const uint EnvironmentBlockSignature = 0xA0000001;

    public static bool TryRead(string linkPath, out ShellLinkTarget? target)
    {
        target = null;
        byte[] data;
        try
        {
            using var stream = new FileStream(linkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < HeaderSize || stream.Length > MaxBytes) return false;
            data = new byte[stream.Length];
            stream.ReadExactly(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        try
        {
            return TryParse(data, linkPath, out target);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or DecoderFallbackException)
        {
            return false; // a malformed link is simply not followed
        }
    }

    internal static bool TryParse(ReadOnlySpan<byte> data, string linkPath, out ShellLinkTarget? target)
    {
        target = null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != HeaderSize || new Guid(data.Slice(4, 16)) != LinkClsid) return false;
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
        bool isDirectory = (BinaryPrimitives.ReadUInt32LittleEndian(data[24..]) & FileAttributeDirectory) != 0;
        int pos = HeaderSize;
        if ((flags & HasLinkTargetIdList) != 0) pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);

        string? path = null;
        if ((flags & HasLinkInfo) != 0 && (flags & ForceNoLinkInfo) == 0)
        {
            int infoSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]));
            path = ReadLinkInfo(data.Slice(pos, infoSize));
            pos += infoSize;
        }
        else if ((flags & HasLinkInfo) != 0)
        {
            pos += checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]));
        }

        // String data: name, relative path, working directory, arguments, icon location (each only when flagged).
        bool unicode = (flags & IsUnicode) != 0;
        string? relative = null;
        if ((flags & HasName) != 0) SkipString(data, ref pos, unicode);
        if ((flags & HasRelativePath) != 0) relative = ReadString(data, ref pos, unicode);
        if ((flags & HasWorkingDir) != 0) SkipString(data, ref pos, unicode);
        if ((flags & HasArguments) != 0) SkipString(data, ref pos, unicode);
        if ((flags & HasIconLocation) != 0) SkipString(data, ref pos, unicode);

        if (path is null && (flags & HasExpString) != 0) path = FindEnvironmentTarget(data, pos);
        if (path is null && relative is not null && Path.GetDirectoryName(linkPath) is { } folder)
            path = Path.GetFullPath(Path.Combine(folder, relative));
        if (string.IsNullOrWhiteSpace(path)) return false;
        target = new ShellLinkTarget(path, isDirectory);
        return true;
    }

    /// <summary>Local base path (Unicode when present) or the network share name, plus the common path suffix.</summary>
    private static string? ReadLinkInfo(ReadOnlySpan<byte> info)
    {
        int headerSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(info[4..]));
        uint infoFlags = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        int localBase = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[16..]);
        int network = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[20..]);
        int suffix = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[24..]);
        bool hasUnicode = headerSize >= 0x24;
        string suffixText = hasUnicode && BinaryPrimitives.ReadUInt32LittleEndian(info[32..]) is var us and > 0
            ? UnicodeZ(info[(int)us..])
            : AnsiZ(info[suffix..]);
        if ((infoFlags & 0x1) != 0)
        {
            string basePath = hasUnicode && BinaryPrimitives.ReadUInt32LittleEndian(info[28..]) is var ub and > 0
                ? UnicodeZ(info[(int)ub..])
                : AnsiZ(info[localBase..]);
            return Join(basePath, suffixText);
        }
        if ((infoFlags & 0x2) != 0)
        {
            var link = info[network..];
            int netNameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(link[8..]);
            string share = netNameOffset > 0x14 && BinaryPrimitives.ReadUInt32LittleEndian(link[20..]) is var un and > 0
                ? UnicodeZ(link[(int)un..])
                : AnsiZ(link[netNameOffset..]);
            return Join(share, suffixText);
        }
        return null;
    }

    private static string Join(string basePath, string suffix) =>
        suffix.Length == 0 ? basePath : basePath.EndsWith('\\') ? basePath + suffix : basePath + "\\" + suffix;

    /// <summary>The environment-variable target ("%USERPROFILE%\Documents"), expanded for this user.</summary>
    private static string? FindEnvironmentTarget(ReadOnlySpan<byte> data, int pos)
    {
        // Extra data blocks follow the string data; the environment block holds the target in both encodings.
        while (pos + 8 <= data.Length)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
            if (size < 8 || pos + size > data.Length) return null;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]) == EnvironmentBlockSignature && size >= 8 + 260 + 520)
            {
                var text = UnicodeZ(data.Slice(pos + 8 + 260, 520));
                if (text.Length == 0) text = AnsiZ(data.Slice(pos + 8, 260));
                return text.Length == 0 ? null : Environment.ExpandEnvironmentVariables(text);
            }
            pos += size;
        }
        return null;
    }

    private static void SkipString(ReadOnlySpan<byte> data, ref int pos, bool unicode)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2 + count * (unicode ? 2 : 1);
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int pos, bool unicode)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2;
        int bytes = count * (unicode ? 2 : 1);
        var text = unicode ? Encoding.Unicode.GetString(data.Slice(pos, bytes)) : Ansi.GetString(data.Slice(pos, bytes));
        pos += bytes;
        return text;
    }

    private static string UnicodeZ(ReadOnlySpan<byte> data)
    {
        int end = 0;
        while (end + 1 < data.Length && (data[end] | data[end + 1]) != 0) end += 2;
        return Encoding.Unicode.GetString(data[..end]);
    }

    private static string AnsiZ(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        return Ansi.GetString(end < 0 ? data : data[..end]);
    }

    /// <summary>The system code page older links use for their non-Unicode strings.</summary>
    private static Encoding Ansi
    {
        get
        {
            if (field is not null) return field;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                field = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                field = Encoding.Latin1;
            }
            return field;
        }
    }
}
