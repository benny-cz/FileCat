using System.Text.Json;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationCacheRetentionTests(ITestOutputHelper output)
{
    private static VerificationResult Result(string text) => new(VerificationState.Matches, text, []);

    [Fact]
    public void In_memory_cache_retires_old_results_after_its_compaction_window()
    {
        var cache = new VerificationCache(null);
        for (int i = 0; i <= 2 * VerificationCache.MaxEntries; i++)
            cache.Put("owned-" + i, Result("result-" + i));
        cache.Put("owned-0", Result("refreshed"));

        bool oldRetained = cache.Get("owned-1") is not null;
        output.WriteLine(JsonSerializer.Serialize(new { Case = "memory-compaction-window", OldRetained = oldRetained }));
        Assert.False(oldRetained);
        Assert.Equal("refreshed", cache.Get("owned-0")?.Text);
        Assert.Equal("result-" + (2 * VerificationCache.MaxEntries),
            cache.Get("owned-" + (2 * VerificationCache.MaxEntries))?.Text);
    }

    [Fact]
    public void Loading_an_oversized_existing_cache_keeps_recent_unique_results()
    {
        string root = Directory.CreateTempSubdirectory("filecat-verification-cache-retention-").FullName;
        string file = Path.Combine(root, "verification.jsonl");
        try
        {
            using (var writer = new StreamWriter(file))
            {
                for (int i = 0; i <= 2 * VerificationCache.MaxEntries; i++)
                    writer.WriteLine(JsonSerializer.Serialize(new VerificationCacheEntry(
                        "owned-" + i, VerificationState.Matches, "result-" + i, [])));
                writer.WriteLine(JsonSerializer.Serialize(new VerificationCacheEntry(
                    "owned-0", VerificationState.Matches, "refreshed", [])));
            }
            long bytes = new FileInfo(file).Length;
            string before = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
            var cache = new VerificationCache(file);
            bool oldRetained = cache.Get("owned-1") is not null;
            output.WriteLine(JsonSerializer.Serialize(new { Case = "oversized-load", Bytes = bytes,
                OldRetained = oldRetained, SourceSha256 = before, Root = root }));
            Assert.False(oldRetained);
            Assert.Equal("refreshed", cache.Get("owned-0")?.Text);
            Assert.Equal("result-" + (2 * VerificationCache.MaxEntries),
                cache.Get("owned-" + (2 * VerificationCache.MaxEntries))?.Text);
            Assert.Equal(before,
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
