using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisComparisonTests
{
    private static AnalysisComparisonQueryResult Query(AnalysisSnapshot old, AnalysisSnapshot current, AnalysisComparisonResult result,
        string query, AnalysisComparisonSide side = AnalysisComparisonSide.CurrentOrPrevious)
    {
        var rows = result.Retain();
        try { return AnalysisComparisonQuery.Apply(old, current, result.Metric, rows, query, side: side, cancellation: Cancellation); }
        catch { rows.Dispose(); throw; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueryDeltasRetainTheFullSignedUInt128Range(bool negative)
    {
        var large = Sample(("same", "k", 1, 1, "one")); large = large with { Objects = [large.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }] };
        var zero = Sample(("same", "k", 0, 0, "two")); var old = negative ? large : zero; var current = negative ? zero : large;
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var exact = Query(old, current, result, "delta=" + (negative ? "-" : "+") + UInt128.MaxValue);
        Assert.Single(exact.Matches); Assert.Empty(exact.Unknown);
        using var bounded = Query(old, current, result, negative ? "delta<-1KiB" : "delta>1KiB"); Assert.Single(bounded.Matches);
        using var wrong = Query(old, current, result, negative ? "delta>0" : "delta<0"); Assert.Empty(wrong.Matches);
        Assert.Equal(0, new AnalysisDelta(true, 0).CompareTo(new(false, 0)));
    }

    [Fact]
    public void QueryKeepsAWholeSharedAccountingRowAndEvaluatesEachAliasExpression()
    {
        var s = Sample(("one", "k", 100, 80, "v"), ("two", "k", 100, 80, "v"));
        using var result = AnalysisComparison.Build(s, s, AnalysisMetric.OnDisk, cancellation: Cancellation);
        using var match = Query(s, s, result, "name=two AND allocated=80 AND root=r");
        var row = Assert.Single(match.Matches); Assert.Equal(2, row.Current!.Paths.Length); Assert.Equal((UInt128)80, row.Current.Value.Value);
        using var none = Query(s, s, result, "name=one AND path=two"); Assert.Empty(none.Matches); Assert.Empty(none.Unknown);
        using var ordinal = Query(s, s, result, "name=TWO"); Assert.Single(ordinal.Matches);
    }

    [Fact]
    public void QuerySelectsTheRecordedSideAndRetainsRemovedObservations()
    {
        var old = Sample(("before", "k", 10, 8, "v")); var current = Sample(("after", "k", 10, 8, "v"));
        using (var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation))
        {
            using var present = Query(old, current, result, "name=before", AnalysisComparisonSide.Current); Assert.Empty(present.Matches);
            using var previous = Query(old, current, result, "name=before AND change:moved", AnalysisComparisonSide.Previous); Assert.Single(previous.Matches);
            using var either = Query(old, current, result, "name=before", AnalysisComparisonSide.Either); Assert.Single(either.Matches);
        }
        current = Sample();
        using var removed = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var fallback = Query(old, current, removed, "change:removed AND path=before"); Assert.Single(fallback.Matches);
        using var absent = Query(old, current, removed, "name=before", AnalysisComparisonSide.Current); Assert.Empty(absent.Matches); Assert.Single(absent.Unknown);
        using var unmatched = Query(old, current, removed, "name=other", AnalysisComparisonSide.Either); Assert.Empty(unmatched.Matches); Assert.Empty(unmatched.Unknown);
    }

    [Fact]
    public void QueryPreservesUnknownCoverageAndUnrepresentableNames()
    {
        var old = Sample() with { Coverage = NamespaceCoverage.Partial }; var current = Sample(("new", "n", 10, 8, "v"));
        using (var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation))
        {
            using var unknown = Query(old, current, result, "delta>0"); Assert.Empty(unknown.Matches); Assert.Single(unknown.Unknown);
            using var status = Query(old, current, result, "change:unknown"); Assert.Single(status.Matches);
        }
        var s = Sample(("bad�", "k", 10, 8, "v"));
        s = s with { Nodes = s.Nodes.SetItem(1, s.Nodes[1] with { NameEncoding = "unix-bytes", RawName = [(byte)'b', (byte)'a', (byte)'d', 0xff] }) };
        using var raw = AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var text = Query(s, s, raw, "NOT path:bad"); Assert.Empty(text.Matches); Assert.Single(text.Unknown);
        using var numeric = Query(s, s, raw, "size=10"); Assert.Single(numeric.Matches);
    }

    [Fact]
    public void QueryUsesRecordedTimezonesAndRefusesAmbiguousRootLabels()
    {
        var s = Sample(("same", "k", 10, 8, "v")); s = s with { Identity = s.Identity with { TimeZoneId = "unavailable-test-zone" } };
        using var result = AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Throws<FormatException>(() => Query(s, s, result, "modified>=1970-01-01"));
        using var explicitOffset = Query(s, s, result, "modified>=1970-01-01T00:00:00Z"); Assert.Single(explicitOffset.Matches);
        var ambiguous = s with { Roots = s.Roots.Add(s.Roots[0] with { Id = "other" }) };
        Assert.Throws<FormatException>(() => Query(ambiguous, ambiguous, result, "root=fixture"));
    }

    [Fact]
    public void QueryAndReadLeasesKeepAdmissionAfterTheResultOwnerCloses()
    {
        var s = Sample(("same", "k", 10, 8, "v")) with { Taint = AnalysisPrivacy.All };
        var result = AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation);
        var read = result.Retain(); var filtered = Query(s, s, result, "size>0");
        result.Dispose(); Assert.Throws<ObjectDisposedException>(() => result.Rows); Assert.Single(read.Rows); Assert.Single(filtered.Matches);
        Assert.Equal(AnalysisPrivacy.All, filtered.Taint);
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation));
        read.Dispose(); read.Dispose(); Assert.Throws<ObjectDisposedException>(() => read.Rows);
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation));
        filtered.Dispose(); filtered.Dispose(); Assert.Throws<ObjectDisposedException>(() => filtered.Matches);
        using var next = AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation); Assert.Single(next.Rows);
    }

    [Fact]
    public void FailedCanceledAndOversizedQueriesLeaveTheirCallerOwnedLeaseUsable()
    {
        var s = Sample(("same", "k", 10, 8, "v")); using var result = AnalysisComparison.Build(s, s, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var rows = result.Retain();
        Assert.Throws<FormatException>(() => AnalysisComparisonQuery.Apply(s, s, result.Metric, rows, "delta>", cancellation: Cancellation));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AnalysisComparisonQuery.Apply(s, s, result.Metric, rows, "", cancellation: cancel.Token));
        var oversized = s with { Nodes = Enumerable.Repeat(s.Nodes[0], 20000).ToImmutableArray() };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparisonQuery.Apply(oversized, s, result.Metric, rows, "", cancellation: Cancellation));
        Assert.Single(rows.Rows);
        using var success = Query(s, s, result, "size>0"); Assert.Single(success.Matches);
    }
}
