using System.Reflection;
using System.Security.Cryptography;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationFolderRetentionTests
{
    [Fact]
    public async Task Concurrent_folder_scans_keep_the_shared_cache_bounded()
    {
        using var root = new TempDir();
        var folders = Enumerable.Range(0, 640).Select(i => root.Dir(i.ToString("000"))).ToArray();
        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []);
        var ct = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            for (int i = worker; i < folders.Length; i += 16)
                service.SidecarsOf(folders[i], ct);
        }, ct)));
        var cache = (System.Collections.IDictionary)typeof(VerificationService)
            .GetField("_folders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.Equal(512, cache.Count);
    }

    [Fact]
    public void Browsed_folders_release_old_sidecar_payloads_without_losing_change_notification()
    {
        using var root = new TempDir();
        string first = root.Dir("000");
        string file = root.File("000/item.bin", "owned bytes");
        string sidecar = root.File("000/item.bin.sha256",
            Convert.ToHexStringLower(SHA256.HashData("owned bytes"u8)) + "\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-2));
        var service = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []);
        int notifications = 0;
        service.SidecarsChanged += folder =>
        {
            Assert.Equal(first, folder);
            notifications++;
        };

        Assert.Equal(VerificationState.Matches, service.Automatic(file, TestContext.Current.CancellationToken)!.State);
        for (int i = 1; i <= 520; i++)
            service.SidecarsOf(root.Dir(i.ToString("000")), TestContext.Current.CancellationToken);

        var folders = (System.Collections.IDictionary)typeof(VerificationService)
            .GetField("_folders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        Assert.InRange(folders.Count, 1, 512);
        Assert.Equal(0, notifications);

        File.WriteAllText(sidecar, Convert.ToHexStringLower(SHA256.HashData("different"u8)) + "\n");
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(1));
        service.FolderChanged(first, TestContext.Current.CancellationToken);
        Assert.Equal(1, notifications);
        Assert.Equal(VerificationState.Differs, service.Automatic(file, TestContext.Current.CancellationToken)!.State);
        Assert.Equal("owned bytes", File.ReadAllText(file));
    }
}
