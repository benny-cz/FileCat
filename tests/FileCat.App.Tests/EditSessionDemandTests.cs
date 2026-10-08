using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Edit;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class EditSessionDemandTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string> EndedReads
    {
        get { var rows = new TheoryData<string, string, string>(); foreach (string op in new[] { "restore", "list", "session" }) foreach (string held in new[] { "open", "read" }) foreach (string end in new[] { "shutdown", "close" }) rows.Add(op, held, end); return rows; }
    }
    [AvaloniaTheory]
    [MemberData(nameof(EndedReads))]
    public async Task An_ended_session_read_owns_its_active_source_and_publishes_no_late_prompt(string operation, string held, string end)
    {
        using var f = await Fixture.Create(held); Task? task = null;
        try
        {
            task = f.Start(operation); await Wait(() => f.Gate.Active > 0 || task.IsCompleted || f.HasDialog);
            AssertWorkers(f); Assert.True(f.Gate.Active > 0); Assert.False(task.IsCompleted);
            f.End(end); string? notification = f.Vm.Notification;
            await Task.Delay(30, TestContext.Current.CancellationToken); bool pending = !task.IsCompleted; int disposed = f.Provider.Sources.Sum(s => s.Disposals);
            f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(10));
            Emit(f, "ended", new { operation, held, end, pending, disposed }); Assert.True(pending); Assert.Equal(0, disposed); Assert.Equal(notification, f.Vm.Notification); Assert.Null(f.Button("Close")); Assert.Null(f.Button("Cancel"));
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); }); Assert.Equal("base", File.ReadAllText(f.Session.WorkingPath));
        }
        finally { f.Gate.Release.Set(); await f.Drain(task); }
    }

    [AvaloniaTheory]
    [InlineData("restore")]
    [InlineData("list")]
    [InlineData("session")]
    public async Task Live_review_reads_on_the_shared_worker_and_keeps_the_persistent_edit(string operation)
    {
        using var f = await Fixture.Create(); var task = f.Start(operation);
        if (operation != "restore") { await Wait(() => f.HasDialog); if (operation == "list") f.Escape(); else f.Click("Close"); }
        await task.WaitAsync(TimeSpan.FromSeconds(10)); AssertWorkers(f); Emit(f, "live", new { operation }); Assert.Single(f.Services.EditSessions.LoadAll()); Assert.Equal("base", File.ReadAllText(f.Session.WorkingPath));
    }

    [AvaloniaTheory]
    [InlineData("open", "shutdown")]
    [InlineData("read", "shutdown")]
    [InlineData("open", "close")]
    [InlineData("read", "close")]
    [InlineData("open", "unwatch")]
    [InlineData("read", "unwatch")]
    public async Task Ended_watch_probes_cannot_announce_after_their_source_returns(string held, string end)
    {
        using var f = await Fixture.Create(held); f.Watch(); await f.WatchReady();
        try
        {
            File.WriteAllText(f.Session.WorkingPath, "modified");
            await Wait(() => f.Gate.Active > 0 || (f.Vm.Notification?.Contains("changed in the editor") ?? false)); AssertWorkers(f); Assert.Equal(1, f.Gate.Active);
            f.End(end); string? notification = f.Vm.Notification; int disposed = f.Provider.Sources.Sum(s => s.Disposals);
            f.Gate.Release.Set(); await Wait(() => f.Gate.Active == 0); await Task.Delay(850, TestContext.Current.CancellationToken);
            Emit(f, "watch-ended", new { held, end, disposed }); Assert.Equal(0, disposed); Assert.Equal(notification, f.Vm.Notification); Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); });
        }
        finally { f.Gate.Release.Set(); f.Unwatch(); await Wait(() => f.Gate.Active == 0); }
    }

    [AvaloniaTheory]
    [InlineData("shutdown")]
    [InlineData("close")]
    [InlineData("unwatch")]
    public async Task A_save_event_queued_before_demand_ends_cannot_announce_later(string end)
    {
        using var f = await Fixture.Create(); f.Watch(); await f.WatchReady(); File.WriteAllText(f.Session.WorkingPath, "modified");
        await Task.Delay(100, TestContext.Current.CancellationToken); f.End(end); string? before = f.Vm.Notification;
        await Task.Delay(1100, TestContext.Current.CancellationToken); Emit(f, "watch-queued-end", new { end, before, after = f.Vm.Notification }); Assert.Equal(before, f.Vm.Notification);
        if (end is "close" or "unwatch") Assert.Equal(0, f.WatcherCount);
    }

    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("read")]
    public async Task Sixty_four_save_events_coalesce_behind_one_owned_probe(string held)
    {
        using var f = await Fixture.Create(held); f.Watch(); await f.WatchReady();
        try
        {
            File.WriteAllText(f.Session.WorkingPath, "modified");
            await Wait(() => f.Gate.Active > 0 || (f.Vm.Notification?.Contains("changed in the editor") ?? false)); AssertWorkers(f);
            for (int i = 0; i < 64; i++) File.WriteAllText(f.Session.WorkingPath, "modified " + i);
            await Task.Delay(100, TestContext.Current.CancellationToken); int active = f.Gate.Active; int opens = f.Gate.Calls.Count(c => c.Operation == "open");
            bool other = await f.Services.Io.Run("owned-session-independent", FileCat.Core.Threading.IoPriority.Normal, _ => true).WaitAsync(TimeSpan.FromSeconds(2));
            f.Gate.Release.Set(); await Wait(() => f.Gate.Active == 0); await Task.Delay(1000, TestContext.Current.CancellationToken);
            Emit(f, "watch-coalesced", new { held, active, opens, other }); Assert.Equal(1, active); Assert.Equal(1, opens); Assert.True(other); AssertWorkers(f);
        }
        finally { f.Gate.Release.Set(); f.Unwatch(); await Wait(() => f.Gate.Active == 0); }
    }

    [AvaloniaTheory]
    [InlineData("reopen", "shutdown")]
    [InlineData("commit", "shutdown")]
    [InlineData("discard", "shutdown")]
    [InlineData("reopen", "close")]
    [InlineData("commit", "close")]
    [InlineData("discard", "close")]
    public async Task An_answer_after_shutdown_does_not_launch_or_mutate(string action, string end)
    {
        using var f = await Fixture.Create(); File.WriteAllText(f.Session.WorkingPath, "modified"); var task = f.Start("session"); await Wait(() => f.Button("Close") is not null);
        f.End(end); string? before = f.Vm.Notification; f.Click(action switch { "reopen" => "Reopen editor", "commit" => "Commit", _ => "Discard…" });
        await task.WaitAsync(TimeSpan.FromSeconds(10)); Emit(f, "late-answer", new { action, end, before, after = f.Vm.Notification }); Assert.Equal(before, f.Vm.Notification); Assert.Empty(f.Services.Jobs.Jobs); Assert.Null(f.Button("Discard")); Assert.Single(f.Services.EditSessions.LoadAll());
    }

    [AvaloniaTheory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("invalid")]
    [InlineData("short")]
    [InlineData("unknown")]
    [InlineData("partial")]
    [InlineData("negative")]
    [InlineData("grew")]
    [InlineData("revision")]
    [InlineData("caveat")]
    [InlineData("oversize")]
    public async Task Unavailable_working_content_is_reported_without_offering_commit_or_editor(string outcome)
    {
        using var f = await Fixture.Create(outcome: outcome); var task = f.Start("session");
        try
        {
            await Wait(() => f.Button("Close") is not null);
            var text = string.Join(" ", f.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Emit(f, "unavailable", new { outcome, text }); AssertWorkers(f); Assert.Contains("cannot be read", text); Assert.Null(f.Button("Commit")); Assert.Null(f.Button("Reopen editor")); f.Click("Close"); await task;
        }
        finally { await f.Drain(task); }
    }

    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("read")]
    public async Task A_replaced_watcher_cannot_publish_the_old_probe(string held)
    {
        using var f = await Fixture.Create(held); f.Watch(); await f.WatchReady();
        try
        {
            File.WriteAllText(f.Session.WorkingPath, "modified"); await Wait(() => f.Gate.Active > 0); AssertWorkers(f);
            f.Unwatch(); f.Watch(); await f.WatchReady(); string? before = f.Vm.Notification;
            f.Gate.Release.Set(); await Wait(() => f.Gate.Active == 0); await Task.Delay(900, TestContext.Current.CancellationToken);
            Assert.Equal(before, f.Vm.Notification); Assert.Single(f.Provider.Sources); Assert.Equal(1, f.Provider.Sources.Single().Disposals);
            File.WriteAllText(f.Session.WorkingPath, "next save"); await Wait(() => f.Vm.Notification?.Contains("changed in the editor") == true);
            Emit(f, "watch-generation", new { held, before, after = f.Vm.Notification }); Assert.Equal(1, f.WatcherCount); AssertWorkers(f);
        }
        finally { f.Gate.Release.Set(); f.Unwatch(); await Wait(() => f.Gate.Active == 0); }
    }

    [AvaloniaFact]
    public async Task A_missing_working_file_keeps_the_record_without_editor_or_commit()
    {
        using var f = await Fixture.Create(); File.Delete(f.Session.WorkingPath); var task = f.Start("session");
        try
        {
            await Wait(() => f.Button("Close") is not null); AssertWorkers(f);
            string text = string.Join(" ", f.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Emit(f, "missing", new { text }); Assert.Contains("working copy missing", text); Assert.Null(f.Button("Commit")); Assert.Null(f.Button("Reopen editor")); Assert.Single(f.Services.EditSessions.LoadAll());
            f.Click("Close"); await task;
        }
        finally { await f.Drain(task); }
    }

    [AvaloniaTheory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("changed")]
    [InlineData("shutdown")]
    [InlineData("close")]
    [InlineData("second-io")]
    [InlineData("missing-changed")]
    public async Task Standalone_discard_preserves_saves_not_reviewed_by_its_confirmation(string action)
    {
        using var f = await Fixture.Create(outcome: action == "second-io" ? action : "ordinary");
        File.WriteAllText(f.Session.WorkingPath, "reviewed edit"); f.Watch(); await f.WatchReady();
        if (action == "missing-changed") File.Delete(f.Session.WorkingPath);
        var task = (Task)typeof(MainViewModel).GetMethod("DiscardSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(f.Vm, [f.Session, action == "missing-changed" ? EditState.Missing : EditState.Modified])!;
        try
        {
            await Wait(() => f.Button("Discard") is not null);
            if (action is "changed" or "missing-changed") File.WriteAllText(f.Session.WorkingPath, "later unreviewed save");
            if (action is "shutdown" or "close") f.End(action);
            string? before = f.Vm.Notification; f.Click(action == "cancel" ? "Cancel" : "Discard"); await task.WaitAsync(TimeSpan.FromSeconds(10));
            bool retained = File.Exists(f.Session.WorkingPath); Emit(f, "standalone-discard", new { action, retained, before, after = f.Vm.Notification, watchers = f.WatcherCount });
            AssertWorkers(f); Assert.Equal(action != "success", retained); Assert.Equal(action == "success" ? 0 : 1, f.Services.EditSessions.LoadAll().Count);
            Assert.Equal(action is "success" or "close" ? 0 : 1, f.WatcherCount);
            if (retained) Assert.Equal(action is "changed" or "missing-changed" ? "later unreviewed save" : "reviewed edit", File.ReadAllText(f.Session.WorkingPath));
            if (action is "shutdown" or "close" or "cancel") Assert.Equal(before, f.Vm.Notification);
            Assert.Empty(f.Services.Jobs.Jobs);
        }
        finally { await f.Drain(task); f.Unwatch(); }
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("EDIT_SESSION_DEMAND " + JsonSerializer.Serialize(new { control, detail, Calls = f.Gate.Calls.ToArray(), NativeDesktopInteraction = false, PhysicalOrNetworkSource = false, RealOwnedWorkingFile = true }));
    private static void AssertWorkers(Fixture f) { Assert.NotEmpty(f.Gate.Calls); Assert.All(f.Gate.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); }); }
    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned edit demand checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Gate(string? held)
    {
        public readonly ManualResetEventSlim Release = new(held is null); public readonly ConcurrentQueue<Call> Calls = new(); private int _active;
        public int Active => Volatile.Read(ref _active);
        public void Invoke(string op) { Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (op != held) return; Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(12))) throw new IOException("Owned hold expired."); } finally { Interlocked.Decrement(ref _active); } }
    }
    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window; public required string Root; public required EditSessionRecord Session; public required Provider Provider; public required Gate Gate;
        public static async Task<Fixture> Create(string? held = null, string outcome = "ordinary")
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            var startup = typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) as Task;
            if (startup is not null) await startup; else await (Task)typeof(MainWindow).GetMethod("LoadInterruptedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
            using var source = new MemoryContentSource("notes.txt", "base"u8.ToArray()); var session = services.EditSessions.CreateRemote("owned-unused", "owned", "/notes.txt", source, source.GetRevision()!.Value, null);
            services.Settings.Editor = new FileCat.Core.State.ToolDefinition { Executable = Path.Join(root, "owned-editor-does-not-exist.exe") };
            var gate = new Gate(held); var provider = new Provider(services.Providers.Get(Schemes.FileSystem), session.WorkingPath, gate, outcome); services.Providers.Register(provider);
            return new Fixture { Services = services, Vm = vm, Window = window, Root = root, Session = session, Gate = gate, Provider = provider };
        }
        public int WatcherCount => ((IDictionary)typeof(MainViewModel).GetField("_sessionWatchers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Vm)!).Count;
        public Task Start(string operation)
        {
            if (operation == "restore") { var async = typeof(MainViewModel).GetMethod("RestoreEditSessionsAsync"); if (async is not null) return (Task)async.Invoke(Vm, null)!; typeof(MainViewModel).GetMethod("RestoreEditSessions")!.Invoke(Vm, null); return Task.CompletedTask; }
            return (Task)typeof(MainViewModel).GetMethod(operation == "list" ? "ShowEditSessionsAsync" : "ShowSessionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Vm, operation == "list" ? null : [Session])!;
        }
        public void Watch() => typeof(MainViewModel).GetMethod("Watch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Vm, [Session]);
        public Task WatchReady() => Wait(() =>
        {
            var watchers = (IDictionary)typeof(MainViewModel).GetField("_sessionWatchers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Vm)!;
            var owner = watchers[Session.Id]; return owner is FileSystemWatcher || owner?.GetType().GetField("Watcher")?.GetValue(owner) is FileSystemWatcher;
        });
        public void Unwatch() => typeof(MainViewModel).GetMethod("Unwatch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Vm, [Session.Id]);
        public void End(string end) { if (end == "shutdown") Services.Io.Dispose(); else if (end == "close") Window.Close(); else Unwatch(); }
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public bool HasDialog => Window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("backdrop"));
        public void Escape() => Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public async Task Drain(Task? task) { for (int i = 0; task is not null && !task.IsCompleted && i < 10; i++) { if (Button("Close") is not null) Click("Close"); else if (HasDialog) Escape(); await Task.Delay(20, TestContext.Current.CancellationToken); } if (task is not null) await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        public void Dispose() { Gate.Release.Set(); Unwatch(); AccessibilityTests.Close(Services, Window, Root); Gate.Release.Dispose(); }
    }
    private sealed class Provider(ResourceProvider inner, string path, Gate gate, string outcome) : ResourceProvider
    {
        public readonly ConcurrentQueue<Source> Sources = new(); public override string Scheme => Schemes.FileSystem;
        public override string GetDeviceKey(Location l) => "owned-session-demand";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l);
        public override Location? GetParent(Location l) => inner.GetParent(l);
        public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e);
        public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l);
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        public override IContentSource? OpenContent(ItemRef item)
        {
            if (item.FileSystemPath != path) return inner.OpenContent(item);
            gate.Invoke("open"); if (outcome == "io" || outcome == "second-io" && gate.Calls.Count(c => c.Operation == "open") > 1) throw new IOException("Owned read failure."); if (outcome == "access") throw new UnauthorizedAccessException("Owned read denied.");
            var source = new Source(inner.OpenContent(item)!, gate, outcome); Sources.Enqueue(source); return source;
        }
    }
    private sealed class Source(IContentSource inner, Gate gate, string outcome) : IContentSource, IPartialContent
    {
        public int Disposals; public bool DisposedWhileReading; private bool _reading;
        private int _reads;
        public string DisplayName => inner.DisplayName; public long Length => outcome == "unknown" ? -1 : outcome == "oversize" ? EditSessionStore.MaxMemberBytes + 1 : inner.Length; public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath;
        public ContentRevision? GetRevision() { var revision = inner.GetRevision(); return outcome == "revision" && _reads > 0 && revision is { } r ? r with { ModifiedTicks = r.ModifiedTicks + 1 } : revision; }
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => outcome == "partial" ? [(0, 1)] : []; public string? Caveat => outcome == "caveat" ? "Owned uncertainty" : null;
        public int Read(long offset, Span<byte> buffer) { _reading = true; try { gate.Invoke("read"); _reads++; return outcome == "invalid" ? buffer.Length + 1 : outcome == "negative" ? -1 : outcome == "short" ? 0 : outcome == "grew" && offset == inner.Length ? 1 : inner.Read(offset, buffer); } finally { _reading = false; } }
        public void Dispose() { DisposedWhileReading |= _reading; Disposals++; inner.Dispose(); }
    }
}
