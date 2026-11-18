using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private async Task FolderExclusionProbeAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            var window = _window!;
            var model = _models.Single();
            var expander = window.GetVisualDescendants().OfType<Expander>().Single(item => item.Header as string == "Exclude folders…");
            expander.IsExpanded = true;
            TextBox? Editor() => window.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
                box.IsEffectivelyVisible && AutomationProperties.GetName(box) == "Excluded folders for location 1");
            Button? StartButton() => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
                button.IsEffectivelyVisible && button.Content as string == "Start analysis");
            await ReadyAsync(() => Editor() is not null);
            var editor = Editor()!;
            editor.Text = "Small files";
            editor.BringIntoView();
            await ReadyAsync(() => editor.Bounds.Height > 0 && StartButton()?.IsEnabled == true);
            var start = StartButton()!;
            var position = start.TranslatePoint(default, window);
            if (position is null || position.Value.Y < 0 || position.Value.Y + start.Bounds.Height > window.ClientSize.Height + 1)
                throw new InvalidOperationException("Start analysis was clipped after opening the exclusions editor.");
            if (window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible &&
                button.Content as string is "Export entire inventory…" or "Export report…"))
                throw new InvalidOperationException("The ready scanner showed result export commands.");
            var panelFilter = window.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                AutomationProperties.GetName(box) == "Use panel filter for display after scan");
            if (panelFilter.IsChecked == true) throw new InvalidOperationException("A new scan selected the panel display filter automatically.");
            panelFilter.BringIntoView();
            await ReadyAsync(() => panelFilter.TranslatePoint(default, window) is { } top && top.Y >= 0 &&
                top.Y + panelFilter.Bounds.Height <= window.ClientSize.Height + 1);
            panelFilter.IsChecked = true;
            await ReadyAsync(() => panelFilter.IsChecked == true);
            RenderNativeFrame("folder-exclusions-setup.png");
            start.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await ReadyAsync(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished && model.PanelFilterApplied);
            var snapshot = model.Tree!.Snapshot;
            var skipped = snapshot.BoundaryTotals.GetValueOrDefault(AnalysisBoundaryKind.FolderExclusion);
            if (snapshot.Coverage != NamespaceCoverage.CompleteUnderPolicy || model.SourceCloseFailures != 0 || skipped != 1 ||
                snapshot.Nodes.Any(node => node.Name.StartsWith("item-", StringComparison.Ordinal)) ||
                !snapshot.Nodes.Any(node => node.Name == "Small files" && !node.IncludedByPolicy) ||
                !snapshot.Nodes.Any(node => node.Name == "photo.png" && node.IncludedByPolicy))
                throw new InvalidDataException("The native scan did not preserve the requested exclusion and sibling evidence.");
            if (model.Tree.Projection.Totals.Known != 60 || model.Tree.Projection.Totals.MatchingPaths != 1)
                throw new InvalidDataException("The native view did not apply only the requested panel filename mask.");
            await ReadyAsync(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                text.Text?.Contains("Folder exclusion rules: 1. Folders skipped: 1.", StringComparison.Ordinal) == true));
            var panelStatus = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Panel filter active");
            Point? panelTop = panelStatus.TranslatePoint(default, window);
            if (panelTop is null || panelTop.Value.Y < 0 || panelTop.Value.Y + panelStatus.Bounds.Height > window.ClientSize.Height)
                throw new InvalidOperationException("The display filter status was clipped in the compact results.");
            var table = window.GetVisualDescendants().OfType<ListBox>().Single(view =>
                view.IsEffectivelyVisible && AutomationProperties.GetName(view) == "Ranked analysis entries");
            var tableTop = table.TranslatePoint(default, window);
            if (tableTop is null || table.Bounds.Height < 30 || tableTop.Value.Y + 30 > window.ClientSize.Height)
                throw new InvalidOperationException("The ranked table was below the compact window viewport.");
            RenderNativeFrame("folder-exclusions-result.png");
            var newAnalysis = window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.IsEffectivelyVisible && button.Content as string == "New analysis");
            newAnalysis.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await ReadyAsync(() => window.SessionCount == 2 && _tabs?.SelectedIndex == 1);
            window.SelectTemporaryWork();
            await ReadyAsync(() => _tabs?.SelectedIndex == 0);
            _indexedProbe = new { kind = "folder-exclusions", skipped, observed = snapshot.Nodes.Length, setupButtonVisible = true,
                sourceCloseFailures = model.SourceCloseFailures, coverage = snapshot.Coverage.ToString(), selectedTemporaryAnalysis = true,
                panelFilterViewApplied = true };
            _indexedProbeFinished = true;
            WriteReport("folder-exclusion-probe-finished");
        }
        catch (Exception error)
        {
            _indexedProbeError = error.GetType().Name + ": " + error.Message;
            try { RenderNativeFrame("folder-exclusions-failure.png"); } catch (Exception) { }
            WriteReport("folder-exclusion-probe-failed");
        }
    }
}
