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
    private sealed record ChartFrameTiming(string Chart, int Rows, double Width, double Height, double Scale,
        double[] FocusFramesMilliseconds, double[] LayoutFramesMilliseconds,
        double P95FocusFrameMilliseconds, double P95LayoutFrameMilliseconds);

    private async Task<ChartFrameTiming[]> MeasureChartFramesAsync(CancellationToken cancellation)
    {
        const int rows = 700;
        const int sampleCount = 30;
        var baseline = new ChartTimingData(rows, "initial");
        var alternate = new ChartTimingData(rows, "refreshed");
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
                    var selected = baseline.Children[sample].Key;
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
                results.Add(new(chart.GetType().Name, rows, size.Width, size.Height, scale, focus, layout,
                    Percentile95(focus), Percentile95(layout)));
            }
        }
        finally { window.Close(); }
        return results.ToArray();

        static double Percentile95(double[] values) => values.Order().ElementAt((int)Math.Ceiling(values.Length * .95) - 1);
    }

    private sealed class ChartTimingData : IAnalysisChartData
    {
        public ChartTimingData(int rows, string prefix)
        {
            var children = ImmutableArray.CreateBuilder<AnalysisTreeItem>(rows);
            for (ulong id = 1; id <= (ulong)rows; id++)
                children.Add(new(AnalysisTree.Key(id), AnalysisTree.RootKey, $"{prefix}-{id:D4}.bin", id,
                    AnalysisKind.File, false, true, 4096, 4096, 1, false, false, "other", [], [id]));
            Children = children.MoveToImmutable();
            Items = Children.ToImmutableDictionary(item => item.Key, StringComparer.Ordinal);
        }

        public ImmutableArray<AnalysisTreeItem> Children { get; }
        public AnalysisMetric Metric => AnalysisMetric.FileSize;
        public DateTimeOffset ObservedUtc => DateTimeOffset.UnixEpoch;
        public ImmutableDictionary<string, AnalysisTreeItem> Items { get; }
        public string ParentOf(string key) => AnalysisTree.RootKey;
        public string NameOf(string key) => Items.GetValueOrDefault(key)?.Name ?? "Chart performance fixture";
        public UInt128 ScopeValue(string key) => key == AnalysisTree.RootKey ? (UInt128)Children.Length * 4096 : Items[key].Value;
        public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key) => key == AnalysisTree.RootKey ? Children : [];
    }
}
