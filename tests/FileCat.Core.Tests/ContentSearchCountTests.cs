using System.Text;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ContentSearchCountTests
{
    [Fact]
    public void Byte_count_includes_overlaps_and_chunk_boundary_without_duplication()
    {
        byte[] data = new byte[2 * 64 * 1024 + 8];
        data.AsSpan().Fill((byte)'x');
        "aaa"u8.CopyTo(data.AsSpan(64 * 1024 - 1));
        "aaaa"u8.CopyTo(data.AsSpan(64 * 1024 + 15));
        using var reader = new PagedReader(new MemoryContentSource("bytes", data));
        var count = ContentSearch.CountBytes(reader, "aa"u8, data.Length, 100, CancellationToken.None);
        Assert.Equal(5, count.Matches);
        Assert.True(count.Complete);
        Assert.Equal(data.Length, count.ScannedBytes);
        Assert.Equal(0, ContentSearch.CountBytes(reader, "missing"u8, data.Length, 100, CancellationToken.None).Matches);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void Text_count_honors_encoding_case_overlaps_and_chunk_crossing(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        int unit = TextDecoding.UnitSize(encoding);
        var data = encoding.GetBytes(new string('x', 64 * 1024 / unit - 2) + "aAaA" + new string('x', 20) + "Aa");
        using var reader = new PagedReader(new MemoryContentSource("text", data));
        var insensitive = ContentSearch.CountText(reader, encoding, "aa", false, data.Length, 100, CancellationToken.None);
        Assert.Equal(4, insensitive.Matches);
        Assert.True(insensitive.Complete);
        Assert.Equal(2, ContentSearch.CountText(reader, encoding, "Aa", true, data.Length, 100, CancellationToken.None).Matches);
    }

    [Fact]
    public void Bounds_and_cancellation_never_report_an_incomplete_count_as_exact()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', 3 * 64 * 1024));
        using var reader = new PagedReader(new MemoryContentSource("large", bytes));
        var byteLimit = ContentSearch.CountBytes(reader, "a"u8, 64 * 1024, int.MaxValue, CancellationToken.None);
        Assert.Equal(64 * 1024, byteLimit.Matches);
        Assert.False(byteLimit.Complete);
        var hitLimit = ContentSearch.CountText(reader, Encoding.UTF8, "a", true, bytes.Length, 7, CancellationToken.None);
        Assert.Equal(7, hitLimit.Matches);
        Assert.False(hitLimit.Complete);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ContentSearch.CountText(reader, Encoding.UTF8, "a", true, bytes.Length, 100, canceled.Token));
    }

    [Fact]
    public void Unreadable_content_cannot_produce_an_exact_zero()
    {
        using var reader = new PagedReader(new ShortSource());
        Assert.Throws<EndOfStreamException>(() =>
            ContentSearch.CountBytes(reader, "absent"u8, 100, 100, CancellationToken.None));
        Assert.Throws<EndOfStreamException>(() =>
            ContentSearch.CountText(reader, Encoding.UTF8, "absent", true, 100, 100, CancellationToken.None));
    }

    [Fact]
    public void Malformed_utf8_at_a_chunk_edge_counts_the_same_match_as_find()
    {
        byte[] bytes = new byte[64 * 1024 + 8];
        bytes.AsSpan().Fill((byte)'x');
        bytes[64 * 1024 - 1] = 0xC3;
        using var reader = new PagedReader(new MemoryContentSource("malformed", bytes));
        long found = ContentSearch.FindText(reader, Encoding.UTF8, 0, "\uFFFD", true, CancellationToken.None);
        var count = ContentSearch.CountText(reader, Encoding.UTF8, "\uFFFD", true, bytes.Length, 100,
            CancellationToken.None);
        Assert.Equal(64 * 1024 - 1, found);
        Assert.Equal(1, count.Matches);
        Assert.True(count.Complete);
    }

    private sealed class ShortSource : IContentSource
    {
        public string DisplayName => "short";
        public long Length => 100;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(Length, 0);
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= 3) return 0;
            int length = Math.Min(buffer.Length, 3 - (int)offset);
            "abc"u8.Slice((int)offset, length).CopyTo(buffer);
            return length;
        }
        public void Dispose() { }
    }
}
