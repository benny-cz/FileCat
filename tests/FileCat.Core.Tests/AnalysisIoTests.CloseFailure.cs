using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisIoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledNativeCloseIsUnconfirmedAndNeverRetriedAsQueuedShutdownWork(bool stopped)
    {
        using var io = new DeviceIoScheduler(); if (stopped) io.Dispose();
        var original = new OperationCanceledException("injected native close result");
        var source = new ClosingSource(() => throw original);
        var error = await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => AnalysisIo.ReleaseAsync(io, source));
        Assert.Same(original, error.InnerException); Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task CloseFailuresRemainDistinctFromOrdinaryWorkFailuresAndKeepPrivateDetailsOutOfTheirPublicMessage()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var original = new IOException("PRIVATE_CLOSE_PATH_CANARY");
        var ordinary = await Assert.ThrowsAsync<IOException>(() => AnalysisIo.Run<int>(io, Key(), _ => throw original, Cancellation));
        Assert.Same(original, ordinary); Assert.False(AnalysisCloseUnconfirmedException.IsPresent(ordinary));
        var directory = new ClosingDirectory(() => throw original);
        var error = await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => AnalysisIo.ReleaseAsync(io, Key(), directory));
        Assert.Same(original, error.InnerException); Assert.Equal(1, directory.Calls); Assert.DoesNotContain("PRIVATE_CLOSE_PATH_CANARY", error.Message);
        Assert.True(AnalysisCloseUnconfirmedException.IsPresent(new IOException("wrapper", error)));
        Assert.True(AnalysisCloseUnconfirmedException.IsPresent(new AggregateException(ordinary, error)));
        Assert.False(AnalysisCloseUnconfirmedException.IsPresent(new AggregateException(ordinary, new InvalidDataException("bad input"))));
    }
}
