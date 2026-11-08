using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisMemoryReportTests
{
    [Theory]
    [InlineData("/", false, "", "/")] [InlineData("/", false, "etc", "/etc")]
    [InlineData("C:\\", true, "", "C:\\")] [InlineData("C:\\", true, "folder", "C:\\folder")]
    [InlineData("/ending\\", false, "", "/ending\\")] [InlineData("/ending\\", false, "file", "/ending\\/file")]
    public void CapturedReportLocationsPreserveNativeRootAndUnixBackslashNames(string locator, bool windows, string child, string expected)
    {
        var root = new RootBinding("r", "file", locator, locator, null, IdentityAssurance.Unknown, "Root")
            { NameRules = windows ? AnalysisNameRules.Windows : AnalysisNameRules.Unix };
        Assert.Equal(expected, AnalysisReportLocations.Append(root, child.Length == 0 ? [] : new[] { child }));
    }
    [Theory]
    [InlineData(AnalysisMetric.FileSize, 0)] [InlineData(AnalysisMetric.UniqueFileSize, 0)] [InlineData(AnalysisMetric.OnDisk, 0)] [InlineData(AnalysisMetric.Files, 0)]
    [InlineData(AnalysisMetric.FileSize, 1)] [InlineData(AnalysisMetric.UniqueFileSize, 1)] [InlineData(AnalysisMetric.OnDisk, 1)] [InlineData(AnalysisMetric.Files, 1)]
    [InlineData(AnalysisMetric.FileSize, 2)] [InlineData(AnalysisMetric.UniqueFileSize, 2)] [InlineData(AnalysisMetric.OnDisk, 2)] [InlineData(AnalysisMetric.Files, 2)]
    public void CanonicalReportFoldKeepsMissingCohortsLogicalAliasesAndUnverifiedUniqueAmounts(AnalysisMetric metric, int scenario)
    {
        var snapshot = AnalysisAccountingTests.Golden(); var token = TestContext.Current.CancellationToken;
        if (scenario == 1) snapshot = snapshot with { Objects = snapshot.Objects.SetItem(1, snapshot.Objects[1] with { VerifiedIdentity = null }) };
        if (scenario == 2) snapshot = snapshot with { Objects = snapshot.Objects.SetItem(0, snapshot.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Failed) }) };
        var projection = AnalysisAggregate.Project(snapshot, metric, cancellation: token);
        if (metric == AnalysisMetric.OnDisk) Assert.Equal("fixture-v1", Assert.Single(projection.MeasurementBases!.Labels));
        var report = AnalysisTree.Build(snapshot, projection, false, token, retainReportStatistics: true);
        var root = report.Items[AnalysisTree.RootKey]; var totals = root.ReportTotals!;
        Assert.Equal(projection.Totals.Known, totals.Known); Assert.Equal(projection.Totals.IdentityUnverified, totals.IdentityUnverified);
        Assert.Equal(projection.Totals.MatchingPaths, totals.MatchingPaths); Assert.Equal(projection.Totals.CompatibleBasis, totals.CompatibleBasis);
        Assert.Equal(projection.Totals.Missing.OrderBy(value => value.Key), totals.Missing.OrderBy(value => value.Key));
        Assert.Equal(3UL, root.ReportLogical!.Files); Assert.Equal(scenario == 2 ? (UInt128)20 : 220, root.ReportLogical.KnownBytes);
        Assert.Equal(scenario == 2 ? 2UL : 0, root.ReportLogical.UnknownFiles);
        if (metric == AnalysisMetric.UniqueFileSize && scenario == 1)
        {
            Assert.Equal((UInt128)100, root.Value); Assert.Equal((UInt128)20, root.IdentityUnverified);
            Assert.Equal((UInt128)0, report.Items["n:6"].Value); Assert.Equal((UInt128)20, report.Items["n:6"].ReportTotals!.IdentityUnverified);
        }
        var ordinary = AnalysisTree.Build(snapshot, projection, false, token);
        Assert.Null(ordinary.Items[AnalysisTree.RootKey].ReportTotals); Assert.Equal(root.Value, ordinary.ScopeValue(AnalysisTree.RootKey));
    }

    [Fact]
    public async Task CapturedMemoryReportRetainsExactOwnerAndRejectsAnotherSnapshotWithReusedIdentity()
    {
        var snapshot = AnalysisAccountingTests.Golden(); int owners = 0;
        AnalysisSnapshotLease Retain() { owners++; return new(snapshot, () => owners--, Retain); }
        using var io = new DeviceIoScheduler(); var owner = Retain();
        var token = TestContext.Current.CancellationToken;
        var other = snapshot with { Nodes = snapshot.Nodes.SetItem(0, snapshot.Nodes[0] with { Name = "other" }) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisCapturedMemoryReport.CaptureAsync(owner,
            AnalysisTree.Build(other, AnalysisAggregate.Project(other, AnalysisMetric.Files, cancellation: token), false, token), io,
            AnalysisReportFormat.Csv, AnalysisReportScope.EntireResult, cancellation: token));
        Assert.Equal(1, owners);
        var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: token), false, token);
        var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, io, AnalysisReportFormat.Csv, AnalysisReportScope.EntireResult, cancellation: token);
        Assert.Equal(2, owners); owner.Dispose(); Assert.Equal(1, owners);
        Assert.Equal((UInt128)220, report.Header.Logical.KnownBytes);
        await report.DisposeAsync(); await report.DisposeAsync(); Assert.Equal(0, owners);
    }

    [Fact]
    public async Task MemoryReportBudgetRefusalKeepsOriginalOwnerAndReleasesFailedCaptureAdmission()
    {
        var snapshot = AnalysisAccountingTests.Golden();
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(snapshot.Nodes[0]);
        for (ulong id = 2; id <= 70000; id++) nodes.Add(new(id, "r", 1, "file", AnalysisKind.File));
        snapshot = snapshot with { Nodes = nodes.ToImmutable(), Objects = [], Facts = [] }; int owners = 0;
        AnalysisSnapshotLease Retain() { owners++; return new(snapshot, () => owners--, Retain); }
        using var owner = Retain(); using var io = new DeviceIoScheduler(); var token = TestContext.Current.CancellationToken;
        var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.Files, cancellation: token), false, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, io,
            AnalysisReportFormat.Csv, AnalysisReportScope.EntireResult, cancellation: token));
        Assert.Equal(1, owners); Assert.Equal(69999UL, tree.Projection.Totals.MatchingPaths);
    }
}
