using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public async Task NativeIndexedComparisonPersistenceControlsRestoreAppliedOfflineViewAndCrossPageFocus(int mode, bool previous)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(sharedAlias: true); var token = TestContext.Current.CancellationToken;
        string name = $"saved-view-{mode}-{previous}.fcdsc", path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name);
        var window = new DiskSpaceWindow();
        var roots = mode == 2 ? source.Lease!.Descriptor.Roots.ToDictionary(root => root.Id, root => root.Id) : null;
        await window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.Files, roots, cancellation: token, services: profile.Services);
        window.Show(); Frame(window); string? focused = null;
        try
        {
            ComparisonControl<TextBox>(window, "Comparison query")!.Text = "name:file-";
            ComparisonControl<ComboBox>(window, "Comparison query side")!.SelectedIndex = (int)AnalysisComparisonSide.Either;
            ComparisonControl<ComboBox>(window, "Comparison row order")!.SelectedIndex = (int)AnalysisComparisonOrder.Path;
            Click(window, "Apply comparison view");
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!;
            await UntilComparisonPersistence(() => rows.IsEnabled && rows.ItemCount == 32 && ComparisonControl<TextBlock>(window, "Applied comparison view")!.Text!.Contains("name:file-", StringComparison.Ordinal));
            if (mode != 0)
            {
                ComparisonControl<ComboBox>(window, "Comparison chart")!.SelectedIndex = mode;
                ComparisonControl<ComboBox>(window, "Comparison chart side")!.SelectedIndex = previous ? 1 : 0;
                Click(window, "Apply comparison chart"); await UntilComparisonPersistence(() => rows.IsEnabled && window.GetVisualDescendants().OfType<AnalysisChartControl>().Any(chart => chart.IsEffectivelyVisible));
            }
            var first = rows.Items[0]; Click(window, "Next comparison page"); await UntilComparisonPersistence(() => rows.IsEnabled && !Equals(first, rows.Items[0]));
            rows.SelectedIndex = 0;
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<TextBox>(window, "Current comparison details")!.Text!.Contains("Recorded location:", StringComparison.Ordinal));
            focused = Assert.IsType<AnalysisPathMetadata>(ComparisonControl<ListBox>(window, "Current observed comparison paths")!.Items[0]).Name;
            // Unapplied editor changes must never replace the captured view.
            ComparisonControl<TextBox>(window, "Comparison query")!.Text = "name:unapplied-draft";
            Click(window, "Save named comparison…"); await UntilComparisonPersistence(() => ComparisonControl<TextBox>(window, "Comparison filename") is not null);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Cancel")).IsDefault);
            ComparisonControl<TextBox>(window, "Comparison filename")!.Text = name; Click(window, "Save new comparison");
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<TextBlock>(window, "Comparison status")!.Text!.StartsWith("Saved ", StringComparison.Ordinal), () => ComparisonText(window));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        byte[] saved = File.ReadAllBytes(path);
        string original = Path.Combine(source.Root, "source"), offline = Path.Combine(source.Root, "source-offline"); Directory.Move(original, offline);
        var opened = new DiskSpaceWindow();
        try
        {
            await opened.AddIndexedSessionAsync(source.Lease!, AnalysisMetric.Files, cancellation: token, services: profile.Services);
            opened.Show(); Frame(opened); Click(opened, "Open comparison…");
            await UntilComparisonPersistence(() => ComparisonControl<TextBox>(opened, "Saved comparison filename") is not null);
            Click(opened, "Browse saved comparisons"); await UntilComparisonPersistence(() => ComparisonControl<ComboBox>(opened, "Saved comparisons")?.ItemCount == 1);
            ComparisonControl<ComboBox>(opened, "Saved comparisons")!.SelectedIndex = 0;
            await UntilComparisonPersistence(() => opened.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Open comparison observations")).IsEffectivelyEnabled);
            Click(opened, "Open comparison observations");
            await UntilComparisonPersistence(() => opened.SessionCount == 2 && ComparisonControl<ListBox>(opened, "Captured comparison rows")?.SelectedItem is not null, () => ComparisonText(opened));
            Assert.Equal("name:file-", ComparisonControl<TextBox>(opened, "Comparison query")!.Text);
            Assert.Equal((int)AnalysisComparisonSide.Either, ComparisonControl<ComboBox>(opened, "Comparison query side")!.SelectedIndex);
            Assert.Equal((int)AnalysisComparisonOrder.Path, ComparisonControl<ComboBox>(opened, "Comparison row order")!.SelectedIndex);
            Assert.Equal(mode, ComparisonControl<ComboBox>(opened, "Comparison chart")!.SelectedIndex);
            if (mode != 0) Assert.Equal(previous ? 1 : 0, ComparisonControl<ComboBox>(opened, "Comparison chart side")!.SelectedIndex);
            Assert.Contains(focused!, ComparisonControl<TextBox>(opened, "Current comparison details")!.Text!);
            Assert.Contains("Imported observations are unverified", ComparisonText(opened));
            Assert.DoesNotContain(opened.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && Equals(button.Content, "Add to cleanup"));
            Click(opened, "Previous comparison page");
            await UntilComparisonPersistence(() => ComparisonControl<ListBox>(opened, "Captured comparison rows")!.IsEnabled && ComparisonControl<ListBox>(opened, "Captured comparison rows")!.SelectedItem is null);
            Assert.Equal(saved, File.ReadAllBytes(path)); Assert.False(Directory.Exists(original));
        }
        finally { opened.ForceClose(); await opened.WaitForIndexedCloseAsync(); Directory.Move(offline, original); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeIndexedComparisonExternalOpenRefusesCorruptionAndReservesBeforeLoader(bool cancel)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var pool = new AnalysisSessionAdmission(); var occupied = Enumerable.Range(0, AnalysisLimits.Sessions).Select(_ => pool.Acquire()).ToArray();
        var window = new DiskSpaceWindow(null, null, pool); bool called = false;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.AddPreparedIndexedComparisonSessionAsync(profile.Services, _ =>
            { called = true; return Task.FromException<AnalysisSavedIndexedComparison>(new IOException("not admitted")); }, token));
            Assert.False(called); Assert.Equal(0, window.SessionCount);
            foreach (var slot in occupied) await slot.CloseAfterAsync(Task.CompletedTask);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); if (cancel) stop.Cancel();
            string folder = Path.Combine(profile.Root, "external-comparison"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "corrupt.fcdsc"); byte[] original = [1, 2, 3, 4]; File.WriteAllBytes(path, original);
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.AddExternalIndexedComparisonSessionAsync(profile.Services, path, stop.Token));
            else await Assert.ThrowsAsync<InvalidDataException>(() => window.AddExternalIndexedComparisonSessionAsync(profile.Services, path, stop.Token));
            Assert.Equal(0, window.SessionCount); Assert.Equal(original, File.ReadAllBytes(path));
            Assert.True(pool.HasCapacity); Assert.False(pool.HasFailedClose);
            Assert.True(!Directory.Exists(profile.Services.Paths.AnalysisScratchDirectory) || !Directory.EnumerateFileSystemEntries(profile.Services.Paths.AnalysisScratchDirectory).Any());
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(1)] [InlineData(2)]
    public async Task NativeIndexedComparisonChartKeyboardAutomaticallyLocatesAcrossPagesAndKeepsFilters(int mode)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow();
        await window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.Files, cancellation: token);
        window.Show(); Frame(window);
        try
        {
            ComparisonControl<ComboBox>(window, "Comparison row order")!.SelectedIndex = (int)AnalysisComparisonOrder.Path;
            Click(window, "Apply comparison view"); var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!; await UntilComparisonPersistence(() => rows.IsEnabled);
            ComparisonControl<ComboBox>(window, "Comparison chart")!.SelectedIndex = mode; Click(window, "Apply comparison chart");
            await UntilComparisonPersistence(() => rows.IsEnabled && window.GetVisualDescendants().OfType<AnalysisChartControl>().Any(chart => chart.IsEffectivelyVisible));
            Frame(window); var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible);
            using (var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize(1180, 780))) bitmap.Render(window);
            // The combined chart initially contains its root summary. Explore the
            // recorded root before selecting a file accounting row among its children.
            chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await UntilComparisonPersistence(() => rows.IsEnabled && window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Up comparison chart")).IsEnabled);
            using (var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize(1180, 780))) bitmap.Render(window);
            string parentCaption = ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text!;
            // At this width Sunburst correctly aggregates the 300 tiny siblings.
            // Explore the first recorded subfolder, whose two files have actual
            // visible fragments in both charts, before testing row selection.
            chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text != parentCaption);
            using (var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize(1180, 780))) bitmap.Render(window);
            // Move the table far from the chart's first fragment before selecting it.
            for (int index = 0; index < 4; index++)
            {
                var first = rows.Items[0]; Click(window, "Next comparison page"); await UntilComparisonPersistence(() => rows.IsEnabled && !Equals(first, rows.Items[0]));
            }
            var distant = rows.Items[0]; chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
            await UntilComparisonPersistence(() => rows.IsEnabled && rows.SelectedItem is not null && !Equals(distant, rows.Items[0]), () => ComparisonText(window));
            ComparisonControl<TextBox>(window, "Comparison query")!.Text = "name:never-a-match"; Click(window, "Apply comparison view");
            await UntilComparisonPersistence(() => rows.IsEnabled && rows.ItemCount == 0);
            using (var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize(1180, 780))) bitmap.Render(window);
            chart.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
            await UntilComparisonPersistence(() => rows.IsEnabled && ComparisonControl<TextBlock>(window, "Comparison chart scope")!.Text!.Contains("outside the applied table filter", StringComparison.Ordinal), () => ComparisonText(window));
            Assert.Equal(0, rows.ItemCount); Assert.Equal("name:never-a-match", ComparisonControl<TextBox>(window, "Comparison query")!.Text);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    private static async Task UntilComparisonPersistence(Func<bool> predicate, Func<string>? diagnostic = null)
    {
        // A cold native import validates and indexes two entire inventories.
        // Wait for observed completion, allowing the VM's slower first run.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!predicate() && DateTime.UtcNow < deadline)
        { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.True(predicate(), "The captured comparison operation did not complete." + (diagnostic is null ? "" : " " + diagnostic()));
    }
}
