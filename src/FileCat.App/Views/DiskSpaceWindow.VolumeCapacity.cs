using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private Button VolumeCapacityRibbon(Func<ImmutableDictionary<string, AnalysisVolumeCapacity>> samples,
        Func<ImmutableArray<RootBinding>> roots, Func<UInt128?> fullAllocation, Func<string> currentAnalysis)
    {
        var button = new Button { Content = new TextBlock { TextWrapping = TextWrapping.Wrap }, IsVisible = false };
        AutomationProperties.SetAutomationId(button, "AnalysisVolumeCapacity");
        AutomationProperties.SetHelpText(button, "Open the observed filesystem capacity, measurement time and scope.");
        button.Click += async (_, _) => await ShowVolumeCapacityAsync(samples(), roots(), fullAllocation(), currentAnalysis());
        return button;
    }

    private static void RefreshVolumeCapacityRibbon(Button ribbon, ImmutableDictionary<string, AnalysisVolumeCapacity> samples)
    {
        ribbon.IsVisible = samples.Count != 0;
        if (samples.IsEmpty) return;
        string title = $"Volume capacity for {samples.Count} locations";
        if (samples.Count == 1)
        {
            var sample = samples.Values.Single();
            title = $"Volume capacity · {DiskSpaceViewModel.Bytes(sample.UsedBytes)} used · {DiskSpaceViewModel.Bytes(sample.FreeBytes)} free";
        }
        ((TextBlock)ribbon.Content!).Text = title;
        AutomationProperties.SetName(ribbon, title);
    }

    private async Task ShowVolumeCapacityAsync(ImmutableDictionary<string, AnalysisVolumeCapacity> samples,
        ImmutableArray<RootBinding> roots, UInt128? fullAllocation, string currentAnalysis)
    {
        if (samples.IsEmpty) return;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Heading("Current analysis"));
        content.Children.Add(Paragraph(currentAnalysis));
        content.Children.Add(Heading(roots.Length == 1 ? "Containing filesystem" : "Containing filesystems"));
        foreach (var root in roots)
        {
            if (!samples.TryGetValue(root.Id, out var sample)) continue;
            string scope = sample.WholeVolumeVerified
                ? "The source is the filesystem root."
                : "File totals describe the scanned scope. Capacity describes the containing filesystem.";
            var observed = sample.ObservedUtc.ToLocalTime();
            if (roots.Length != 1) content.Children.Add(Paragraph("Location · " + AnalysisNames.Display(root.Label, 120)));
            content.Children.Add(Paragraph($"Reported used {DiskSpaceViewModel.Bytes(sample.UsedBytes)}\n" +
                $"Reported free {DiskSpaceViewModel.Bytes(sample.FreeBytes)} · Available to you {DiskSpaceViewModel.Bytes(sample.AvailableBytes)}\n" +
                $"Reported capacity {DiskSpaceViewModel.Bytes(sample.TotalBytes)}"));
            if (sample.MayShareCapacity)
                content.Children.Add(new TextBlock
                {
                    Text = "This filesystem may share capacity with other volumes. Pool identity is unavailable. Each volume is shown on its own.",
                    TextWrapping = TextWrapping.Wrap
                });
            else if (roots.Length == 1 && sample.WholeVolumeVerified && fullAllocation is { } known)
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"Known observed allocation {DiskSpaceViewModel.Bytes(known)}\n" +
                        $"Difference from reported used space {VolumeDifference(sample.UsedBytes, known)}",
                    TextWrapping = TextWrapping.Wrap
                });
                content.Children.Add(new TextBlock
                {
                    Text = "Only observed allocation is counted. The difference can reflect shared blocks, snapshots, reservations, unobserved space, measurement basis or activity during the scan.",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else if (sample.WholeVolumeVerified)
                content.Children.Add(new TextBlock
                {
                    Text = "The difference is available in the unfiltered On disk view of one finished whole-volume scan.",
                    TextWrapping = TextWrapping.Wrap
                });
            content.Children.Add(Paragraph($"{scope}\nMeasured {observed:g} ({observed:zzz})\nVolume ID {AnalysisNames.Display(sample.VolumeIdentity, 120)}"));
        }
        await _dialogs.ShowCustomAsync("Volume capacity", content, [new DialogButton("Close capacity", true, IsDefault: true, IsCancel: true)]);

        static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
        static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
    }

    private static string CapacityScope(string label, AnalysisSavedView view) => label +
        (view.ShowUnknown ? " · uncertain filter results" : !string.IsNullOrWhiteSpace(view.Query) || view.PanelFilter is not null ? " · filtered results" : "");

    internal static string VolumeDifference(UInt128 used, UInt128 known)
    {
        if (used == known) return DiskSpaceViewModel.Bytes(0);
        return used > known ? "+" + DiskSpaceViewModel.Bytes(used - known) : "−" + DiskSpaceViewModel.Bytes(known - used);
    }

    private static bool CanShowVolumeDifference(AnalysisSavedView view, AnalysisPhase phase, ScanPolicy policy) =>
        phase == AnalysisPhase.Finished && policy.BasisIds.Length == 1 &&
        view is { Metric: AnalysisMetric.OnDisk, Grouping: AnalysisGroupingDimension.FolderHierarchy, Scope: AnalysisTree.RootKey,
            Root: null, ShowUnknown: false, PanelFilter: null } && string.IsNullOrWhiteSpace(view.Query);

    private sealed partial class SessionView
    {
        private string CurrentAnalysisCapacityContext()
        {
            if (Model.Tree is not { } tree) return "No measurements yet.";
            var view = Model.PublishedReportView;
            string scope = view.Scope == AnalysisTree.RootKey && tree.Snapshot.Roots.Length == 1 &&
                view.Grouping == AnalysisGroupingDimension.FolderHierarchy ? tree.Snapshot.Roots[0].Label : tree.NameOf(view.Scope);
            return CapacityScope(AnalysisNames.Display(scope, 120), view) + "\n" + _summary.Text + "\n" + _coverageHeadline.Text;
        }

        private UInt128? FullVolumeAllocation()
        {
            if (Model.IsIndexedRuntimePreview || Model.Tree is not { } tree || !CanShowVolumeDifference(Model.PublishedReportView,
                tree.Snapshot.Phase, tree.Snapshot.Policy)) return null;
            return tree.Projection.Totals.Known;
        }
    }

    private sealed partial class IndexedSessionView
    {
        private ImmutableDictionary<string, AnalysisVolumeCapacity> CurrentCapacity =>
            _runtimeAcquisition?.RuntimeSession.VolumeCapacities ?? ImmutableDictionary<string, AnalysisVolumeCapacity>.Empty;

        private string CurrentAnalysisCapacityContext()
        {
            var view = CapturePublishedView();
            var descriptor = _source.Descriptor;
            string scope = _groupPage?.Scope?.Definition.Label ?? _shownPage?.Scope?.Path.Name ??
                (descriptor.Roots.Length == 1 ? descriptor.Roots[0].Label : "All locations");
            return CapacityScope(AnalysisNames.Display(scope, 120), view) + "\n" + _summary.Text + "\n" +
                CoverageHeadline(descriptor.Coverage, descriptor.Freshness);
        }

        private UInt128? FullVolumeAllocation()
        {
            if (_building || _publishing || _navigationCancel is not null) return null;
            var descriptor = _source.Descriptor;
            return CanShowVolumeDifference(CapturePublishedView(), descriptor.Phase, descriptor.Policy)
                ? _explorer.Totals.Known : null;
        }
    }
}
