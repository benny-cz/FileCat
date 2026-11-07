using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using FileCat.App.Controls.DiskSpace;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private StackPanel? _comparisonChartHost;
        private AnalysisComparisonChart? _comparisonChartData;
        private TreemapControl? _comparisonTreemap;
        private SunburstControl? _comparisonSunburst;
        private CancellationTokenSource? _comparisonChartCancel, _comparisonMetricCancel;
        private Task _comparisonChartWork = Task.CompletedTask;
        private int _comparisonChartGeneration;
        private int _comparisonMetricGeneration;

        private void AddComparisonMetric(StackPanel heading, AnalysisMetric selected)
        {
            var metric = new ComboBox { ItemsSource = new[] { "File size", "Unique file size", "On disk — reported", "Files" },
                SelectedIndex = (int)selected, MinWidth = 190 };
            AutomationProperties.SetName(metric, "Comparison metric");
            ToolTip.SetTip(metric, new Controls.RichTip("Comparison metric", detail: "Recompute both captured inventories without opening source files. Changing metric clears the table query, ranking, focus and chart. Old readers finish before a new result is admitted."));
            heading.Children.Add(metric);
            metric.SelectionChanged += (_, _) =>
            {
                if (metric.SelectedIndex >= 0 && metric.SelectedIndex != (int)selected)
                { metric.IsEnabled = false; _ = ChangeComparisonMetricAsync((AnalysisMetric)metric.SelectedIndex); }
            };
        }

        private async Task ChangeComparisonMetricAsync(AnalysisMetric metric)
        {
            if (_disposed) return;
            using var actualWork = _actualWork.Enter();
            _comparisonMetricCancel?.Cancel(); using var cancel = new CancellationTokenSource(); _comparisonMetricCancel = cancel;
            int generation = ++_comparisonMetricGeneration;
            ReleaseComparisonView(); Children.Clear();
            var status = SessionText("Changing comparison metric… Waiting for previous readers, then recomputing recorded observations. Query, ranking, focus and chart are reset.");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); Children.Add(status); Grid.SetRowSpan(status, 5);
            try
            {
                await Model.ChangeComparisonMetricAsync(metric, cancel.Token);
                if (_disposed || cancel.IsCancellationRequested || generation != _comparisonMetricGeneration) return;
                Children.Clear(); ShowComparison(Model.Comparison!);
            }
            catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or InvalidDataException or OverflowException)
            {
                if (_disposed || generation != _comparisonMetricGeneration) return;
                Children.Clear(); var failure = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
                failure.Children.Add(SessionText("Comparison metric was not applied. " + AnalysisNames.Display(error.Message, 400)));
                failure.Children.Add(ActionButton("Retry comparison metric", () => _ = ChangeComparisonMetricAsync(metric), "Retry with the same captured inputs. No filesystem source is opened."));
                if (Model.Comparison is { } previous)
                    failure.Children.Add(ActionButton("Restore previous comparison metric", () => _ = ChangeComparisonMetricAsync(previous.Metric), "Rebuild the previous metric from the retained captured inputs."));
                Children.Add(failure); Grid.SetRowSpan(failure, 5);
            }
            finally { if (ReferenceEquals(_comparisonMetricCancel, cancel)) _comparisonMetricCancel = null; }
        }

        private void AddComparisonCharts(StackPanel heading, ListBox rows, Grid panel)
        {
            var mode = new ComboBox { ItemsSource = new[] { "Table", "Treemap + table", "Sunburst + table" }, SelectedIndex = 0, MinWidth = 160 };
            var side = new ComboBox { ItemsSource = new[] { "Current observed amounts", "Previous observed amounts (includes removed)" }, SelectedIndex = 0, MinWidth = 190 };
            AutomationProperties.SetName(mode, "Comparison chart"); AutomationProperties.SetName(side, "Comparison chart side");
            var options = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { mode, side } }; heading.Children.Add(options);
            var host = _comparisonChartHost = new StackPanel { Spacing = 6, Margin = new Thickness(10, 4), IsVisible = false };
            Grid.SetRow(host, 1); panel.Children.Add(host);
            var caption = SessionText(""); AutomationProperties.SetLiveSetting(caption, AutomationLiveSetting.Polite);
            var navigation = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
            var treemap = _comparisonTreemap = new TreemapControl { Height = 260 };
            var sunburst = _comparisonSunburst = new SunburstControl { Height = 260 };
            var reveal = ActionButton("Show selected chart", () => RevealChart(), "Scroll to the selected recorded chart. The complete table remains below it.");
            reveal.IsVisible = false; options.Children.Add(reveal);
            var legend = new AnalysisColorLegend();
            host.Children.Add(caption); host.Children.Add(navigation); host.Children.Add(treemap); host.Children.Add(sunburst); host.Children.Add(legend);
            string scope = AnalysisTree.RootKey; string? focus = null;
            void Draw()
            {
                if (_comparisonChartData is not { } data) return;
                treemap.IsVisible = mode.SelectedIndex == 1; sunburst.IsVisible = mode.SelectedIndex == 2;
                foreach (var chart in new AnalysisChartControl[] { treemap, sunburst })
                    chart.SetView(data, scope, focus, ImmutableHashSet<ulong>.Empty, AnalysisColorDimension.Growth);
                legend.SetView(AnalysisColorDimension.Growth, data.ObservedUtc);
                caption.Text = (data.Previous ? "Previous" : "Current") + " recorded amounts · " + ComparisonMetric(data.Metric) + " · " + data.NameOf(scope) +
                    ". Full captured hierarchy; table filters apply to the table. Removed area is shown on Previous only.";
                navigation.Children.Clear();
                navigation.Children.Add(ActionButton("All comparison locations", () => { scope = AnalysisTree.RootKey; focus = null; Draw(); }, "Show all locations on this recorded side."));
                navigation.Children.Add(ActionButton("Up comparison chart", () => { scope = data.ParentOf(scope); focus = null; Draw(); }, "Show the captured parent without opening a source."));
                navigation.Children.Add(ActionButton("Show comparison table", () => rows.BringIntoView(), "Scroll to the complete accounting table, including zero and unavailable amounts."));
            }
            void Activate(string key, bool toggle, bool drill)
            {
                if (_comparisonChartData is not { } data) return;
                focus = key;
                if (data.RowFor(key) is { } row)
                {
                    if (rows.Items.Contains(row)) { rows.SelectedItem = row; rows.ScrollIntoView(row); }
                    if (drill && !data.CanExplore(key)) _ = InspectComparisonAsync(row);
                }
                if (drill && data.CanExplore(key)) { scope = key; focus = null; }
                Draw();
                if (data.RowFor(key) is { } hidden && !rows.Items.Contains(hidden))
                    caption.Text += " Selected entry is hidden by table filters; double-activate it to inspect, or clear filters to reveal it.";
            }
            foreach (var chart in new AnalysisChartControl[] { treemap, sunburst })
            {
                chart.Activated += Activate;
                chart.InteractionChanged += active => { mode.IsEnabled = side.IsEnabled = !active; };
            }
            rows.SelectionChanged += (_, _) =>
            {
                if (_comparisonChartData is not { } data) return;
                focus = rows.SelectedItem is AnalysisComparisonRow row ? data.FocusKey(row) : null; Draw();
            };
            void Changed()
            {
                bool previous = side.SelectedIndex == 1; int selectedMode = mode.SelectedIndex;
                host.IsVisible = selectedMode > 0;
                reveal.IsVisible = selectedMode > 0;
                if (selectedMode > 0 && _comparisonChartData is { } existing && existing.Previous == previous) { Draw(); RevealChart(); return; }
                _comparisonChartCancel?.Cancel(); int generation = ++_comparisonChartGeneration;
                treemap.Clear(); sunburst.Clear(); _comparisonChartData?.Dispose(); _comparisonChartData = null;
                if (selectedMode == 0) return;
                caption.Text = "Preparing recorded comparison hierarchy…"; scope = AnalysisTree.RootKey; focus = null;
                Task preceding = _comparisonChartWork;
                _comparisonChartWork = Build();
                async Task Build()
                {
                    using var actualWork = _actualWork.Enter(); using var cancel = new CancellationTokenSource(); _comparisonChartCancel = cancel;
                    AnalysisComparisonChart? data = null;
                    try
                    {
                        await preceding;
                        preceding = Task.CompletedTask;
                        if (_disposed || cancel.IsCancellationRequested || generation != _comparisonChartGeneration) return;
                        data = await Model.BuildComparisonChartAsync(previous, cancel.Token);
                        if (_disposed || cancel.IsCancellationRequested || generation != _comparisonChartGeneration) return;
                        _comparisonChartData = data; data = null; Draw(); RevealChart();
                    }
                    catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or InvalidDataException or OverflowException)
                    { if (!_disposed && generation == _comparisonChartGeneration) caption.Text = "Chart unavailable. " + AnalysisNames.Display(error.Message, 400) + " The comparison table remains available."; }
                    finally { data?.Dispose(); if (ReferenceEquals(_comparisonChartCancel, cancel)) _comparisonChartCancel = null; }
                }
            }
            void RevealChart()
            {
                int generation = _comparisonChartGeneration;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || generation != _comparisonChartGeneration || _comparisonChartData is null || !host.IsVisible) return;
                    (mode.SelectedIndex == 1 ? (Control)treemap : sunburst).BringIntoView();
                }, DispatcherPriority.Background);
            }
            mode.SelectionChanged += (_, _) => Changed(); side.SelectionChanged += (_, _) => Changed();
        }

        private void RetireComparisonChart()
        {
            _comparisonChartCancel?.Cancel(); ++_comparisonChartGeneration;
            _comparisonTreemap?.Clear(); _comparisonSunburst?.Clear(); _comparisonChartData?.Dispose(); _comparisonChartData = null;
            _comparisonTreemap = null; _comparisonSunburst = null; _comparisonChartHost = null;
            if (_disposed) _comparisonMetricCancel?.Cancel();
        }
    }
}
