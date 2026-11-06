using System.Diagnostics;
using System.Globalization;
using FileCat.Core.Analysis;

namespace FileCat.Tests;

internal static class AnalysisFileQuotaCases
{
    private const long Capacity = AnalysisLimits.ProfileScratchBytes, Quantum = AnalysisProfileQuota.QuantumBytes;
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static void SharedAndRetained(Func<string, string, AnalysisPrivateDirectory> open)
    {
        using var fixture = new Fixture(open); var owner = fixture.Open();
        using var retained = owner.Borrow(); using var second = fixture.Open(); using var third = fixture.Open();
        try
        {
            Assert.True(ReserveWithFixtureDiagnostic(owner, Capacity / 2)); Assert.True(ReserveWithFixtureDiagnostic(second, Capacity / 2));
            byte[] state = File.ReadAllBytes(fixture.State); owner.Dispose();
            Assert.False(third.TryReserve(1)); Assert.Equal(Capacity / 2, owner.ReservedBytes);
            second.Release(Quantum); Assert.True(third.TryReserve(1));
            Assert.False(third.TryReserve(Quantum)); Assert.Equal(1, third.ReservedBytes); Assert.Equal(Quantum, third.CapacityBytes);
            retained.Dispose(); Assert.True(third.TryReserve(Capacity / 2));
            Assert.Equal(state, File.ReadAllBytes(fixture.State)); Assert.Equal(32, Directory.GetFiles(fixture.Directory, "capacity-*.state").Length);
            Assert.Equal("untouched neighbor", File.ReadAllText(fixture.Canary));
        }
        finally { owner.Dispose(); }
    }
    private static bool ReserveWithFixtureDiagnostic(AnalysisProfileQuota quota, long bytes)
    {
        try { return quota.TryReserve(bytes); }
        catch (IOException)
        {
            // Only generated fixtures use this diagnostic. Product failures continue to omit
            // private native names; inspect the retained exception solely to explain a QA failure.
            var failure = (Exception?)typeof(AnalysisProfileQuota).GetField("_failure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(quota);
            throw new Xunit.Sdk.XunitException("Owned capacity fixture failed: " + failure);
        }
    }
    internal static void ForeignAndMismatched(Func<string, string, AnalysisPrivateDirectory> open)
    {
        using var fixture = new Fixture(open);
        using (var wrong = AnalysisProfileQuota.Open(Guid.NewGuid(), open(fixture.Parent, "capacity")))
            Assert.Throws<IOException>(() => wrong.TryReserve(1));
        Assert.Single(Directory.GetFiles(fixture.Directory));
        using (var initial = fixture.Open()) Assert.True(initial.TryReserve(1));
        string token = Path.Combine(fixture.Directory, "capacity-00.state"); byte[] original = File.ReadAllBytes(token);
        File.WriteAllText(token, "FOREIGN_CAPACITY_CANARY"); byte[] foreign = File.ReadAllBytes(token);
        using (var refused = fixture.Open())
        {
            Assert.Throws<IOException>(() => refused.TryReserve(1)); Assert.Equal(0, refused.ReservedBytes); Assert.Equal(foreign, File.ReadAllBytes(token));
            // A running failed owner cannot regain admission after outside repair.
            File.WriteAllBytes(token, original); Assert.Throws<IOException>(() => refused.TryReserve(1));
        }
        using var fresh = fixture.Open(); Assert.True(fresh.TryReserve(Capacity));
        fresh.Dispose(); // Ordinary path-based .NET reads request a shared flock on Unix.
        Assert.Equal(original, File.ReadAllBytes(token));
    }
    internal static async Task CrossProcess(Func<string, string, AnalysisPrivateDirectory> open, bool crash)
    {
        using var fixture = new Fixture(open); using var parent = fixture.Open(); using var contender = fixture.Open();
        Assert.True(parent.TryReserve(Capacity / 2));
        using var child = Start(fixture, Capacity / 2);
        var errors = child.StandardError.ReadToEndAsync(Cancellation);
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(20), Cancellation);
            Assert.True(ready == "QUOTA_READY", $"Child readiness {ready}; {(child.HasExited ? await errors : "still running")}");
            Assert.False(contender.TryReserve(1));
            using (var denied = Start(fixture, 1))
            {
                try
                {
                    await denied.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(20), Cancellation); Assert.Equal(73, denied.ExitCode);
                }
                finally { if (!denied.HasExited) { denied.Kill(); await denied.WaitForExitAsync(Cancellation); } }
            }
            if (crash) child.Kill();
            else { await child.StandardInput.WriteLineAsync("EXIT".AsMemory(), Cancellation); await child.StandardInput.FlushAsync(Cancellation); }
            await child.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(15), Cancellation);
            // A dead process's file locks can retire shortly after process-exit notification.
            var until = DateTime.UtcNow.AddSeconds(5); bool acquired;
            while (!(acquired = contender.TryReserve(Capacity / 2)) && DateTime.UtcNow < until) await Task.Delay(10, Cancellation);
            Assert.True(acquired); Assert.False(contender.TryReserve(1));
            Assert.Equal("untouched neighbor", File.ReadAllText(fixture.Canary));
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(Cancellation); } Assert.True(child.HasExited); }
    }
    private static Process Start(Fixture fixture, long bytes)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll"));
        start.ArgumentList.Add("--analysis-file-quota-hold"); start.ArgumentList.Add(fixture.Profile.ToString("N"));
        start.ArgumentList.Add(bytes.ToString(CultureInfo.InvariantCulture)); start.ArgumentList.Add("same"); start.ArgumentList.Add(fixture.Parent);
        return Process.Start(start)!;
    }
    private sealed class Fixture : IDisposable
    {
        private readonly Func<string, string, AnalysisPrivateDirectory> _open;
        private readonly string _base = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".tmp"));
        internal string Parent { get; }
        internal string Directory => Path.Combine(Parent, "capacity");
        internal string State => Path.Combine(Directory, "private-state.json");
        internal string Canary => Path.Combine(Parent, "neighbor.txt");
        internal Guid Profile { get; }
        internal Fixture(Func<string, string, AnalysisPrivateDirectory> open)
        {
            _open = open; Parent = Path.Combine(_base, "file-capacity-" + Guid.NewGuid().ToString("N"));
            if (System.IO.Directory.Exists(Parent)) throw new IOException("Owned fixture collision.");
            System.IO.Directory.CreateDirectory(Parent); File.WriteAllText(Canary, "untouched neighbor");
            using var store = new AnalysisPrivateInventoryStore(open(Parent, "capacity")); Profile = new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
        }
        internal AnalysisProfileQuota Open()
        {
            var directory = _open(Parent, "capacity");
            try { return AnalysisProfileQuota.Open(Profile, directory); } catch { directory.Dispose(); throw; }
        }
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Parent);
            if (!resolved.StartsWith(_base + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Fixture cleanup escaped its root.");
            for (int attempt = 0; ; attempt++)
                try { System.IO.Directory.Delete(resolved, recursive: true); return; }
                catch (IOException) when (attempt < 20) { Thread.Sleep(100); }
        }
    }
}
