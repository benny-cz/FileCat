using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisAccountingTests
{
    private static AnalysisProjection Project(AnalysisSnapshot snapshot, AnalysisMetric metric,
        Func<AnalysisNode, AnalysisObject?, bool>? filter = null) =>
        AnalysisAggregate.Project(snapshot, metric, filter, TestContext.Current.CancellationToken);
    internal static AnalysisSnapshot Golden() => new(
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, 1, "test", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        [new("r", "file", "Root", "Root", "device", IdentityAssurance.Verified, "Root")],
        [new(1, "r", 0, "Root", AnalysisKind.Directory), new(2, "r", 1, "A", AnalysisKind.Directory),
         new(3, "r", 1, "B", AnalysisKind.Directory), new(4, "r", 2, "x.bin", AnalysisKind.File, "X"),
         new(5, "r", 3, "y.bin", AnalysisKind.File, "X"), new(6, "r", 2, "z.bin", AnalysisKind.File, "Z")],
        [new("X", "device:X", AnalysisMeasurement.Known(100), AnalysisMeasurement.Known(2)),
         new("Z", "device:Z", AnalysisMeasurement.Known(20), AnalysisMeasurement.Known(1))],
        [new("f1", "X", null, "primary", "fixture-v1", AnalysisMeasurement.Known(80), "fixture", DateTimeOffset.UnixEpoch, []),
         new("f2", "Z", null, "primary", "fixture-v1", AnalysisMeasurement.Missing(MeasurementState.Unsupported), "fixture", DateTimeOffset.UnixEpoch, [])],
        [], new(), AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);

    [Fact]
    public void GoldenAccountingAndFilteredAttribution()
    {
        var s = Golden();
        Assert.Equal((UInt128)220, Project(s, AnalysisMetric.FileSize).Totals.Known);
        Assert.Equal((UInt128)3, Project(s, AnalysisMetric.Files).Totals.Known);
        Assert.Equal((UInt128)120, Project(s, AnalysisMetric.UniqueFileSize).Totals.Known);
        var all = Project(s, AnalysisMetric.OnDisk);
        Assert.Equal((UInt128)80, all.Totals.Known);
        Assert.Equal(1ul, all.Totals.Missing[MeasurementState.Unsupported]);
        var filtered = Project(s, AnalysisMetric.OnDisk, (n, _) => n.Name == "x.bin");
        var charge = Assert.Single(filtered.Charges);
        Assert.Equal(1ul, charge.ParentId);
        Assert.True(charge.Shared);
        Assert.Equal(new ulong[] { 4, 5 }, charge.Aliases);
        Assert.Equal(new ulong[] { 4 }, filtered.MatchingNodes);
        Assert.Equal((UInt128)80, filtered.Totals.Known);
        Assert.Equal((UInt128)100, Project(s, AnalysisMetric.FileSize, (n, _) => n.Name == "x.bin").Totals.Known);
    }

    [Fact]
    public void IdentityUnknownIsNotAssertedUniqueAndMatchingHashesAreNotIdentity()
    {
        var s = Golden();
        s = s with { Objects = [s.Objects[0] with { VerifiedIdentity = null }, s.Objects[1]] };
        var p = Project(s, AnalysisMetric.UniqueFileSize);
        Assert.Equal((UInt128)20, p.Totals.Known);
        Assert.Equal((UInt128)200, p.Totals.IdentityUnverified);
        Assert.Single(p.Charges);
    }

    [Fact]
    public void MissingDoesNotBecomeZeroOrAFileCount()
    {
        var s = Golden();
        foreach (var state in Enum.GetValues<MeasurementState>().Where(v => v != MeasurementState.Known))
        {
            var revised = s with { Objects = [s.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(state) }, s.Objects[1]] };
            var p = Project(revised, AnalysisMetric.FileSize);
            Assert.Equal((UInt128)20, p.Totals.Known);
            Assert.Equal(2ul, p.Totals.Missing[state]);
            Assert.Equal(3ul, p.Totals.MatchingPaths);
        }
        Assert.Throws<ArgumentException>(() => AnalysisMeasurement.Missing(MeasurementState.Known));
    }

    [Fact]
    public void CompoundAllocationDoesNotDoubleCountAndCyclesAreRejected()
    {
        var s = Golden();
        var primary = s.Facts[0];
        var total = primary with { Id = "total", ComponentId = "total", Measurement = AnalysisMeasurement.Known(90), Includes = ["primary"] };
        Assert.Equal((UInt128)90, Project(s with { Facts = [primary, total] }, AnalysisMetric.OnDisk).Totals.Known);
        primary = primary with { Includes = ["total"] };
        Assert.Throws<InvalidDataException>(() => Project(s with { Facts = [primary, total] }, AnalysisMetric.OnDisk));
    }

    [Fact]
    public void OverflowNeverWrapsIntoPlausibleTotal()
    {
        var s = Golden();
        s = s with { Objects = [s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }, s.Objects[1]] };
        Assert.Throws<OverflowException>(() => Project(s, AnalysisMetric.FileSize));
    }

    [Fact]
    public void VerifiedAndUnverifiedAllocationShareTheSameCheckedByteCeiling()
    {
        var s = Golden();
        s = s with
        {
            Objects = [s.Objects[0], s.Objects[1] with { VerifiedIdentity = null }],
            Facts = [s.Facts[0] with { Measurement = AnalysisMeasurement.Known(UInt128.MaxValue) }, s.Facts[1] with { Measurement = AnalysisMeasurement.Known(1) }]
        };
        Assert.Throws<OverflowException>(() => Project(s, AnalysisMetric.OnDisk));
    }

    [Fact]
    public void ConflictingAliasVersionsDoNotBecomeAnAssertedUniqueObjectCharge()
    {
        var s = Golden();
        s = s with
        {
            Nodes = [..s.Nodes.Select(n => n.Id == 5 ? n with { ObjectId = "X2" } : n)],
            Objects = [s.Objects[0] with { VersionEvidence = "a" }, s.Objects[1], s.Objects[0] with { Id = "X2", VersionEvidence = "b" }],
            Facts = [..s.Facts, s.Facts[0] with { Id = "x2-fact", ObjectId = "X2" }]
        };
        var unique = Project(s, AnalysisMetric.UniqueFileSize);
        Assert.Equal((UInt128)20, unique.Totals.Known);
        Assert.Equal(1ul, unique.Totals.Missing[MeasurementState.Changed]);
        Assert.Equal((UInt128)0, Project(s, AnalysisMetric.OnDisk).Totals.Known);
    }

    [Fact]
    public void DirectoryComponentsAreAdditiveOnceAndConflictingInclusionCannotHideACycle()
    {
        var s = Golden();
        var primary = new StorageFact("d1", null, 1, "directory-primary", "fixture-v1", AnalysisMeasurement.Known(1), "fixture", DateTimeOffset.UnixEpoch, []);
        var total = primary with { Id = "d2", ComponentId = "directory-total", Measurement = AnalysisMeasurement.Known(9), Includes = ["directory-primary"] };
        var p = Project(s with { Facts = [..s.Facts, primary, total] }, AnalysisMetric.OnDisk);
        Assert.Equal((UInt128)89, p.Totals.Known);
        Assert.Single(p.Charges.Where(c => c.DirectorySelf));
        Assert.Throws<InvalidDataException>(() => Project(s with { Facts = [..s.Facts, primary, primary with { Id = "conflict", Includes = ["directory-primary"] }] }, AnalysisMetric.OnDisk));
    }

    [Fact]
    public void DifferentAllocationBasesRefuseCombinedChart()
    {
        var s = Golden();
        s = s with
        {
            Roots = [..s.Roots, new("r2", "file", "Other", "Other", "other", IdentityAssurance.Verified, "Other")],
            Nodes = [..s.Nodes, new(7, "r2", 0, "Other", AnalysisKind.Directory), new(8, "r2", 7, "other", AnalysisKind.File, "Q")],
            Objects = [..s.Objects, new("Q", "other:Q", AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1))],
            Facts = [..s.Facts, new("q", "Q", null, "total", "other-basis", AnalysisMeasurement.Known(1), "fixture", DateTimeOffset.UnixEpoch, [])]
        };
        var p = Project(s, AnalysisMetric.OnDisk);
        Assert.False(p.Totals.CompatibleBasis);
        Assert.Empty(p.Charges);
        Assert.Equal((UInt128)0, p.Totals.Known);
    }

    [Fact]
    public void CanonicalPolicyGoldenAndOrdinalSorting()
    {
        const string expected = """{"schemaVersion":"1","analysisMode":"disk","componentPreset":"standard","basisIds":[],"exclusions":[],"mountPolicy":"stay-on-source","packagePolicy":"opaque","cloudPolicy":"metadata-only","limits":{},"classificationVersion":"1","allocationVersion":"1","attributionVersion":"1"}""";
        Assert.Equal(expected, new ScanPolicy().CanonicalJson);
        Assert.Equal("c981968e6709e811cff2ca70d8b45b9ffcd136b292112fed00713b7497b2afca", new ScanPolicy().Fingerprint);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), new ScanPolicy().CanonicalBytes());
        var a = new ScanPolicy { BasisIds = ["z", "A"], Exclusions = ["z", "a"], Limits = ImmutableSortedDictionary.Create<string, ulong>().Add("z", 20).Add("a", 10) };
        var b = a with { BasisIds = ["A", "z"], Exclusions = ["a", "z"] };
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(a.Fingerprint, (a with { Exclusions = ["Z", "a"] }).Fingerprint);
        Assert.Equal(64, a.Fingerprint.Length);
    }

    [Fact]
    public void EveryUnqualifiedActionFailsClosedWithAnExplanation()
    {
        foreach (var verb in Enum.GetValues<AnalysisVerb>())
        {
            var capability = new AnalysisCapabilities().For(verb);
            Assert.False(capability.Enabled);
            Assert.NotEmpty(capability.Reason);
            Assert.Equal(AnalysisAssurance.ObservedMetadata, capability.Assurance);
        }
    }
}
