using System.Diagnostics;
using FileCat.Core.Operations;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class ChecksumManifestBoundedInputTests
{
    [Fact]
    public void A_regular_manifest_still_parses_within_its_budget()
    {
        string root = Directory.CreateTempSubdirectory("filecat-manifest-budget-").FullName;
        try
        {
            string path = Path.Combine(root, "SHA256SUMS");
            string line = new string('a', 64) + " *target.bin\n";
            File.WriteAllText(path, line);
            Assert.Single(ChecksumManifests.Load(path, line.Length).Entries);
            Assert.Throws<InvalidDataException>(() => ChecksumManifests.Load(path, line.Length - 1));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unix_zero_length_device_cannot_escape_the_explicit_manifest_budget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The special-file control requires /dev/zero on Unix.");
        var source = new FileInfo("/dev/zero");
        Assert.True(source.Exists);
        Assert.Equal(0, source.Length);
        var clock = Stopwatch.StartNew();
        Assert.Throws<InvalidDataException>(() => ChecksumManifests.Load(source.FullName, 4096));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Unix_automatic_sidecar_scan_does_not_read_an_endless_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The special-file control requires /dev/zero on Unix.");
        string root = Directory.CreateTempSubdirectory("filecat-sidecar-budget-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "target.bin"), "owned");
            string sidecar = Path.Combine(root, "target.bin.sha256");
            File.CreateSymbolicLink(sidecar, "/dev/zero");
            // FileInfo reports the short link's own length on these Unix hosts;
            // either way it passes the sidecar's metadata-size admission check.
            Assert.InRange(new FileInfo(sidecar).Length, 0, FolderSidecars.MaxChecksumFileBytes);
            var clock = Stopwatch.StartNew();
            FolderSidecars result = FolderSidecars.Scan(root, TestContext.Current.CancellationToken);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Empty(result.Checksums("target.bin"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
