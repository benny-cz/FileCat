using System.Text;

namespace FileCat.Core.Content;

/// <summary>
/// Streaming search over paged content with bounded buffers and boundary-spanning matches (plan §11, §13.1).
/// Runs on a background thread; progress reports the byte offset reached.
/// </summary>
public static class ContentSearch
{
    private const int ChunkBytes = 1024 * 1024;

    /// <summary>Finds <paramref name="pattern"/> bytes forward from <paramref name="start"/>; -1 when absent.</summary>
    public static long FindBytes(PagedReader reader, long start, ReadOnlySpan<byte> pattern, CancellationToken ct, Action<long>? progress = null)
    {
        if (pattern.Length == 0) return -1;
        var p = pattern.ToArray();
        var buffer = new byte[ChunkBytes + p.Length];
        long pos = Math.Max(0, start);
        long len = reader.Length;
        while (pos < len)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(pos, buffer.AsSpan(0, (int)Math.Min(buffer.Length, len - pos)), ct);
            if (n < p.Length) return -1;
            int idx = buffer.AsSpan(0, n).IndexOf(p);
            if (idx >= 0) return pos + idx;
            pos += n - p.Length + 1;
            progress?.Invoke(pos);
        }
        return -1;
    }

    /// <summary>Finds the last occurrence starting before <paramref name="before"/>; -1 when absent.</summary>
    public static long FindBytesBackward(PagedReader reader, long before, ReadOnlySpan<byte> pattern, CancellationToken ct)
    {
        if (pattern.Length == 0) return -1;
        var p = pattern.ToArray();
        long end = Math.Min(before + p.Length - 1, reader.Length);
        var buffer = new byte[ChunkBytes + p.Length];
        while (end > 0)
        {
            ct.ThrowIfCancellationRequested();
            long from = Math.Max(0, end - buffer.Length);
            int n = reader.Read(from, buffer.AsSpan(0, (int)(end - from)), ct);
            int idx = buffer.AsSpan(0, n).LastIndexOf(p);
            if (idx >= 0 && from + idx < before) return from + idx;
            if (from == 0) return -1;
            end = from + p.Length - 1;
        }
        return -1;
    }

    /// <summary>
    /// Text search in an encoding. Chunks are decoded independently with an overlap so matches across chunk
    /// boundaries are found; the returned offset is exact for the match start.
    /// </summary>
    public static long FindText(PagedReader reader, Encoding encoding, long start, string pattern, bool matchCase, CancellationToken ct, Action<long>? progress = null)
    {
        if (pattern.Length == 0) return -1;
        int unit = TextDecoding.UnitSize(encoding);
        int overlapBytes = (pattern.Length + 4) * Math.Max(4, encoding.GetMaxByteCount(1));
        var buffer = new byte[ChunkBytes + overlapBytes];
        long pos = Align(Math.Max(0, start), unit);
        long len = reader.Length;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        while (pos < len)
        {
            ct.ThrowIfCancellationRequested();
            int n = reader.Read(pos, buffer.AsSpan(0, (int)Math.Min(buffer.Length, len - pos)), ct);
            if (n <= 0) return -1;
            // For UTF-8, begin at a character boundary.
            int skip = 0;
            if (encoding is UTF8Encoding)
            {
                while (skip < n && skip < 4 && (buffer[skip] & 0xC0) == 0x80) skip++;
            }
            var text = encoding.GetString(buffer, skip, n - skip);
            int idx = text.IndexOf(pattern, comparison);
            if (idx >= 0) return pos + skip + CharIndexToByteOffset(encoding, buffer.AsSpan(skip, n - skip), idx);
            if (pos + n >= len) return -1;
            pos = Align(pos + Math.Max(unit, n - overlapBytes), unit);
            progress?.Invoke(pos);
        }
        return -1;
    }

    private static long Align(long v, int unit) => unit <= 1 ? v : v - v % unit;

    /// <summary>
    /// Byte offset of the <paramref name="charIndex"/>-th decoded character. Re-encoding the decoded prefix is
    /// wrong when the bytes contain invalid sequences (U+FFFD re-encodes to 3 bytes), so UTF-8 is walked with
    /// the same maximal-subpart replacement rule the .NET decoder uses.
    /// </summary>
    public static int CharIndexToByteOffset(Encoding encoding, ReadOnlySpan<byte> bytes, int charIndex)
    {
        if (encoding is UnicodeEncoding) return Math.Min(bytes.Length, charIndex * 2);
        if (encoding.IsSingleByte) return Math.Min(bytes.Length, charIndex);
        if (encoding is not UTF8Encoding) return encoding.GetByteCount(encoding.GetString(bytes).AsSpan(0, charIndex));
        int i = 0, chars = 0;
        while (i < bytes.Length && chars < charIndex)
        {
            byte b = bytes[i];
            int need;
            byte lo = 0x80, hi = 0xBF;
            if (b < 0x80) { i++; chars++; continue; }
            if (b is >= 0xC2 and <= 0xDF) need = 1;
            else if (b == 0xE0) { need = 2; lo = 0xA0; }
            else if (b is >= 0xE1 and <= 0xEC or 0xEE or 0xEF) need = 2;
            else if (b == 0xED) { need = 2; hi = 0x9F; }
            else if (b == 0xF0) { need = 3; lo = 0x90; }
            else if (b is >= 0xF1 and <= 0xF3) need = 3;
            else if (b == 0xF4) { need = 3; hi = 0x8F; }
            else { i++; chars++; continue; } // invalid lead byte: one replacement character
            int k = 1;
            for (; k <= need && i + k < bytes.Length; k++)
            {
                byte c = bytes[i + k];
                byte min = k == 1 ? lo : (byte)0x80, max = k == 1 ? hi : (byte)0xBF;
                if (c < min || c > max) break;
            }
            if (k == need + 1)
            {
                i += k;
                chars += need == 3 ? 2 : 1; // 4-byte sequences decode to a surrogate pair
            }
            else
            {
                i += k; // maximal invalid subpart: one replacement character
                chars++;
            }
        }
        return i;
    }

    /// <summary>Parses "4D 5A 90", "4d5a", or "0x4D,0x5A" into bytes; null when invalid.</summary>
    public static byte[]? ParseHex(string text)
    {
        var cleaned = new StringBuilder();
        foreach (var token in text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            if (t.Length % 2 == 1) t = "0" + t;
            cleaned.Append(t);
        }
        var s = cleaned.ToString();
        if (s.Length == 0 || s.Length % 2 != 0) return null;
        try { return Convert.FromHexString(s); }
        catch (FormatException) { return null; }
    }
}
