using System.Diagnostics;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Rendering.Tests;

internal enum NativeScaleRequirement { CompleteIndex, ScratchBudget, IndexOrScratchBudget }

// Explicit guest-only resource qualification. Ordinary rendering runs do not create a large tree.
public sealed class AnalysisAcquisitionScaleTests
{
    [AvaloniaFact]
    public Task NativeAcquisitionAndIndexedExploration() => AnalysisRenderingTests.RunNativeAcquisitionScaleAsync();

    [AvaloniaFact]
    public Task NativeAcquisitionAtScratchBudgetKeepsBoundedPreview() =>
        AnalysisRenderingTests.RunNativeAcquisitionScaleAsync(NativeScaleRequirement.ScratchBudget);

    [AvaloniaFact]
    public Task NativeAcquisitionAtScalePublishesIndexOrScratchBudgetPreview() =>
        AnalysisRenderingTests.RunNativeAcquisitionScaleAsync(NativeScaleRequirement.IndexOrScratchBudget);
}

public sealed partial class AnalysisRenderingTests
{
    internal static async Task RunNativeAcquisitionScaleAsync(NativeScaleRequirement requirement = NativeScaleRequirement.CompleteIndex)
    {
        string? input = Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_SCALE_COUNT");
        Assert.SkipWhen(input is null, "Opt-in native filesystem resource suite; run only on an idle test guest.");
        if (!int.TryParse(input, out int count) || count is < 1_000 or > 10_000_000)
            throw new InvalidOperationException("Choose an explicit native file count from 1,000 to 10,000,000.");
        OrdinaryPreparation();
        string shape = Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_SCALE_SHAPE") ?? "wide";
        if (shape is not ("wide" or "deep" or "wide-linked")) throw new InvalidOperationException("Unknown native fixture shape.");
        string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_SCALE_ROOT") ??
            throw new InvalidOperationException("An explicit disposable guest fixture directory is required."));
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        DriveInfo? fixtureDrive = OperatingSystem.IsWindows() && shape == "wide-linked" ? new(Path.GetPathRoot(parent)!) : null;
        long? availableBeforeFixture = fixtureDrive?.AvailableFreeSpace, availableAfterFixture = null;
        long fixtureSpaceFloor = fixtureDrive is null ? 0 : AnalysisLimits.ScratchBytes +
            checked((long)AnalysisPrivateInventoryStore.RequiredReserve((ulong)fixtureDrive.TotalSize));
        string root = Path.Combine(parent, "native-" + Guid.NewGuid().ToString("N"));
        string receipt = Path.Combine(parent, "receipt.json");
        if (File.Exists(receipt)) throw new IOException("A fresh scale receipt destination is required.");
        Directory.CreateDirectory(root);
        var phases = new List<object>(); var timer = Stopwatch.StartNew();
        string phase = "fixture"; string? failure = null; bool completed = false, cleanup = false;
        bool logicalBaselineMeasured = false;
        long baselinePrivate = 0, peakPrivate = 0, baselineWorking = 0, peakWorking = 0;
        long peakScratchReserved = 0;
        AnalysisProgress? acquisitionProgress = null;
        object? acquisitionOutcome = null;
        using var process = Process.GetCurrentProcess(); using var samplingStop = new CancellationTokenSource();
        Task sampling = Task.CompletedTask; AnalysisPhysicalMemory? physical = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int minutes = count < 5_000_000 ? 60 : fixtureDrive is null ? 180 : 360;
        timeout.CancelAfter(TimeSpan.FromMinutes(minutes)); var token = timeout.Token;
        string cancelPath = Path.Combine(parent, "cancel.txt");
        var cancellationWatch = Task.Run(async () =>
        {
            try
            {
                using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
                while (await ticks.WaitForNextTickAsync(samplingStop.Token))
                {
                    if (!File.Exists(cancelPath)) continue;
                    timeout.Cancel();
                    break;
                }
            }
            catch (OperationCanceledException) when (samplingStop.IsCancellationRequested) { }
        });
        void CheckCancellation()
        {
            if (File.Exists(cancelPath)) timeout.Cancel();
            token.ThrowIfCancellationRequested();
        }
        int fixtureChecks = 0;
        void CheckFixtureProgress()
        {
            CheckCancellation();
            if (fixtureDrive is not null && fixtureChecks++ % 128 == 0 && fixtureDrive.AvailableFreeSpace <= fixtureSpaceFloor)
                throw new IOException("Fixture creation would leave less than the scratch budget and free-space reserve.");
        }
        AnalysisIndexedAcquisition? acquisition = null; AnalysisDatasetLease? lease = null;
        IAnalysisBackend? source = null; AppServices? services = null; DiskSpaceWindow? window = null;
        long firstObservationTicks = 0; int progressReports = 0; ulong sourceCloseFailures = 0, paths = 0;
        string selected = Path.Combine(root, "source"); bool reusedSource = false, fixtureComplete = false; string? retainedSource = null;
        void Save() => File.WriteAllText(receipt, JsonSerializer.Serialize(new
        {
            purpose = "Real native filesystem acquisition, retained indexed exploration and actual owner cleanup; headless UI is not physical input qualification",
            count, shape, phase, completed, cleanup, phases, paths, sourceCloseFailures, progressReports, reusedSource, retainedSource,
            hardLinkCohortSize = shape == "wide-linked" ? AnalysisNativeScaleFixture.HardLinkCohortSize : (int?)null,
            availableBeforeFixture, availableAfterFixture,
            firstObservationMilliseconds = firstObservationTicks == 0 ? (double?)null : TimeSpan.FromTicks(firstObservationTicks).TotalMilliseconds,
            sampledIntervalMilliseconds = 50,
            baselinePrivateBytes = baselinePrivate > 0 ? (long?)baselinePrivate : null,
            sampledPeakPrivateIncrementBytes = baselinePrivate > 0 ? (long?)Math.Max(0, peakPrivate - baselinePrivate) : null,
            privateMemoryBudgetVerified = baselinePrivate > 0 && peakPrivate - baselinePrivate <= 512L * 1024 * 1024,
            sampledPeakWorkingSetIncrementBytes = Math.Max(0, peakWorking - baselineWorking),
            nativePhysicalMemory = physical?.Describe(), sampledPeakScratchReservedBytes = peakScratchReserved,
            remainingSharedQuotaBytes = AnalysisScratchBudget.Shared.ReservedBytes, acquisitionOutcome,
            logicalBaselineMeasured, failure,
        }, new JsonSerializerOptions { WriteIndented = true }));
        void Mark(string name)
        {
            phases.Add(new { phase, seconds = timer.Elapsed.TotalSeconds }); phase = name; timer.Restart(); Save();
        }
        try
        {
            CheckCancellation();
            string? reuse = AnalysisNativeScaleFixture.ReusableSource(count, shape, CheckFixtureProgress, CreateGroupedNativeAlias);
            reusedSource = reuse is not null; selected = reuse ?? selected;
            if (!reusedSource) Directory.CreateDirectory(selected);
            string bottom = selected; int directories = shape == "deep" ? 32 : 0;
            for (int i = 0; i < directories; i++) { bottom = Path.Combine(bottom, "d"); if (!reusedSource) Directory.CreateDirectory(bottom); }
            string anchor = Path.Combine(selected, "anchor.bin");
            if (!reusedSource)
            {
                File.WriteAllBytes(anchor, [1, 2, 3, 4, 5, 6, 7]);
                CreateGroupedNativeAlias(anchor, Path.Combine(bottom, "anchor-alias.txt"));
            }
            ulong expectedBytes = 14, expectedUniqueBytes = 7;
            bool checkpoint = !reusedSource && AnalysisNativeScaleFixture.KeepSource(count);
            for (int at = 2; at < count; at++)
            {
                if (at % 128 == 0) CheckFixtureProgress();
                if (checkpoint && at % AnalysisNativeScaleFixture.CheckpointInterval == 0)
                    AnalysisNativeScaleFixture.WriteProgress(root, selected, count, shape, at);
                token.ThrowIfCancellationRequested();
                int owner = AnalysisNativeScaleFixture.RowOwner(at, shape);
                int length = owner % 17;
                if (!reusedSource) AnalysisNativeScaleFixture.WriteRow(bottom, at, owner, CreateGroupedNativeAlias);
                expectedBytes += (ulong)length;
                if (at == owner) expectedUniqueBytes += (ulong)length;
            }
            availableAfterFixture = fixtureDrive?.AvailableFreeSpace;
            fixtureComplete = true;
            if (Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_SCALE_SKIP_LOGICAL_BASELINE") == "1")
                Assert.True(requirement == NativeScaleRequirement.ScratchBudget && reusedSource,
                    "Omit the separate logical benchmark only when retesting scratch-budget handling on a retained fixture.");
            else
            {
                Mark("logical-baseline");
                var logical = await Task.Run(() => DirectorySizer.Compute(selected, null, token), token);
                Assert.True(logical.Complete); Assert.Equal(0, logical.Inaccessible);
                Assert.Equal(count, logical.Files); Assert.Equal(directories, logical.Directories); Assert.Equal(expectedBytes, (ulong)logical.Bytes);
                logicalBaselineMeasured = true;
            }
            Mark("native-acquisition");
            using var styles = new ProductStyles();
            services = AppServices.CreateForPaths(AppPaths.Resolve(dataRoot: Path.Combine(root, "profile")));
            GC.Collect(); process.Refresh(); baselinePrivate = peakPrivate = process.PrivateMemorySize64;
            baselineWorking = peakWorking = process.WorkingSet64; physical = new AnalysisPhysicalMemory();
            sampling = Task.Run(async () =>
            {
                while (!samplingStop.IsCancellationRequested)
                {
                    process.Refresh(); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                    peakWorking = Math.Max(peakWorking, process.WorkingSet64); physical.Observe();
                    peakScratchReserved = Math.Max(peakScratchReserved, AnalysisScratchBudget.Shared.ReservedBytes);
                    try { await Task.Delay(50, samplingStop.Token); } catch (OperationCanceledException) { break; }
                }
            });
            acquisition = await AnalysisIndexedAcquisition.PrepareAsync(services, token);
            Assert.Null(acquisition.MemoryPreparationNotice);
            string device = PathUtil.GetDeviceKey(selected);
            source = await AnalysisIo.Run(services.Io, device, _ => NativeRoot(selected), token);
            long lastProgressReceipt = 0;
            var session = acquisition.Start([source], new InlineScaleProgress(value =>
            {
                acquisitionProgress = value;
                Interlocked.Increment(ref progressReports);
                if (value.ObservedPaths > 0) Interlocked.CompareExchange(ref firstObservationTicks, timer.Elapsed.Ticks, 0);
                long milliseconds = timer.ElapsedMilliseconds;
                if (milliseconds - lastProgressReceipt >= 1_000 || value.DatasetReady)
                {
                    lastProgressReceipt = milliseconds;
                    File.WriteAllText(Path.Combine(parent, "progress.json"), JsonSerializer.Serialize(new
                    { count, shape, milliseconds, value.ObservedPaths, value.PreviewPaths, value.Phase, value.DatasetReady, value.SourceCloseFailures }));
                }
            })); source = null;
            await session.Completion.WaitAsync(token);
            var snapshot = session.Snapshot(); sourceCloseFailures = session.SourceCloseFailures;
            acquisitionOutcome = new
            {
                phase = snapshot.Phase.ToString(), coverage = snapshot.Coverage.ToString(),
                observedPaths = acquisitionProgress?.ObservedPaths, previewPaths = snapshot.Nodes.Length,
                datasetReady = session.DatasetDescriptor is not null, scratchLimit = session.ScratchLimitReached?.ToString(),
                issueCodes = snapshot.Issues.Select(issue => issue.Code).Distinct().Order().ToArray()
            };
            Assert.Equal(AnalysisPhase.Finished, snapshot.Phase); Assert.Equal(0UL, sourceCloseFailures);
            if (requirement == NativeScaleRequirement.ScratchBudget ||
                requirement == NativeScaleRequirement.IndexOrScratchBudget && session.ScratchLimitReached == AnalysisScratchLimit.Budget)
            {
                Mark("verify-retained-preview");
                Assert.Equal(AnalysisScratchLimit.Budget, session.ScratchLimitReached);
                Assert.Equal(NamespaceCoverage.Partial, snapshot.Coverage);
                Assert.Equal(4_096, snapshot.Nodes.Length);
                Assert.NotNull(acquisitionProgress);
                Assert.InRange(acquisitionProgress.ObservedPaths, 4_097, count + directories + 1);
                Assert.Null(session.DatasetDescriptor);
                Assert.Contains(snapshot.Issues, issue => issue.Code == "analysis-scratch-budget-reached");
                Assert.DoesNotContain(snapshot.Issues, issue => issue.Code is "analysis-source-unavailable" or "analysis-index-unavailable");

                var model = DiskSpaceViewModel.AdoptAcquisition(acquisition, services, view: null);
                acquisition = null;
                try
                {
                    window = new DiskSpaceWindow();
                    window.AddSession(model);
                    await model.RebuildAsync(true, token);
                    window.Show();
                    Frame(window);
                    Assert.Contains("private scratch budget was reached", model.RuntimePreviewNotice);
                    Assert.Contains("bounded preview remains available", model.RuntimePreviewNotice);
                    Assert.Contains("private scratch budget was reached", model.RecordedResultUnavailableReason);
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text == model.RuntimePreviewNotice);
                    Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button =>
                        button.IsEffectivelyVisible && button.Content is "Open recorded result" && !button.IsEnabled);
                    Assert.NotNull(model.Tree);
                }
                finally
                {
                    window?.ForceClose();
                    if (window is not null) await window.WaitForIndexedCloseAsync();
                    window = null;
                    model.Dispose();
                    await model.CloseCompletion;
                }
                Assert.Equal(0, AnalysisScratchBudget.Shared.ReservedBytes);
                Assert.Empty(Directory.GetFiles(services.Paths.AnalysisScratchDirectory));
                Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, File.ReadAllBytes(anchor));
                completed = true;
                return;
            }
            Assert.NotNull(session.DatasetDescriptor); paths = session.DatasetDescriptor.Paths;
            Assert.Equal((ulong)(count + directories + 1), paths); Assert.True(snapshot.Nodes.Length <= 4_096);
            lease = await session.AcquireDatasetAsync(token); Assert.NotNull(lease);
            // Closing acquisition first proves the retained dataset owns the actual native store.
            await acquisition.DisposeAsync(); acquisition = null;
            Mark("verify-captured-paths");
            ulong offset = 0, seenFiles = 0, seenDirectories = 0, anchorId = 0;
            string? anchorObject = null, cohortObject = null;
            do
            {
                var page = await lease.ReadPathsAsync(offset, maximum: 256, cancellation: token);
                Assert.NotEmpty(page.Rows); Assert.True(page.NextOffset > offset);
                foreach (var row in page.Rows)
                {
                    Assert.True(row.IncludedByPolicy); Assert.Equal(MeasurementState.Known, row.MetadataState);
                    if (row.Kind == AnalysisKind.File) seenFiles++; else if (row.Kind == AnalysisKind.Directory) seenDirectories++;
                    else throw new InvalidDataException("Unexpected native fixture entry kind.");
                    if (row.Name == "anchor.bin") { anchorId = row.Id; anchorObject = row.ObjectId; }
                    if (row.Name == "row-00000002.bin") cohortObject = row.ObjectId;
                }
                offset = page.NextOffset;
            } while (offset < paths);
            Assert.Equal((ulong)count, seenFiles); Assert.Equal((ulong)directories + 1, seenDirectories); Assert.NotNull(anchorObject);
            var aliases = await lease.ReadAliasesAsync(anchorObject, 0, maximum: 128, cancellation: token);
            Assert.Equal(2, aliases.Rows.Length); Assert.Contains(aliases.Rows, row => row.Name == "anchor-alias.txt");
            if (shape == "wide-linked")
            {
                Assert.NotNull(cohortObject);
                var cohort = await lease.ReadAliasesAsync(cohortObject, 0, maximum: AnalysisNativeScaleFixture.HardLinkCohortSize, cancellation: token);
                Assert.Equal(AnalysisNativeScaleFixture.HardLinkCohortSize, cohort.Rows.Length);
                Assert.Contains(cohort.Rows, row => row.Name == "row-00000129.txt");
            }
            Mark("hierarchy-sort-charts");
            window = new DiskSpaceWindow();
            var explorer = await window.AddIndexedSessionAsync(lease, AnalysisMetric.FileSize, cancellation: token);
            window.Show(); Frame(window);
            Assert.Equal((UInt128)expectedBytes, explorer.Totals.Known); Assert.Equal((ulong)count, explorer.LogicalTotals.Files);
            foreach (var sort in new[] { AnalysisHierarchySort.Metric, AnalysisHierarchySort.Name, AnalysisHierarchySort.FileSize })
            foreach (bool descending in new[] { true, false })
            {
                Assert.True(await explorer.LoadPageAsync(1, sort, maximum: 128, cancellation: token, descending: descending));
                Assert.True(explorer.Page!.Children.Rows.Length is > 0 and <= 128);
            }
            Assert.True(await explorer.InspectAsync(anchorId, cancellation: token)); Assert.Equal(2, explorer.Inspection!.Aliases.Rows.Length);
            Assert.True(await explorer.LoadChartAsync(cancellation: token)); Assert.NotEmpty(explorer.Chart!.Items);
            var modes = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "Indexed chart mode");
            for (int mode = 0; mode < 3; mode++) { modes.SelectedIndex = mode; Frame(window); }
            window.ForceClose(); await window.WaitForIndexedCloseAsync(); window = null;
            Mark("filtered-unique");
            await using (var filtered = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.UniqueFileSize, new("name:anchor"), token))
            {
                Assert.Equal(7U, filtered.Totals.Known); Assert.Equal(2UL, filtered.LogicalTotals.Files);
                Assert.True(await filtered.LoadChartAsync(cancellation: token)); Assert.NotEmpty(filtered.Chart!.Items);
            }
            Mark("extension-grouping");
            await using (var groups = await AnalysisDatasetGroupExplorer.CreateAsync(lease, AnalysisMetric.UniqueFileSize, AnalysisGroupingDimension.Extension, cancellation: token))
            {
                Assert.Equal((UInt128)expectedUniqueBytes, groups.Totals.Known); Assert.Equal((ulong)count, groups.LogicalTotals.Files);
                var page = await groups.ReadGroupsAsync(maximum: 128, cancellation: token); Assert.NotEmpty(page.Rows);
                var geometry = await groups.ReadGeometryAsync(maximum: 128, cancellation: token); Assert.NotEmpty(geometry.Rows);
            }
            Mark("actual-close");
            await lease.DisposeAsync(); lease = null;
            Assert.Equal(0, AnalysisScratchBudget.Shared.ReservedBytes);
            Assert.Empty(Directory.GetFiles(services.Paths.AnalysisScratchDirectory));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, File.ReadAllBytes(anchor));
            completed = true;
        }
        catch (Exception error) { failure = error.ToString(); Save(); throw; }
        finally
        {
            try
            {
                if (window is not null) { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
                if (lease is not null) await lease.DisposeAsync();
                if (source is not null && services is not null) await AnalysisIo.ReleaseAsync(services.Io, source);
                if (acquisition is not null) await acquisition.DisposeAsync();
                services?.Dispose();
                retainedSource = reusedSource ? selected : AnalysisNativeScaleFixture.RetainCreatedSource(selected, root, parent, count, shape, fixtureComplete);
                // Only this invocation's generated directory may be removed.
                Assert.Equal(parent, Path.GetDirectoryName(Path.GetFullPath(root)));
                Directory.Delete(root, recursive: true); cleanup = !Directory.Exists(root);
            }
            catch (Exception error)
            {
                failure = failure is null ? error.ToString() : failure + Environment.NewLine + "Cleanup: " + error;
                throw;
            }
            finally
            {
                samplingStop.Cancel(); await cancellationWatch; await sampling; physical?.Observe();
                phases.Add(new { phase, seconds = timer.Elapsed.TotalSeconds }); phase = "closed"; Save();
                if (completed)
                {
                    Assert.True(cleanup);
                    if (baselinePrivate > 0)
                        Assert.True(peakPrivate - baselinePrivate <= 512L * 1024 * 1024, "The sampled process-private increment exceeded 512 MiB.");
                }
            }
        }
        Assert.True(completed);
    }

    private sealed class InlineScaleProgress(Action<AnalysisProgress> report) : IProgress<AnalysisProgress>
    { public void Report(AnalysisProgress value) => report(value); }
}
