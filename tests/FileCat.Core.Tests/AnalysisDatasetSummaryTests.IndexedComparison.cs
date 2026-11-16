using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    private static AnalysisSnapshot ComparisonSample()
    {
        var s = AnalysisAccountingTests.Golden();
        string host = AnalysisHostProfileIdentity.Create(Guid.Parse("01010101-0202-0303-0404-050505050505"),
            AnalysisHostObservation.LinuxApplicationMachineId, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()).ToString();
        return s with { Identity = s.Identity with { HostProfile = host },
            Roots = [s.Roots[0] with { ObservedObjectIdentity = "fixture-root-object", ConnectionIdentityReference = "11111111222233334444555555555555" }],
            Objects = s.Objects.Select(o => o with { VersionEvidence = "v1", ModifiedUtc = DateTimeOffset.UnixEpoch }).ToImmutableArray() };
    }
    private static AnalysisEnumerationProgress ComparisonEnumeration(AnalysisSnapshot s)
    {
        ulong directories = (ulong)s.Nodes.Count(n => n.Kind == AnalysisKind.Directory && n.IncludedByPolicy && n.MetadataState == MeasurementState.Known && n.Cloud != true);
        return new(directories, s.Phase == AnalysisPhase.Finished && s.Coverage == NamespaceCoverage.CompleteUnderPolicy ? directories : 0, 0);
    }
    private static async Task CheckIndexedComparison(AnalysisSnapshot old, AnalysisSnapshot current, AnalysisMetric metric,
        IReadOnlyDictionary<string, string>? mapping = null)
    {
        Ordinary(); using var io = new DeviceIoScheduler();
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);
        await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        await using var actual = await now.CompareIndexedAsync(before, metric, mapping, Cancellation);
        old = old with { Freshness = before.Descriptor.Freshness, CoverageDetails = before.Descriptor.CoverageDetails.Observations };
        current = current with { Freshness = now.Descriptor.Freshness, CoverageDetails = now.Descriptor.CoverageDetails.Observations };
        using var expected = AnalysisComparison.Build(old, current, metric, mapping, Cancellation);
        var x = expected.Compatibility; var y = actual.Compatibility;
        Assert.Equal((x.SamePolicy, x.SameBasis, x.SameScope, x.VerifiedIdentityContext, x.PreviousComplete, x.CurrentComplete),
            (y.SamePolicy, y.SameBasis, y.SameScope, y.VerifiedIdentityContext, y.PreviousComplete, y.CurrentComplete));
        Equal(expected.PreviousTotals, actual.PreviousTotals); Equal(expected.CurrentTotals, actual.CurrentTotals);
        Assert.Equal(expected.NetChange, actual.NetChange); Assert.Equal((ulong)expected.Rows.Length, actual.Count);
        var seen = new HashSet<string>(); ulong offset = 0; UInt128 last = UInt128.MaxValue;
        while (true)
        {
            var page = await actual.ReadAsync(offset, 2, Cancellation);
            foreach (var row in page.Rows)
            {
                async Task<ulong[]> Members(bool previous, AnalysisComparisonAccountingEntity? entity)
                {
                    if (entity is null) return [];
                    var members = new List<ulong>(); ulong at = 0;
                    while (true)
                    {
                        var aliases = await actual.ReadMembersAsync(previous, entity.Id, at, 1, Cancellation);
                        members.AddRange(aliases.Rows.Select(n => n.Id)); if (!aliases.HasMore) break;
                        Assert.True(aliases.NextOffset > at); at = aliases.NextOffset;
                    }
                    return members.Order().ToArray();
                }
                ulong[] oldIds = await Members(true, row.Previous), newIds = await Members(false, row.Current);
                string key = string.Join(',', oldIds) + "|" + string.Join(',', newIds); Assert.True(seen.Add(key));
                var reference = Assert.Single(expected.Rows.Where(r => (r.Previous?.NodeIds ?? []).Order().SequenceEqual(oldIds) && (r.Current?.NodeIds ?? []).Order().SequenceEqual(newIds)));
                Assert.Equal(reference.Change, row.Change); Assert.Equal(reference.Evidence, row.Evidence); Assert.Equal(reference.Delta, row.Delta);
                Assert.Equal(reference.Previous?.Value, row.Previous?.Value); Assert.Equal(reference.Current?.Value, row.Current?.Value);
                Assert.Equal(reference.RelativePercent, row.RelativePercent);
                UInt128 amount = row.Delta?.Magnitude ?? 0; Assert.True(last >= amount); last = amount;
            }
            if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
        }
        Assert.Equal(expected.Rows.Length, seen.Count);
    }
    public static IEnumerable<object[]> IndexedComparisonCases()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
            foreach (string scenario in new[] { "same", "growth", "replace", "added-removed", "alias", "split", "merge", "policy", "basis", "previous-partial", "current-partial", "unknown" })
                yield return [metric, scenario];
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonCases))]
    public async Task IndexedComparisonMatchesAllMetricChangesCoverageAndAliasAmbiguity(AnalysisMetric metric, string scenario)
    {
        var old = ComparisonSample(); var current = old with { Identity = old.Identity with { Revision = 2, ObservedUtc = DateTimeOffset.UnixEpoch.AddSeconds(1) } };
        AnalysisSnapshot Split(AnalysisSnapshot s) => s with
        {
            Nodes = s.Nodes.SetItem(4, s.Nodes[4] with { ObjectId = "X2" }),
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }, s.Objects[1], s.Objects[0] with { Id = "X2", VerifiedIdentity = "device:X2", LinkCount = AnalysisMeasurement.Known(1) }],
            Facts = s.Facts.Add(s.Facts[0] with { Id = "f3", ObjectId = "X2" })
        };
        AnalysisSnapshot WithoutZ(AnalysisSnapshot s) => s with { Nodes = s.Nodes.Where(n => n.Id != 6).ToImmutableArray(), Objects = [s.Objects[0]], Facts = [s.Facts[0]] };
        switch (scenario)
        {
            case "growth": current = current with { Objects = current.Objects.SetItem(0, current.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(130) }), Facts = current.Facts.SetItem(0, current.Facts[0] with { Measurement = AnalysisMeasurement.Known(96) }) }; break;
            case "replace": current = current with { Objects = current.Objects.SetItem(0, current.Objects[0] with { VerifiedIdentity = "replacement" }) }; break;
            case "added-removed": current = current with { Nodes = current.Nodes.SetItem(5, current.Nodes[5] with { Name = "new.bin", ObjectId = "Q" }),
                Objects = [current.Objects[0], current.Objects[1] with { Id = "Q", VerifiedIdentity = "new-object" }], Facts = [current.Facts[0], current.Facts[1] with { ObjectId = "Q" }] }; break;
            case "alias": current = current with { Nodes = current.Nodes.Where(n => n.Id != 5).ToImmutableArray(), Objects = current.Objects.SetItem(0, current.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }) }; break;
            case "split": current = Split(current); break;
            case "merge": old = Split(old); break;
            case "policy": current = current with { Policy = current.Policy with { Exclusions = ["excluded-subtree"] } }; break;
            case "basis": current = current with { Facts = current.Facts.SetItem(0, current.Facts[0] with { BasisId = "other-basis" }) }; break;
            case "previous-partial": old = WithoutZ(old) with { Phase = AnalysisPhase.Stopped, Coverage = NamespaceCoverage.Partial }; break;
            case "current-partial": current = WithoutZ(current) with { Phase = AnalysisPhase.Stopped, Coverage = NamespaceCoverage.Partial }; break;
            case "unknown": old = old with { Nodes = old.Nodes.SetItem(5, old.Nodes[5] with { Kind = AnalysisKind.Unknown, ObjectId = null, MetadataState = MeasurementState.Failed }) }; break;
        }
        await CheckIndexedComparison(old, current, metric);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonIdentityVersionMoveAndAttributionRequireQualifiedContext(AnalysisMetric metric)
    {
        var old = ComparisonSample(); old = old with { Nodes = old.Nodes.Where(n => n.Id != 5).ToImmutableArray(), Objects = old.Objects.SetItem(0, old.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }) };
        var current = old with { Identity = old.Identity with { SessionId = Guid.NewGuid() }, Nodes = old.Nodes.SetItem(3, old.Nodes[3] with { Name = "moved.bin", ParentId = 3 }) };
        await CheckIndexedComparison(old, current, metric);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("host")]
    [InlineData("root-object")]
    [InlineData("legacy")]
    public async Task IndexedComparisonExplicitMappingCannotUpgradeUnqualifiedIdentityEvidence(string fault)
    {
        var old = ComparisonSample(); var current = old with { Identity = old.Identity with { SessionId = Guid.NewGuid() }, Nodes = old.Nodes.SetItem(5, old.Nodes[5] with { Name = "renamed.bin" }) };
        current = fault switch
        {
            "binding" => current with { Roots = [current.Roots[0] with { ConnectionIdentityReference = Guid.NewGuid().ToString("N") }] },
            "host" => current with { Identity = current.Identity with { HostProfile = AnalysisHostProfileIdentity.Unverified(Guid.NewGuid()).ToString() } },
            "root-object" => current with { Roots = [current.Roots[0] with { ObservedObjectIdentity = "another-root" }] },
            _ => current with { Identity = current.Identity with { HostProfile = "legacy" } }
        };
        await CheckIndexedComparison(old, current, AnalysisMetric.FileSize, new Dictionary<string, string> { ["r"] = "r" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedComparisonComparesExactNonuniformMetadataSetsRatherThanOnlyRepresentatives(bool different)
    {
        var old = ComparisonSample(); old = old with { Nodes = old.Nodes.SetItem(3, old.Nodes[3] with { Sparse = true }).SetItem(4, old.Nodes[4] with { Sparse = false }) };
        var current = old with { Nodes = old.Nodes.SetItem(3, old.Nodes[3] with { Sparse = false }).SetItem(4, old.Nodes[4] with { Sparse = different ? null : true }) };
        await CheckIndexedComparison(old, current, AnalysisMetric.UniqueFileSize);
    }

    [Fact]
    public async Task IndexedComparisonRetainsBothInputsAndRejectsInvalidPagesUntilActualClose()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var left = new MemoryDirectory(); var right = new MemoryDirectory();
        var a = await Build(s, io, left, enumeration: ComparisonEnumeration(s)); var b = await Build(s, io, right, enumeration: ComparisonEnumeration(s));
        var before = await a.AcquireAsync(Cancellation); var current = await b.AcquireAsync(Cancellation);
        var comparison = await current.CompareIndexedAsync(before, AnalysisMetric.OnDisk, cancellation: Cancellation);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => current.CompareIndexedAsync(before, AnalysisMetric.Files, cancellation: Cancellation));
            await a.DisposeAsync(); await b.DisposeAsync(); await before.DisposeAsync(); await current.DisposeAsync();
            Assert.False(left.Closed); Assert.False(right.Closed); Assert.Equal(2UL, (await comparison.ReadAsync(cancellation: Cancellation)).Total);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => comparison.ReadAsync(3, cancellation: Cancellation));
        }
        finally { await comparison.DisposeAsync(); await before.DisposeAsync(); await current.DisposeAsync(); await a.DisposeAsync(); await b.DisposeAsync(); }
        Assert.True(left.Closed); Assert.True(right.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => comparison.ReadAsync(cancellation: Cancellation));
    }

    [Fact]
    public async Task IndexedComparisonReadFailureOrCancellationCannotPublishPartialRowsOrLeakItsInputPlaces()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var directory = new MemoryDirectory();
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var b = await Build(s, io, directory, enumeration: ComparisonEnumeration(s));
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        directory.BeforeNextRead = () => throw new IOException("comparison read failed");
        await Assert.ThrowsAsync<IOException>(() => current.CompareIndexedAsync(before, AnalysisMetric.OnDisk, cancellation: Cancellation));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); directory.BeforeNextRead = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current.CompareIndexedAsync(before, AnalysisMetric.Files, cancellation: cancel.Token));
        await using var result = await current.CompareIndexedAsync(before, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        Assert.Equal(2UL, result.Count);
    }

    [Theory]
    [InlineData(AnalysisMetric.UniqueFileSize, false)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    public async Task IndexedComparisonKeepsFullUnsigned128BitSignedDeltasAndKnownZero(AnalysisMetric metric, bool shrinking)
    {
        var old = ComparisonSample();
        // One alias keeps every captured metric within UInt128; two MaxValue aliases
        // would intentionally overflow the independent reference File-size total.
        old = old with { Nodes = old.Nodes.Where(n => n.Id != 5).ToImmutableArray(), Objects = old.Objects.SetItem(0, old.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }) };
        var current = old;
        old = old with { Objects = [old.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(shrinking ? UInt128.MaxValue : 1) }, old.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }],
            Facts = [old.Facts[0] with { Measurement = AnalysisMeasurement.Known(shrinking ? UInt128.MaxValue : 1) }, old.Facts[1] with { Measurement = AnalysisMeasurement.Known(0) }] };
        current = current with { Objects = [current.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(shrinking ? 0 : UInt128.MaxValue) }, current.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }],
            Facts = [current.Facts[0] with { Measurement = AnalysisMeasurement.Known(shrinking ? 0 : UInt128.MaxValue) }, current.Facts[1] with { Measurement = AnalysisMeasurement.Known(0) }] };
        await CheckIndexedComparison(old, current, metric);
    }

    [Fact]
    public async Task IndexedComparisonEmptyDeclaredFileBasisAndRawEncodingChangesRemainConservative()
    {
        var current = ComparisonSample(); current = current with { Policy = current.Policy with { BasisIds = ["fixture-v1"] } };
        var old = current with { Nodes = current.Nodes.Where(n => n.Kind == AnalysisKind.Directory).ToImmutableArray(), Objects = [], Facts = [] };
        await CheckIndexedComparison(old, current, AnalysisMetric.OnDisk);
        old = current;
        current = current with { Nodes = current.Nodes.SetItem(5, current.Nodes[5] with { NameEncoding = "unix-bytes", RawName = [.. System.Text.Encoding.UTF8.GetBytes(current.Nodes[5].Name)] }) };
        await CheckIndexedComparison(old, current, AnalysisMetric.FileSize);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonPagesBeyondMemoryFallbackWithOneSharedCohortOrManyPathRows(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var old = ComparisonSample(); const int count = 14000;
        old = old with { Nodes = ImmutableArray.Create(old.Nodes[0]).AddRange(Enumerable.Range(0, count).Select(i =>
                new AnalysisNode((ulong)i + 2, "r", 1, "entry-" + i.ToString("D6"), AnalysisKind.File, "X"))),
            Objects = [old.Objects[0] with { LinkCount = AnalysisMeasurement.Known(count) }], Facts = [old.Facts[0]] };
        var current = old with { Objects = [old.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(101) }],
            Facts = [old.Facts[0] with { Measurement = AnalysisMeasurement.Known(81) }] };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(old, current, metric, cancellation: Cancellation));
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old)); await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current));
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        await using var result = await now.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        bool shared = metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk;
        Assert.Equal(shared ? 1UL : (ulong)count, result.Count); Assert.Equal(new AnalysisDelta(false, metric == AnalysisMetric.Files ? 0 : shared ? 1U : (uint)count), result.NetChange);
        ulong offset = 0, observed = 0;
        while (true)
        {
            var page = await result.ReadAsync(offset, 113, Cancellation); Assert.InRange(page.Rows.Length, 1, 113);
            foreach (var row in page.Rows)
            { Assert.Equal(metric == AnalysisMetric.Files ? AnalysisChange.MetadataChanged : AnalysisChange.Growing, row.Change); observed += row.Current!.ObservedPaths; }
            if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
        }
        Assert.Equal((ulong)count, observed);
    }

    [Fact]
    public async Task IndexedComparisonDefersLargeRawNameDetailsAndKeepsScalarPagesWithinTheirPayloadBound()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        s = s with { Nodes = [s.Nodes[0], s.Nodes[3] with { Id = 2, ParentId = 1, Name = new string('n', AnalysisLimits.RawNameBytes) }],
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }], Facts = [s.Facts[0]] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.FileSize, cancellation: Cancellation);
        var row = Assert.Single((await result.ReadAsync(cancellation: Cancellation)).Rows);
        Assert.Equal(AnalysisChange.NoObservedMetadataChange, row.Change); Assert.Null(row.Current!.RepresentativePath); Assert.False(row.Current.DetailsLoaded);
        var side = await result.ReadSideAsync(false, row.Current.Id, Cancellation);
        Assert.True(side.DetailsLoaded); Assert.Equal(2UL, side.RepresentativePath);
        var member = Assert.Single((await result.ReadMembersAsync(false, side.Id, cancellation: Cancellation)).Rows);
        Assert.Equal(AnalysisLimits.RawNameBytes, member.RawName.IsEmpty ? AnalysisRawNames.Encode(member.NameEncoding, member.Name).Length : member.RawName.Length);
        Assert.Equal(s.Nodes[1].Name, member.Name);
    }
}
