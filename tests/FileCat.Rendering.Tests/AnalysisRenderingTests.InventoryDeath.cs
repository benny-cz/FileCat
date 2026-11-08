using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    public static IEnumerable<object[]> InventoryDeathBoundaries()
    {
        string[] boundaries = ["stage-created", "stage-flushed", "review-ready", "marker-moved", "incoming-published", "incoming-closed",
            "backup-flushed", "backup-published", "backup-closed", "before-switch", "after-switch", "main-closed",
            "catalog-flushed", "catalog-published", "catalog-closed", "previous-removed", "marker-removed", "writer-closed", "completed"];
        foreach (string boundary in boundaries) yield return [boundary, boundary == "marker-moved"];
        foreach (string boundary in new[] { "incoming-published", "incoming-closed", "backup-published", "after-switch", "catalog-published", "catalog-closed" }) yield return [boundary, true];
    }
    [AvaloniaTheory]
    [MemberData(nameof(InventoryDeathBoundaries))]
    public async Task AbruptOwnedInventoryDeathPreservesNamedBytesAndAllowsFreshExplicitRecovery(string boundary, bool restore)
    {
        OrdinaryPreparation(); var cancellation = TestContext.Current.CancellationToken;
        string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FileCatValidation", "inventory-death-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent); string root = Path.Combine(parent, "scratch");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll"));
        start.ArgumentList.Add("--analysis-inventory-death"); start.ArgumentList.Add(parent); start.ArgumentList.Add(boundary);
        if (OperatingSystem.IsWindows()) start.Environment["FILECAT_TEST_SCRATCH_WINDOWS_ADAPTER"] = Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll");
        using var child = Process.Start(start)!; var stderr = child.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            string? line = await child.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(25), cancellation);
            if (line is null || !line.StartsWith("INVENTORY_DEATH_READY ", StringComparison.Ordinal))
                Assert.Fail("The exact owned child did not reach " + boundary + ": " + line + (child.HasExited ? " " + await stderr : ""));
            using var document = JsonDocument.Parse(line!["INVENTORY_DEATH_READY ".Length..]); var ready = document.RootElement;
            Assert.Equal(child.Id, ready.GetProperty("pid").GetInt32()); Assert.Equal(boundary, ready.GetProperty("boundary").GetString());
            Assert.False(child.HasExited);
            string foreign = Path.Combine(root, "user-file.txt"); File.WriteAllText(foreign, "preserve this independent neighbor");
            child.Kill(); await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            var clock = Stopwatch.StartNew();
            while (Directory.EnumerateFiles(root, ".staging-*").Any() && clock.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(10, cancellation);
            Assert.Empty(Directory.EnumerateFiles(root, ".staging-*"));
            Assert.Equal("preserve this independent neighbor", File.ReadAllText(foreign));
            Assert.Equal(ready.GetProperty("otherSha256").GetString(), Hash(Path.Combine(root, "other.fcdsa")));
            using var io = new DeviceIoScheduler(); string device = PathUtil.GetDeviceKey(parent);
            await using var owned = await OwnedAnalysisStore.OpenAsync(io, device, () => OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(parent, "scratch") : new UnixAnalysisPrivateDirectory(parent, "scratch"), cancellation);
            var beforeRead = Directory.EnumerateFiles(root).Order(StringComparer.Ordinal).ToArray();
            using (var catalog = await owned.ReadCatalogAsync(cancellation))
            {
                Assert.Equal(beforeRead, Directory.EnumerateFiles(root).Order(StringComparer.Ordinal));
                if (catalog.PendingReplacements.Length == 0)
                {
                    bool old = boundary is "stage-created" or "stage-flushed" or "review-ready";
                    Assert.Equal(ready.GetProperty(old ? "previousSha256" : "incomingSha256").GetString(), Hash(Path.Combine(root, "owned.fcdsa")));
                    Assert.Equal(2, catalog.Entries.Length);
                }
                else
                {
                    var pending = Assert.Single(catalog.PendingReplacements); catalog.Dispose();
                    var review = await owned.PrepareReplacementRecoveryAsync(pending.OperationId, cancellation);
                    Assert.True(restore ? review.CanRestorePrevious : review.CanFinishIncoming);
                    var result = await owned.RecoverReplacementAsync(review, restore ? OwnedAnalysisRecoveryAction.RestorePrevious : OwnedAnalysisRecoveryAction.FinishIncoming, cancellation: cancellation);
                    Assert.Equal(restore ? OwnedAnalysisRecoveryState.Restored : OwnedAnalysisRecoveryState.FinishedIncoming, result.State);
                    Assert.Equal(ready.GetProperty(restore ? "previousSha256" : "incomingSha256").GetString(), Hash(Path.Combine(root, "owned.fcdsa")));
                    using var after = await owned.ReadCatalogAsync(cancellation); Assert.Empty(after.PendingReplacements); Assert.Equal(2, after.Entries.Length);
                }
            }
            Assert.Equal("preserve this independent neighbor", File.ReadAllText(foreign));
            Assert.Equal(ready.GetProperty("otherSha256").GetString(), Hash(Path.Combine(root, "other.fcdsa")));
            Console.WriteLine($"Owned inventory death {boundary}, restore={restore}: child exited, staging absent, neighbor kept, fresh recovery verified.");
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            Assert.True(child.HasExited, "The exact owned child must exit before another tester uses the machine.");
            Directory.Delete(parent, recursive: true);
        }
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    }
}
