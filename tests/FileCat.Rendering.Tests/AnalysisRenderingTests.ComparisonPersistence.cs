using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
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
    public async Task SavedComparisonNativeInputsRoundtripEveryMetricAndKeepExactRawNamesAndSources(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        source.AssertPayloads();
        using var current = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)) { Metric = metric };
        await current.RebuildAsync(true); await current.SaveNewAsync("native-before.fcdsa");
        var comparison = await current.CompareSavedAsync("native-before.fcdsa");
        var expected = comparison.Comparison!.CurrentTotals;
        comparison.SetComparisonView(comparison.ComparisonView with { Chart = AnalysisComparisonChartMode.Sunburst, PreviousChart = true, ChartScope = "n:1" });
        var receipt = await comparison.SaveComparisonNewAsync("native-pair.fcdsc");
        Assert.False(comparison.HasTemporaryWork); comparison.Dispose(); await comparison.CloseCompletion;
        using var restored = await current.OpenSavedComparisonAsync(receipt.Name);
        Assert.False(restored.HasTemporaryWork); Assert.Equal(metric, restored.Comparison!.Metric);
        Assert.Equal(expected.Known, restored.Comparison.CurrentTotals.Known); Assert.Equal(expected.IdentityUnverified, restored.Comparison.CurrentTotals.IdentityUnverified);
        Assert.True(restored.Comparison.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        Assert.Equal(AnalysisComparisonChartMode.Sunburst, restored.ComparisonView.Chart); Assert.True(restored.ComparisonView.PreviousChart);
        using (var capture = restored.CaptureComparison())
        {
            ValidateNativeNames(source, capture.Previous.Snapshot, TestContext.Current.CancellationToken);
            ValidateNativeNames(source, capture.Current.Snapshot, TestContext.Current.CancellationToken);
        }
        using (var chart = await restored.BuildComparisonChartAsync(true))
        {
            Assert.True(chart.CanExplore(restored.ComparisonView.ChartScope));
        }
        Assert.Contains(receipt.Name, await current.ReadSavedComparisonNamesAsync());
        source.AssertPayloads();
    }
    [AvaloniaFact]
    public async Task ComparisonSaveCaptureOwnsInputsAndAppliedQueryIndependentlyOfLaterEditsAndSourceClosure()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("captured-before.fcdsa");
        var comparison = await source.CompareSavedAsync("captured-before.fcdsa");
        comparison.SetComparisonView(comparison.ComparisonView with { Query = "name=source.cs", QuerySide = AnalysisComparisonSide.Current, Cohort = AnalysisComparisonCohort.All, Order = AnalysisComparisonOrder.Path, FocusNode = 4 });
        using var captured = comparison.CaptureComparison();
        comparison.SetComparisonView(comparison.ComparisonView with { Query = "name=picture.png", FocusNode = 5 });
        await comparison.SaveComparisonNewAsync("captured.fcdsc", captured, TestContext.Current.CancellationToken);
        Assert.True(comparison.HasTemporaryWork); captured.Dispose(); comparison.Dispose(); await comparison.CloseCompletion;
        using var restored = await source.OpenSavedComparisonAsync("captured.fcdsc");
        Assert.Equal("name=source.cs", restored.ComparisonView.Query); Assert.Equal(4UL, restored.ComparisonView.FocusNode);
        using var query = restored.TakeRestoredComparisonQuery(); Assert.NotNull(query); Assert.Single(query.Matches);
        Assert.Contains("source.cs", query.Matches[0].Current!.Paths[0], StringComparison.Ordinal);
        source.Dispose(); Assert.NotEmpty(restored.Comparison!.Rows);
    }
    [AvaloniaFact]
    public async Task ComparisonSavingRefusesPendingMetricAndExistingNamesWhileKeepingThePreviousFile()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("ready-before.fcdsa");
        using var comparison = await source.CompareSavedAsync("ready-before.fcdsa");
        await comparison.SaveComparisonNewAsync("ready.fcdsc");
        string path = Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, "ready.fcdsc"); byte[] before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<IOException>(() => comparison.SaveComparisonNewAsync("ready.fcdsc")); Assert.Equal(before, File.ReadAllBytes(path));
        using var reader = comparison.Comparison!.Retain(); var changing = comparison.ChangeComparisonMetricAsync(AnalysisMetric.Files);
        Assert.False(comparison.CanSaveComparison); Assert.True(comparison.HasTemporaryWork);
        Assert.Throws<InvalidOperationException>(() => comparison.CaptureComparison()); reader.Dispose(); await changing;
        Assert.True(comparison.CanSaveComparison); Assert.Equal(before, File.ReadAllBytes(path));
    }
    [AvaloniaFact]
    public async Task ComparisonSaveAndOpenDialogsUsePrivateAtomicBundlesAndRestoreAppliedViewInANewTab()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("dialog-before.fcdsa");
        var comparison = await source.CompareSavedAsync("dialog-before.fcdsa"); var window = new DiskSpaceWindow(); window.AddSession(source); window.AddSession(comparison); window.Show();
        async Task Wait(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!condition() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            Assert.True(condition()); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        }
        Button Button(string text) => FindAnalysisButton(window, text) ?? throw new InvalidOperationException($"Missing button {text}.");
        void Click(string text) { Button(text).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        ComboBox Choice(string name) => window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationLabel(box) == name);
        void ExpandQuery()
        {
            window.GetVisualDescendants().OfType<Expander>().Single(expander => AutomationLabel(expander) == "Comparison query and ranking controls").IsExpanded = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        }
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); ExpandQuery();
            Choice("Comparison status").SelectedIndex = (int)AnalysisComparisonCohort.NoObservedMetadataChange;
            Choice("Comparison sort").SelectedIndex = (int)AnalysisComparisonOrder.Path;
            Choice("Comparison query side").SelectedIndex = (int)AnalysisComparisonSide.Current;
            window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.IsEffectivelyVisible && box.Content as string == "Case sensitive").IsChecked = true;
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationLabel(box) == "Comparison query");
            query.Text = "name=source.cs"; Click("Apply comparison query");
            await Wait(() => comparison.ComparisonView.Query == "name=source.cs" && Button("Apply comparison query").IsEnabled);
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single(box => AutomationLabel(box) == "Comparison rows");
            Assert.Equal(1, rows.ItemCount); rows.SelectedIndex = 0;
            Choice("Comparison chart").SelectedIndex = (int)AnalysisComparisonChartMode.Sunburst;
            Choice("Comparison chart side").SelectedIndex = 1;
            await Wait(() => comparison.ComparisonView.Chart == AnalysisComparisonChartMode.Sunburst && comparison.ComparisonView.PreviousChart && Button("Save comparison…").IsEnabled);
            Assert.Equal(4UL, comparison.ComparisonView.FocusNode);
            Click("Save comparison…");
            Assert.True(Button("Cancel").IsDefault);
            window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationLabel(box) == "Comparison filename").Text = "dialog-pair.fcdsc";
            Click("Save new comparison"); await Wait(() => comparison.SavedComparisonName == "dialog-pair.fcdsc");
            query.Text = "name=picture.png"; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(comparison.HasTemporaryWork); Click("Close analysis");
            Assert.True(Button("Keep open").IsDefault); Click("Keep open"); Assert.Equal(2, window.SessionCount);
            query.Text = "name=source.cs"; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.False(comparison.HasTemporaryWork);
            Click("Close analysis"); await Wait(() => window.SessionCount == 1); Assert.False(comparison.HasTemporaryWork);
            Click("Open comparison…"); Click("Browse saved comparisons");
            await Wait(() => window.GetVisualDescendants().OfType<ComboBox>().Any(box => AutomationLabel(box) == "Saved comparisons" && box.ItemCount == 1));
            window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationLabel(box) == "Saved comparisons").SelectedIndex = 0;
            Click("Open comparison observations"); await Wait(() => window.SessionCount == 2);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), label => label.IsEffectivelyVisible && label.Text?.Contains("Sources are disconnected", StringComparison.OrdinalIgnoreCase) == true);
            ExpandQuery();
            Assert.Equal((int)AnalysisComparisonCohort.NoObservedMetadataChange, Choice("Comparison status").SelectedIndex);
            Assert.Equal((int)AnalysisComparisonOrder.Path, Choice("Comparison sort").SelectedIndex);
            Assert.Equal((int)AnalysisComparisonSide.Current, Choice("Comparison query side").SelectedIndex);
            Assert.Equal((int)AnalysisComparisonChartMode.Sunburst, Choice("Comparison chart").SelectedIndex);
            Assert.Equal(1, Choice("Comparison chart side").SelectedIndex);
            Assert.Equal("name=source.cs", window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationLabel(box) == "Comparison query").Text);
            rows = window.GetVisualDescendants().OfType<ListBox>().Single(box => AutomationLabel(box) == "Comparison rows");
            Assert.Equal(1, rows.ItemCount); Assert.Contains(4UL, Assert.IsType<AnalysisComparisonRow>(rows.SelectedItem).Current!.NodeIds);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
