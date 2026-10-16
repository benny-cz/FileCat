using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationWindowsCaseTests
{
    [Fact]
    public async Task Case_sensitive_NTFS_directory_keeps_file_and_folder_claims_separate()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("This control requires Windows per-directory case sensitivity.");

        using var root = new TempDir();
        using var process = Process.Start(new ProcessStartInfo("fsutil.exe")
        {
            ArgumentList = { "file", "setCaseSensitiveInfo", root.Path, "enable" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        _ = await stdout;
        _ = await stderr;
        if (process.ExitCode != 0)
            Assert.Skip("The owned Windows directory could not enable case sensitivity (requires supported NTFS and an authorized token).");
        Assert.Same(StringComparer.Ordinal, WindowsCaseSensitivity.NamesIn(root.Path));

        string lower = root.File("item.bin", "lower-owned-payload");
        string upper = root.File("ITEM.bin", "UPPER-OWNED-PAYLOAD");
        Assert.NotEqual(File.ReadAllText(lower), File.ReadAllText(upper));
        root.File("item.bin.sha256", Hash("lower-owned-payload") + "\n");
        root.File("ITEM.bin.sha256", Hash("UPPER-OWNED-PAYLOAD") + "\n");
        var old = DateTime.UtcNow.AddMinutes(-2);
        File.SetLastWriteTimeUtc(lower, old);
        File.SetLastWriteTimeUtc(upper, old);

        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []) { Settle = TimeSpan.Zero };
        var scan = service.SidecarsOf(root.Path, TestContext.Current.CancellationToken);
        Assert.Single(scan.Checksums("item.bin"));
        Assert.Single(scan.Checksums("ITEM.bin"));
        Assert.Equal(VerificationState.Matches, service.Automatic(lower, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(VerificationState.Matches, service.Automatic(upper, TestContext.Current.CancellationToken)!.State);

        string lowerFolder = root.Dir("source"), upperFolder = root.Dir("SOURCE");
        string lowerFile = root.File("source/payload.bin", "lower-owned-payload");
        string upperFile = root.File("SOURCE/payload.bin", "UPPER-OWNED-PAYLOAD");
        Assert.NotEqual(File.ReadAllText(lowerFile), File.ReadAllText(upperFile));
        root.File("source/payload.bin.sha256", Hash("lower-owned-payload") + "\n");
        root.File("SOURCE/payload.bin.sha256", Hash("UPPER-OWNED-PAYLOAD") + "\n");
        File.SetLastWriteTimeUtc(lowerFile, old);
        File.SetLastWriteTimeUtc(upperFile, old);
        Directory.SetLastWriteTimeUtc(lowerFolder, old);
        Directory.SetLastWriteTimeUtc(upperFolder, old);
        Assert.Equal(Directory.GetLastWriteTimeUtc(lowerFolder).Ticks, Directory.GetLastWriteTimeUtc(upperFolder).Ticks);

        var folders = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []) { Settle = TimeSpan.Zero };
        Assert.Equal(VerificationState.Matches, folders.Automatic(lowerFile, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(upperFolder, folders.SidecarsOf(upperFolder, TestContext.Current.CancellationToken).Folder);
        Assert.Equal(VerificationState.Matches, folders.Automatic(upperFile, TestContext.Current.CancellationToken)!.State);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
