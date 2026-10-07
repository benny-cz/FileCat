using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class HexEditorSearchLifetimeTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const long HeldOffset = 2 * PagedReader.PageSize;

    [AvaloniaTheory]
    [InlineData("replace", true)]
    [InlineData("replace", false)]
    [InlineData("close", true)]
    [InlineData("close", false)]
    [InlineData("complete", true)]
    [InlineData("stop", true)]
    public async Task Hex_editor_search_completion_belongs_to_its_current_demand(string action, bool oldMatch)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-hex-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        var bytes = Encoding.UTF8.GetBytes(new string('x', 4 * PagedReader.PageSize));
        "fresh"u8.CopyTo(bytes.AsSpan(64));
        if (oldMatch) "prior"u8.CopyTo(bytes.AsSpan((int)HeldOffset + 42));
        string path = Path.Join(root, "owned.txt");
        File.WriteAllBytes(path, bytes);
        Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
        var window = Assert.Single(HexEditorWindow.OpenWindows);
        var reader = Field<PagedReader>(window, "_reader");
        await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(reader)! == 0);
        // Keep the actual editor overlay/protected file and reader; hold one actual provider call at their boundary.
        var source = new HeldSource(reader.Source);
        typeof(PagedReader).GetField("_source", Fields)!.SetValue(reader, source);
        Task? oldWork = null;
        try
        {
            Field<TextBox>(window, "_search").Text = "prior";
            source.Arm();
            oldWork = Find(window);
            var oldSource = Field<CancellationTokenSource>(window, "_searchCts");
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(oldWork.IsCompleted);
            if (action == "replace")
            {
                Field<TextBox>(window, "_search").Text = "fresh";
                await Find(window).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(68, Field<HexView>(window, "_hex").CursorOffset);
            }
            else if (action == "close") window.CloseNow();
            else if (action == "stop") oldSource.Cancel();
            string statusBeforeRelease = Field<TextBlock>(window, "_status").Text ?? "";
            long cursorBeforeRelease = Field<HexView>(window, "_hex").CursorOffset;
            source.Release.Set();
            await oldWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            bool disposed;
            try { _ = oldSource.Token; disposed = false; } catch (ObjectDisposedException) { disposed = true; }
            string statusAfterRelease = Field<TextBlock>(window, "_status").Text ?? "";
            long cursorAfterRelease = Field<HexView>(window, "_hex").CursorOffset;
            bool currentCleared = typeof(HexEditorWindow).GetField("_searchCts", Fields)!.GetValue(window) is null;
            bool unchanged = SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(ReadOwned(path)));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, oldMatch, statusBeforeRelease, statusAfterRelease, cursorBeforeRelease, cursorAfterRelease, OldSourceDisposed = disposed, CurrentSearchCleared = currentCleared, OwnedContentUnchanged = unchanged, source.Disposals, source.DisposalsDuringRead, source.Active, ActualProtectedFileOverlayAndReader = true, HeadlessComponentOnly = true }));
            Assert.True(unchanged);
            if (action == "stop")
            {
                Assert.StartsWith("Search stopped.", statusAfterRelease, StringComparison.Ordinal);
                Assert.Equal(cursorBeforeRelease, cursorAfterRelease);
            }
            else if (action != "complete")
            {
                Assert.Equal(statusBeforeRelease, statusAfterRelease);
                Assert.Equal(cursorBeforeRelease, cursorAfterRelease);
            }
            else Assert.Equal(HeldOffset + 46, cursorAfterRelease);
            Assert.True(disposed);
            Assert.True(currentCleared);
            Assert.Equal(0, source.DisposalsDuringRead);
        }
        finally
        {
            source.Release.Set();
            if (oldWork is not null) await oldWork.WaitAsync(TimeSpan.FromSeconds(10));
            window.CloseNow();
            await WaitFor(() => source.Active == 0 && source.Disposals == 1);
            source.Release.Dispose();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Invalid_hex_search_has_no_active_editor_search_source()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-hex-invalid-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        string path = Path.Join(root, "owned.txt");
        File.WriteAllText(path, "owned text");
        Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
        var window = Assert.Single(HexEditorWindow.OpenWindows);
        try
        {
            Field<TextBox>(window, "_search").Text = "ZZ";
            Field<CheckBox>(window, "_hexSearch").IsChecked = true;
            await Find(window);
            Assert.Contains("Hex search expects bytes", Field<TextBlock>(window, "_status").Text);
            Assert.Null(typeof(HexEditorWindow).GetField("_searchCts", Fields)!.GetValue(window));
            Assert.Equal("owned text", Encoding.UTF8.GetString(ReadOwned(path)));
        }
        finally { window.CloseNow(); Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root))); Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_live_ordinary_editor_search_still_finds_owned_content()
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-hex-live-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        string path = Path.Join(root, "owned.txt");
        File.WriteAllText(path, "owned search content");
        Assert.Null(HexEditorWindow.OpenOrActivate(services, path));
        var window = Assert.Single(HexEditorWindow.OpenWindows);
        try
        {
            Field<TextBox>(window, "_search").Text = "search";
            await Find(window);
            Assert.Equal(11, Field<HexView>(window, "_hex").CursorOffset);
            Assert.Equal("owned search content", Encoding.UTF8.GetString(ReadOwned(path)));
        }
        finally { window.CloseNow(); Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root))); Directory.Delete(root, recursive: true); }
    }

    private static byte[] ReadOwned(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static Task Find(HexEditorWindow window) => (Task)typeof(HexEditorWindow).GetMethod("FindAsync", Fields)!.Invoke(window, [true])!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned editor search checkpoint was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
    private sealed class HeldSource(IContentSource inner) : IContentSource
    {
        private int _armed, _active, _disposals, _during;
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
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public int Read(long offset, Span<byte> buffer)
        {
            Interlocked.Increment(ref _active);
            try
            {
                if (offset == HeldOffset && Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned editor search read was not released.");
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

