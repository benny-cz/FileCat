using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class ComparisonProviderAdmissionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("left")]
    [InlineData("right")]
    public async Task Repeated_comparisons_share_provider_workers_and_do_not_open_after_shutdown(string heldSide)
    {
        using var f = new Fixture("equal");
        f.Provider.Arm(heldSide);
        var before = CompareWindow.OpenWindows.ToHashSet();
        var tasks = Enumerable.Range(0, 7).Select(_ => f.Open()).ToArray();
        try
        {
            await WaitFor(() => f.Provider.Calls.Count(c => c.Side == heldSide) >= 2 && (heldSide == "left" || f.Provider.Calls.Count(c => c.Side == "left") == 7));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            bool otherDevice = false;
            await f.Services.Io.Run("owned-comparison-other", IoPriority.Interactive, _ => otherDevice = true);
            var heldCalls = f.Provider.Calls.ToArray();
            var heldSources = f.Provider.Sources.Where(s => !s.OpenReturned).ToArray();
            bool uiMarkerWhileHeld = !f.Provider.Release.IsSet && Dispatcher.UIThread.CheckAccess();
            f.Services.Io.Dispose();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            int pendingAfterStop = tasks.Count(t => !t.IsCompleted);
            int heldDisposalsAfterStop = heldSources.Sum(s => s.Disposals);
            f.Provider.Release.Set();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var added = CompareWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            await WaitFor(() => added.All(w => !w.IsComparing));
            output.WriteLine(JsonSerializer.Serialize(new { heldSide, Requests = 7, uiMarkerWhileHeld, otherDevice, HeldCalls = heldCalls,
                HeldSources = heldSources.Length, pendingAfterStop, heldDisposalsAfterStop, FinalCalls = f.Provider.Calls.ToArray(),
                Sources = f.Snapshot(), WindowsAfter = added.Length, Hashes = f.Hashes(), OwnedRealFiles = true,
                ActualMainComparisonAdmission = true, NativeDesktopHardwareOrCandidateQualified = false }));
            Assert.True(uiMarkerWhileHeld); Assert.True(otherDevice);
            Assert.Equal(2, heldCalls.Count(c => c.Side == heldSide));
            Assert.All(heldCalls, c => { Assert.False(c.OnUiThread); Assert.StartsWith("FileCat I/O ", c.ThreadName); });
            Assert.Equal(2, pendingAfterStop); Assert.Equal(0, heldDisposalsAfterStop);
            Assert.Equal(heldSide == "left" ? 2 : 9, f.Provider.Sources.Count);
            Assert.Equal(heldSide == "left" ? 2 : 9, f.Provider.Calls.Count);
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.Equal(0, s.Metadata); Assert.Equal(0, s.Reads); Assert.False(s.DisposedDuringOpen); });
            Assert.Empty(added); f.AssertHashes();
        }
        finally { await f.Finish(tasks, before); }
    }

    [AvaloniaFact]
    public async Task Stopped_comparison_admission_never_opens_providers_or_a_window()
    {
        using var f = new Fixture("equal");
        var before = CompareWindow.OpenWindows.ToHashSet();
        f.Services.Io.Dispose();
        var task = f.Open();
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var added = CompareWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            await WaitFor(() => added.All(w => !w.IsComparing));
            output.WriteLine(JsonSerializer.Serialize(new { StoppedBeforeOpen = true, Calls = f.Provider.Calls.ToArray(), Sources = f.Snapshot(),
                WindowsAfter = added.Length, Hashes = f.Hashes(), OwnedRealFiles = true, NativeDesktopHardwareOrCandidateQualified = false }));
            Assert.Empty(f.Provider.Calls); Assert.Empty(f.Provider.Sources); Assert.Empty(added); f.AssertHashes();
        }
        finally { await f.Finish([task], before); }
    }

    [AvaloniaTheory]
    [InlineData("equal")]
    [InlineData("different")]
    [InlineData("null")]
    [InlineData("io")]
    [InlineData("denied")]
    public async Task Responsive_or_failing_second_provider_preserves_comparison_and_ownership(string outcome)
    {
        using var f = new Fixture(outcome);
        var before = CompareWindow.OpenWindows.ToHashSet();
        var task = f.Open();
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var added = CompareWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            await WaitFor(() => added.All(w => !w.IsComparing));
            string? summary = added.FirstOrDefault()?.Summary;
            string[] differences = added.FirstOrDefault()?.DifferenceTexts.ToArray() ?? [];
            int disposedBeforeClose = f.Provider.Sources.Sum(s => s.Disposals);
            foreach (var w in added) w.Close();
            await WaitFor(() => f.Provider.Sources.All(s => s.Disposals == 1));
            output.WriteLine(JsonSerializer.Serialize(new { outcome, ResponsiveOrErrorControl = true, Calls = f.Provider.Calls.ToArray(),
                WindowsAfter = added.Length, summary, differences, disposedBeforeClose, Sources = f.Snapshot(), Hashes = f.Hashes(),
                OwnedRealFiles = true, NativeDesktopHardwareOrCandidateQualified = false }));
            Assert.Equal(outcome is "equal" or "different" ? 1 : 0, added.Length);
            Assert.Equal(outcome is "equal" or "different" ? 0 : 1, disposedBeforeClose);
            Assert.Equal(outcome is "equal" or "different" ? 2 : 1, f.Provider.Sources.Count);
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedDuringOpen); });
            Assert.All(f.Provider.Calls, c => Assert.False(c.OnUiThread));
            if (outcome == "equal") Assert.StartsWith("Identical: every byte was compared", summary);
            else if (outcome == "different") { Assert.StartsWith("1 differing byte ranges", summary); Assert.Single(differences); }
            else Assert.All(f.Provider.Sources, s => { Assert.Equal(0, s.Metadata); Assert.Equal(0, s.Reads); });
            f.AssertHashes();
        }
        finally { await f.Finish([task], before); }
    }

    [AvaloniaFact]
    public async Task Closing_during_compare_again_keeps_new_provider_sources_until_the_open_returns()
    {
        using var f = new Fixture("equal");
        var before = CompareWindow.OpenWindows.ToHashSet();
        var task = f.Open();
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var window = Assert.Single(CompareWindow.OpenWindows, w => !before.Contains(w));
            await WaitFor(() => !window.IsComparing);
            f.Provider.Arm("right");
            window.CompareAgain();
            await WaitFor(() => f.Provider.Calls.Any(c => c.Phase == 1 && c.Side == "right"));
            var fresh = f.Provider.Sources.Where(s => s.Phase == 1).ToArray();
            window.Close();
            string closedSummary = window.Summary;
            int freshDisposalsWhileHeld = fresh.Sum(s => s.Disposals);
            f.Provider.Release.Set();
            await WaitFor(() => f.Provider.Sources.All(s => s.Disposals == 1));
            string finalSummary = window.Summary;
            output.WriteLine(JsonSerializer.Serialize(new { ReopenCloseControl = true, freshDisposalsWhileHeld, closedSummary, finalSummary,
                Calls = f.Provider.Calls.ToArray(), Sources = f.Snapshot(), WindowsAfter = CompareWindow.OpenWindows.Count(w => !before.Contains(w)),
                Hashes = f.Hashes(), OwnedRealFiles = true, NativeDesktopHardwareOrCandidateQualified = false }));
            Assert.Equal(0, freshDisposalsWhileHeld); Assert.Equal(closedSummary, finalSummary);
            Assert.Equal(4, f.Provider.Sources.Count);
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedDuringOpen); });
            Assert.All(fresh, s => { Assert.Equal(0, s.Metadata); Assert.Equal(0, s.Reads); });
            Assert.Empty(CompareWindow.OpenWindows.Where(w => !before.Contains(w))); f.AssertHashes();
        }
        finally { await f.Finish([task], before); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned comparison admission checkpoint timed out"); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root, _left, _right, _leftHash, _rightHash;
        private readonly MainViewModel _vm;
        public AppServices Services { get; }
        public Provider Provider { get; }
        public Fixture(string outcome)
        {
            _root = Path.Join(Path.GetTempPath(), "filecat-comparison-admission-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root);
            _left = Path.Join(_root, "left.bin"); _right = Path.Join(_root, "right.bin");
            byte[] bytes = Enumerable.Range(0, 32768).Select(i => (byte)((i * 17 + 23) % 251)).ToArray();
            File.WriteAllBytes(_left, bytes); if (outcome == "different") bytes[16123] ^= 32; File.WriteAllBytes(_right, bytes);
            _leftHash = Hash(_left); _rightHash = Hash(_right);
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(_root, "state")));
            Provider = new Provider(_left, _right, outcome); Services.Providers.Register(Provider); _vm = new MainViewModel(Services);
        }
        public Task Open() => (Task)typeof(MainViewModel).GetMethod("OpenFileComparisonAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_vm,
            [new ItemRef(new Location(Provider.Scheme, "left"), "left.bin", EntryKind.File), new ItemRef(new Location(Provider.Scheme, "right"), "right.bin", EntryKind.File)])!;
        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        public object Hashes() => new { LeftBefore = _leftHash, LeftAfter = Hash(_left), RightBefore = _rightHash, RightAfter = Hash(_right) };
        public void AssertHashes() { Assert.Equal(_leftHash, Hash(_left)); Assert.Equal(_rightHash, Hash(_right)); }
        public object Snapshot() => Provider.Sources.Select(s => new { s.Side, s.Phase, s.Disposals, s.Metadata, s.Reads, s.OpenReturned, s.DisposedDuringOpen }).ToArray();
        public async Task Finish(Task[] tasks, HashSet<CompareWindow> before)
        {
            Provider.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var w in CompareWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) w.Close();
            await WaitFor(() => Provider.Sources.All(s => s.Disposals == 1));
        }
        public void Dispose()
        {
            Provider.Release.Set(); foreach (var s in Provider.Sources.Where(s => s.Disposals == 0)) s.Dispose(); Services.Dispose(); Provider.Release.Dispose();
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(_root)));
            Directory.Delete(_root, recursive: true); Assert.False(Directory.Exists(_root));
        }
    }
    private sealed record OpenCall(string Side, int Phase, bool OnUiThread, string ThreadName);
    private sealed class Provider(string left, string right, string outcome) : ResourceProvider
    {
        private string? _heldSide; private int _phase;
        public readonly ConcurrentQueue<OpenCall> Calls = new(); public readonly ConcurrentQueue<Source> Sources = new();
        public readonly ManualResetEventSlim Release = new(true);
        public void Arm(string side) { _heldSide = side; _phase++; Release.Reset(); }
        public override string Scheme => "ownedcomparisonprovider";
        public override string GetDisplayPath(Location location) => location.Path;
        public override string GetDeviceKey(Location location) => Scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource? OpenContent(ItemRef item)
        {
            string side = item.Parent.Path; var source = side == "right" && outcome is "null" or "io" or "denied" ? null : new Source(side == "left" ? left : right, side, _phase);
            if (source is not null) Sources.Enqueue(source);
            Calls.Enqueue(new OpenCall(side, _phase, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            try
            {
                if (side == _heldSide && !Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned comparison provider was not released");
                if (side == "right" && outcome == "io") throw new IOException("owned second-provider failure");
                if (side == "right" && outcome == "denied") throw new UnauthorizedAccessException("owned second-provider denial");
                return source;
            }
            finally { source?.Returned(); }
        }
    }
    private sealed class Source(string path, string side, int phase) : IContentSource
    {
        private readonly FileContentSource _inner = new(path); private int _disposed, _metadata, _reads, _returned, _during;
        public string Side => side; public int Phase => phase;
        public int Disposals => Volatile.Read(ref _disposed); public int Metadata => Volatile.Read(ref _metadata); public int Reads => Volatile.Read(ref _reads);
        public bool OpenReturned => Volatile.Read(ref _returned) != 0; public bool DisposedDuringOpen => Volatile.Read(ref _during) != 0;
        public void Returned() => Interlocked.Exchange(ref _returned, 1);
        public string DisplayName => _inner.DisplayName; public string? LocalPath => _inner.LocalPath; public bool CanSeek => true;
        public long Length { get { Interlocked.Increment(ref _metadata); return _inner.Length; } }
        public ContentRevision? GetRevision() { Interlocked.Increment(ref _metadata); return _inner.GetRevision(); }
        public int Read(long offset, Span<byte> buffer) { Interlocked.Increment(ref _reads); return _inner.Read(offset, buffer); }
        public void Dispose() { if (!OpenReturned) Interlocked.Exchange(ref _during, 1); Interlocked.Increment(ref _disposed); _inner.Dispose(); }
    }
}
