using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisInventoryTests
{
    private static byte[][] FramedRecords() => Encoding.UTF8.GetString(Write()).TrimEnd('\n').Split('\n').Select(Encoding.UTF8.GetBytes).ToArray();
    [Fact]
    public void DecoderAdmitsRetainedPayloadBeforeDecodingAndDoesNotChargeTheTrailer()
    {
        var lines = FramedRecords(); var admitted = new List<int>();
        using (var decoder = new AnalysisInventoryFormat.RecordDecoder(admitted.Add))
        {
            foreach (var line in lines) decoder.Read(line, TestContext.Current.CancellationToken);
            decoder.Complete(); Assert.Equal(lines[..^1].Select(line => line.Length), admitted);
        }
        var refusal = new IOException("No retained-data capacity.");
        using var denied = new AnalysisInventoryFormat.RecordDecoder(_ => throw refusal);
        // Missing manifest fields would fail typed decoding; admission must refuse first.
        Assert.Same(refusal, Assert.Throws<IOException>(() => denied.Read(Encoding.UTF8.GetBytes("{\"kind\":\"manifest\"}"), TestContext.Current.CancellationToken)));
        Assert.Throws<InvalidOperationException>(() => denied.Read(lines[0], TestContext.Current.CancellationToken));
    }
    [Fact]
    public void DecoderRequiresExplicitVerifiedEofAfterItsTrailerAndNeverRetainsInputBuffers()
    {
        var lines = FramedRecords(); using var decoder = new AnalysisInventoryFormat.RecordDecoder();
        var paths = new List<AnalysisNode>();
        foreach (var line in lines)
        {
            byte[] reusable = line.ToArray(); var record = decoder.Read(reusable, TestContext.Current.CancellationToken);
            if (record.Path is { } node) paths.Add(node);
            reusable.AsSpan().Fill(0xff);
            Assert.False(decoder.IsComplete);
        }
        Assert.Equal(AnalysisAccountingTests.Golden().Nodes.Select(n => n.Name), paths.Select(n => n.Name));
        decoder.Complete(); Assert.True(decoder.IsComplete); decoder.Complete();
        Assert.Throws<InvalidOperationException>(() => decoder.Read(lines[0], TestContext.Current.CancellationToken));
    }
    [Fact]
    public void DecoderDoesNotTurnMissingTrailerOrExtraDataIntoSuccessfulCompletion()
    {
        var lines = FramedRecords();
        using (var decoder = new AnalysisInventoryFormat.RecordDecoder())
        {
            foreach (var line in lines[..^1]) decoder.Read(line, TestContext.Current.CancellationToken);
            Assert.Throws<InvalidDataException>(decoder.Complete); Assert.False(decoder.IsComplete);
            Assert.Throws<InvalidOperationException>(() => decoder.Read(lines[^1], TestContext.Current.CancellationToken));
        }
        using (var decoder = new AnalysisInventoryFormat.RecordDecoder())
        {
            foreach (var line in lines) decoder.Read(line, TestContext.Current.CancellationToken);
            Assert.Throws<InvalidDataException>(() => decoder.Read(lines[0], TestContext.Current.CancellationToken));
            Assert.Throws<InvalidOperationException>(decoder.Complete); Assert.False(decoder.IsComplete);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecoderSchemaFailureOrCancellationCannotResumeFromItsPartlyConsumedState(bool cancel)
    {
        var lines = FramedRecords(); using var decoder = new AnalysisInventoryFormat.RecordDecoder();
        if (cancel)
        {
            using var stop = new CancellationTokenSource(); stop.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.Read(lines[0], stop.Token));
        }
        else Assert.Throws<InvalidDataException>(() => decoder.Read(Encoding.UTF8.GetBytes("{\"kind\":\"manifest\",\"kind\":\"manifest\"}"), TestContext.Current.CancellationToken));
        Assert.False(decoder.IsComplete); Assert.Throws<InvalidOperationException>(() => decoder.Read(lines[0], TestContext.Current.CancellationToken));
    }
}
