using System.Globalization;
using System.Text;

namespace FileCat.Core.Content;

/// <summary>Result of looking at a bounded prefix: an encoding choice with the evidence behind it (plan §13.1).</summary>
public sealed record EncodingGuess(Encoding Encoding, int PreambleLength, string Evidence, bool LooksBinary);

public static class TextDecoding
{
    private static bool _registered;

    /// <summary>Makes legacy code pages (Windows-125x, CP437/852...) available.</summary>
    public static void EnsureCodePages()
    {
        if (_registered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _registered = true;
    }

    public static Encoding SystemAnsi
    {
        get
        {
            EnsureCodePages();
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); }
            catch (ArgumentException) { return Encoding.Latin1; }
        }
    }

    public static Encoding Oem
    {
        get
        {
            EnsureCodePages();
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch (ArgumentException) { return Encoding.GetEncoding(437); }
        }
    }

    /// <summary>Encodings offered in the viewer's cycle (F8), in order.</summary>
    public static IReadOnlyList<(string Name, Func<Encoding> Get)> Choices { get; } =
    [
        ("UTF-8", () => new UTF8Encoding(false)),
        ("ANSI", () => SystemAnsi),
        ("OEM", () => Oem),
        ("UTF-16 LE", () => new UnicodeEncoding(false, false)),
        ("UTF-16 BE", () => new UnicodeEncoding(true, false)),
        ("Latin-1", () => Encoding.Latin1),
    ];

    public static EncodingGuess Detect(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
            return new EncodingGuess(new UTF8Encoding(false), 3, "UTF-8 byte-order mark", false);
        if (prefix.Length >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
            return new EncodingGuess(new UnicodeEncoding(false, false), 2, "UTF-16 LE byte-order mark", false);
        if (prefix.Length >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
            return new EncodingGuess(new UnicodeEncoding(true, false), 2, "UTF-16 BE byte-order mark", false);

        int zerosEven = 0, zerosOdd = 0, control = 0, nonAscii = 0;
        for (int i = 0; i < prefix.Length; i++)
        {
            byte b = prefix[i];
            if (b == 0)
            {
                if ((i & 1) == 0) zerosEven++;
                else zerosOdd++;
            }
            else if (b < 0x20 && b is not (9 or 10 or 13 or 12 or 27)) control++;
            else if (b >= 0x80) nonAscii++;
        }
        int n = Math.Max(1, prefix.Length);
        if (zerosOdd > n / 4 && zerosEven < n / 50) return new EncodingGuess(new UnicodeEncoding(false, false), 0, "zero bytes at odd positions suggest UTF-16 LE", false);
        if (zerosEven > n / 4 && zerosOdd < n / 50) return new EncodingGuess(new UnicodeEncoding(true, false), 0, "zero bytes at even positions suggest UTF-16 BE", false);
        bool binary = zerosEven + zerosOdd > 0 || control > n / 20;
        if (binary) return new EncodingGuess(Encoding.Latin1, 0, "zero or control bytes: probably binary", true);
        if (nonAscii == 0) return new EncodingGuess(new UTF8Encoding(false), 0, "plain ASCII", false);
        if (IsValidUtf8(prefix)) return new EncodingGuess(new UTF8Encoding(false), 0, "valid UTF-8 sequences", false);
        return new EncodingGuess(SystemAnsi, 0, $"not valid UTF-8; assuming the system code page ({SystemAnsi.WebName})", false);
    }

    /// <summary>Strict UTF-8 validation that tolerates a sequence cut at the end of the prefix.</summary>
    public static bool IsValidUtf8(ReadOnlySpan<byte> s)
    {
        int i = 0;
        while (i < s.Length)
        {
            byte b = s[i];
            int extra = b < 0x80 ? 0 : (b & 0xE0) == 0xC0 ? 1 : (b & 0xF0) == 0xE0 ? 2 : (b & 0xF8) == 0xF0 ? 3 : -1;
            if (extra < 0 || b is 0xC0 or 0xC1 || b > 0xF4) return false;
            if (i + extra >= s.Length) return true;
            for (int k = 1; k <= extra; k++)
            {
                if ((s[i + k] & 0xC0) != 0x80) return false;
            }
            i += extra + 1;
        }
        return true;
    }

    /// <summary>Byte width of one code unit (for aligning offsets in UTF-16).</summary>
    public static int UnitSize(Encoding e) => e is UnicodeEncoding ? 2 : e is UTF32Encoding ? 4 : 1;
}
