using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task DatasetReadersKeepProfileCapacityAfterQuotaOwnerAndFactoryClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); Guid profile = Guid.NewGuid(); var directory = new MemoryDirectory();
        using var quota = AnalysisProfileQuota.Open(profile); using var competitor = AnalysisProfileQuota.Open(profile);
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory, quota: quota);
        await using var reader = await dataset.AcquireAsync(Cancellation); quota.Dispose(); await dataset.DisposeAsync();
        Assert.False(directory.Closed); Assert.True(quota.ReservedBytes > 0); Assert.False(competitor.TryReserve(AnalysisLimits.ProfileScratchBytes));
        await using var hierarchy = await reader.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        await reader.DisposeAsync(); Assert.Equal((UInt128)80, (await hierarchy.ReadAsync(1, Cancellation)).Totals.Known);
        Assert.False(competitor.TryReserve(AnalysisLimits.ProfileScratchBytes)); await hierarchy.DisposeAsync();
        Assert.True(directory.Closed); Assert.Equal(0, quota.ReservedBytes); Assert.Equal(0, quota.CapacityBytes);
        Assert.True(competitor.TryReserve(AnalysisLimits.ProfileScratchBytes));
    }

    [Fact]
    public async Task RejectedDirectoryFactoryClosesItsQuotaOwnerBeforeReturning()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); Guid profile = Guid.NewGuid(); AnalysisProfileQuota? created = null;
        var snapshot = AnalysisAccountingTests.Golden();
        await Assert.ThrowsAsync<IOException>(() => AnalysisObservationWriter.CreateAsync(io, Device, () => throw new IOException("Controlled private directory refusal."),
            snapshot.Identity, snapshot.Roots, snapshot.Policy, Cancellation, () => created = AnalysisProfileQuota.Open(profile)));
        Assert.NotNull(created); await Assert.ThrowsAsync<ObjectDisposedException>(() => Task.Run(() => created.TryReserve(1), Cancellation));
        using var competitor = AnalysisProfileQuota.Open(profile); Assert.True(competitor.TryReserve(AnalysisLimits.ProfileScratchBytes));
    }

    [Fact]
    public async Task FullProfileRefusesCaptureBeforeItsFirstPayloadWrite()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); Guid profile = Guid.NewGuid();
        using var occupied = AnalysisProfileQuota.Open(profile); Assert.True(occupied.TryReserve(AnalysisLimits.ProfileScratchBytes));
        var directory = new MemoryDirectory(); var snapshot = AnalysisAccountingTests.Golden();
        await using var writer = await AnalysisObservationWriter.CreateAsync(io, Device, () => directory,
            snapshot.Identity, snapshot.Roots, snapshot.Policy, Cancellation, () => AnalysisProfileQuota.Open(profile));
        var root = new AnalysisEntry([], "Root", [82, 111, 111, 116], AnalysisKind.Directory, "fixture:root", AnalysisMeasurement.Known(0),
            AnalysisMeasurement.Missing(MeasurementState.Unsupported), AnalysisMeasurement.Missing(MeasurementState.Unsupported), null, "v1");
        var error = await Assert.ThrowsAsync<AnalysisScratchLimitException>(() => writer.AppendBatchAsync([new(root, 0, "r", "fixture-v1")], Cancellation));
        Assert.Equal(AnalysisScratchLimit.Budget, error.Limit);
        await writer.DisposeAsync();
        Assert.True(directory.Closed); Assert.Equal(0, directory.WrittenBytes);
        occupied.Release(AnalysisLimits.ProfileScratchBytes);
        using var available = AnalysisProfileQuota.Open(profile); Assert.True(available.TryReserve(AnalysisLimits.ProfileScratchBytes));
    }
}
