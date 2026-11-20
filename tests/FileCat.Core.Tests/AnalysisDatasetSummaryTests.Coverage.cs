using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task WholeGraphCoverageCountsAliasesAndFactsWithoutInventingEnumerationStatistics()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); await using var dataset = await Build(AnalysisAccountingTests.Golden(), io);
        var coverage = dataset.Descriptor.CoverageDetails;
        Assert.False(coverage.EnumerationKnown); Assert.Null(coverage.UnscheduledDirectories); Assert.False(coverage.Observations.DenominatorFinal);
        Assert.Equal(3UL, coverage.EligibleDirectories); Assert.Equal(3UL, coverage.Observations.IdentityVerifiedPaths); Assert.Equal(0UL, coverage.Observations.IdentityUnverifiedPaths);
        Assert.Equal(1UL, coverage.Observations.MeasurementStates[MeasurementState.Known]); Assert.Equal(1UL, coverage.Observations.MeasurementStates[MeasurementState.Unsupported]);
    }

    [Fact]
    public async Task ChangedAllocationMarksEveryAliasAndDirectoryScopeEvenWithoutAnIssueSample()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Facts = [snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Changed) }, snapshot.Facts[1],
            new("directory", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Missing(MeasurementState.Changed), "fixture", DateTimeOffset.UnixEpoch, [])] };
        await using var dataset = await Build(snapshot, io); var coverage = dataset.Descriptor.CoverageDetails;
        Assert.Equal(AnalysisFreshness.PossiblyStale, dataset.Descriptor.Freshness); Assert.Equal(new ulong[] { 2, 4, 5 }, coverage.Observations.StaleScopeNodes);
        Assert.Equal(new[] { "r" }, coverage.Observations.StaleRootIds); Assert.True(coverage.StaleScopesComplete); Assert.False(coverage.HasUnlocatedStaleScopes);
        Assert.Equal(2UL, coverage.Observations.MeasurementStates[MeasurementState.Changed]);
    }

    [Theory]
    [InlineData("0-orphan")]
    [InlineData("zz-orphan")]
    public async Task AnUnreferencedChangedObjectIsExplicitlyUnlocatedRatherThanObserved(string id)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = snapshot.Objects.Add(new(id, "orphan-identity", AnalysisMeasurement.Missing(MeasurementState.Changed), AnalysisMeasurement.Known(1))) };
        await using var dataset = await Build(snapshot, io);
        Assert.Equal(AnalysisFreshness.PossiblyStale, dataset.Descriptor.Freshness);
        Assert.True(dataset.Descriptor.CoverageDetails.HasUnlocatedStaleScopes); Assert.False(dataset.Descriptor.CoverageDetails.StaleScopesComplete);
    }

    [Theory]
    [InlineData(4UL, 4UL, 0UL)]
    [InlineData(3UL, 2UL, 1UL)]
    [InlineData(3UL, 2UL, 0UL)]
    [InlineData(2UL, 2UL, 0UL)]
    public async Task ACompleteGraphCannotClaimContradictoryEnumerationCounts(ulong requested, ulong completed, ulong failed)
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        await Assert.ThrowsAsync<InvalidDataException>(() => Build(AnalysisAccountingTests.Golden(), io, enumeration: new(requested, completed, failed)));
    }
}
