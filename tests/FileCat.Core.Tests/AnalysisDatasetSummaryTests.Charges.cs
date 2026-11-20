using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task IndexedObjectChargesPreserveGoldenCommonAncestorAndOutliveDatasetOwners(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        var expected = await lease.SummarizeAsync(metric, Cancellation); await using var charges = await lease.BuildObjectChargesAsync(metric, Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed); Equal(expected.Totals, charges.Totals);
        var shared = Assert.Single((await charges.ReadChargesAsync(1, cancellation: Cancellation)).Rows);
        Assert.True(shared.Shared); Assert.False(shared.IdentityUnverified); Assert.Equal(2UL, shared.ObservedAliases);
        Assert.Equal(4UL, shared.RepresentativePathId); Assert.Equal("X", shared.ObjectId); Assert.True(charges.ContributesChartArea(shared));
        Assert.Equal((UInt128)(metric == AnalysisMetric.OnDisk ? 80 : 100), shared.Measurement.Value);
        Assert.Empty((await charges.ReadChargesAsync(3, cancellation: Cancellation)).Rows);
        var single = Assert.Single((await charges.ReadChargesAsync(2, cancellation: Cancellation)).Rows); Assert.False(single.Shared); Assert.Equal(1UL, single.ObservedAliases);
        if (metric == AnalysisMetric.OnDisk) { Assert.Equal("primary", shared.ComponentId); Assert.Equal(MeasurementState.Unsupported, single.Measurement.State); Assert.False(charges.ContributesChartArea(single)); }
        await charges.DisposeAsync(); Assert.True(directory.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => charges.ReadChargesAsync(1, cancellation: Cancellation));
    }

    [Theory]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task ObjectChargesCoalesceExactIdentityAcrossObjectRecordsAndPromoteConflictsToChanged(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var original = AnalysisAccountingTests.Golden();
        var snapshot = original with
        {
            Nodes = original.Nodes.SetItem(4, original.Nodes[4] with { ObjectId = "X2" }),
            Objects = original.Objects.Add(original.Objects[0] with { Id = "X2" }),
            Facts = original.Facts.Add(original.Facts[0] with { Id = "f3", ObjectId = "X2" })
        };
        await using (var dataset = await Build(snapshot, io))
        await using (var lease = await dataset.AcquireAsync(Cancellation))
        await using (var charges = await lease.BuildObjectChargesAsync(metric, Cancellation))
        {
            Equal((await lease.SummarizeAsync(metric, Cancellation)).Totals, charges.Totals);
            Assert.Equal(2UL, Assert.Single((await charges.ReadChargesAsync(1, cancellation: Cancellation)).Rows).ObservedAliases);
        }
        snapshot = snapshot with { Objects = snapshot.Objects.SetItem(2, snapshot.Objects[2] with { VersionEvidence = "changed" }).SetItem(0, snapshot.Objects[0] with { VersionEvidence = "original" }) };
        // A conflicting version invalidates its measurements as a whole, including disagreeing allocation.
        snapshot = snapshot with { Facts = snapshot.Facts.SetItem(2, snapshot.Facts[2] with { Measurement = AnalysisMeasurement.Known(81) }) };
        await using var changedDataset = await Build(snapshot, io); await using var changedLease = await changedDataset.AcquireAsync(Cancellation);
        await using var changed = await changedLease.BuildObjectChargesAsync(metric, Cancellation);
        var row = Assert.Single((await changed.ReadChargesAsync(1, cancellation: Cancellation)).Rows);
        Assert.Equal(MeasurementState.Changed, row.Measurement.State); Assert.False(changed.ContributesChartArea(row)); Assert.Equal(1UL, changed.Totals.Missing[MeasurementState.Changed]);
        Equal((await changedLease.SummarizeAsync(metric, Cancellation)).Totals, changed.Totals);
    }

    [Theory]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task UnknownIdentityProducesIndependentPathEvidenceWithoutAssertingUniqueArea(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { VerifiedIdentity = null }, snapshot.Objects[1]] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var charges = await lease.BuildObjectChargesAsync(metric, Cancellation);
        Equal((await lease.SummarizeAsync(metric, Cancellation)).Totals, charges.Totals); Assert.Empty((await charges.ReadChargesAsync(1, cancellation: Cancellation)).Rows);
        var a = await charges.ReadChargesAsync(2, maximum: 1, cancellation: Cancellation); Assert.True(a.HasMore); Assert.Equal(2UL, a.Total);
        var unknown = Assert.Single(a.Rows); Assert.True(unknown.IdentityUnverified); Assert.False(unknown.Shared); Assert.Equal(1UL, unknown.ObservedAliases);
        Assert.Equal(metric == AnalysisMetric.OnDisk, charges.ContributesChartArea(unknown));
        var b = Assert.Single((await charges.ReadChargesAsync(3, cancellation: Cancellation)).Rows); Assert.Equal(5UL, b.RepresentativePathId);
        Assert.True(b.IdentityUnverified); Assert.Equal(unknown.Measurement, b.Measurement);
    }

    [Fact]
    public async Task DirectorySelfAndCompoundComponentsRemainSeparateNonFileCharges()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        var total = snapshot.Facts[0] with { Id = "total", ComponentId = "whole", Measurement = AnalysisMeasurement.Known(90), Includes = ["primary"] };
        snapshot = snapshot with { Facts = snapshot.Facts.Add(total).Add(new("dir-a", null, 2, "directory", "fixture-v1", AnalysisMeasurement.Known(30), "fixture", DateTimeOffset.UnixEpoch, []))
            .Add(new("dir-b", null, 3, "directory", "fixture-v1", AnalysisMeasurement.Known(40), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var charges = await lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation);
        Equal((await lease.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation)).Totals, charges.Totals);
        Assert.Equal((UInt128)90, Assert.Single((await charges.ReadChargesAsync(1, cancellation: Cancellation)).Rows).Measurement.Value);
        var a = await charges.ReadChargesAsync(2, cancellation: Cancellation); Assert.Equal(2UL, a.Total); var self = a.Rows[0];
        Assert.True(self.DirectorySelf); Assert.Null(self.ObjectId); Assert.Equal(0UL, self.ObservedAliases); Assert.Equal(2UL, self.RepresentativePathId);
        Assert.Equal((UInt128)30, self.Measurement.Value); Assert.True(self.IdentityUnverified);
        Assert.True(Assert.Single((await charges.ReadChargesAsync(3, cancellation: Cancellation)).Rows).DirectorySelf);
    }

    [Fact]
    public async Task CrossRootSharedChargeUsesVirtualParentAndIncompatibleAllocationHasNoCombinedPage()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Roots = snapshot.Roots.Add(snapshot.Roots[0] with { Id = "r2", Label = "Other", DisplayLocator = "Other", CanonicalLocator = "Other" }),
            Nodes = snapshot.Nodes.Add(new(7, "r2", 0, "Other", AnalysisKind.Directory)).Add(new(8, "r2", 7, "x2.bin", AnalysisKind.File, "X"))
        };
        await using (var dataset = await Build(snapshot, io))
        await using (var lease = await dataset.AcquireAsync(Cancellation))
        await using (var charges = await lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation))
        {
            var shared = Assert.Single((await charges.ReadChargesAsync(0, cancellation: Cancellation)).Rows); Assert.Equal(3UL, shared.ObservedAliases);
            Assert.True(shared.Shared); Assert.Equal((UInt128)80, charges.Totals.Known);
        }
        snapshot = snapshot with { Facts = snapshot.Facts.Add(new("dir-other", null, 7, "directory", "other-basis", AnalysisMeasurement.Known(50), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var other = await Build(snapshot, io); await using var otherLease = await other.AcquireAsync(Cancellation);
        await using var incompatible = await otherLease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation);
        Assert.False(incompatible.Totals.CompatibleBasis); Assert.Equal((UInt128)0, incompatible.Totals.Known);
        Assert.Empty((await incompatible.ReadChargesAsync(0, cancellation: Cancellation)).Rows); Assert.Empty((await incompatible.ReadChargesAsync(7, cancellation: Cancellation)).Rows);
    }

    [Fact]
    public async Task ThousandsOfAliasesUseOneChargeAndYieldToInteractiveWork()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var original = AnalysisAccountingTests.Golden(); var directory = new MemoryDirectory();
        var nodes = original.Nodes.Take(3).Concat(Enumerable.Range(0, 3000).Select(n => new AnalysisNode((ulong)n + 4, "r", (ulong)(n % 2) + 2, "alias-" + n, AnalysisKind.File, "X"))).ToImmutableArray();
        var snapshot = original with { Nodes = nodes, Objects = [original.Objects[0]], Facts = [original.Facts[0]] };
        await using var dataset = await Build(snapshot, io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        bool finished = false; Task<bool>? interactive = null; directory.BeforeNextRead = () => interactive = io.Run(Device, IoPriority.Interactive, _ => finished);
        await using var charges = await lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation); finished = true;
        Assert.NotNull(interactive); Assert.False(await interactive); Assert.Equal((UInt128)80, charges.Totals.Known); Assert.Equal(3000UL, charges.Totals.MatchingPaths);
        var all = await charges.ReadAllChargesAsync(cancellation: Cancellation); var row = Assert.Single(all.Rows); Assert.Equal(1UL, all.Total);
        Assert.Equal(1UL, row.ParentId); Assert.Equal(3000UL, row.ObservedAliases); Assert.True(row.Shared);
    }

    [Fact]
    public async Task ChargePagesBudgetTheReferencedEvidencePayloadEvenWhenReturnedLabelsAreSmall()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var original = AnalysisAccountingTests.Golden();
        var includes = Enumerable.Range(0, 128).Select(n => new string('c', 240) + n.ToString("D3")).ToImmutableArray();
        var snapshot = original with
        {
            Nodes = original.Nodes.Take(1).Concat(Enumerable.Range(0, 16).Select(n => new AnalysisNode((ulong)n + 2, "r", 1, "file-" + n, AnalysisKind.File, "object-" + n))).ToImmutableArray(),
            Objects = Enumerable.Range(0, 16).Select(n => original.Objects[0] with { Id = "object-" + n, VerifiedIdentity = "identity-" + n, LinkCount = AnalysisMeasurement.Known(1) }).ToImmutableArray(),
            Facts = Enumerable.Range(0, 16).Select(n => original.Facts[0] with { Id = "fact-" + n, ObjectId = "object-" + n, Includes = includes }).ToImmutableArray()
        };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var charges = await lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation);
        var ranked = await charges.ReadChargesAsync(1, maximum: 512, cancellation: Cancellation); Assert.Equal(16UL, ranked.Total); Assert.InRange(ranked.Rows.Length, 1, 3); Assert.True(ranked.HasMore);
        ulong count = 0;
        for (ulong offset = 0;;)
        {
            var page = await charges.ReadAllChargesAsync(offset, maximum: 512, cancellation: Cancellation);
            Assert.InRange(page.Rows.Length, 1, 3); count += (ulong)page.Rows.Length;
            if (!page.HasMore) break; offset = page.NextOffset;
        }
        Assert.Equal(16UL, count);
    }

    [Fact]
    public async Task OversizedComponentPayloadRefusesDerivedChargesWithoutLosingOriginalDataset()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        var includes = Enumerable.Range(0, 128).Select(n => new string('c', 240) + n.ToString("D3")).ToImmutableArray();
        snapshot = snapshot with { Facts = Enumerable.Range(0, 128).Select(n => snapshot.Facts[0] with { Id = "fact-" + n, ComponentId = "component-" + n, Includes = includes }).ToImmutableArray() };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        var failure = await Assert.ThrowsAsync<IOException>(() => lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation));
        Assert.Contains("component buffer limit", failure.Message); Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
        Assert.Equal((UInt128)220, (await lease.SummarizeAsync(AnalysisMetric.FileSize, Cancellation)).Totals.Known);
    }

    [Fact]
    public async Task FailedDerivedChargeBuildPreservesOriginalDatasetAndSessionQuota()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); AnalysisScratchArena? filler = null;
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, sessionLimit: 28 * 1024, afterSeal: workspace =>
        {
            filler = workspace.CreateArena(); filler.Append(new byte[checked((int)(28 * 1024 - workspace.ReservedBytes - 48))], Cancellation);
            Assert.Equal(28 * 1024, workspace.ReservedBytes);
        });
        using var reserved = filler!;
        await using var lease = await dataset.AcquireAsync(Cancellation);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, stop.Token));
        var error = await Assert.ThrowsAsync<AnalysisScratchLimitException>(() => lease.BuildObjectChargesAsync(AnalysisMetric.OnDisk, Cancellation));
        Assert.Equal(AnalysisScratchLimit.Budget, error.Limit);
        Assert.Equal((UInt128)80, (await lease.SummarizeAsync(AnalysisMetric.OnDisk, Cancellation)).Totals.Known);
        Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
    }
}
