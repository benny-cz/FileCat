using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class CachedSourceEarlyEndTests
{
    [Fact]
    public void A_short_underlying_block_is_not_returned_or_cached_as_a_normal_end()
    {
        using var inner = new ChangingLengthSource(1024);
        using var cached = new CachedSource(inner, blockSize: 2048);
        var bytes = new byte[1536];

        Assert.Throws<IOException>(() => cached.Read(0, bytes));

        inner.ServedLength = 2048;
        Assert.Equal(bytes.Length, cached.Read(0, bytes));
        Assert.All(bytes, value => Assert.Equal((byte)0xA5, value));
    }

    [Fact]
    public void A_short_direct_read_is_not_returned_as_a_normal_end()
    {
        using var inner = new ChangingLengthSource(1024);
        using var cached = new CachedSource(inner);
        Assert.Throws<IOException>(() => cached.ReadDirect(0, new byte[1536]));
    }

    [Fact]
    public void A_short_large_read_is_not_returned_as_a_normal_end()
    {
        using var inner = new ChangingLengthSource(1024);
        using var cached = new CachedSource(inner, blockSize: 512);
        Assert.Throws<IOException>(() => cached.Read(0, new byte[1536]));
    }

    [Fact]
    public void A_stable_underlying_source_keeps_exact_bytes_and_normal_end_zero()
    {
        using var inner = new ChangingLengthSource(2048);
        using var cached = new CachedSource(inner, blockSize: 2048);
        var bytes = new byte[1536];
        Assert.Equal(bytes.Length, cached.Read(0, bytes));
        Assert.All(bytes, value => Assert.Equal((byte)0xA5, value));
        var tail = new byte[1024];
        Assert.Equal(512, cached.Read(1536, tail));
        Assert.All(tail[..512], value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0, cached.Read(2048, tail));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stable_direct_or_large_read_clips_only_at_the_reported_end(bool direct)
    {
        using var inner = new ChangingLengthSource(2048);
        using var cached = new CachedSource(inner, blockSize: 512);
        var bytes = new byte[1536];
        Assert.Equal(bytes.Length, direct ? cached.ReadDirect(0, bytes) : cached.Read(0, bytes));
        Assert.All(bytes, value => Assert.Equal((byte)0xA5, value));
        var tail = new byte[1024];
        Assert.Equal(512, direct ? cached.ReadDirect(1536, tail) : cached.Read(1536, tail));
        Assert.All(tail[..512], value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0, direct ? cached.ReadDirect(2048, tail) : cached.Read(2048, tail));
    }

    private sealed class ChangingLengthSource(int servedLength) : IBlockSource
    {
        public int ServedLength { get; set; } = servedLength;
        public string Description => "controlled source";
        public long Length => 2048;

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= ServedLength) return 0;
            int count = Math.Min(buffer.Length, ServedLength - (int)offset);
            buffer[..count].Fill(0xA5);
            return count;
        }

        public void Dispose() { }
    }
}
