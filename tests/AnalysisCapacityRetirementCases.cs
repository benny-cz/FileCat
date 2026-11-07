using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.Analysis;

namespace FileCat.Tests;

internal static class AnalysisCapacityRetirementCases
{
    internal static async Task KernelAsync(string mode, bool retainsPhysicalCapacity)
    {
        Guid profile = Guid.NewGuid(); using var contender = retainsPhysicalCapacity || mode == "kernel-close" ? AnalysisProfileQuota.Open(profile) : null;
        await RunAsync(profile, mode, null, () =>
        {
            if (retainsPhysicalCapacity)
            {
                Assert.False(contender!.TryReserve(AnalysisLimits.ProfileScratchBytes));
                Assert.True(contender.TryReserve(AnalysisLimits.ProfileScratchBytes - AnalysisProfileQuota.QuantumBytes));
                Assert.False(contender.TryReserve(1));
            }
            else if (contender is not null)
            { Assert.True(contender.TryReserve(AnalysisLimits.ProfileScratchBytes)); contender.Release(AnalysisLimits.ProfileScratchBytes); }
        }, retainsPhysicalCapacity ? () => ReuseAsync(contender!) : null);
    }
    internal static async Task RunAsync(Guid profile, string mode, string? parent, Action? whileHeld = null, Func<Task>? afterExit = null)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll"),
            "--analysis-capacity-retirement-probe", profile.ToString("N"), mode, parent ?? "-" }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!; var errors = child.StandardError.ReadToEndAsync(cancellation);
        try
        {
            string? line = await child.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(45), cancellation);
            if (line is null || !line.StartsWith("CAPACITY_READY ", StringComparison.Ordinal))
            {
                if (child.HasExited) throw new Xunit.Sdk.XunitException("Capacity probe exited " + child.ExitCode + ": " + await errors);
                Assert.Fail("Capacity probe did not become ready.");
            }
            using var receipt = JsonDocument.Parse(line!["CAPACITY_READY ".Length..]);
            int expected = mode == "kernel-limit" ? AnalysisLimits.Sessions : 1;
            Assert.True(receipt.RootElement.GetProperty("workerAlive").GetBoolean());
            Assert.Equal(expected, receipt.RootElement.GetProperty("workerCount").GetInt32());
            Assert.True(receipt.RootElement.GetProperty("nativeCount").GetInt32() >= expected);
            Assert.Equal(expected, receipt.RootElement.GetProperty("retainedBytes").GetInt64());
            Assert.Equal(AnalysisProfileQuota.QuantumBytes * expected, receipt.RootElement.GetProperty("capacityBytes").GetInt64());
            Assert.Equal(expected, receipt.RootElement.GetProperty("failureCalls").GetInt32());
            if (mode == "kernel-limit") Assert.True(receipt.RootElement.GetProperty("newOwnerRefused").GetBoolean());
            if (mode == "file") Assert.False(receipt.RootElement.GetProperty("directoryClosed").GetBoolean());
            whileHeld?.Invoke(); Assert.False(child.HasExited);
            await child.StandardInput.WriteLineAsync("EXIT".AsMemory(), cancellation); await child.StandardInput.FlushAsync(cancellation);
            await child.WaitForExitAsync(cancellation).WaitAsync(TimeSpan.FromSeconds(15), cancellation); Assert.Equal(0, child.ExitCode);
            if (afterExit is not null) await afterExit();
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        }
    }
    internal static async Task ReuseAsync(AnalysisProfileQuota contender)
    {
        var clock = Stopwatch.StartNew(); bool acquired;
        while (!(acquired = contender.TryReserve(AnalysisProfileQuota.QuantumBytes)) && clock.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(acquired); Assert.False(contender.TryReserve(1));
    }
}
