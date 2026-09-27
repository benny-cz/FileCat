using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>TV-01 headless UI smoke at 10k; set FILECAT_UI_SCALE_COUNT=1000000 for the full fixture.</summary>
public sealed class UiScaleTests
{
    [AvaloniaFact]
    public async Task Four_panels_render_and_navigate_large_synthetic_listings()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("FILECAT_UI_SCALE_COUNT"), out int count)) count = 10_000;
        Assert.InRange(count, 1, 2_000_000);
        string root = Path.Combine(Path.GetTempPath(), "filecat-ui-scale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            services.Providers.Register(new ScaleProvider(count));
            var workspace = new WorkspaceViewModel(services);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var tabs = new List<TabViewModel>();
            var lists = new List<FileListControl>();
            for (int i = 0; i < 4; i++)
            {
                var panel = new PanelViewModel(workspace, services);
                workspace.Panels.Add(panel);
                var tab = panel.OpenTab(Location.FileSystem(Path.Combine(root, "synthetic-" + i)));
                var list = new FileListControl { Tab = tab };
                Grid.SetColumn(list, i);
                grid.Children.Add(list);
                tabs.Add(tab);
                lists.Add(list);
            }
            var window = new Window { Width = 1920, Height = 900, Content = grid };
            window.Show();
            try
            {
                var clock = Stopwatch.StartNew();
                var process = Process.GetCurrentProcess();
                long firstRowsMs = -1, peakPrivate = 0, peakManaged = 0;
                while (tabs.Any(t => t.Listing.State == ListingState.Loading))
                {
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                    if (firstRowsMs < 0 && tabs.All(t => t.Listing.VisibleCount > 0)) firstRowsMs = clock.ElapsedMilliseconds;
                    process.Refresh();
                    peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                    peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                    Assert.True(clock.Elapsed < TimeSpan.FromMinutes(10), "UI scale listing timed out.");
                }
                window.CaptureRenderedFrame();
                foreach (var (tab, list) in tabs.Zip(lists))
                {
                    Assert.Null(tab.Listing.Error);
                    Assert.Equal(count, tab.Listing.VisibleCount);
                    tab.Listing.SetFocus(count - 1);
                    list.EnsureFocusVisible();
                    tab.Listing.SetMark(count - 1, true);
                    Assert.True(tab.Listing.IsVisibleMarked(count - 1));
                }
                window.CaptureRenderedFrame();
                process.Refresh();
                TestContext.Current.TestOutputHelper?.WriteLine($"TV-01 headless UI: count={count} panels=4 first_rows_ms={firstRowsMs} complete_ms={clock.ElapsedMilliseconds} peak_private_mib={peakPrivate / 1048576.0:F1} peak_managed_mib={peakManaged / 1048576.0:F1} spill_mib={tabs.Sum(t => t.Listing.Store.SpillBytes) / 1048576.0:F1}");
            }
            finally
            {
                window.Close();
                foreach (var tab in tabs) tab.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class ScaleProvider(int count) : FileCat.Core.Resources.ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            var batch = new EntryData[512];
            for (int from = 0; from < count; from += batch.Length)
            {
                ct.ThrowIfCancellationRequested();
                int n = Math.Min(batch.Length, count - from);
                for (int i = 0; i < n; i++)
                {
                    int sequence = count - from - i;
                    batch[i] = new EntryData($"file-{sequence:0000000}-long-αβγ.txt", EntryKind.File, sequence);
                }
                sink.AddBatch(batch.AsSpan(0, n));
            }
            return Task.CompletedTask;
        }
    }
}
