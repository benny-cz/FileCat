using System.Text;

namespace FileCat.Core.Content;

/// <summary>
/// Streaming search over paged content with bounded buffers and boundary-spanning matches (plan §11, §13.1).
/// Runs on a background thread; progress reports the byte offset reached.
/// </summary>
public static class ContentSearch
{
    private const int ChunkBytes = 1024 * 1024;
    private const int CountChunkBytes = 64 * 1024;

    /// <summary>A bounded match count. An incomplete count is a lower bound, never a file total.</summary>
    public readonly record struct MatchCount(int Matches, bool Complete, long ScannedBytes);

    /// <summary>Counts overlapping byte matches in a bounded prefix without retaining match positions.</summary>
    public static MatchCount CountBytes(PagedReader reader, ReadOnlySpan<byte> pattern, long maxBytes, int maxMatches,
        CancellationToken ct)
    {
        if (pattern.Length == 0) return new MatchCount(0, true, 0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMatches);
        byte[] needle = pattern.ToArray();
        byte[] buffer = new byte[checked(CountChunkBytes + needle.Length - 1)];
        long length = reader.Length, limit = Math.Min(length, maxBytes);
        int matches = 0;
        for (long pos = 0; pos < limit;)
        {
            ct.ThrowIfCancellationRequested();
            long mainEnd = pos + Math.Min(CountChunkBytes, limit - pos);
            long end = mainEnd + Math.Min(needle.Length - 1L, length - mainEnd);
            int requested = checked((int)(end - pos));
            int read = reader.Read(pos, buffer.AsSpan(0, requested), ct);
            RequireCompleteRead(reader, read, requested);
            int index = 0;
            while (index <= read - needle.Length)
            {
                int next = buffer.AsSpan(index, read - index).IndexOf(needle);
                if (next < 0) break;
                index += next;
                if (pos + index >= mainEnd) break;
                if (++matches >= maxMatches) return new MatchCount(matches, false, pos + index + 1);
                index++; // overlapping occurrences are distinct matches
            }
            pos = mainEnd;
        }
        return new MatchCount(matches, limit == length, limit);
    }

