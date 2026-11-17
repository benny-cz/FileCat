using System.Diagnostics;
using System.Xml.Linq;

namespace FileCat.App.Tests;

/// <summary>Process-wide clocks and heap measurements require a process containing only the intended probe.</summary>
internal static class IsolatedAppTestProcess
{
    private const string EnvironmentName = "FILECAT_ISOLATED_APP_TEST";
    internal static bool IsChild(Type fixture, string method)
    {
        string? current = Environment.GetEnvironmentVariable(EnvironmentName);
        if (current is null) return false;
        if (current != fixture.FullName + "." + method) throw new InvalidOperationException("The isolated test filter executed an unexpected probe.");
        return true;
    }

    internal static async Task<string> RunAsync(Type fixture, string method)
    {
        string directory = Directory.CreateTempSubdirectory("filecat-app-test-probe-").FullName;
        string report = Path.Join(directory, "result.xml");
        string? processPath = Environment.ProcessPath;
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? (Path.GetFileNameWithoutExtension(processPath) == "dotnet" ? processPath : null) ?? "dotnet";
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(fixture.Assembly.Location);
        start.ArgumentList.Add("-method"); start.ArgumentList.Add(fixture.FullName + "." + method);
        start.ArgumentList.Add("-noLogo"); start.ArgumentList.Add("-xml"); start.ArgumentList.Add(report);
        start.Environment[EnvironmentName] = fixture.FullName + "." + method;
        using var child = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        bool started = false;
        try
        {
            started = child.Start(); Assert.True(started);
            var stdout = child.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = child.StandardError.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            string diagnostic = await stdout + "\n" + await stderr;
            Assert.True(child.ExitCode == 0, $"Isolated {method} exited {child.ExitCode}: {diagnostic}");
            Assert.True(File.Exists(report), "The isolated probe did not publish its test result. " + diagnostic);
            var test = Assert.Single(XDocument.Load(report).Descendants("test"));
            Assert.Equal(method, (string?)test.Attribute("method"));
            Assert.Equal("Pass", (string?)test.Attribute("result"));
            return test.Element("output")?.Value ?? string.Empty;
        }
        finally
        {
            try
            {
                if (started && !child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                try { File.Delete(report); Directory.Delete(directory); }
                catch (IOException) { }
            }
        }
    }
}
