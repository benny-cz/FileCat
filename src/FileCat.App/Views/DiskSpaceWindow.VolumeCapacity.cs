using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private readonly record struct CapacityContext(string Scope, string Amount, string Details, string Coverage, bool Warning);

    private Button VolumeCapacityRibbon(Func<ImmutableDictionary<string, AnalysisVolumeCapacity>> samples,
        Func<ImmutableArray<RootBinding>> roots, Func<UInt128?> fullAllocation, Func<CapacityContext> currentAnalysis)
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
        ImmutableArray<RootBinding> roots, UInt128? fullAllocation, CapacityContext currentAnalysis)
    {
        if (samples.IsEmpty) return;
        var content = new StackPanel { Spacing = 8 };
        var current = new StackPanel { Spacing = 4 };
        var coverage = new ReadingStatus(Paragraph(currentAnalysis.Coverage));
        coverage.SetWarning(currentAnalysis.Warning);
        current.Children.Add(Heading("Current analysis"));
        current.Children.Add(Paragraph(currentAnalysis.Scope, "Secondary"));
        var amount = MeasureLabel();
        amount.Text = KeepMeasurementUnits(currentAnalysis.Amount);
        current.Children.Add(amount);
        if (currentAnalysis.Details.Length != 0) current.Children.Add(Paragraph(KeepMeasurementUnits(currentAnalysis.Details), "Secondary"));
        current.Children.Add(coverage);
        content.Children.Add(Section(current));
        var filesystems = new StackPanel { Spacing = 7 };
        filesystems.Children.Add(Heading(roots.Length == 1 ? "Containing filesystem" : "Containing filesystems"));
        content.Children.Add(Section(filesystems));
        foreach (var root in roots)
        {
            if (!samples.TryGetValue(root.Id, out var sample)) continue;
            string scope = sample.WholeVolumeVerified
                ? "The source is the filesystem root."
                : "File totals describe the scanned scope. Capacity describes the containing filesystem.";
            var observed = sample.ObservedUtc.ToLocalTime();
            if (roots.Length != 1) filesystems.Children.Add(Heading(AnalysisNames.Display(root.Label, 120)));
            var figures = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10, RowSpacing = 4 };
            filesystems.Children.Add(figures);
            AddFigure("Reported used", DiskSpaceViewModel.Bytes(sample.UsedBytes));
            AddFigure("Reported free", DiskSpaceViewModel.Bytes(sample.FreeBytes));
            AddFigure("Available to you", DiskSpaceViewModel.Bytes(sample.AvailableBytes));
            AddFigure("Reported capacity", DiskSpaceViewModel.Bytes(sample.TotalBytes));
            if (sample.MayShareCapacity)
                filesystems.Children.Add(Paragraph("This filesystem may share capacity with other volumes. Pool identity is unavailable. Each volume is shown on its own."));
            else if (roots.Length == 1 && sample.WholeVolumeVerified && fullAllocation is { } known)
            {
                AddFigure("Known observed allocation", DiskSpaceViewModel.Bytes(known));
                AddFigure("Difference from reported used space", VolumeDifference(sample.UsedBytes, known));
                filesystems.Children.Add(Paragraph("Only observed allocation is counted. The difference can reflect shared blocks, snapshots, reservations, unobserved space, measurement basis or activity during the scan."));
            }
            else if (sample.WholeVolumeVerified)
                filesystems.Children.Add(Paragraph("The difference is available in the unfiltered On disk view of one finished whole-volume scan."));
            filesystems.Children.Add(Paragraph($"{scope}\nMeasured {observed:g} ({observed:zzz})\nVolume ID {AnalysisNames.Display(sample.VolumeIdentity, 120)}", "Secondary"));

            void AddFigure(string label, string value)
            {
                int row = figures.RowDefinitions.Count;
                figures.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var key = Paragraph(label);
                var number = ReadingLabel("Text", FontWeight.SemiBold);
                number.Text = KeepMeasurementUnits(value);
                number.TextAlignment = TextAlignment.Right;
                number.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetRow(key, row);
                Grid.SetRow(number, row);
                Grid.SetColumn(number, 1);
                figures.Children.Add(key);
                figures.Children.Add(number);
            }
        }
        await _dialogs.ShowCustomAsync("Volume capacity", content, [new DialogButton("Close capacity", true, IsDefault: true, IsCancel: true)]);

        static Border Section(Control body) => ReadingSurface(body, new Thickness(8, 6), new Thickness(2, 0, 0, 0), "Accent");
        static TextBlock Paragraph(string text, string role = "Text")
        {
            var label = ReadingLabel(role);
            label.Text = text;
            return label;
        }
        static TextBlock Heading(string text) => SectionLabel(text);
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
        private CapacityContext CurrentAnalysisCapacityContext()
        {
            if (Model.Tree is not { } tree) return new("", "No measurements yet.", "", "", false);
            var view = Model.PublishedReportView;
            string scope = ResultScopeName(tree, view.Scope);
            var (amount, details) = Model.ScopeReading(compact: false);
            return new(CapacityScope(AnalysisNames.Display(scope, 120), view), amount, details,
                CoverageHeadline(tree.Snapshot.Coverage, tree.Snapshot.Freshness), HasCoverageWarning(tree.Snapshot.Coverage, tree.Snapshot.Freshness));
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

        private CapacityContext CurrentAnalysisCapacityContext()
        {
            var view = CapturePublishedView();
            var descriptor = _source.Descriptor;
            return new(CapacityScope(_readingScope.Text ?? "All locations", view), _summary.Text ?? "", _readingDetails.Text ?? "",
                CoverageHeadline(descriptor.Coverage, descriptor.Freshness), HasCoverageWarning(descriptor.Coverage, descriptor.Freshness));
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
