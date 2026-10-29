using System.Buffers.Binary;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class FatTableCapacityTests
{
    private sealed class UndersizedFatImage(Fat32Image source) : IBlockSource
    {
        public string Description => source.Description;
        public long Length => source.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            int count = source.Read(offset, buffer);
            if (offset == 0 && count >= 40) BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(36, 4), 1);
            return count;
        }
        public void Dispose() { }
    }

    [Fact]
    public void Public_recovery_scan_refuses_an_undersized_table_without_a_cluster_array()
    {
        var image = new UndersizedFatImage(new Fat32Image(70_000));
        long before = GC.GetAllocatedBytesForCurrentThread();
        var volumes = RecoveryScanner.Scan(image, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var unavailable = Assert.Single(volumes);
        Assert.Equal("Unknown", unavailable.FileSystem);
        Assert.Contains(unavailable.Warnings, warning => warning.Contains("allocation table is too small", StringComparison.Ordinal));
        Assert.True(allocated < 4 * 1024 * 1024, $"Public recovery refusal allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Exactly_sufficient_fat_accepts_a_sparse_healthy_image()
    {
        var image = new Fat32Image(70_000);
        var boot = new byte[512];
        Assert.Equal(512, image.Read(0, boot));
        var result = FatScanner.Scan(image, boot, new VolumeSlot(0, 0, image.Length, null), TestContext.Current.CancellationToken);
        Assert.Equal("FAT32", result.FileSystem);
    }

    [Fact]
    public void Undersized_fat_is_rejected_before_cluster_array_allocation()
    {
        var image = new Fat32Image(70_000);
        var boot = new byte[512];
        Assert.Equal(512, image.Read(0, boot));
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36), 1);
        var slot = new VolumeSlot(0, 0, image.Length, null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<InvalidDataException>(() => FatScanner.Scan(image, boot, slot, TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Contains("allocation table is too small", error.Message);
        Assert.True(allocated < 4 * 1024 * 1024, $"FAT refusal allocated {allocated:N0} bytes.");
    }
}
