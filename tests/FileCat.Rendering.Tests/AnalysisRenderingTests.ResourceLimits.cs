using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ResourceLimitsSetupOpensAndClosesWithoutStartingScan()
    {
        using var fixture = new PreparationFixture();
        using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services);
        var window = new DiskSpaceWindow { Width = 380, Height = 430, FontSize = 20 };
        window.AddSession(model, [fixture.Root]);
        window.Show();
        try
        {
            Button Find(string title) => window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && button.Content as string == title);
            Find("Resource limits…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Frame(window);
            var text = window.GetVisualDescendants().OfType<TextBlock>().Where(row => row.IsEffectivelyVisible).Select(row => row.Text).ToArray();
            Assert.Contains("Disk space resource limits", text);
            Assert.Contains(text, row => row?.Contains("partial results", StringComparison.Ordinal) == true);
            Assert.Contains(text, row => row?.Contains("free-space reserve", StringComparison.Ordinal) == true);
            var close = Find("Close limits");
            Assert.True(close.IsDefault && close.IsCancel);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Frame(window);
            Assert.True(Find("Start analysis").IsEnabled);
            Assert.Null(model.Tree);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
