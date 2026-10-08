using System.Diagnostics;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerSearchLifetimeTests
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
    public async Task Viewer_search_completion_belongs_to_its_current_demand(string action, bool oldMatch)
    {
        string temp = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Join(temp, "filecat-viewer-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Join(root, "state")));
        var bytes = Encoding.UTF8.GetBytes(new string('x', 4 * PagedReader.PageSize));
        "fresh"u8.CopyTo(bytes.AsSpan(64));
        if (oldMatch) "prior"u8.CopyTo(bytes.AsSpan((int)HeldOffset + 42));
        string path = Path.Join(root, "owned.txt");
        File.WriteAllBytes(path, bytes);
        using var source = new HeldSource(path);
        var viewer = new ViewerWindow(services, source, path, hex: true, deviceKey: "owned-viewer-search");
        Task? oldWork = null;
        try
        {
            viewer.Show();
            await WaitFor(() => Field<DispatcherTimer>(viewer, "_changeTimer").IsEnabled);
            Field<DispatcherTimer>(viewer, "_changeTimer").Stop();
            await WaitFor(() => (int)typeof(PagedReader).GetProperty("PendingLoads", Fields)!.GetValue(Field<PagedReader>(viewer, "_reader"))! == 0);
            Field<TextBox>(viewer, "_search").Text = "prior";
            typeof(ViewerWindow).GetField("_lastHit", Fields)!.SetValue(viewer, HeldOffset - 1);
            typeof(ViewerWindow).GetField("_lastHitLength", Fields)!.SetValue(viewer, 1);
            source.Arm();
            oldWork = Find(viewer);
            var oldSource = Field<CancellationTokenSource>(viewer, "_searchCts");
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(oldWork.IsCompleted);
            if (action == "replace")
            {
                typeof(ViewerWindow).GetField("_lastHit", Fields)!.SetValue(viewer, -1L);
                Field<TextBox>(viewer, "_search").Text = "fresh";
                var replacement = Find(viewer);
                // The replacement's search chunk includes the page still being read by the old
                // demand. It shares that call, so release it before awaiting the new search.
                Assert.False(replacement.IsCompleted);
                Assert.Single(source.ArmedReads.Where(c => c.Offset == HeldOffset));
                source.Release.Set();
                await replacement.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(68, Field<HexView>(viewer, "_hex").CursorOffset);
            }
            else if (action == "close") viewer.Close();
            else if (action == "stop") oldSource.Cancel();
            string statusBeforeRelease = Field<TextBlock>(viewer, "_status").Text ?? "";
            long cursorBeforeRelease = Field<HexView>(viewer, "_hex").CursorOffset;
            source.Release.Set();
            await oldWork.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            bool disposed;
            try { _ = oldSource.Token; disposed = false; } catch (ObjectDisposedException) { disposed = true; }
            string statusAfterRelease = Field<TextBlock>(viewer, "_status").Text ?? "";
            long cursorAfterRelease = Field<HexView>(viewer, "_hex").CursorOffset;
            bool currentCleared = typeof(ViewerWindow).GetField("_searchCts", Fields)!.GetValue(viewer) is null;
            bool unchanged = SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { action, oldMatch, statusBeforeRelease, statusAfterRelease, cursorBeforeRelease, cursorAfterRelease, OldSourceDisposed = disposed, CurrentSearchCleared = currentCleared, OwnedContentUnchanged = unchanged, source.Disposals, source.DisposalsDuringRead, source.Active, ArmedReads = source.ArmedReads.ToArray(), HeadlessComponentOnly = true }));
            Assert.True(unchanged);
            if (action == "stop")
            {
                Assert.Equal("Search canceled.", statusAfterRelease);
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
            Assert.Single(source.ArmedReads.Where(c => c.Offset == HeldOffset));
        }
        finally
        {
            source.Release.Set();
            if (oldWork is not null) await oldWork.WaitAsync(TimeSpan.FromSeconds(10));
            viewer.Close();
            await WaitFor(() => source.Active == 0 && source.Disposals == 1);
            source.Release.Dispose();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(root)));
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Invalid_hex_search_has_no_active_cancellation_source()
    {
        string temp=Path.GetFullPath(Path.GetTempPath()), root=Path.Join(temp,"filecat-viewer-invalid-search-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var services=AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot:Path.Join(root,"state")));
        string path=Path.Join(root,"owned.txt");File.WriteAllText(path,"owned text");
        var viewer=new ViewerWindow(services,new FileContentSource(path),path,hex:true);
        try
        {
            viewer.Show();await WaitFor(()=>Field<DispatcherTimer>(viewer,"_changeTimer").IsEnabled);
            Field<DispatcherTimer>(viewer,"_changeTimer").Stop();
            Field<TextBox>(viewer,"_search").Text="ZZ";Field<CheckBox>(viewer,"_hexSearch").IsChecked=true;
            await Find(viewer);
            bool cleared=typeof(ViewerWindow).GetField("_searchCts",Fields)!.GetValue(viewer) is null;
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { CurrentSearchCleared=cleared, Status=Field<TextBlock>(viewer,"_status").Text, OwnedContentUnchanged=File.ReadAllText(path)=="owned text", HeadlessComponentOnly=true }));
            Assert.Contains("Hex search expects bytes",Field<TextBlock>(viewer,"_status").Text);
            Assert.True(cleared);
        }
        finally { viewer.Close();Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar),Path.GetDirectoryName(Path.GetFullPath(root)));Directory.Delete(root,recursive:true); }
    }

    [AvaloniaFact]
    public async Task A_live_ordinary_search_still_finds_owned_content()
    {
        string temp=Path.GetFullPath(Path.GetTempPath()),root=Path.Join(temp,"filecat-viewer-live-search-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        using var services=AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot:Path.Join(root,"state")));
        string path=Path.Join(root,"owned.txt");File.WriteAllText(path,"owned search content");
        var viewer=new ViewerWindow(services,new FileContentSource(path),path,hex:true);
        try
        {
            viewer.Show();await WaitFor(()=>Field<DispatcherTimer>(viewer,"_changeTimer").IsEnabled);Field<DispatcherTimer>(viewer,"_changeTimer").Stop();
            Field<TextBox>(viewer,"_search").Text="search";await Find(viewer);
            Assert.Equal(11,Field<HexView>(viewer,"_hex").CursorOffset);Assert.Equal("owned search content",File.ReadAllText(path));
        }
        finally { viewer.Close();Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar),Path.GetDirectoryName(Path.GetFullPath(root)));Directory.Delete(root,recursive:true); }
    }

    private static Task Find(ViewerWindow viewer)=>(Task)typeof(ViewerWindow).GetMethod("FindAsync",Fields)!.Invoke(viewer,[true])!;
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Fields)!.GetValue(owner)!;
    private static async Task WaitFor(Func<bool> condition)
    {
        var clock=Stopwatch.StartNew();
        while (!condition()) { if (clock.Elapsed>TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned viewer search checkpoint was not reached.");await Task.Delay(10,TestContext.Current.CancellationToken); }
    }
    private sealed class HeldSource(string path):IContentSource
    {
        private readonly FileContentSource _inner=new(path);
        private int _armed,_active,_disposals,_during,_observe;
        public sealed record SourceRead(long Offset, int Bytes, string Thread);
        public readonly ConcurrentQueue<SourceRead> ArmedReads = new();
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release=new();
        public int Active=>Volatile.Read(ref _active);
        public int Disposals=>Volatile.Read(ref _disposals);
        public int DisposalsDuringRead=>Volatile.Read(ref _during);
        public string DisplayName=>_inner.DisplayName;public long Length=>_inner.Length;public bool CanSeek=>true;public string? LocalPath=>_inner.LocalPath;
        public ContentRevision? GetRevision()=>_inner.GetRevision();
        public void Arm() { Interlocked.Exchange(ref _observe,1);Interlocked.Exchange(ref _armed,1); }
        public int Read(long offset,Span<byte> buffer)
        {
            if (Volatile.Read(ref _observe)!=0) ArmedReads.Enqueue(new(offset,buffer.Length,Thread.CurrentThread.Name ?? ""));
            Interlocked.Increment(ref _active);
            try
            {
                if (offset==HeldOffset && Interlocked.Exchange(ref _armed,0)==1)
                {
                    Entered.TrySetResult();
                    if (!Release.Wait(TimeSpan.FromSeconds(25))) throw new TimeoutException("Owned viewer search read was not released.");
                }
                return _inner.Read(offset,buffer);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() { if (Interlocked.Increment(ref _disposals)!=1)return;if(Active!=0)Interlocked.Increment(ref _during);_inner.Dispose(); }
    }
}
