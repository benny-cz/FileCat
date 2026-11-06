using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize, 100UL)]
    [InlineData(AnalysisMetric.Files, 1UL)]
    [InlineData(AnalysisMetric.UniqueFileSize, 100UL)]
    [InlineData(AnalysisMetric.OnDisk, 80UL)]
    public async Task IndexedNameFilterPreservesOriginalLcaAndDoesNotCountContextAliases(AnalysisMetric metric, ulong known)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var view = await lease.BuildHierarchyAsync(metric, new("name:x.bin"), Cancellation);
        Assert.Equal((UInt128)known, view.Totals.Known); Assert.Equal(1UL, view.Totals.MatchingPaths); Assert.Empty(view.Totals.Missing);
        Assert.NotNull(view.Filter); Assert.Equal(1UL, view.Filter.DefinitePaths); Assert.Equal(0UL, view.Filter.UnknownPaths);
        var matching = await view.ReadMatchingFilesAsync(cancellation: Cancellation); Assert.Equal(4UL, Assert.Single(matching.Rows).Path.Id); Assert.Equal(1UL, matching.Total);
        Assert.True(matching.Rows[0].IsMatch); var alias = await view.ReadAsync(5, Cancellation); Assert.False(alias.IsMatch); Assert.Equal(AnalysisTruth.False, alias.FilterTruth);
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            Assert.True(alias.IsVisible); Assert.True(alias.HasChargesElsewhere); Assert.Equal(0UL, alias.Totals.MatchingPaths);
            var bucket = Assert.Single(await view.ReadBucketsAsync(1, Cancellation)); Assert.Equal((UInt128)known, bucket.Totals.Known); Assert.Equal(2UL, bucket.ObservedAliases);
            Assert.Equal((UInt128)0, (await view.ReadAsync(2, Cancellation)).Totals.Known); Assert.Equal(2UL, (await view.ReadChildrenAsync(1, cancellation: Cancellation)).Total);
        }
        else { Assert.False(alias.IsVisible); Assert.Empty(await view.ReadBucketsAsync(1, Cancellation)); Assert.Equal(1UL, (await view.ReadChildrenAsync(1, cancellation: Cancellation)).Total); }
        Assert.False((await view.ReadAsync(6, Cancellation)).IsVisible);
    }

    [Fact]
    public async Task FilterIncludesOnlyEligibleObjectsFromASharedBucket()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Nodes = s.Nodes.Add(new(7, "r", 2, "other-a", AnalysisKind.File, "W")).Add(new(8, "r", 3, "other-b", AnalysisKind.File, "W")),
            Objects = s.Objects.Add(new("W", "device:W", AnalysisMeasurement.Known(50), AnalysisMeasurement.Known(2))),
            Facts = s.Facts.Add(new("w", "W", null, "primary", "fixture-v1", AnalysisMeasurement.Known(40), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var view = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:x.bin"), Cancellation);
        var bucket = Assert.Single(await view.ReadBucketsAsync(1, Cancellation)); Assert.Equal((UInt128)80, bucket.Totals.Known); Assert.Equal(1UL, bucket.Objects); Assert.Equal(2UL, bucket.ObservedAliases);
        Assert.False((await view.ReadAsync(7, Cancellation)).IsVisible); Assert.False((await view.ReadAsync(8, Cancellation)).IsVisible);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize, 200UL, 20UL)]
    [InlineData(AnalysisMetric.Files, 2UL, 1UL)]
    [InlineData(AnalysisMetric.UniqueFileSize, 100UL, 20UL)]
    [InlineData(AnalysisMetric.OnDisk, 80UL, 0UL)]
    public async Task UnknownFilterCohortIsSeparateFromDefiniteMatches(AnalysisMetric metric, ulong definite, ulong unknown)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisAccountingTests.Golden(), io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using (var view = await lease.BuildHierarchyAsync(metric, new("allocated > 0"), Cancellation))
        {
            Assert.Equal((UInt128)definite, view.Totals.Known); Assert.Equal(2UL, view.Totals.MatchingPaths); Assert.Equal(2UL, view.Filter!.DefinitePaths); Assert.Equal(1UL, view.Filter.UnknownPaths);
            Assert.Equal(new ulong[] { 4, 5 }, (await view.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows.Select(r => r.Path.Id));
        }
        await using var uncertain = await lease.BuildHierarchyAsync(metric, new("allocated > 0", UnknownOnly: true), Cancellation);
        Assert.Equal((UInt128)unknown, uncertain.Totals.Known); Assert.Equal(1UL, uncertain.Totals.MatchingPaths); Assert.Equal(1UL, uncertain.Filter!.UnknownPaths);
        var row = Assert.Single((await uncertain.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows); Assert.Equal(6UL, row.Path.Id); Assert.Equal(AnalysisTruth.Unknown, row.FilterTruth);
        Assert.Empty(await uncertain.ReadBucketsAsync(1, Cancellation)); if (metric == AnalysisMetric.OnDisk) Assert.Equal(1UL, uncertain.Totals.Missing[MeasurementState.Unsupported]);
    }

    [Fact]
    public async Task NamespaceScopeRetainsDirectorySelfWhileFilePredicatesExcludeIt()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Facts = s.Facts.Add(new("directory", null, 2, "self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using (var scoped = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("", RootId: "r"), Cancellation))
        { Assert.Equal((UInt128)32, scoped.Totals.IdentityUnverified); Assert.Single(await scoped.ReadBucketsAsync(2, Cancellation)); Assert.Equal(3UL, scoped.Totals.MatchingPaths); }
        await using var files = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:x.bin"), Cancellation);
        Assert.Equal((UInt128)0, files.Totals.IdentityUnverified); Assert.Empty(await files.ReadBucketsAsync(2, Cancellation));
    }

    [Fact]
    public async Task CoveredFileRootMatchesOnlyItsRecordedAliasAndKeepsSharedChargeAboveIt()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Roots = s.Roots.Add(s.Roots[0] with { Id = "child", Kind = AnalysisKind.File, Disposition = AnalysisRootDisposition.Covered,
            CoveredByRootId = "r", CoveredRelativePath = "A/x.bin", ObservedObjectIdentity = "device:X", Label = "Selected alias" }) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var view = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("root:child"), Cancellation);
        Assert.Equal(1UL, view.Totals.MatchingPaths); Assert.Equal((UInt128)80, view.Totals.Known);
        Assert.Equal(4UL, Assert.Single((await view.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows).Path.Id);
        Assert.False((await view.ReadAsync(5, Cancellation)).IsMatch); Assert.True((await view.ReadAsync(5, Cancellation)).IsVisible); Assert.Single(await view.ReadBucketsAsync(1, Cancellation));
    }

    [Fact]
    public async Task PathFiltersTreatLossyAncestorsAsUnknownWithoutLosingNameMatches()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); ImmutableArray<byte> raw = [0xff];
        s = s with { Nodes = s.Nodes.SetItem(1, s.Nodes[1] with { Name = AnalysisRawNames.Decode("unix-bytes", raw.AsSpan()), NameEncoding = "unix-bytes", RawName = raw }) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using (var uncertain = await lease.BuildHierarchyAsync(AnalysisMetric.Files, new("path:x.bin", UnknownOnly: true), Cancellation))
        { Assert.Equal(2UL, uncertain.Totals.MatchingPaths); Assert.Equal(new ulong[] { 4, 6 }, (await uncertain.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows.Select(r => r.Path.Id)); }
        await using var name = await lease.BuildHierarchyAsync(AnalysisMetric.Files, new("name:x.bin"), Cancellation); Assert.Equal(1UL, name.Totals.MatchingPaths);
    }

    [Fact]
    public async Task ActiveUnknownStateUsesSharedVersionConflictsWithoutMovingItsCharge()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Nodes = s.Nodes.SetItem(4, s.Nodes[4] with { ObjectId = "X2" }), Objects = s.Objects.SetItem(0, s.Objects[0] with { VersionEvidence = "old" }).Add(s.Objects[0] with { Id = "X2", VersionEvidence = "new" }) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var view = await lease.BuildHierarchyAsync(AnalysisMetric.UniqueFileSize, new("state:unknown"), Cancellation);
        Assert.Equal(2UL, view.Totals.MatchingPaths); Assert.Equal((UInt128)0, view.Totals.Known); Assert.Equal(1UL, view.Totals.Missing[MeasurementState.Changed]);
        Assert.Equal(MeasurementState.Changed, Assert.Single(await view.ReadBucketsAsync(1, Cancellation)).Totals.Missing.Keys.Single());
    }

    [Fact]
    public async Task AllocationFilterDeduplicatesCoherentComponentObservations()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); s = s with { Facts = s.Facts.Add(s.Facts[0] with { Id = "repeat" }) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var view = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("allocated=80"), Cancellation);
        Assert.Equal(2UL, view.Totals.MatchingPaths); Assert.Equal((UInt128)80, view.Totals.Known); Assert.Equal(1UL, view.Filter!.UnknownPaths); Assert.Equal(1UL, Assert.Single(await view.ReadBucketsAsync(1, Cancellation)).Charges);
    }

    [Fact]
    public async Task CoveredDirectoryScopeIncludesOnlyItsNamespaceAndIndependentSelfAllocation()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with
        {
            Roots = s.Roots.Add(s.Roots[0] with { Id = "child", Disposition = AnalysisRootDisposition.Covered, CoveredByRootId = "r", CoveredRelativePath = "A", ObservedObjectIdentity = "dir:A" }),
            Nodes = s.Nodes.SetItem(1, s.Nodes[1] with { ObservedIdentity = "dir:A" }),
            Facts = s.Facts.Add(new("self-a", null, 2, "self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, []))
                .Add(new("self-b", null, 3, "self", "fixture-v1", AnalysisMeasurement.Known(64), "fixture", DateTimeOffset.UnixEpoch, []))
        };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var view = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("", RootId: "child"), Cancellation);
        Assert.Equal(2UL, view.Totals.MatchingPaths); Assert.Equal((UInt128)112, view.Totals.Known); Assert.Equal((UInt128)0, view.Totals.IdentityUnverified);
        Assert.Equal(new ulong[] { 4, 6 }, (await view.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows.Select(r => r.Path.Id));
        Assert.Single(await view.ReadBucketsAsync(2, Cancellation)); Assert.Empty(await view.ReadBucketsAsync(3, Cancellation)); Assert.False((await view.ReadAsync(5, Cancellation)).IsMatch);
    }

    [Fact]
    public async Task FilterUsesCapturedDatesAndCaseAndKeepsMetadataFlagsThreeValued()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Identity = s.Identity with { TimeZoneId = "UTC" }, Objects = [s.Objects[0] with { ModifiedUtc = DateTimeOffset.UnixEpoch }, s.Objects[1]],
            Nodes = s.Nodes.SetItem(3, s.Nodes[3] with { Sparse = true }).SetItem(4, s.Nodes[4] with { Sparse = false }) };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using (var date = await lease.BuildHierarchyAsync(AnalysisMetric.Files, new("modified-before:1970-01-02"), Cancellation)) { Assert.Equal(2UL, date.Totals.MatchingPaths); Assert.Equal(1UL, date.Filter!.UnknownPaths); }
        await using (var sensitive = await lease.BuildHierarchyAsync(AnalysisMetric.Files, new("name:X.BIN", CaseSensitive: true), Cancellation)) Assert.Equal(0UL, sensitive.Totals.MatchingPaths);
        await using (var insensitive = await lease.BuildHierarchyAsync(AnalysisMetric.Files, new("name:X.BIN"), Cancellation)) Assert.Equal(1UL, insensitive.Totals.MatchingPaths);
        await using var sparseUnknown = await lease.BuildHierarchyAsync(AnalysisMetric.Files, new("sparse:true", UnknownOnly: true), Cancellation);
        Assert.Equal(6UL, Assert.Single((await sparseUnknown.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows).Path.Id);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    public async Task HiddenHugeValuesDoNotOverflowTheSelectedFilterResult(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        s = s with { Objects = [s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }, s.Objects[1]],
            Facts = [s.Facts[0] with { Measurement = AnalysisMeasurement.Known(UInt128.MaxValue) }, s.Facts[1]] };
        await using var dataset = await Build(s, io); await using var lease = await dataset.AcquireAsync(Cancellation); await using var view = await lease.BuildHierarchyAsync(metric, new("name:z.bin"), Cancellation);
        Assert.Equal(1UL, view.Totals.MatchingPaths); Assert.Equal((UInt128)(metric == AnalysisMetric.OnDisk ? 0 : 20), view.Totals.Known); Assert.Empty(await view.ReadBucketsAsync(1, Cancellation));
    }

    [Fact]
    public async Task FilteredHierarchyAndExactMembersOutliveOwnersAndRejectedQueriesLeaveSourceReadable()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var directory = new MemoryDirectory(); await using var dataset = await Build(AnalysisAccountingTests.Golden(), io, directory); await using var lease = await dataset.AcquireAsync(Cancellation);
        await Assert.ThrowsAsync<FormatException>(() => lease.BuildHierarchyAsync(AnalysisMetric.Files, new("size:nope"), Cancellation));
        await using var view = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, new("name:x.bin"), Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        Assert.Equal(4UL, Assert.Single((await view.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows).Path.Id); Assert.Single(await view.ReadBucketsAsync(1, Cancellation));
        await view.DisposeAsync(); Assert.True(directory.Closed);
    }
}
