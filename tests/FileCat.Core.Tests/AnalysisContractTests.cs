using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisContractTests
{
    [Fact]
    public void DisplayPreservesUnpairedCodeUnitsAndMakesTrailingAndEscapedNamesDistinct()
    {
        Assert.Equal("a\\uD800b\\uDC00", AnalysisNames.Display("a\ud800b\udc00"));
        Assert.Equal("name\\u002E\\u0020", AnalysisNames.Display("name. ", escapeTrailing: true));
        Assert.Equal("A normal sentence.", AnalysisNames.Display("A normal sentence."));
        Assert.Equal("safe\\u202Ename\\u000A", AnalysisNames.Display("safe\u202Ename\n"));
        Assert.Equal("café 😀", AnalysisNames.Display("café 😀"));
        var literal = new AnalysisNode(1, "r", 0, "\\u202E", AnalysisKind.File);
        var bidi = literal with { Name = "\u202E" };
        Assert.NotEqual(AnalysisNames.DisplayName(literal), AnalysisNames.DisplayName(bidi));
        var raw = literal with { NameEncoding = "unix-bytes", Name = "\ufffdx", RawName = [0xff, (byte)'x'] };
        Assert.Equal("[unix bytes] FF78", AnalysisNames.DisplayName(raw));
        Assert.Equal("Raw name (unix-bytes): FF78", AnalysisNames.NameEvidence(raw));
        Assert.Null(AnalysisNames.NameEvidence(literal));
        Assert.Equal("C:\\parent\\u002E\\u0020\\child", AnalysisNames.DisplayPath("C:\\parent. \\child", AnalysisNameRules.Windows));
        Assert.Equal("/parent\\u002E\\u0020/child", AnalysisNames.DisplayPath("/parent. /child", AnalysisNameRules.Unix));
    }
    [Fact]
    public void SourceComponentClassificationDoesNotInterpretTheCurrentHostsPathSeparators()
    {
        Assert.Equal("cs\\name", AnalysisNames.Extension("source.cs\\name"));
        Assert.Equal("other", AnalysisClassification.Family("source.cs\\name"));
        Assert.Equal("code", AnalysisClassification.Family("source\\name.cs"));
    }
    [Fact]
    public void AllAxesAndCoveredBindingsAreValidatedIndependently()
    {
        var s = AnalysisAccountingTests.Golden(); var token = TestContext.Current.CancellationToken;
        AnalysisValidation.Validate(s, token);
        foreach (var invalid in new[]
        {
            s with { Phase = (AnalysisPhase)99 }, s with { Coverage = (NamespaceCoverage)99 }, s with { Freshness = (AnalysisFreshness)99 },
            s with { Origin = (AnalysisOrigin)99 }, s with { Persistence = (AnalysisPersistence)99 }, s with { Taint = (AnalysisTaint)128 },
            s with { Roots = [s.Roots[0] with { Disposition = AnalysisRootDisposition.Covered, CoveredByRootId = "r", CoveredRelativePath = "" }] },
            s with { Nodes = s.Nodes.Add(s.Nodes[5] with { Id = 7 }) },
            s with { Objects = [s.Objects[0] with { ObservedAliases = [4, 6] }, s.Objects[1]] },
            s with { Policy = s.Policy with { AllocationVersion = 2 } },
            s with { Roots = [s.Roots[0] with { Scheme = "sftp", CanonicalLocator = "sftp://user:password@example.test/" }] }
        }) Assert.Throws<InvalidDataException>(() => AnalysisValidation.Validate(invalid, token));
    }

    [Fact]
    public void TaggedNamesPreserveRawUnixBytesAndUtf16CodeUnits()
    {
        byte[] raw = [0xff, (byte)'x'];
        var node = new AnalysisNode(1, "r", 0, AnalysisRawNames.Decode("unix-bytes", raw), AnalysisKind.Directory) { NameEncoding = "unix-bytes", RawName = raw.ToImmutableArray() };
        Assert.False(AnalysisRawNames.HasLosslessText(node));
        Assert.Throws<InvalidDataException>(() => AnalysisRawNames.Decode("utf8", raw));
        const string unpaired = "a\ud800b";
        Assert.Equal(unpaired, AnalysisRawNames.Decode("utf16le", AnalysisRawNames.Encode("utf16le", unpaired)));
        Assert.Throws<InvalidDataException>(() => AnalysisRawNames.Decode("utf16le", [1]));
        var s = AnalysisAccountingTests.Golden();
        Assert.Throws<InvalidDataException>(() => AnalysisValidation.Validate(s with { Nodes = s.Nodes.SetItem(5, s.Nodes[5] with { RawName = [(byte)'Q'] }) }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PrivacyJoinsCannotLosePrivilegedOrImportedClassification()
    {
        var all = AnalysisPrivacy.Join(AnalysisTaint.PrivilegedDerived, AnalysisPrivacy.Imported(AnalysisTaint.OrdinarySensitive));
        Assert.Equal(AnalysisPrivacy.All, all);
        var s = AnalysisAccountingTests.Golden() with { Taint = all };
        var projection = AnalysisAggregate.Project(s, AnalysisMetric.FileSize, (n, _) => n.Name == "x.bin", TestContext.Current.CancellationToken);
        Assert.Equal(all, projection.Taint);
        Assert.Throws<InvalidDataException>(() => AnalysisPrivacy.Imported((AnalysisTaint)128));
    }

    [Fact]
    public void IssueCountersContinueAfterDetailedSamplesReachTheirCap()
    {
        var s = AnalysisAccountingTests.Golden(); using var store = new AnalysisStore();
        for (int i = 0; i < AnalysisLimits.Issues + 7; i++) store.Issue("fixture-failed");
        var snapshot = store.Snapshot(s.Identity, s.Roots, s.Policy, AnalysisPhase.Finished, NamespaceCoverage.Partial);
        Assert.Equal(AnalysisLimits.Issues, snapshot.Issues.Length);
        Assert.Equal((ulong)AnalysisLimits.Issues + 7, AnalysisCoverage.Create(snapshot).FailedIssues);
        for (int i = 0; i < 5; i++) store.Issue("component-not-requested", state: MeasurementState.Excluded);
        for (int i = 0; i < 3; i++) store.Issue("source-mount-boundary", state: MeasurementState.Excluded);
        for (int i = 0; i < 4; i++) store.Issue("navigation-link-not-followed", state: MeasurementState.Excluded);
        for (int i = 0; i < 2; i++) store.Issue("root-overlap-unverified");
        snapshot = store.Snapshot(s.Identity, s.Roots, s.Policy, AnalysisPhase.Finished, NamespaceCoverage.Partial);
        var exact = AnalysisCoverage.Create(snapshot); AnalysisValidation.Validate(snapshot, TestContext.Current.CancellationToken);
        Assert.Equal(9ul, exact.Boundaries); Assert.Equal(12ul, exact.ExcludedIssues);
        Assert.Equal(3ul, exact.BoundaryTotals[AnalysisBoundaryKind.Mount]);
        Assert.Equal(4ul, exact.BoundaryTotals[AnalysisBoundaryKind.NavigationLink]);
        Assert.Equal(2ul, exact.BoundaryTotals[AnalysisBoundaryKind.UnverifiedRootOverlap]);
        Assert.Throws<InvalidDataException>(() => AnalysisValidation.Validate(snapshot with { BoundaryTotals = snapshot.BoundaryTotals.SetItem(AnalysisBoundaryKind.Mount, (ulong)AnalysisLimits.Issues + 100) }, TestContext.Current.CancellationToken));
        var coverage = AnalysisCoverage.Create(s);
        Assert.Equal(3ul, coverage.IdentityVerifiedPaths);
        Assert.Throws<InvalidDataException>(() => AnalysisValidation.Validate(s with { CoverageDetails = coverage with { IdentityVerifiedPaths = 99 } }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CoveredRootQueriesUseTheRecordedSubtreeAndNeverAnotherHardLinkAlias()
    {
        var s = AnalysisAccountingTests.Golden();
        s = s with
        {
            Roots = [s.Roots[0], s.Roots[0] with { Id = "child", Kind = AnalysisKind.File, Disposition = AnalysisRootDisposition.Covered,
                CoveredByRootId = "r", CoveredRelativePath = "A/x.bin", ObservedObjectIdentity = "device:X", Label = "Selected alias" }]
        };
        var scope = new AnalysisRootScopeIndex(s, TestContext.Current.CancellationToken);
        var filter = AnalysisFilter.Parse("root:child", TimeZoneInfo.Utc, roots: s.Roots, rootScopes: scope);
        Assert.Equal(AnalysisTruth.True, filter.Evaluate(new(s.Nodes[3], s.Objects[0], "A/x.bin", AnalysisMeasurement.Known(80))));
        Assert.Equal(AnalysisTruth.False, filter.Evaluate(new(s.Nodes[4], s.Objects[0], "B/y.bin", AnalysisMeasurement.Known(80))));
    }
}
