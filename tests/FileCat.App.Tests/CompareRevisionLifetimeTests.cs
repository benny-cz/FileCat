using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class CompareRevisionLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Closing_or_reopening_a_comparison_retains_its_active_revision_call(bool pageRefresh, bool reopen)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        string root = Path.GetFullPath(Path.Combine(tempRoot, "filecat-compare-revision-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string leftPath = Path.Combine(root, "left.txt"), rightPath = Path.Combine(root, "right.txt");
        File.WriteAllText(leftPath, "same owned content\n");
        File.WriteAllText(rightPath, "same owned content\n");
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var before = (Hash(leftPath), Hash(rightPath));
        using var left = new HeldRevisionSource(leftPath);
        var right = new FileContentSource(rightPath);
        CompareWindow? window = null;
        Task? refresh = null;
        int disposedWhileHeld = -1, activeWhileHeld = -1;
        try
        {
            window = CompareWindow.Open(leftPath, left, rightPath, right,
                () => (new FileContentSource(leftPath), new FileContentSource(rightPath)));
            for (int i = 0; i < 300 && window.IsComparing; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.False(window.IsComparing);
            Assert.StartsWith("Identical: every byte was compared", window.Summary);
            Assert.True(((Task)typeof(CompareWindow).GetField("_runs", fields)!.GetValue(window)!).IsCompleted);
            left.Arm();
            if (pageRefresh)
            {
                var pages = ((PagedReader Left, PagedReader Right))typeof(CompareWindow).GetField("_pages", fields)!.GetValue(window)!;
                refresh = Task.Run(() => pages.Left.Refresh());
            }
            else
                typeof(CompareWindow).GetMethod("CheckInputs", fields)!.Invoke(window, null);
            await left.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, left.DisposeCalls);
            if (reopen)
            {
                window.CompareAgain();
                for (int i = 0; i < 300 && ReferenceEquals(typeof(CompareWindow).GetField("_left", fields)!.GetValue(window), left); i++)
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                Assert.NotSame(left, typeof(CompareWindow).GetField("_left", fields)!.GetValue(window));
            }
            else window.Close();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            disposedWhileHeld = left.DisposeCalls;
            activeWhileHeld = left.ActiveCalls;
        }
        finally
        {
            left.Release.Set();
            if (left.Entered.Task.IsCompleted) await left.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (refresh is not null)
            {
                try { await refresh.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (ObjectDisposedException) { }
            }
            window?.Close();
            if (window is not null) await left.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            else { left.Dispose(); right.Dispose(); }
        }
        var after = (Hash(leftPath), Hash(rightPath));
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { pageRefresh, reopen, disposedWhileHeld,
            activeWhileHeld, left.DisposedDuringCall, left.RevisionFailedAfterDisposal, left.DisposeCalls, left.ActiveCalls,
            FilesUnchanged = before == after, OwnedRealFilesOnly = true, NativeDesktopInteraction = false, PhysicalDeviceAccess = false }));
        try
        {
            Assert.Equal(0, disposedWhileHeld);
            Assert.Equal(1, activeWhileHeld);
            Assert.False(left.DisposedDuringCall);
            Assert.False(left.RevisionFailedAfterDisposal);
            Assert.Equal(1, left.DisposeCalls);
            Assert.Equal(0, left.ActiveCalls);
            Assert.Equal(before, after);
        }
        finally
        {
            Assert.Equal(tempRoot.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class HeldRevisionSource(string path) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        private int _armed, _active, _disposed, _during, _failed;
        public readonly ManualResetEventSlim Release = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActiveCalls => Volatile.Read(ref _active);
        public int DisposeCalls => Volatile.Read(ref _disposed);
        public bool DisposedDuringCall => Volatile.Read(ref _during) != 0;
        public bool RevisionFailedAfterDisposal => Volatile.Read(ref _failed) != 0;
        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => _inner.CanSeek;
        public string? LocalPath => _inner.LocalPath;
        public int Read(long offset, Span<byte> buffer) => _inner.Read(offset, buffer);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public ContentRevision? GetRevision()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0) return _inner.GetRevision();
            Interlocked.Increment(ref _active);
            Entered.TrySetResult();
            try
            {
                if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned revision call was not released");
                return _inner.GetRevision();
            }
            catch (ObjectDisposedException) { Interlocked.Exchange(ref _failed, 1); throw; }
            finally { Interlocked.Decrement(ref _active); Exited.TrySetResult(); }
        }
        public void Dispose()
        {
            // Window ownership can dispose this wrapper once. The test's using protects exceptional setup too.
            if (Interlocked.Increment(ref _disposed) != 1) return;
            if (ActiveCalls > 0) Interlocked.Exchange(ref _during, 1);
            _inner.Dispose();
            Disposed.TrySetResult();
        }
    }
}
