using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class ViewerAdmissionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("length", "ok")]
    [InlineData("length", "io")]
    [InlineData("length", "denied")]
    [InlineData("length", "stopped")]
    [InlineData("revision", "ok")]
    [InlineData("revision", "io")]
    [InlineData("revision", "denied")]
    [InlineData("revision", "stopped")]
    public async Task Initial_metadata_admission_keeps_UI_live_and_transfers_or_releases_source(string heldCall, string outcome)
    {
        using var fixture = new Fixture(heldCall, outcome);
        var before = ViewerWindow.OpenWindows.ToHashSet();
        Task? launch = null;
        Exception? error = null;
        bool markerWhileHeld = false, forcedRelease = false, otherDeviceCompleted = false;
        int disposedAtMarker = -1, windowsAtMarker = -1;
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = Task.Factory.StartNew(() =>
        {
            try
            {
                if (!fixture.Source.Entered.Task.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned initial metadata never entered");
                otherDeviceCompleted = fixture.Services.Io.Run("owned-admission-other", IoPriority.Interactive, _ => { }).Wait(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.Post(() =>
                {
                    markerWhileHeld = fixture.Source.Active == 1 && !fixture.Source.Release.IsSet;
                    disposedAtMarker = fixture.Source.Disposals;
                    windowsAtMarker = ViewerWindow.OpenWindows.Count(w => !before.Contains(w));
                    if (outcome == "stopped") fixture.Services.Io.Dispose();
                    fixture.Source.Release.Set();
                    marker.TrySetResult();
                });
                if (!fixture.Source.Release.Wait(TimeSpan.FromSeconds(3))) { forcedRelease = true; fixture.Source.Release.Set(); }
            }
            finally { fixture.Source.Release.Set(); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        double invocationMilliseconds;
        try
        {
            var clock = Stopwatch.StartNew();
            try { launch = typeof(ViewerLauncher).GetMethod(nameof(ViewerLauncher.Open))!.Invoke(null, [fixture.Services, fixture.Item, fixture.Source, true]) as Task; }
            catch (TargetInvocationException ex) { error = ex.InnerException; }
            invocationMilliseconds = clock.Elapsed.TotalMilliseconds;
            if (launch is not null)
                try { await launch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
                catch (Exception ex) { error = ex; }
            await controller.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await marker.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var added = ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            int windowsAfter = added.Length;
            int disposalsBeforeClose = fixture.Source.Disposals;
            bool shown = added.Length == 1 && added[0].IsVisible;
            if (shown && outcome == "ok")
            {
                added[0].Hide(); added[0].Show();
                Assert.Single(ViewerWindow.OpenWindows, w => !before.Contains(w));
            }
            foreach (var window in added) window.Close();
            if (outcome == "ok") await WaitFor(() => fixture.Source.Disposals == 1);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Path)));
            var calls = fixture.Source.Calls.ToArray();
            output.WriteLine(JsonSerializer.Serialize(new { heldCall, outcome, markerWhileHeld, forcedRelease, otherDeviceCompleted,
                disposedAtMarker, windowsAtMarker, windowsAfter, disposalsBeforeClose, shown, invocationMilliseconds,
                Error = error?.GetType().FullName, Calls = calls, fixture.Source.Disposals, fixture.Source.DisposedDuringCall,
                fixture.Source.Active, BeforeSHA256 = fixture.Hash, AfterSHA256 = hash, OwnedRealFile = true,
                InitialViewerLauncherAndPagedReaderAdmission = true, NativeDesktopOrHardwareOrCandidateQualified = false }));
            Assert.True(markerWhileHeld);
            Assert.False(forcedRelease);
            Assert.True(otherDeviceCompleted);
            Assert.Equal(0, disposedAtMarker);
            Assert.Equal(0, windowsAtMarker);
            Assert.All(calls, c => Assert.False(c.OnUiThread));
            Assert.All(calls, c => Assert.StartsWith("FileCat I/O ", c.ThreadName));
            Assert.Equal(heldCall == "length" && outcome is "io" or "denied" ? ["length"] : ["length", "revision"], calls.Select(c => c.Kind));
            Assert.Equal(outcome == "ok" ? 1 : 0, windowsAfter);
            Assert.Equal(outcome == "ok", shown);
            Assert.Equal(outcome == "ok" ? 0 : 1, disposalsBeforeClose);
            if (outcome == "ok") Assert.Null(error);
            else if (outcome == "io") Assert.IsType<IOException>(error);
            else if (outcome == "denied") Assert.IsType<UnauthorizedAccessException>(error);
            else Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Equal(1, fixture.Source.Disposals);
            Assert.False(fixture.Source.DisposedDuringCall);
            Assert.Equal(0, fixture.Source.Active);
            Assert.Equal(fixture.Hash, hash);
            Assert.Empty(ViewerWindow.OpenWindows.Where(w => !before.Contains(w)));
        }
        finally
        {
            fixture.Source.Release.Set();
            await controller.WaitAsync(TimeSpan.FromSeconds(15));
            if (launch is not null) { try { await launch.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { } }
            foreach (var window in ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) window.Close();
            // Failed baseline constructors never reach Closed; remove only this fixture's unfinished registry entry.
            var registry = (List<ViewerWindow>)typeof(ViewerWindow).GetField("s_open", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            registry.RemoveAll(w => !before.Contains(w));
        }
    }

    [AvaloniaFact]
    public async Task Stopped_admission_releases_untransferred_source_without_querying_it()
    {
        using var fixture = new Fixture("none", "ok");
        var before = ViewerWindow.OpenWindows.ToHashSet();
        fixture.Services.Io.Dispose();
        Exception? error = null;
        try
        {
            try
            {
                var launch = typeof(ViewerLauncher).GetMethod(nameof(ViewerLauncher.Open))!.Invoke(null, [fixture.Services, fixture.Item, fixture.Source, true]) as Task;
                if (launch is not null) await launch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }
            catch (TargetInvocationException ex) { error = ex.InnerException; }
            catch (Exception ex) { error = ex; }
            var added = ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Path)));
            output.WriteLine(JsonSerializer.Serialize(new { StoppedAdmission = true, Error = error?.GetType().FullName,
                Calls = fixture.Source.Calls.ToArray(), fixture.Source.Disposals, fixture.Source.DisposedDuringCall,
                WindowsAfter = added.Length, BeforeSHA256 = fixture.Hash, AfterSHA256 = hash,
                OwnedRealFile = true, NativeDesktopOrHardwareOrCandidateQualified = false }));
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            Assert.Empty(fixture.Source.Calls);
            Assert.Equal(1, fixture.Source.Disposals);
            Assert.False(fixture.Source.DisposedDuringCall);
            Assert.Empty(added);
            Assert.Equal(fixture.Hash, hash);
        }
        finally { foreach (var window in ViewerWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) window.Close(); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned admission checkpoint timed out");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public string Path { get; }
        public string Hash { get; }
        public AppServices Services { get; }
        public Source Source { get; }
        public ItemRef Item { get; }
        public Fixture(string heldCall, string outcome)
        {
            _root = System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-viewer-admission-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Path = System.IO.Path.Join(_root, "owned.txt");
            byte[] bytes = Encoding.UTF8.GetBytes("owned viewer admission input\n");
            File.WriteAllBytes(Path, bytes);
            Hash = Convert.ToHexString(SHA256.HashData(bytes));
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: System.IO.Path.Join(_root, "state")));
            Source = new Source(Path, heldCall, outcome);
            Item = ItemRef.ForFileSystemPath(Path, EntryKind.File);
        }
        public void Dispose()
        {
            Source.Release.Set(); Source.Dispose(); Services.Dispose();
            Assert.Equal(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_root)));
            Directory.Delete(_root, recursive: true);
            Assert.False(Directory.Exists(_root));
        }
    }

    private sealed record MetadataCall(string Kind, bool OnUiThread, string ThreadName);
    private sealed class Source(string path, string heldCall, string outcome) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        private int _held, _active, _disposed, _during;
        public readonly ConcurrentQueue<MetadataCall> Calls = new();
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active => Volatile.Read(ref _active);
        public int Disposals => Volatile.Read(ref _disposed);
        public bool DisposedDuringCall => Volatile.Read(ref _during) != 0;
        public string DisplayName => _inner.DisplayName;
        public string? LocalPath => _inner.LocalPath;
        public bool CanSeek => true;
        public long Length { get { Boundary("length"); return _inner.Length; } }
        public ContentRevision? GetRevision() { Boundary("revision"); return _inner.GetRevision(); }
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        private void Boundary(string kind)
        {
            Calls.Enqueue(new MetadataCall(kind, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            if (kind != heldCall || Interlocked.Exchange(ref _held, 1) != 0) return;
            Interlocked.Increment(ref _active); Entered.TrySetResult();
            try
            {
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned initial metadata hold never released");
                if (outcome == "io") throw new IOException("owned initial metadata failure");
                if (outcome == "denied") throw new UnauthorizedAccessException("owned initial metadata denial");
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
