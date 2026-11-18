using System.Buffers.Binary;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsAllocatedRangeDetailsTests
{
    private static int Page(byte[] output, long cursor, int count)
    {
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(16 * i), cursor + 2 * i);
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(16 * i + 8), 1);
        }
        return 16 * count;
    }

    [Theory]
    [InlineData(0, FactState.Known)]
    [InlineData(1, FactState.Partial)]
    public void Exact_record_limit_requires_a_complete_terminal_response(int extra, FactState expected)
    {
        int remaining = WindowsFileRecords.AllocatedRangeRecordLimit + extra;
        long eof = remaining * 4L;
        int calls = 0;
        var result = WindowsFileRecords.ReadAllocatedRangePages(eof, (cursor, length, output) =>
        {
            Assert.Equal(eof - cursor, length);
            int count = Math.Min(remaining, output.Length / 16);
            remaining -= count;
            calls++;
            return (remaining > 0 ? 234 : 0, Page(output, cursor, count));
        }, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.State);
        Assert.Equal(WindowsFileRecords.AllocatedRangeRecordLimit, result.Value!.Count);
        Assert.InRange(calls, 2, WindowsFileRecords.AllocatedRangeQueryLimit);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("empty continuation")]
    [InlineData("bad length")]
    [InlineData("overlap")]
    [InlineData("overflow")]
    public void A_later_failure_keeps_the_validated_prefix_without_inventing_a_total(string failure)
    {
        int calls = 0;
        var result = WindowsFileRecords.ReadAllocatedRangePages(256, (cursor, _, output) =>
        {
            if (++calls == 1) return (234, Page(output, 0, 1));
            Assert.Equal(1, cursor);
            if (failure == "denied") return (5, 0);
            if (failure == "empty continuation") return (234, 0);
            if (failure == "bad length") return (0, output.Length + 1);
            if (failure == "overlap") return (0, Page(output, 0, 1));
            int length = Page(output, cursor, 1);
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(8), long.MaxValue);
            return (0, length);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        Assert.Equal(FactState.Partial, result.State);
        Assert.Equal(new WindowsStorageTransforms.AllocatedRange(0, 1), Assert.Single(result.Value!));
        Assert.NotEmpty(result.Reason!);
    }

    [Fact]
    public void Terminal_rounding_is_clamped_and_empty_success_remains_distinct_from_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var outOfBounds = WindowsFileRecords.ReadAllocatedRangePages(7, (_, _, output) =>
        {
            int length = Page(output, 3, 1);
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(8), 40);
            return (0, length);
        }, ct);
        Assert.Equal(FactState.Known, outOfBounds.State);
        Assert.Equal(new WindowsStorageTransforms.AllocatedRange(3, 4), Assert.Single(outOfBounds.Value!));
        var empty = WindowsFileRecords.ReadAllocatedRangePages(7, (_, _, _) => (0, 0), ct);
        Assert.Equal(FactState.Known, empty.State);
        Assert.Empty(empty.Value!);
        var denied = WindowsFileRecords.ReadAllocatedRangePages(7, (_, _, _) => (5, 0), ct);
        Assert.Equal(FactState.Denied, denied.State);
    }

    [Fact]
    public void Query_count_has_its_own_bound_and_cancellation_prevents_the_next_call()
    {
        int calls = 0;
        var ct = TestContext.Current.CancellationToken;
        var limited = WindowsFileRecords.ReadAllocatedRangePages(10_000, (cursor, _, output) =>
        {
            calls++;
            return (234, Page(output, cursor, 1));
        }, ct);
        Assert.Equal(WindowsFileRecords.AllocatedRangeQueryLimit, calls);
        Assert.Equal(FactState.Partial, limited.State);
        Assert.Contains("query inspection limit", limited.Reason);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => WindowsFileRecords.ReadAllocatedRangePages(10_000, (cursor, _, output) =>
        {
            calls++;
            stop.Cancel();
            return (234, Page(output, cursor, 1));
        }, stop.Token));
        Assert.Equal(1, calls);
    }
}
