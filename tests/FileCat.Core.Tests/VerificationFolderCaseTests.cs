using System.Security.Cryptography;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationFolderCaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Case_distinct_unix_folders_keep_their_own_checksum_claims(bool reverse)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Windows folder-path comparison is case-insensitive for this control.");

        using var root = new TempDir();
        string lower = root.Dir("source"), upper = root.Dir("SOURCE");
        string lowerMarker = root.File("source/marker.txt", "lower");
        root.File("SOURCE/marker.txt", "upper");
        if (File.ReadAllText(lowerMarker) != "lower")
            Assert.Skip("The owned filesystem aliases case-only folder names.");

        string lowerFile = root.File("source/item.bin", "first-owned-payload");
        string upperFile = root.File("SOURCE/item.bin", "second-owned-payload");
        root.File("source/item.bin.sha256", Hash("first-owned-payload") + "\n");
        root.File("SOURCE/item.bin.sha256", Hash("second-owned-payload") + "\n");
        var old = DateTime.UtcNow.AddMinutes(-2);
        File.SetLastWriteTimeUtc(lowerFile, old);
        File.SetLastWriteTimeUtc(upperFile, old);
        Directory.SetLastWriteTimeUtc(lower, old);
        Directory.SetLastWriteTimeUtc(upper, old);
        Assert.Equal(Directory.GetLastWriteTimeUtc(lower).Ticks, Directory.GetLastWriteTimeUtc(upper).Ticks);

        string firstFolder = reverse ? upper : lower, secondFolder = reverse ? lower : upper;
        string firstFile = reverse ? upperFile : lowerFile, secondFile = reverse ? lowerFile : upperFile;
        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []);
        var first = service.Automatic(firstFile, TestContext.Current.CancellationToken);
        var secondScanned = service.SidecarsOf(secondFolder, TestContext.Current.CancellationToken);
        var second = service.Automatic(secondFile, TestContext.Current.CancellationToken);

        Assert.Equal(VerificationState.Matches, first!.State);
        Assert.Equal((secondFolder, VerificationState.Matches), (secondScanned.Folder, second!.State));
        Assert.Equal(Hash("first-owned-payload"), Hash(File.ReadAllText(lowerFile)));
        Assert.Equal(Hash("second-owned-payload"), Hash(File.ReadAllText(upperFile)));
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
