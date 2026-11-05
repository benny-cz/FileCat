using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisStoreTests
{
    private static AnalysisEntry Entry(string name, AnalysisKind kind, string identity, ulong length, ulong allocation, string version = "v1") =>
        new([], name, Encoding.UTF8.GetBytes(name).ToImmutableArray(), kind, identity, AnalysisMeasurement.Known(length),
            AnalysisMeasurement.Known(allocation), AnalysisMeasurement.Known(2), DateTimeOffset.UnixEpoch, version, ObservedUtc: DateTimeOffset.UnixEpoch);
    private static AnalysisSnapshot Snapshot(AnalysisStore store, bool detailed) => store.Snapshot(
        new(Guid.NewGuid(), 1, 1, "fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow),
        [new("r", "file", "Root", "/root", "device", IdentityAssurance.Verified, "Root")],
        new() { ComponentPreset = detailed ? "detailed" : "standard" }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);

    [Fact]
    public void InterleavedDirectoryFactsNeverReceiveFileVersionInvalidation()
    {
        using var store = new AnalysisStore();
        store.Append(Entry("Root", AnalysisKind.Directory, "root", 0, 7), 0, "r", "fixture", true);
        store.Append(Entry("A", AnalysisKind.Directory, "A", 0, 3), 1, "r", "fixture", true);
        store.Append(Entry("x", AnalysisKind.File, "X", 100, 80), 2, "r", "fixture", true);
        store.Append(Entry("B", AnalysisKind.Directory, "B", 0, 5), 1, "r", "fixture", true);
        store.Append(Entry("y", AnalysisKind.File, "X", 200, 90, "v2"), 4, "r", "fixture", true);
        var s = Snapshot(store, true); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.All(s.Facts.Where(f => f.DirectoryId.HasValue), f => Assert.True(f.Measurement.IsKnown));
        Assert.Equal(MeasurementState.Changed, Assert.Single(s.Facts, f => f.ObjectId is not null).Measurement.State);
        Assert.Equal(MeasurementState.Changed, Assert.Single(s.Objects).LogicalLength.State);
        var p = AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal((UInt128)15, p.Totals.Known); Assert.Equal(3, p.Charges.Count(c => c.DirectorySelf));
    }

    [Fact]
    public void AllocationOnlyAliasChangePreservesCoherentLogicalLength()
    {
        using var store = new AnalysisStore();
        store.Append(Entry("Root", AnalysisKind.Directory, "root", 0, 7), 0, "r", "fixture", true);
        store.Append(Entry("x", AnalysisKind.File, "X", 100, 80), 1, "r", "fixture", true);
        store.Append(Entry("y", AnalysisKind.File, "X", 100, 81), 1, "r", "fixture", true);
        var s = Snapshot(store, true); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.Equal((UInt128)100, Assert.Single(s.Objects).LogicalLength.Value);
        Assert.Equal(MeasurementState.Changed, Assert.Single(s.Facts, f => f.ObjectId is not null).Measurement.State);
        Assert.Equal((UInt128)200, AnalysisAggregate.Project(s, AnalysisMetric.FileSize, cancellation: TestContext.Current.CancellationToken).Totals.Known);
        Assert.Equal((UInt128)7, AnalysisAggregate.Project(s, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken).Totals.Known);
    }

    [Fact]
    public void StandardDoesNotRequestDirectoryAllocationAndExcludedBoundariesHaveNoSelfFact()
    {
        using var store = new AnalysisStore();
        store.Append(Entry("Root", AnalysisKind.Directory, "root", 0, 7), 0, "r", "fixture");
        store.Append(Entry("x", AnalysisKind.File, "X", 100, 80), 1, "r", "fixture");
        var boundary = AnalysisEntry.Unavailable([], [(byte)'b'], "utf8", AnalysisKind.Directory, MeasurementState.Excluded, "source-mount-boundary");
        store.Append(boundary, 1, "r", "fixture", true);
        var s = Snapshot(store, false); AnalysisValidation.Validate(s, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(s.Facts, f => f.DirectoryId.HasValue);
        Assert.Single(AnalysisComponents.Summarize(s)); Assert.Contains("unqualified", AnalysisComponents.Describe(s));
    }
}
