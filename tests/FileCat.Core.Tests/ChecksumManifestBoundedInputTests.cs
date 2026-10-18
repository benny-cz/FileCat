using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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
    public void Verification_inputs_that_are_pipes_or_devices_are_refused_at_once()
    {
        // A FIFO on Linux and macOS (an extracted archive can plant one under any name), a device on Windows: hashing it,
        // or reading it as a key, would wait or read forever.
        string root = Directory.CreateTempSubdirectory("filecat-verify-special-").FullName;
        try
        {
            string special = OperatingSystem.IsWindows() ? @"\\.\NUL" : Path.Combine(root, "image.iso");
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(0, MakeFifo(special, 384));
                Assert.Equal(0, MakeFifo(Path.Combine(root, "planted.pub"), 384));
            }
            var ct = TestContext.Current.CancellationToken;
            var clock = Stopwatch.StartNew();
            Assert.True(VerificationFileReader.IsSpecial(special));
            Assert.ThrowsAny<IOException>(() => Verifier.Hash(special, [ChecksumKind.Sha256], ct));
            Assert.ThrowsAny<IOException>(() => Checksums.Compute(special, ChecksumKind.Sha256, ct));
            Assert.ThrowsAny<IOException>(() => Minisign.Blake2b(special, ct));
            // A pipe named like a key is passed over (Windows cannot hold one under a file's name).
            if (!OperatingSystem.IsWindows()) Assert.Empty(Minisign.KeysIn([root]));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Refusing took {clock.Elapsed}.");
            // What cannot be opened at all is no pipe: whoever reads it says why.
            Assert.False(VerificationFileReader.IsSpecial(Path.Combine(root, "absent")));
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
            File.WriteAllText(Path.Combine(root, "target.bin"), "owned");
            File.WriteAllText(path, hash + "\n");
            var ct = TestContext.Current.CancellationToken;
            Assert.Equal(hash, Assert.Single(FolderSidecars.Scan(root, ct).Checksums("target.bin")).Expected);

            // The bare value is read from the bytes the scan read once, within its budget: an oversized file is not read at all.
            File.WriteAllText(path, new string('x', (int)FolderSidecars.MaxChecksumFileBytes + 1));
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Null(VerificationFileReader.ReadAll(path, FolderSidecars.MaxChecksumFileBytes));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 1_000_000, $"Bare sidecar fallback allocated {allocated:N0} bytes.");
            Assert.Empty(FolderSidecars.Scan(root, ct).Checksums("target.bin"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("target.bin", 0)]
    [InlineData("caf\u00e9-\U0001f431.bin", 0)]
    [InlineData("target.bin", 65_536)]
    [InlineData("caf\u00e9-\U0001f431.bin", 65_536)]
    public void A_regular_manifest_uses_an_exact_byte_budget_and_retains_only_the_complete_input(string name, int padding)
    {
        string root = Directory.CreateTempSubdirectory("filecat-manifest-budget-").FullName;
        try
        {
            string path = Path.Combine(root, "SHA256SUMS");
            byte[] bytes = Encoding.UTF8.GetBytes(new string('\n', padding) + new string('a', 64) + " *" + name + "\n");
            File.WriteAllBytes(path, bytes);
            Assert.False(VerificationFileReader.IsSpecial(path));
            // Include multibyte names and a manifest that crosses the reader's chunk boundary. The limit is bytes,
            // and verification/signature callers must receive the same complete bytes that were parsed.
            var manifest = ChecksumManifests.Load(path, bytes.Length, out var retained);
            Assert.Equal(name, Assert.Single(manifest.Entries).Name);
            Assert.Equal(bytes, retained);
            Assert.Equal(bytes, VerificationFileReader.ReadAll(path, bytes.Length));
            Assert.Null(VerificationFileReader.ReadAll(path, bytes.Length - 1));
            Assert.Throws<InvalidDataException>(() => ChecksumManifests.Load(path, bytes.Length - 1));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unix_zero_length_device_is_refused_before_the_manifest_content_budget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The special-file control requires /dev/zero on Unix.");
        var source = new FileInfo("/dev/zero");
        Assert.True(source.Exists);
        Assert.Equal(0, source.Length);
        // Establish that this is an accessible, nonempty source despite the misleading zero metadata length.
        // A permission/open failure must not accidentally satisfy the admission test below.
        using (var probe = File.OpenRead(source.FullName)) Assert.Equal(0, probe.ReadByte());
        var clock = Stopwatch.StartNew();
        Assert.True(VerificationFileReader.IsSpecial(source.FullName));
        var refusal = Assert.ThrowsAny<IOException>(() => ChecksumManifests.Load(source.FullName, 4096));
        Assert.Contains("not a regular file", refusal.Message, StringComparison.Ordinal);
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
