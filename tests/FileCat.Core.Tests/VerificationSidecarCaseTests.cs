using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationSidecarCaseTests
{
    [Theory]
    [InlineData("bare")]
    [InlineData("manifest")]
    [InlineData("signature")]
    public void Ordinary_windows_folder_keeps_case_insensitive_sidecar_aliases(string kind)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("This control requires an ordinary case-insensitive Windows folder.");

        using var root = new TempDir();
        if (WindowsCaseSensitivity.NamesIn(root.Path) != StringComparer.OrdinalIgnoreCase)
            Assert.Skip("The owned Windows folder is case-sensitive or its flag is unavailable.");
        string file = root.File("item.bin", "owned-payload");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-2));
        string hash = Hash("owned-payload");
        switch (kind)
        {
            case "bare": root.File("ITEM.bin.sha256", hash + "\n"); break;
            case "manifest": root.File("SHA256SUMS", hash + " *ITEM.bin\n"); break;
            case "signature": root.File("ITEM.bin.sig", "owned-invalid-signature"); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []);
        var scan = service.SidecarsOf(root.Path, TestContext.Current.CancellationToken);
        if (kind == "signature")
            Assert.Equal(file, Assert.Single(scan.Signatures("item.bin")).Signed);
        else
        {
            Assert.Single(scan.Checksums("item.bin"));
            Assert.Equal(VerificationState.Matches, service.Automatic(file, TestContext.Current.CancellationToken)!.State);
        }
    }

    [Theory]
    [InlineData("bare")]
    [InlineData("manifest")]
    [InlineData("signature")]
    public void Case_insensitive_mac_alias_keeps_its_existing_sidecar_coverage(string kind)
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("This control requires a case-insensitive macOS filesystem.");

        using var root = new TempDir();
        string file = root.File("item.bin", "owned-payload");
        if (!File.Exists(Path.Combine(root.Path, "ITEM.bin")))
            Assert.Skip("The owned Mac filesystem is case-sensitive.");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-2));
        string hash = Hash("owned-payload");
        switch (kind)
        {
            case "bare": root.File("ITEM.bin.sha256", hash + "\n"); break;
            case "manifest": root.File("SHA256SUMS", hash + " *ITEM.bin\n"); break;
            case "signature": root.File("ITEM.bin.sig", "owned-invalid-signature"); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []);
        var scan = service.SidecarsOf(root.Path, TestContext.Current.CancellationToken);
        if (kind == "signature")
            Assert.Equal(file, Assert.Single(scan.Signatures("item.bin")).Signed);
        else
        {
            Assert.Single(scan.Checksums("item.bin"));
            Assert.Equal(VerificationState.Matches, service.Automatic(file, TestContext.Current.CancellationToken)!.State);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Case_distinct_unix_files_keep_their_own_sidecar_claims(bool reverse)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Windows does not provide this case-distinct filename control.");

        using var root = new TempDir();
        string lower = root.File("item.bin", "lower-owned-payload");
        string upper = root.File("ITEM.bin", "UPPER-OWNED-PAYLOAD");
        if (File.ReadAllText(lower) != "lower-owned-payload")
            Assert.Skip("The owned filesystem aliases case-only file names.");

        root.File("item.bin.sha256", Hash("lower-owned-payload") + "\n");
        root.File("ITEM.bin.sha256", Hash("UPPER-OWNED-PAYLOAD") + "\n");
        var old = DateTime.UtcNow.AddMinutes(-2);
        File.SetLastWriteTimeUtc(lower, old);
        File.SetLastWriteTimeUtc(upper, old);

        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []);
        var sidecars = service.SidecarsOf(root.Path, TestContext.Current.CancellationToken);
        Assert.Single(sidecars.Checksums("item.bin"));
        Assert.Single(sidecars.Checksums("ITEM.bin"));

        string first = reverse ? upper : lower, second = reverse ? lower : upper;
        Assert.Equal(VerificationState.Matches, service.Automatic(first, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(VerificationState.Matches, service.Automatic(second, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(Hash("lower-owned-payload"), Hash(File.ReadAllText(lower)));
        Assert.Equal(Hash("UPPER-OWNED-PAYLOAD"), Hash(File.ReadAllText(upper)));
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
