using System.Diagnostics;
using System.Reflection;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

public sealed class UnixDeviceToolOutputBudgetTests(ITestOutputHelper output)
{
    private static readonly MethodInfo RunTool = typeof(UnixDisks).GetMethod("Run",
        BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(string), typeof(string[])], null)
        ?? throw new InvalidOperationException("The Unix topology helper is missing.");

    [Fact]
    public void Oversized_topology_helper_output_is_refused_before_retaining_the_whole_stream()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This owned topology helper requires Unix.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-topology-output-").FullName;
        string script = Path.Combine(root, "owned-tool");
        byte[] bulk = Enumerable.Repeat((byte)'x', (4 << 20) + 4096).ToArray();
        File.WriteAllBytes(script + ".bulk", bulk);
        File.WriteAllText(script, "#!/bin/sh\n/bin/cat \"$0.bulk\"\nprintf READY\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var elapsed = Stopwatch.StartNew();
            var error = Record.Exception(() => RunTool.Invoke(null, [script, Array.Empty<string>()]));
            var cause = error is TargetInvocationException invocation ? invocation.InnerException : error;
            output.WriteLine($"elapsedMs={elapsed.ElapsedMilliseconds}, error={cause?.GetType().Name}");
            Assert.Contains("output exceeded", Assert.IsType<IOException>(cause).Message);
            Assert.Equal(bulk, File.ReadAllBytes(script + ".bulk"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
