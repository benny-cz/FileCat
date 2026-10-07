using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerCopyLifetimeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long HeldOffset = 2 * PagedReader.PageSize;

    [AvaloniaTheory]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    [InlineData("close", false)]
    [InlineData("close", true)]
    [InlineData("complete", false)]
    [InlineData("complete", true)]
    public async Task Viewer_copy_completion_belongs_to_its_current_live_demand(string action, bool range)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-viewer-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        byte[] bytes = Enumerable.Range(0, 4 * PagedReader.PageSize).Select(i => (byte)((i * 17 + 23) % 251)).ToArray();
        string path = Path.Join(root, "owned.bin");
        File.WriteAllBytes(path, bytes);
        using var source = new HeldSource(path);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-viewer-copy");
        Task? oldWork = null;
        try
        {
            viewer.Show();
            await WaitFor(() => Field<DispatcherTimer>(viewer, "_changeTimer").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            var reader = Field<PagedReader>(viewer, "_reader");
            await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(reader)! == 0);
            Assert.False((bool)typeof(PagedReader).GetMethod("HasPage", Fields)!.Invoke(reader, [2L])!);
            var clipboard = Assert.IsAssignableFrom<IClipboard>(viewer.Clipboard);
            await ClipboardExtensions.SetTextAsync(clipboard, "current clipboard before copy");
            var hex = Field<HexView>(viewer, "_hex");
            Select(hex, range ? HeldOffset : 0, range ? 4096 : bytes.Length);
            source.Arm();
            oldWork = Copy(viewer);
            var sourceField = typeof(ViewerWindow).GetField("_copyCts", Fields);
            var oldCancellation = sourceField?.GetValue(viewer) as CancellationTokenSource;
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(oldWork.IsCompleted);
            if (action == "replace")
            {
                Select(hex, 64, 32);
                await Copy(viewer).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                string fresh = (await ClipboardExtensions.TryGetTextAsync(clipboard)) ?? "";
                Assert.Equal(Convert.ToHexString(bytes.AsSpan(64, 32)), fresh);
            }
            else if (action == "close") viewer.Close();
            string beforeStatus = Field<TextBlock>(viewer, "_status").Text ?? "";
            string beforeClipboard = await ClipboardExtensions.TryGetTextAsync(clipboard) ?? "";
            int beforeReads = source.ReadsAfterArm;
            source.Release.Set();
            Exception? error = null;
            try { await oldWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            catch (Exception ex) { error = ex; }
            string afterStatus = Field<TextBlock>(viewer, "_status").Text ?? "";
            string afterClipboard = await ClipboardExtensions.TryGetTextAsync(clipboard) ?? "";
            bool? cancellationDisposed = null;
            if (oldCancellation is not null)
            {
                try { _ = oldCancellation.Token; cancellationDisposed = false; }
                catch (ObjectDisposedException) { cancellationDisposed = true; }
            }
            bool? currentCleared = sourceField is null ? null : sourceField.GetValue(viewer) is null;
            bool unchanged = SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, range, beforeStatus, afterStatus, BeforeClipboardSHA256 = Hash(beforeClipboard), AfterClipboardSHA256 = Hash(afterClipboard), BeforeClipboardCharacters = beforeClipboard.Length, AfterClipboardCharacters = afterClipboard.Length, beforeReads, source.ReadsAfterArm, Error = error?.GetType().FullName, CancellationDisposed = cancellationDisposed, CurrentCopyCleared = currentCleared, OwnedContentUnchanged = unchanged, source.Active, source.DisposalsDuringRead, ActualFileAndPagedReader = true, HeadlessClipboardAndComponentOnly = true }));
            Assert.Null(error);
            Assert.True(unchanged);
            Assert.Equal(0, source.DisposalsDuringRead);
            if (action != "complete")
            {
                Assert.Equal(beforeClipboard, afterClipboard);
                Assert.Equal(beforeStatus, afterStatus);
                Assert.Equal(beforeReads, source.ReadsAfterArm);
            }
            else
            {
                Assert.Equal(Convert.ToHexString(range ? bytes.AsSpan((int)HeldOffset, 4096) : bytes), afterClipboard);
                Assert.Contains("Copied", afterStatus);
                Assert.Equal(range ? 1 : 3, source.ReadsAfterArm);
            }
            if (sourceField is not null) { Assert.True(cancellationDisposed); Assert.True(currentCleared); }
        }
        finally
        {
            source.Release.Set();
            if (oldWork is not null) try { await oldWork.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
            viewer.Close();
            await WaitFor(() => source.Active == 0 && source.Disposals == 1);
            source.Release.Dispose();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static void Select(HexView hex, long start, long length)
    {
        typeof(HexView).GetField("_anchor", Fields)!.SetValue(hex, start);
        typeof(HexView).GetField("_cursor", Fields)!.SetValue(hex, start + length - 1);
        Assert.Equal((start, length), hex.Selection);
    }
    private static Task Copy(ViewerWindow viewer) => (Task)typeof(ViewerWindow).GetMethod("CopyAsync", Fields)!.Invoke(viewer, [])!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned copy checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
    private sealed class HeldSource(string path) : IContentSource
    {
        private readonly FileContentSource _inner = new(path);
        private int _armed, _counting, _reads, _active, _disposals, _during;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public int Active => Volatile.Read(ref _active);
        public int Disposals => Volatile.Read(ref _disposals);
        public int DisposalsDuringRead => Volatile.Read(ref _during);
        public int ReadsAfterArm => Volatile.Read(ref _reads);
        public string DisplayName => _inner.DisplayName;
        public long Length => _inner.Length;
        public bool CanSeek => true;
        public string? LocalPath => _inner.LocalPath;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public void Arm() { Interlocked.Exchange(ref _counting, 1); Interlocked.Exchange(ref _armed, 1); }
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _active);
            try
            {
                if (Volatile.Read(ref _counting) != 0) Interlocked.Increment(ref _reads);
                if (offset == HeldOffset && Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned copy read was not released.");
                }
                return _inner.Read(offset, buffer);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposals) != 1) return;
            if (Active != 0) Interlocked.Increment(ref _during);
            _inner.Dispose();
        }
    }
}
