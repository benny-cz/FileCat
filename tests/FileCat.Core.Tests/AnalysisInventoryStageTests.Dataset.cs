using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;
using FileCat.Tests;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisInventoryStageTests
{
    private static AnalysisSnapshot OpaqueIds()
    {
        var s = AnalysisAccountingTests.Golden(); ulong[] ids = [0, ulong.MaxValue, 400, 2, ulong.MaxValue - 1, 1, 3];
        return s with
        {
            Nodes = s.Nodes.Select(n => n with { Id = ids[n.Id], ParentId = ids[n.ParentId] }).ToImmutableArray(),
            Issues = [new("fixture-issue", 1, MeasurementState.Failed) { RootId = "r" }],
            Facts = s.Facts.Add(new("directory", null, 400, "directory", "fixture-v1", AnalysisMeasurement.Known(8), "fixture", DateTimeOffset.UnixEpoch, []))
        };
    }
    private static byte[] Encode(AnalysisSnapshot snapshot)
    {
        using var output = new MemoryStream(); AnalysisInventoryFormat.Write(output, snapshot, Guid.NewGuid(), Cancellation); return output.ToArray();
    }
    [Fact]
    public async Task OriginalIdsSurviveFullValidationEveryMetricHierarchyFilterAndExplorerAfterStageClosure()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        var snapshot = OpaqueIds(); var input = new MemoryDirectory { Input = Encode(snapshot) };
        await using var stage = await AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, Cancellation);
        await using var dataset = await stage.BuildDatasetAsync(Cancellation); await stage.DisposeAsync(); workspace.Dispose();
        Assert.True(input.Closed); Assert.False(scratch.Closed); Assert.Equal(AnalysisOrigin.ImportedInventory, dataset.Descriptor.Origin);
        await using var lease = await dataset.AcquireAsync(Cancellation); await dataset.DisposeAsync();
        Assert.Equal(snapshot.Nodes.Select(n => n.Id), (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Rows.Select(n => n.Id));
        Assert.Equal(new ulong[] { 400, 2 }, (await lease.ReadChildrenAsync(0, ulong.MaxValue, 0, cancellation: Cancellation)).Rows.Select(n => n.Id));
        string objectId = (await lease.ReadObjectsAsync(0, cancellation: Cancellation)).Rows[0].Id;
        Assert.Equal(new ulong[] { ulong.MaxValue - 1, 1 }, (await lease.ReadAliasesAsync(objectId, 0, cancellation: Cancellation)).Rows.Select(n => n.Id));
        Assert.Equal(400UL, Assert.Single((await lease.ReadDirectoryFactsAsync(400, 0, cancellation: Cancellation)).Rows).DirectoryId);
        Assert.Equal(1UL, Assert.Single((await lease.ReadPathIssuesAsync(1, 0, cancellation: Cancellation)).Rows).NodeId);
        await using (var logical = await lease.BuildLogicalHierarchyAsync(Cancellation))
        { Assert.Equal((UInt128)220, (await logical.ReadAsync(ulong.MaxValue, Cancellation)).Totals.KnownBytes); Assert.Equal(2UL, (await logical.ReadChildrenAsync(400, AnalysisLogicalSort.FileSize, cancellation: Cancellation)).Total); }
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
        {
            var expected = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation).Totals;
            var actual = (await lease.SummarizeAsync(metric, Cancellation)).Totals; Assert.Equal(expected.Known, actual.Known); Assert.Equal(expected.MatchingPaths, actual.MatchingPaths);
            await using (var hierarchy = await lease.BuildHierarchyAsync(metric, Cancellation))
            {
                Assert.Equal(expected.Known, hierarchy.Totals.Known); Assert.Equal(ulong.MaxValue, (await hierarchy.ReadAsync(ulong.MaxValue, Cancellation)).Path.Id);
                foreach (var sort in Enum.GetValues<AnalysisHierarchySort>())
                    foreach (bool descending in new[] { false, true })
                        Assert.Equal(2UL, (await hierarchy.ReadChildrenAsync(400, sort, cancellation: Cancellation, descending: descending)).Total);
            }
            await using var filtered = await lease.BuildHierarchyAsync(metric, new("name:x.bin"), Cancellation);
            Assert.Equal(ulong.MaxValue - 1, Assert.Single((await filtered.ReadMatchingFilesAsync(cancellation: Cancellation)).Rows).Path.Id);
            Assert.False((await filtered.ReadAsync(1, Cancellation)).IsMatch);
            if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
            { Assert.True((await filtered.ReadAsync(1, Cancellation)).IsVisible); Assert.Single(await filtered.ReadBucketsAsync(ulong.MaxValue, Cancellation)); }
        }
        await using (var explorer = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.OnDisk, cancellation: Cancellation))
        {
            Assert.True(await explorer.LoadPageAsync(400, cancellation: Cancellation)); Assert.True(await explorer.InspectAsync(1, cancellation: Cancellation));
            Assert.Equal(1UL, explorer.Inspection!.Row.Path.Id); Assert.True(await explorer.LoadChartAsync(ulong.MaxValue, Cancellation));
        }
        await lease.DisposeAsync(); Assert.True(scratch.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("forward-parent")]
    [InlineData("missing-object")]
    [InlineData("parent-file")]
    [InlineData("missing-fact-owner")]
    [InlineData("missing-issue-node")]
    [InlineData("coverage-count")]
    [InlineData("measurement-count")]
    [InlineData("aggregate-overflow")]
    public async Task ValidChecksumDoesNotAuthorizePoisonedGraphOrCoverage(string fault)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        var lines = Encoding.UTF8.GetString(Encode(OpaqueIds())).TrimEnd('\n').Split('\n');
        var json = lines.Select(line => JsonNode.Parse(line)!).ToArray();
        var paths = json.Where(j => (string?)j["kind"] == "path-node").ToArray();
        var fact = json.First(j => (string?)j["kind"] == "storage-fact"); var coverage = json.First(j => (string?)j["kind"] == "coverage");
        switch (fault)
        {
            case "duplicate-id": paths[5]["id"] = "1"; break;
            case "forward-parent": paths[1]["parent"] = "1"; break;
            case "missing-object": paths[3]["object"] = "99"; break;
            case "parent-file": paths[5]["parent"] = "1"; break;
            case "missing-fact-owner": fact["object"] = "99"; break;
            case "missing-issue-node": json.First(j => (string?)j["kind"] == "issue")["node"] = "900"; break;
            case "coverage-count": coverage["identityVerified"] = "99"; break;
            case "measurement-count": coverage["measurements"]![0]![1] = "99"; break;
            case "aggregate-overflow": json.First(j => (string?)j["kind"] == "object")["length"]![1] = UInt128.MaxValue.ToString(); break;
        }
        byte[] bytes = AnalysisInventoryStageCases.WithTrailer(json.Select(j => j.ToJsonString()).ToArray());
        var input = new MemoryDirectory { Input = bytes };
        await using var stage = await AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, Cancellation);
        var pending = stage.BuildDatasetAsync(Cancellation);
        try { await Assert.ThrowsAsync<InvalidDataException>(() => pending); }
        finally { if (pending.IsCompletedSuccessfully) await (await pending).DisposeAsync(); }
        await stage.DisposeAsync(); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes); Assert.Equal(bytes, input.Input);
    }

    [Theory]
    [InlineData("valid-complete")]
    [InlineData("valid-incomplete")]
    [InlineData("wrong-eligible")]
    [InlineData("unknown-counts")]
    [InlineData("missing-stale-node")]
    [InlineData("false-stale-node")]
    [InlineData("false-freshness")]
    public async Task QualifiedCoveragePreservesOriginalStaleIdsAndRefusesContradictoryClaims(string fault)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        var s = OpaqueIds(); s = s with { Freshness = AnalysisFreshness.PossiblyStale,
            Facts = s.Facts.SetItem(0, s.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Changed) }) };
        var coverage = AnalysisCoverage.Create(s) with { DenominatorFinal = false, StaleScopeNodes = [1, ulong.MaxValue - 1], StaleRootIds = ["r"], Qualification = new(false, 3, true, false) };
        s = s with { CoverageDetails = coverage };
        var lines = Encoding.UTF8.GetString(Encode(s)).TrimEnd('\n').Split('\n'); var json = lines.Select(line => JsonNode.Parse(line)!).ToArray();
        var declared = json.First(j => (string?)j["kind"] == "coverage");
        switch (fault)
        {
            case "valid-incomplete": declared["qualification"]![2] = false; declared["staleNodes"] = new JsonArray("1"); break;
            case "wrong-eligible": declared["qualification"]![1] = "4"; break;
            case "unknown-counts": declared["requested"] = "1"; break;
            case "missing-stale-node": declared["staleNodes"] = new JsonArray("1"); break;
            case "false-stale-node": declared["staleNodes"] = new JsonArray("1", "400", (ulong.MaxValue - 1).ToString()); break;
            case "false-freshness": json[0]["freshness"] = AnalysisFreshness.Observed.ToString(); break;
        }
        var input = new MemoryDirectory { Input = AnalysisInventoryStageCases.WithTrailer(json.Select(j => j.ToJsonString()).ToArray()) };
        await using var stage = await AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => input, Cancellation);
        var pending = stage.BuildDatasetAsync(Cancellation);
        try
        {
            if (fault.StartsWith("valid", StringComparison.Ordinal))
            {
                await using var dataset = await pending; var detail = dataset.Descriptor.CoverageDetails;
                Assert.Equal(AnalysisFreshness.PossiblyStale, dataset.Descriptor.Freshness); Assert.False(detail.EnumerationKnown);
                Assert.Equal(fault == "valid-complete", detail.StaleScopesComplete); Assert.Equal(1UL, detail.Observations.StaleScopeNodes[0]);
                if (detail.StaleScopesComplete) Assert.Equal(ulong.MaxValue - 1, detail.Observations.StaleScopeNodes[1]);
            }
            else await Assert.ThrowsAsync<InvalidDataException>(() => pending);
        }
        finally { if (pending.IsCompletedSuccessfully) await (await pending).DisposeAsync(); }
        await stage.DisposeAsync(); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingStageDuringActualGraphConstructionRetainsItsFilesUntilTheWorkReturns(bool cancel)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var scratch = new MemoryDirectory(); using var workspace = new AnalysisScratchWorkspace(scratch);
        await using var stage = await AnalysisInventoryFormat.StagePrivateAsync(workspace, io, "stage-output", "stage-input", "source.fcdsa", () => new MemoryDirectory { Input = Encode(OpaqueIds()) }, Cancellation);
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int seen = 0;
        scratch.OnScratchRead = () => { if (Interlocked.Exchange(ref seen, 1) == 0) { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); } };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        var build = stage.BuildDatasetAsync(stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); var closing = stage.DisposeAsync(); workspace.Dispose(); if (cancel) stop.Cancel();
            Assert.False(build.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(scratch.Closed);
            release.Set();
            if (cancel) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build); await closing; }
            else
            {
                await using var dataset = await build; await closing;
                await using var lease = await dataset.AcquireAsync(Cancellation); Assert.Equal(6UL, (await lease.ReadPathsAsync(0, cancellation: Cancellation)).Total);
            }
        }
        finally { release.Set(); if (build.IsCompletedSuccessfully) await (await build).DisposeAsync(); }
        Assert.True(scratch.Closed); Assert.Equal(0, scratch.LiveFiles); Assert.Equal(0, workspace.ReservedBytes);
    }
}
