using System.Security.Cryptography;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationCacheKeyIdentityTests
{
    [Fact]
    public void Distinct_sidecar_source_sets_have_distinct_result_keys()
    {
        const string file = "/owned/item.bin";
        var single = new[] { ("a.sha256:10:20;b.sha256", 30L, 40L) };
        var pair = new[] { ("a.sha256", 10L, 20L), ("b.sha256", 30L, 40L) };

        Assert.NotEqual(
            VerificationCache.KeyOf(file, 99, 1234, single),
            VerificationCache.KeyOf(file, 99, 1234, pair));
    }

    [Fact]
    public void Replaced_unix_sidecars_cannot_reuse_the_previous_matching_result()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("The filename control requires Unix separators inside a filename.");

        using var root = new TempDir();
        string target = root.File("item.bin", "owned-good-payload");
        File.SetLastWriteTimeUtc(target, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string good = Hash("owned-good-payload") + " *item.bin\n";
        string bad = Hash("owned-other-payload") + " *item.bin\n";
        long size = System.Text.Encoding.UTF8.GetByteCount(good);
        Assert.Equal(size, System.Text.Encoding.UTF8.GetByteCount(bad));
        var firstTime = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondTime = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        string joinedName = $"a.sha256:{size}:{firstTime.Ticks};b.sha256";
        string joined;
        try { joined = root.File(joinedName, good); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            Assert.Skip("The owned Unix filesystem rejects a colon/semicolon filename.");
            return;
        }
        File.SetLastWriteTimeUtc(joined, secondTime);
        Assert.Equal((size, secondTime.Ticks), (new FileInfo(joined).Length, File.GetLastWriteTimeUtc(joined).Ticks));

        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []) { Settle = TimeSpan.Zero };
        Assert.Equal(VerificationState.Matches, service.Automatic(target, TestContext.Current.CancellationToken)!.State);
        File.Delete(joined);
        string first = root.File("a.sha256", good);
        string second = root.File("b.sha256", bad);
        File.SetLastWriteTimeUtc(first, firstTime);
        File.SetLastWriteTimeUtc(second, secondTime);
        Assert.Equal((size, firstTime.Ticks), (new FileInfo(first).Length, File.GetLastWriteTimeUtc(first).Ticks));
        Assert.Equal((size, secondTime.Ticks), (new FileInfo(second).Length, File.GetLastWriteTimeUtc(second).Ticks));
        service.FolderChanged(root.Path, TestContext.Current.CancellationToken);
        Assert.Equal(2, service.SidecarsOf(root.Path, TestContext.Current.CancellationToken).Checksums("item.bin").Count);
        // The remembered hash is of earlier bytes, and no check ever found this file to differ from the new sidecars: it
        // stays neutral until the current bytes are read (FCSEC-006; R-M13 keeps only a failure a check found).
        Assert.Equal(VerificationState.NotChecked, service.Automatic(target, TestContext.Current.CancellationToken)!.State);
        Assert.Equal(VerificationState.Differs, service.OnRequest(target, TestContext.Current.CancellationToken)!.State);
        Assert.Equal("owned-good-payload", File.ReadAllText(target));
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
