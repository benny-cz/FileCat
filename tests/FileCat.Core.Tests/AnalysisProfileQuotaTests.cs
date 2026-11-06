using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

[Collection("Analysis native source device")]
public sealed partial class AnalysisProfileQuotaTests
{
    private const long Capacity = AnalysisLimits.ProfileScratchBytes, Quantum = AnalysisProfileQuota.QuantumBytes;
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Fact]
    public void SameProfileOwnersShareTheCeilingAndFailedGrowthPreservesExistingReservations()
    {
        Ordinary(); Guid id = Guid.NewGuid();
        using var first = AnalysisProfileQuota.Open(id); using var second = AnalysisProfileQuota.Open(id); using var third = AnalysisProfileQuota.Open(id);
        Assert.True(first.TryReserve(Capacity / 2)); Assert.True(second.TryReserve(Capacity / 2)); Assert.False(third.TryReserve(1));
        first.Release(Quantum); Assert.True(third.TryReserve(1)); Assert.Equal(1, third.ReservedBytes); Assert.Equal(Quantum, third.CapacityBytes);
        Assert.False(third.TryReserve(Quantum)); Assert.Equal(1, third.ReservedBytes); Assert.Equal(Quantum, third.CapacityBytes);
        first.Release(Quantum); Assert.True(third.TryReserve(Quantum)); Assert.Equal(Quantum + 1, third.ReservedBytes); Assert.Equal(Quantum * 2, third.CapacityBytes);
        third.Release(Quantum + 1); Assert.Equal(0, third.CapacityBytes); Assert.True(first.TryReserve(Quantum * 2));
        Assert.False(first.TryReserve(long.MaxValue)); Assert.Throws<ArgumentOutOfRangeException>(() => first.TryReserve(-1));
        Assert.Throws<InvalidOperationException>(() => first.Release(Capacity));
    }

    [Fact]
    public void DifferentPrivateProfilesHaveIndependentNativeCeilings()
    {
        Ordinary(); using var first = AnalysisProfileQuota.Open(Guid.NewGuid()); using var second = AnalysisProfileQuota.Open(Guid.NewGuid());
        Assert.True(first.TryReserve(Capacity)); Assert.True(second.TryReserve(Quantum));
        Assert.Equal(Capacity, first.CapacityBytes); Assert.Equal(Quantum, second.CapacityBytes);
    }

    [Fact]
    public void WindowsForeignObjectTypeIsPreservedAndItsPrivateNameDoesNotEscapeTheFailure()
    {
        Ordinary(); Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows named-kernel-object negative control.");
        Guid profile = Guid.NewGuid(); string name = "FileCat.AnalysisQuota.v1." + Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(profile.ToString("N")))) + ".0";
        using var other = new EventWaitHandle(false, EventResetMode.ManualReset, name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });
        using var quota = AnalysisProfileQuota.Open(profile); var failure = Assert.Throws<IOException>(() => quota.TryReserve(1));
        Assert.DoesNotContain(name, failure.ToString(), StringComparison.Ordinal); Assert.Equal(0, quota.ReservedBytes); Assert.Equal(0, quota.CapacityBytes);
        Assert.False(other.WaitOne(0)); Assert.True(other.Set()); Assert.True(other.WaitOne(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastOwnedChildExitLeavesNoCapacityDebtForTheNextOwner(bool crash)
    {
        Ordinary(); Guid id = Guid.NewGuid();
        using var child = Start(id, Capacity, true, null); var errors = child.StandardError.ReadToEndAsync(Cancellation);
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(15), Cancellation);
            Assert.True(ready == "QUOTA_READY", $"Child readiness: {ready}; {(child.HasExited ? await errors : "still running")}");
            if (crash) child.Kill(); else { await child.StandardInput.WriteLineAsync("EXIT".AsMemory(), Cancellation); await child.StandardInput.FlushAsync(Cancellation); }
            await child.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(10), Cancellation); Assert.True(child.HasExited);
            using var next = AnalysisProfileQuota.Open(id); Assert.True(next.TryReserve(Capacity)); Assert.False(next.TryReserve(1));
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(10), Cancellation); } Assert.True(child.HasExited); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OwnedChildSharesCapacityAcrossSessionsAndReturnsItThroughActualExit(bool crash, bool separateSession)
    {
        Ordinary(); Guid id = Guid.NewGuid(); using var first = AnalysisProfileQuota.Open(id); using var contender = AnalysisProfileQuota.Open(id);
        Assert.True(first.TryReserve(Capacity / 2));
        using var child = Start(id, Capacity / 2, separateSession, null);
        var errors = child.StandardError.ReadToEndAsync(Cancellation);
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(15), Cancellation);
            Assert.True(ready == "QUOTA_READY", $"Child readiness: {ready}; {(child.HasExited ? await errors : "still running")}");
            Assert.False(contender.TryReserve(1));
            if (crash) child.Kill(); else { await child.StandardInput.WriteLineAsync("EXIT".AsMemory(), Cancellation); await child.StandardInput.FlushAsync(Cancellation); }
            await child.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            Assert.True(child.HasExited); Assert.True(contender.TryReserve(Capacity / 2)); Assert.False(contender.TryReserve(1));
            if (!crash) Assert.Equal(0, child.ExitCode);
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(10), Cancellation); } Assert.True(child.HasExited); }
    }

    [Fact]
    public async Task PrivateRuntimeEnvironmentCannotGiveAnOwnedChildASecondCeiling()
    {
        Ordinary(); Guid id = Guid.NewGuid(); using var first = AnalysisProfileQuota.Open(id); Assert.True(first.TryReserve(Capacity / 2));
        string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FileCatValidation");
        string directory = Path.Combine(parent, "quota-environment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var child = Start(id, Capacity / 2, true, directory); using var contender = AnalysisProfileQuota.Open(id);
        var errors = child.StandardError.ReadToEndAsync(Cancellation);
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(15), Cancellation);
            Assert.True(ready == "QUOTA_READY", $"Child readiness: {ready}; {(child.HasExited ? await errors : "still running")}");
            Assert.False(contender.TryReserve(1));
        }
        finally
        {
            if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            Assert.True(child.HasExited); Assert.Equal(parent, Path.GetDirectoryName(Path.GetFullPath(directory))); Directory.Delete(directory, true);
        }
    }

    private static Process Start(Guid profile, long bytes, bool separateSession, string? environment)
    {
        string harness = Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll");
        Assert.True(File.Exists(harness));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(harness); start.ArgumentList.Add("--analysis-quota-hold"); start.ArgumentList.Add(profile.ToString("N"));
        start.ArgumentList.Add(bytes.ToString(CultureInfo.InvariantCulture)); start.ArgumentList.Add(separateSession ? "new" : "same");
        if (environment is not null) { start.Environment["TMPDIR"] = environment; start.Environment["XDG_RUNTIME_DIR"] = environment; }
        return Process.Start(start)!;
    }
}
