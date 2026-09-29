using System.Globalization;
using System.Text;

namespace FileCat.Core.Search;

/// <summary>
/// The bytes a hex content search looks for (plan §11): pairs of hex digits, with or without spaces, and quoted text
/// among them, which stands for its UTF-8 bytes. <c>4D 5A "This program"</c> and <c>4d5a90</c> are both patterns;
/// <c>""</c> inside quotes is one quote character.
/// </summary>
public static class HexPattern
{
    public static bool TryParse(string? text, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        var result = new List<byte>();
        var digits = new StringBuilder();
        string s = text ?? string.Empty;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"')
            {
                if (!FlushDigits(digits, result, out error)) return false;
                var quoted = new StringBuilder();
                int j = i + 1;
                for (; j < s.Length; j++)
                {
                    if (s[j] != '"') quoted.Append(s[j]);
                    else if (j + 1 < s.Length && s[j + 1] == '"') quoted.Append(s[++j]);
                    else break;
                }
                if (j >= s.Length)
                {
                    error = "A quote is not closed.";
                    return false;
                }
                result.AddRange(Encoding.UTF8.GetBytes(quoted.ToString()));
                i = j;
                continue;
            }
            if (char.IsWhiteSpace(c) || c is ',' or '-')
            {
                if (!FlushDigits(digits, result, out error)) return false;
                continue;
            }
            if (!char.IsAsciiHexDigit(c))
            {
                error = $"\"{c}\" is not a hex digit. Write bytes as hex pairs (4D 5A) and text in quotes (\"MZ\").";
                return false;
            }
            digits.Append(c);
        }
        if (!FlushDigits(digits, result, out error)) return false;
        if (result.Count == 0)
        {
            error = "Enter the bytes to find: hex pairs (4D 5A) or text in quotes.";
            return false;
        }
        bytes = [.. result];
        return true;
    }

    private static bool FlushDigits(StringBuilder digits, List<byte> result, out string? error)
    {
        error = null;
        if (digits.Length == 0) return true;
        if (digits.Length % 2 != 0)
        {
            error = $"\"{digits}\" has an odd number of hex digits; each byte is two digits.";
            return false;
        }
        for (int i = 0; i < digits.Length; i += 2)
            result.Add(byte.Parse(digits.ToString(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        digits.Clear();
        return true;
    }

    /// <summary>The bytes as hex pairs ("4D 5A 90"), for the search's description.</summary>
    public static string Format(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes) is var hex && hex.Length > 0
        ? string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)))
        : string.Empty;
}
