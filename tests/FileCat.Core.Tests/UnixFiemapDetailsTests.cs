using System.Buffers.Binary;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed class UnixFiemapDetailsTests
{
    private const int RecordBytes = 56, HeaderBytes = 32;

    private static void Fill(byte[] buffer, int count, uint firstFlags = 0, bool last = false)
    {
        ulong cursor = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16)));
        Assert.Equal((uint)UnixFileRecords.FiemapRecordsPerQuery, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(24)));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), (uint)count);
        for (int i = 0; i < count; i++)
        {
            var record = buffer.AsSpan(HeaderBytes + i * RecordBytes, RecordBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(record, cursor + (ulong)i * 8192);
            BinaryPrimitives.WriteUInt64LittleEndian(record[8..], 4096 + cursor + (ulong)i * 8192);
            BinaryPrimitives.WriteUInt64LittleEndian(record[16..], 4096);
            uint flags = i == 0 ? firstFlags : 0;
            if (last && i + 1 == count) flags |= 1;
            BinaryPrimitives.WriteUInt32LittleEndian(record[40..], flags);
        }
    }

    [Theory]
    [InlineData(0, FactState.Known)]
    [InlineData(1, FactState.Partial)]
    public void Exact_map_limit_is_distinct_from_a_report_with_a_truncated_suffix(int extra, FactState state)
    {
        int remaining = UnixFileRecords.FiemapRecordLimit + extra, calls = 0;
        var result = UnixFileRecords.ReadFiemapPages(buffer =>
        {
            calls++;
            int count = Math.Min(remaining, UnixFileRecords.FiemapRecordsPerQuery);
            remaining -= count;
            Fill(buffer, count, last: remaining == 0);
            return 0;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(state, result.State);
        Assert.Equal(UnixFileRecords.FiemapRecordLimit, result.Value!.Count);
        Assert.InRange(calls, 2, UnixFileRecords.FiemapQueryLimit);
    }

    [Theory]
    [InlineData(0x2u)]
    [InlineData(0x6u)]
    [InlineData(0x8u)]
    [InlineData(0x200u)]
    [InlineData(0x80000000u)]
    public void A_complete_map_can_contain_records_with_unavailable_locations(uint flags)
    {
        var result = UnixFileRecords.ReadFiemapPages(buffer =>
        {
            Fill(buffer, 1, firstFlags: flags, last: true);
            return 0;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(FactState.Known, result.State);
        var extent = Assert.Single(result.Value!);
        Assert.Equal(flags | 1u, extent.Flags);
        Assert.False(extent.HasKnownLocation);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("large count")]
    [InlineData("overlaps")]
    [InlineData("overflow")]
    public void A_later_failure_preserves_only_the_validated_prefix_and_its_sharing_flags(string failure)
    {
        int calls = 0;
        var result = UnixFileRecords.ReadFiemapPages(buffer =>
        {
            if (++calls == 1)
            {
                Fill(buffer, UnixFileRecords.FiemapRecordsPerQuery, firstFlags: 0x2000);
                return 0;
            }
            if (failure == "denied") return 13;
            Fill(buffer, 1, last: true);
            if (failure == "large count") BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), uint.MaxValue);
            if (failure == "overlaps") BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(HeaderBytes), 0);
            if (failure == "overflow") BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(HeaderBytes + 16), ulong.MaxValue);
            return 0;
        }, TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        Assert.Equal(FactState.Partial, result.State);
        Assert.Equal(UnixFileRecords.FiemapRecordsPerQuery, result.Value!.Count);
        Assert.Equal(0x2000u, result.Value[0].Flags);
        Assert.NotEmpty(result.Reason!);
    }

    [Fact]
    public void Cancellation_after_a_completed_query_does_not_start_another()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => UnixFileRecords.ReadFiemapPages(buffer =>
        {
            calls++;
            Fill(buffer, UnixFileRecords.FiemapRecordsPerQuery);
            stop.Cancel();
            return 0;
        }, stop.Token));
        Assert.Equal(1, calls);
    }
}
