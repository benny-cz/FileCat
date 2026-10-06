using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed class ReadServerIdentityTests
{
    [Fact]
    public async Task The_kernel_server_must_be_the_process_held_by_the_client()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Read-session peer identity uses Windows process and pipe handles.");
        string name = "FileCat-identity-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await connected;
        using var self = Process.GetCurrentProcess();
        using var matching = new ElevatedProcess(new SafeProcessHandle(self.SafeHandle.DangerousGetHandle(), ownsHandle: false));
        matching.VerifyReadServer(client.SafePipeHandle);

        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" }) info.ArgumentList.Add(arg);
        using var other = Process.Start(info) ?? throw new IOException("The owned unrelated peer control did not start.");
        try
        {
            using var mismatched = new ElevatedProcess(new SafeProcessHandle(other.SafeHandle.DangerousGetHandle(), ownsHandle: false));
            var error = Assert.Throws<IOException>(() => mismatched.VerifyReadServer(client.SafePipeHandle));
            Assert.Contains("Nothing was read", error.Message, StringComparison.Ordinal);
            Assert.True(PeekNamedPipe(server.SafePipeHandle, 0, 0, 0, out uint available, 0));
            Assert.Equal(0u, available);
        }
        finally
        {
            if (!other.HasExited) other.Kill();
            Assert.True(other.WaitForExit(5000));
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafePipeHandle pipe, nint buffer, uint bufferSize, nint bytesRead, out uint available, nint bytesLeft);
}
