using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ContentSearchBackwardTests
{
    [Theory]
    [InlineData("utf-8", true)]
    [InlineData("utf-8", false)]
    [InlineData("utf-16", true)]
    [InlineData("utf-16", false)]
    [InlineData("utf-16BE", true)]
    [InlineData("iso-8859-1", false)]
    public void Reverse_text_search_exhausts_full_range_and_finds_boundary_matches(string encodingName, bool matchCase)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unit = TextDecoding.UnitSize(encoding);
        var data = encoding.GetBytes(new string('x', 13 * 1024 * 1024 / unit));
        var needle = encoding.GetBytes("Needle-Ä");
        long first = 1024 * 1024 + 42 * unit, second = 8 * 1024 * 1024 - 3 * unit;
        needle.CopyTo(data, first);
        needle.CopyTo(data, second);
        using var reader = new PagedReader(new MemoryContentSource("large", data));
        string query = matchCase ? "Needle-Ä" : "needle-ä";
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(second, ContentSearch.FindTextBackward(reader, encoding, data.Length, query, matchCase, stop.Token));
        Assert.Equal(first, ContentSearch.FindTextBackward(reader, encoding, second, query, matchCase, stop.Token));
        Assert.Equal(-1, ContentSearch.FindTextBackward(reader, encoding, first, query, matchCase, stop.Token));
    }

    [Theory]
    [InlineData("utf-16", false)]
    [InlineData("utf-16", true)]
    [InlineData("utf-16BE", false)]
    [InlineData("utf-16BE", true)]
    public void Utf16_progress_is_encoding_aligned_with_or_without_bom(string name, bool bom)
    {
        var encoding = Encoding.GetEncoding(name);
        byte[] prefix = bom ? encoding.GetPreamble() : [];
        var data = prefix.Concat(encoding.GetBytes("Žlutý cat kůň CAT konec")).ToArray();
        long first = prefix.Length + encoding.GetByteCount("Žlutý ");
        long second = prefix.Length + encoding.GetByteCount("Žlutý cat kůň ");
        using var reader = new PagedReader(new MemoryContentSource("utf16", data));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.Equal(second, ContentSearch.FindTextBackward(reader, encoding, data.Length, "cat", false, stop.Token));
        Assert.Equal(first, ContentSearch.FindTextBackward(reader, encoding, second, "cat", false, stop.Token));
        Assert.Equal(-1, ContentSearch.FindTextBackward(reader, encoding, first, "cat", false, stop.Token));
        Assert.Equal(first, ContentSearch.FindTextBackward(reader, encoding, second, "cat", true, stop.Token));
        // A forward restart inside a code unit must never return a hit before the requested start.
        Assert.Equal(second, ContentSearch.FindText(reader, encoding, first + 1, "cat", false, stop.Token));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("iso-8859-1")]
    public void Reverse_and_forward_search_preserve_overlapping_matches(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        int unit = TextDecoding.UnitSize(encoding);
        byte[] data = encoding.GetBytes("aaaaa");
        using var reader = new PagedReader(new MemoryContentSource("overlap", data));
        Assert.Equal(2 * unit, ContentSearch.FindTextBackward(reader, encoding, data.Length, "aaa", true, CancellationToken.None));
        Assert.Equal(unit, ContentSearch.FindTextBackward(reader, encoding, 2 * unit, "aaa", true, CancellationToken.None));
        Assert.Equal(0, ContentSearch.FindTextBackward(reader, encoding, unit, "aaa", true, CancellationToken.None));
        Assert.Equal(-1, ContentSearch.FindTextBackward(reader, encoding, 0, "aaa", true, CancellationToken.None));
        Assert.Equal(unit, ContentSearch.FindText(reader, encoding, unit, "aaa", true, CancellationToken.None));
    }

    [Fact]
    public void Every_missed_reverse_window_moves_to_an_earlier_boundary_until_zero()
    {
        byte[] data = Encoding.UTF8.GetBytes(new string('x', 13 * 1024 * 1024));
        using var reader = new PagedReader(new MemoryContentSource("range", data));
        long previous = data.Length;
        int windows = 0;
        Assert.Equal(-1, ContentSearch.FindTextBackward(reader, Encoding.UTF8, data.Length, "absent", true, CancellationToken.None, at =>
        {
            Assert.InRange(at, 0, previous - 1);
            previous = at;
            windows++;
        }));
        Assert.Equal(0, previous);
        Assert.InRange(windows, 13, 14);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Window_context_distinguishes_real_replacement_characters_from_split_multibyte_characters(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        int unit = TextDecoding.UnitSize(encoding);
        byte[] data = encoding.GetBytes(new string('x', 3 * 1024 * 1024 / unit));
        // Make the reverse 1 MiB window begin inside this valid character.
        int at = 2 * 1024 * 1024 - unit;
        encoding.GetBytes("😀").CopyTo(data, at);
        using var reader = new PagedReader(new MemoryContentSource("split-scalar", data));
        Assert.Equal(-1, ContentSearch.FindTextBackward(reader, encoding, data.Length, "\uFFFD", true, CancellationToken.None));
        Assert.Equal(-1, ContentSearch.FindText(reader, encoding, at + unit, "\uFFFD", true, CancellationToken.None));
        Assert.Equal(at, ContentSearch.FindTextBackward(reader, encoding, data.Length, "😀", true, CancellationToken.None));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Match_offsets_remain_exact_for_real_invalid_sequences_at_window_edges(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        int unit = TextDecoding.UnitSize(encoding);
        byte[] data = encoding.GetBytes(new string('x', 3 * 1024 * 1024 / unit));
        int at = 2 * 1024 * 1024;
        if (encoding is UTF8Encoding) data[at] = 0x80;
        else
        {
            // An unpaired low surrogate is one actual invalid UTF-16 code unit.
            byte[] invalid = name == "utf-16" ? [0x00, 0xdc] : [0xdc, 0x00];
            invalid.CopyTo(data, at);
        }
        using var reader = new PagedReader(new MemoryContentSource("invalid-sequence", data));
        Assert.Equal(at, ContentSearch.FindTextBackward(reader, encoding, data.Length, "\uFFFD", true, CancellationToken.None));
        Assert.Equal(at, ContentSearch.FindText(reader, encoding, at, "\uFFFD", true, CancellationToken.None));
    }

    [Fact]
    public void Start_beyond_length_is_a_complete_empty_range_without_alignment_overflow()
    {
        using var reader = new PagedReader(new MemoryContentSource("small", Encoding.Unicode.GetBytes("text")));
        Assert.Equal(-1, ContentSearch.FindText(reader, Encoding.Unicode, long.MaxValue, "text", true, CancellationToken.None));
    }

    [Fact]
    public void A_match_that_starts_after_the_cursor_is_never_returned()
    {
        using var reader = new PagedReader(new MemoryContentSource("bounded", "prefix needle"u8.ToArray()));
        Assert.Equal(-1, ContentSearch.FindTextBackward(reader, Encoding.UTF8, 5, "needle", true, CancellationToken.None));
        Assert.Equal(7, ContentSearch.FindTextBackward(reader, Encoding.UTF8, 8, "needle", true, CancellationToken.None));
    }

    [Fact]
    public void Cancellation_stops_before_the_next_reverse_window()
    {
        using var stop = new CancellationTokenSource();
        using var reader = new PagedReader(new MemoryContentSource("cancel", Encoding.UTF8.GetBytes(new string('x', 4 * 1024 * 1024))));
        int windows = 0;
        Assert.Throws<OperationCanceledException>(() => ContentSearch.FindTextBackward(reader, Encoding.UTF8, reader.Length, "absent", true, stop.Token,
            _ => { windows++; stop.Cancel(); }));
        Assert.Equal(1, windows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Incomplete_text_read_is_an_error_instead_of_a_completed_negative_result(bool backward)
    {
        using var reader = new PagedReader(new ShortSource());
        Assert.Throws<EndOfStreamException>(() => backward
            ? ContentSearch.FindTextBackward(reader, Encoding.UTF8, reader.Length, "absent", true, CancellationToken.None)
            : ContentSearch.FindText(reader, Encoding.UTF8, 0, "absent", true, CancellationToken.None));
    }

    private sealed class ShortSource : IContentSource
    {
        public string DisplayName => "short source";
        public long Length => 100;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => null;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= 5) return 0;
            int length = (int)Math.Min(buffer.Length, 5 - offset);
            buffer[..length].Fill((byte)'x');
            return length;
        }
        public void Dispose() { }
    }
}
