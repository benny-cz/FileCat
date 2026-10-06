using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

public sealed class BrokerFileTrustTests
{
    [Fact]
    public void An_uninstalled_or_missing_helper_is_refused_before_requesting_elevation()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The administrator helper launches through the Windows shell.");
        string path = Path.Combine(Path.GetTempPath(), "FileCat-untrusted-helper-" + Guid.NewGuid().ToString("N") + ".exe");
        // A malformed owned file contains no executable code, even if the pre-launch boundary regresses.
        File.WriteAllBytes(path, [0]);
        try
        {
            var uninstalled = Assert.Throws<IOException>(() => ElevationBroker.Launch(path, "owned-unused.plan", new string('0', 64), 0));
            Assert.Contains("Nothing was run", uninstalled.Message, StringComparison.Ordinal);
            File.Delete(path);
            var missing = Assert.Throws<IOException>(() => ElevationBroker.Launch(path, "owned-unused.plan", new string('0', 64), 0));
            Assert.Contains("Nothing was run", missing.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
