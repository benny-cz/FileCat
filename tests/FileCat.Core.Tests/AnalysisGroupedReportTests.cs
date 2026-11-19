using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using Microsoft.VisualBasic.FileIO;

namespace FileCat.Core.Tests;

public sealed class AnalysisGroupedReportTests
{
    [Fact]
    public async Task OtherGroupOverviewCountsDistinctReferencePathsWhoseEntryGroupWasOmitted()
    {
        var snapshot = AnalysisAccountingTests.Golden(); snapshot = snapshot with
        { Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { Name = "y.jpg" }), Objects = snapshot.Objects.SetItem(1, snapshot.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(1000) }) };
        var token = TestContext.Current.CancellationToken; using var io = new DeviceIoScheduler();
        AnalysisSnapshotLease Retain() => new(snapshot, () => { }, Retain); using var owner = Retain();
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: token), AnalysisGroupingDimension.Extension, false, token);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, io, AnalysisReportFormat.Csv, AnalysisReportScope.FilteredView,
            AnalysisTree.OtherKey(AnalysisTree.RootKey, 1), cancellation: token);
        Assert.Equal((UInt128)100, report.Header.Totals.Known); Assert.Equal((UInt128)200, report.Header.Logical.KnownBytes);
        Assert.Equal(2UL, report.Header.Logical.Files); Assert.Equal(2UL, report.Header.Totals.MatchingPaths);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task MemoryReportCapturesGroupReferenceAndOtherAccountingWithoutReprojectingMembership(int variant)
    {
        var snapshot = AnalysisAccountingTests.Golden(); snapshot = snapshot with { Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { Name = "y.jpg" }) };
        var token = TestContext.Current.CancellationToken; using var io = new DeviceIoScheduler();
        AnalysisSnapshotLease Retain() => new(snapshot, () => { }, Retain); using var owner = Retain();
        var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: token);
        var tree = variant == 6 ? AnalysisTree.Build(snapshot, projection, false, token) : AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.Extension, false, token);
        var entries = variant == 6 ? null : tree.Groups.Values.Single(group => group.Kind == AnalysisGroupKind.Entries && group.Definition.Value.Contains("bin", StringComparison.Ordinal));
        var shared = variant == 6 ? null : tree.Groups.Values.Single(group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
        string parent = variant switch { 1 or 3 => entries!.Definition.Key, 4 or 5 => shared!.Definition.Key, 6 => "n:1", _ => AnalysisTree.RootKey };
        string scope = variant is 2 or 3 or 5 or 6 ? AnalysisTree.OtherKey(parent, 1) : parent;
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, io, AnalysisReportFormat.Csv, AnalysisReportScope.FilteredView, scope, cancellation: token);
        Assert.Equal(tree.ScopeValue(scope), report.Header.Totals.Known);
        Assert.Equal((ulong)tree.ChildrenOf(scope).Count(item => item.NodeId is not null || item.GroupDefinition is not null), report.Header.Rows);
        Assert.Equal(variant != 6, report.Header.IncludesAccountingReferences);
        if (variant is 4 or 5) { Assert.Equal((UInt128)200, report.Header.Logical.KnownBytes); Assert.Equal(2UL, report.Header.Logical.Files); }
        if (variant == 5) Assert.Equal((UInt128)0, report.Header.Totals.Known);
        if (variant == 0) { Assert.Equal((UInt128)220, report.Header.Logical.KnownBytes); Assert.False(report.Header.NamespaceRowsOnly); }
    }

    [Fact]
    public void MissingAllocationIsNotReportedAsMissingNamespaceInMemoryReportRows()
    {
        var snapshot = AnalysisAccountingTests.Golden(); var token = TestContext.Current.CancellationToken;
        var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: token);
        var folders = AnalysisTree.Build(snapshot, projection, false, token, retainReportStatistics: true);
        Assert.True(folders.Items["n:6"].Unknown); Assert.False(folders.Items["n:6"].NamespaceIncomplete);
        var groups = AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.FileType, false, token, retainReportStatistics: true);
        Assert.True(groups.Items["n:6"].Unknown); Assert.False(groups.Items["n:6"].NamespaceIncomplete);
    }

    [Theory]
    [InlineData(AnalysisReportFormat.Csv)] [InlineData(AnalysisReportFormat.Html)] [InlineData(AnalysisReportFormat.Markdown)]
    public async Task GroupReportsLabelReferencesAndKeepDirectoryAllocationSeparateFromLogicalFileStatistics(AnalysisReportFormat format)
    {
        var empty = ImmutableDictionary<MeasurementState, ulong>.Empty;
        var totals = new AnalysisTotals(4, 0, 0, empty, true); var logical = new AnalysisLogicalTotals(0, 0, empty);
        var directory = new AnalysisDatasetGroup(new("directory", "directory", "=DIRECTORY<svg>", "folder"), AnalysisGroupKind.DirectoryAllocation, totals, logical, 1, 1);
        var path = new AnalysisPathMetadata(1, "r", 0, "reference.bin", AnalysisKind.File, "o", null, null, null, MeasurementState.Known, true, "utf8", [], null, null);
        var reference = new AnalysisDatasetGroupRow(new(path, AnalysisMeasurement.Known(5), true, null, 0), totals with { Known = 0, MatchingPaths = 1 }, true);
        var header = new AnalysisReportHeader(AnalysisMetric.OnDisk, AnalysisReportScope.FilteredView, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            NamespaceCoverage.CompleteUnderPolicy, AnalysisFreshness.Observed, AnalysisTaint.OrdinarySensitive, totals, logical, "Accounting groups", 2)
        { ViewKind = AnalysisReportViewKind.GroupOverview, IncludesAccountingReferences = true };
        async IAsyncEnumerable<AnalysisReportRow> Rows() { await Task.CompletedTask; yield return AnalysisReportGroupRows.Group(directory); yield return AnalysisReportGroupRows.Member(reference, null, true); }
        using var output = new MemoryStream(); await AnalysisHumanReport.WriteAsync(output, header, Rows(), format, false, TestContext.Current.CancellationToken);
        string text = Encoding.UTF8.GetString(output.ToArray()); Assert.Contains("Reference path statistics", text); Assert.Contains("not additional allocation", text);
        if (format == AnalysisReportFormat.Csv)
        {
            using var parser = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true }; parser.SetDelimiters(",");
            var columns = parser.ReadFields()!; parser.ReadFields(); var row = parser.ReadFields()!;
            Assert.Equal("4", row[Array.IndexOf(columns, "active_known")]); Assert.Equal("", row[Array.IndexOf(columns, "file_size_known_bytes")]);
            Assert.Equal("Not applicable", row[Array.IndexOf(columns, "file_size_missing")]); Assert.StartsWith("'=DIRECTORY", row[Array.IndexOf(columns, "item")]);
        }
        if (format == AnalysisReportFormat.Html) Assert.DoesNotContain("<svg>", text);
    }

    [Theory]
    [InlineData(AnalysisReportFormat.Markdown)] [InlineData(AnalysisReportFormat.Html)]
    public async Task EntireHumanReportDeclaresActiveMetricDescendingRatherThanInheritedNameOrder(AnalysisReportFormat format)
    {
        var snapshot = AnalysisAccountingTests.Golden(); using var io = new DeviceIoScheduler();
        AnalysisSnapshotLease Retain() => new(snapshot, () => { }, Retain); using var owner = Retain();
        var token = TestContext.Current.CancellationToken; var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: token), false, token);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, io, format, AnalysisReportScope.EntireResult, sort: AnalysisTableSort.Name, descending: false, cancellation: token);
        Assert.Equal(new AnalysisReportOrder(AnalysisTableSort.ActiveMetric, true), report.Header.TableOrder); Assert.True(report.Header.DetailLimited);
    }
}
