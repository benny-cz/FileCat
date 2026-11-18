using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private sealed record WarmShellTiming(double FirstManagerOpenMilliseconds, double[] WarmOpenMilliseconds,
        double P95WarmOpenMilliseconds);

    private async Task<WarmShellTiming> MeasureWarmShellAsync(MainViewModel main, CancellationToken cancellation)
    {
        var manager = _services!.DiskSpace;
        if (manager.Window is not null)
            throw new InvalidOperationException("The shell measurement requires its own window manager.");
        var samples = new double[21];
        for (int i = 0; i < samples.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var elapsed = Stopwatch.StartNew();
            main.OpenDiskSpace([_fixture], fromTools: true);
            var window = manager.Window ?? throw new InvalidOperationException("The Tools entry did not open Disk space.");
            try
            {
                samples[i] = await WaitForNativeLayoutAsync(elapsed, () => window.IsVisible &&
                    window.GetVisualDescendants().OfType<Button>().Any(button =>
                        button.Content is "Start analysis" && button.IsEffectivelyVisible && button.IsEnabled &&
                        button.Bounds.Width > 0 && button.Bounds.Height > 0), "the analysis setup", cancellation);
            }
            finally
            {
                window.ForceClose();
                await window.WaitForIndexedCloseAsync();
            }
            if (manager.Window is not null)
                throw new InvalidOperationException("The measured analysis shell did not close.");
        }
        var warm = samples[1..];
        return new(samples[0], warm, warm.Order().ElementAt((int)Math.Ceiling(warm.Length * .95) - 1));
    }

    private static async Task<double> WaitForNativeLayoutAsync(Stopwatch elapsed, Func<bool> ready, string target, CancellationToken cancellation)
    {
        while (!await Dispatcher.UIThread.InvokeAsync(ready, DispatcherPriority.Background))
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException($"The native window did not lay out {target}.");
            await Task.Delay(5, cancellation);
        }
        return elapsed.Elapsed.TotalMilliseconds;
    }
}
