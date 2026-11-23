using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// X-THREAD (FILECAT_CLOUD_PLACEHOLDER_EXPOSURE_PLAN.md): exposing Cloud Files placeholders changes only the calling
/// thread's mode, for the duration of the read. It restores the previous mode after success, failure and nesting, and
/// leaves other threads and the process mode as they were.
/// </summary>
public sealed partial class PlaceholderViewTests
{
    [LibraryImport("ntdll.dll")]
    private static partial sbyte RtlQueryProcessPlaceholderCompatibilityMode();

    [Fact]
    public void Exposure_is_scoped_to_the_calling_thread_and_always_restored()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && PlaceholderView.Available, "Windows placeholder compatibility modes.");
        sbyte process = RtlQueryProcessPlaceholderCompatibilityMode();
        sbyte before = PlaceholderView.ThreadMode();

        Assert.Equal(2, PlaceholderView.Exposed(() => PlaceholderView.ThreadMode()));
        Assert.Equal(before, PlaceholderView.ThreadMode());

        Assert.Throws<InvalidOperationException>(() => PlaceholderView.Exposed<int>(() => throw new InvalidOperationException("read failed")));
        Assert.Equal(before, PlaceholderView.ThreadMode());

        PlaceholderView.Exposed(() =>
        {
            PlaceholderView.Exposed(() => 0);
            Assert.Equal(2, PlaceholderView.ThreadMode()); // the inner scope restored the outer exposed mode
            sbyte other = -9;
            var thread = new Thread(() => other = PlaceholderView.ThreadMode());
            thread.Start();
            thread.Join();
            Assert.Equal(0, other); // another thread follows the process mode
            Assert.Equal(process, RtlQueryProcessPlaceholderCompatibilityMode());
            return 0;
        });
        Assert.Equal(before, PlaceholderView.ThreadMode());
        Assert.Equal(process, RtlQueryProcessPlaceholderCompatibilityMode());
    }

    [Fact]
    public void Pool_threads_leave_with_the_mode_they_arrived_with_after_failure_and_cancellation()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && PlaceholderView.Available, "Windows placeholder compatibility modes.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var results = Enumerable.Range(0, 64).AsParallel().WithDegreeOfParallelism(8).Select(i =>
        {
            sbyte arrived = PlaceholderView.ThreadMode();
            try
            {
                PlaceholderView.Exposed<int>(() =>
                {
                    if (i % 2 == 0) throw new OperationCanceledException(canceled.Token);
                    throw new IOException("listing failed");
                });
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
            return (arrived, left: PlaceholderView.ThreadMode());
        }).ToList();
        Assert.All(results, r => Assert.Equal(r.arrived, r.left));
    }

    [Fact]
    public void Without_the_export_the_read_keeps_the_view_the_thread_has()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows placeholder compatibility modes.");
        sbyte before = PlaceholderView.ThreadMode();
        Assert.Equal(before, PlaceholderView.Exposed(PlaceholderView.ThreadMode, available: false));
        Assert.Equal(before, PlaceholderView.ThreadMode());
    }
}
