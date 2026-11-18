using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

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
            Assert.Contains("This view currently holds no private index reservation.", text);
            var close = Find("Close limits");
            Assert.True(close.IsDefault && close.IsCancel);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Frame(window);
            Assert.True(Find("Start analysis").IsEnabled);
            Assert.Null(model.Tree);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task NativeResourceLimitsShowActualScratchReservationsFromPreviewAndRecordedViews()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(fixture.Services, indexedStorage: true);
        string input = Path.Combine(fixture.Root, "source");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "sample.txt"), "unchanged");
        var window = new DiskSpaceWindow(null, null, new AnalysisSessionAdmission()) { Width = 380, Height = 430, FontSize = 20 };
        window.AddSession(model, [input]);
        window.Show();
        try
        {
            await StartReadyPreviewAsync(model, input);
            Assert.True(model.ScratchReservedBytes > 0);
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var recorded = await model.AcquireRecordedDatasetAsync(lifetime.Token);
            Assert.Equal(recorded.ScratchReservedBytes, model.ScratchReservedBytes);
            CheckLimits(recorded.ScratchReservedBytes);

            await window.AddIndexedSessionAsync(recorded, services: fixture.Services, cancellation: lifetime.Token);
            Frame(window);
            CheckLimits(recorded.ScratchReservedBytes);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); await model.CloseCompletion; }

        void CheckLimits(long bytes)
        {
            Assert.True(bytes > 0);
            Click(window, "Resource limits…");
            Frame(window);
            var text = window.GetVisualDescendants().OfType<TextBlock>().Where(row => row.IsEffectivelyVisible).Select(row => row.Text).ToArray();
            var rules = OperatingSystem.IsWindows() ? AnalysisNameRules.Windows : AnalysisNameRules.Unix;
            Assert.Contains(AnalysisNames.DisplayPath(fixture.Services.Paths.AnalysisScratchDirectory, rules), text);
            Assert.Contains($"{DiskSpaceViewModel.Bytes((UInt128)bytes)} for this index and its derived data.", text);
            Click(window, "Close limits");
            Frame(window);
        }
    }
}
