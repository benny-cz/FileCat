using System.Diagnostics;
using System.Text.Json;

namespace FileCat.Tests;

internal static class AnalysisNativeHandleCases
{
    internal static async Task BackendCloseFault()
    {
        var cancellation = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), "FileCat-backend-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string source = Path.Combine(root, "source.bin"); File.WriteAllBytes(source, [1, 2, 3]);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll"));
        start.ArgumentList.Add("--analysis-backend-close-probe"); start.ArgumentList.Add(root);
        using var child = Process.Start(start)!; var stderr = child.StandardError.ReadToEndAsync(cancellation);
        try
        {
            string? line = await child.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(30), cancellation);
            if (line is null || !line.StartsWith("BACKEND_READY ", StringComparison.Ordinal))
            {
                if (child.HasExited) throw new Xunit.Sdk.XunitException("Native backend probe exited " + child.ExitCode + ": " + await stderr);
                Assert.Fail("Native backend probe did not become ready.");
            }
            using var receipt = JsonDocument.Parse(line!["BACKEND_READY ".Length..]);
            Assert.Equal(1, receipt.RootElement.GetProperty("retainedSlots").GetInt32());
            Assert.Equal(1, receipt.RootElement.GetProperty("faults").GetInt32());
            Assert.Equal(0, receipt.RootElement.GetProperty("newNativeCallsAfterFault").GetInt32());
            Assert.True(receipt.RootElement.GetProperty("backendWorkRefused").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("backendCloseRetainedFailure").GetBoolean());
            Assert.False(child.HasExited); await child.StandardInput.WriteLineAsync("EXIT".AsMemory(), cancellation);
            await child.WaitForExitAsync(cancellation).WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            Assert.Equal(0, child.ExitCode); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            File.Delete(source); Directory.Delete(root);
        }
    }
}
