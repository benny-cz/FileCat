using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task RetainedHierarchyReadsAndCleanupKeepProfileDeviceAdmissionAfterDatasetCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var other = new DeviceIoScheduler();
        string primary = "scratch-" + Guid.NewGuid().ToString("N"), profile = "profile-" + Guid.NewGuid().ToString("N");
        var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory, new(primary, profile));
        await using var reader = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await reader.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        await reader.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var held = AnalysisIo.Run(other, profile, _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; }, Cancellation);
        Task<AnalysisHierarchyRow>? page = null; Task? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            page = hierarchy.ReadAsync(1, Cancellation);
            closing = hierarchy.DisposeAsync().AsTask();
            await Task.Delay(40, Cancellation); Assert.False(page.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(directory.Closed);
        }
        finally { release.Set(); await held.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); }
        Assert.Equal((UInt128)80, (await page!.WaitAsync(TimeSpan.FromSeconds(5), Cancellation)).Totals.Known);
        await closing!.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); Assert.True(directory.Closed);
    }
}
