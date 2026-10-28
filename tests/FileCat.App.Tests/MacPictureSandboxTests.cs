using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class MacPictureSandboxTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Actual_macOS_worker_policy_denies_descendants_and_network()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("The macOS Seatbelt policy is native to macOS."); return; }
        var (executable, _) = PictureDecoder.WorkerCommand(64);
        string[] arguments = Path.GetFileNameWithoutExtension(executable).Equals("FileCat", StringComparison.OrdinalIgnoreCase)
            ? [PictureWorker.Argument, "64"]
            : [typeof(PictureDecoder).Assembly.Location, PictureWorker.Argument, "64"];
        var start = MacPictureSandbox.StartInfo(executable, arguments);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The policy probe did not start.");
        try
        {
            var clock = Stopwatch.StartNew();
            while (!process.HasExited && !process.ProcessName.Equals("FileCat", StringComparison.OrdinalIgnoreCase) &&
                   clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                process.Refresh();
            }
            Assert.False(process.HasExited);
            Assert.Equal("FileCat", process.ProcessName, ignoreCase: true);
            int fork = SandboxCheck(process.Id, "process-fork", 0);
            int outbound = SandboxCheck(process.Id, "network-outbound", 0);
            int inbound = SandboxCheck(process.Id, "network-inbound", 0);
            output.WriteLine($"MAC_PICTURE_SANDBOX pid={process.Id} executable={executable} process={process.ProcessName} process-fork={fork} network-outbound={outbound} network-inbound={inbound}");
            Assert.Equal(1, fork);
            Assert.Equal(1, outbound);
            Assert.Equal(1, inbound);
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [DllImport("/usr/lib/libsandbox.1.dylib", EntryPoint = "sandbox_check", CharSet = CharSet.Ansi)]
    private static extern int SandboxCheck(int pid, string operation, int filterType);
}
