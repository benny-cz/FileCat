using System.Diagnostics;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ReportSearchLifetimeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long HeldOffset = 2L * PagedReader.PageSize;

    [AvaloniaTheory]
    [InlineData("replace", true)]
    [InlineData("replace", false)]
    [InlineData("refresh", true)]
    [InlineData("refresh", false)]
    [InlineData("close", true)]
    [InlineData("close", false)]
    [InlineData("complete", true)]
    public async Task Report_search_completion_belongs_to_its_current_live_report(string action, bool oldMatch)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-report-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var chars = Enumerable.Repeat('a', 4 * PagedReader.PageSize).ToArray();
        for (int i = 79; i < chars.Length; i += 80) chars[i] = '\n';
        chars[63] = '\n'; "fresh".CopyTo(0, chars, 64, 5); chars[69] = '\n';
        if (oldMatch) "prior".CopyTo(0, chars, checked((int)HeldOffset + 10), 5);
        string path = Path.Join(root, "report.txt");
        File.WriteAllText(path, new string(chars), new UTF8Encoding(false));
        byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
        int productions = 0;
        var window = new ReportWindow("Owned record", path, async _ =>
        {
            if (Interlocked.Increment(ref productions) == 1) return await File.ReadAllTextAsync(path);
            return "Owned refreshed report\nfresh\n";
        });
        Task? oldWork = null;
        HeldSource? source = null;
        try
        {
            window.Show();
            await window.Reading!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var reader = Field<PagedReader>(window, "_reader");
            await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(reader)! == 0);
            Assert.False((bool)typeof(PagedReader).GetMethod("HasPage", Fields)!.Invoke(reader, [2L])!);
            source = new HeldSource(reader.Source);
            typeof(PagedReader).GetField("_source", Fields)!.SetValue(reader, source);
            source.Arm();
            Field<TextBox>(window, "_search").Text = "prior";
            oldWork = Find(window, true);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (action == "replace")
            {
                Field<TextBox>(window, "_search").Text = "fresh";
                var replacement = Find(window, true);
                // Both searches need the held page; the replacement shares its active read.
                Assert.False(replacement.IsCompleted);
                Assert.Single(source.ArmedReads.Where(c => c.Offset == HeldOffset));
                source.Release.Set();
                await replacement.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(64, Field<long>(window, "_lastHit"));
            }
            else if (action == "refresh") await Read(window).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            else if (action == "close") window.Close();
            string textBefore = window.Text, statusBefore = window.StatusText;
            long hitBefore = Field<long>(window, "_lastHit");
            var readerBefore = Field<PagedReader>(window, "_reader");
            string? highlightBefore = typeof(TextViewer).GetField("_highlight", Fields)!.GetValue(Field<TextViewer>(window, "_view")) as string;
            source.Release.Set();
            Exception? error = null;
            try { await oldWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
            catch (Exception ex) { error = ex; }
            string? highlightAfter = typeof(TextViewer).GetField("_highlight", Fields)!.GetValue(Field<TextViewer>(window, "_view")) as string;
            bool unchanged = hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, oldMatch, productions, hitBefore, HitAfterRelease = Field<long>(window, "_lastHit"), statusBefore, StatusAfterRelease = window.StatusText, highlightBefore, highlightAfter, SameTextAfterRelease = textBefore == window.Text, SameReaderAfterRelease = ReferenceEquals(readerBefore, Field<PagedReader>(window, "_reader")), Error = error?.GetType().FullName, OwnedContentUnchanged = unchanged, source.Active, source.Disposals, source.DisposalsDuringRead, ArmedReads = source.ArmedReads.ToArray(), CompleteActualReportAndMemoryReader = true }));
            Assert.True(unchanged);
            Assert.Null(error);
            Assert.Equal(0, source.Active);
            Assert.Equal(0, source.DisposalsDuringRead);
            Assert.Single(source.ArmedReads.Where(c => c.Offset == HeldOffset));
            if (action == "complete") Assert.Equal(HeldOffset + 10, Field<long>(window, "_lastHit"));
            else
            {
                Assert.Equal(hitBefore, Field<long>(window, "_lastHit"));
                Assert.Equal(statusBefore, window.StatusText);
                Assert.Equal(highlightBefore, highlightAfter);
                Assert.Equal(textBefore, window.Text);
                Assert.Same(readerBefore, Field<PagedReader>(window, "_reader"));
            }
        }
        finally
        {
            source?.Release.Set();
            if (oldWork is not null) { try { await oldWork.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { } }
            window.Close();
            if (source is not null) { Assert.Equal(0, source.Active); Assert.Equal(1, source.Disposals); Assert.Equal(0, source.DisposalsDuringRead); }
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task An_ordinary_report_search_still_finds_forward_and_backward()
    {
        var window = new ReportWindow("Owned record", "owned report", _ => Task.FromResult("prior fresh prior\n"));
        try
        {
            window.Show();
            await window.Reading!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Field<TextBox>(window, "_search").Text = "prior";
            await Find(window, true); Assert.Equal(0, Field<long>(window, "_lastHit"));
            await Find(window, true); Assert.Equal(12, Field<long>(window, "_lastHit"));
            await Find(window, false); Assert.Equal(0, Field<long>(window, "_lastHit"));
        }
        finally { window.Close(); }
    }

    private static Task Find(ReportWindow window, bool forward) => (Task)typeof(ReportWindow).GetMethod("FindAsync", Fields)!.Invoke(window, [forward])!;
    private static Task Read(ReportWindow window) => (Task)typeof(ReportWindow).GetMethod("ReadAsync", Fields)!.Invoke(window, [])!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned report page checkpoint was not reached."); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class HeldSource(IContentSource inner) : IContentSource
    {
        private int _armed, _active, _disposals, _during, _observe;
        public sealed record SourceRead(long Offset, int Bytes, string Thread);
        public readonly ConcurrentQueue<SourceRead> ArmedReads = new();
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public int Active => Volatile.Read(ref _active);
        public int Disposals => Volatile.Read(ref _disposals);
        public int DisposalsDuringRead => Volatile.Read(ref _during);
        public string DisplayName => inner.DisplayName;
        public long Length => inner.Length;
        public bool CanSeek => inner.CanSeek;
        public string? LocalPath => inner.LocalPath;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public void Arm() { Interlocked.Exchange(ref _observe, 1); Interlocked.Exchange(ref _armed, 1); }
        public int Read(long offset, Span<byte> buffer)
        {
            if (Volatile.Read(ref _observe) != 0) ArmedReads.Enqueue(new(offset, buffer.Length, Thread.CurrentThread.Name ?? ""));
            Interlocked.Increment(ref _active);
            try
            {
                if (offset == HeldOffset && Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned report search read was not released.");
                }
                return inner.Read(offset, buffer);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposals) != 1) return;
            if (Active != 0) Interlocked.Increment(ref _during);
            inner.Dispose();
        }
    }
}
