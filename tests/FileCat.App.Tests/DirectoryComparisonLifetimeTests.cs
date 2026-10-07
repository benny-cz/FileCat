using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class DirectoryComparisonLifetimeTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("left", "open", false)]
    [InlineData("left", "read", false)]
    [InlineData("right", "open", false)]
    [InlineData("right", "read", false)]
    [InlineData("left", "open", true)]
    [InlineData("left", "read", true)]
    [InlineData("right", "open", true)]
    [InlineData("right", "read", true)]
    public async Task Mark_comparisons_share_device_workers_and_stop_without_disposing_active_calls(string side, string operation, bool sameDevice)
    {
        using var f = new Fixture("different", sameDevice); await f.Load(); f.Provider.Arm(side, operation);
        var tasks = new List<Task>();
        try
        {
            for (int i = 0; i < 7; i++) tasks.Add(await f.Start(false));
            await Wait(() => f.Provider.Calls.Count(c => c.Side == side && c.Operation == operation) >= 2);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            var held = f.Provider.Calls.Where(c => c.Side == side && c.Operation == operation).ToArray();
            var borrowed = f.Provider.Sources.Where(s => s.Active).ToArray();
            bool other = false; await f.Services.Io.Run("owned-other-directory-device", IoPriority.Interactive, _ => other = true);
            bool ui = Dispatcher.UIThread.CheckAccess(); f.Services.Io.Dispose();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            int pending = tasks.Count(t => !t.IsCompleted), disposedDuringHold = borrowed.Sum(s => s.Disposals);
            f.Provider.Release.Set(); var error = await Finish(tasks);
            output.WriteLine("DIRECTORY_LIFETIME " + JsonSerializer.Serialize(new { Case = "mark-admission", side, operation, sameDevice, Held = held,
                other, ui, pending, disposedDuringHold, Error = error?.GetType().Name, LeftMarks = f.Left.Listing.MarkedCount,
                RightMarks = f.Right.Listing.MarkedCount, Sources = f.Sources(), Calls = f.Provider.Calls.ToArray(), Hashes = f.Hashes() }));
            Assert.Equal(2, held.Length); Assert.True(other); Assert.True(ui); Assert.Equal(2, pending);
            Assert.Null(error); Assert.Equal(0, disposedDuringHold);
            Assert.Equal(0, f.Left.Listing.MarkedCount + f.Right.Listing.MarkedCount);
            Assert.All(f.Provider.Calls, c => { Assert.False(c.OnUiThread); Assert.StartsWith("FileCat I/O ", c.Thread); });
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedDuringCall); }); f.AssertHashes();
        }
        finally { f.Provider.Release.Set(); await Finish(tasks); }
    }

    [AvaloniaTheory]
    [InlineData("left", "navigate")]
    [InlineData("right", "navigate")]
    [InlineData("left", "refresh")]
    [InlineData("right", "refresh")]
    [InlineData("left", "close")]
    [InlineData("right", "close")]
    public async Task An_old_comparison_never_marks_or_labels_a_changed_or_closed_tab(string side, string action)
    {
        using var f = new Fixture("different"); await f.Load(); f.Provider.Arm("left", "read");
        var task = await f.Start(false);
        try
        {
            await Wait(() => f.Provider.Calls.Any(c => c.Operation == "read"));
            var changed = side == "left" ? f.Left : f.Right;
            if (action == "navigate")
            {
                changed.Navigate(new Location(f.Provider.Scheme, "replacement")); await Wait(() => changed.Listing.State == ListingState.Complete);
                // Returning to the same location must not revive a comparison of the earlier store.
                changed.Navigate(new Location(f.Provider.Scheme, side)); await Wait(() => changed.Listing.State == ListingState.Complete);
            }
            else if (action == "refresh") { changed.Refresh(); await Wait(() => !changed.Listing.IsRefreshing); }
            else
            {
                var replacement = changed.Panel.OpenTab(new Location(f.Provider.Scheme, "replacement"));
                await Wait(() => replacement.Listing.State == ListingState.Complete);
                changed.Panel.CloseTab(changed);
            }
            int readsBeforeRelease = f.Provider.Calls.Count(c => c.Operation == "read");
            int disposalsHeld = f.Provider.Sources.Sum(s => s.Disposals);
            string? labelBefore = changed.ComparisonLabel;
            f.Provider.Release.Set(); var error = await Finish([task]);
            output.WriteLine("DIRECTORY_LIFETIME " + JsonSerializer.Serialize(new { Case = "stale-mark", side, action, readsBeforeRelease,
                disposalsHeld, Error = error?.GetType().Name, labelBefore, LabelAfter = changed.ComparisonLabel,
                LeftMarks = f.Left.Listing.MarkedCount, RightMarks = f.Right.Listing.MarkedCount, Calls = f.Provider.Calls.ToArray(), Sources = f.Sources(), Hashes = f.Hashes() }));
            Assert.Null(error); Assert.Equal(0, disposalsHeld);
            Assert.Equal(labelBefore, changed.ComparisonLabel); Assert.Equal(0, f.Left.Listing.MarkedCount + f.Right.Listing.MarkedCount);
            Assert.Equal(readsBeforeRelease, f.Provider.Calls.Count(c => c.Operation == "read"));
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedDuringCall); }); f.AssertHashes();
        }
        finally { f.Provider.Release.Set(); await Finish([task]); }
    }

    [AvaloniaTheory]
    [InlineData("equal")]
    [InlineData("different")]
    [InlineData("io")]
    public async Task Responsive_and_unreadable_mark_comparisons_remain_truthful(string outcome)
    {
        using var f = new Fixture(outcome); await f.Load(); var task = await f.Start(false);
        var error = await Finish([task]);
        output.WriteLine("DIRECTORY_LIFETIME " + JsonSerializer.Serialize(new { Case = "responsive-mark", outcome, Error = error?.GetType().Name,
            Label = f.Left.ComparisonLabel, LeftMarks = f.Left.Listing.MarkedCount, RightMarks = f.Right.Listing.MarkedCount,
            Calls = f.Provider.Calls.ToArray(), Sources = f.Sources(), Hashes = f.Hashes() }));
        Assert.Null(error); Assert.Equal(outcome == "equal" ? 0 : 1, f.Left.Listing.MarkedCount);
        Assert.Equal(outcome == "equal" ? 0 : 1, f.Right.Listing.MarkedCount);
        Assert.Contains(outcome == "io" ? "1 could not be compared" : outcome == "equal" ? "1 same" : "1 differ", f.Left.ComparisonLabel);
        Assert.All(f.Provider.Sources, s => Assert.Equal(1, s.Disposals)); f.AssertHashes();
    }

    [AvaloniaTheory]
    [InlineData("left", "enumerate", false)]
    [InlineData("left", "read", false)]
    [InlineData("right", "enumerate", false)]
    [InlineData("right", "read", false)]
    [InlineData("left", "enumerate", true)]
    [InlineData("left", "read", true)]
    [InlineData("right", "enumerate", true)]
    [InlineData("right", "read", true)]
    public async Task Recursive_comparisons_share_device_workers_and_close_without_late_publication(string side, string operation, bool sameDevice)
    {
        using var f = new Fixture("different", sameDevice); await f.Load(); f.Provider.Arm(side, operation);
        var before = DirectoryDiffWindow.OpenWindows.ToHashSet(); var windows = new List<DirectoryDiffWindow>();
        try
        {
            for (int i = 0; i < 7; i++) { await Finish([await f.Start(true)]); windows.Add(Assert.Single(DirectoryDiffWindow.OpenWindows, w => !before.Contains(w) && !windows.Contains(w))); }
            await Wait(() => f.Provider.Calls.Count(c => c.Side == side && c.Operation == operation) >= 2);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            var held = f.Provider.Calls.Where(c => c.Side == side && c.Operation == operation).ToArray();
            foreach (var w in windows) w.Close(); string[] closed = windows.Select(w => w.Summary).ToArray();
            int disposedDuringHold = f.Provider.Sources.Sum(s => s.Disposals);
            f.Services.Io.Dispose(); f.Provider.Release.Set();
            await Wait(() => f.Provider.Active == 0); await Task.Delay(200, TestContext.Current.CancellationToken);
            output.WriteLine("DIRECTORY_LIFETIME " + JsonSerializer.Serialize(new { Case = "tree-admission", side, operation, sameDevice, Held = held,
                disposedDuringHold, Closed = closed, Final = windows.Select(w => w.Summary).ToArray(), Calls = f.Provider.Calls.ToArray(), Sources = f.Sources(), Hashes = f.Hashes() }));
            Assert.Equal(2, held.Length); Assert.Equal(0, disposedDuringHold); Assert.Equal(closed, windows.Select(w => w.Summary));
            Assert.All(f.Provider.Calls, c => { Assert.False(c.OnUiThread); Assert.StartsWith("FileCat I/O ", c.Thread); });
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedDuringCall); }); f.AssertHashes();
        }
        finally { f.Provider.Release.Set(); await Wait(() => f.Provider.Active == 0); foreach (var w in windows) w.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_a_recursive_preview_does_not_publish_its_late_result()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var w = DirectoryDiffWindow.Start("left", "right", "owned close control", (_, _) =>
        {
            entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned preview not released");
            returned.SetResult(); return new TreeCompareResult([new TreeDiffEntry("owned", TreeDiffKind.LeftOnly, new EntryData("owned", EntryKind.File), null)], true, 1);
        }, (_, _) => { });
        try
        {
            await Wait(() => entered.IsSet); w.Close(); string closed = w.Summary; release.Set(); await returned.Task;
            await Task.Delay(100, TestContext.Current.CancellationToken);
            output.WriteLine("DIRECTORY_LIFETIME " + JsonSerializer.Serialize(new { Case = "closed-preview", Closed = closed, Final = w.Summary, Entries = w.ShownEntries.Count }));
            Assert.Equal(closed, w.Summary); Assert.Empty(w.ShownEntries);
        }
        finally { release.Set(); await returned.Task; w.Close(); }
    }

    private static async Task Wait(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew(); while (!condition()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "Owned directory comparison checkpoint timed out"); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private static async Task<Exception?> Finish(IEnumerable<Task> tasks)
    {
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); return null; }
        catch (Exception e) { return e; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly MainWindow _window; private readonly MainViewModel _vm; private readonly string _root, _leftFile, _rightFile, _leftHash, _rightHash;
        public AppServices Services { get; }
        public Provider Provider { get; }
        public TabViewModel Left { get; }
        public TabViewModel Right { get; }
        public Fixture(string outcome, bool sameDevice = false)
        {
            (Services, _vm, _window, _root) = AccessibilityTests.OpenMainWindow();
            _leftFile = Path.Join(_root, "owned-left.bin"); _rightFile = Path.Join(_root, "owned-right.bin");
            var bytes = Enumerable.Range(0, 32768).Select(i => (byte)(i * 19 % 251)).ToArray(); File.WriteAllBytes(_leftFile, bytes);
            if (outcome != "equal") bytes[15321] ^= 16; File.WriteAllBytes(_rightFile, bytes); _leftHash = Hash(_leftFile); _rightHash = Hash(_rightFile);
            Provider = new Provider(_leftFile, _rightFile, outcome, sameDevice); Services.Providers.Register(Provider);
            Left = _vm.Workspace.Panels[0].ActiveTab!; Right = _vm.Workspace.Panels[1].ActiveTab!;
        }
        public async Task Load()
        {
            Left.Navigate(new Location(Provider.Scheme, "left")); Right.Navigate(new Location(Provider.Scheme, "right")); _vm.Workspace.Activate(Left.Panel);
            await Wait(() => Left.Listing.State == ListingState.Complete && Right.Listing.State == ListingState.Complete); Provider.Calls.Clear();
        }
        public async Task<Task> Start(bool recursive)
        {
            var task = (Task)typeof(MainViewModel).GetMethod("CompareDirectoriesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_vm, null)!;
            await Wait(() => _window.GetVisualDescendants().OfType<CheckBox>().Any(c => (c.Content as string)?.StartsWith("Include subfolders", StringComparison.Ordinal) == true));
            var checks = _window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            checks.Single(c => c.Content as string == "Size").IsChecked = false;
            checks.Single(c => c.Content as string == "Modification time").IsChecked = false;
            checks.Single(c => (c.Content as string)?.StartsWith("Content (", StringComparison.Ordinal) == true).IsChecked = true;
            checks.Single(c => (c.Content as string)?.StartsWith("Include subfolders", StringComparison.Ordinal) == true).IsChecked = recursive;
            _window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return task;
        }
        private static string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        public object Hashes() => new { LeftBefore = _leftHash, LeftAfter = Hash(_leftFile), RightBefore = _rightHash, RightAfter = Hash(_rightFile) };
        public object Sources() => Provider.Sources.Select(s => new { s.Side, s.Reads, s.Disposals, s.DisposedDuringCall }).ToArray();
        public void AssertHashes() { Assert.Equal(_leftHash, Hash(_leftFile)); Assert.Equal(_rightHash, Hash(_rightFile)); }
        public void Dispose() { Provider.Release.Set(); AccessibilityTests.Close(Services, _window, _root); Provider.Release.Dispose(); }
    }
    private sealed record Call(string Side, string Operation, bool OnUiThread, string Thread);
    private sealed class Provider(string left, string right, string outcome, bool sameDevice) : ResourceProvider
    {
        public readonly ConcurrentQueue<Call> Calls = new(); public readonly ConcurrentQueue<Source> Sources = new();
        public readonly ManualResetEventSlim Release = new(true); private string? _side, _operation; private int _active;
        public int Active => Volatile.Read(ref _active);
        public void Arm(string side, string operation) { _side = side; _operation = operation; Release.Reset(); }
        public void Invoke(string side, string operation)
        {
            Interlocked.Increment(ref _active); Calls.Enqueue(new Call(side, operation, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            try { if (_side == side && _operation == operation && !Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned directory provider was not released"); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public override string Scheme => "owneddirectorycomparison";
        public override string GetDisplayPath(Location l) => l.Path;
        public override string GetDeviceKey(Location l) => Scheme + ":" + (sameDevice ? "shared" : l.Path);
        public override Location? GetParent(Location l) => null;
        public override Location? GetChildLocation(Location l, in EntryData e) => null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            Invoke(l.Path, "enumerate"); sink.AddBatch([new EntryData("shared.bin", EntryKind.File, 32768)]); return Task.CompletedTask;
        }
        public override IContentSource? OpenContent(ItemRef item)
        {
            Invoke(item.Parent.Path, "open"); if (item.Parent.Path == "right" && outcome == "io") throw new IOException("owned content unavailable");
            var s = new Source(item.Parent.Path == "left" ? left : right, item.Parent.Path, this); Sources.Enqueue(s); return s;
        }
    }
    private sealed class Source(string path, string side, Provider provider) : IContentSource
    {
        private readonly FileContentSource _inner = new(path); private int _active;
        public bool Active => Volatile.Read(ref _active) != 0;
        public string Side => side; public int Reads, Disposals; public bool DisposedDuringCall;
        public string DisplayName => _inner.DisplayName; public string? LocalPath => path; public bool CanSeek => true; public long Length => _inner.Length;
        public ContentRevision? GetRevision() => _inner.GetRevision();
        public int Read(long offset, Span<byte> into)
        {
            Interlocked.Increment(ref _active); Interlocked.Increment(ref Reads);
            try { provider.Invoke(side, "read"); return _inner.Read(offset, into[..Math.Min(into.Length, 256)]); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() { if (Volatile.Read(ref _active) != 0) DisposedDuringCall = true; Interlocked.Increment(ref Disposals); _inner.Dispose(); }
    }
}
