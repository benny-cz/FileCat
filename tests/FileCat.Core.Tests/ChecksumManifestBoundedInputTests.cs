using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Operations;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class ChecksumManifestBoundedInputTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

    [Fact]
    public void Unix_checksum_manifest_fifo_is_refused_without_waiting_for_a_writer()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix FIFO control.");
        string root = Directory.CreateTempSubdirectory("filecat-manifest-fifo-").FullName;
        try
        {
            string path = Path.Combine(root, "target.bin.sha256");
            Assert.Equal(0, MakeFifo(path, 384));
            var clock = Stopwatch.StartNew();
            Assert.ThrowsAny<IOException>(() => ChecksumManifests.Load(path, FolderSidecars.MaxChecksumFileBytes));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"FIFO refusal took {clock.Elapsed}.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unix_automatic_sidecar_scan_does_not_wait_on_a_fifo_link()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix FIFO control.");
        string root = Directory.CreateTempSubdirectory("filecat-sidecar-fifo-").FullName;
        try
        {
            string pipe = Path.Combine(root, "source.pipe");
            Assert.Equal(0, MakeFifo(pipe, 384));
            File.WriteAllText(Path.Combine(root, "target.bin"), "owned");
            File.CreateSymbolicLink(Path.Combine(root, "target.bin.sha256"), pipe);
            var clock = Stopwatch.StartNew();
            FolderSidecars scan = FolderSidecars.Scan(root, TestContext.Current.CancellationToken);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Sidecar scan took {clock.Elapsed}.");
            Assert.Empty(scan.Checksums("target.bin"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Bare_checksum_fallback_does_not_materialize_an_oversized_file()
    {
        string root = Directory.CreateTempSubdirectory("filecat-bare-sum-budget-").FullName;
        try
        {
            string path = Path.Combine(root, "target.bin.sha256");
            string hash = new string('a', 64);
            File.WriteAllText(path, hash + "\n");
            Assert.Equal(hash, FolderSidecars.BareChecksum(path, ChecksumKind.Sha256));

            File.WriteAllText(path, new string('x', (int)FolderSidecars.MaxChecksumFileBytes + 1));
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Null(FolderSidecars.BareChecksum(path, ChecksumKind.Sha256));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 1_000_000, $"Bare sidecar fallback allocated {allocated:N0} bytes.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
