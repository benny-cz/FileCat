using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static TheoryData<AnalysisGroupingDimension, AnalysisMetric, string> SelectionParityCases
    {
        get
        {
            var cases = new TheoryData<AnalysisGroupingDimension, AnalysisMetric, string>();
            foreach (var grouping in Enum.GetValues<AnalysisGroupingDimension>())
            foreach (var metric in Enum.GetValues<AnalysisMetric>())
            foreach (string filter in new[] { "", "name:x.cs", "root:r" }) cases.Add(grouping, metric, filter);
            return cases;
        }
    }
    [Theory]
    [MemberData(nameof(SelectionParityCases))]
    public async Task SelectionParityMemoryAndIndexedCountsAndMatchingCapturesShareExactScope(AnalysisGroupingDimension dimension, AnalysisMetric metric, string query)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        snapshot = snapshot with { Nodes = snapshot.Nodes.Add(new(7, "r", 2, "empty", AnalysisKind.Directory))
            .Add(new(8, "r", 2, "link", AnalysisKind.Link)).Add(new(9, "r", 2, "pipe", AnalysisKind.Special))
            .Add(new AnalysisNode(10, "r", 2, "excluded.cs", AnalysisKind.File, "excluded") { IncludedByPolicy = false, MetadataState = MeasurementState.Excluded }),
            Objects = snapshot.Objects.Add(new("excluded", null, AnalysisMeasurement.Missing(MeasurementState.Excluded), AnalysisMeasurement.Missing(MeasurementState.Excluded))) };
        var roots = new AnalysisRootScopeIndex(snapshot, Cancellation); var filter = AnalysisFilter.Parse(query, TimeZoneInfo.Utc, roots: snapshot.Roots, rootScopes: roots);
        bool Matches(AnalysisNode node, AnalysisObject? obj) => filter.Evaluate(new(node, obj, node.Name, AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True;
        Func<AnalysisNode, bool>? directories = query.Length != 0 && filter.IsNamespaceOnly ? node => Matches(node, null) : null;
        var projection = AnalysisAggregate.Project(snapshot, metric, query.Length == 0 ? null : Matches, Cancellation, directories) with { FilterText = query };
        var tree = dimension == AnalysisGroupingDimension.FolderHierarchy ? AnalysisTree.Build(snapshot, projection, query.Length != 0, Cancellation, directories) :
            AnalysisTree.BuildGrouped(snapshot, projection, dimension, query.Length != 0, Cancellation, directories);
        ImmutableHashSet<ulong> marks = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 999];
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        if (dimension == AnalysisGroupingDimension.FolderHierarchy)
        {
            await using var hierarchy = await lease.BuildHierarchyAsync(metric, new(query), Cancellation);
            foreach (ulong folder in new ulong[] { 0, 2, 7 })
            {
                if (folder != 0 && tree.Items[AnalysisTree.Key(folder)].Visible != true) continue;
                string scope = folder == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(folder);
                Assert.Equal(await hierarchy.ClassifyMarksAsync(marks, folder, Cancellation), tree.ClassifyMarks(marks, scope, Cancellation));
                Assert.Equal((await hierarchy.CaptureMatchingFilesAsync(folder, cancellation: Cancellation)).NodeIds.Order(), tree.CaptureMatchingFiles(scope, Cancellation).NodeIds);
            }
        }
        else
        {
            await using var groups = await lease.BuildGroupingAsync(metric, dimension, new(query), Cancellation);
            foreach (string scope in tree.Groups.Keys.Prepend(AnalysisTree.RootKey))
            {
                string? key = scope == AnalysisTree.RootKey ? null : scope;
                Assert.Equal(await groups.ClassifyMarksAsync(marks, key, Cancellation), tree.ClassifyMarks(marks, scope, Cancellation));
                Assert.Equal((await groups.CaptureMatchingFilesAsync(key, cancellation: Cancellation)).NodeIds, tree.CaptureMatchingFiles(scope, Cancellation).NodeIds);
            }
        }
        var capturedMatches = tree.CaptureMatchingFiles(cancellation: Cancellation);
        Assert.Equal(AnalysisSelectionKind.VisibleMatches, capturedMatches.Kind);
        Assert.All(capturedMatches.NodeIds, id => Assert.True(id is >= 4 and <= 6));
    }

    [Fact]
    public async Task SelectionParityMatchingCaptureRefusesLimitAndCancellationAndOutlivesOriginalOwners()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture(); var directory = new MemoryDirectory();
        var dataset = await Build(snapshot, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, cancellation: Cancellation);
        var groups = await lease.BuildGroupingAsync(AnalysisMetric.OnDisk, AnalysisGroupingDimension.Extension, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.CaptureMatchingFilesAsync(maximumPaths: 1, cancellation: Cancellation));
            await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.CaptureMatchingFilesAsync(2, 1, Cancellation));
            await Assert.ThrowsAsync<InvalidOperationException>(() => groups.CaptureMatchingFilesAsync(maximumPaths: 1, cancellation: Cancellation));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hierarchy.CaptureMatchingFilesAsync(cancellation: canceled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => groups.CaptureMatchingFilesAsync(cancellation: canceled.Token));
            Assert.Equal(new ulong[] { 4, 6 }, (await hierarchy.CaptureMatchingFilesAsync(2, cancellation: Cancellation)).NodeIds.Order());
            Assert.Equal(new ulong[] { 4, 5, 6 }, (await groups.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
            string directoryGroup = Assert.Single(await AllGroups(groups), group => group.Kind == AnalysisGroupKind.DirectoryAllocation).Definition.Key;
            Assert.Empty((await groups.CaptureMatchingFilesAsync(directoryGroup, 1, Cancellation)).NodeIds); Assert.False(directory.Closed);
        }
        finally { await hierarchy.DisposeAsync(); await groups.DisposeAsync(); }
        Assert.True(directory.Closed); Assert.Equal(0, AnalysisExplorerAdmission.Active);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionParityQueuedMatchingCaptureCancellationRetainsActualOwnership(bool grouped)
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1); var directory = new MemoryDirectory();
        var dataset = await Build(AnalysisGroupingTests.Fixture(), io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var groups = grouped ? await lease.BuildGroupingAsync(AnalysisMetric.FileSize, AnalysisGroupingDimension.Extension, cancellation: Cancellation) : null;
        var hierarchy = grouped ? null : await lease.BuildHierarchyAsync(AnalysisMetric.FileSize, cancellation: Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Task? hold = null, close = null; Task<AnalysisSelection>? capture = null;
        try
        {
            hold = io.Run(Device, IoPriority.Background, _ => { entered.Set(); release.Wait(Cancellation); }, Cancellation);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(4), Cancellation));
            capture = groups is not null ? groups.CaptureMatchingFilesAsync(cancellation: stop.Token) : hierarchy!.CaptureMatchingFilesAsync(cancellation: stop.Token);
            Assert.Equal(1, AnalysisExplorerAdmission.Active); stop.Cancel(); close = groups is not null ? groups.DisposeAsync().AsTask() : hierarchy!.DisposeAsync().AsTask();
            Assert.False(close.IsCompleted); Assert.False(directory.Closed);
            release.Set(); await hold; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture); await close;
            Assert.True(directory.Closed); Assert.Equal(0, AnalysisExplorerAdmission.Active);
        }
        finally
        {
            release.Set(); if (hold is not null) await hold;
            if (capture is not null) try { await capture; } catch (OperationCanceledException) { }
            if (groups is not null) await groups.DisposeAsync(); if (hierarchy is not null) await hierarchy.DisposeAsync();
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectionParityRetainedReferencesCannotBeRetaggedAcrossSessionOrEpoch(bool changeSession, bool basket)
    {
        var snapshot = AnalysisGroupingTests.Fixture(); var state = new AnalysisSelectionState(); state.BindIdentity(snapshot.Identity);
        if (basket) state.StageCaptured([4]); else state.MarkCaptured([4]);
        var same = snapshot with { Identity = snapshot.Identity with { Revision = snapshot.Identity.Revision + 1 } };
        Assert.Equal(same.Identity.Revision, state.Capture(same).Revision);
        var changed = snapshot with { Identity = snapshot.Identity with { SessionId = changeSession ? Guid.NewGuid() : snapshot.Identity.SessionId,
            Epoch = changeSession ? snapshot.Identity.Epoch : snapshot.Identity.Epoch + 1 } };
        Assert.Throws<InvalidOperationException>(() => state.Capture(changed));
        Assert.Equal(new ulong[] { 4 }, (basket ? state.Basket : state.Marks).Order());
        state.ClearMarks(); state.ClearBasket(); state.BindIdentity(changed.Identity); state.MarkCaptured([6]);
        Assert.Equal(changed.Identity.SessionId, state.Capture(changed).SessionId); Assert.Equal(new ulong[] { 6 }, state.Capture(changed).NodeIds);
    }
    [Fact]
    public void SelectionParityOtherScopesCountOnlyTheirExplicitDisplayedSubset()
    {
        var source = AnalysisGroupingTests.Fixture(); var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, AnalysisMetric.FileSize, cancellation: Cancellation), false, Cancellation);
        var children = tree.ChildrenOf("n:2"); Assert.Equal(2, children.Length);
        var scope = AnalysisTree.OtherKey("n:2", 1); ulong id = children[1].NodeId!.Value;
        var counts = tree.ClassifyMarks([4, 5, 6, 999], scope, Cancellation);
        Assert.Equal(1, counts.InScope); Assert.Equal(2, counts.Elsewhere); Assert.Equal(1, counts.Missing);
        Assert.Equal(new[] { id }, tree.CaptureMatchingFiles(scope, Cancellation).NodeIds);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => tree.ClassifyMarks([], cancellation: canceled.Token));
    }
    [Theory]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    public async Task SelectionParityNonmatchingAliasContextDoesNotBecomeAMatchWhenSharedMeasurementsAreMissing(AnalysisMetric metric, bool missing)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisGroupingTests.Fixture();
        if (missing) source = metric == AnalysisMetric.OnDisk ? source with { Facts = source.Facts.SetItem(0, source.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Failed) }) } :
            source with { Objects = source.Objects.SetItem(0, source.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Failed) }) };
        var projection = AnalysisAggregate.Project(source, metric, (node, _) => node.Id == 4, Cancellation);
        var tree = AnalysisTree.Build(source, projection, true, Cancellation);
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(metric, new("name:x.cs"), Cancellation);
        Assert.Equal(await hierarchy.ClassifyMarksAsync([4, 5, 6], cancellation: Cancellation), tree.ClassifyMarks([4, 5, 6], cancellation: Cancellation));
        var context = tree.Items["n:5"]; Assert.True(context.Visible); Assert.True(context.IsFilterContext); Assert.Equal(0UL, context.Files); Assert.Equal((UInt128)0, context.LogicalSize);
        Assert.True(AnalysisIndexedChart.Row(await hierarchy.ReadAsync(5, Cancellation), metric).IsFilterContext);
        Assert.Equal(new ulong[] { 4 }, tree.CaptureMatchingFiles(cancellation: Cancellation).NodeIds);
        Assert.Equal(new ulong[] { 4 }, (await hierarchy.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
    }
}
