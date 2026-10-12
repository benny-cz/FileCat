using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed class SandboxedWorkerEnvironmentTests
{
    [Fact]
    public async Task Picture_worker_does_not_inherit_parent_environment()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows restricted picture worker."); return; }
        string marker = "FILECAT_PICTURE_ENV_PROBE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(marker, "owned-secret-marker");
        try
        {
            string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            using var worker = SandboxedWorker.Start(cmd, "/d /c set", 128L * 1024 * 1024);
            using var reader = new StreamReader(worker.Output);
            string variables = await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(variables.Contains(marker + "=", StringComparison.OrdinalIgnoreCase));
            Assert.True(variables.Contains("DOTNET_EnableDiagnostics=0", StringComparison.OrdinalIgnoreCase));
        }
        finally { Environment.SetEnvironmentVariable(marker, null); }
    }
}
