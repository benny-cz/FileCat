using System.Buffers.Binary;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class PartitionTableBoundsTests
{
    [Fact]
    public void Oversized_GPT_entry_table_location_is_refused_before_offset_arithmetic()
    {
        using var source = GptSource(long.MaxValue / 512 + 1, 8, 9);
        var warnings = new List<string>();
        Assert.Empty(PartitionTable.Find(source, warnings));
        Assert.Contains(warnings, w => w.Contains("GPT partition table is damaged", StringComparison.Ordinal));
    }

    [Fact]
    public void GPT_entry_table_must_fit_inside_the_actual_source()
    {
        using var source = GptSource(31, 8, 9, entryCount: 5);
        var warnings = new List<string>();
        Assert.Empty(PartitionTable.Find(source, warnings));
        Assert.Contains(warnings, w => w.Contains("GPT partition table is damaged", StringComparison.Ordinal));
    }

    [Fact]
    public void Oversized_GPT_partition_start_never_becomes_a_negative_volume_offset()
    {
        long hugeLba = long.MaxValue / 512 + 1;
        using var source = GptSource(2, hugeLba, hugeLba);
        var warnings = new List<string>();
        Assert.Empty(PartitionTable.Find(source, warnings));
        Assert.Contains(warnings, w => w.Contains("outside the source", StringComparison.Ordinal));
    }

    [Fact]
    public void GPT_partition_end_is_bounded_by_the_actual_source()
    {
        using var source = GptSource(2, 8, long.MaxValue);
        var warnings = new List<string>();
        var slot = Assert.Single(PartitionTable.Find(source, warnings));
        Assert.Equal(8L * 512, slot.Offset);
        Assert.Equal(source.Length - slot.Offset, slot.Length);
        Assert.Contains(warnings, w => w.Contains("past the end", StringComparison.Ordinal));
    }

    private static MemorySource GptSource(long tableLba, long firstLba, long lastLba, uint entryCount = 1)
    {
        var data = new byte[16 * 1024];
        var header = data.AsSpan(512, 92);
        "EFI PART"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 92);
        BinaryPrimitives.WriteInt64LittleEndian(header[72..], tableLba);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], entryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[84..], 128);
        var entry = data.AsSpan(1024, 128);
        new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7").TryWriteBytes(entry[..16]);
        BinaryPrimitives.WriteInt64LittleEndian(entry[32..], firstLba);
        BinaryPrimitives.WriteInt64LittleEndian(entry[40..], lastLba);
        return new MemorySource(data);
    }

    private sealed class MemorySource(byte[] data) : IBlockSource
    {
        public string Description => "owned malformed GPT";
        public long Length => data.Length;
        public int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset >= data.Length) return 0;
            int count = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, count).CopyTo(buffer);
            return count;
        }
        public void Dispose() { }
    }
}
