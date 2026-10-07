using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Edit;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Core.Threading;
using FileCat.Remote.Sftp;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class EditPreparationTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string> EndedDemand
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (string scheme in new[] { "zip", "sftp" })
                foreach (string held in new[] { "open", "read" })
                    foreach (string action in new[] { "refresh", "navigate", "close", "shutdown" }) rows.Add(scheme, held, action);
            return rows;
        }
    }
    [AvaloniaTheory]
    [MemberData(nameof(EndedDemand))]
    public async Task Ended_F4_preparation_keeps_its_owner_until_return_and_publishes_no_editor_or_session(string scheme, string held, string action)
    {
        using var f = new Fixture(scheme, "ordinary", held); await f.Load(); Task? task = null;
        using var rescue = new Timer(_ => f.Gate.Release.Set(), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        try
        {
            task = f.Vm.ExecuteAsync(CommandIds.Edit); await Wait(() => f.Gate.Active > 0 || task.IsCompleted);
            AssertWorker(f); Assert.Equal(1, f.Gate.Active); Assert.False(task.IsCompleted);
            rescue.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (action == "shutdown") f.Services.Io.Dispose();
            else if (action == "refresh") { f.Tab.Refresh(); await Wait(() => !f.Tab.Listing.IsRefreshing); }
            else if (action == "navigate") { f.Tab.Navigate(Location.FileSystem(f.Root)); await Wait(() => f.Tab.Listing.State == ListingState.Complete); }
            else { var replacement = f.Tab.Panel.OpenTab(Location.FileSystem(f.Root)); await Wait(() => replacement.Listing.State == ListingState.Complete); f.Tab.Panel.CloseTab(f.Tab); }
            string? notification = f.Vm.Notification; int disposed = f.Provider.Sources.Sum(s => s.Disposals);
            await Task.Delay(30, TestContext.Current.CancellationToken); bool pending = !task.IsCompleted;
            f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(10));
            Emit(f, "ended", new { held, action, pending, disposed, sessions = f.Services.EditSessions.LoadAll().Count });
            Assert.True(pending); Assert.Equal(0, disposed); Assert.Equal(notification, f.Vm.Notification);
            Assert.Empty(f.Services.EditSessions.LoadAll());
            if (Directory.Exists(f.Services.EditSessions.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(f.Services.EditSessions.Root));
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); }); f.AssertUnchanged();
        }
        finally { f.Gate.Release.Set(); if (task is not null) await task.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [AvaloniaTheory]
    [InlineData("zip")]
    [InlineData("sftp")]
    public async Task A_completed_edit_has_exact_bytes_and_survives_navigation_and_tab_closure(string scheme)
    {
        using var f = new Fixture(scheme); await f.Load();
        await f.Vm.ExecuteAsync(CommandIds.Edit).WaitAsync(TimeSpan.FromSeconds(10));
        var session = Assert.Single(f.Services.EditSessions.LoadAll()); AssertWorker(f);
        Assert.Equal("owned editable bytes", File.ReadAllText(session.WorkingPath));
        Assert.Equal(EditState.Unchanged, f.Services.EditSessions.StateOf(session));
        File.WriteAllText(session.WorkingPath, "owned uncommitted changes");
        f.Tab.Navigate(Location.FileSystem(f.Root)); await Wait(() => f.Tab.Listing.State == ListingState.Complete);
        var replacement = f.Tab.Panel.OpenTab(Location.FileSystem(f.Root)); await Wait(() => replacement.Listing.State == ListingState.Complete); f.Tab.Panel.CloseTab(f.Tab);
        Emit(f, "persistent", new { session.Id, state = f.Services.EditSessions.StateOf(session).ToString() });
        Assert.Equal(session, Assert.Single(f.Services.EditSessions.LoadAll())); Assert.Equal("owned uncommitted changes", File.ReadAllText(session.WorkingPath));
        Assert.All(f.Provider.Sources, s => Assert.Equal(1, s.Disposals)); f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("zip", "io")]
    [InlineData("zip", "access")]
    [InlineData("zip", "invalid")]
    [InlineData("zip", "altered")]
    [InlineData("sftp", "io")]
    [InlineData("sftp", "access")]
    [InlineData("sftp", "invalid")]
    public async Task Failed_preparation_reports_an_error_and_preserves_the_source(string scheme, string outcome)
    {
        using var f = new Fixture(scheme, outcome); await f.Load();
        await f.Vm.ExecuteAsync(CommandIds.Edit).WaitAsync(TimeSpan.FromSeconds(10));
        Emit(f, "error", new { outcome, notification = f.Vm.Notification }); AssertWorker(f);
        Assert.Contains("Cannot edit", f.Vm.Notification); Assert.Empty(f.Services.EditSessions.LoadAll()); f.AssertUnchanged();
        Assert.All(f.Provider.Sources, s => Assert.Equal(1, s.Disposals));
    }

    [AvaloniaTheory]
    [InlineData("zip")]
    [InlineData("sftp")]
    public async Task Eight_distinct_edits_share_bounded_device_workers_while_another_device_progresses(string scheme)
    {
        using var f = new Fixture(scheme, "ordinary", "open"); await f.Load(); var tasks = new List<Task>();
        using var rescue = new Timer(_ => f.Gate.Release.Set(), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        try
        {
            for (int i = 0; i < 8; i++) { Assert.True(f.Tab.Listing.FocusName($"notes{i}.txt")); tasks.Add(f.Vm.ExecuteAsync(CommandIds.Edit)); if (i == 0) { await Wait(() => f.Gate.Active > 0 || tasks[0].IsCompleted); AssertWorker(f); } }
            await Wait(() => f.Gate.Active >= 2 || f.Gate.Release.IsSet); rescue.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            bool other = await f.Services.Io.Run("owned-edit-other-device", IoPriority.Interactive, _ => true).WaitAsync(TimeSpan.FromSeconds(2));
            int active = f.Gate.Active; var health = f.Services.Io.GetHealth(f.Provider.GetDeviceKey(f.Tab.Location!));
            int limit = health == DeviceHealth.Responsive ? f.Services.Io.ThreadsPerDevice : f.Services.Io.MaxThreadsPerDevice;
            f.Gate.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
            Emit(f, "bounded", new { other, active, limit, sessions = f.Services.EditSessions.LoadAll().Count });
            Assert.True(other); Assert.InRange(active, 2, limit); Assert.Equal(8, f.Services.EditSessions.LoadAll().Count); AssertWorker(f); f.AssertUnchanged();
        }
        finally { f.Gate.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [AvaloniaTheory]
    [InlineData("zip")]
    [InlineData("sftp")]
    public async Task Repeated_F4_on_one_pending_member_creates_one_private_copy(string scheme)
    {
        using var f = new Fixture(scheme, "ordinary", "read"); await f.Load(); Task? first = null;
        using var rescue = new Timer(_ => f.Gate.Release.Set(), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        try
        {
            first = f.Vm.ExecuteAsync(CommandIds.Edit); await Wait(() => f.Gate.Active > 0 || first.IsCompleted); AssertWorker(f);
            rescue.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var more = Enumerable.Range(0, 16).Select(_ => f.Vm.ExecuteAsync(CommandIds.Edit)).ToArray();
            await Task.Delay(30, TestContext.Current.CancellationToken); int opens = f.Gate.Calls.Count(c => c.Operation == "open");
            f.Gate.Release.Set(); await Task.WhenAll(more.Append(first)).WaitAsync(TimeSpan.FromSeconds(15));
            Emit(f, "coalesced", new { repeats = 16, opens, sessions = f.Services.EditSessions.LoadAll().Count });
            Assert.Equal(1, opens); Assert.Single(f.Services.EditSessions.LoadAll()); f.AssertUnchanged();
        }
        finally { f.Gate.Release.Set(); if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("EDIT_PREPARATION " + JsonSerializer.Serialize(new
    {
        f.Scheme, control, detail, f.Before, After = f.Hash(), Calls = f.Gate.Calls.ToArray(),
        SyntheticRemoteProvider = f.Scheme == "sftp", NativeDesktopInteraction = false, EditorProcessStarted = false, PhysicalOrNetworkSource = false,
    }));
    private static void AssertWorker(Fixture f) { Assert.NotEmpty(f.Gate.Calls); Assert.All(f.Gate.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); }); }
    private static async Task Wait(Func<bool> done)
    { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned edit preparation checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }

    private sealed class Fixture : IDisposable
    {
        public readonly AppServices Services; public readonly MainViewModel Vm; public readonly MainWindow Window; public readonly string Root, Scheme, Archive, Before;
        public readonly TabViewModel Tab; public readonly Gate Gate; public readonly Provider Provider;
        private readonly Location _location;
        public Fixture(string scheme, string outcome = "ordinary", string? held = null)
        {
            Scheme = scheme; (Services, Vm, Window, Root) = AccessibilityTests.OpenMainWindow(new NoNetworkConnector());
            Services.Settings.Editor = new ToolDefinition { Executable = Path.Join(Root, "owned-editor-does-not-exist.exe") };
            Archive = Path.Join(Root, "owned.zip");
            using (var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create)) for (int i = 0; i < 8; i++) { using var s = zip.CreateEntry($"notes{i}.txt").Open(); s.Write("owned editable bytes"u8); }
            Before = Hash(); var profile = new RemoteProfile { Name = "Owned", Host = "owned.invalid", User = "test" }; Services.Settings.RemoteProfiles.Add(profile);
            _location = scheme == "zip" ? Services.Zip.GetContainerLocation(Archive)! : SftpProvider.At(profile, "/owned");
            Tab = Vm.Workspace.Panels[0].ActiveTab!; Gate = new Gate(held);
            Provider = new Provider(scheme, Services.Providers.Get(scheme), Gate, outcome); Services.Providers.Register(Provider);
        }
        public string Hash() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Archive)));
        public void AssertUnchanged() => Assert.Equal(Before, Hash());
        public async Task Load() { Tab.Navigate(_location); Vm.Workspace.Activate(Tab.Panel); await Wait(() => Tab.Listing.State == ListingState.Complete); Assert.True(Tab.Listing.FocusName("notes0.txt")); }
        public void Dispose()
        {
            Gate.Release.Set();
            // These tests do not qualify persistent-session watcher disposal. Release the fixture's own watchers.
            var unwatch = typeof(MainViewModel).GetMethod("Unwatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var s in Services.EditSessions.LoadAll()) unwatch.Invoke(Vm, [s.Id]);
            AccessibilityTests.Close(Services, Window, Root); Gate.Release.Dispose();
        }
    }
    private sealed class NoNetworkConnector : ISftpConnector
    { public ISftpChannel Connect(RemoteProfile p, ConnectContext c, CancellationToken ct) => throw new IOException("Owned test connector: no network connection is permitted."); }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Gate(string? held)
    {
        public readonly ConcurrentQueue<Call> Calls = new(); public readonly ManualResetEventSlim Release = new(held is null); private int _active;
        public int Active => Volatile.Read(ref _active);
        public void Invoke(string op)
        {
            Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (op != held) return;
            Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new IOException("Owned edit hold expired."); } finally { Interlocked.Decrement(ref _active); }
        }
    }
    private sealed class Provider(string scheme, ResourceProvider inner, Gate gate, string outcome) : ResourceProvider
    {
        public readonly ConcurrentQueue<Source> Sources = new(); public override string Scheme => scheme;
        public override string GetDeviceKey(Location l) => "owned-edit-preparation";
        public override string GetDisplayPath(Location l) => scheme == "zip" ? inner.GetDisplayPath(l) : "owned server: " + l.Path;
        public override Location? GetParent(Location l) => scheme == "zip" ? inner.GetParent(l) : l.WithPath("/");
        public override Location? GetChildLocation(Location l, in EntryData e) => scheme == "zip" ? inner.GetChildLocation(l, e) : null;
        public override LocationCapabilities GetCapabilities(Location l) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct)
        {
            if (scheme == "zip") return inner.EnumerateAsync(l, sink, ct);
            sink.AddBatch(Enumerable.Range(0, 8).Select(i => new EntryData { Name = $"notes{i}.txt", Kind = EntryKind.File, Size = 20 }).ToArray()); return Task.CompletedTask;
        }
        public override IContentSource? OpenContent(ItemRef item)
        {
            gate.Invoke("open"); if (outcome == "io") throw new IOException("Owned source unavailable."); if (outcome == "access") throw new UnauthorizedAccessException("Owned source denied.");
            var source = new Source(scheme == "zip" && outcome != "altered" ? inner.OpenContent(item)! : new MemoryContentSource(item.Name, (outcome == "altered" ? "wrong editable bytes"u8 : "owned editable bytes"u8).ToArray()), gate, outcome);
            Sources.Enqueue(source); return source;
        }
    }
    private sealed class Source(IContentSource inner, Gate gate, string outcome) : IContentSource
    {
        public int Disposals; public bool DisposedWhileReading; private bool _reading;
        public string DisplayName => inner.DisplayName; public long Length => inner.Length; public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            _reading = true; try { gate.Invoke("read"); return outcome == "invalid" ? buffer.Length + 1 : inner.Read(offset, buffer); } finally { _reading = false; }
        }
        public void Dispose() { DisposedWhileReading |= _reading; Disposals++; inner.Dispose(); }
    }
}
