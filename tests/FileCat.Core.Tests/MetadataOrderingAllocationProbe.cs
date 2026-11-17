using System.Diagnostics;
using System.Xml.Linq;

namespace FileCat.Core.Tests;

/// <summary>Measure the comparer without another test or runtime tier promotion entering its sample.</summary>
internal static class MetadataOrderingAllocationProbe
{
    private const string ChildMarker = "FILECAT_PRINCIPAL_ALLOCATION_PROBE", ControlMarker = "FILECAT_PRINCIPAL_ALLOCATING_CONTROL";
    private const string Method = nameof(MetadataOrderingSnapshotTests.Canonical_principal_comparison_does_not_allocate_for_each_comparison);
    internal static bool IsChild => Environment.GetEnvironmentVariable(ChildMarker) == Method;
    internal static bool AllocatingControl => Environment.GetEnvironmentVariable(ControlMarker) == "1";

    internal static async Task<string> RunAsync(bool allocatingControl)
    {
        string directory = Directory.CreateTempSubdirectory("filecat-principal-allocation-").FullName;
        string report = Path.Join(directory, "result.xml");
        string? processPath = Environment.ProcessPath;
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? (Path.GetFileNameWithoutExtension(processPath) == "dotnet" ? processPath : null) ?? "dotnet";
        var start = new ProcessStartInfo(dotnet)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(MetadataOrderingSnapshotTests).Assembly.Location);
        start.ArgumentList.Add("-method"); start.ArgumentList.Add(typeof(MetadataOrderingSnapshotTests).FullName + "." + Method);
        start.ArgumentList.Add("-noLogo"); start.ArgumentList.Add("-xml"); start.ArgumentList.Add(report);
        start.Environment[ChildMarker] = Method;
        start.Environment[ControlMarker] = allocatingControl ? "1" : "0";
        start.Environment["DOTNET_TieredCompilation"] = "0";
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
            Assert.Equal(allocatingControl ? 1 : 0, child.ExitCode);
            Assert.True(File.Exists(report), "The isolated allocation probe published no result: " + diagnostic);
            var test = Assert.Single(XDocument.Load(report).Descendants("test"));
            Assert.Equal(Method, (string?)test.Attribute("method"));
            Assert.Equal(allocatingControl ? "Fail" : "Pass", (string?)test.Attribute("result"));
            string measurement = test.Element("output")?.Value ?? string.Empty;
            Assert.Contains("PRINCIPAL_ALLOCATION comparisons=10000 allocatedBytes=", measurement);
            Assert.Contains("allocatingControl=" + allocatingControl, measurement);
            if (allocatingControl)
            {
                var failure = Assert.Single(test.Elements("failure"));
                Assert.Equal("Xunit.Sdk.InRangeException", (string?)failure.Attribute("exception-type"));
                Assert.Contains("Assert.InRange() Failure", failure.Element("message")?.Value ?? string.Empty);
            }
            return measurement;
        }
        finally
        {
            try
            {
                if (started && !child.HasExited)
                { child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
            finally
            {
                try { File.Delete(report); Directory.Delete(directory); }
                catch (IOException) { }
            }
        }
    }
}
