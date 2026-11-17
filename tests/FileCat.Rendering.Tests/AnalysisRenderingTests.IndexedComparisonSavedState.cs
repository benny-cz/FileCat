using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeIndexedComparisonSavedStateKeepsExactPagedAliasAndInvalidatesRemovedCopy(bool removed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(extraAliases: 70); var token = TestContext.Current.CancellationToken;
        var window = new DiskSpaceWindow(); string name = "paged-alias.fcdsc"; ulong focused = 0;
        await window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.UniqueFileSize, cancellation: token, services: profile.Services);
        window.Show(); Frame(window);
        try
        {
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!; object? cohort = null;
            for (int index = 0; index < 12; index++)
            {
                cohort = rows.Items.Cast<object>().SingleOrDefault(item => item.ToString()!.Contains("observed paths: 71 → 71", StringComparison.Ordinal));
                if (cohort is not null) break;
                var first = rows.Items[0]; Click(window, "Next comparison page"); await UntilComparisonPersistence(() => rows.IsEnabled && !Equals(first, rows.Items[0]));
            }
            Assert.NotNull(cohort); rows.SelectedItem = cohort;
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<ListBox>(window, "Previous observed comparison paths")?.ItemCount == 32);
            var aliases = ComparisonControl<ListBox>(window, "Previous observed comparison paths")!;
            for (int index = 0; index < 2; index++)
            {
                ulong first = Assert.IsType<AnalysisPathMetadata>(aliases.Items[0]).Id;
                ComparisonControl<Button>(window, "Previous next observed paths")!.RaiseEvent(new(Button.ClickEvent));
                await UntilComparisonPersistence(() => aliases.IsEnabled && Assert.IsType<AnalysisPathMetadata>(aliases.Items[0]).Id != first);
            }
            focused = Assert.IsType<AnalysisPathMetadata>(aliases.Items[3]).Id; aliases.SelectedIndex = 3;
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<TextBox>(window, "Previous comparison details")!.Text!.Contains("\nPath " + focused + "\n", StringComparison.Ordinal));
            Click(window, "Save named comparison…"); await UntilComparisonPersistence(() => ComparisonControl<TextBox>(window, "Comparison filename") is not null);
            ComparisonControl<TextBox>(window, "Comparison filename")!.Text = name; Click(window, "Save new comparison");
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<TextBlock>(window, "Comparison status")!.Text!.StartsWith("Saved ", StringComparison.Ordinal));
            Click(window, "Close analysis"); await UntilComparisonPersistence(() => window.SessionCount == 0, () => ComparisonText(window));
            await window.WaitForIndexedCloseAsync();
            await window.AddSavedIndexedComparisonSessionAsync(profile.Services, name, token); Frame(window);
            aliases = ComparisonControl<ListBox>(window, "Previous observed comparison paths")!;
            Assert.Equal(focused, Assert.IsType<AnalysisPathMetadata>(aliases.SelectedItem).Id);
            Assert.Equal(focused, Assert.IsType<AnalysisPathMetadata>(aliases.Items[0]).Id);
            Assert.Contains("Selected recorded path:", ComparisonControl<TextBox>(window, "Previous comparison details")!.Text!);
            if (removed)
            {
                // The invalidation signal needs only the saved receipt identity.
                // Loading two additional complete graphs here needlessly consumes
                // the process's strict native-handle budget during the UI check.
                Guid id;
                using (var reader = File.OpenText(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name)))
                using (var header = System.Text.Json.JsonDocument.Parse(reader.ReadLine()!))
                    id = Guid.Parse(header.RootElement.GetProperty("comparison").GetString()!);
                await profile.Services.AnalysisSavedReferences.RemovedAsync(id, name, true);
                Click(window, "Close analysis");
                await UntilComparisonPersistence(() => ComparisonText(window).Contains("Discard these temporary comparison observations", StringComparison.Ordinal));
                Click(window, "Keep open"); Assert.Equal(1, window.SessionCount);
            }
            else { Click(window, "Close analysis"); await UntilComparisonPersistence(() => window.SessionCount == 0, () => ComparisonText(window)); }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeIndexedComparisonUnpublishedRestoreClosesBothImportsAndKeepsCapacity(bool cancelAfterLoad)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken; string name = "unpublished.fcdsc";
        var view = new AnalysisComparisonView { Chart = cancelAfterLoad ? AnalysisComparisonChartMode.Table : AnalysisComparisonChartMode.Treemap, ChartScope = "n:18446744073709551615" };
        await using (var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.FileSize, cancellation: token))
        await using (var captured = await result.CaptureBundleAsync(view, cancellation: token))
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveComparisonAsync(captured, name, token);
        byte[] original = File.ReadAllBytes(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name));
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var opening = window.AddPreparedIndexedComparisonSessionAsync(profile.Services, async cancellation =>
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, cancellation);
                var loaded = await prepared.LoadSavedComparisonAsync(name, cancellation);
                if (cancelAfterLoad) stop.Cancel(); return loaded;
            }, stop.Token);
            if (cancelAfterLoad) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
            else await Assert.ThrowsAnyAsync<ArgumentException>(() => opening);
            Assert.Equal(0, window.SessionCount); Assert.True(pool.HasCapacity); Assert.False(pool.HasFailedClose);
            Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name)));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
