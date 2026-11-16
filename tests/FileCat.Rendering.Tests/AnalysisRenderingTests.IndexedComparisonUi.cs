using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task NativeIndexedComparisonCatalogRouteUsesEntireRevisionsAndPagesBothRecordedSides(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "previous.fcdsa", AnalysisSavedView.Default, token);
        string saved = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "previous.fcdsa");
        byte[] payload = File.ReadAllBytes(saved);
        var window = new DiskSpaceWindow();
        await window.AddIndexedSessionAsync(source.Lease!, metric, new("name:does-not-match-any-file"), token, profile.Services);
        await source.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            Click(window, "Compare…");
            await Until(() => ComparisonControl<TextBox>(window, "Saved inventory filename") is not null);
            Click(window, "Browse saved analyses");
            await Until(() => ComparisonControl<ComboBox>(window, "Saved analyses")?.ItemCount == 1);
            ComparisonControl<ComboBox>(window, "Saved analyses")!.SelectedIndex = 0;
            bool structural = metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk;
            if (structural) window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.IsEffectivelyVisible && Equals(control.Content, "Review structural root mapping")).IsChecked = true;
            Click(window, "Compare captured inventories");
            if (structural)
            {
                await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control)?.StartsWith("Previous root for current ", StringComparison.Ordinal) == true));
                window.GetVisualDescendants().OfType<ComboBox>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control)?.StartsWith("Previous root for current ", StringComparison.Ordinal) == true).SelectedIndex = 1;
                Click(window, "Compare mapped observations");
            }
            await Until(() => window.SessionCount == 2 && ComparisonControl<ListBox>(window, "Captured comparison rows")?.ItemCount > 0, () => ComparisonText(window)); Frame(window);
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!;
            Assert.InRange(rows.ItemCount, 1, 32); Assert.Contains("of " + (structural ? "303" : "304"), ComparisonText(window));
            var first = rows.Items[0];
            Click(window, "Next comparison page"); await Until(() => rows.ItemCount > 0 && !Equals(rows.Items[0], first), () => ComparisonText(window));
            Click(window, "Previous comparison page"); await Until(() => rows.ItemCount > 0 && Equals(rows.Items[0], first));
            rows.SelectedIndex = 0;
            await Until(() => ComparisonControl<TextBox>(window, "Previous comparison details")?.Text?.Contains("Recorded location:", StringComparison.Ordinal) == true &&
                ComparisonControl<TextBox>(window, "Current comparison details")?.Text?.Contains("Recorded location:", StringComparison.Ordinal) == true, () => ComparisonText(window));
            Assert.NotEmpty(ComparisonControl<ListBox>(window, "Previous observed comparison paths")!.Items);
            Assert.NotEmpty(ComparisonControl<ListBox>(window, "Current observed comparison paths")!.Items);
            Assert.Contains("File contents were not compared", ComparisonText(window));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), control => control.IsEffectivelyVisible && Equals(control.Content, "Add to cleanup"));
            Click(window, "Close analysis"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Keep open")));
            Click(window, "Keep open"); Assert.Equal(2, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(payload, File.ReadAllBytes(saved)); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaFact]
    public async Task NativeIndexedComparisonMetricChangesKeepInputsAfterOwnerCloseAndReleasePlacesForRetry()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var a = await IndexedFixture.CreateAsync(sharedAlias: true); await using var b = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken; var mapping = new Dictionary<string, string> { [a.Lease!.Descriptor.Roots.Single().Id] = b.Lease!.Descriptor.Roots.Single().Id };
        var window = new DiskSpaceWindow();
        await window.AddIndexedComparisonSessionAsync(a.Lease, b.Lease, AnalysisMetric.FileSize, mapping, "recorded.fcdsa", token);
        await a.ReleaseInitialLeaseAsync(); await b.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            foreach (var metric in new[] { AnalysisMetric.UniqueFileSize, AnalysisMetric.OnDisk, AnalysisMetric.Files, AnalysisMetric.FileSize })
            {
                var selector = ComparisonControl<ComboBox>(window, "Comparison metric")!; selector.SelectedIndex = (int)metric;
                await Until(() => selector.IsEnabled && ComparisonControl<ListBox>(window, "Captured comparison rows")?.ItemCount > 0, () => ComparisonText(window));
                var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!; rows.SelectedIndex = 0;
                await Until(() => ComparisonControl<TextBox>(window, "Current comparison details")?.Text?.Contains("Recorded location:", StringComparison.Ordinal) == true, () => ComparisonText(window));
                Assert.DoesNotContain("unavailable", ComparisonControl<TextBlock>(window, "Comparison status")!.Text!, StringComparison.OrdinalIgnoreCase);
                Assert.Equal((int)metric, selector.SelectedIndex);
                Assert.Contains("Difference:", ComparisonText(window));
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(a.PrivatePath)); Assert.Empty(Directory.EnumerateFileSystemEntries(b.PrivatePath));
        Assert.Equal(a.Payload, File.ReadAllBytes(a.PayloadPath)); Assert.Equal(b.Payload, File.ReadAllBytes(b.PayloadPath));
    }

    [AvaloniaFact]
    public async Task NativeIndexedComparisonCanceledMappingAndAcquisitionPublishNoPartialTab()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "cancel.fcdsa", AnalysisSavedView.Default, token);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(source.Lease!, services: profile.Services, cancellation: token); window.Show(); Frame(window);
        try
        {
            Click(window, "Compare…"); await Until(() => ComparisonControl<TextBox>(window, "Saved inventory filename") is not null);
            ComparisonControl<TextBox>(window, "Saved inventory filename")!.Text = "cancel.fcdsa";
            window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.IsEffectivelyVisible && Equals(control.Content, "Review structural root mapping")).IsChecked = true;
            Click(window, "Compare captured inventories");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control)?.StartsWith("Previous root for current ", StringComparison.Ordinal) == true));
            Click(window, "Cancel"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Compare…") && control.IsEnabled));
            Assert.Equal(1, window.SessionCount);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.Files, cancellation: cancel.Token));
            Assert.Equal(1, window.SessionCount);
            // This deliberately shares one graph with both inputs, the fixture lease and the live
            // explorer. The eight-reader bound refuses it without publishing or leaking a place.
            var limit = await Assert.ThrowsAsync<IOException>(() => window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.Files, cancellation: token));
            Assert.Contains("reader limit", limit.Message); Assert.Equal(1, window.SessionCount);
            // The ordinary catalog route imports an independent Previous graph and must still work.
            Click(window, "Compare…"); await Until(() => ComparisonControl<TextBox>(window, "Saved inventory filename") is not null);
            ComparisonControl<TextBox>(window, "Saved inventory filename")!.Text = "cancel.fcdsa"; Click(window, "Compare captured inventories");
            await Until(() => window.SessionCount == 2, () => ComparisonText(window));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    private static T? ComparisonControl<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name);
    private static string ComparisonText(Window window) => string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Where(control => control.IsEffectivelyVisible).Select(control => control.Text));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeIndexedComparisonIncompatiblePoliciesRequireExplicitLimitedReview(bool accept)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var before = await IndexedFixture.CreateAsync(); await using var current = await IndexedFixture.CreateAsync(components: AnalysisComponentPreset.DetailedAllocation);
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(before.Lease!, "different-policy.fcdsa", AnalysisSavedView.Default, token);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(current.Lease!, AnalysisMetric.OnDisk, cancellation: token, services: profile.Services); window.Show(); Frame(window);
        try
        {
            Click(window, "Compare…"); await Until(() => ComparisonControl<TextBox>(window, "Saved inventory filename") is not null);
            ComparisonControl<TextBox>(window, "Saved inventory filename")!.Text = "different-policy.fcdsa"; Click(window, "Compare captured inventories");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control)?.StartsWith("Previous root for current ", StringComparison.Ordinal) == true));
            window.GetVisualDescendants().OfType<ComboBox>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control)?.StartsWith("Previous root for current ", StringComparison.Ordinal) == true).SelectedIndex = 1;
            Click(window, "Compare mapped observations");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Open limited comparison")), () => ComparisonText(window));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(control => control.IsEffectivelyVisible && Equals(control.Content, "Cancel comparison")).IsDefault);
            Assert.Equal(1, window.SessionCount);
            Click(window, accept ? "Open limited comparison" : "Cancel comparison");
            await Until(() => accept ? window.SessionCount == 2 : window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Compare…") && control.IsEnabled));
            Assert.Equal(accept ? 2 : 1, window.SessionCount);
            if (accept)
            {
                await Until(() => ComparisonControl<ListBox>(window, "Captured comparison rows")?.ItemCount > 0);
                Frame(window);
                Assert.Contains("limited comparison", ComparisonText(window)); Assert.Contains("Observed net change: Unknown", ComparisonText(window));
                var metric = ComparisonControl<ComboBox>(window, "Comparison metric")!; metric.SelectedIndex = (int)AnalysisMetric.Files;
                await Until(() => window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Use limited metric")));
                Click(window, "Keep previous metric"); await Until(() => metric.IsEnabled && metric.SelectedIndex == (int)AnalysisMetric.OnDisk);
                Assert.NotEmpty(ComparisonControl<ListBox>(window, "Captured comparison rows")!.Items);
                metric.SelectedIndex = (int)AnalysisMetric.Files;
                await Until(() => window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Use limited metric")));
                Click(window, "Use limited metric"); await Until(() => metric.IsEnabled);
                Assert.Equal((int)AnalysisMetric.Files, metric.SelectedIndex);
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaFact]
    public async Task NativeIndexedComparisonAliasMembershipPagesAreIndependentAndResolveOnlyRecordedLocations()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var source = await IndexedFixture.CreateAsync(extraAliases: 70);
        var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow();
        await window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.UniqueFileSize, cancellation: token);
        await source.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!; object? selected = null;
            for (int page = 0; page < 12; page++)
            {
                selected = rows.Items.Cast<object>().SingleOrDefault(item => item.ToString()!.Contains("observed paths: 71 → 71", StringComparison.Ordinal));
                if (selected is not null) break;
                var first = rows.Items[0]; Click(window, "Next comparison page"); await Until(() => rows.ItemCount != 0 && !Equals(rows.Items[0], first));
            }
            Assert.NotNull(selected); rows.SelectedItem = selected;
            await Until(() => ComparisonControl<ListBox>(window, "Previous observed comparison paths")?.ItemCount == 32 && ComparisonControl<ListBox>(window, "Current observed comparison paths")?.ItemCount == 32);
            var before = ComparisonControl<ListBox>(window, "Previous observed comparison paths")!; var after = ComparisonControl<ListBox>(window, "Current observed comparison paths")!;
            ulong initial = Assert.IsType<AnalysisPathMetadata>(before.Items[0]).Id;
            ComparisonControl<Button>(window, "Previous next observed paths")!.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent));
            await Until(() => before.ItemCount == 32 && Assert.IsType<AnalysisPathMetadata>(before.Items[0]).Id != initial);
            Assert.Equal(initial, Assert.IsType<AnalysisPathMetadata>(after.Items[0]).Id);
            before.SelectedIndex = 0;
            await Until(() => ComparisonControl<TextBox>(window, "Previous comparison details")!.Text!.StartsWith("Selected recorded path:", StringComparison.Ordinal));
            Assert.Contains(source.Root, ComparisonControl<TextBox>(window, "Previous comparison details")!.Text);
            ComparisonControl<Button>(window, "Previous previous observed paths")!.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent));
            await Until(() => before.ItemCount == 32 && Assert.IsType<AnalysisPathMetadata>(before.Items[0]).Id == initial);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath));
    }
}
