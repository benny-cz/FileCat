using System.Buffers;
using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Controls;

internal enum ContentCopyRepresentation { SourceText, DisplayedText, HexBytes, PrintableBytes }

/// <summary>The requested extent, the represented bytes, and the reason any bytes were omitted.</summary>
internal sealed record ContentCopyResult(string Text, long RequestedBytes, int CopiedBytes, int Budget,
    ContentCopyRepresentation Representation, bool Capped, bool ShortRead, string? Error)
{
    internal bool CanPublish => Error is null && (CopiedBytes > 0 || RequestedBytes == 0);
    internal string Describe(bool selected = true)
    {
        if (Error is not null) return Error;
        string form = Representation switch
        {
            ContentCopyRepresentation.SourceText => "source text (decoded characters; original tabs and line endings)",
            ContentCopyRepresentation.DisplayedText => "displayed text (tabs expanded and control characters shown as symbols)",
            ContentCopyRepresentation.PrintableBytes => "displayed ASCII text (nonprintable bytes shown as dots)",
            _ => "hex bytes",
        };
        string scope = selected ? "selected" : "in the captured visible lines";
        if (ShortRead)
            return $"INCOMPLETE READING: copied {CopiedBytes:N0} of {RequestedBytes:N0} bytes {scope} as {form}. The source ended or could not be read before the requested range was complete."
                + (Capped ? $" The clipboard budget also limits a copy to the first {Budget:N0} bytes (1 MiB)." : "");
        if (Capped)
            return $"Copied the first {CopiedBytes:N0} of {RequestedBytes:N0} bytes {scope} as {form}. Clipboard limit: {Budget:N0} bytes (1 MiB); the remaining bytes were not copied."
                + (Representation is ContentCopyRepresentation.SourceText or ContentCopyRepresentation.DisplayedText ? " The prefix ends at a decoded character boundary." : "");
        return $"Copied {CopiedBytes:N0} bytes {scope} as {form}.";
    }
}

/// <summary>One bounded copy policy shared by read-only text/hex and the editing host.</summary>
internal static class ContentCopy
{
    internal const int ByteLimit = 1024 * 1024;

    internal static string CanceledMessage(bool publicationStarted) => publicationStarted
        ? "Copy was canceled after a clipboard write began. The clipboard backend may already have received that captured result."
        : "Copy canceled. No new clipboard result was published.";

    internal static ContentCopyResult Read(PagedReader reader, long start, long length,
        ContentCopyRepresentation representation, Encoding? encoding, int displayColumn,
        ContentRevision? expectedRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (start < 0 || length < 0 || start > long.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(start));
        ContentRevision? before = null, after = null;
        long beforeLength = 0, afterLength = 0;
        reader.WithSource(source => { before = source.GetRevision(); beforeLength = source.Length; });
        ct.ThrowIfCancellationRequested();
        if (expectedRevision is not null && before != expectedRevision || beforeLength != reader.Length)
            return Failed("The source changed before Copy. Review the current content and copy again.");
        bool text = representation is ContentCopyRepresentation.SourceText or ContentCopyRepresentation.DisplayedText;
        bool capped = length > ByteLimit;
        int target = (int)Math.Min(length, ByteLimit);
        int requested = (int)Math.Min(length, target + (text && capped ? 16L : 0L));
        var bytes = new byte[requested];
        int read = reader.Read(start, bytes, ct);
        ct.ThrowIfCancellationRequested();
        bool shortRead = read < requested;
        int count = Math.Min(read, target);
        if (text && (capped || shortRead)) count = BoundaryBefore(bytes.AsSpan(0, read), count, encoding!);
        string value = representation switch
        {
            ContentCopyRepresentation.HexBytes => Convert.ToHexString(bytes.AsSpan(0, count)),
            ContentCopyRepresentation.PrintableBytes => new string(bytes.AsSpan(0, count).ToArray().Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.').ToArray()),
            _ => encoding!.GetString(bytes, 0, count),
        };
        if (representation == ContentCopyRepresentation.DisplayedText) value = DisplayText(value, displayColumn);
        reader.WithSource(source => { after = source.GetRevision(); afterLength = source.Length; });
        ct.ThrowIfCancellationRequested();
        if (before != after || beforeLength != afterLength)
            return Failed("The source changed during Copy. Nothing was copied; review the current content and try again.");
        if (representation == ContentCopyRepresentation.SourceText && value.Contains('\0'))
            return Failed("Source text contains U+0000 (NUL). Native text clipboards can stop at NUL, so it was not copied. Choose Copy displayed text for visible symbols, or Hex mode for original byte values.");
        if (read == 0 && length > 0)
            return Failed("INCOMPLETE READING: no bytes could be copied from the requested range. " + (reader.ReadError ?? "The source ended before its reported length."));
        return new(value, length, count, ByteLimit, representation, capped, shortRead, null);

        ContentCopyResult Failed(string why) => new("", length, 0, ByteLimit, representation, length > ByteLimit, false, why);
    }

    /// <summary>Last complete decoder group at or before a byte budget; never infer bytes by re-encoding.</summary>
    internal static int BoundaryBefore(ReadOnlySpan<byte> bytes, int limit, Encoding encoding)
    {
        limit = Math.Min(limit, bytes.Length);
        if (encoding is UTF8Encoding)
        {
            int at = Math.Max(0, limit - 3), boundary = at;
            while (at < bytes.Length && at < limit)
            {
                var status = Rune.DecodeFromUtf8(bytes[at..], out _, out int used);
                if (status == OperationStatus.NeedMoreData) break;
                used = Math.Max(1, used);
                if (at + used > limit) break;
                boundary = at += used;
            }
            return boundary;
        }
        if (encoding is UnicodeEncoding)
        {
            limit -= limit % 2;
            if (limit >= 2)
            {
                bool be = encoding.CodePage == 1201;
                int first = be ? bytes[limit - 2] << 8 | bytes[limit - 1] : bytes[limit - 2] | bytes[limit - 1] << 8;
                if (first is >= 0xD800 and <= 0xDBFF)
                {
                    int next = limit + 1 < bytes.Length ? be ? bytes[limit] << 8 | bytes[limit + 1] : bytes[limit] | bytes[limit + 1] << 8 : -1;
                    if (next == -1 || next is >= 0xDC00 and <= 0xDFFF) limit -= 2;
                }
            }
            return limit;
        }
        if (encoding is UTF32Encoding) return limit - limit % 4;
        if (encoding.IsSingleByte) return limit;
        var segment = TextSourceSegment.Decode(bytes, 0, encoding);
        return (int)(segment.Atoms.LastOrDefault(a => a.ByteEnd <= limit).ByteEnd);
    }

    internal static string DisplayText(string source, int initialColumn = 0)
    {
        var result = new StringBuilder(source.Length);
        int column = initialColumn;
        foreach (char c in source)
        {
            if (c is '\r' or '\n') { result.Append(c); column = 0; }
            else if (c == '\t') { int spaces = 4 - column % 4; result.Append(' ', spaces); column += spaces; }
            else { result.Append(c < 0x20 || c == 0x7F ? c == 0 ? '·' : (char)(0x2400 + (c == 0x7F ? 0x21 : c)) : c); column++; }
        }
        return result.ToString();
    }
}
