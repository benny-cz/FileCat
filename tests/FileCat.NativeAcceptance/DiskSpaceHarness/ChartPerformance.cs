using System.Collections.Immutable;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using FileCat.App.Controls.DiskSpace;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private sealed record ChartFrameTiming(string Chart, int Rows, int Folders, double Width, double Height, double Scale,
        double[] FocusFramesMilliseconds, double[] LayoutFramesMilliseconds,
        double P95FocusFrameMilliseconds, double P95LayoutFrameMilliseconds);

    private async Task<ChartFrameTiming[]> MeasureChartFramesAsync(bool hierarchical, CancellationToken cancellation)
    {
        const int sampleCount = 30;
        int folders = hierarchical ? 8 : 0;
        int rows = hierarchical ? folders * 512 : 700;
        var baseline = new ChartTimingData(rows, folders, "initial");
        var alternate = new ChartTimingData(rows, folders, "refreshed");
        var results = new List<ChartFrameTiming>();
        var window = new Window { Title = "FileCat chart performance fixture", Width = 1100, Height = 700 };
        try
        {
            window.Show(_window!);
            foreach (AnalysisChartControl chart in new AnalysisChartControl[] { new TreemapControl(), new SunburstControl() })
            {
                window.Content = chart;
                chart.SetView(baseline, AnalysisTree.RootKey, null, []);
                await ReadyAsync(() => chart.IsEffectivelyVisible && chart.Bounds.Width > 0 && chart.Bounds.Height > 0);
                var size = chart.Bounds.Size;
                double scale = window.RenderScaling;
                var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
                using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
                bitmap.Render(chart);
                var focus = new double[sampleCount];
                var layout = new double[sampleCount];
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var selected = baseline.Leaves[sample].Key;
                    long start = Stopwatch.GetTimestamp();
                    chart.SetView(baseline, AnalysisTree.RootKey, selected, []);
                    bitmap.Render(chart);
                    focus[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                    start = Stopwatch.GetTimestamp();
                    chart.SetView(alternate, AnalysisTree.RootKey, selected, []);
                    bitmap.Render(chart);
                    layout[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                    // Establish a stable layout before timing the next focus-only update.
                    chart.SetView(baseline, AnalysisTree.RootKey, null, []);
                    bitmap.Render(chart);
                }
                results.Add(new(chart.GetType().Name, rows, folders, size.Width, size.Height, scale, focus, layout,
                    Percentile95(focus), Percentile95(layout)));
            }
        }
        finally { window.Close(); }
        return results.ToArray();

        static double Percentile95(double[] values) => values.Order().ElementAt((int)Math.Ceiling(values.Length * .95) - 1);
    }

    private sealed class ChartTimingData : IAnalysisChartData
    {
        private const uint FileSize = 4096;
        private readonly ImmutableDictionary<string, ImmutableArray<AnalysisTreeItem>> _children;

        public ChartTimingData(int rows, int folders, string prefix)
        {
            var items = ImmutableDictionary.CreateBuilder<string, AnalysisTreeItem>(StringComparer.Ordinal);
            var children = ImmutableDictionary.CreateBuilder<string, ImmutableArray<AnalysisTreeItem>>(StringComparer.Ordinal);
            var leaves = ImmutableArray.CreateBuilder<AnalysisTreeItem>(rows);
            var roots = ImmutableArray.CreateBuilder<AnalysisTreeItem>(folders);
            ulong id = (ulong)folders + 1;
            int groups = Math.Max(folders, 1);
            int filesPerGroup = rows / groups;
            for (int group = 0; group < groups; group++)
            {
                ulong folderId = (ulong)group + 1;
                string key = folders == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(folderId);
                var files = ImmutableArray.CreateBuilder<AnalysisTreeItem>(filesPerGroup);
                for (int file = 0; file < filesPerGroup; file++, id++)
                {
                    var leaf = new AnalysisTreeItem(AnalysisTree.Key(id), key, $"{prefix}-{id:D4}.bin", id,
                        AnalysisKind.File, false, true, FileSize, FileSize, 1, false, false, "other", [], [id]);
                    items.Add(leaf.Key, leaf);
                    files.Add(leaf);
                    leaves.Add(leaf);
                }
                children.Add(key, files.MoveToImmutable());
                if (folders == 0) continue;
                UInt128 value = (UInt128)filesPerGroup * FileSize;
                var folder = new AnalysisTreeItem(key, AnalysisTree.RootKey, $"{prefix} folder {folderId}", folderId,
                    AnalysisKind.Directory, false, true, value, value, (ulong)filesPerGroup, false, false, "folder",
                    [.. children[key].Select(file => file.Key)], []);
                items.Add(key, folder);
                roots.Add(folder);
            }
            if (folders > 0) children.Add(AnalysisTree.RootKey, roots.MoveToImmutable());
            Leaves = leaves.MoveToImmutable();
            Items = items.ToImmutable();
            _children = children.ToImmutable();
        }

        public ImmutableArray<AnalysisTreeItem> Leaves { get; }
        public AnalysisMetric Metric => AnalysisMetric.FileSize;
        public DateTimeOffset ObservedUtc => DateTimeOffset.UnixEpoch;
        public ImmutableDictionary<string, AnalysisTreeItem> Items { get; }
        public string ParentOf(string key) => Items.GetValueOrDefault(key)?.ParentKey ?? AnalysisTree.RootKey;
        public string NameOf(string key) => Items.GetValueOrDefault(key)?.Name ?? "Chart performance fixture";
        public UInt128 ScopeValue(string key) => key == AnalysisTree.RootKey ? (UInt128)Leaves.Length * FileSize : Items[key].Value;
        public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key) => _children.TryGetValue(key, out var children) ? children : [];
    }
}
