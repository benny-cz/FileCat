using Avalonia.Automation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task PointerSelectedComparisonRowOpensItsInspectorWithEnter()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("keyboard.fcdsa");
        using var comparison = await source.CompareSavedAsync("keyboard.fcdsa"); var window = new DiskSpaceWindow(); window.AddSession(comparison); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Comparison rows");
            var item = Assert.IsType<ListBoxItem>(rows.ContainerFromIndex(0));
            var point = item.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(); Assert.NotNull(rows.SelectedItem); Assert.True(rows.IsKeyboardFocusWithin);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Comparison entry");
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task ComparisonQueryPreservesLastValidRowsAndExposesUncertainMatches()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("query.fcdsa");
        using var comparison = await source.CompareSavedAsync("query.fcdsa"); var window = new DiskSpaceWindow(); window.AddSession(comparison); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            window.GetVisualDescendants().OfType<Expander>().Single(e => AutomationProperties.GetName(e) == "Comparison query and ranking controls").IsExpanded = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var query = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Comparison query");
            var rows = window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Comparison rows");
            var apply = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Apply comparison query");
            Assert.Equal(3, rows.ItemCount);
            async Task Run(string expression)
            {
                query.Text = expression; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(5); while (!apply.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.True(apply.IsEnabled); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            await Run("path=Projects/source.cs AND delta=0"); Assert.Equal(1, rows.ItemCount);
            await Run("delta>"); Assert.Equal(1, rows.ItemCount);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.StartsWith("Filter not applied.", StringComparison.Ordinal) == true);
            await Run("allocated>0"); Assert.Equal(2, rows.ItemCount);
            var unknown = window.GetVisualDescendants().OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Inspect uncertain comparison matches");
            Assert.True(unknown.IsEffectivelyVisible); unknown.IsChecked = true; Assert.Equal(1, rows.ItemCount);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("1 uncertain query matches", StringComparison.Ordinal) == true);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Clear comparison query").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, rows.ItemCount); Assert.False(unknown.IsVisible);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task QueryWorkRetainsInputsAndResultWhenItsComparisonViewCloses()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        using var source = new DiskSpaceViewModel(fixture.Services, ImportedFixture()) { Metric = AnalysisMetric.FileSize };
        await source.RebuildAsync(true); await source.SaveNewAsync("query.fcdsa");
        using var comparison = await source.CompareSavedAsync("query.fcdsa");
        var query = comparison.QueryComparisonAsync("size>0", false, AnalysisComparisonSide.CurrentOrPrevious, CancellationToken.None);
        comparison.Dispose(); source.Dispose();
        using var result = await query; Assert.Equal(3, result.Matches.Length);
        var snapshot = Fixture();
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(snapshot, snapshot, AnalysisMetric.Files));
        result.Dispose(); using var next = AnalysisComparison.Build(snapshot, snapshot, AnalysisMetric.Files); Assert.Equal(3, next.Rows.Length);
    }
}
