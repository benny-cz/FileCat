using System.Buffers;
using System.Globalization;
using System.Text;

namespace FileCat.App.Controls;

/// <summary>
/// A bounded decoded source segment. Every displayed character group retains the bytes consumed by the
/// decoder, including malformed input; display substitutions and wrapping never become source content.
/// A character group (a grapheme, tab, or decoder fallback) is selected as a whole.
/// </summary>
internal sealed class TextSourceSegment
{
    internal readonly record struct Atom(int SourceStart, int SourceLength, int DisplayStart, int DisplayLength,
        long ByteStart, long ByteEnd);

    internal long Start { get; }
    internal long ContentEnd { get; }
    internal long Next { get; }
    internal string Source { get; }
    internal string Ending { get; }
    internal string Display { get; }
    internal IReadOnlyList<Atom> Atoms { get; }
    internal int InitialColumn { get; }
    internal int FinalColumn { get; }

    private TextSourceSegment(long start, int byteCount, int newlineBytes, string source, string ending,
        string display, List<Atom> atoms, int initialColumn, int finalColumn)
    {
        Start = start;
        ContentEnd = start + byteCount;
        Next = ContentEnd + newlineBytes;
        Source = source;
        Ending = ending;
        Display = display;
        Atoms = atoms;
        InitialColumn = initialColumn;
        FinalColumn = finalColumn;
    }

    internal static TextSourceSegment Decode(ReadOnlySpan<byte> bytes, long start, Encoding encoding,
        ReadOnlySpan<byte> newline = default, int initialColumn = 0)
    {
        var source = new StringBuilder();
        var decoded = new List<(int CharStart, int CharLength, int ByteStart, int ByteLength)>();
        int at = 0;
        // The offered UTF and single-byte encodings have explicit scalar/consumption boundaries. The
        // remaining system code pages use Decoder.Convert, retaining indivisible decoder output groups.
        if (encoding is UTF8Encoding || encoding is UnicodeEncoding || encoding is UTF32Encoding || encoding.IsSingleByte)
        {
            while (at < bytes.Length)
            {
                int count;
                if (encoding is UTF8Encoding)
                {
                    var status = Rune.DecodeFromUtf8(bytes[at..], out _, out count);
                    if (status == OperationStatus.NeedMoreData) count = bytes.Length - at;
                    count = Math.Max(1, count);
                }
                else if (encoding is UnicodeEncoding)
                {
                    count = Math.Min(2, bytes.Length - at);
                    bool bigEndian = encoding.CodePage == 1201;
                    if (count == 2 && Unit(bytes, at, bigEndian) is >= 0xD800 and <= 0xDBFF && at + 3 < bytes.Length && Unit(bytes, at + 2, bigEndian) is >= 0xDC00 and <= 0xDFFF)
                        count = 4;
                }
                else count = Math.Min(encoding is UTF32Encoding ? 4 : 1, bytes.Length - at);
                string text = encoding.GetString(bytes.Slice(at, count));
                decoded.Add((source.Length, text.Length, at, count));
                source.Append(text);
                at += count;
            }
        }
        else
        {
            var decoder = encoding.GetDecoder();
            Span<char> chars = stackalloc char[Math.Max(4, encoding.GetMaxCharCount(1))];
            int pending = 0;
            while (at < bytes.Length)
            {
                decoder.Convert(bytes.Slice(at, 1), chars, flush: at + 1 == bytes.Length,
                    out int used, out int written, out _);
                at += used;
                if (written > 0)
                {
                    decoded.Add((source.Length, written, pending, at - pending));
                    source.Append(chars[..written]);
                    pending = at;
                }
                if (used == 0 && written == 0) throw new DecoderFallbackException("The selected decoder made no progress.");
            }
        }

        string original = source.ToString();
        var display = new StringBuilder(original.Length);
        var atoms = new List<Atom>();
        var starts = StringInfo.ParseCombiningCharacters(original);
        int group = 0, col = initialColumn;
        for (int i = 0; i < starts.Length;)
        {
            int charStart = starts[i];
            int charEnd = i + 1 < starts.Length ? starts[i + 1] : original.Length;
            while (group < decoded.Count && decoded[group].CharStart + decoded[group].CharLength <= charStart) group++;
            int first = group;
            while (group + 1 < decoded.Count && decoded[group + 1].CharStart < charEnd) group++;
            // A multi-character fallback or a legacy decoder output group must not be split into
            // different source ranges. Extend this grapheme to include its complete consumption group.
            charEnd = Math.Max(charEnd, decoded[group].CharStart + decoded[group].CharLength);
            while (i + 1 < starts.Length && starts[i + 1] < charEnd) i++;
            int displayStart = display.Length;
            foreach (char c in original.AsSpan(charStart, charEnd - charStart))
            {
                if (c == '\t') { int spaces = 4 - col % 4; display.Append(' ', spaces); col += spaces; }
                else if (c < 0x20 || c == 0x7F)
                {
                    display.Append(c == 0 ? '·' : (char)(0x2400 + (c == 0x7F ? 0x21 : c)));
                    col++;
                }
                else { display.Append(c); col++; }
            }
            var last = decoded[group];
            atoms.Add(new Atom(charStart, charEnd - charStart, displayStart, display.Length - displayStart,
                start + decoded[first].ByteStart, start + last.ByteStart + last.ByteLength));
            i++;
        }
        return new(start, bytes.Length, newline.Length, original, encoding.GetString(newline), display.ToString(), atoms, initialColumn, col % 4);
    }

