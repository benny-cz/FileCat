using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.App.Tests;

/// <summary>
/// I682: started where it cannot open an X display, FileCat says why and exits with 1. Before, Avalonia's "XOpenDisplay
/// failed" went unhandled: .NET printed a stack trace and aborted, so the process ended by SIGABRT with a core dump, and
/// Ubuntu filed a crash report.
/// </summary>
public sealed class NoDisplayStartupTests
{
    [Fact]
    public void Only_the_display_failure_gets_the_message_and_it_names_the_display()
    {
        var failure = new Exception("XOpenDisplay failed");
        Assert.StartsWith("FileCat could not open a window: no X display is set", Program.NoDisplayMessage(failure, null));
        Assert.StartsWith("FileCat could not open a window: no X display is set", Program.NoDisplayMessage(failure, ""));
        Assert.StartsWith("FileCat could not open a window on the X display \":5\":", Program.NoDisplayMessage(failure, ":5"));
        Assert.Null(Program.NoDisplayMessage(new InvalidOperationException("XOpenDisplay failed"), ":0"));
        Assert.Null(Program.NoDisplayMessage(new Exception("XOpenDisplay failed for another reason"), ":0"));
        Assert.Null(Program.NoDisplayMessage(new DllNotFoundException("libX11.so.6"), null));
    }

    // No display at all (as over SSH or in a text console), and a display no X server answers.
    [Theory]
    [InlineData(null)]
    [InlineData(":987")]
    public async Task Without_a_usable_X_display_FileCat_says_why_and_exits_with_1(string? display)
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("FileCat opens its windows through X11 only on Linux."); return; }
        string data = Path.Combine(Path.GetTempPath(), "filecat-i682-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            // The app as the tests built it, run by this runtime's own host, with its own profile folder.
            string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            string app = Path.Combine(AppContext.BaseDirectory, "FileCat.dll");
            Assert.True(File.Exists(app), app);
            var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { app, "--new-instance", "--data", data }) start.ArgumentList.Add(argument);
            start.Environment.Remove("WAYLAND_DISPLAY");
            start.Environment.Remove("XAUTHORITY");
            if (display is null) start.Environment.Remove("DISPLAY");
            else start.Environment["DISPLAY"] = display;
            var cancel = TestContext.Current.CancellationToken;
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var errors = process.StandardError.ReadToEndAsync(cancel);
            try { await process.WaitForExitAsync(cancel).WaitAsync(TimeSpan.FromMinutes(2), cancel); }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("FileCat did not end within 2 minutes without a display");
            }
            string stderr = await errors;
            Assert.True(process.ExitCode == 1, $"exit code {process.ExitCode}; stderr: {stderr}; stdout: {await output}");
            Assert.DoesNotContain("Unhandled exception", stderr);
            Assert.Contains(display is null ? "FileCat could not open a window: no X display is set" : $"X display \"{display}\"", stderr);
        }
        finally
        {
            try { Directory.Delete(data, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
