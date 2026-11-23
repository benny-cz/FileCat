using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

/// <summary>
/// 64-bit Windows can answer a file it cannot run with a modal message box raised inside CreateProcess, which waits for a
/// person while Process.Start holds its process-wide lock. On 23 November 2026 one stalled the whole Core suite: every
/// test that started a program waited behind it. The box appears only in a process that reports critical errors, an error
/// mode a process inherits from its parent, so this case runs in a child that reports them whatever its parent does.
/// </summary>
public sealed partial class ProgramStartTests(ITestOutputHelper output)
{
    private const string ChildRoot = "FILECAT_PROGRAM_START_TEST_ROOT";
    private const uint FailCriticalErrors = 0x0001; // SEM_FAILCRITICALERRORS

    [Fact]
    public async Task A_file_Windows_cannot_run_is_a_start_error_even_where_critical_errors_are_reported()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The modal box comes from 64-bit Windows' CreateProcess.");
        if (Environment.GetEnvironmentVariable(ChildRoot) is { } owned) { StartUnsupportedImage(owned); return; }

        string root = Directory.CreateTempSubdirectory("filecat-program-start-").FullName;
        string host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string arg in new[] { typeof(ProgramStartTests).Assembly.Location, "-method",
                     typeof(ProgramStartTests).FullName + "." + nameof(A_file_Windows_cannot_run_is_a_start_error_even_where_critical_errors_are_reported),
                     "-noAutoReporters", "-noColor", "-noLogo" })
            start.ArgumentList.Add(arg);
        start.Environment[ChildRoot] = root;
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var child = Process.Start(start) ?? throw new IOException("The owned child test host did not start.");
        try
        {
            var stdout = child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            bool finished = false;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                // An unanswered box never returns; ending the child closes it.
                try { await child.WaitForExitAsync(timeout.Token); finished = true; }
                catch (OperationCanceledException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); }
            }
            string result = Path.Combine(root, "result.json");
            output.WriteLine("PROGRAM_START_CHILD " + JsonSerializer.Serialize(new
            {
                ChildPID = child.Id, Finished = finished, child.ExitCode, Stdout = await stdout, Stderr = await stderr,
                Observation = File.Exists(result) ? File.ReadAllText(result) : null,
            }));
            Assert.True(finished, "The child did not finish within 60 s: a modal box most likely held CreateProcess.");
            Assert.Equal(0, child.ExitCode);
            Assert.True(File.Exists(result), "The child ran no case.");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); }
            Directory.Delete(root, recursive: true);
        }
    }

    private static void StartUnsupportedImage(string root)
    {
        // Report critical errors: the default error mode, which a program started through the shell gets.
        SetErrorMode(GetErrorMode() & ~FailCriticalErrors);
        Assert.Equal(0u, GetErrorMode() & FailCriticalErrors);
        Assert.Equal(0u, GetThreadErrorMode() & FailCriticalErrors);
        string exe = Path.Combine(root, "owned-unsupported.exe");
        File.WriteAllBytes(exe, Enumerable.Range(0, 128).Select(n => (byte)(n * 19 + 7)).ToArray());

        var clock = Stopwatch.StartNew();
        var tool = new ToolDefinition { Name = "Owned unsupported image", Executable = exe, Arguments = [] };
        var launch = Assert.Throws<ToolLaunchException>(() => ToolLauncher.Launch(tool, new ToolContext([], root), root));
        // The per-item start of an Apply command, with its redirected streams.
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var native = Assert.Throws<Win32Exception>(() => ProgramStart.Start(start));
        var elapsed = clock.Elapsed;
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            LaunchError = launch.Message, NativeError = native.NativeErrorCode, NativeMessage = native.Message,
            ElapsedMilliseconds = (long)elapsed.TotalMilliseconds, ProcessErrorMode = GetErrorMode(), ThreadErrorMode = GetThreadErrorMode(),
        }));
        // The caller's thread mode is restored.
        Assert.Equal(0u, GetThreadErrorMode() & FailCriticalErrors);
        // Both refusals take milliseconds. A box someone closed shows up here as the wait it caused.
        Assert.True(elapsed < TimeSpan.FromSeconds(20), $"The refusals took {elapsed.TotalSeconds:0.0} s: a modal box held CreateProcess.");
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetErrorMode();

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint mode);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetThreadErrorMode();
}
