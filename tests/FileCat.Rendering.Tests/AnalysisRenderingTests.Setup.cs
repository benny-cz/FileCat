using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ReadySetupKeepsStartVisibleWithLargeTextAndEightNarrowTabsWithoutBindingSources()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        ThemeManager.Apply("ClassicDark"); int bindings = 0;
        var window = new DiskSpaceWindow { Width = 380, Height = 430, FontSize = 20 };
        try
        {
            for (int i = 0; i < 8; i++) window.AddSession(new DiskSpaceViewModel(fixture.Services, _ =>
                { bindings++; throw new InvalidOperationException("Layout must not bind a source."); }), [fixture.Root]);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var start = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content as string == "Start analysis");
            Assert.True(start.IsEnabled);
            var at = start.TranslatePoint(default, window); Assert.NotNull(at);
            Assert.True(at.Value.Y >= 0 && at.Value.Y + start.Bounds.Height <= window.ClientSize.Height + .5,
                "Start clipped: " + at + " / " + start.Bounds + " in " + window.ClientSize);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ListBox>(), list => list.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible &&
                b.Content as string is "Review cleanup" or "Back" or "Save…" or "Compare…" or "Export entire inventory…" or "Export report…");
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && b.Content as string == "Open saved…");
            Assert.Equal(0, bindings);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task RemovingAndAddingLocationsKeepsAccessibleEditorAndRemoveNamesUnique()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services); var window = new DiskSpaceWindow();
        window.AddSession(model, [fixture.Root + "-A", fixture.Root + "-B", fixture.Root + "-C"]);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var second = window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationLabel(box) == "Analysis location 2");
            Assert.IsAssignableFrom<Grid>(second.Parent).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Add location").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var editors = window.GetVisualDescendants().OfType<TextBox>().Where(box => AutomationLabel(box)?.StartsWith("Analysis location ", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(3, editors.Length); Assert.Equal(3, editors.Select(AutomationProperties.GetName).Distinct().Count());
            Assert.Equal(fixture.Root + "-A", editors.Single(box => AutomationLabel(box) == "Analysis location 1").Text);
            Assert.Equal(fixture.Root + "-C", editors.Single(box => AutomationLabel(box) == "Analysis location 2").Text);
            Assert.Equal("", editors.Single(box => AutomationLabel(box) == "Analysis location 3").Text);
            var removes = window.GetVisualDescendants().OfType<Button>().Where(b => b.Content as string == "Remove").ToArray();
            Assert.Equal(3, removes.Length); Assert.Equal(3, removes.Select(AutomationProperties.GetName).Distinct().Count());
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
