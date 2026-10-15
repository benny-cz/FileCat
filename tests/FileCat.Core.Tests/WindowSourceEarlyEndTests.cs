using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class WindowSourceEarlyEndTests
{
    [Fact]
    public void A_window_refuses_a_partial_underlying_read_before_its_reported_end()
    {
        using var inner = new ControlledSource(1024);
        using var window = new WindowSource(inner, 512, 2048, "controlled volume");
        Assert.Throws<IOException>(() => window.Read(0, new byte[1536]));
    }

    [Fact]
    public void A_window_refuses_zero_at_the_underlying_end_before_its_own_end()
    {
        using var inner = new ControlledSource(1024);
        using var window = new WindowSource(inner, 512, 2048, "controlled volume");
        Assert.Throws<IOException>(() => window.Read(512, new byte[512]));
    }

    [Fact]
    public void A_stable_window_preserves_exact_bytes_clipping_and_normal_end_zero()
    {
        using var inner = new ControlledSource(4096);
        using var window = new WindowSource(inner, 512, 2048, "controlled volume");
        var bytes = new byte[1536];
        Assert.Equal(bytes.Length, window.Read(0, bytes));
        Assert.All(bytes, value => Assert.Equal((byte)0xA5, value));
        var tail = new byte[1024];
        Assert.Equal(512, window.Read(1536, tail));
        Assert.All(tail[..512], value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0, window.Read(2048, tail));
    }

    private sealed class ControlledSource(int servedLength) : IBlockSource
    {
        public string Description => "controlled disk";
        public long Length => 4096;

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= servedLength) return 0;
            int count = Math.Min(buffer.Length, servedLength - (int)offset);
            buffer[..count].Fill(0xA5);
            return count;
        }

        public void Dispose() { }
    }
}
