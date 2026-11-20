using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisInventoryFramerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    [Fact]
    public void MaximumRecordSpansTurnsWithAtMostOneBoundedSourceReadPerStep()
    {
        byte[] bytes = [.. Enumerable.Repeat((byte)'x', AnalysisLimits.RecordBytes), (byte)'\n', (byte)'y', (byte)'\n'];
        using var source = new FragmentStream(bytes, int.MaxValue); var frame = new AnalysisInventoryFormat.RecordFramer(source);
        int turns = 0;
        while (true)
        {
            int before = source.Calls; var step = frame.Step(Cancellation); turns++;
            Assert.InRange(source.Calls - before, 0, 1); Assert.InRange(step.BytesRead, 0, 64 * 1024); Assert.InRange(source.LargestRequest, 1, 64 * 1024);
            if (step.Status != AnalysisInventoryFormat.FrameStatus.Record) continue;
            Assert.Equal(AnalysisLimits.RecordBytes, frame.Line.Length); Assert.True(frame.Line.Span.IndexOfAnyExcept((byte)'x') < 0); break;
        }
        Assert.True(turns >= 17); Assert.Equal("y", Encoding.UTF8.GetString(frame.Next(Cancellation)!.Value.Span));
        Assert.Null(frame.Next(Cancellation)); Assert.Throws<InvalidOperationException>(() => frame.Line);
        Assert.Equal(AnalysisInventoryFormat.FrameStatus.End, frame.Step(Cancellation).Status);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void FragmentedMultibyteTextEmptyRecordsAndBufferedEofKeepExactFraming(int fragment)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("žluťoučký猫\n\nlast\n"); using var source = new FragmentStream(bytes, fragment);
        var frame = new AnalysisInventoryFormat.RecordFramer(source);
        Assert.Equal("žluťoučký猫", Encoding.UTF8.GetString(frame.Next(Cancellation)!.Value.Span));
        Assert.True(frame.Next(Cancellation)!.Value.IsEmpty); Assert.Equal("last", Encoding.UTF8.GetString(frame.Next(Cancellation)!.Value.Span)); Assert.Null(frame.Next(Cancellation));
    }
    [Theory]
    [InlineData("xxxx\n", 5, 3)]
    [InlineData("xxxx", 4, 4)]
    [InlineData("xxx\n", 3, 4)]
    public void OversizeOrUndelimitedInputCannotBecomeEofOrResume(string text, long limit, int record)
    {
        using var source = new FragmentStream(Encoding.UTF8.GetBytes(text), 2); var frame = new AnalysisInventoryFormat.RecordFramer(source, limit, record);
        Assert.Throws<InvalidDataException>(() => frame.Next(Cancellation));
        Assert.Throws<InvalidOperationException>(() => frame.Step(Cancellation));
    }
    [Fact]
    public void ExactByteAndRecordLimitsAcceptTheirLfButCancellationPoisonsAFramer()
    {
        using var source = new FragmentStream(Encoding.UTF8.GetBytes("xxx\n"), 2); var frame = new AnalysisInventoryFormat.RecordFramer(source, 4, 3);
        Assert.Equal("xxx", Encoding.UTF8.GetString(frame.Next(Cancellation)!.Value.Span)); Assert.Null(frame.Next(Cancellation));
        using var stop = new CancellationTokenSource(); stop.Cancel(); var canceled = new AnalysisInventoryFormat.RecordFramer(new MemoryStream([1, 10]));
        Assert.ThrowsAny<OperationCanceledException>(() => canceled.Step(stop.Token)); Assert.Throws<InvalidOperationException>(() => canceled.Step(Cancellation));
    }
    private sealed class FragmentStream(byte[] bytes, int fragment) : MemoryStream(bytes, writable: false)
    {
        internal int Calls, LargestRequest;
        public override int Read(Span<byte> buffer)
        { Calls++; LargestRequest = Math.Max(LargestRequest, buffer.Length); return base.Read(buffer[..Math.Min(fragment, buffer.Length)]); }
    }
}
