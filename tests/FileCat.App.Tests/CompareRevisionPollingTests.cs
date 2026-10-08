using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class CompareRevisionPollingTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData("live", false)]
    [InlineData("live", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("reopen", false)]
    [InlineData("reopen", true)]
    public async Task Repeated_comparison_revision_checks_coalesce_while_a_source_is_held(string action, bool fail)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-compare-poll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string leftPath = Path.Join(root, "left.txt"), rightPath = Path.Join(root, "right.txt");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("owned comparison polling input\n");
        File.WriteAllBytes(leftPath, bytes); File.WriteAllBytes(rightPath, bytes);
        using var left = new PollSource(leftPath, hold: true, fail);
        using var right = new PollSource(rightPath, hold: false, fail: false);
        CompareWindow? window = null;
        ThreadPool.GetMinThreads(out int priorWorkers, out int priorPorts);
        int reservedWorkers = Math.Max(priorWorkers, 40);
        Assert.True(ThreadPool.SetMinThreads(reservedWorkers, priorPorts));
        int activeWhileHeld = -1, queriesWhileHeld = -1, disposedWhileHeld = -1;
        bool uiMarkerWhileHeld = false, oldBannerWhileHeld = false;
        try
        {
            window = CompareWindow.Open(leftPath, left, rightPath, right,
                () => (new FileContentSource(leftPath), new FileContentSource(rightPath)));
            // Activation can already own an unarmed revision check after loading completes. Drain it before
            // arming the held check; otherwise CheckInputs correctly coalesces and Entered can never signal.
            await WaitFor(() => !window.IsComparing && Field<Task>(window, "_loading").IsCompleted &&
                Field<Task>(window, "_runs").IsCompleted && !Field<bool>(window, "_checkingInputs"));
            Assert.StartsWith("Identical: every byte was compared", window.Summary);
            left.Arm(); right.Arm();
            typeof(CompareWindow).GetMethod("CheckInputs", Fields)!.Invoke(window, []);
            await left.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (int i = 0; i < 25; i++)
                typeof(CompareWindow).GetMethod("CheckInputs", Fields)!.Invoke(window, []);
            // The fixture reserves enough pool capacity for all 26 original holds. This is an observation
            // interval, not a claim about native input latency or reference-machine performance.
            await Task.Delay(750, TestContext.Current.CancellationToken);
            uiMarkerWhileHeld = Dispatcher.UIThread.CheckAccess() && left.Active > 0 && !left.Release.IsSet;
            activeWhileHeld = left.Active; queriesWhileHeld = left.Queries;
            oldBannerWhileHeld = Field<Border>(window, "_changedBanner").IsVisible;
            if (action == "close") window.Close();
            else if (action == "reopen")
            {
                window.CompareAgain();
                await WaitFor(() => !ReferenceEquals(Field<IContentSource>(window, "_left"), left) &&
                    !window.IsComparing && Field<Task>(window, "_loading").IsCompleted &&
                    Field<Task>(window, "_runs").IsCompleted);
                Assert.StartsWith("Identical: every byte was compared", window.Summary);
            }
            disposedWhileHeld = left.Disposals;
            left.Release.Set();
            await WaitFor(() => left.Active == 0);
            if (action == "live") await WaitFor(() => right.Queries == left.Queries);
            else await WaitFor(() => left.Disposals == 1);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            bool afterBanner = Field<Border>(window, "_changedBanner").IsVisible;
            string afterBannerText = Field<TextBlock>(window, "_changed").Text ?? "";
            string afterSummary = window.Summary;
            window.Close();
            await WaitFor(() => left.Disposals == 1 && right.Disposals == 1);
            ThreadPool.GetMinThreads(out int heldWorkers, out int heldPorts);
            output.WriteLine(JsonSerializer.Serialize(new { action, fail, RepeatedChecks = 25,
                uiMarkerWhileHeld, activeWhileHeld, queriesWhileHeld, disposedWhileHeld, oldBannerWhileHeld,
                LeftQueries = left.Queries, RightQueries = right.Queries,
                LeftActive = left.Active, RightActive = right.Active,
                LeftDisposals = left.Disposals, RightDisposals = right.Disposals,
                LeftDisposedDuringCall = left.DisposedDuringCall, RightDisposedDuringCall = right.DisposedDuringCall,
                LeftMetadataOnUI = left.MetadataOnUI, RightMetadataOnUI = right.MetadataOnUI,
                afterBanner, afterBannerText, afterSummary,
                OriginalContentBase64 = Convert.ToBase64String(bytes),
                LeftSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(leftPath))),
                RightSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rightPath))),
                priorWorkers, priorPorts, reservedWorkers, heldWorkers, heldPorts,
                ActualFileContentSourcesAndCompareWindow = true, OwnedBoundedMetadataHolds = true,
                HeadlessComponentNotNativeGUIOrPhysicalNetwork = true }));
            Assert.True(uiMarkerWhileHeld);
            Assert.Equal(1, activeWhileHeld); Assert.Equal(1, queriesWhileHeld);
            Assert.Equal(0, disposedWhileHeld);
            Assert.False(oldBannerWhileHeld);
            Assert.Equal(1, left.Queries);
            Assert.Equal(action == "live" ? 1 : 0, right.Queries);
            Assert.Equal(0, left.Active); Assert.Equal(0, right.Active);
            Assert.Equal(1, left.Disposals); Assert.Equal(1, right.Disposals);
            Assert.False(left.DisposedDuringCall); Assert.False(right.DisposedDuringCall);
            Assert.False(left.MetadataOnUI); Assert.False(right.MetadataOnUI);
            if (action == "live")
            {
                Assert.Equal(fail, afterBanner);
                if (fail)
                {
                    Assert.Contains("current state could not be checked", afterBannerText, StringComparison.Ordinal);
                    Assert.DoesNotContain("changed after they were compared", afterBannerText, StringComparison.Ordinal);
                }
            }
            else Assert.False(afterBanner);
            Assert.Equal(bytes, File.ReadAllBytes(leftPath)); Assert.Equal(bytes, File.ReadAllBytes(rightPath));
            Assert.Equal((reservedWorkers, priorPorts), (heldWorkers, heldPorts));
        }
        finally
        {
            left.Release.Set();
            window?.Close();
            if (window is not null) await WaitFor(() => left.Disposals == 1 && right.Disposals == 1);
            Assert.True(ThreadPool.SetMinThreads(priorWorkers, priorPorts));
            ThreadPool.GetMinThreads(out int restoredWorkers, out int restoredPorts);
            Assert.Equal((priorWorkers, priorPorts), (restoredWorkers, restoredPorts));
            output.WriteLine(JsonSerializer.Serialize(new { PoolMinimumRestored = true, restoredWorkers, restoredPorts }));
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_pending_unarmed_check_can_satisfy_the_old_ready_predicate_without_entering_the_armed_hold()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-compare-warmup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string leftPath = Path.Join(root, "left.txt"), rightPath = Path.Join(root, "right.txt");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("owned pending comparison metadata input\n");
        File.WriteAllBytes(leftPath, bytes); File.WriteAllBytes(rightPath, bytes);
        using var left = new PollSource(leftPath, hold: true, fail: false);
        using var right = new PollSource(rightPath, hold: false, fail: false);
        CompareWindow? window = null;
        ThreadPool.GetMinThreads(out int priorWorkers, out int priorPorts);
        int reservedWorkers = Math.Max(priorWorkers, 40);
        Assert.True(ThreadPool.SetMinThreads(reservedWorkers, priorPorts));
        try
        {
            window = CompareWindow.Open(leftPath, left, rightPath, right);
            await WaitFor(() => !window.IsComparing && Field<Task>(window, "_loading").IsCompleted &&
                Field<Task>(window, "_runs").IsCompleted && !Field<bool>(window, "_checkingInputs"));
            Assert.StartsWith("Identical: every byte was compared", window.Summary);
            left.HoldNextUnarmedCheck();
            typeof(CompareWindow).GetMethod("CheckInputs", Fields)!.Invoke(window, []);
            await left.UnarmedEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            bool oldReadyPredicateSatisfied = !window.IsComparing && Field<Task>(window, "_loading").IsCompleted &&
                Field<Task>(window, "_runs").IsCompleted;
            bool checkingBeforeArm = Field<bool>(window, "_checkingInputs");
            left.Arm(); right.Arm();
            typeof(CompareWindow).GetMethod("CheckInputs", Fields)!.Invoke(window, []);
            await Assert.ThrowsAsync<TimeoutException>(() => left.Entered.Task.WaitAsync(
                TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken));
            int queriesAtMissingCheckpoint = left.Queries;
            bool armedEnteredWhileUnarmedHeld = left.Entered.Task.IsCompleted;
            Assert.True(oldReadyPredicateSatisfied); Assert.True(checkingBeforeArm);
            Assert.Equal(1, left.UnarmedActive); Assert.False(armedEnteredWhileUnarmedHeld);
            Assert.Equal(0, queriesAtMissingCheckpoint); Assert.Equal(0, left.Disposals);
            left.UnarmedRelease.Set();
            await WaitFor(() => left.UnarmedActive == 0 && !Field<bool>(window, "_checkingInputs"));
            Assert.False(left.Entered.Task.IsCompleted); Assert.Equal(1, right.Queries);
            typeof(CompareWindow).GetMethod("CheckInputs", Fields)!.Invoke(window, []);
            await left.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            for (int i = 0; i < 25; i++) typeof(CompareWindow).GetMethod("CheckInputs", Fields)!.Invoke(window, []);
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Equal(1, left.Queries); Assert.Equal(1, left.Active); Assert.Equal(1, right.Queries);
            Assert.False(Field<Border>(window, "_changedBanner").IsVisible);
            left.Release.Set();
            await WaitFor(() => left.Active == 0 && right.Queries == 2 && !Field<bool>(window, "_checkingInputs"));
            Assert.False(Field<Border>(window, "_changedBanner").IsVisible);
            string summary = window.Summary;
            window.Close();
            await WaitFor(() => left.Disposals == 1 && right.Disposals == 1);
            output.WriteLine("COMPARE_WARMUP " + JsonSerializer.Serialize(new {
                oldReadyPredicateSatisfied, checkingBeforeArm, armedEnteredWhileUnarmedHeld,
                queriesAtMissingCheckpoint, MissingCheckpointTimeoutMilliseconds = 150,
                RepeatedChecks = 25, LeftQueries = left.Queries, RightQueries = right.Queries,
                LeftActive = left.Active, UnarmedActive = left.UnarmedActive,
                LeftDisposals = left.Disposals, RightDisposals = right.Disposals,
                LeftDisposedDuringCall = left.DisposedDuringCall, RightDisposedDuringCall = right.DisposedDuringCall,
                LeftMetadataOnUI = left.MetadataOnUI, RightMetadataOnUI = right.MetadataOnUI,
                UnarmedMetadataOnUI = left.UnarmedMetadataOnUI, summary,
                OriginalContentBase64 = Convert.ToBase64String(bytes),
                LeftSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(leftPath))),
                RightSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rightPath))),
                OwnedFileSystemAndActualCompareWindow = true, NativeDesktop = false,
                PhysicalOrNetworkSource = false, HistoricalCICauseProved = false }));
            Assert.False(left.MetadataOnUI); Assert.False(right.MetadataOnUI); Assert.False(left.UnarmedMetadataOnUI);
            Assert.False(left.DisposedDuringCall); Assert.False(right.DisposedDuringCall);
            Assert.Equal(bytes, File.ReadAllBytes(leftPath)); Assert.Equal(bytes, File.ReadAllBytes(rightPath));
        }
        finally
        {
            left.UnarmedRelease.Set(); left.Release.Set(); window?.Close();
            if (window is not null) await WaitFor(() => left.Disposals == 1 && right.Disposals == 1);
            Assert.True(ThreadPool.SetMinThreads(priorWorkers, priorPorts));
            ThreadPool.GetMinThreads(out int restoredWorkers, out int restoredPorts);
            Assert.Equal((priorWorkers, priorPorts), (restoredWorkers, restoredPorts));
            output.WriteLine(JsonSerializer.Serialize(new { PoolMinimumRestored = true, restoredWorkers, restoredPorts }));
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned comparison polling checkpoint timed out");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class PollSource(string path, bool hold, bool fail) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        private int _armed, _active, _queries, _disposed, _during, _onUi, _holdUnarmed, _unarmedActive, _unarmedOnUi;
        public readonly ManualResetEventSlim UnarmedRelease = new();
        public readonly TaskCompletionSource UnarmedEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int UnarmedActive => Volatile.Read(ref _unarmedActive);
        public bool UnarmedMetadataOnUI => Volatile.Read(ref _unarmedOnUi) != 0;
        public void HoldNextUnarmedCheck() => Volatile.Write(ref _holdUnarmed, 1);
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active => Volatile.Read(ref _active);
        public int Queries => Volatile.Read(ref _queries);
        public int Disposals => Volatile.Read(ref _disposed);
        public bool DisposedDuringCall => Volatile.Read(ref _during) != 0;
        public bool MetadataOnUI => Volatile.Read(ref _onUi) != 0;
        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => true;
        public string? LocalPath => _inner.LocalPath;
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        public void Arm() => Volatile.Write(ref _armed, 1);
        public ContentRevision? GetRevision()
        {
            if (Volatile.Read(ref _armed) == 0)
            {
                if (Interlocked.Exchange(ref _holdUnarmed, 0) == 0) return _inner.GetRevision();
                if (Dispatcher.UIThread.CheckAccess()) Interlocked.Exchange(ref _unarmedOnUi, 1);
                Interlocked.Increment(ref _unarmedActive); UnarmedEntered.TrySetResult();
                try
                {
                    if (!UnarmedRelease.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned unarmed comparison metadata hold was not released");
                    return _inner.GetRevision();
                }
                finally { Interlocked.Decrement(ref _unarmedActive); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Interlocked.Exchange(ref _onUi, 1);
            Interlocked.Increment(ref _queries); Interlocked.Increment(ref _active);
            Entered.TrySetResult();
            try
            {
                if (hold && !Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned comparison metadata hold was not released");
                if (hold && fail) throw new IOException("owned comparison metadata failure");
                return _inner.GetRevision();
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            if (Active > 0) Interlocked.Exchange(ref _during, 1);
            _inner.Dispose();
        }
    }
}
