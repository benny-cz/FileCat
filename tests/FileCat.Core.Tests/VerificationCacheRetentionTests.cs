using System.Text.Json;
using System.Runtime.InteropServices;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class VerificationCacheRetentionTests(ITestOutputHelper output)
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

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

    [Fact]
    public void Damaged_oversized_cache_line_does_not_allocate_it_or_hide_later_valid_results()
    {
        string root = Directory.CreateTempSubdirectory("filecat-verification-cache-line-").FullName;
        string file = Path.Combine(root, "verification.jsonl");
        try
        {
            using (var writer = new StreamWriter(file))
            {
                writer.Write(new string('X', 16 * 1024 * 1024));
                writer.WriteLine();
                writer.WriteLine(JsonSerializer.Serialize(new VerificationCacheEntry(
                    "after-damage", VerificationState.Matches, "retained", [])));
            }
            var cache = new VerificationCache(file);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = cache.Get("after-damage");
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine(JsonSerializer.Serialize(new { Case = "oversized-line", Allocated = allocated }));
            Assert.Equal("retained", result?.Text);
            Assert.True(allocated < 16 * 1024 * 1024, $"Loading one cache line allocated {allocated:N0} bytes.");
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public void Existing_cache_fifo_does_not_wait_for_a_writer()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix FIFO control.");
        string root = Directory.CreateTempSubdirectory("filecat-verification-cache-fifo-").FullName;
        try
        {
            string file = Path.Combine(root, "verification.jsonl");
            Assert.Equal(0, MakeFifo(file, 384));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.Null(new VerificationCache(file).Get("missing"));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Cache loading waited {clock.Elapsed}.");
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public void Persisted_cache_above_the_total_budget_is_replaced_on_next_result()
    {
        string root = Directory.CreateTempSubdirectory("filecat-verification-cache-file-").FullName;
        string file = Path.Combine(root, "verification.jsonl");
        try
        {
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
                stream.SetLength(64L * 1024 * 1024 + 1);
            var cache = new VerificationCache(file);
            Assert.Null(cache.Get("missing"));
            cache.Put("new", Result("bounded"));
            Assert.True(new FileInfo(file).Length < 1024 * 1024);
            Assert.Equal("bounded", new VerificationCache(file).Get("new")?.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
