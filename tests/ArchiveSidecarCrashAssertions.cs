using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveSidecarCrashAssertions
{
    internal static async Task<ArchiveSidecarFacts> KillAndRecover(string root, string role, string point, string windowsAssembly)
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
        foreach (string arg in new[] { harness, "--archive-sidecar-crash", root, role, point, windowsAssembly }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; Task<string> errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            string? checkpoint = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(checkpoint == "archive-sidecar-checkpoint:" + point, "Missing checkpoint: " + checkpoint + (process.HasExited ? " stderr: " + await errors : ""));
            Assert.False(process.HasExited); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode); Assert.Equal("", await errors.WaitAsync(timeout.Token));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        var interrupted = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Null(interrupted.ArchiveTransactions.Problem); Assert.True(interrupted.ArchiveTransactions.RequiresReview);
        Assert.False(JournalRecovery.TryClose(interrupted, "sidecar crash evidence requires native review"));
        var facts = Assert.Single(interrupted.ArchiveTransactions.Transactions); var sidecar = Assert.Single(facts.Sidecars);
        Assert.Equal(ArchiveTransactionPhase.Reserved, facts.Phase); Assert.NotNull(facts.StageIdentity); Assert.Null(facts.VerifiedStage);
        Assert.Equal(role == "manifest" ? ArchiveSidecarRole.ExpectedOutput : ArchiveSidecarRole.OriginEvidence, sidecar.Role);
        Assert.Equal(point != "reserved", sidecar.Identity is not null); Assert.Equal(point == "recorded", sidecar.Version is not null);
        Assert.Equal(point != "reserved", File.Exists(sidecar.Path));
        if (point is "identified" or "partial") Assert.Equal(point == "partial" ? 1 : 0, new FileInfo(sidecar.Path).Length);
        if (point is "sealed" or "recorded") Assert.Equal(sidecar.MaximumLength, new FileInfo(sidecar.Path).Length);
        Assert.Equal("owned unchanged sentinel", File.ReadAllText(sentinel)); Assert.False(File.Exists(Path.Combine(root, "result.tar")));
        Assert.True(File.Exists(interrupted.JournalPath)); Assert.True(File.Exists(facts.StagePath));
        return sidecar;
    }
}
