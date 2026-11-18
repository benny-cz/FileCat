using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        private TextBox? _comparisonQuery;
        private AnalysisComparisonQueryResult? _comparisonQueryResult;
        private CancellationTokenSource? _comparisonQueryCancel;
        private int _comparisonQueryGeneration;
        private ListBox? _comparisonRows;
        private double ComparisonMinimumHeight => (_comparisonHeading?.DesiredSize.Height ?? 0) + (_comparisonFooter?.DesiredSize.Height ?? 0) + (_comparisonChartHost?.IsVisible == true ? _comparisonChartHost.DesiredSize.Height : 0) + 220;
        private static string ComparisonMetric(AnalysisMetric metric) => metric switch
        { AnalysisMetric.FileSize => "File size", AnalysisMetric.UniqueFileSize => "Unique file size", AnalysisMetric.OnDisk => "On disk — reported", _ => "Files" };
        private static string ChangeLabel(AnalysisChange change) => change switch
        {
            AnalysisChange.Added => "+ Added",
            AnalysisChange.Removed => "− Removed",
            AnalysisChange.Growing => "↑ Growing",
            AnalysisChange.Shrinking => "↓ Shrinking",
            AnalysisChange.Moved => "Moved / renamed",
            AnalysisChange.Attribution => "Attribution movement",
            AnalysisChange.Replaced => "Different observed object",
            AnalysisChange.MetadataChanged => "Metadata changed",
            AnalysisChange.NoObservedMetadataChange => "No observed metadata change",
            AnalysisChange.PreviouslyUnknown => "Previously unknown",
            _ => "Unknown"
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
        private TextBlock SessionText(string text, double? size = null)
        {
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            if (size.HasValue)
            {
                label.Bind(TextBlock.FontSizeProperty, new Binding(nameof(FontSize))
                {
                    Source = _window,
                    Converter = new FuncValueConverter<double, double>(value => Math.Max(value, size.Value))
                });
                if (size.Value >= 18) label.FontWeight = FontWeight.SemiBold;
            }
            return label;
        }

        private void ShowComparison(AnalysisComparisonResult comparison)
        {
            var savedView = Model.ComparisonView;
            _comparisonQueryResult = Model.TakeRestoredComparisonQuery();
            foreach (var child in Children) child.IsVisible = false;
            var heading = _comparisonHeading = new StackPanel { Spacing = 7, Margin = new Thickness(10, 5) };
            heading.Children.Add(SessionText("Compare observations · " + ComparisonMetric(comparison.Metric), 20));
            heading.Children.Add(SessionText("Export captured comparison reports, the selected recorded chart, or a complete machine bundle. Chart areas show one recorded side; growth colors describe observed differences."));
            heading.Children.Add(SessionText("Saved previous: " + AnalysisNames.Display(Model.ComparisonPreviousName ?? "", 240)));
            AddComparisonMetric(heading, comparison.Metric);
            var details = new StackPanel { Spacing = 7 };
            details.Children.Add(SessionText("Previous interval: " + Interval(Model.ComparisonPreviousIdentity) +
                "\nCurrent captured revision: " + Interval(Model.ComparisonCurrentIdentity)));
            string Total(AnalysisTotals total) => !total.CompatibleBasis ? "No combined total · incompatible bases" :
                ComparisonValue(AnalysisMeasurement.Known(total.Known), comparison.Metric) + " known" +
                (total.IdentityUnverified > 0 ? " + " + total.IdentityUnverified.ToString("N0") + " identity-unverified bytes" : "") +
                (total.Missing.Count > 0 ? " · " + total.Missing.Values.Aggregate(0ul, (a, b) => checked(a + b)).ToString("N0") + " unavailable measurements" : "");
            string totals = "Previous: " + Total(comparison.PreviousTotals) + "\nCurrent: " + Total(comparison.CurrentTotals) +
                "\nObserved net change: " + ComparisonDelta(comparison.NetChange, comparison.Metric);
            heading.Children.Add(SessionText(totals, 16));
            details.Children.Add(SessionText(string.Join("\n", comparison.Compatibility.Explanations)));
            string comparisonKind = Model.ComparisonIsStructural ? "Explicit structural comparison" : comparison.Compatibility.VerifiedIdentityContext ? "Recorded comparison" : "Path-based comparison";
            if (!comparison.Compatibility.ComparableTotals) comparisonKind += " · limited";
            var evidence = new Expander { Header = SessionText(comparisonKind + " · details and compatibility"), Content = details, IsExpanded = false };
            AutomationProperties.SetName(evidence, "Comparison observation details and compatibility"); heading.Children.Add(evidence);
            if (comparison.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)) heading.Children.Add(SessionText("Contains privileged-derived observations. Private handling remains required."));
            var list = _comparisonRows = new ListBox { MinHeight = 180, Focusable = true, SelectionMode = SelectionMode.Single };
            var count = SessionText(""); AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
            var status = _comparisonStatus = new ComboBox
            {
                MinWidth = 180,
                ItemsSource = new[]
                { "All observations", "Growing", "Shrinking", "Added", "Removed", "Moved", "Attribution", "Replaced", "Metadata changed", "No observed metadata change", "Unknown / previously unknown" },
                SelectedIndex = (int)savedView.Cohort
            };
            var sort = new ComboBox { MinWidth = 180, ItemsSource = new[] { "Absolute difference", "Relative change", "Current amount", "Path" }, SelectedIndex = (int)savedView.Order };
            AutomationProperties.SetName(status, "Comparison status"); AutomationProperties.SetName(sort, "Comparison sort");
            ToolTip.SetTip(status, new RichTip("Comparison status", detail: "Show added, removed, grown, shrunk, moved, attribution or unknown observations. Unknown coverage is never a zero baseline."));
            ToolTip.SetTip(sort, new RichTip("Comparison ranking", detail: "Absolute difference is the default. Relative ranking is explicit; a zero or unknown previous value has no percentage."));
            var filterBody = new StackPanel { Spacing = 7 };
            var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { status, sort } }; filterBody.Children.Add(filters);
            var query = _comparisonQuery = new TextBox { Text = savedView.Query, PlaceholderText = "e.g. delta>1MiB OR change:removed", MaxLength = AnalysisLimits.QueryBytes, MinWidth = 180, Width = 310 };
            var querySide = new ComboBox { ItemsSource = new[] { "Current; Previous for removed rows", "Current observations", "Previous observations", "Either recorded side" }, SelectedIndex = (int)savedView.QuerySide, MinWidth = 180 };
            var sensitive = new CheckBox { Content = "Case sensitive", IsChecked = savedView.CaseSensitive };
            var queryError = SessionText(string.IsNullOrWhiteSpace(savedView.Query) ? "" : "Restored applied query: " + AnalysisNames.Display(savedView.Query, 8192)); AutomationProperties.SetLiveSetting(queryError, AutomationLiveSetting.Assertive);
            var unknown = new CheckBox { Content = "Inspect uncertain query matches", IsVisible = _comparisonQueryResult is { Unknown.IsEmpty: false }, IsChecked = savedView.ShowUnknown };
            AutomationProperties.SetName(query, "Comparison query"); AutomationProperties.SetName(querySide, "Comparison query side");
            AutomationProperties.SetName(unknown, "Inspect uncertain comparison matches");
            const string queryHelp = "Use name/path/ext/type, size/allocated/delta, modified, root, state or change with AND/OR/NOT. Paths are root-relative; dates use the selected side's recorded timezone. A shared row matches when any recorded alias satisfies the complete expression; its full amount and aliases remain together. Unknown predicates appear in the uncertain cohort. Select one side when root IDs or labels differ.";
            ToolTip.SetTip(query, new RichTip("Comparison query", detail: queryHelp)); AutomationProperties.SetHelpText(query, queryHelp);
            var apply = new Button { Content = "Apply comparison query" };
            var growth = CreateGrowthInsight();
            ((Button)growth.Child!).Click += (_, _) =>
            {
                if (!apply.IsEnabled || !Model.ComparisonReady || !comparison.Compatibility.ComparableValues) return;
                query.Text = GrowthRecipeQuery;
                querySide.SelectedIndex = (int)AnalysisComparisonSide.Current;
                _ = ApplyQuery(growthPreset: true);
            };
            SetGrowthInsightAvailability(growth, comparison.Compatibility, Model.ComparisonReady);
            heading.Children.Add(growth);
            async Task ApplyQuery(bool growthPreset = false)
            {
                if (_disposed || !apply.IsEnabled) return;
                using var actualWork = _actualWork.Enter();
                _comparisonQueryCancel?.Cancel(); using var cancel = new CancellationTokenSource(); _comparisonQueryCancel = cancel;
                int generation = ++_comparisonQueryGeneration; apply.IsEnabled = false;
                SetGrowthInsightAvailability(growth, comparison.Compatibility, ready: false);
                string expression = query.Text ?? ""; bool caseSensitive = sensitive.IsChecked == true; var side = (AnalysisComparisonSide)querySide.SelectedIndex;
                queryError.Text = "Applying query to recorded observations…";
                try
                {
                    var result = await Model.QueryComparisonAsync(expression, caseSensitive, side, cancel.Token);
                    if (_disposed || cancel.IsCancellationRequested || generation != _comparisonQueryGeneration) { result.Dispose(); return; }
                    var previous = _comparisonQueryResult; _comparisonQueryResult = result;
                    Model.SetComparisonView(Model.ComparisonView with { Query = expression, CaseSensitive = caseSensitive, QuerySide = side, ShowUnknown = false });
                    unknown.IsChecked = false; unknown.IsVisible = !result.Unknown.IsEmpty;
                    if (growthPreset)
                    {
                        status.SelectedIndex = (int)AnalysisComparisonCohort.Growing;
                        sort.SelectedIndex = (int)AnalysisComparisonOrder.AbsoluteDifference;
                    }
                    queryError.Text = query.Text == expression && (sensitive.IsChecked == true) == caseSensitive && querySide.SelectedIndex == (int)side
                        ? "Applied query: " + (string.IsNullOrWhiteSpace(expression) ? "All observations" : AnalysisNames.Display(expression, 8192))
                        : "Query controls changed during work. The previous controls were applied; choose Apply to update.";
                    Filter(); previous?.Dispose();
                }
                catch (OperationCanceledException) { if (!_disposed && generation == _comparisonQueryGeneration) queryError.Text = "Query canceled. The last applied rows remain visible."; }
                catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException or InvalidDataException)
                { if (!_disposed && generation == _comparisonQueryGeneration) queryError.Text = "Filter not applied. " + AnalysisNames.Display(ex.Message, 400); }
                finally
                {
                    if (ReferenceEquals(_comparisonQueryCancel, cancel)) _comparisonQueryCancel = null;
                    if (!_disposed)
                    {
                        apply.IsEnabled = true;
                        SetGrowthInsightAvailability(growth, comparison.Compatibility, Model.ComparisonReady);
                        Draft();
                    }
                }
            }
            apply.Click += (_, _) => _ = ApplyQuery();
            query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = ApplyQuery(); } };
            void Draft()
            {
                if (!_disposed) Model.SetComparisonDraftChanged(query.Text != Model.ComparisonView.Query || (sensitive.IsChecked == true) != Model.ComparisonView.CaseSensitive || querySide.SelectedIndex != (int)Model.ComparisonView.QuerySide);
            }
            void Edited() { Draft(); if (!_disposed && apply.IsEnabled) queryError.Text = "Query changed. Choose Apply comparison query; the last applied rows remain visible."; }
            query.TextChanged += (_, _) => Edited(); querySide.SelectionChanged += (_, _) => Edited(); sensitive.IsCheckedChanged += (_, _) => Edited();
            var queryControls = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { query, querySide, sensitive, apply } };
            queryControls.Children.Add(ActionButton("Clear comparison query", () =>
            {
                _comparisonQueryCancel?.Cancel(); ++_comparisonQueryGeneration; query.Text = ""; unknown.IsChecked = false; unknown.IsVisible = false;
                list.ItemsSource = null; _comparisonQueryResult?.Dispose(); _comparisonQueryResult = null;
                Model.SetComparisonView(Model.ComparisonView with { Query = "", ShowUnknown = false }); Draft(); queryError.Text = "All observations · no query applied."; Filter();
            }, "Remove the comparison query and uncertain cohort. The full-inventory totals and original accounting rows remain unchanged."));
            filterBody.Children.Add(queryControls); filterBody.Children.Add(unknown); filterBody.Children.Add(queryError);
            var filterDisclosure = new Expander { Header = SessionText("Filter and rank comparison rows"), Content = filterBody, IsExpanded = false };
            AutomationProperties.SetName(filterDisclosure, "Comparison query and ranking controls"); heading.Children.Add(filterDisclosure);
            AutomationProperties.SetName(list, "Comparison rows");
            list.ItemTemplate = new FuncDataTemplate<AnalysisComparisonRow>((row, _) =>
            {
                if (row is null) return new Border();
                var body = new StackPanel { Spacing = 4, Margin = new Thickness(8, 6) };
                string title = ChangeLabel(row.Change) + " · " + ComparisonDelta(row.Delta, comparison.Metric) + " · " + RelativeChange(row);
                body.Children.Add(SessionText(title, 15));
                if (row.Current is { } current) body.Children.Add(SessionText("Current: " + current.Paths[0] + AliasCount(current) + " · " + ComparisonValue(current.Value, comparison.Metric)));
                if (row.Previous is { } previous) body.Children.Add(SessionText("Previous: " + previous.Paths[0] + AliasCount(previous) + " · " + ComparisonValue(previous.Value, comparison.Metric)));
                body.Children.Add(SessionText(row.Evidence == AnalysisComparisonEvidence.CompatibleIdentityAndVersion ? "Compatible observed identity/version evidence" : row.Evidence == AnalysisComparisonEvidence.Structural ? "Explicit structural comparison" : "Path-based observations"));
                AutomationProperties.SetName(body, title + "; " + (row.Current?.Paths[0] ?? row.Previous!.Paths[0]));
                ToolTip.SetTip(body, new RichTip("Observed difference", detail: row.Explanation + " " + row.Evidence + ". Inspect the entry for both path sets and measurement bases."));
                return body;
            });
            bool updatingRows = false;
            void Filter()
            {
                if (_disposed || !Model.ComparisonReady || !ReferenceEquals(_comparisonRows, list)) return;
                Model.SetComparisonView(Model.ComparisonView with { Cohort = (AnalysisComparisonCohort)Math.Max(0, status.SelectedIndex), Order = (AnalysisComparisonOrder)Math.Max(0, sort.SelectedIndex), ShowUnknown = unknown.IsChecked == true });
                IEnumerable<AnalysisComparisonRow> rows = _comparisonQueryResult is { } applied
                    ? unknown.IsChecked == true ? applied.Unknown : applied.Matches : comparison.Rows;
                rows = AnalysisComparisonProjection.Apply(rows, Model.ComparisonView.Cohort, Model.ComparisonView.Order);
                var selected = list.SelectedItem as AnalysisComparisonRow;
                var anchor = Model.ComparisonView; var shown = rows.ToArray();
                selected ??= shown.SingleOrDefault(row => anchor.FocusNode != 0 && (anchor.PreviousFocus ? row.Previous : row.Current)?.NodeIds.Contains(anchor.FocusNode) == true);
                updatingRows = true;
                try { list.ItemsSource = shown; if (selected is not null && shown.Contains(selected)) list.SelectedItem = selected; }
                finally { updatingRows = false; }
                count.Text = shown.Length.ToString("N0") + " of " + comparison.Rows.Length.ToString("N0") + " accounting rows" +
                    " · " + status.SelectedItem + " · " + sort.SelectedItem + (_comparisonQueryResult is not null ? " · applied query" : "") +
                    (_comparisonQueryResult is { } filtered ? " · " + filtered.Unknown.Length.ToString("N0") + " uncertain query matches" : "") +
                    ". Shared aliases remain together. Totals above describe the full inventories.";
            }
            status.SelectionChanged += (_, _) => Filter(); sort.SelectionChanged += (_, _) => Filter(); unknown.IsCheckedChanged += (_, _) => Filter();
            list.SelectionChanged += (_, _) =>
            {
                if (_disposed || updatingRows || !Model.ComparisonReady || !ReferenceEquals(_comparisonRows, list)) return;
                var row = list.SelectedItem as AnalysisComparisonRow;
                bool previous = row?.Current is null && row?.Previous is not null;
                ulong node = (previous ? row?.Previous : row?.Current)?.NodeIds.FirstOrDefault() ?? 0;
                Model.SetComparisonView(Model.ComparisonView with { FocusNode = node, PreviousFocus = previous });
            };
            list.DoubleTapped += (_, _) => _ = InspectComparisonAsync();
            list.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = InspectComparisonAsync(); } }, RoutingStrategies.Tunnel);
            var footer = _comparisonFooter = new StackPanel { Spacing = 6, Margin = new Thickness(10, 6) };
            footer.Children.Add(ActionButton("Inspect comparison entry", () => _ = InspectComparisonAsync(), "Read both path sets, measured values and compatibility evidence. Historical rows supply no current file target."));
            footer.Children.Add(ActionButton("Copy summary", () => _ = CopyCapturedTextAsync(($"{ComparisonMetric(comparison.Metric)}\n{totals}\n{comparisonKind}", "")),
                "Copy the displayed totals and comparison scope as text."));
            AddComparisonPersistence(footer);
            footer.Children.Add(SessionText("Offline metadata comparison. Reported allocation is not consumed or reclaimable physical space. No file contents were compared. Chart area shows the chosen side's amount; growth color describes comparison status."));
            var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            var table = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(10, 4) };
            table.Children.Add(count); Grid.SetRow(list, 1); table.Children.Add(list);
            panel.Children.Add(heading); Grid.SetRow(table, 2); panel.Children.Add(table); Grid.SetRow(footer, 3); panel.Children.Add(footer);
            AddComparisonCharts(heading, list, panel);
            Grid.SetRowSpan(panel, 5); Children.Add(panel); Filter();
            if (savedView.FocusNode != 0)
                list.SelectedItem = list.Items.Cast<AnalysisComparisonRow>().SingleOrDefault(row =>
                    (savedView.PreviousFocus ? row.Previous : row.Current)?.NodeIds.Contains(savedView.FocusNode) == true);
        }

        private async Task InspectComparisonAsync(AnalysisComparisonRow? selected = null)
        {
            if (_disposed) return;
            using var actualWork = _actualWork.Enter();
            if (_disposed || _window._dialogs.IsOpen || Model.Comparison is not { } comparison) return;
            var row = selected ?? _comparisonRows?.SelectedItem as AnalysisComparisonRow;
            if (row is null) { _comparisonRows?.Focus(); return; }
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(SessionText(ChangeLabel(row.Change) + " · " + ComparisonDelta(row.Delta, comparison.Metric) + " · " + RelativeChange(row), 17));
            body.Children.Add(SessionText(row.Explanation));
            body.Children.Add(SessionText("Metadata evidence: " + ComparisonEvidence(row.Evidence) + ". No current source or content proof is supplied."));
            void Side(string title, AnalysisComparisonEntity? entity)
            {
                body.Children.Add(SessionText(title + ": " + (entity is null ? "No comparable observed entry" : ComparisonValue(entity.Value, comparison.Metric)), 16));
                if (entity is null) return;
                body.Children.Add(SessionText("Observed modified time: " + (entity.ModifiedUtc?.ToString("u") ?? "Unknown") + " · " + entity.Paths.Length.ToString("N0") + (entity.Paths.Length == 1 ? " path" : " paths")));
                var paths = new ListBox
                {
                    ItemsSource = entity.Paths,
                    MaxHeight = 180,
                    MinHeight = 60,
                    ItemTemplate = new FuncDataTemplate<string>((path, _) => SessionText(path ?? ""))
                };
                AutomationProperties.SetName(paths, title + " observed paths"); body.Children.Add(paths);
            }
            Side("Previous", row.Previous); Side("Current", row.Current);
            if (comparison.Metric == AnalysisMetric.OnDisk)
            {
                foreach (bool previous in new[] { true, false })
                {
                    string title = previous ? "Previous recorded measurement bases" : "Current recorded measurement bases";
                    body.Children.Add(SessionText(title, 16));
                    var descriptions = Model.ComparisonBases(previous, previous ? row.Previous : row.Current);
                    if (descriptions.IsEmpty) body.Children.Add(SessionText("No measurement bases recorded for this entry; component coverage is unknown."));
                    var bases = new ListBox
                    {
                        ItemsSource = descriptions,
                        MaxHeight = 140,
                        MinHeight = 40,
                        ItemTemplate = new FuncDataTemplate<string>((basis, _) => SessionText(basis ?? ""))
                    };
                    AutomationProperties.SetName(bases, title); body.Children.Add(bases);
                }
            }
            string description = string.Join('\n', body.Children.OfType<TextBlock>().Select(label => label.Text));
            body.Children.Add(ActionButton("Copy details", () => _ = CopyCapturedTextAsync((description, "")),
                "Copy the selected change's displayed measurements and explanation."));
            await _window._dialogs.ShowCustomAsync("Comparison entry", body, [new DialogButton("Close", false, IsDefault: true, IsCancel: true)]);
        }

        private static string AliasCount(AnalysisComparisonEntity entity) => entity.Paths.Length > 1 ? " (" + entity.Paths.Length.ToString("N0") + " aliases)" : "";
        private static string Interval(SessionIdentity? identity) => identity is null ? "Unknown observation interval" : identity.StartedUtc.ToString("u") + " – " + identity.ObservedUtc.ToString("u");
        private void ReleaseComparisonView()
        {
            _comparisonQueryCancel?.Cancel(); ++_comparisonQueryGeneration;
            RetireComparisonChart();
            if (_comparisonRows is null) return;
            _comparisonRows.ItemsSource = null; _comparisonRows.ItemTemplate = null; Children.Clear();
            _comparisonQueryResult?.Dispose(); _comparisonQueryResult = null;
            _comparisonRows = null; _comparisonHeading = null; _comparisonFooter = null; _comparisonStatus = null; _comparisonQuery = null;
        }
    }
}
