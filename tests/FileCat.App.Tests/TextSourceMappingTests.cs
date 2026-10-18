using System.Text;
using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>FCUX-012/076: source ranges are decoder-consumed byte ranges, never re-encoded display coordinates.</summary>
public sealed class TextSourceMappingTests
{
    public static TheoryData<string, string, string, int> MalformedCases => new()
    {
        { "utf-8", "418042", "A�B", 2 },
        { "utf-8", "41E28242", "A�B", 3 },
        { "utf-8", "41F09F92A942", "A💩B", 5 },
        { "utf-8", "41EDA08042", "A���B", 4 },
        { "utf-16", "410000D84200", "A�B", 4 },
        { "utf-16BE", "0041D8000042", "A�B", 4 },
        { "utf-32", "4100000042F0110042000000", "A�B", 8 },
        { "iso-8859-1", "418042", "A\u0080B", 2 },
    };

    [Theory]
    [MemberData(nameof(MalformedCases))]
    public void Decoded_atoms_retain_original_offsets_even_after_invalid_sequences(string encodingName, string hex, string expected, int bOffset)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = Convert.FromHexString(hex);
        var segment = TextSourceSegment.Decode(bytes, 0, encoding);
        Assert.Equal(expected, encoding.GetString(bytes));
        Assert.Equal(expected, segment.Source);
        var b = segment.Atoms.Single(a => segment.Source.Substring(a.SourceStart, a.SourceLength) == "B");
        Assert.Equal(bOffset, b.ByteStart);
        Assert.Equal(bytes.Length, b.ByteEnd);
        Assert.Equal("B", segment.SliceSource(b.ByteStart, b.ByteEnd));
        Assert.Equal(bOffset, segment.ByteAtDisplay(b.DisplayStart));
        long prior = 0;
        foreach (var atom in segment.Atoms)
        {
            Assert.Equal(prior, atom.ByteStart);
            Assert.True(atom.ByteEnd > atom.ByteStart);
            prior = atom.ByteEnd;
        }
        Assert.Equal(bytes.Length, prior);
    }

    [Theory]
    [InlineData("A\tB", "A   B")]
    [InlineData("A\0B\u0001C\u007f", "A·B␁C␡")]
    [InlineData("e\u0301💩Z", "e\u0301💩Z")]
    public void Display_atoms_can_expand_without_changing_decoded_source(string original, string displayed)
    {
        var bytes = Encoding.UTF8.GetBytes(original);
        var segment = TextSourceSegment.Decode(bytes, 13, Encoding.UTF8);
        Assert.Equal(original, segment.Source);
        Assert.Equal(displayed, segment.Display);
        Assert.Equal(original, segment.SliceSource(13, 13 + bytes.Length));
        if (original.StartsWith("e", StringComparison.Ordinal))
        {
            Assert.Equal(3, segment.Atoms.Count);
            Assert.Equal(3, segment.Atoms[0].ByteEnd - segment.Atoms[0].ByteStart);
            Assert.Equal(4, segment.Atoms[1].ByteEnd - segment.Atoms[1].ByteStart);
        }
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("")]
    public void Only_real_logical_terminators_belong_to_source_copy(string ending)
    {
        var segment = TextSourceSegment.Decode(Encoding.UTF8.GetBytes("line"), 0, Encoding.UTF8, Encoding.UTF8.GetBytes(ending));
        Assert.Equal("line", segment.Source);
        Assert.Equal("line", segment.Display);
        Assert.Equal("line" + ending, segment.SliceSource(0, segment.Next));
        Assert.Equal(4 + ending.Length, segment.Next);
    }

    [Fact]
    public void System_multibyte_decoder_groups_cover_original_bytes_without_reencoding()
    {
        TextDecoding.EnsureCodePages();
        var encoding = Encoding.GetEncoding(932);
        byte[] bytes = [0x82, 0xA0, 0x82, 0x20, 0x42];
        var segment = TextSourceSegment.Decode(bytes, 0, encoding);
        Assert.Equal(encoding.GetString(bytes), segment.Source);
        Assert.Equal(bytes.Length, segment.Atoms[^1].ByteEnd);
        Assert.Equal("B", segment.SliceSource(4, 5));
    }

    [Theory]
    [InlineData("utf-8", "é", 1)]
    [InlineData("utf-8", "💩", 3)]
    [InlineData("utf-16", "💩", 2)]
    [InlineData("utf-16BE", "💩", 2)]
    public void A_clipboard_cap_cannot_split_a_valid_scalar(string encodingName, string scalar, int prefixShortfall)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unit = TextDecoding.UnitSize(encoding);
        int prefixLength = ContentCopy.ByteLimit - prefixShortfall;
        string prefix = new('x', prefixLength / unit);
        string source = prefix + scalar + "tail";
        byte[] bytes = encoding.GetBytes(source);
        using var reader = new PagedReader(new MemoryContentSource("bounded", bytes));
        var result = ContentCopy.Read(reader, 0, bytes.Length, ContentCopyRepresentation.SourceText, encoding, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.True(result.CanPublish);
        Assert.True(result.Capped);
        Assert.False(result.ShortRead);
        Assert.Equal(prefix, result.Text);
        Assert.Equal(prefixLength, result.CopiedBytes);
        Assert.Contains("character boundary", result.Describe());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_and_display_copy_preserve_real_endings_and_never_add_segment_breaks(bool displayed)
    {
        string source = "A\tB\u0001\r\nnext\n" + new string('x', 40_000) + "tail";
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        using var reader = new PagedReader(new MemoryContentSource("long-line", bytes));
        var result = ContentCopy.Read(reader, 0, bytes.Length, displayed ? ContentCopyRepresentation.DisplayedText : ContentCopyRepresentation.SourceText,
            Encoding.UTF8, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.True(result.CanPublish);
        Assert.False(result.Capped);
        Assert.False(result.ShortRead);
        Assert.Equal(displayed ? "A   B␁\r\nnext\n" + new string('x', 40_000) + "tail" : source, result.Text);
        Assert.Equal(2, result.Text.Count(c => c == '\n'));
        Assert.Equal(bytes, Encoding.UTF8.GetBytes(source));
    }

    [Fact]
    public void Source_NUL_refuses_ambiguous_native_text_but_displayed_copy_is_explicit()
    {
        byte[] bytes = [0x41, 0, 0x42, 1, 0x43];
        using var reader = new PagedReader(new MemoryContentSource("controls", bytes));
        var source = ContentCopy.Read(reader, 0, bytes.Length, ContentCopyRepresentation.SourceText, Encoding.UTF8, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.False(source.CanPublish);
        Assert.Contains("U+0000", source.Describe());
        Assert.Contains("Native text clipboards", source.Describe());
        var displayed = ContentCopy.Read(reader, 0, bytes.Length, ContentCopyRepresentation.DisplayedText, Encoding.UTF8, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.True(displayed.CanPublish);
        Assert.Equal("A·B␁C", displayed.Text);
        var hex = ContentCopy.Read(reader, 0, bytes.Length, ContentCopyRepresentation.HexBytes, null, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.Equal("4100420143", hex.Text);
    }

    [Theory]
    [InlineData("utf-8", 1)]
    [InlineData("utf-16", 2)]
    [InlineData("utf-16BE", 2)]
    public void An_unread_lookahead_is_a_short_read_and_cannot_publish_a_half_character(string encodingName, int partialBytes)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unit = TextDecoding.UnitSize(encoding);
        string prefix = new('x', (ContentCopy.ByteLimit - partialBytes) / unit);
        byte[] all = encoding.GetBytes(prefix + (unit == 1 ? "é" : "💩") + "tail");
        byte[] readable = all[..ContentCopy.ByteLimit];
        using var reader = new PagedReader(new ShortSource(readable, all.Length));
        var result = ContentCopy.Read(reader, 0, all.Length, ContentCopyRepresentation.SourceText, encoding, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.True(result.CanPublish);
        Assert.True(result.Capped);
        Assert.True(result.ShortRead);
        Assert.Equal(prefix, result.Text);
        Assert.Equal(ContentCopy.ByteLimit - partialBytes, result.CopiedBytes);
        Assert.Contains("INCOMPLETE READING", result.Describe());
        Assert.Contains("budget also limits", result.Describe());
        Assert.DoesNotContain('�', result.Text);
    }

    [Theory]
    [InlineData("utf-8", "418042")]
    [InlineData("utf-8", "41E282")]
    [InlineData("utf-16", "410000D8")]
    public void A_complete_source_still_uses_the_selected_decoder_fallback_for_original_malformed_bytes(string encodingName, string hex)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = Convert.FromHexString(hex);
        using var reader = new PagedReader(new MemoryContentSource("original malformed source", bytes));
        var result = ContentCopy.Read(reader, 0, bytes.Length, ContentCopyRepresentation.SourceText, encoding, 0, reader.Revision, TestContext.Current.CancellationToken);
        Assert.Equal(encoding.GetString(bytes), result.Text);
        Assert.False(result.ShortRead);
        Assert.False(result.Capped);
    }

    private sealed class ShortSource(byte[] bytes, long length) : IContentSource
    {
        public string DisplayName => "copy lookahead shortfall";
        public long Length => length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(length, 1);
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, Math.Max(0, bytes.Length - offset));
            if (count > 0) bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }
        public void Dispose() { }
    }
}
