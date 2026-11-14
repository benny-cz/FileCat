using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveStageCrashAssertions
{
    internal static async Task<ArchiveTransactionFacts> KillAndRecover(string root, string point, string windowsAssembly)
    {
        string sentinel = Path.Combine(root, "untouched-sentinel"); File.WriteAllText(sentinel, "owned unchanged sentinel");
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { harness, "--archive-stage-crash", root, point, windowsAssembly }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            string? checkpoint = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(checkpoint == "archive-stage-checkpoint:" + point, "Missing checkpoint: " + checkpoint + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited);
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode); Assert.Equal("", await errors.WaitAsync(timeout.Token));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.True(process.HasExited);
        var interrupted = Assert.Single(await SettledJournals.ScanAsync(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var recovered = Assert.Single(interrupted.ArchiveTransactions.Transactions);
        Assert.Null(interrupted.ArchiveTransactions.Problem); Assert.True(interrupted.ArchiveTransactions.RequiresReview);
        Assert.Equal(ArchiveTransactionPhase.Reserved, recovered.Phase); Assert.Equal(1024, recovered.StageMaximumLength);
        Assert.Equal(point == "recorded" ? 2 : 1, recovered.Sequence); Assert.Equal(point == "recorded", recovered.StageIdentity is not null);
        Assert.Equal(point != "reserved", File.Exists(recovered.StagePath));
        Assert.False(JournalRecovery.TryClose(interrupted, "crash evidence requires native archive review"));
        Assert.True(File.Exists(interrupted.JournalPath)); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
        Assert.Equal("owned unchanged sentinel", File.ReadAllText(sentinel));
        return recovered;
    }
}
