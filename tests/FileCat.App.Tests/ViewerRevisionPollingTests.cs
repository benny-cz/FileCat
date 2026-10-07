using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class ViewerRevisionPollingTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData("revision", false, false)]
    [InlineData("revision", false, true)]
    [InlineData("revision", true, false)]
    [InlineData("revision", true, true)]
    [InlineData("length", false, false)]
    [InlineData("length", false, true)]
    [InlineData("length", true, false)]
    [InlineData("length", true, true)]
    public async Task Source_revision_polling_keeps_UI_work_live_and_retires_at_metadata_boundaries(string heldCall, bool close, bool fail)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-revision-poll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Join(root, "owned.txt");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("owned metadata polling input\n");
        File.WriteAllBytes(path, bytes);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        using var source = new HeldMetadataSource(path, heldCall, fail);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-revision-poll");
        Task? poll = null, controller = null;
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool markerWhileHeld = false, forcedRelease = false, otherDeviceCompleted = false;
        int activeAtMarker = -1, disposedAtMarker = -1, repeatedChecks = 0;
        string statusAtClose = "";
        Exception? invocationError = null;
        double invocationMilliseconds = -1;
        try
        {
            viewer.Show();
            await WaitFor(() => !string.IsNullOrEmpty(Field<TextBlock>(viewer, "_encodingInfo").Text));
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var reader = Field<PagedReader>(viewer, "_reader");
            await Task.Run(() => Assert.Equal(bytes.Length, reader.Read(0, new byte[bytes.Length])));
            await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(reader)! == 0);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            source.Arm();
            controller = Task.Factory.StartNew(() =>
            {
                try
                {
                    if (!source.Entered.Task.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned metadata call did not start");
                    services.Io.Run("owned-other-device", IoPriority.Interactive, _ => otherDeviceCompleted = true).Wait(TimeSpan.FromSeconds(5));
                    Dispatcher.UIThread.Post(() =>
                    {
                        markerWhileHeld = source.Active == 1 && !source.Release.IsSet;
                        activeAtMarker = source.Active;
                        if (markerWhileHeld)
                        {
                            // Timer pressure while a poll is active must not admit another source query.
                            for (int i = 0; i < 25; i++)
                            {
                                _ = typeof(ViewerWindow).GetMethod("CheckForChanges", Fields)!.Invoke(viewer, []);
                                repeatedChecks++;
                            }
                        }
                        if (close) { viewer.Close(); statusAtClose = viewer.StatusText; }
                        disposedAtMarker = source.Disposals;
                        source.Release.Set();
                        marker.TrySetResult();
                    });
                    if (!source.Release.Wait(TimeSpan.FromSeconds(3))) { forcedRelease = true; source.Release.Set(); }
                }
                finally { source.Release.Set(); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var clock = Stopwatch.StartNew();
            try { poll = typeof(ViewerWindow).GetMethod("CheckForChanges", Fields)!.Invoke(viewer, []) as Task; }
            catch (TargetInvocationException ex) { invocationError = ex.InnerException; }
            invocationMilliseconds = clock.Elapsed.TotalMilliseconds;
            if (poll is not null) await poll.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await controller.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await marker.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await source.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (close) await WaitFor(() => source.Disposals == 1);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            string afterStatus = viewer.StatusText;
            var calls = source.Calls.ToArray();
            string afterHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            output.WriteLine(JsonSerializer.Serialize(new { heldCall, close, fail, markerWhileHeld, forcedRelease,
                otherDeviceCompleted, activeAtMarker, disposedAtMarker, repeatedChecks, invocationMilliseconds,
                InvocationError = invocationError?.GetType().FullName, statusAtClose, afterStatus, QueryLog = calls,
                source.Active, source.Disposals, source.DisposedDuringCall,
                OriginalContentBase64 = Convert.ToBase64String(bytes), AfterFileSHA256 = afterHash,
                ActualFileContentSourceAndPagedReaderAndViewer = true, OwnedBoundedMetadataHold = true,
                HeadlessDispatcherNotNativeDesktopOrPhysicalNetwork = true }));
            Assert.All(calls, c => Assert.False(c.OnUiThread));
            Assert.True(markerWhileHeld);
            Assert.False(forcedRelease);
            Assert.True(otherDeviceCompleted);
            Assert.Equal(1, activeAtMarker);
            Assert.Equal(0, disposedAtMarker);
            Assert.Equal(25, repeatedChecks);
            Assert.Null(invocationError);
            Assert.Equal(0, source.Active);
            Assert.False(source.DisposedDuringCall);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), afterHash);
            if (heldCall == "revision" && (close || fail)) Assert.Equal(["revision"], calls.Select(c => c.Kind));
            else Assert.Equal(["revision", "length"], calls.Select(c => c.Kind));
            Assert.All(calls, c => Assert.StartsWith("FileCat I/O owned-revision-poll", c.ThreadName));
            if (close) { Assert.Equal(statusAtClose, afterStatus); Assert.Equal(1, source.Disposals); }
            else if (fail) Assert.Contains("could not be checked", afterStatus, StringComparison.Ordinal);
            else Assert.DoesNotContain("could not be checked", afterStatus, StringComparison.Ordinal);
        }
        finally
        {
            source.Release.Set();
            if (controller is not null) await controller.WaitAsync(TimeSpan.FromSeconds(15));
            if (poll is not null) await poll.WaitAsync(TimeSpan.FromSeconds(10));
            viewer.Close();
            await WaitFor(() => source.Disposals == 1);
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Owned revision checkpoint timed out");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    private sealed record MetadataCall(string Kind, bool OnUiThread, string ThreadName);
    private sealed class HeldMetadataSource(string path, string heldCall, bool fail) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        private int _armed, _hold, _active, _disposed, _disposedDuring;
        public readonly ConcurrentQueue<MetadataCall> Calls = new();
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active => Volatile.Read(ref _active);
        public int Disposals => Volatile.Read(ref _disposed);
        public bool DisposedDuringCall => Volatile.Read(ref _disposedDuring) != 0;
        public string DisplayName => _inner.DisplayName;
        public string? LocalPath => _inner.LocalPath;
        public bool CanSeek => true;
        public long Length { get { Boundary("length"); return _inner.Length; } }
        public ContentRevision? GetRevision() { Boundary("revision"); return _inner.GetRevision(); }
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        public void Arm() { Volatile.Write(ref _hold, 1); Volatile.Write(ref _armed, 1); }
        private void Boundary(string kind)
        {
            if (Volatile.Read(ref _armed) == 0) return;
            Calls.Enqueue(new MetadataCall(kind, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            if (kind != heldCall || Interlocked.Exchange(ref _hold, 0) == 0) return;
            Interlocked.Increment(ref _active); Entered.TrySetResult();
            try
            {
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned metadata hold was not released");
                if (fail) throw new IOException("owned metadata polling failure");
            }
            finally { Interlocked.Decrement(ref _active); Exited.TrySetResult(); }
        }
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
            if (Active > 0) Interlocked.Exchange(ref _disposedDuring, 1);
            _inner.Dispose();
        }
    }
}
