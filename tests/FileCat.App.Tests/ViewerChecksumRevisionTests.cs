using System.Collections.Concurrent;
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

public sealed class ViewerChecksumRevisionTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long HeldOffset = 2 * PagedReader.PageSize;

    [AvaloniaTheory]
    [InlineData("refresh", false)]
    [InlineData("refresh", true)]
    [InlineData("unchanged", false)]
    [InlineData("unchanged", true)]
    public async Task Viewer_checksum_completion_does_not_replace_status_or_clipboard_after_source_refresh(string action, bool range)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-viewer-checksum-revision-" + Guid.NewGuid().ToString("N"));
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
            // UI layout does not guarantee a page was requested before PendingLoads reaches zero.
            // Cache the first page explicitly so the ordinary four-page read-count oracle has a fixed baseline.
            var firstBytes = new byte[32];
            Assert.Equal(firstBytes.Length, reader.Read(0, firstBytes));
            Assert.Equal(bytes[..firstBytes.Length], firstBytes);
            await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(reader)! == 0);
            bool firstPageCachedBeforeArm = (bool)typeof(PagedReader).GetMethod("HasPage", Fields)!.Invoke(reader, [0L])!;
            Assert.True(firstPageCachedBeforeArm);
            source.Arm();
            oldWork = Checksum(viewer);
            var sourceField = typeof(ViewerWindow).GetField("_checksumCts", Fields);
            var oldCancellation = sourceField?.GetValue(viewer) as CancellationTokenSource;
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(oldWork.IsCompleted);
            var beforeRevision = reader.Revision;
            long beforeGeneration = Field<long>(viewer, "_sourceGeneration");
            byte[] replacement = Enumerable.Range(0, bytes.Length).Select(i => (byte)((i * 31 + 7) % 251)).ToArray();
            if (action == "refresh")
            {
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, new DateTime(beforeRevision!.Value.ModifiedTicks, DateTimeKind.Utc).AddSeconds(10));
            }
            await ((Task)typeof(ViewerWindow).GetMethod("CheckForChanges", Fields)!.Invoke(viewer, [])!)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var afterRevision = reader.Revision;
            long afterGeneration = Field<long>(viewer, "_sourceGeneration");
            string beforeStatus = Field<TextBlock>(viewer, "_status").Text ?? "";
            string beforeClipboard = await ClipboardExtensions.TryGetTextAsync(clipboard) ?? "";
            int beforeReads = source.ReadsAfterArm;
            int beforeChecksumReads = source.ChecksumReadsAfterArm;
            bool canceledAfterPoll = oldCancellation!.IsCancellationRequested;
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
            byte[] finalBytes = File.ReadAllBytes(path);
            string originalSHA256 = Convert.ToHexString(SHA256.HashData(bytes));
            string replacementSHA256 = Convert.ToHexString(SHA256.HashData(replacement));
            string finalSHA256 = Convert.ToHexString(SHA256.HashData(finalBytes));
            bool sourceMatchesExpected = finalBytes.SequenceEqual(action == "refresh" ? replacement : bytes);
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, range, beforeStatus, afterStatus, beforeClipboard, afterClipboard, beforeReads, source.ReadsAfterArm, beforeChecksumReads, source.ChecksumReadsAfterArm, canceledAfterPoll, source.HeldThread, source.ReadLog,
                Error = error?.GetType().FullName, CancellationDisposed = cancellationDisposed, CurrentChecksumCleared = currentCleared, sourceMatchesExpected,
                beforeRevision, afterRevision, beforeGeneration, afterGeneration, originalSHA256, replacementSHA256, finalSHA256,
                source.Active, source.DisposalsDuringRead, ActualOwnedFileAndPagedReaderAndViewerChecksumAndRevisionPoll = true,
                FirstPageCachedBeforeArm = firstPageCachedBeforeArm, HeadlessClipboardAndComponentOnly = true, PhysicalOrHumanOrCandidateQualified = false }));
            Assert.Null(error);
            Assert.True(sourceMatchesExpected);
            Assert.Equal(0, source.DisposalsDuringRead);
            if (action == "refresh")
            {
                Assert.NotEqual(beforeRevision, afterRevision);
                Assert.Equal(beforeGeneration + 1, afterGeneration);
                Assert.Contains("The file changed", beforeStatus);
                Assert.Equal(beforeClipboard, afterClipboard);
                Assert.Equal(beforeStatus, afterStatus);
                Assert.True(canceledAfterPoll);
                Assert.Equal(beforeChecksumReads, source.ChecksumReadsAfterArm);
                Assert.DoesNotContain(source.ReadLog, c => c.Offset >= 3 * PagedReader.PageSize);
            }
            else
            {
                Assert.Equal(beforeRevision, afterRevision);
                Assert.Equal(beforeGeneration, afterGeneration);
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
        private int _armed, _counting, _reads, _active, _disposals, _during, _heldThread;
        private readonly ConcurrentQueue<ReadCall> _readLog = new();
        public sealed record ReadCall(long Offset, int Thread, bool OnUiThread);
        public int HeldThread => Volatile.Read(ref _heldThread);
        public ReadCall[] ReadLog => _readLog.ToArray();
        public int ChecksumReadsAfterArm => _readLog.Count(c => c.Thread == HeldThread);
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
                if (Volatile.Read(ref _counting) != 0)
                {
                    Interlocked.Increment(ref _reads);
                    _readLog.Enqueue(new ReadCall(offset, Environment.CurrentManagedThreadId, Dispatcher.UIThread.CheckAccess()));
                }
                if (offset == HeldOffset && Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Volatile.Write(ref _heldThread, Environment.CurrentManagedThreadId);
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
