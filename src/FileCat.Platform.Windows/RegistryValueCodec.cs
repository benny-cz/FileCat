using System.Globalization;
using System.Text;

namespace FileCat.Platform.Windows;

/// <summary>UI conversion only. Stored Registry values always retain the exact native type and bytes.</summary>
public static class RegistryValueCodec
{
    public static readonly uint[] EditableTypes = [1, 2, 7, 4, 11, 3];

    public static string TypeName(uint type) => new RegistryValueData(type, [], 0).TypeName;

    public static string Format(RegistryValueData value, out bool rawOnly)
    {
        rawOnly = false;
        var data = value.Data;
        if (data.Length != value.Length) { rawOnly = true; return ""; }
        if (value.Type is 1 or 2 or 7)
        {
            if ((data.Length & 1) != 0) { rawOnly = true; return Convert.ToHexString(data); }
            var s = Encoding.Unicode.GetString(data);
            if (!Encoding.Unicode.GetBytes(s).AsSpan().SequenceEqual(data))
            {
                rawOnly = true;
                return Convert.ToHexString(data);
            }
            if (value.Type == 7)
            {
                if (!s.EndsWith("\0\0", StringComparison.Ordinal)) { rawOnly = true; return Convert.ToHexString(data); }
                return string.Join(Environment.NewLine, s[..^2].Split('\0'));
            }
            if (!s.EndsWith('\0') || s[..^1].Contains('\0'))
            {
                rawOnly = true;
                return Convert.ToHexString(data);
            }
            return s[..^1];
        }
        if (value.Type == 4 && data.Length == 4) return BitConverter.ToUInt32(data).ToString(CultureInfo.InvariantCulture);
        if (value.Type == 11 && data.Length == 8) return BitConverter.ToUInt64(data).ToString(CultureInfo.InvariantCulture);
        rawOnly = true;
        return Convert.ToHexString(data);
    }

    public static bool TryParse(uint type, string text, bool rawHex, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        if (rawHex || type is not (1 or 2 or 7 or 4 or 11))
        {
            var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c) && c is not ',' and not '-').ToArray());
            if ((cleaned.Length & 1) != 0 || !cleaned.All(Uri.IsHexDigit)) error = "Enter pairs of hexadecimal digits (spaces are allowed).";
            else bytes = Convert.FromHexString(cleaned);
            return error is null;
        }
        if (type is 1 or 2)
        {
            if (text.Contains('\0')) error = "A string cannot contain NUL.";
            else bytes = Encoding.Unicode.GetBytes(text + '\0');
        }
        else if (type == 7)
        {
            var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
            if (lines.Any(s => s.Length == 0 && lines.Length > 1 || s.Contains('\0')))
                error = "Multi-string rows cannot be empty or contain NUL.";
            else bytes = Encoding.Unicode.GetBytes(string.Join('\0', lines) + "\0\0");
        }
        else if (type == 4)
        {
            if (!TryUnsigned(text, uint.MaxValue, out var n)) error = "Enter an unsigned 32-bit number (decimal or 0x hexadecimal).";
            else bytes = BitConverter.GetBytes((uint)n);
        }
        else if (!TryUnsigned(text, ulong.MaxValue, out var n)) error = "Enter an unsigned 64-bit number (decimal or 0x hexadecimal).";
        else bytes = BitConverter.GetBytes(n);
        return error is null;
    }

    private static bool TryUnsigned(string text, ulong max, out ulong value)
    {
        var s = text.Trim();
        bool hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex) s = s[2..];
        return ulong.TryParse(s, hex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;
    }
}
