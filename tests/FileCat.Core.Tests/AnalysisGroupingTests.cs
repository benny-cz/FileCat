using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisGroupingTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Captured = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
    internal static AnalysisSnapshot Fixture()
    {
        var source = AnalysisAccountingTests.Golden();
        return source with
        {
            Identity = source.Identity with { StartedUtc = Captured, ObservedUtc = Captured },
            Roots = source.Roots.Add(source.Roots[0] with { Id = "second", Label = "Root", CanonicalLocator = "/second" }),
            Nodes = source.Nodes.SetItem(2, source.Nodes[2] with { ParentId = 0, RootId = "second" })
                .SetItem(3, source.Nodes[3] with { Name = "x.cs", Times = new(Captured, Captured.AddDays(-3), 1) })
                .SetItem(4, source.Nodes[4] with { Name = "y.jpg", RootId = "second", Times = new(Captured, Captured.AddDays(-45), 1) })
                .SetItem(5, source.Nodes[5] with { Name = "z", Times = new(Captured, null, 1) }),
            Facts = source.Facts.Select(f => f with { ObservedUtc = Captured }).Append(
                new("directory", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Known(32), "fixture", Captured, [])).ToImmutableArray()
        };
    }
    public static TheoryData<AnalysisGroupingDimension, AnalysisMetric> DimensionsAndMetrics
    {
        get
        {
            var cases = new TheoryData<AnalysisGroupingDimension, AnalysisMetric>();
            foreach (var group in Enum.GetValues<AnalysisGroupingDimension>().Where(d => d != AnalysisGroupingDimension.FolderHierarchy))
            foreach (var metric in Enum.GetValues<AnalysisMetric>()) cases.Add(group, metric);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(DimensionsAndMetrics))]
    public void EveryGroupingPreservesKnownUnverifiedMissingAndLogicalPathTotals(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        var source = Fixture(); var projection = AnalysisAggregate.Project(source, metric, cancellation: Cancellation);
        var grouped = AnalysisGrouping.Project(source, projection, dimension, Cancellation);
        Assert.Equal(source.Identity, grouped.Identity); Assert.Equal(AnalysisGroupingRules.RuleVersion, grouped.RuleVersion);
        Assert.Same(projection.Totals, grouped.Totals);
        Assert.Equal((UInt128)220, grouped.Groups.Aggregate((UInt128)0, (sum, group) => checked(sum + group.Logical.KnownBytes)));
        Assert.Equal(3UL, grouped.Groups.Aggregate(0UL, (sum, group) => checked(sum + group.Totals.MatchingPaths)));
        Assert.Equal((UInt128)(metric switch { AnalysisMetric.OnDisk => 112U, AnalysisMetric.UniqueFileSize => 120U, AnalysisMetric.FileSize => 220U, _ => 3U }),
            grouped.Groups.Aggregate((UInt128)0, (sum, group) => checked(sum + group.ChartArea(metric))));
        if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
        {
            var shared = Assert.Single(grouped.Groups, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
            Assert.Equal((UInt128)(metric == AnalysisMetric.OnDisk ? 80U : 100U), shared.ChartArea(metric));
            Assert.Equal(new ulong[] { 4, 5 }, shared.Members); Assert.Equal(0UL, shared.Totals.MatchingPaths); Assert.Equal((UInt128)0, shared.Logical.KnownBytes);
            Assert.Equal(new ulong[] { 4, 5 }, Assert.Single(shared.Charges).Charge.Aliases);
        }
        if (metric == AnalysisMetric.OnDisk)
        {
            var directory = Assert.Single(grouped.Groups, g => g.Kind == AnalysisGroupKind.DirectoryAllocation);
            Assert.Equal((UInt128)32, directory.ChartArea(metric)); Assert.Equal(new ulong[] { 2 }, directory.Members);
            Assert.Equal(0UL, directory.Logical.Files); Assert.Equal("Directory allocation", directory.Definition.Label);
        }
    }
    [Theory]
    [InlineData(AnalysisGroupingDimension.FileType)]
    [InlineData(AnalysisGroupingDimension.Extension)]
    [InlineData(AnalysisGroupingDimension.ModifiedAge)]
    [InlineData(AnalysisGroupingDimension.CapturedLocation)]
    public void AFilterCannotPullACrossGroupObjectIntoTheOnlyVisibleAliasGroup(AnalysisGroupingDimension dimension)
    {
        var source = Fixture(); var projection = AnalysisAggregate.Project(source, AnalysisMetric.UniqueFileSize, (node, _) => node.Id == 4, Cancellation);
        var grouped = AnalysisGrouping.Project(source, projection, dimension, Cancellation);
        Assert.Equal(2, grouped.Groups.Length);
        var entry = Assert.Single(grouped.Groups, g => g.Kind == AnalysisGroupKind.Entries);
        Assert.Equal(1UL, entry.Totals.MatchingPaths); Assert.Equal((UInt128)0, entry.ChartArea(grouped.Metric));
        var shared = Assert.Single(grouped.Groups, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
        Assert.Equal((UInt128)100, shared.ChartArea(grouped.Metric)); Assert.Equal(new ulong[] { 4 }, shared.Members);
        Assert.Equal(new ulong[] { 4, 5 }, Assert.Single(shared.Charges).Charge.Aliases);
        Assert.Equal((UInt128)100, entry.Logical.KnownBytes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameTypeAliasesShareOneGroupChargeAndKeepTheirExactFilteredMembers(bool filtered)
    {
        var source = Fixture(); source = source with { Nodes = source.Nodes.SetItem(4, source.Nodes[4] with { Name = "second.CS" }) };
        var projection = AnalysisAggregate.Project(source, AnalysisMetric.UniqueFileSize, filtered ? (node, _) => node.Id == 4 : null, Cancellation);
        var grouped = AnalysisGrouping.Project(source, projection, AnalysisGroupingDimension.FileType, Cancellation);
        Assert.DoesNotContain(grouped.Groups, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups);
        var code = Assert.Single(grouped.Groups, g => g.Definition.Value == "type:code"); Assert.Equal((UInt128)100, code.Totals.Known);
        Assert.Equal(filtered ? new ulong[] { 4 } : new ulong[] { 4, 5 }, code.Members);
        Assert.True(Assert.Single(code.Charges).Charge.Shared);
    }
    [Fact]
    public void MissingSharedMeasurementsAreCountedOnceAndDirectoryUnknownsRemainSeparate()
    {
        var source = Fixture(); source = source with { Facts = source.Facts.SetItem(0, source.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Failed) })
            .SetItem(2, source.Facts[2] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }) };
        var projection = AnalysisAggregate.Project(source, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.Equal(3, projection.MissingCharges.Length);
        var grouped = AnalysisGrouping.Project(source, projection, AnalysisGroupingDimension.FileType, Cancellation);
        Assert.Equal(1UL, Assert.Single(grouped.Groups, g => g.Kind == AnalysisGroupKind.SharedAcrossGroups).Totals.Missing[MeasurementState.Failed]);
        Assert.Equal(1UL, Assert.Single(grouped.Groups, g => g.Kind == AnalysisGroupKind.DirectoryAllocation).Totals.Missing[MeasurementState.Unsupported]);
        Assert.Equal(2UL, grouped.Totals.Missing[MeasurementState.Unsupported]);
    }
    [Fact]
    public void UnverifiedUniqueEvidenceNeverBecomesChartAreaOrVerifiedSharedIdentity()
    {
        var source = Fixture(); source = source with { Objects = source.Objects.SetItem(0, source.Objects[0] with { VerifiedIdentity = null }) };
        var grouped = AnalysisGrouping.Project(source, AnalysisAggregate.Project(source, AnalysisMetric.UniqueFileSize, cancellation: Cancellation), AnalysisGroupingDimension.FileType, Cancellation);
        Assert.DoesNotContain(grouped.Groups, group => group.Kind == AnalysisGroupKind.SharedAcrossGroups);
        Assert.Equal((UInt128)200, grouped.Groups.Aggregate((UInt128)0, (sum, group) => sum + group.Totals.IdentityUnverified));
        Assert.Equal((UInt128)20, grouped.Groups.Aggregate((UInt128)0, (sum, group) => sum + group.ChartArea(AnalysisMetric.UniqueFileSize)));
    }
    [Fact]
    public void ClassificationMakesRawNamesAbsentExtensionsAndUncertainAgesExplicitWithoutInventingAnObjectTime()
    {
        var source = Fixture(); var file = source.Nodes[5]; var obj = source.Objects[1] with { ModifiedUtc = Captured.AddDays(-2) };
        AnalysisGroupDefinition Classify(AnalysisGroupingDimension dimension, AnalysisNode node) => AnalysisGroupingRules.Classify(dimension, node, obj, source.Identity, source.Roots);
        Assert.Equal("extension:none", Classify(AnalysisGroupingDimension.Extension, file).Value);
        Assert.Equal(Classify(AnalysisGroupingDimension.Extension, file with { Name = "a.CS" }), Classify(AnalysisGroupingDimension.Extension, file with { Name = "b.cs" }));
        Assert.Equal("age:unknown", Classify(AnalysisGroupingDimension.ModifiedAge, file).Value);
        Assert.Equal("age:age-7", Classify(AnalysisGroupingDimension.ModifiedAge, file with { Times = null }).Value);
        Assert.Equal("age:unknown", Classify(AnalysisGroupingDimension.ModifiedAge, file with { Times = new(Captured, Captured.AddDays(-7).AddTicks(-1), 2) }).Value);
        var raw = file with { Name = "\ufffd.cs", NameEncoding = "unix-bytes", RawName = ImmutableArray.Create<byte>(0xff, (byte)'.', (byte)'c', (byte)'s') };
        Assert.Equal("extension:unknown", Classify(AnalysisGroupingDimension.Extension, raw).Value);
        Assert.Equal("type:unknown", Classify(AnalysisGroupingDimension.FileType, raw).Value);
        Assert.NotEqual(Classify(AnalysisGroupingDimension.Extension, file).Key, Classify(AnalysisGroupingDimension.Extension, file with { Name = "none" + new string('x', 200) + ".none" }).Key);
        Assert.InRange(Classify(AnalysisGroupingDimension.Extension, file with { Name = "a." + new string('x', 1000) }).Key.Length, 1, 256);
        Assert.NotEqual(Classify(AnalysisGroupingDimension.Extension, file with { Name = "a." + new string('x', 1000) }).Label,
            Classify(AnalysisGroupingDimension.Extension, file with { Name = "a." + new string('x', 1000) + "y" }).Label);
        Assert.Equal(".cs\\u0020", Classify(AnalysisGroupingDimension.Extension, file with { Name = "a.cs " }).Label);
        Assert.NotEqual(Classify(AnalysisGroupingDimension.Extension, file with { Name = "a.cs " }).Label,
            Classify(AnalysisGroupingDimension.Extension, file with { Name = "a.cs\\u0020" }).Label);
    }
    [Fact]
    public void ProjectionRejectsAStaleRevisionAndHonorsCancellation()
    {
        var source = Fixture(); var projection = AnalysisAggregate.Project(source, AnalysisMetric.Files, cancellation: Cancellation);
        Assert.Throws<InvalidOperationException>(() => AnalysisGrouping.Project(source with { Identity = source.Identity with { Revision = source.Identity.Revision + 1 } }, projection, AnalysisGroupingDimension.FileType, Cancellation));
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        Assert.Throws<OperationCanceledException>(() => AnalysisGrouping.Project(source, projection, AnalysisGroupingDimension.FileType, stopped.Token));
    }
    [Fact]
    public void IncompatibleCapturedBasesCannotBecomeACombinedGroupedAmount()
    {
        var source = Fixture(); source = source with { Facts = source.Facts.SetItem(1, source.Facts[1] with { BasisId = "another-basis" }) };
        var projection = AnalysisAggregate.Project(source, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.False(projection.Totals.CompatibleBasis);
        var grouped = AnalysisGrouping.Project(source, projection, AnalysisGroupingDimension.CapturedLocation, Cancellation);
        Assert.False(grouped.Totals.CompatibleBasis); Assert.All(grouped.Groups, g => Assert.Equal((UInt128)0, g.ChartArea(AnalysisMetric.OnDisk)));
        Assert.Equal((UInt128)220, grouped.Groups.Aggregate((UInt128)0, (sum, group) => sum + group.Logical.KnownBytes));
    }
    [Fact]
    public void ConflictingTotalsAndAnInvalidAliasAfterACrossGroupPairAreRejected()
    {
        var source = Fixture(); var projection = AnalysisAggregate.Project(source, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        Assert.Throws<InvalidDataException>(() => AnalysisGrouping.Project(source, projection with { Charges = projection.Charges.SetItem(0, projection.Charges[0] with { Value = projection.Charges[0].Value + 1 }) }, AnalysisGroupingDimension.FileType, Cancellation));
        Assert.Throws<InvalidDataException>(() => AnalysisGrouping.Project(source, projection with { Charges = projection.Charges.SetItem(0, projection.Charges[0] with { Aliases = [4, 5, 999] }) }, AnalysisGroupingDimension.FileType, Cancellation));
    }
    [Fact]
    public void GroupingCannotStripSensitiveImportedOrPrivilegedProvenance()
    {
        var source = Fixture() with { Taint = AnalysisTaint.ImportedUnverified };
        var projection = AnalysisAggregate.Project(source, AnalysisMetric.Files, cancellation: Cancellation) with { Taint = AnalysisTaint.PrivilegedDerived };
        var grouped = AnalysisGrouping.Project(source, projection, AnalysisGroupingDimension.FileType, Cancellation);
        Assert.Equal(AnalysisPrivacy.All, grouped.Taint);
        Assert.Throws<InvalidDataException>(() => AnalysisGrouping.Project(source, projection with { Taint = (AnalysisTaint)128 }, AnalysisGroupingDimension.FileType, Cancellation));
    }
}