    internal long ByteAtDisplay(int index, bool trailing = false)
    {
        if (index <= 0) return Start;
        if (index >= Display.Length) return ContentEnd;
        foreach (var atom in Atoms)
        {
            if (index <= atom.DisplayStart) return atom.ByteStart;
            if (index < atom.DisplayStart + atom.DisplayLength)
                return trailing || index - atom.DisplayStart >= atom.DisplayLength / 2.0 ? atom.ByteEnd : atom.ByteStart;
        }
        return ContentEnd;
    }

    internal int DisplayAtByte(long offset, bool trailing = false)
    {
        foreach (var atom in Atoms)
        {
            if (offset <= atom.ByteStart) return atom.DisplayStart;
            if (offset < atom.ByteEnd) return trailing ? atom.DisplayStart + atom.DisplayLength : atom.DisplayStart;
        }
        return Display.Length;
    }

    internal int DisplayAtSource(int index, bool trailing = false)
    {
        int low = 0, high = Atoms.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            var atom = Atoms[middle];
            if (atom.SourceStart + atom.SourceLength <= index) low = middle + 1;
            else high = middle;
        }
        if (low >= Atoms.Count) return Display.Length;
        var found = Atoms[low];
        return index <= found.SourceStart || !trailing ? found.DisplayStart : found.DisplayStart + found.DisplayLength;
    }

    internal int ColumnAtByte(long offset) => (InitialColumn + DisplayAtByte(offset)) % 4;

    private static int Unit(ReadOnlySpan<byte> bytes, int index, bool bigEndian)
        => bigEndian ? bytes[index] << 8 | bytes[index + 1] : bytes[index] | bytes[index + 1] << 8;

    internal string SliceSource(long from, long to)
    {
        var first = Atoms.FirstOrDefault(a => a.ByteEnd > from);
        var last = Atoms.LastOrDefault(a => a.ByteStart < to);
        string text = first.ByteEnd > from && last.ByteEnd > 0
            ? Source.Substring(first.SourceStart, Math.Max(0, last.SourceStart + last.SourceLength - first.SourceStart)) : "";
        if (from <= ContentEnd && to >= Next && Next > ContentEnd) text += Ending;
        return text;
    }
}
