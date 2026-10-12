using System.Diagnostics;
using System.Reflection;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

public sealed class UnixDeviceToolDeadlineTests(ITestOutputHelper output)
{
    private static readonly MethodInfo RunTool = typeof(UnixDisks).GetMethod("Run",
        BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(string), typeof(string[])], null)
        ?? throw new InvalidOperationException("The Unix topology helper is missing.");

    private static string Run(string executable) =>
        Assert.IsType<string>(RunTool.Invoke(null, [executable, Array.Empty<string>()]));

    [Fact]
    public void Native_topology_tool_drains_stderr_while_reading_stdout()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix topology helper process control."); return; }
        string root = Path.Combine(Path.GetTempPath(), "filecat-unix-tool-stderr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string script = Path.Combine(root, "tool.sh");
        try
        {
            File.WriteAllText(script, "#!/bin/sh\ni=0\nwhile [ \"$i\" -lt 4096 ]; do\n  printf '" +
                new string('x', 128) + "' >&2\n  i=$((i + 1))\ndone\nprintf READY\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var elapsed = Stopwatch.StartNew();
            string result = Run(script);
            output.WriteLine($"stderrBurstBytes={4096 * 128}, stdout={result}, elapsedMs={elapsed.ElapsedMilliseconds}");
            Assert.Equal("READY", result);
        }
        finally
        {
            File.Delete(script);
            Directory.Delete(root);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public async Task Native_topology_tool_refuses_a_descendant_held_output_pipe_at_its_deadline()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix topology helper process control."); return; }
        string root = Path.Combine(Path.GetTempPath(), "filecat-unix-tool-pipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string script = Path.Combine(root, "tool.sh");
        string release = script + ".release", done = script + ".done";
        Task<string>? pending = null;
        try
        {
            File.WriteAllText(script, "#!/bin/sh\n(i=0; while [ \"$i\" -lt 300 ] && [ ! -f \"$0.release\" ]; do " +
                "/bin/sleep 0.1; i=$((i + 1)); done; printf done > \"$0.done\") &\nprintf READY\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var elapsed = Stopwatch.StartNew();
            pending = Task.Run(() => Run(script));
            Assert.Same(pending, await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)));
            var error = await Record.ExceptionAsync(async () => await pending);
            output.WriteLine($"error={error?.GetType().Name}, inner={error?.InnerException?.GetType().Name}, elapsedMs={elapsed.ElapsedMilliseconds}");
            var cause = error is TargetInvocationException invocation ? invocation.InnerException : error;
            Assert.Contains("did not answer", Assert.IsType<IOException>(cause).Message);
        }
        finally
        {
            // Release only our finite child, even if the old wrapper is still waiting on its pipe.
            File.WriteAllText(release, "release");
            Assert.True(SpinWait.SpinUntil(() => File.Exists(done), TimeSpan.FromSeconds(3)));
            if (pending is not null)
                try { await pending.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None); }
                catch (IOException) { /* The corrected helper refused the held pipe. */ }
                catch (TargetInvocationException) { /* Reflection can wrap the refusal. */ }
            File.Delete(done);
            File.Delete(release);
            File.Delete(script);
            Directory.Delete(root);
            Assert.False(Directory.Exists(root));
        }
    }
}
