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
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class DirectoryContentPublicationTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> UnknownCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string side in new[] { "left", "right" })
                foreach (string mode in new[] { "revision-changed", "revision-lost", "revision-error", "incomplete-before", "incomplete-after", "caveat", "early-eof" }) data.Add(side, mode);
            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(UnknownCases))]
    public async Task Mark_results_describe_changed_or_unavailable_bytes_as_unknown(string side, string mode)
    {
        using var f = new Fixture(side, mode); await f.Load(); await (await f.Start()).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Emit("unknown", side, mode, f);
        Assert.Contains("1 could not be compared", f.Left.ComparisonLabel);
        Assert.Equal(1, f.Left.Listing.MarkedCount); Assert.Equal(1, f.Right.Listing.MarkedCount); f.AssertOwned();
    }

    [AvaloniaTheory]
    [InlineData("equal")]
    [InlineData("different")]
    [InlineData("no-revision")]
    public async Task Stable_mark_results_remain_equal_or_different_with_optional_revision_support(string mode)
    {
        using var f = new Fixture("right", mode); await f.Load(); await (await f.Start()).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Emit("positive", "right", mode, f);
        Assert.Contains(mode == "different" ? "1 differ" : "1 same", f.Left.ComparisonLabel);
        Assert.Equal(mode == "different" ? 1 : 0, f.Left.Listing.MarkedCount); f.AssertOwned();
    }

    [AvaloniaTheory]
    [InlineData("left", "before")]
    [InlineData("right", "before")]
    [InlineData("left", "after")]
    [InlineData("right", "after")]
    public async Task A_held_revision_probe_retains_its_source_and_cancels_publication_after_refresh(string side, string phase)
    {
        using var f = new Fixture(side, "equal"); await f.Load(); f.Provider.HoldSide = side; f.Provider.HoldPhase = phase; f.Provider.Release.Reset();
        var task = await f.Start();
        try
        {
            await Wait(() => f.Provider.Entered.IsSet);
            var held = Assert.Single(f.Provider.Sources, s => s.Active);
            bool ui = Dispatcher.UIThread.CheckAccess(); var tab = side == "left" ? f.Left : f.Right;
            tab.Refresh(); await Wait(() => !tab.Listing.IsRefreshing);
            int readsAtCancel = f.Provider.Sources.Sum(s => s.Reads);
            await Task.Delay(50, TestContext.Current.CancellationToken); int heldDisposals = held.Disposals;
            bool pending = !task.IsCompleted;
            f.Provider.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            output.WriteLine("DIRECTORY_CONTENT_PUBLICATION " + JsonSerializer.Serialize(new { Case = "held-revision", side, phase, ui, pending,
                heldDisposals, readsAtCancel, ReadsAfter = f.Provider.Sources.Sum(s => s.Reads), Label = f.Left.ComparisonLabel,
                LeftMarks = f.Left.Listing.MarkedCount, RightMarks = f.Right.Listing.MarkedCount, Calls = f.Provider.Calls.ToArray(), Sources = f.Sources(), Hashes = f.Hashes() }));
            Assert.True(ui); Assert.True(pending); Assert.Equal(0, heldDisposals); Assert.Equal(readsAtCancel, f.Provider.Sources.Sum(s => s.Reads));
            Assert.Null(f.Left.ComparisonLabel); Assert.Equal(0, f.Left.Listing.MarkedCount + f.Right.Listing.MarkedCount); f.AssertOwned();
        }
        finally { f.Provider.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken); }
    }

    private void Emit(string kind, string side, string mode, Fixture f)
        => output.WriteLine("DIRECTORY_CONTENT_PUBLICATION " + JsonSerializer.Serialize(new { Case = kind, side, mode, Label = f.Left.ComparisonLabel,
            LeftMarks = f.Left.Listing.MarkedCount, RightMarks = f.Right.Listing.MarkedCount, Calls = f.Provider.Calls.ToArray(), Sources = f.Sources(), Hashes = f.Hashes() }));
    private static async Task Wait(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew(); while (!condition()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "Owned content publication checkpoint timed out"); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly MainViewModel _vm; private readonly MainWindow _window; private readonly string _root, _leftFile, _rightFile, _leftHash, _rightHash;
        public AppServices Services { get; } public Provider Provider { get; } public TabViewModel Left { get; } public TabViewModel Right { get; }
        public Fixture(string side, string mode)
        {
            (Services, _vm, _window, _root) = AccessibilityTests.OpenMainWindow();
            _leftFile = Path.Join(_root, "content-left.bin"); _rightFile = Path.Join(_root, "content-right.bin");
            File.WriteAllBytes(_leftFile, new byte[32768]); var bytes = new byte[32768]; if (mode == "different") bytes[16321] = 7; File.WriteAllBytes(_rightFile, bytes);
            _leftHash = Hash(_leftFile); _rightHash = Hash(_rightFile); Provider = new Provider(_leftFile, _rightFile, side, mode); Services.Providers.Register(Provider);
            Left = _vm.Workspace.Panels[0].ActiveTab!; Right = _vm.Workspace.Panels[1].ActiveTab!;
        }
        public async Task Load()
        {
            Left.Navigate(new Location(Provider.Scheme, "left")); Right.Navigate(new Location(Provider.Scheme, "right")); _vm.Workspace.Activate(Left.Panel);
            await Wait(() => Left.Listing.State == ListingState.Complete && Right.Listing.State == ListingState.Complete); Provider.Calls.Clear();
        }
        public async Task<Task> Start()
        {
            var task = (Task)typeof(MainViewModel).GetMethod("CompareDirectoriesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_vm, null)!;
            await Wait(() => _window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Content as string == "Size"));
            var checks = _window.GetVisualDescendants().OfType<CheckBox>().ToArray(); checks.Single(c => c.Content as string == "Size").IsChecked = false;
            checks.Single(c => c.Content as string == "Modification time").IsChecked = false;
            checks.Single(c => (c.Content as string)?.StartsWith("Content (", StringComparison.Ordinal) == true).IsChecked = true;
            _window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return task;
        }
        public object Sources() => Provider.Sources.Select(s => new { s.Side, s.Reads, s.Revisions, s.Disposals, s.DisposedDuringCall }).ToArray();
        private static string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        public object Hashes() => new { LeftBefore = _leftHash, LeftAfter = Hash(_leftFile), RightBefore = _rightHash, RightAfter = Hash(_rightFile) };
        public void AssertOwned()
        {
            Assert.All(Provider.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); });
            Assert.All(Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedDuringCall); });
            Assert.Equal(_leftHash, Hash(_leftFile)); Assert.Equal(_rightHash, Hash(_rightFile));
        }
        public void Dispose() { Provider.Release.Set(); AccessibilityTests.Close(Services, _window, _root); Provider.Release.Dispose(); Provider.Entered.Dispose(); }
    }
    private sealed record Call(string Side, string Operation, bool Ui, string Thread);
    private sealed class Provider(string left, string right, string adverseSide, string mode) : ResourceProvider
    {
        public readonly ConcurrentQueue<Source> Sources = new(); public readonly ConcurrentQueue<Call> Calls = new();
        public readonly ManualResetEventSlim Entered = new(), Release = new(true); public string? HoldSide, HoldPhase;
        public override string Scheme => "ownedcontentpublication";
        public override string GetDeviceKey(Location l) => Scheme + ":" + l.Path;
        public override string GetDisplayPath(Location l) => l.Path;
        public override Location? GetParent(Location l) => null;
        public override Location? GetChildLocation(Location l, in EntryData e) => null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public void Boundary(string side, string operation) => Calls.Enqueue(new(side, operation, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
        public void Revision(string side, bool after)
        {
            Boundary(side, "revision");
            if (HoldSide == side && HoldPhase == (after ? "after" : "before")) { Entered.Set(); if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned revision probe not released"); }
        }
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            Boundary(l.Path, "enumerate"); sink.AddBatch([new EntryData("shared.bin", EntryKind.File, 32768)]); return Task.CompletedTask;
        }
        public override IContentSource OpenContent(ItemRef item)
        {
            Boundary(item.Parent.Path, "open"); var source = new Source(item.Parent.Path == "left" ? left : right, item.Parent.Path,
                item.Parent.Path == adverseSide ? mode : "equal", this); Sources.Enqueue(source); return source;
        }
    }
    private sealed class Source(string path, string side, string mode, Provider provider) : IContentSource, IPartialContent
    {
        private readonly FileContentSource _inner = new(path); private int _active;
        public string Side => side; public int Reads, Revisions, Disposals; public bool DisposedDuringCall; public bool Active => Volatile.Read(ref _active) != 0;
        public string DisplayName => _inner.DisplayName; public string? LocalPath => path; public bool CanSeek => true;
        public long Length { get { provider.Boundary(side, "length"); return _inner.Length; } }
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => mode == "incomplete-before" || mode == "incomplete-after" && Reads > 0 ? [(16384L, 8192L)] : [];
        public string? Caveat => mode == "caveat" ? "owned uncertain recovery start" : null;
        public ContentRevision? GetRevision()
        {
            Interlocked.Increment(ref _active); Revisions++;
            try
            {
                provider.Revision(side, Reads > 0);
                if (mode == "no-revision" || mode == "revision-lost" && Reads > 0) return null;
                if (mode == "revision-error" && Reads > 0) throw new IOException("owned revision unavailable");
                var revision = _inner.GetRevision(); return mode == "revision-changed" && Reads > 0 && revision is { } known ? known with { NativeId = "owned changed revision" } : revision;
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public int Read(long offset, Span<byte> buffer)
        {
            provider.Boundary(side, "read"); Reads++; return mode == "early-eof" ? 0 : _inner.Read(offset, buffer[..Math.Min(buffer.Length, 256)]);
        }
        public void Dispose() { if (Active) DisposedDuringCall = true; Disposals++; _inner.Dispose(); }
    }
}
