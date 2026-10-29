using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class NtfsCompressedUnitBudgetTests
{
    /// <param name="length">A larger volume than the image, read as zeros past it; 0 for the image's own length.</param>
    private sealed class PatchedImage(IBlockSource inner, IReadOnlyDictionary<long, byte> patches, long length = 0) : IBlockSource
    {
        public string Description => inner.Description;
        public long Length => length > 0 ? length : inner.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= inner.Length)
            {
                int zeros = (int)Math.Min(buffer.Length, Math.Max(0, Length - offset));
                buffer[..zeros].Clear();
                return zeros;
            }
            int count = inner.Read(offset, buffer);
            for (int i = 0; i < count; i++)
                if (patches.TryGetValue(offset + i, out byte value)) buffer[i] = value;
            return count;
        }
        public void Dispose() => inner.Dispose();
    }

    [Theory]
    [InlineData(100, false, 0L)]
    [InlineData(4096, false, 0L)]
    [InlineData(100_000, true, 0L)]
    // Review finding F-M7 of f134ba37: on a large volume, a compressed file of 12 GiB (200,000 units of 64 KiB) is
    // described, where 100,000 units per file made it its name only.
    [InlineData(200_000, false, 64L << 30)]
    public void Public_scan_bounds_compressed_sparse_claim_to_the_image(int claimedUnits, bool overBudget, long volumeBytes)
    {
        string image = RecoveryFixtures.Image("ntfs");
        byte[] bytes = File.ReadAllBytes(image);
        byte[] name = Encoding.Unicode.GetBytes("gaps.txt");
        var patches = new Dictionary<long, byte>();
        int claimedClusters = claimedUnits * 16; // the image contains only 4,096 clusters
        for (int recordAt = 0; recordAt + 1024 <= bytes.Length; recordAt += 1024)
        {
            var record = bytes.AsSpan(recordAt, 1024);
            if (!record[..4].SequenceEqual("FILE"u8) || record.IndexOf(name) < 0) continue;
            int attrAt = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
            while (attrAt + 72 <= record.Length)
            {
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(record[attrAt..]);
                if (type == uint.MaxValue) break;
                int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(attrAt + 4)..]);
                if (length < 72 || attrAt + length > record.Length) break;
                var attr = record.Slice(attrAt, length);
                if (type == 0x80 && attr[8] == 1 && attr[9] == 0 && attr[34] == 4 &&
                    BinaryPrimitives.ReadInt64LittleEndian(attr[48..]) == 231_072)
                {
                    void Set(long at, ReadOnlySpan<byte> data)
                    { for (int i = 0; i < data.Length; i++) patches[at + i] = data[i]; }
                    long start = recordAt + attrAt;
                    Set(start + 24, BitConverter.GetBytes((long)claimedClusters - 1));
                    Set(start + 48, BitConverter.GetBytes((long)claimedClusters * 4096));
                    Set(start + 56, BitConverter.GetBytes((long)claimedClusters * 4096));
                    int runAt = BinaryPrimitives.ReadUInt16LittleEndian(attr[32..]);
                    Assert.True(runAt + 6 <= length);
                    Set(start + runAt, [4, .. BitConverter.GetBytes(claimedClusters), 0]);
                    break;
                }
                attrAt += length;
            }
            if (patches.Count > 0) break;
        }
        Assert.NotEmpty(patches);
        using var source = new PatchedImage(new ImageFileSource(image), patches, volumeBytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var volume = Assert.Single(RecoveryScanner.Scan(source, TestContext.Current.CancellationToken));
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"NTFS public scan: claimed-units={claimedUnits}, elapsed={watch.Elapsed}, thread-allocated={allocated:N0} bytes");
        var file = RecoveryFixtures.Find(volume.Root, "packed/gaps.txt");
        Assert.NotNull(file);
        if (overBudget)
        {
            Assert.Equal(RecoveryState.NameOnly, file.State);
            Assert.Contains(file.Evidence, e => e.Contains("more units than this image can safely index", StringComparison.Ordinal));
        }
        else
        {
            Assert.NotNull(file.Compression);
            Assert.Equal(claimedUnits, file.Compression.Units.Count);
        }
        Assert.True(allocated < 64L * 1024 * 1024, $"Scan allocated {allocated:N0} bytes.");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Scan took {watch.Elapsed}.");
    }
}
