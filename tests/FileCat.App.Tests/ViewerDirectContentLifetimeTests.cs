using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerDirectContentLifetimeTests(ITestOutputHelper output)
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [AvaloniaTheory]
    [InlineData("line", true)]
    [InlineData("html", true)]
    [InlineData("markdown", true)]
    [InlineData("info", true)]
    [InlineData("line", false)]
    [InlineData("html", false)]
    [InlineData("markdown", false)]
    [InlineData("info", false)]
    public async Task Closing_the_viewer_retires_direct_content_demand_before_releasing_the_file(string mode, bool closeWhileHeld)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Combine(temp, "filecat-direct-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        var bytes = Encoding.UTF8.GetBytes(mode switch
        {
            "html" => "<!doctype html><html><body>" + new string('x', 100000) + "</body></html>",
            "markdown" => "# Owned page\n\n" + new string('x', 100000),
            _ => string.Concat(Enumerable.Repeat("line\n", 20000))
        });
        string path = Path.Combine(root, "owned." + (mode == "markdown" ? "md" : mode == "html" ? "html" : "txt"));
        File.WriteAllBytes(path, bytes);
        string before = Convert.ToHexString(SHA256.HashData(bytes));
        var source = new HeldSource(path, mode == "line" ? 1024 * 1024 : mode == "info" ? 64 : 64 * 1024);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-direct-content");
        Task? work = null;
        HtmlPage? page = null;
        (byte[] Bytes, string MimeType)? served = null;
        Exception? error = null;
        int disposedWhileHeld = -1, activeWhileHeld = -1, chargedAfterClose = -1, readsAfterWork = -1, readsWhileHeld = -1;
        double closeMilliseconds = -1;
        long topOffsetAfterWork = -1;
        string statusAfterWork = "";
        try
        {
            viewer.Show();
            var timer = Field<DispatcherTimer>(viewer, "_changeTimer");
            await WaitFor(() => timer.IsEnabled);
            timer.Stop();
            var reader = Field<PagedReader>(viewer, "_reader");
            await Task.Run(() => reader.Read(0, new byte[bytes.Length])).WaitAsync(TimeSpan.FromSeconds(10));
            await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(reader)! == 0);
            Assert.True((int)typeof(PagedReader).GetProperty("CachedPages", Fields)!.GetValue(reader)! > 0);
            source.Arm();
            if (mode == "line") work = (Task)typeof(ViewerWindow).GetMethod("GoToLineAsync", Fields)!.Invoke(viewer, [9000L])!;
            else if (mode == "info") work = viewer.ShowInfoAsync();
            else
            {
                viewer.ShowPage();
                page = Field<HtmlPage>(viewer, "_htmlPage");
                work = Task.Run(() => served = page.Resolve("/"));
            }
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (closeWhileHeld)
            {
                var clock = Stopwatch.StartNew();
                viewer.Close();
                closeMilliseconds = clock.Elapsed.TotalMilliseconds;
                disposedWhileHeld = source.Disposals;
                activeWhileHeld = source.Active;
                readsWhileHeld = source.Reads;
                chargedAfterClose = (int)typeof(PagedReader).GetProperty("CachedPages", Fields)!.GetValue(reader)!;
            }
            source.Release.Set();
            try { await work.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            catch (Exception ex) { error = ex; }
            topOffsetAfterWork = Field<TextViewer>(viewer, "_text").TopOffset;
            statusAfterWork = Field<Avalonia.Controls.TextBlock>(viewer, "_status").Text ?? "";
            readsAfterWork = source.Reads;
            if (!closeWhileHeld)
            {
                Assert.Equal(0, source.Disposals);
                viewer.Close();
            }
            await WaitFor(() => source.Disposals > 0);
            if (page is not null) Assert.Null(await Task.Run(() => page.Resolve("/")));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            output.WriteLine(JsonSerializer.Serialize(new { mode, closeWhileHeld, disposedWhileHeld, activeWhileHeld,
                chargedAfterClose, closeMilliseconds, source.Disposals, source.DisposalsDuringRead, source.ReadFailedAfterDisposal,
                source.Active, source.Reads, readsAfterWork, readsWhileHeld, Error = error?.GetType().FullName, topOffsetAfterWork,
                statusAfterWork, ServedBytes = served?.Bytes.Length, BeforeSHA256 = before, AfterSHA256 = after,
                OwnedRealFile = true, NativeDesktop = false, PhysicalDevice = false }));
            if (closeWhileHeld)
            {
                Assert.Equal(0, disposedWhileHeld);
                Assert.Equal(1, activeWhileHeld);
                Assert.Equal(0, chargedAfterClose);
                Assert.Equal(readsWhileHeld, readsAfterWork);
                Assert.True(closeMilliseconds < 3000);
                if (mode == "line") Assert.Equal(0, topOffsetAfterWork);
                if (page is not null) Assert.Null(served);
            }
            else if (mode == "line") Assert.Equal(44995, topOffsetAfterWork);
            else if (page is not null)
            {
                Assert.NotNull(served);
                Assert.Equal("text/html", served.Value.MimeType);
                if (mode == "html") Assert.Equal(bytes, served.Value.Bytes);
                else Assert.Contains("Owned page", Encoding.UTF8.GetString(served.Value.Bytes));
            }
            else if (!closeWhileHeld) Assert.Contains("No structure inspector", viewer.InfoText);
            Assert.Null(error);
            Assert.Equal(1, source.Disposals);
            Assert.Equal(0, source.DisposalsDuringRead);
            Assert.False(source.ReadFailedAfterDisposal);
            Assert.Equal(0, source.Active);
            Assert.Equal(readsAfterWork, source.Reads);
            Assert.Equal(before, after);
        }
        finally
        {
            source.Release.Set();
            if (work is not null) { try { await work.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { } }
            viewer.Close();
            source.Cleanup();
            services.Dispose();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned direct-content checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class HeldSource(string path, int targetBytes) : IContentSource
    {
        private readonly FileContentSource _file = new(path);
        private int _armed, _active, _reads, _disposals, _during, _failed;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public int Reads => Volatile.Read(ref _reads);
        public int Active => Volatile.Read(ref _active);
        public int Disposals => Volatile.Read(ref _disposals);
        public int DisposalsDuringRead => Volatile.Read(ref _during);
        public bool ReadFailedAfterDisposal => Volatile.Read(ref _failed) != 0;
        public string DisplayName => _file.DisplayName;
        public long Length => _file.Length;
        public bool CanSeek => true;
        public string? LocalPath => _file.LocalPath;
        public ContentRevision? GetRevision() => _file.GetRevision();
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _reads);
            Interlocked.Increment(ref _active);
            try
            {
                if (buffer.Length == targetBytes && Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned direct-content read was not released.");
                }
                return _file.Read(offset, buffer);
            }
            catch (ObjectDisposedException) { Interlocked.Exchange(ref _failed, 1); throw; }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            if (Active > 0) Interlocked.Increment(ref _during);
            _file.Dispose();
        }
        public void Cleanup() { _file.Dispose(); Release.Dispose(); }
    }
}
