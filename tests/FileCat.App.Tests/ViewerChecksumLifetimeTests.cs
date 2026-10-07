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

public sealed class ViewerChecksumLifetimeTests
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
    public async Task Viewer_checksum_completion_belongs_to_its_current_live_demand(string action, bool range)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-viewer-checksum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        byte[] bytes = Enumerable.Range(0, 4 * PagedReader.PageSize).Select(i => (byte)((i * 17 + 23) % 251)).ToArray();
        string path = Path.Join(root, "owned.bin");
        File.WriteAllBytes(path, bytes);
        using var source = new HeldSource(path);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-viewer-checksum");
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
            await ClipboardExtensions.SetTextAsync(clipboard, "current clipboard before checksum");
            var hex = Field<HexView>(viewer, "_hex");
            if (range) Select(hex, HeldOffset, 4096);
            source.Arm();
            oldWork = Checksum(viewer);
            var sourceField = typeof(ViewerWindow).GetField("_checksumCts", Fields);
            var oldCancellation = sourceField?.GetValue(viewer) as CancellationTokenSource;
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(oldWork.IsCompleted);
            if (action == "replace")
            {
                Select(hex, 64, 32);
                await Checksum(viewer).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                string fresh = (await ClipboardExtensions.TryGetTextAsync(clipboard)) ?? "";
                Assert.Contains("SHA-256: 419bc33e3daa241a93ea0284b83980ef46b250afa59985a963c347ed33084f95", fresh);
                Assert.Contains("CRC-32: ac3ddea0", fresh);
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
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, range, beforeStatus, afterStatus, beforeClipboard, afterClipboard, beforeReads, source.ReadsAfterArm, Error = error?.GetType().FullName, CancellationDisposed = cancellationDisposed, CurrentChecksumCleared = currentCleared, OwnedContentUnchanged = unchanged, source.Active, source.DisposalsDuringRead, ActualFileAndPagedReader = true, HeadlessClipboardAndComponentOnly = true }));
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
                Assert.Contains("SHA-256: " + (range ? "009bbde8ad374ff4374d69adc7bcb73a7652a75bc14dde857fd15f254dd92b7f" : "6b646183f04fba97e2529fc94fc0e58d0c5aa68a8cd2b440f4a285e0b0777515"), afterClipboard);
                Assert.Contains("CRC-32: " + (range ? "4aa67210" : "2a6f43f6"), afterClipboard);
                Assert.DoesNotContain("Warning:", afterClipboard);
                Assert.Contains(range ? "range 0x20000–0x20FFF" : "whole file", afterClipboard);
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

    private static void Select(HexView hex, long start, long length)
    {
        typeof(HexView).GetField("_anchor", Fields)!.SetValue(hex, start);
        typeof(HexView).GetField("_cursor", Fields)!.SetValue(hex, start + length - 1);
        Assert.Equal((start, length), hex.Selection);
    }
    private static Task Checksum(ViewerWindow viewer) => (Task)typeof(ViewerWindow).GetMethod("ChecksumAsync", Fields)!.Invoke(viewer, [])!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned checksum checkpoint was not reached.");
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
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned checksum read was not released.");
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
