using System.Collections.Immutable;
using System.Text;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed partial class AnalysisComparisonTests
{
    private static readonly CancellationToken Cancellation = TestContext.Current.CancellationToken;

    [Fact]
    public void WindowsObservedPathsKeepTheirSourceSeparatorsOnEveryHost()
    {
        var sample = Sample(("same", "k", 1, 1, "one"));
        sample = sample with { Roots = [sample.Roots[0] with { CanonicalLocator = @"C:\fixture", DisplayLocator = @"C:\fixture", NameRules = AnalysisNameRules.Windows }] };
        using var result = AnalysisComparison.Build(sample, sample, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Equal(@"C:\fixture\same", Assert.Single(result.Rows).Current!.Paths[0]);
    }

    [Fact]
    public void RootSuggestionsUseCapturedSourceContextRatherThanPositionalRootIds()
    {
        var old = Sample(("same", "k", 1, 1, "one")); var current = old with
        { Roots = [old.Roots[0] with { Id = "other-id" }], Nodes = old.Nodes.Select(n => n with { RootId = "other-id" }).ToImmutableArray() };
        var suggested = AnalysisComparison.SuggestRootMapping(old, current);
        Assert.Equal("other-id", Assert.Single(suggested).Value); Assert.Equal("r", Assert.Single(suggested).Key);
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.SuggestRootMapping(old, current with { Roots = [current.Roots[0] with { SourceIdentity = "different-source" }] }));
    }

    [Fact]
    public void EmptyKnownFileScopeHasAZeroAllocationBaselineWithinTheDeclaredBasis()
    {
        using var result = AnalysisComparison.Build(Sample(), Sample(("new", "n", 10, 8, "one")), AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.True(result.Compatibility.SameBasis); Assert.Equal(new AnalysisDelta(false, 8), result.NetChange);
        var added = Assert.Single(result.Rows); Assert.Equal(AnalysisChange.Added, added.Change); Assert.Null(added.RelativePercent);
    }

    [Fact]
    public void ChangedRawNameRepresentationCannotInventAdditionsAndRemovals()
    {
        var old = Sample(("same", "k", 10, 8, "one")); var current = Sample(("same", "k", 10, 8, "one"));
        current = current with { Nodes = current.Nodes.SetItem(1, current.Nodes[1] with { NameEncoding = "unix-bytes", RawName = Encoding.UTF8.GetBytes("same").ToImmutableArray() }), Objects = [current.Objects[0] with { VerifiedIdentity = null }] };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.False(result.Compatibility.SameScope); Assert.Null(result.NetChange);
        Assert.DoesNotContain(result.Rows, r => r.Change == AnalysisChange.Added || r.Change == AnalysisChange.Removed || r.Change == AnalysisChange.Growing);
    }

    [Fact]
    public void ProducerBoundsAccountForLongVersionsAndRejectDeepPathExpansionWithoutPartialResults()
    {
        var small = Sample(("same", "k", 1, 1, "one"));
        var large = small with { Objects = Enumerable.Range(0, 400).Select(i => new AnalysisObject("o" + i, "identity" + i, AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1), VersionEvidence: new string('v', 4096))).ToImmutableArray() };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(large, large, AnalysisMetric.FileSize, cancellation: Cancellation));
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(new(1, "r", 0, "fixture", AnalysisKind.Directory));
        for (ulong id = 2; id < 102; id++) nodes.Add(new(id, "r", id - 1, new string('a', 1024), AnalysisKind.Directory));
        var deep = small with { Nodes = nodes.ToImmutable(), Objects = [], Facts = [] };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(deep, deep, AnalysisMetric.FileSize, cancellation: Cancellation));
        using var valid = AnalysisComparison.Build(small, small, AnalysisMetric.FileSize, cancellation: Cancellation); Assert.Single(valid.Rows);
    }

    [Fact]
    public async Task NativeUnixInventoriesCompareActualGrowthAndHardLinkAttribution()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Unix native fixtures.");
        string root = Path.Combine(Environment.CurrentDirectory, ".tmp", "comparison-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "data.bin"), new byte[128]); File.WriteAllBytes(Path.Combine(root, "shared.bin"), new byte[64]);
            Assert.Equal(0, Link(Path.Combine(root, "shared.bin"), Path.Combine(root, "shared-alias.bin")));
            using var io = new DeviceIoScheduler();
            async Task<AnalysisSnapshot> Scan()
            {
                using var session = new AnalysisCoordinator(io, "native-comparison-fixture").Start([new UnixAnalysisBackend(root)]);
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(15), Cancellation); return session.Snapshot();
            }
            var old = await Scan();
            using (var wire = new MemoryStream())
            {
                AnalysisInventoryFormat.Write(wire, old, Guid.NewGuid(), Cancellation); wire.Position = 0;
                using var saved = AnalysisInventoryFormat.Read(wire, Cancellation);
                using var same = AnalysisComparison.Build(saved.Snapshot, old, AnalysisMetric.OnDisk, cancellation: Cancellation);
                Assert.All(same.Rows, row => Assert.Equal(AnalysisChange.NoObservedMetadataChange, row.Change));
            }
            File.WriteAllBytes(Path.Combine(root, "data.bin"), new byte[256]); File.WriteAllBytes(Path.Combine(root, "added.bin"), new byte[3]); File.Delete(Path.Combine(root, "shared-alias.bin"));
            var current = await Scan();
            Assert.Throws<InvalidOperationException>(() => AnalysisComparison.SuggestRootMapping(old, current));
            var mapping = new Dictionary<string, string> { [old.Roots[0].Id] = current.Roots[0].Id };
            using (var logical = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, mapping, Cancellation))
            {
                Assert.Equal(new AnalysisDelta(false, 67), logical.NetChange);
                Assert.Contains(logical.Rows, r => r.Change == AnalysisChange.Growing); Assert.Contains(logical.Rows, r => r.Change == AnalysisChange.Added);
            }
            using var allocation = AnalysisComparison.Build(old, current, AnalysisMetric.OnDisk, mapping, Cancellation);
            var shared = Assert.Single(allocation.Rows, r => r.Current?.Paths.Any(p => p.EndsWith("/shared.bin", StringComparison.Ordinal)) == true);
            Assert.Equal(AnalysisChange.Attribution, shared.Change); Assert.Equal(new AnalysisDelta(false, 0), shared.Delta);
        }
        finally { Directory.Delete(root, true); }
    }
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)] private static partial int Link(string original, string alias);

    [Fact]
    public void FileCountParityCannotHideOtherObservedMetadataChangesWithoutVersionEvidence()
    {
        var old = Sample(("same", "k", 10, 8, "one")); var current = Sample(("same", "k", 20, 8, "one"));
        old = old with { Objects = [old.Objects[0] with { VersionEvidence = null }] };
        current = current with { Objects = [current.Objects[0] with { VersionEvidence = null }] };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.Files, cancellation: Cancellation);
        Assert.Equal(AnalysisChange.MetadataChanged, Assert.Single(result.Rows).Change);
        Assert.Equal(new AnalysisDelta(false, 0), result.NetChange);
    }

    [Fact]
    public void ChangedAncestorWithApparentlyKnownChildDoesNotAuthorizeADifference()
    {
        var old = Sample(("same", "k", 10, 8, "one")); var current = Sample(("same", "k", 20, 8, "two"));
        current = current with { Nodes = current.Nodes.SetItem(0, current.Nodes[0] with { IssueCodes = ["changed-during-observation"] }),
            Issues = [new("changed-during-observation", 1, MeasurementState.Changed) { RootId = "r" }] };
        current = current with { CoverageDetails = AnalysisCoverage.Create(current, 1, 1, 0) };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.False(result.Compatibility.CurrentComplete); Assert.Null(result.NetChange);
        var row = Assert.Single(result.Rows); Assert.Equal(AnalysisChange.Unknown, row.Change); Assert.Null(row.Delta);
    }

    [Fact]
    public void UnknownEntryKindCannotMakeAnApparentlyCompleteZeroFileBaseline()
    {
        var old = Sample(); var current = Sample(("new", "n", 10, 8, "one"));
        old = old with { Nodes = old.Nodes.Add(new(2, "r", 1, "unreadable", AnalysisKind.Unknown) { MetadataState = MeasurementState.Failed }) };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.Files, cancellation: Cancellation);
        Assert.False(result.Compatibility.PreviousComplete); Assert.Null(result.NetChange);
        Assert.DoesNotContain(result.Rows, r => r.Change == AnalysisChange.Added || r.Change == AnalysisChange.Removed);
    }

    [Fact]
    public void RootIdentifiersCannotCollideWithTaggedChildPathKeys()
    {
        var old = Sample(("z", "z", 10, 8, "one")); var current = Sample();
        var first = old.Roots[0] with { Id = "a" }; var second = first with { Id = "a/utf8:7A", Kind = AnalysisKind.File, CanonicalLocator = "/another", DisplayLocator = "/another", SourceIdentity = "volume:second" };
        AnalysisSnapshot Extend(AnalysisSnapshot s) => s with
        {
            Roots = [first, second], Nodes = [..s.Nodes.Select(n => n with { RootId = "a" }), new((ulong)s.Nodes.Length + 1, second.Id, 0, "another", AnalysisKind.File, "other")],
            Objects = [..s.Objects, new("other", "volume:other", AnalysisMeasurement.Known(100), AnalysisMeasurement.Known(1), VersionEvidence: "one")],
            Facts = [..s.Facts, new("other-fact", "other", null, "primary", "fixture-v1", AnalysisMeasurement.Known(80), "fixture-v1", DateTimeOffset.UnixEpoch, [])]
        };
        using var result = AnalysisComparison.Build(Extend(old), Extend(current), AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Equal(2, result.Rows.Length); Assert.Contains(result.Rows, r => r.Change == AnalysisChange.Removed); Assert.Contains(result.Rows, r => r.Change == AnalysisChange.NoObservedMetadataChange);
        Assert.Equal(new AnalysisDelta(true, 10), result.NetChange);
    }
    private static AnalysisSnapshot Sample(params (string Name, string Object, ulong Size, ulong Allocated, string Version)[] files)
    {
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(new(1, "r", 0, "fixture", AnalysisKind.Directory));
        foreach (var file in files) nodes.Add(new((ulong)nodes.Count + 1, "r", 1, file.Name, AnalysisKind.File, file.Object));
        var objects = files.DistinctBy(f => f.Object).Select(f => new AnalysisObject(f.Object, "volume:" + f.Object, AnalysisMeasurement.Known(f.Size),
            AnalysisMeasurement.Known((ulong)files.Count(a => a.Object == f.Object)), DateTimeOffset.UnixEpoch, f.Version)
        { ObservedAliases = nodes.Where(n => n.ObjectId == f.Object).Select(n => n.Id).ToImmutableArray() }).ToImmutableArray();
        var facts = files.DistinctBy(f => f.Object).Select(f => new StorageFact("fact:" + f.Object, f.Object, null, "primary", "fixture-v1",
            AnalysisMeasurement.Known(f.Allocated), "fixture-v1", DateTimeOffset.UnixEpoch, [])).ToImmutableArray();
        string profile = AnalysisHostProfileIdentity.Create(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), AnalysisHostObservation.LinuxApplicationMachineId, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()).ToString();
        return new(new(Guid.NewGuid(), 1, 1, profile, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            [new("r", "file", "/fixture", "/fixture", "volume:root", IdentityAssurance.Verified, "fixture") { NameRules = AnalysisNameRules.Unix,
                ConnectionIdentityReference = "66554433221144338899aabbccddeeff", ObservedObjectIdentity = "volume:root-object" }],
            nodes.ToImmutable(), objects, facts, [], new() { BasisIds = ["fixture-v1"] }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public void CompatibleInventoriesExposeAdditionsRemovalsAndExactTotals(AnalysisMetric metric)
    {
        var old = Sample(("keep", "k", 10, 8, "one"), ("removed", "r", 20, 16, "one"));
        var current = Sample(("keep", "k", 15, 12, "two"), ("added", "a", 2, 4, "one"));
        using var result = AnalysisComparison.Build(old, current, metric, cancellation: Cancellation);
        Assert.True(result.Compatibility.ComparableTotals); Assert.NotNull(result.NetChange);
        Assert.Contains(result.Rows, r => r.Change == AnalysisChange.Added);
        Assert.Contains(result.Rows, r => r.Change == AnalysisChange.Removed);
        Assert.Contains(result.Rows, r => r.Change == (metric == AnalysisMetric.Files ? AnalysisChange.MetadataChanged : AnalysisChange.Growing));
        Assert.Equal(metric switch { AnalysisMetric.OnDisk => new AnalysisDelta(true, 8), AnalysisMetric.Files => new(false, 0), _ => new(true, 13) }, result.NetChange);
        Assert.Equal(AnalysisTaint.OrdinarySensitive, result.Taint);
    }

    [Fact]
    public void EarlierUnreadableCoverageIsPreviouslyUnknownAndCannotInventGrowth()
    {
        var old = Sample(("known", "k", 10, 8, "one")) with { Coverage = NamespaceCoverage.Partial };
        var current = Sample(("known", "k", 10, 8, "one"), ("previously unread", "new", 100, 80, "one"));
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Null(result.NetChange); var row = Assert.Single(result.Rows, r => r.Current!.Paths[0].EndsWith("/previously unread", StringComparison.Ordinal));
        Assert.Equal(AnalysisChange.PreviouslyUnknown, row.Change); Assert.Null(row.Delta); Assert.Null(row.RelativePercent);
    }

    [Fact]
    public void PolicyAndAllocationBasisChangesStayLimitedAndNeverBecomeZeroBaselines()
    {
        var old = Sample(("same", "k", 10, 8, "one"));
        var current = Sample(("same", "k", 20, 16, "two")) with { Policy = old.Policy with { Exclusions = ["other-subtree"] } };
        using (var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation))
        { Assert.False(result.Compatibility.SamePolicy); Assert.Null(result.NetChange); Assert.Equal(AnalysisChange.Unknown, Assert.Single(result.Rows).Change); Assert.Equal((UInt128)20, result.CurrentTotals.Known); }
        current = current with { Policy = old.Policy, Facts = current.Facts.Select(f => f with { BasisId = "another-basis" }).ToImmutableArray() };
        using var basis = AnalysisComparison.Build(old, current, AnalysisMetric.OnDisk, cancellation: Cancellation);
        Assert.False(basis.Compatibility.SameBasis); Assert.Null(basis.NetChange); Assert.Null(Assert.Single(basis.Rows).Delta);
    }

    [Theory]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    public void HardLinkAliasChangesAreOneAttributionRowWithoutStorageGrowth(AnalysisMetric metric)
    {
        var old = Sample(("one", "k", 100, 80, "before"), ("two", "k", 100, 80, "before"));
        var current = Sample(("one", "k", 100, 80, "after"));
        using var result = AnalysisComparison.Build(old, current, metric, cancellation: Cancellation);
        var row = Assert.Single(result.Rows); Assert.Equal(AnalysisChange.Attribution, row.Change);
        Assert.Equal(new AnalysisDelta(false, 0), row.Delta); Assert.Equal(new AnalysisDelta(false, 0), result.NetChange);
        Assert.Equal(2, row.Previous!.Paths.Length); Assert.Single(row.Current!.Paths);
        Assert.Equal(AnalysisComparisonEvidence.PathBased, row.Evidence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenameRequiresCompatibleVersionEvidenceAndNeverTrustsAReusedIdAlone(bool sameVersion)
    {
        var old = Sample(("before", "k", 10, 8, "one")); var current = Sample(("after", "k", 10, 8, sameVersion ? "one" : "different"));
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        if (sameVersion) { var row = Assert.Single(result.Rows); Assert.Equal(AnalysisChange.Moved, row.Change); Assert.Equal(AnalysisComparisonEvidence.CompatibleIdentityAndVersion, row.Evidence); }
        else { Assert.Equal(2, result.Rows.Length); Assert.DoesNotContain(result.Rows, r => r.Change == AnalysisChange.Moved); }
    }

    [Fact]
    public void ContradictoryCreationMetadataBlocksAnApparentlyMatchingRenameVersion()
    {
        var old = Sample(("before", "k", 10, 8, "one")); var current = Sample(("after", "k", 10, 8, "one"));
        old = old with { Nodes = old.Nodes.SetItem(1, old.Nodes[1] with { Times = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch, 1) }) };
        current = current with { Nodes = current.Nodes.SetItem(1, current.Nodes[1] with { Times = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch.AddDays(1), 1) }) };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Equal(2, result.Rows.Length); Assert.DoesNotContain(result.Rows, r => r.Change == AnalysisChange.Moved);
    }

    [Fact]
    public void DifferentSourceNeedsExplicitStructuralMappingAndCannotClaimIdentityContinuity()
    {
        var old = Sample(("same", "k", 10, 8, "one")); var current = Sample(("same", "k", 20, 16, "two"));
        current = current with { Roots = [current.Roots[0] with { CanonicalLocator = "/other", SourceIdentity = "different-volume" }], Taint = AnalysisTaint.PrivilegedDerived | AnalysisTaint.ImportedUnverified };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation));
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, new Dictionary<string, string> { ["r"] = "r" }, Cancellation);
        Assert.False(result.Compatibility.VerifiedIdentityContext); Assert.Equal(AnalysisComparisonEvidence.Structural, Assert.Single(result.Rows).Evidence);
        Assert.Equal(AnalysisPrivacy.All, result.Taint); Assert.Equal(new AnalysisDelta(false, 10), result.NetChange);
    }

    [Fact]
    public void InvalidUnixNamesWithTheSameLossyDisplayNeverMatchAsOnePath()
    {
        var old = Sample(("bad�", "k", 10, 8, "one")); var current = Sample(("bad�", "k", 10, 8, "one"));
        old = old with { Nodes = old.Nodes.SetItem(1, old.Nodes[1] with { NameEncoding = "unix-bytes", RawName = [(byte)'b', (byte)'a', (byte)'d', 0xff] }), Objects = [old.Objects[0] with { VerifiedIdentity = null }] };
        current = current with { Nodes = current.Nodes.SetItem(1, current.Nodes[1] with { NameEncoding = "unix-bytes", RawName = [(byte)'b', (byte)'a', (byte)'d', 0xfe] }), Objects = [current.Objects[0] with { VerifiedIdentity = null }] };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Equal(2, result.Rows.Length); Assert.Contains(result.Rows, r => r.Change == AnalysisChange.Added); Assert.Contains(result.Rows, r => r.Change == AnalysisChange.Removed);
    }

    [Fact]
    public void ExactDifferenceNeverWrapsAtUInt128AndZeroBaselineHasNoInfinitePercent()
    {
        var old = Sample(("same", "k", 1, 1, "one")); var current = Sample(("same", "k", 0, 0, "two"));
        old = old with { Objects = [old.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(UInt128.MaxValue) }] };
        using var result = AnalysisComparison.Build(old, current, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Equal(new AnalysisDelta(true, UInt128.MaxValue), result.NetChange); Assert.Equal(result.NetChange, Assert.Single(result.Rows).Delta);
        Assert.Null(new AnalysisDelta(false, 10).RelativePercent(0)); Assert.Equal(100d, new AnalysisDelta(false, 10).RelativePercent(10));
    }

    [Fact]
    public void FailedCanceledAndClosedComparisonsReleaseTheirBoundedMemoryAdmission()
    {
        var sample = Sample(("same", "k", 1, 1, "one")); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AnalysisComparison.Build(sample, sample, AnalysisMetric.FileSize, cancellation: canceled.Token));
        Assert.Throws<InvalidDataException>(() => AnalysisComparison.Build(sample with { Roots = [] }, sample, AnalysisMetric.FileSize, cancellation: Cancellation));
        using var first = AnalysisComparison.Build(sample, sample, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(sample, sample, AnalysisMetric.FileSize, cancellation: Cancellation));
        first.Dispose(); first.Dispose(); Assert.Throws<ObjectDisposedException>(() => first.Rows); using var second = AnalysisComparison.Build(sample, sample, AnalysisMetric.FileSize, cancellation: Cancellation);
        Assert.Equal(AnalysisChange.NoObservedMetadataChange, Assert.Single(second.Rows).Change);
    }
}
