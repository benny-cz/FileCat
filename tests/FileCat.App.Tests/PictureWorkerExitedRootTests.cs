using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>Root-process retirement cannot depend on EOF from a separately owned descendant.</summary>
public sealed class PictureWorkerExitedRootTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Exited_root_disposal_does_not_wait_for_child_stderr(bool retainError, bool killFirst)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix stderr EOF lifetime; Windows uses job-owned pipes."); return; }
        string root = Path.Combine(Path.GetTempPath(), "filecat-exited-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string release = Path.Combine(root, "release");
        string childScript = Path.Combine(root, "child.sh");
        string rootScript = Path.Combine(root, "root.sh");
        // An independent finite watchdog only bounds failed-fixture cleanup. Success requires retirement
        // while the owned child is still running and before the fixture releases its inherited stderr.
        await File.WriteAllTextAsync(childScript, "i=0\nwhile [ ! -f \"$1/release\" ] && [ $i -lt 1000 ]; do i=$((i+1)); sleep 0.02; done\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(rootScript, "/bin/sh \"$1/child.sh\" \"$1\" </dev/null >/dev/null " + (retainError ? "" : "2>/dev/null ") + "&\necho $!\nexit 0\n", TestContext.Current.CancellationToken);
        Type type = typeof(PictureDecoder).GetNestedType("Worker", BindingFlags.NonPublic)!;
        object? worker = null;
        Process? child = null;
        Task? disposing = null;
        try
        {
            worker = type.GetMethod("Start")!.Invoke(null, ["/bin/sh", $"\"{rootScript}\" \"{root}\""])!;
            var process = (Process)type.GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(worker)!;
            using var reader = new StreamReader((Stream)type.GetProperty("Output")!.GetValue(worker)!, leaveOpen: true);
            string pid = (await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))!;
            child = Process.GetProcessById(int.Parse(pid, CultureInfo.InvariantCulture));
            long childStart = child.StartTime.ToUniversalTime().Ticks;
            var clock = Stopwatch.StartNew();
            while (!process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(process.HasExited);
            Assert.Equal(0, process.ExitCode);
            Assert.False(child.HasExited);
            output.WriteLine($"RootPID={process.Id}; ChildPID={child.Id}; ChildStartTicks={childStart}; RootExited=True; RetainsError={retainError}; KillFirst={killFirst}");
            if (killFirst) type.GetMethod("Kill")!.Invoke(worker, null);
            disposing = ((IAsyncDisposable)worker).DisposeAsync().AsTask();
            bool completed = await Task.WhenAny(disposing, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)) == disposing;
            output.WriteLine($"DisposalCompletedBeforeFixtureRelease={completed}; ChildStillRunning={!child.HasExited}; ReleaseExists={File.Exists(release)}");
            Assert.True(completed, "Exited decoder root retirement waited for its child's stderr EOF.");
            await disposing;
            Assert.False(child.HasExited);
            Assert.False(File.Exists(release));
            Assert.Equal(childStart, child.StartTime.ToUniversalTime().Ticks);
        }
        finally
        {
            await File.WriteAllTextAsync(release, "release-owned-child\n", TestContext.Current.CancellationToken);
            if (child is not null)
            {
                await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                output.WriteLine($"OwnedChildExited={child.HasExited}");
                child.Dispose();
            }
            if (worker is not null)
                await (disposing ??= ((IAsyncDisposable)worker).DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Directory.Delete(root, recursive: true);
            output.WriteLine($"OwnedRootRemoved={!Directory.Exists(root)}");
        }
    }
}
