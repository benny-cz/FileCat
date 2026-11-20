using FileCat.Core.Analysis;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Text.Json;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed class AnalysisNativeHandleTests
{
    [Fact]
    public async Task NativeBackendChildCloseFailureStopsNewWorkAndRemainsVisibleAtFinalClosure()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Windows native adapter runs in AnalysisWindowsTests.");
        await FileCat.Tests.AnalysisNativeHandleCases.BackendCloseFault();
    }
    [Fact]
    public void ProfileCapacityCannotDowngradeAnUnconfirmedNativeCloseToAnOrdinaryUnavailableError()
    {
        Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
        var directory = new UnknownCloseDirectory();
        using (var quota = AnalysisProfileQuota.Open(Guid.NewGuid(), directory))
        {
            var error = Assert.Throws<AnalysisCloseUnconfirmedException>(() => quota.TryReserve(1));
            Assert.DoesNotContain("PRIVATE_NATIVE_DETAIL", error.Message, StringComparison.Ordinal);
            Assert.Throws<AnalysisCloseUnconfirmedException>(() => quota.TryReserve(1)); Assert.Equal(1, directory.Reads);
        }
        Assert.True(directory.Closed);
    }
    [Fact]
    public void RepeatedUnixPrivateCatalogCursorsReturnTheirNativeReservations()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Unix native fixtures.");
        string root = Path.Combine(Environment.CurrentDirectory, ".tmp", "native-cursors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); int baseline = AnalysisNativeHandles.Count;
        try
        {
            using (var directory = new UnixAnalysisPrivateDirectory(root, "saved"))
            {
                int held = AnalysisNativeHandles.Count;
                for (int i = 0; i <= AnalysisLimits.NativeHandles; i++)
                { Assert.Empty(directory.Entries(TestContext.Current.CancellationToken)); Assert.Equal(held, AnalysisNativeHandles.Count); }
            }
            Assert.Equal(baseline, AnalysisNativeHandles.Count);
        }
        finally { Directory.Delete(Path.Combine(root, "saved")); Directory.Delete(root); }
    }
    [Fact]
    public async Task NativeFilesKeepFailedCloseChargedAtTheGlobalLimitInAnOwnedProcess()
    {
        Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process for the native handle probe.");
        string path = Path.Combine(Path.GetTempPath(), "FileCat-native-handle-probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, [1, 2, 3]);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll"));
        start.ArgumentList.Add("--analysis-native-handle-probe"); start.ArgumentList.Add(path);
        using var child = Process.Start(start)!;
        var stderr = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            string? line = await child.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            if (line is null || !line.StartsWith("HANDLE_READY ", StringComparison.Ordinal))
            {
                if (child.HasExited) throw new Xunit.Sdk.XunitException("Native handle probe exited " + child.ExitCode + ": " + await stderr);
                Assert.Fail("Native handle probe did not become ready.");
            }
            using var receipt = JsonDocument.Parse(line!["HANDLE_READY ".Length..]);
            Assert.Equal(128, receipt.RootElement.GetProperty("nativeLimit").GetInt32());
            Assert.Equal(1, receipt.RootElement.GetProperty("retainedSlots").GetInt32());
            Assert.Equal(1, receipt.RootElement.GetProperty("failedCloseCalls").GetInt32());
            Assert.True(receipt.RootElement.GetProperty("realCloseConfirmed").GetBoolean());
            Assert.False(child.HasExited); await child.StandardInput.WriteLineAsync("EXIT".AsMemory(), TestContext.Current.CancellationToken);
            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(0, child.ExitCode); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            File.Delete(path);
        }
    }
    [Fact]
    public void ConfirmedCloseReturnsItsSlotExactlyOnce()
    {
        var slot = new Slot(); int calls = 0;
        using var original = new SafeFileHandle(42, ownsHandle: false);
        var owner = AnalysisNativeHandle.Adopt(original, slot, new(), _ => { calls++; return true; });
        Assert.True(original.IsClosed); owner.CloseChecked(); owner.CloseChecked();
        Assert.Equal(1, calls); Assert.Equal(1, slot.Releases);
    }
    [Fact]
    public void FailedNativeResultNeverReturnsItsSlotOrRetriesTheNativeNumber()
    {
        var slot = new Slot(); var quarantine = new AnalysisResourceQuarantine(); int calls = 0;
        using var original = new SafeFileHandle(42, ownsHandle: false);
        var owner = AnalysisNativeHandle.Adopt(original, slot, quarantine, _ => { calls++; return false; });
        Assert.Throws<AnalysisCloseUnconfirmedException>(owner.CloseChecked);
        Assert.Throws<AnalysisCloseUnconfirmedException>(owner.CloseChecked);
        Assert.Equal(1, calls); Assert.Equal(0, slot.Releases); Assert.Equal(1, quarantine.Count);
        Assert.Throws<ObjectDisposedException>(() => owner.BorrowFile());
    }
    [Fact]
    public void CancellationFromCloseRemainsAnUnconfirmedOwnerWithoutRetry()
    {
        var slot = new Slot(); var quarantine = new AnalysisResourceQuarantine(); int calls = 0;
        using var original = new SafeFileHandle(42, ownsHandle: false);
        var owner = AnalysisNativeHandle.Adopt(original, slot, quarantine, _ => { calls++; throw new OperationCanceledException(); });
        Assert.IsType<OperationCanceledException>(Assert.Throws<AnalysisCloseUnconfirmedException>(owner.CloseChecked).InnerException);
        Assert.Throws<AnalysisCloseUnconfirmedException>(owner.CloseChecked);
        Assert.Equal(1, calls); Assert.Equal(0, slot.Releases); Assert.Equal(1, quarantine.Count);
    }
    [Fact]
    public void ActiveNativePinKeepsTheHandleAndSlotUntilTheActualCallerReturns()
    {
        var slot = new Slot(); int calls = 0;
        using var original = new SafeFileHandle(42, ownsHandle: false);
        var owner = AnalysisNativeHandle.Adopt(original, slot, new(), _ => { calls++; return true; });
        using var pin = owner.BorrowFile();
        Assert.Throws<AnalysisCloseUnconfirmedException>(owner.CloseChecked);
        Assert.Equal(0, calls); Assert.Equal(0, slot.Releases);
        pin.Dispose(); Assert.Equal(1, calls); Assert.Equal(1, slot.Releases); owner.CloseChecked();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposingAStreamViewDoesNotCloseItsCountedNativeOwner(bool asyncDispose)
    {
        string path = Path.Combine(Path.GetTempPath(), "FileCat-owned-stream-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var slot = new Slot();
            var owner = AnalysisNativeHandle.Adopt(File.OpenHandle(path, FileMode.Open, FileAccess.Read), slot, new());
            var stream = owner.OpenStream(FileAccess.Read);
            Assert.Equal(1, stream.ReadByte());
            if (asyncDispose) await stream.DisposeAsync(); else stream.Dispose();
            Assert.Equal(0, slot.Releases);
            using (var pin = owner.BorrowFile()) Assert.Equal(3, RandomAccess.GetLength(pin.View));
            owner.CloseChecked(); Assert.Equal(1, slot.Releases);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void CleanupAttemptsEveryIndependentOwnerAndPreservesCloseFailures()
    {
        int calls = 0;
        var error = Assert.Throws<AggregateException>(() => AnalysisNativeHandle.CloseAll([
            new ActionOwner(() => { calls++; throw new IOException(); }),
            new ActionOwner(() => { calls++; throw new AnalysisCloseUnconfirmedException(new IOException()); }),
            new ActionOwner(() => calls++)]));
        Assert.Equal(3, calls); Assert.Equal(2, error.InnerExceptions.Count); Assert.True(AnalysisCloseUnconfirmedException.IsPresent(error));
    }
    private sealed class Slot : IDisposable
    { internal int Releases; public void Dispose() => Releases++; }
    private sealed class UnknownCloseDirectory : AnalysisPrivateDirectory
    {
        internal int Reads; internal bool Closed;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => (1UL << 40, 1UL << 40);
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name)
        { Reads++; throw new AnalysisCloseUnconfirmedException(new IOException("PRIVATE_NATIVE_DETAIL")); }
        public override void Dispose() => Closed = true;
    }
    private sealed class ActionOwner(Action close) : IDisposable
    { public void Dispose() => close(); }
}
