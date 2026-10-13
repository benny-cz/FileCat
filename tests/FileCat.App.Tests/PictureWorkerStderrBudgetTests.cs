using System.Diagnostics;
using System.Reflection;
using FileCat.App.Services;

namespace FileCat.App.Tests;

[CollectionDefinition("Picture worker stderr budget", DisableParallelization = true)]
public sealed class PictureWorkerStderrBudgetCollection;

[Collection("Picture worker stderr budget")]
public sealed class PictureWorkerStderrBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Unix_worker_discards_a_newline_free_stderr_stream_without_retaining_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows picture workers use sandbox-owned pipes.");
        const long emitted = 32L * 1024 * 1024;
        const long maximumRetained = 12L * 1024 * 1024;
        Type workerType = typeof(PictureDecoder).GetNestedType("Worker", BindingFlags.NonPublic)!;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        object worker = workerType.GetMethod("Start")!.Invoke(null,
            ["/bin/sh", "-c \"/usr/bin/head -c 33554432 /dev/zero | /usr/bin/tr '\\000' X >&2; echo ready; read ignored\""])!;
        try
        {
            var process = (Process)workerType.GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(worker)!;
            using var reader = new StreamReader((Stream)workerType.GetProperty("Output")!.GetValue(worker)!, leaveOpen: true);
            string? ready = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Equal("ready", ready);
            Assert.False(process.HasExited);
            long after = GC.GetTotalMemory(forceFullCollection: true);
            long retained = after - before;
            output.WriteLine($"PICTURE_STDERR_BUDGET emittedBytes={emitted} retainedManagedBytes={retained} maximumRetainedBytes={maximumRetained} rootAlive={!process.HasExited}");
            Assert.InRange(retained, 0, maximumRetained);
        }
        finally
        {
            await ((IAsyncDisposable)worker).DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }
}
