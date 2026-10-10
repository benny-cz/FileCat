using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>A stopped Unix decoder must also stop an owned child that is still attached to it.</summary>
public sealed class PictureWorkerProcessLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopping_a_Unix_worker_stops_its_live_child(bool dispose)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix worker process-tree lifetime; Windows uses a one-process job."); return; }
        Type type = typeof(PictureDecoder).GetNestedType("Worker", BindingFlags.NonPublic)!;
        // The shell stays in a built-in read while its bounded child sleeps. Its output streams are separate,
        // so EOF or an inherited pipe cannot masquerade as process-lifetime cleanup.
        object worker = type.GetMethod("Start")!.Invoke(null,
            ["/bin/sh", "-c \"sleep 90 </dev/null >/dev/null 2>&1 & echo $!; read ignored\""])!;
        Process? child = null;
        Task? disposing = null;
        try
        {
            using var reader = new StreamReader((Stream)type.GetProperty("Output")!.GetValue(worker)!, leaveOpen: true);
            string pid = (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))!;
            child = Process.GetProcessById(int.Parse(pid, CultureInfo.InvariantCulture));
            Assert.True(Running(child));
            if (dispose) disposing = ((IAsyncDisposable)worker).DisposeAsync().AsTask();
            else type.GetMethod("Kill")!.Invoke(worker, null);
            var clock = Stopwatch.StartNew();
            while (Running(child) && clock.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.False(Running(child));
            await (disposing ??= ((IAsyncDisposable)worker).DisposeAsync().AsTask())
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            // Failure cleanup is independent of the product's stop path; it owns only this child.
            if (child is not null)
            {
                if (Running(child)) child.Kill();
                child.Dispose();
            }
            await (disposing ??= ((IAsyncDisposable)worker).DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static bool Running(Process process)
    {
        process.Refresh();
        if (process.HasExited) return false;
        if (OperatingSystem.IsLinux())
        {
            try
            {
                string stat = File.ReadAllText($"/proc/{process.Id}/stat");
                // A dead child can await PID 1's reap; it no longer runs or retains a worker's authority.
                return stat[stat.LastIndexOf(')') + 2] != 'Z';
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        return true;
    }
}
