using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls.DiskSpace;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedComparisonView
    {
        private readonly ComboBox _chartChoice = new() { ItemsSource = new[] { "Table", "Treemap + table", "Sunburst + table" }, SelectedIndex = 0 };
        private readonly ComboBox _chartSide = new() { ItemsSource = new[] { "Current observed amounts", "Previous observed amounts (includes removed)" }, SelectedIndex = 0 };
        private readonly ComboBox _chartRoot = new() { MinWidth = 180 };
        private readonly StackPanel _chartHost = new() { Spacing = 6, IsVisible = false };
        private readonly TextBlock _chartCaption = Text("");
        private readonly TreemapControl _chartTreemap = new() { Height = 280 };
        private readonly SunburstControl _chartSunburst = new() { Height = 280 };
        private readonly AnalysisColorLegend _chartLegend = new();
        private Button _chartApply = null!, _chartAll = null!, _chartUp = null!, _chartOpenRoot = null!;
        private ImmutableArray<(ulong Id, string Label)> _chartRoots = [];
        private AnalysisIndexedComparison.Chart? _chartOwner;
        private AnalysisIndexedComparisonChartProjection? _chartProjection;
        private string? _chartFocus;
        private int _chartMode;
        private bool _chartInteracting;

        private void AddCharts()
        {
            AutomationProperties.SetName(_chartChoice, "Comparison chart"); AutomationProperties.SetName(_chartSide, "Comparison chart side");
            AutomationProperties.SetName(_chartRoot, "Recorded comparison chart location");
            AutomationProperties.SetName(_chartCaption, "Comparison chart scope");
            AutomationProperties.SetLiveSetting(_chartCaption, AutomationLiveSetting.Polite);
            _chartApply = Button("Apply comparison chart", () => _ = ApplyChartAsync(), "Show Table, Treemap or Sunburst for the selected captured side. Area is its observed amount, never a signed difference.");
            _chartAll = Button("All comparison locations", () => _ = NavigateChartAsync(AnalysisTree.RootKey), "Show the full captured hierarchy on the applied chart side.");
            _chartUp = Button("Up comparison chart", () => { if (_chartProjection is { } data) _ = NavigateChartAsync(data.ParentOf(data.Scope)); }, "Open the recorded parent without opening a filesystem source.");
            _chartOpenRoot = Button("Open comparison location", () =>
            {
                if (_chartRoot.SelectedIndex is var index && index >= 0 && index < _chartRoots.Length) _ = NavigateChartAsync(AnalysisTree.Key(_chartRoots[index].Id));
            }, "Explore one recorded location. Incompatible On disk component bases have separate charts.");
            Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _chartChoice, _chartSide, _chartApply } });
            _chartHost.Children.Add(_chartCaption);
            _chartHost.Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _chartAll, _chartUp, _chartRoot, _chartOpenRoot } });
            _chartHost.Children.Add(_chartTreemap); _chartHost.Children.Add(_chartSunburst); _chartHost.Children.Add(_chartLegend); Children.Add(_chartHost);
            foreach (var chart in new AnalysisChartControl[] { _chartTreemap, _chartSunburst })
            {
                chart.Activated += (key, toggle, drill) => _ = ActivateChartAsync(key, drill);
                chart.InteractionChanged += active => { _chartInteracting = active; Refresh(); };
            }
        }
        private Task ApplyChartAsync() => RunAsync("Preparing the captured comparison chart…", async token =>
        {
            int mode = _chartChoice.SelectedIndex;
            if (mode is < 0 or > 2 || _chartSide.SelectedIndex is < 0 or > 1) throw new ArgumentException("Choose a chart and recorded side.");
            if (mode == 0) { await CloseChartAsync(); _chartMode = 0; return; }
            if (_result is null) return;
            bool previous = _chartSide.SelectedIndex == 1;
            if (_chartOwner is { } current && current.Previous == previous)
            { _chartMode = mode; DrawChart(); return; }
            // One chart place includes both indexes and real close. Keep the old immutable
            // image until replacement is ready, but retire its reader before rebuilding.
            var old = _chartOwner; _chartOwner = null;
            if (old is not null) await old.DisposeAsync();
            AnalysisIndexedComparison.Chart? replacement = null;
            try
            {
                replacement = await _result.BuildChartAsync(previous, token);
                var roots = await replacement.ReadRootsAsync(token);
                if (!replacement.CombinedAreaAvailable && roots.Length == 0) throw new InvalidOperationException("No compatible recorded directory chart is available. Use the complete accounting table.");
                var projection = await replacement.LoadAsync(replacement.CombinedAreaAvailable ? AnalysisTree.RootKey : AnalysisTree.Key(roots[0].Id), token); token.ThrowIfCancellationRequested();
                _chartOwner = replacement; replacement = null; _chartProjection = projection; _chartFocus = null; _chartMode = mode; DrawChart();
                _chartRoots = roots; _chartRoot.ItemsSource = roots.Select(root => root.Label).ToArray(); _chartRoot.SelectedIndex = roots.Length == 0 ? -1 : 0;
            }
            finally { if (replacement is not null) await replacement.DisposeAsync(); }
        });
        private Task NavigateChartAsync(string scope) => RunAsync("Reading the recorded chart scope…", async token =>
        {
            if (_chartOwner is null) return;
            var projection = await _chartOwner.LoadAsync(scope, token); token.ThrowIfCancellationRequested();
            _chartProjection = projection; _chartFocus = null; DrawChart();
        });
        private Task ActivateChartAsync(string key, bool drill) => RunAsync("Reading the recorded chart entry…", async token =>
        {
            if (_chartOwner is null || _chartProjection is not { } data) return;
            if (drill && data.CanExplore(key))
            {
                var projection = await _chartOwner.LoadAsync(key, token); token.ThrowIfCancellationRequested();
                _chartProjection = projection; _chartFocus = null; DrawChart(); return;
            }
            _chartFocus = key; DrawChart();
            if (!data.Items.TryGetValue(key, out var item)) return;
            var row = await _chartOwner.RowForAsync(item, token); token.ThrowIfCancellationRequested();
            if (row is null) return;
            var visible = _page.Rows.FirstOrDefault(candidate => candidate.Row.Id == row.Id);
            _publishing = true;
            try { _rows.SelectedItem = visible; if (visible is not null) _rows.ScrollIntoView(visible); }
            finally { _publishing = false; }
            await InspectRowAsync(row, token);
            if (visible is null) _chartCaption.Text += " Selected accounting row is outside the current table page or applied table filter; both captured sides are shown in the inspector.";
        });
        private void DrawChart()
        {
            if (_chartProjection is not { } data || _chartMode == 0) { _chartHost.IsVisible = false; return; }
            _chartHost.IsVisible = true; _chartTreemap.IsVisible = _chartMode == 1; _chartSunburst.IsVisible = _chartMode == 2;
            foreach (var chart in new AnalysisChartControl[] { _chartTreemap, _chartSunburst })
                chart.SetView(data, data.Scope, _chartFocus, ImmutableHashSet<ulong>.Empty, AnalysisColorDimension.Growth);
            _chartLegend.SetView(AnalysisColorDimension.Growth, data.ObservedUtc);
            _chartCaption.Text = (data.Previous ? "Previous" : "Current") + " recorded amounts · " + _metric.Items[(int)data.Metric] + " · " + data.NameOf(data.Scope) +
                ". Full captured hierarchy; query and change-cohort filters apply to the table. Removed area is shown on Previous only. Unknown amounts have no invented area." +
                (data.DetailLimited ? " Small entries are aggregated; open Other small items or use the complete table." : "");
            if (_chartOwner is { CombinedAreaAvailable: false }) _chartCaption.Text += " Combined On disk area is unavailable because these locations report different components. Choose one recorded location.";
        }
        private void FocusChart(AnalysisIndexedComparisonRow row, AnalysisDatasetPage<AnalysisPathMetadata>? before, AnalysisDatasetPage<AnalysisPathMetadata>? after)
        {
            if (_chartProjection is not { } chart) return;
            var entity = chart.Previous ? row.Previous : row.Current; var page = chart.Previous ? before : after;
            string? key = page?.Rows.FirstOrDefault() is { } path ? AnalysisTree.Key(path.Id) : null;
            if (entity is { ObservedPaths: > 1, AttributionParent: { } parent } && chart.Metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
            {
                string shared = AnalysisGeometryOrder.BucketKey(parent, AnalysisHierarchyBucketKind.SharedFiles);
                if (chart.Items.ContainsKey(shared)) key = shared;
            }
            _chartFocus = key is not null && chart.Items.ContainsKey(key) ? key : null; DrawChart();
        }
        private void RefreshCharts(bool usable)
        {
            bool enabled = usable && _result is not null && !_chartInteracting;
            _chartChoice.IsEnabled = _chartSide.IsEnabled = _chartApply.IsEnabled = enabled;
            _chartAll.IsEnabled = enabled && _chartOwner is { CombinedAreaAvailable: true };
            _chartUp.IsEnabled = enabled && _chartOwner is not null && _chartProjection is { } data && data.Scope != AnalysisTree.RootKey &&
                (_chartOwner.CombinedAreaAvailable || data.ParentOf(data.Scope) != AnalysisTree.RootKey);
            _chartRoot.IsEnabled = _chartOpenRoot.IsEnabled = enabled && _chartOwner is not null && _chartRoots.Length > 0;
            _chartTreemap.IsEnabled = _chartSunburst.IsEnabled = usable && _chartOwner is not null;
        }
        private async Task CloseChartAsync()
        {
            _chartTreemap.Clear(); _chartSunburst.Clear(); _chartProjection = null; _chartFocus = null; _chartHost.IsVisible = false; _chartRoots = []; _chartRoot.ItemsSource = null;
            var chart = _chartOwner; _chartOwner = null; if (chart is not null) await chart.DisposeAsync();
        }
    }
}
