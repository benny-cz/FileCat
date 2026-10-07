using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

public sealed class ViewerProviderAdmissionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("content")]
    [InlineData("null")]
    [InlineData("io")]
    [InlineData("denied")]
    public async Task Repeated_provider_opens_share_device_workers_and_retire_on_shutdown(string outcome)
    {
        using var fixture = new Fixture(outcome);
        var before = ViewerWindow.OpenWindows.ToHashSet();
        var launches = Enumerable.Range(0, 7).Select(_ => fixture.Open()).ToArray();
        try
        {
            await WaitFor(() => fixture.Provider.Calls.Count >= 2);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            bool otherDevice = false;
            await fixture.Services.Io.Run("owned-provider-other", IoPriority.Interactive, _ => otherDevice = true);
            var heldCalls = fixture.Provider.Calls.ToArray();
            int sourcesHeld = fixture.Provider.Sources.Count;
            int disposedHeld = fixture.Provider.Sources.Sum(s => s.Disposals);
            bool uiMarkerWhileHeld = !fixture.Provider.Release.IsSet && Dispatcher.UIThread.CheckAccess();
            fixture.Services.Io.Dispose();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            int unfinishedAfterStop = launches.Count(t => !t.IsCompleted);
            int disposedAfterStop = fixture.Provider.Sources.Sum(s => s.Disposals);
            fixture.Provider.Release.Set();
            await Task.WhenAll(launches).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var added = ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            var sources = fixture.Provider.Sources.ToArray();
            string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Path)));
            output.WriteLine(JsonSerializer.Serialize(new { outcome, Requests = launches.Length, uiMarkerWhileHeld,
                otherDevice, HeldCalls = heldCalls, sourcesHeld, disposedHeld, unfinishedAfterStop, disposedAfterStop,
                FinalCalls = fixture.Provider.Calls.ToArray(), Sources = sources.Select(s => new { s.Disposals, s.Metadata, s.Reads }),
                WindowsAfter = added.Length, BeforeSHA256 = fixture.Hash, AfterSHA256 = after,
                OwnedRealFile = true, OrdinaryF3ViewItemPath = true, NativeDesktopHardwareOrCandidateQualified = false }));
            Assert.True(uiMarkerWhileHeld);
            Assert.True(otherDevice);
            Assert.Equal(fixture.Services.Io.ThreadsPerDevice, heldCalls.Length);
            Assert.All(heldCalls, c => { Assert.False(c.OnUiThread); Assert.StartsWith("FileCat I/O ", c.ThreadName); });
            Assert.Equal(outcome == "content" ? 2 : 0, sourcesHeld);
            Assert.Equal(0, disposedHeld);
            Assert.Equal(2, unfinishedAfterStop);
            Assert.Equal(0, disposedAfterStop);
            Assert.Equal(heldCalls.Length, fixture.Provider.Calls.Count);
            Assert.All(sources, s => { Assert.Equal(1, s.Disposals); Assert.Equal(0, s.Metadata); Assert.Equal(0, s.Reads); });
            Assert.Empty(added);
            Assert.Equal(fixture.Hash, after);
        }
        finally
        {
            fixture.Provider.Release.Set();
            await Task.WhenAll(launches).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var window in ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Stopped_F3_admission_never_opens_provider_content()
    {
        using var fixture = new Fixture("content");
        fixture.Provider.Release.Set();
        fixture.Services.Io.Dispose();
        await fixture.Open().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Path)));
        output.WriteLine(JsonSerializer.Serialize(new { StoppedBeforeOpen = true, Calls = fixture.Provider.Calls.ToArray(),
            Sources = fixture.Provider.Sources.Select(s => new { s.Disposals, s.Metadata, s.Reads }),
            BeforeSHA256 = fixture.Hash, AfterSHA256 = after, OwnedRealFile = true, NativeDesktopHardwareOrCandidateQualified = false }));
        Assert.Empty(fixture.Provider.Calls);
        Assert.Empty(fixture.Provider.Sources);
        Assert.Equal(fixture.Hash, after);
    }

    [AvaloniaFact]
    public async Task Responsive_provider_open_shows_and_releases_the_actual_file()
    {
        using var fixture = new Fixture("content");
        fixture.Provider.Release.Set();
        var before = ViewerWindow.OpenWindows.ToHashSet();
        try
        {
            await fixture.Open().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var added = ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            var source = Assert.Single(fixture.Provider.Sources);
            bool shown = added.Length == 1 && added[0].IsVisible;
            int beforeClose = source.Disposals;
            foreach (var window in added) window.Close();
            await WaitFor(() => source.Disposals == 1);
            string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Path)));
            output.WriteLine(JsonSerializer.Serialize(new { ResponsiveControl = true, Calls = fixture.Provider.Calls.ToArray(),
                shown, beforeClose, source.Disposals, source.Metadata, source.Reads, BeforeSHA256 = fixture.Hash,
                AfterSHA256 = after, OwnedRealFile = true, NativeDesktopHardwareOrCandidateQualified = false }));
            Assert.True(shown);
            Assert.Equal(0, beforeClose);
            Assert.Equal(1, source.Disposals);
            Assert.All(fixture.Provider.Calls, c => Assert.False(c.OnUiThread));
            Assert.Equal(fixture.Hash, after);
        }
        finally { foreach (var window in ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) window.Close(); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned provider admission checkpoint timed out");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public string Path { get; }
        public string Hash { get; }
        public AppServices Services { get; }
        public Provider Provider { get; }
        private readonly MainViewModel _vm;
        public Fixture(string outcome)
        {
            _root = System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-provider-admission-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Join(_root, "owned.txt");
            byte[] bytes = Encoding.UTF8.GetBytes("owned provider admission input\n");
            File.WriteAllBytes(Path, bytes);
            Hash = Convert.ToHexString(SHA256.HashData(bytes));
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: System.IO.Path.Join(_root, "state")));
            Provider = new Provider(Path, outcome);
            Services.Providers.Register(Provider);
            _vm = new MainViewModel(Services);
        }
        public Task Open() => (Task)typeof(MainViewModel).GetMethod("ViewItemAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_vm, [new ItemRef(new Location(Provider.Scheme, "owned"), "owned.txt", EntryKind.File), "owned.txt", true])!;
        public void Dispose()
        {
            Provider.Release.Set();
            foreach (var source in Provider.Sources) source.Dispose();
            Services.Dispose(); Provider.Release.Dispose();
            Assert.Equal(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_root)));
            Directory.Delete(_root, recursive: true);
            Assert.False(Directory.Exists(_root));
        }
    }

    private sealed record OpenCall(bool OnUiThread, string ThreadName);
    private sealed class Provider(string path, string outcome) : ResourceProvider
    {
        public readonly ConcurrentQueue<OpenCall> Calls = new();
        public readonly ConcurrentQueue<Source> Sources = new();
        public readonly ManualResetEventSlim Release = new();
        public override string Scheme => "ownedviewerprovider";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource? OpenContent(ItemRef item)
        {
            var source = outcome == "content" ? new Source(path) : null;
            if (source is not null) Sources.Enqueue(source);
            Calls.Enqueue(new OpenCall(Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned provider open was not released");
            if (outcome == "io") throw new IOException("owned provider open failure");
            if (outcome == "denied") throw new UnauthorizedAccessException("owned provider open denial");
            return source;
        }
    }
    private sealed class Source(string path) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        private int _disposed, _metadata, _reads;
        public int Disposals => Volatile.Read(ref _disposed);
        public int Metadata => Volatile.Read(ref _metadata);
        public int Reads => Volatile.Read(ref _reads);
        public string DisplayName => _inner.DisplayName;
        public string? LocalPath => _inner.LocalPath;
        public bool CanSeek => true;
        public long Length { get { Interlocked.Increment(ref _metadata); return _inner.Length; } }
        public ContentRevision? GetRevision() { Interlocked.Increment(ref _metadata); return _inner.GetRevision(); }
        public int Read(long offset, Span<byte> buffer) { Interlocked.Increment(ref _reads); return _inner.Read(offset, buffer); }
        public void Dispose() { if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0) _inner.Dispose(); }
    }
}
