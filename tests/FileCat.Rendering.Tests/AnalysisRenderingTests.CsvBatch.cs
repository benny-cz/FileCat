using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)] [InlineData(AnalysisMetric.UniqueFileSize)] [InlineData(AnalysisMetric.OnDisk)] [InlineData(AnalysisMetric.Files)]
    public async Task NativeCsvBatchRowsKeepRawNamespaceOrderAndEveryCanonicalField(AnalysisMetric metric)
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true, extraExtensions: 4);
        var token = TestContext.Current.CancellationToken; var lease = source.Lease!;
        await using var hierarchy = await lease.BuildHierarchyAsync(metric, token);
        ulong offset = 0; var ids = new List<ulong>();
        while (offset < lease.Descriptor.Paths)
        {
            var batch = await hierarchy.ReadNamespaceRowsAsync(offset, maximum: 2, cancellation: token);
            Assert.NotEmpty(batch.Rows); Assert.InRange(batch.Rows.Length, 1, 2); Assert.Equal(lease.Descriptor.Paths, batch.Total);
            var paths = await lease.ReadPathsAsync(offset, 2, token);
            Assert.Equal(paths.Rows.Take(batch.Rows.Length).Select(row => row.Id), batch.Rows.Select(row => row.Path.Id));
            foreach (var row in batch.Rows)
            {
                var single = await hierarchy.ReadAsync(row.Path.Id, token);
                Assert.Equal(JsonSerializer.Serialize(single), JsonSerializer.Serialize(row)); ids.Add(row.Path.Id);
            }
            Assert.True(batch.NextOffset > offset); offset = batch.NextOffset;
        }
        Assert.Equal((int)lease.Descriptor.Paths, ids.Distinct().Count());
        await source.ReleaseInitialLeaseAsync();
        Assert.NotEmpty((await hierarchy.ReadNamespaceRowsAsync(0, cancellation: token)).Rows);
        Assert.Empty((await hierarchy.ReadNamespaceRowsAsync(lease.Descriptor.Paths, cancellation: token)).Rows);
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    [AvaloniaFact]
    public async Task NativeCsvLineageReusesCapturedDirectoryPrefixesOfflineWithoutChangingDisplayedLocations()
    {
        OrdinaryPreparation(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true, extraExtensions: 4);
        var token = TestContext.Current.CancellationToken; var lease = source.Lease!; var lineage = new AnalysisReportLineage(lease);
        string live = Path.Combine(source.Root, "source"), offline = Path.Combine(source.Root, "source-offline"); Directory.Move(live, offline);
        try
        {
            ulong offset = 0; int referenceLookups = 0;
            while (offset < lease.Descriptor.Paths)
            {
                var page = await lease.ReadPathsAsync(offset, cancellation: token);
                foreach (var path in page.Rows)
                {
                    var names = new List<string>(); var current = path;
                    while (current.ParentId != 0)
                    { names.Add(AnalysisNames.DisplayName(current)); current = await lease.ReadPathAsync(current.ParentId, token); referenceLookups++; }
                    names.Reverse(); var root = lease.Descriptor.Roots.Single(root => root.Id == path.RootId);
                    Assert.Equal(AnalysisReportLocations.Append(root, names), await lineage.ResolveAsync(path, token));
                    Assert.InRange(lineage.CachedBytes, 0, AnalysisReportLineage.BudgetBytes); Assert.InRange(lineage.CachedEntries, 0, AnalysisReportLineage.MaximumEntries);
                }
                offset = page.NextOffset;
            }
            Assert.True(referenceLookups > 0); Assert.True(lineage.MetadataReads < referenceLookups);
            var first = (await lease.ReadPathsAsync(0, 1, token)).Rows.Single();
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lineage.ResolveAsync(first, canceled.Token));
            Assert.False(Directory.Exists(live)); lineage.Clear(); Assert.Equal(0, lineage.CachedBytes); Assert.Equal(0, lineage.CachedEntries);
        }
        finally { lineage.Clear(); Directory.Move(offline, live); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }
}
