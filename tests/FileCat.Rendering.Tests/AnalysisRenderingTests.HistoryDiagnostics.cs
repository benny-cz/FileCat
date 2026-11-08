using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ActualDiagnosticsZipIncludesOnlyFiniteAnalysisPreferencesAndPreservesPrivateSentinels()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var services = profile.Services;
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token, indexedStorage: false)) { }
        services.Settings.AnalysisPreferences = new() { RollingHistory = new(true, 20, 60, 2L << 30) };
        string sentinel = "private-history-query-" + Guid.NewGuid().ToString("N");
        string folder = services.Paths.AnalysisSnapshotsDirectory;
        var files = new[] { Path.Combine(folder, "private-query-fixture.json"), Path.Combine(folder, "private-history-fixture.fcdsa") };
        foreach (string file in files) await File.WriteAllTextAsync(file, sentinel, token);
        string exported = await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(profile.Root), _ => DiagnosticsBundle.Write(services, profile.Root), token);
        using var zip = ZipFile.OpenRead(exported);
        Assert.Contains(zip.Entries, entry => entry.FullName == "settings.json");
        foreach (var entry in zip.Entries)
        {
            Assert.DoesNotContain("analysis", entry.FullName, StringComparison.OrdinalIgnoreCase);
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            string text = await reader.ReadToEndAsync(token);
            Assert.DoesNotContain(sentinel, text);
            if (entry.FullName == "settings.json")
            {
                using var settings = JsonDocument.Parse(text);
                var analysis = settings.RootElement.GetProperty("AnalysisPreferences");
                Assert.Equal(new[] { "RollingHistory" }, analysis.EnumerateObject().Select(p => p.Name));
                var history = analysis.GetProperty("RollingHistory");
                Assert.Equal(new[] { "Enabled", "MaximumAgeDays", "MaximumBytes", "MaximumEntries" }, history.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
                Assert.True(history.GetProperty("Enabled").GetBoolean()); Assert.Equal(20, history.GetProperty("MaximumEntries").GetInt32());
            }
        }
        foreach (string file in files) Assert.Equal(sentinel, await File.ReadAllTextAsync(file, token));
    }
}
