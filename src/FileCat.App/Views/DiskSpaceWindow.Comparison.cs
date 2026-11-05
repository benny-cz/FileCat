using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.App.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private StackPanel? _comparisonHeading, _comparisonFooter;
        private ComboBox? _comparisonStatus;
        private ListBox? _comparisonRows;
        private double ComparisonMinimumHeight => (_comparisonHeading?.DesiredSize.Height ?? 0) + (_comparisonFooter?.DesiredSize.Height ?? 0) + 220;
        private static string ComparisonMetric(AnalysisMetric metric) => metric switch
        { AnalysisMetric.FileSize => "File size", AnalysisMetric.UniqueFileSize => "Unique file size", AnalysisMetric.OnDisk => "On disk — reported", _ => "Files" };
        private static string ChangeLabel(AnalysisChange change) => change switch
        {
            AnalysisChange.Added => "+ Added", AnalysisChange.Removed => "− Removed", AnalysisChange.Growing => "↑ Growing", AnalysisChange.Shrinking => "↓ Shrinking",
            AnalysisChange.Moved => "Moved / renamed", AnalysisChange.Attribution => "Attribution movement", AnalysisChange.Replaced => "Different observed object",
            AnalysisChange.MetadataChanged => "Metadata changed", AnalysisChange.NoObservedMetadataChange => "No observed metadata change",
            AnalysisChange.PreviouslyUnknown => "Previously unknown", _ => "Unknown"
        };
        private static string ComparisonValue(AnalysisMeasurement measurement, AnalysisMetric metric) => measurement.IsKnown
            ? measurement.Value!.Value.ToString("N0") + (metric == AnalysisMetric.Files ? measurement.Value.Value == 1 ? " path" : " paths" : " B")
            : "Unavailable · " + measurement.State;
        private static string ComparisonDelta(AnalysisDelta? delta, AnalysisMetric metric) => delta is { } value
            ? (value.Magnitude == 0 ? "" : value.Negative ? "−" : "+") + value.Magnitude.ToString("N0") + (metric == AnalysisMetric.Files ? " paths" : " B") : "Unknown";
        private static string RelativeChange(AnalysisComparisonRow row) => row.Delta is null ? "Relative change unknown" : row.RelativePercent is { } value
            ? value.ToString("+0.###;-0.###;0") + "%" : "New / zero baseline";
        private static string ComparisonEvidence(AnalysisComparisonEvidence evidence) => evidence switch
        { AnalysisComparisonEvidence.CompatibleIdentityAndVersion => "Compatible observed identity and version", AnalysisComparisonEvidence.Structural => "Explicit structural comparison", _ => "Path-based observations" };
        private TextBlock ComparisonText(string text, double? size = null)
        {
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            if (size.HasValue)
            {
                label.Bind(TextBlock.FontSizeProperty, new Binding(nameof(FontSize)) { Source = _window,
                    Converter = new FuncValueConverter<double, double>(value => Math.Max(value, size.Value)) });
                if (size.Value >= 18) label.FontWeight = FontWeight.SemiBold;
            }
            return label;
        }

        private void ShowComparison(AnalysisComparisonResult comparison)
        {
            foreach (var child in Children) child.IsVisible = false;
            var heading = _comparisonHeading = new StackPanel { Spacing = 7, Margin = new Thickness(10, 5) };
            heading.Children.Add(ComparisonText("Compare observations · " + ComparisonMetric(comparison.Metric), 20));
            heading.Children.Add(ComparisonText("Previous: " + AnalysisNames.Display(Model.ComparisonPreviousName ?? "", 240) + " · " + Interval(Model.ComparisonPreviousIdentity) +
                "\nCurrent captured revision: " + Interval(Model.ComparisonCurrentIdentity)));
            string Total(AnalysisTotals total) => !total.CompatibleBasis ? "No combined total · incompatible bases" :
                ComparisonValue(AnalysisMeasurement.Known(total.Known), comparison.Metric) + " known" +
                (total.IdentityUnverified > 0 ? " + " + total.IdentityUnverified.ToString("N0") + " identity-unverified bytes" : "") +
                (total.Missing.Count > 0 ? " · " + total.Missing.Values.Aggregate(0ul, (a, b) => checked(a + b)).ToString("N0") + " unavailable measurements" : "");
            heading.Children.Add(ComparisonText("Previous: " + Total(comparison.PreviousTotals) + "\nCurrent: " + Total(comparison.CurrentTotals) +
                "\nObserved net change: " + ComparisonDelta(comparison.NetChange, comparison.Metric), 16));
            heading.Children.Add(ComparisonText(string.Join("\n", comparison.Compatibility.Explanations)));
            if (comparison.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)) heading.Children.Add(ComparisonText("Contains privileged-derived observations. Private handling remains required."));
            var status = _comparisonStatus = new ComboBox { MinWidth = 180, ItemsSource = new[]
                { "All observations", "Growing", "Shrinking", "Added", "Removed", "Moved", "Attribution", "Replaced", "Metadata changed", "No observed metadata change", "Unknown / previously unknown" }, SelectedIndex = 0 };
            var sort = new ComboBox { MinWidth = 180, ItemsSource = new[] { "Absolute difference", "Relative change", "Current amount", "Path" }, SelectedIndex = 0 };
            AutomationProperties.SetName(status, "Comparison status"); AutomationProperties.SetName(sort, "Comparison sort");
            ToolTip.SetTip(status, new RichTip("Comparison status", detail: "Show added, removed, grown, shrunk, moved, attribution or unknown observations. Unknown coverage is never a zero baseline."));
            ToolTip.SetTip(sort, new RichTip("Comparison ranking", detail: "Absolute difference is the default. Relative ranking is explicit; a zero or unknown previous value has no percentage."));
            var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { status, sort } }; heading.Children.Add(filters);
            var count = ComparisonText(""); AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite); heading.Children.Add(count);
            var list = _comparisonRows = new ListBox { MinHeight = 180, Focusable = true, SelectionMode = SelectionMode.Single };
            AutomationProperties.SetName(list, "Comparison rows");
            list.ItemTemplate = new FuncDataTemplate<AnalysisComparisonRow>((row, _) =>
            {
                if (row is null) return new Border();
                var body = new StackPanel { Spacing = 4, Margin = new Thickness(8, 6) };
                string title = ChangeLabel(row.Change) + " · " + ComparisonDelta(row.Delta, comparison.Metric) + " · " + RelativeChange(row);
                body.Children.Add(ComparisonText(title, 15));
                if (row.Current is { } current) body.Children.Add(ComparisonText("Current: " + current.Paths[0] + AliasCount(current) + " · " + ComparisonValue(current.Value, comparison.Metric)));
                if (row.Previous is { } previous) body.Children.Add(ComparisonText("Previous: " + previous.Paths[0] + AliasCount(previous) + " · " + ComparisonValue(previous.Value, comparison.Metric)));
                body.Children.Add(ComparisonText(row.Evidence == AnalysisComparisonEvidence.CompatibleIdentityAndVersion ? "Compatible observed identity/version evidence" : row.Evidence == AnalysisComparisonEvidence.Structural ? "Explicit structural comparison" : "Path-based observations"));
                AutomationProperties.SetName(body, title + "; " + (row.Current?.Paths[0] ?? row.Previous!.Paths[0]));
                ToolTip.SetTip(body, new RichTip("Observed difference", detail: row.Explanation + " " + row.Evidence + ". Inspect the entry for both path sets and measurement bases."));
                return body;
            });
            void Filter()
            {
                if (_disposed) return;
                IEnumerable<AnalysisComparisonRow> rows = comparison.Rows;
                rows = status.SelectedIndex switch
                {
                    1 => rows.Where(r => r.Change == AnalysisChange.Growing), 2 => rows.Where(r => r.Change == AnalysisChange.Shrinking),
                    3 => rows.Where(r => r.Change == AnalysisChange.Added), 4 => rows.Where(r => r.Change == AnalysisChange.Removed),
                    5 => rows.Where(r => r.Change == AnalysisChange.Moved), 6 => rows.Where(r => r.Change == AnalysisChange.Attribution),
                    7 => rows.Where(r => r.Change == AnalysisChange.Replaced), 8 => rows.Where(r => r.Change == AnalysisChange.MetadataChanged),
                    9 => rows.Where(r => r.Change == AnalysisChange.NoObservedMetadataChange), 10 => rows.Where(r => r.Change is AnalysisChange.Unknown or AnalysisChange.PreviouslyUnknown), _ => rows
                };
                rows = sort.SelectedIndex switch
                {
                    1 => rows.OrderByDescending(r => r.RelativePercent.HasValue).ThenByDescending(r => Math.Abs(r.RelativePercent ?? 0)),
                    2 => rows.OrderByDescending(r => r.Current?.Value.IsKnown == true).ThenByDescending(r => r.Current?.Value.Value ?? 0),
                    3 => rows.OrderBy(r => r.Current?.Paths[0] ?? r.Previous!.Paths[0], StringComparer.Ordinal),
                    _ => rows.OrderByDescending(r => r.Delta?.Magnitude ?? 0)
                };
                var shown = rows.ToArray(); list.ItemsSource = shown; count.Text = shown.Length.ToString("N0") + " of " + comparison.Rows.Length.ToString("N0") + " accounting rows. Shared aliases occupy one row for Unique file size and On disk.";
            }
            status.SelectionChanged += (_, _) => Filter(); sort.SelectionChanged += (_, _) => Filter();
            list.DoubleTapped += (_, _) => _ = InspectComparisonAsync();
            list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = InspectComparisonAsync(); } };
            var footer = _comparisonFooter = new StackPanel { Spacing = 6, Margin = new Thickness(10, 6) };
            footer.Children.Add(ActionButton("Inspect comparison entry", () => _ = InspectComparisonAsync(), "Read both path sets, measured values and compatibility evidence. Historical rows supply no current file target."));
            footer.Children.Add(ComparisonText("Offline metadata comparison. Reported allocation is not consumed or reclaimable physical space. No file contents were compared. This view uses the captured metric; choose another metric before starting a new comparison."));
            var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            panel.Children.Add(heading); Grid.SetRow(list, 1); panel.Children.Add(list); Grid.SetRow(footer, 2); panel.Children.Add(footer);
            Grid.SetRowSpan(panel, 5); Children.Add(panel); Filter();
        }

        private async Task InspectComparisonAsync()
        {
            if (_disposed || _window._dialogs.IsOpen || Model.Comparison is not { } comparison) return;
            if (_comparisonRows?.SelectedItem is not AnalysisComparisonRow row) { _comparisonRows?.Focus(); return; }
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(ComparisonText(ChangeLabel(row.Change) + " · " + ComparisonDelta(row.Delta, comparison.Metric) + " · " + RelativeChange(row), 17));
            body.Children.Add(ComparisonText(row.Explanation));
            body.Children.Add(ComparisonText("Metadata evidence: " + ComparisonEvidence(row.Evidence) + ". No current source or content proof is supplied."));
            void Side(string title, AnalysisComparisonEntity? entity)
            {
                body.Children.Add(ComparisonText(title + ": " + (entity is null ? "No comparable observed entry" : ComparisonValue(entity.Value, comparison.Metric)), 16));
                if (entity is null) return;
                body.Children.Add(ComparisonText("Observed modified time: " + (entity.ModifiedUtc?.ToString("u") ?? "Unknown") + " · " + entity.Paths.Length.ToString("N0") + (entity.Paths.Length == 1 ? " path" : " paths")));
                var paths = new ListBox { ItemsSource = entity.Paths, MaxHeight = 180, MinHeight = 60,
                    ItemTemplate = new FuncDataTemplate<string>((path, _) => ComparisonText(path ?? "")) };
                AutomationProperties.SetName(paths, title + " observed paths"); body.Children.Add(paths);
            }
            Side("Previous", row.Previous); Side("Current", row.Current);
            if (comparison.Metric == AnalysisMetric.OnDisk)
            {
                foreach (bool previous in new[] { true, false })
                {
                    string title = previous ? "Previous recorded measurement bases" : "Current recorded measurement bases";
                    body.Children.Add(ComparisonText(title, 16));
                    var descriptions = Model.ComparisonBases(previous, previous ? row.Previous : row.Current);
                    if (descriptions.IsEmpty) body.Children.Add(ComparisonText("No measurement bases recorded for this entry; component coverage is unknown."));
                    var bases = new ListBox { ItemsSource = descriptions, MaxHeight = 140, MinHeight = 40,
                        ItemTemplate = new FuncDataTemplate<string>((basis, _) => ComparisonText(basis ?? "")) };
                    AutomationProperties.SetName(bases, title); body.Children.Add(bases);
                }
            }
            await _window._dialogs.ShowCustomAsync("Comparison entry", body, [new DialogButton("Close", false, IsDefault: true, IsCancel: true)]);
        }

        private static string AliasCount(AnalysisComparisonEntity entity) => entity.Paths.Length > 1 ? " (" + entity.Paths.Length.ToString("N0") + " aliases)" : "";
        private static string Interval(SessionIdentity? identity) => identity is null ? "Unknown observation interval" : identity.StartedUtc.ToString("u") + " – " + identity.ObservedUtc.ToString("u");
        private void ReleaseComparisonView()
        {
            if (_comparisonRows is null) return;
            _comparisonRows.ItemsSource = null; _comparisonRows.ItemTemplate = null; Children.Clear();
            _comparisonRows = null; _comparisonHeading = null; _comparisonFooter = null; _comparisonStatus = null;
        }
    }
}
