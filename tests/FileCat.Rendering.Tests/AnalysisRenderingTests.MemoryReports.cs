using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeMemoryCapturedAndIndexedContextUsesRecordedBasesAndResetsFiltersForWholeResult(bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(indexed);
        var token = TestContext.Current.CancellationToken;
        using var input = indexed ? null : new MemoryStream(ExternalInventoryBytes(source.Memory!));
        using var inventory = input is null ? null : AnalysisInventoryFormat.Read(input, token); using var owner = inventory?.Retain();
        string root = indexed ? source.Lease!.Descriptor.Roots[0].Id : owner!.Snapshot.Roots[0].Id;
        var filter = new AnalysisFilterRequest("name:plain", true, false, root);
        AnalysisTree? tree = owner is null ? null : AnalysisTree.Build(owner.Snapshot,
            AnalysisAggregate.Project(owner.Snapshot, AnalysisMetric.OnDisk, (node, _) => node.Name.Contains("plain", StringComparison.Ordinal), token)
                with { FilterText = filter.Text }, true, token);
        async Task<AnalysisCapturedReport> Capture(AnalysisReportScope scope) => indexed
            ? await source.Lease!.CaptureReportAsync(AnalysisMetric.OnDisk, AnalysisReportFormat.Csv, scope, filter, cancellation: token)
            : await AnalysisCapturedMemoryReport.CaptureAsync(owner!, tree!, profile.Services.Io, AnalysisReportFormat.Csv, scope, cancellation: token, context: filter);
        await using var report = await Capture(AnalysisReportScope.FilteredView);
        Assert.Equal(filter.Text, report.Header.FilterContext!.Query); Assert.True(report.Header.FilterContext.CaseSensitive); Assert.True(report.Header.FilterContext.RootScoped);
        Assert.False(report.Header.MeasurementBases!.DetailLimited); Assert.NotEmpty(report.Header.MeasurementBases.Labels);
        var facts = indexed ? (await source.Lease!.ReadFactsAsync(0, cancellation: token)).Rows : owner!.Snapshot.Facts;
        Assert.Equal(facts.Select(fact => fact.BasisId).Distinct().Order(StringComparer.Ordinal), report.Header.MeasurementBases.Labels);
        await using var whole = await Capture(AnalysisReportScope.EntireResult); Assert.Null(whole.Header.FilterContext);
        Assert.Equal(report.Header.MeasurementBases.Labels, whole.Header.MeasurementBases!.Labels); Assert.False(whole.Header.MeasurementBases.DetailLimited);
        source.AssertPayloads();
    }
    [AvaloniaTheory]
    [InlineData(AnalysisReportScope.FilteredView, false)] [InlineData(AnalysisReportScope.FilteredView, true)]
    [InlineData(AnalysisReportScope.SelectedRows, false)] [InlineData(AnalysisReportScope.SelectedRows, true)]
    [InlineData(AnalysisReportScope.EntireResult, false)] [InlineData(AnalysisReportScope.EntireResult, true)]
    public async Task NativeMemoryCapturedCsvKeepsCanonicalScopeAfterOwnersCloseAndSourceGoesOffline(AnalysisReportScope scope, bool redacted)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var snapshot = owner.Snapshot;
        var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, (node, _) => node.Name.Contains("plain", StringComparison.Ordinal), token);
        var tree = AnalysisTree.Build(snapshot, projection, true, token);
        var marks = snapshot.Nodes.Where(node => node.Name is "plain.bin" or "ordinary.txt").Select(node => node.Id).ToImmutableArray(); Assert.Equal(2, marks.Length);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, AnalysisReportFormat.Csv, scope,
            AnalysisTree.Key(snapshot.Nodes.Single(node => node.ParentId == 0).Id), marks, AnalysisTableSort.Name, false, token);
        Assert.Equal(scope == AnalysisReportScope.EntireResult ? (ulong)snapshot.Nodes.Length : 2UL, report.Header.Rows);
        Assert.Equal(scope == AnalysisReportScope.FilteredView ? 2UL : (ulong)source.Files.Count, report.Header.Logical.Files);
        Assert.Equal((UInt128)report.Header.Logical.Files * 4, report.Header.Logical.KnownBytes);
        owner.Dispose(); inventory.Dispose(); string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        string offline = source.SourcePath + "-offline", prefix = Path.GetFullPath(source.Root) + Path.DirectorySeparatorChar;
        Assert.StartsWith(prefix, Path.GetFullPath(source.SourcePath)); Assert.StartsWith(prefix, Path.GetFullPath(offline)); Directory.Move(source.SourcePath, offline);
        try
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await prepared.ExportReportAsync(report, folder, "memory.csv", redacted, token);
            string text = File.ReadAllText(Path.Combine(folder, "memory.csv")); var rows = CapturedCsv(text); int item = Array.IndexOf(rows[0], "item");
            Assert.Equal((int)report.Header.Rows + 2, rows.Count);
            if (redacted) { Assert.DoesNotContain(source.SourcePath, text); Assert.DoesNotContain("plain", text); Assert.DoesNotContain("ordinary", text); }
            else
            {
                Assert.Contains(source.SourcePath, text);
                if (scope == AnalysisReportScope.FilteredView) Assert.All(rows.Skip(2), row => Assert.Contains("plain", row[item]));
                if (scope == AnalysisReportScope.SelectedRows) Assert.Contains(rows.Skip(2), row => row[item] == "ordinary.txt");
            }
            Assert.False(Directory.Exists(source.SourcePath));
        }
        finally { Directory.Move(offline, source.SourcePath); }
        await report.DisposeAsync(); await report.DisposeAsync(); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(AnalysisReportFormat.Markdown)] [InlineData(AnalysisReportFormat.Html)]
    public async Task NativeMemoryCapturedHumanReportUsesBoundedLabelledRootDetailAndPreservesExistingOutput(AnalysisReportFormat format)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var snapshot = owner.Snapshot; var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: token), false, token);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, format, AnalysisReportScope.EntireResult, cancellation: token);
        Assert.True(report.Header.DetailLimited); Assert.Equal(tree.Projection.Totals.Known, report.Header.Totals.Known);
        string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder); string name = format == AnalysisReportFormat.Markdown ? "memory.md" : "memory.html";
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await prepared.ExportReportAsync(report, folder, name, false, token);
        byte[] original = File.ReadAllBytes(Path.Combine(folder, name));
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => prepared.ExportReportAsync(report, folder, name, true, token));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, name))); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeMemoryCapturedReportCloseWaitsForActualBlockedOutputAndReleasesAllOwners()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var snapshot = owner.Snapshot; var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: token), false, token);
        var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, AnalysisReportFormat.Csv, AnalysisReportScope.EntireResult, cancellation: token);
        owner.Dispose(); inventory.Dispose(); string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = report.ExportNewAsync("canceled.csv", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "report-staging")), "cancel", entered, release), true, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); closing = report.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(exporting.IsCompleted); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing;
            Assert.Empty(Directory.GetFiles(folder)); source.AssertPayloads();
        }
        finally
        {
            release.Set(); try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
            await (closing ?? report.DisposeAsync().AsTask()); await report.DisposeAsync();
        }
    }
}
