using System.Diagnostics;

namespace FileCat.Tests;

internal static class AnalysisScratchCrashFixture
{
    internal static async Task KillOwnedChild(string parent, CancellationToken cancellation)
    {
        string name = "FileCat.CoreCrashHarness.dll", harness = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(harness) || !File.Exists(Path.ChangeExtension(harness, ".runtimeconfig.json")))
        {
            var checkout = new DirectoryInfo(AppContext.BaseDirectory);
            while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
            Assert.NotNull(checkout);
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", name);
        }
        Assert.True(File.Exists(harness), "Build and deploy the owned scratch crash harness.");
        var start = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(harness); start.ArgumentList.Add("--analysis-scratch-hold"); start.ArgumentList.Add(parent);
        if (OperatingSystem.IsWindows()) start.Environment["FILECAT_TEST_SCRATCH_WINDOWS_ADAPTER"] = Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll");
        using var child = Process.Start(start)!;
        var errors = child.StandardError.ReadToEndAsync(cancellation);
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            Assert.True(ready == "SCRATCH_READY", $"Scratch child did not become ready: {ready}; {(child.HasExited ? await errors : "still running")}");
            string path = Path.Combine(parent, "scratch"), foreign = Path.Combine(path, "user-file.txt");
            File.WriteAllText(foreign, "preserve me");
            child.Kill(); await child.WaitForExitAsync(cancellation).WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            // Process exit and observable namespace retirement are separate native outcomes. Keep
            // checking the canary and never delete/reopen the child's scratch file to make this pass.
            // A process signal alone must not be reported as successful scratch cleanup.
            var clock = Stopwatch.StartNew(); string[] entries = Directory.GetFileSystemEntries(path); int initial = entries.Length;
            while (!entries.SequenceEqual(new[] { foreign }) && clock.Elapsed < TimeSpan.FromSeconds(2))
            {
                Assert.Equal("preserve me", File.ReadAllText(foreign));
                await Task.Delay(10, cancellation); entries = Directory.GetFileSystemEntries(path);
            }
            Console.WriteLine($"Owned scratch child exited; initial entries={initial}, final entries={entries.Length}, cleanup observation={clock.Elapsed.TotalMilliseconds:F3}ms.");
            Assert.Equal("preserve me", File.ReadAllText(foreign)); Assert.Equal(new[] { foreign }, entries);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); child.WaitForExit(10_000); }
            Assert.True(child.HasExited, "The exact owned scratch child must exit before another tester uses this guest.");
        }
    }
}
