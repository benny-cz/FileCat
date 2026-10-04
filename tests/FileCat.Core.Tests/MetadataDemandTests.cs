using System.Collections.Concurrent;
using System.Security.Cryptography;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

public sealed class MetadataDemandTests
{
    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The owned metadata demand did not reach its checkpoint.");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task A_sidecar_change_cannot_restore_an_inflight_old_verification(bool invalidateAll, bool explicitAnalysis)
    {
        using var dir = new TempDir();
        using var release = new ManualResetEventSlim();
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromMinutes(1));
        var metadata = new MetadataService(io);
        var verification = new VerificationService(new VerificationCache(null), () => 1L << 20, () => []) { Settle = TimeSpan.Zero };
        var bytes = "independent checksum fixture"u8.ToArray();
        string path = Path.Combine(dir.Path, "data.bin");
        string sum = path + ".sha256";
        File.WriteAllBytes(path, bytes);
        File.WriteAllText(sum, Convert.ToHexStringLower(SHA256.HashData(bytes)) + "\n");
        File.SetLastWriteTimeUtc(sum, DateTime.UtcNow.AddMinutes(-1));
        long modified = File.GetLastWriteTimeUtc(path).Ticks;
        var entry = new EntryData("data.bin", EntryKind.File, bytes.Length, modified);
        var started = new TaskCompletionSource<VerificationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invalidationShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int notificationPhase = 0;
        metadata.ValuesChanged += () =>
        {
            if (Volatile.Read(ref notificationPhase) == 0) invalidationShown.TrySetResult();
            else retryShown.TrySetResult();
        };
        int calls = 0, active = 0, invalidations = 0;
        Task<MetadataValue>? analysis = null;
        metadata.Register(new MetadataField("verified", "Verified", MetadataCost.Cheap, _ => true, (p, ct) =>
        {
            Interlocked.Increment(ref active);
            try
            {
                var value = verification.Automatic(p, ct)!;
                if (Interlocked.Increment(ref calls) == 1)
                {
                    started.TrySetResult(value);
                    if (!release.Wait(TimeSpan.FromSeconds(15), ct)) throw new TimeoutException("Owned verification release missing.");
                }
                return value;
            }
            finally { Interlocked.Decrement(ref active); }
        }, value => ((VerificationResult)value!).Text));
        verification.SidecarsChanged += _ =>
        {
            if (invalidateAll) metadata.Invalidate();
            else metadata.Forget("verified", path);
            Interlocked.Increment(ref invalidations);
        };
        try
        {
            if (explicitAnalysis) analysis = Task.Run(() => metadata.Compute("verified", path, entry, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            else Assert.Equal(MetadataState.Pending, metadata.Get("verified", path, entry, "owned", false).State);
            var old = await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(VerificationState.Matches, old.State);
            // The file's bytes/size/time stay identical; only what is supposed to vouch for them changes.
            File.WriteAllText(sum, Convert.ToHexStringLower(SHA256.HashData("different bytes"u8)) + "\n");
            verification.FolderChanged(dir.Path, TestContext.Current.CancellationToken);
            Assert.Equal(1, Volatile.Read(ref invalidations));
            Assert.Equal(VerificationState.Differs, verification.Automatic(path, TestContext.Current.CancellationToken)!.State);
            // Let the invalidation redraw happen while the old producer is held. Once it ends, the real UI needs
            // another notification to ask again; no polling Get calls may manufacture that wakeup for this test.
            await invalidationShown.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Volatile.Write(ref notificationPhase, 1);
            release.Set();
            if (analysis is not null) Assert.Equal(MetadataState.NotRequested, (await analysis).State);
            await retryShown.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            MetadataValue current = default;
            await WaitFor(() => (current = metadata.Get("verified", path, entry, "owned", false)).State == MetadataState.Available);
            Assert.Equal(VerificationState.Differs, Assert.IsType<VerificationResult>(current.Value).State);
            Assert.Equal(2, Volatile.Read(ref calls));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(modified, File.GetLastWriteTimeUtc(path).Ticks);
        }
        finally
        {
            release.Set();
            if (analysis is not null) await analysis;
            await WaitFor(() => Volatile.Read(ref active) == 0);
        }
    }

    [Fact]
    public async Task Forgetting_one_field_keeps_an_unrelated_inflight_value()
    {
        using var release = new ManualResetEventSlim();
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, active = 0;
        metadata.Register(new MetadataField("unrelated", "Unrelated", MetadataCost.Cheap, _ => true, (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            Interlocked.Increment(ref active);
            try
            {
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(15), ct)) throw new TimeoutException("Owned field release missing.");
                return "unrelated value";
            }
            finally { Interlocked.Decrement(ref active); }
        }, value => (string)value!));
        var entry = new EntryData("row.bin", EntryKind.File, 1, 1);
        try
        {
            Assert.Equal(MetadataState.Pending, metadata.Get("unrelated", "owned-row", entry, "owned", false).State);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            metadata.Forget("verified", "owned-row");
            release.Set();
            MetadataValue current = default;
            await WaitFor(() => (current = metadata.Get("unrelated", "owned-row", entry, "owned", false)).State == MetadataState.Available);
            Assert.Equal("unrelated value", current.Value);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
            await WaitFor(() => Volatile.Read(ref active) == 0);
        }
    }

    [Fact]
    public async Task Rapid_viewport_changes_drop_queued_rows_and_a_healthy_device_remains_available()
    {
        using var releaseA = new ManualResetEventSlim();
        using var releaseB = new ManualResetEventSlim();
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromMinutes(1), threadsPerDevice: 2, maxThreadsPerDevice: 2);
        var metadata = new MetadataService(io);
        var produced = new ConcurrentQueue<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0, active = 0;
        var wanted = new HashSet<string>(StringComparer.Ordinal) { "held-a", "held-b" };
        metadata.Register(new MetadataField("observed", "Observed", MetadataCost.Cheap, _ => true, (path, ct) =>
        {
            produced.Enqueue(path);
            if (path.StartsWith("held-", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref active);
                try
                {
                    if (Interlocked.Increment(ref entered) == 2) started.TrySetResult();
                    var release = path == "held-a" ? releaseA : releaseB;
                    if (!release.Wait(TimeSpan.FromSeconds(15), ct)) throw new TimeoutException("Owned viewport release missing.");
                }
                finally { Interlocked.Decrement(ref active); }
            }
            return path;
        }, value => (string)value!));
        var entry = new EntryData("row.bin", EntryKind.File, 1, 1);
        MetadataValue Ask(string path, string device = "slow") =>
            metadata.Get("observed", path, entry, device, false, () => Volatile.Read(ref wanted).Contains(path));
        try
        {
            Ask("held-a");
            Ask("held-b");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // Separate viewport snapshots: only the latest rows remain wanted when queued work gets a worker.
            Volatile.Write(ref wanted, new HashSet<string>(Enumerable.Range(0, 1000).Select(i => "abandoned-" + i), StringComparer.Ordinal));
            for (int i = 0; i < 1000; i++) Assert.Equal(MetadataState.Pending, Ask("abandoned-" + i).State);
            Volatile.Write(ref wanted, new HashSet<string>(StringComparer.Ordinal) { "current-a", "current-b", "healthy" });
            await WaitFor(() => Ask("healthy", "healthy-device").State == MetadataState.Available);
            Assert.Equal(2, Volatile.Read(ref active));
            Assert.DoesNotContain(produced, p => p.StartsWith("abandoned-", StringComparison.Ordinal));
            var interactive = io.Run("slow", IoPriority.Interactive, _ => produced.Enqueue("interactive"), TestContext.Current.CancellationToken);
            Ask("current-a");
            Ask("current-b");
            // Only one worker becomes free: dequeuing priority and executing its callback cannot race on two workers.
            releaseA.Set();
            await interactive.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await WaitFor(() => Ask("current-a").State == MetadataState.Available && Ask("current-b").State == MetadataState.Available);
            Assert.Equal(new[] { "current-a", "current-b", "healthy", "held-a", "held-b", "interactive" }, produced.Order(StringComparer.Ordinal));
            Assert.True(Array.IndexOf(produced.ToArray(), "interactive") < Array.IndexOf(produced.ToArray(), "current-a"));
            Assert.True(Array.IndexOf(produced.ToArray(), "interactive") < Array.IndexOf(produced.ToArray(), "current-b"));
        }
        finally
        {
            releaseA.Set();
            releaseB.Set();
            await WaitFor(() => Volatile.Read(ref active) == 0);
        }
    }
}