    /// <summary>Counts overlapping decoded-text matches with the same case and chunk-boundary rules as FindText.</summary>
    public static MatchCount CountText(PagedReader reader, Encoding encoding, string pattern, bool matchCase,
        long maxBytes, int maxMatches, CancellationToken ct)
    {
        if (pattern.Length == 0) return new MatchCount(0, true, 0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMatches);
        int unit = TextDecoding.UnitSize(encoding);
        int overlap = checked(encoding.GetMaxByteCount(checked(pattern.Length + 2)) + 4);
        int context = Math.Max(4, encoding.GetMaxByteCount(1));
        byte[] buffer = new byte[checked(CountChunkBytes + overlap + context + unit)];
        long length = reader.Length, limit = Math.Min(length, maxBytes);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int matches = 0;
        for (long pos = 0; pos < limit;)
        {
            ct.ThrowIfCancellationRequested();
            long from = Align(Math.Max(0, pos - context), unit);
            long mainEnd = pos + Math.Min(CountChunkBytes, limit - pos);
            long end = mainEnd + Math.Min(overlap, length - mainEnd);
            if (end < length) end = Math.Min(length, AlignUp(end, unit));
            int requested = checked((int)(end - from));
            int read = reader.Read(from, buffer.AsSpan(0, requested), ct);
            RequireCompleteRead(reader, read, requested);
            string decoded = encoding.GetString(buffer, 0, read);
            for (int index = decoded.IndexOf(pattern, comparison); index >= 0;
                 index = decoded.IndexOf(pattern, index + 1, comparison))
            {
                ct.ThrowIfCancellationRequested();
                long found = from + CharIndexToByteOffset(encoding, buffer.AsSpan(0, read), index);
                if (found >= mainEnd) break;
                if (found < pos) continue;
                if (++matches >= maxMatches) return new MatchCount(matches, false, found + 1);
            }
            pos = mainEnd;
        }
        return new MatchCount(matches, limit == length, limit);
    }

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
            int requested = (int)Math.Min(buffer.Length, len - pos);
            int n = reader.Read(pos, buffer.AsSpan(0, requested), ct);
            RequireCompleteRead(reader, n, requested);
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
            int requested = (int)(end - from);
            int n = reader.Read(from, buffer.AsSpan(0, requested), ct);
            RequireCompleteRead(reader, n, requested);
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
        ct.ThrowIfCancellationRequested();
        if (pattern.Length == 0) return -1;
        long len = reader.Length;
        if (start >= len) return -1;
        int unit = TextDecoding.UnitSize(encoding);
        int overlapBytes = checked(encoding.GetMaxByteCount(checked(pattern.Length + 2)) + 4);
        int context = Math.Max(4, encoding.GetMaxByteCount(1));
        var buffer = new byte[checked(ChunkBytes + overlapBytes + context + unit)];
        long pos = AlignUp(Math.Max(0, start), unit);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        while (pos < len)
        {
            ct.ThrowIfCancellationRequested();
            // Decode a short prefix as context. A window/start inside a valid UTF-8 sequence or UTF-16
            // surrogate pair must not invent a replacement character; actual malformed input still matches.
            long from = Align(Math.Max(0, pos - context), unit);
            long end = pos + Math.Min(checked(ChunkBytes + overlapBytes), len - pos);
            long matchLimit = end == len ? end : pos + ChunkBytes;
            int requested = (int)(end - from);
            int n = reader.Read(from, buffer.AsSpan(0, requested), ct);
            RequireCompleteRead(reader, n, requested);
            var text = encoding.GetString(buffer, 0, n);
            int idx = text.IndexOf(pattern, comparison);
            while (idx >= 0)
            {
                long found = from + CharIndexToByteOffset(encoding, buffer.AsSpan(0, n), idx);
                if (found >= matchLimit) break;
                if (found >= pos) return found;
                idx = text.IndexOf(pattern, idx + 1, comparison);
            }
            if (end >= len) return -1;
            pos += ChunkBytes;
            progress?.Invoke(pos);
        }
        return -1;
    }

    /// <summary>
    /// Finds the last text occurrence whose start precedes <paramref name="before"/>, including overlapping
    /// matches. Reverse windows bound memory, not the searchable range; each miss moves to an earlier window.
    /// Decoding respects encoding units and enough look-ahead is retained for a match crossing a window or
    /// the starting cursor. Case-insensitive matching uses the same ordinal Unicode policy as forward search.
    /// Throws on cancellation or an unreadable/truncated range instead of claiming a complete negative result.
    /// </summary>
    public static long FindTextBackward(PagedReader reader, Encoding encoding, long before, string pattern, bool matchCase,
        CancellationToken ct, Action<long>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        if (pattern.Length == 0 || before <= 0) return -1;
        int unit = TextDecoding.UnitSize(encoding);
        int overlap = checked(encoding.GetMaxByteCount(checked(pattern.Length + 2)) + 4);
        int context = Math.Max(4, encoding.GetMaxByteCount(1));
        var buffer = new byte[checked(ChunkBytes + overlap + context + unit)];
        long length = reader.Length;
        long boundary = Math.Min(before, length);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        while (boundary > 0)
        {
            ct.ThrowIfCancellationRequested();
            long windowStart = Align(Math.Max(0, boundary - ChunkBytes), unit);
            long from = Align(Math.Max(0, windowStart - context), unit);
            long readEnd = boundary + Math.Min(overlap, length - boundary);
            // Avoid a synthetic replacement character caused solely by cutting a UTF-16/32 code unit.
            if (readEnd < length) readEnd = Align(readEnd, unit);
            int requested = (int)(readEnd - from);
            int n = reader.Read(from, buffer.AsSpan(0, requested), ct);
            RequireCompleteRead(reader, n, requested);
            var decoded = encoding.GetString(buffer, 0, n);
            int precedingBytes = (int)Math.Clamp(boundary - from, 0, n);
            int precedingChars = encoding.GetCharCount(buffer, 0, precedingBytes);
            // LastIndexOf bounds the end of a potential match. Permit overlap across the cursor while
            // ensuring its start precedes that cursor; mapping is performed once for the chosen candidate.
            int lastCharacter = Math.Min(decoded.Length - 1, checked(precedingChars + pattern.Length - 2));
            while (precedingChars > 0 && lastCharacter >= 0)
            {
                ct.ThrowIfCancellationRequested();
                int index = decoded.LastIndexOf(pattern, lastCharacter, comparison);
                if (index < 0) break;
                long found = from + CharIndexToByteOffset(encoding, buffer.AsSpan(0, n), index);
                if (found < windowStart) break;
                if (found < boundary) return found;
                lastCharacter = index - 1;
            }
            // windowStart is strictly less than boundary; no hit is ever used as a byte +1 restart.
            boundary = windowStart;
            progress?.Invoke(boundary);
        }
        return -1;
    }

    private static void RequireCompleteRead(PagedReader reader, int read, int requested)
    {
        if (read != requested)
            throw new EndOfStreamException(reader.ReadError is { } error
                ? "Search did not finish because some content could not be read: " + error
                : "Search did not finish because the content ended before its reported length.");
    }

    private static long Align(long v, int unit) => unit <= 1 ? v : v - v % unit;

    private static long AlignUp(long v, int unit) => unit <= 1 || v % unit == 0 ? v : checked(v + unit - v % unit);

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
