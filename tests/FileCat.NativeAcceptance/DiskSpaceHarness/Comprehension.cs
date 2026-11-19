using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private async Task CaptureComprehensionStudyAsync()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var window = _window!;
        var model = _models.Single();
        var token = _indexedLifetime.Token;
        Add("Unreadable folder/hidden.bin", 4096);
        var unreadable = Path.Combine(_fixture, "Unreadable folder");
        var permission = File.GetUnixFileMode(unreadable);
        try
        {
            File.SetUnixFileMode(unreadable, UnixFileMode.None);
            await ReadyAsync(() => FindButton("Start analysis") is { IsEnabled: true });
            Click("Start analysis");
            await ReadyAsync(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished);
            var tree = model.Tree!;
            var shared = tree.Items.Values.Single(item => item.Synthetic && item.Family == "shared" && item.Members.Length == 2);
            var gap = tree.Items.Values.Single(item => item.Name == "Unreadable folder");
            var original = tree.Snapshot.Nodes.Single(node => node.Name == "large.bin");
            if (tree.Snapshot.Coverage != NamespaceCoverage.Partial || shared.Value == 0 || !gap.Unknown)
                throw new InvalidDataException("The study needs a nonzero shared charge and a real unreadable group.");

            model.Focus(shared.Key);
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TreemapControl>().Any(chart => chart.IsEffectivelyVisible && chart.Bounds.Height > 0));
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.IsEffectivelyVisible && text.Text?.Contains("the chart counts its measured amount once", StringComparison.Ordinal) == true));
            await RecipeFrameAsync("comprehension-treemap.png", true, token);
            var chartMode = window.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == "Chart view");
            chartMode.SelectedIndex = 1;
            await ReadyAsync(() => window.GetVisualDescendants().OfType<SunburstControl>().Any(chart => chart.IsEffectivelyVisible && chart.Bounds.Height > 0));
            await RecipeFrameAsync("comprehension-sunburst.png", true, token);

            model.Focus(gap.Key);
            if (FindButton("Inspect selection") is { IsEnabled: true }) Click("Inspect selection");
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.IsEffectivelyVisible && text.Text?.Contains("File paths: 0 observed (coverage partial)", StringComparison.Ordinal) == true));
            await RecipeFrameAsync("comprehension-unknown.png", true, token);
            await CheckNativeVolumeCapacityAsync(window, model, "comprehension-capacity.png");

            model.Focus(AnalysisTree.Key(original.Id));
            await ReadyAsync(() => FindButton("Review cleanup") is { IsEnabled: true });
            Click("Review cleanup");
            await ReadyAsync(() => FindButton("Cancel / Keep files") is { IsDefault: true, IsCancel: true }
                && FindButton("Recycle included entries") is { IsEnabled: false });
            await RecipeFrameAsync("comprehension-trash.png", true, token);
            Click("Cancel / Keep files");
            await ReadyAsync(() => FindButton("Cancel / Keep files") is null);
            _indexedProbe = new { kind = "comprehension", sharedBytes = shared.Value, sharedNames = shared.Members.Length,
                unknownObserved = gap.Unknown, captured = true };
            _indexedProbeFinished = true;
        }
        catch (Exception error) { _indexedProbeError = error.ToString(); }
        finally
        {
            File.SetUnixFileMode(unreadable, permission);
            WriteReport("comprehension-capture-complete");
        }

        Button? FindButton(string name) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            button.IsEffectivelyVisible && Equals(button.Content, name));

        void Click(string name) => (FindButton(name) ?? throw new InvalidOperationException($"Missing button {name}."))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
