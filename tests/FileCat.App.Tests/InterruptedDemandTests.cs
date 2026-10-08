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
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class InterruptedDemandTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("changed")]
    [InlineData("same-length-time")]
    [InlineData("replacement")]
    [InlineData("removed")]
    [InlineData("unchanged")]
    [InlineData("cancel")]
    public async Task Staged_cleanup_rechecks_the_reviewed_bytes_after_the_actual_confirmation(string change)
    {
        using var f = await Fixture.Create(); var run = f.Vm.RunInterruptedAgainAsync(f.Interrupted);
        await Wait(() => f.HasDialog);
        var stamp = File.GetLastWriteTimeUtc(f.Staged);
        if (change == "changed") File.WriteAllText(f.Staged, "user's newer staged bytes");
        if (change == "same-length-time") { File.WriteAllText(f.Staged, "other"); File.SetLastWriteTimeUtc(f.Staged, stamp); }
        if (change == "replacement") { File.Delete(f.Staged); File.WriteAllText(f.Staged, "replacement"); }
        if (change == "removed") File.Delete(f.Staged);
        if (change == "cancel") f.Escape(); else f.Click("Copy the rest");
        await f.Drain(run); bool retained = File.Exists(f.Staged); int jobs = f.Services.Jobs.Jobs.Count;
        Emit(f, "staged-change", new { change, retained, jobs, Ended = f.Ended, Started = await run });
        Assert.Equal(change is "changed" or "same-length-time" or "replacement" or "cancel", retained);
        Assert.Equal(change == "cancel" ? 1 : 2, jobs); Assert.Equal(change != "cancel", f.Ended);
        if (change == "same-length-time") Assert.Equal("other", File.ReadAllText(f.Staged));
    }

    [AvaloniaTheory]
    [InlineData("shutdown", "copy")]
    [InlineData("close", "copy")]
    [InlineData("shutdown", "move")]
    [InlineData("close", "move")]
    public async Task Approval_after_demand_ends_cannot_delete_close_the_journal_or_submit(string end, string kind)
    {
        using var f = await Fixture.Create(); var original = f.Interrupted with { Kind = kind == "move" ? nameof(JobKind.Move) : nameof(JobKind.Copy) };
        var run = f.Vm.RunInterruptedAgainAsync(original); await Wait(() => f.HasDialog);
        f.End(end); string? notification = f.Vm.Notification; f.Click(kind == "move" ? "Move the rest" : "Copy the rest"); await f.Drain(run);
        Emit(f, "ended-approval", new { end, kind, Started = await run, Retained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs); Assert.Equal(notification, f.Vm.Notification);
    }

    [AvaloniaFact]
    public async Task Repeated_run_again_has_one_owner_and_one_confirmation()
    {
        using var f = await Fixture.Create(); var first = f.Vm.RunInterruptedAgainAsync(f.Interrupted); await Wait(() => f.HasDialog);
        var second = f.Vm.RunInterruptedAgainAsync(f.Interrupted); bool secondFinished = second.IsCompleted;
        f.Click("Copy the rest"); await f.Drain(first, second);
        Emit(f, "coalesced", new { secondFinished, First = await first, Second = await second, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.True(secondFinished); Assert.False(await second); Assert.True(await first); Assert.Equal(2, f.Services.Jobs.Jobs.Count);
    }

    [AvaloniaTheory]
    [InlineData("open", "shutdown")]
    [InlineData("read", "shutdown")]
    [InlineData("open", "close")]
    [InlineData("read", "close")]
    public async Task An_active_staged_review_retains_its_source_until_the_call_returns(string held, string end)
    {
        using var f = await Fixture.Create(held); var run = f.Vm.RunInterruptedAgainAsync(f.Interrupted);
        try
        {
            await Wait(() => f.Provider.Active > 0 || f.HasDialog || run.IsCompleted);
            Assert.NotEmpty(f.Provider.Calls); Assert.Equal(1, f.Provider.Active); Assert.False(run.IsCompleted); Assert.False(f.HasDialog);
            f.End(end); bool pending = !run.IsCompleted; int disposed = f.Provider.Disposals; f.Provider.Release.Set(); await f.Drain(run);
            Emit(f, "held-end", new { held, end, pending, disposed, Started = await run, Retained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
            Assert.True(pending); Assert.Equal(0, disposed); Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs);
            Assert.Equal(1, f.Provider.Disposals); Assert.False(f.Provider.DisposedDuringRead); Workers(f);
        }
        finally { f.Provider.Release.Set(); await f.Drain(run); }
    }

    [AvaloniaTheory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("unknown")]
    [InlineData("oversize")]
    [InlineData("negative")]
    [InlineData("invalid")]
    [InlineData("short")]
    [InlineData("growth")]
    [InlineData("revision")]
    [InlineData("partial")]
    [InlineData("caveat")]
    public async Task An_unavailable_staged_review_keeps_the_file_and_names_it(string outcome)
    {
        using var f = await Fixture.Create(outcome: outcome); var run = f.Vm.RunInterruptedAgainAsync(f.Interrupted); await Wait(() => f.HasDialog || run.IsCompleted);
        if (f.HasDialog) f.Click("Copy the rest"); await f.Drain(run);
        Emit(f, "unavailable", new { outcome, Retained = File.Exists(f.Staged), Started = await run, Jobs = f.Services.Jobs.Jobs.Count });
        Workers(f); Assert.True(File.Exists(f.Staged)); Assert.True(await run); Assert.Equal(2, f.Services.Jobs.Jobs.Count);
    }

    [AvaloniaTheory]
    [InlineData("shutdown")]
    [InlineData("close")]
    public async Task Ending_demand_during_the_kept_file_alert_leaves_the_old_journal_open(string end)
    {
        using var f = await Fixture.Create(); var cut = Path.Join(f.Destination, "cut.txt"); var source = Path.Join(f.SourceFolder, "cut.txt");
        File.WriteAllText(source, "the whole source"); File.WriteAllText(cut, "the whole");
        var job = f.Interrupted with { FillDirectories = [new FillDirectory(f.SourceFolder, f.Destination)] };
        var run = f.Vm.RunInterruptedAgainAsync(job); await Wait(() => f.HasDialog); File.WriteAllText(cut, "newer destination bytes"); f.Click("Copy the rest");
        await Wait(() => f.Button("OK") is not null || run.IsCompleted); Assert.NotNull(f.Button("OK"));
        f.End(end); f.Click("OK"); await f.Drain(run);
        Emit(f, "kept-alert-end", new { end, Started = await run, Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.False(await run); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs); Assert.Equal("newer destination bytes", File.ReadAllText(cut));
    }

    [AvaloniaTheory]
    [InlineData("changed")]
    [InlineData("same-length-time")]
    [InlineData("replacement")]
    [InlineData("unchanged")]
    [InlineData("cancel")]
    [InlineData("shutdown")]
    [InlineData("close")]
    public async Task The_real_cleanup_button_uses_the_same_staged_review_and_ended_demand_guards(string change)
    {
        using var f = await Fixture.Create(); f.Cleanup(); await Wait(() => f.HasDialog);
        string? notification = f.Vm.Notification; var stamp = File.GetLastWriteTimeUtc(f.Staged);
        if (change == "changed") File.WriteAllText(f.Staged, "newer");
        if (change == "same-length-time") { File.WriteAllText(f.Staged, "other"); File.SetLastWriteTimeUtc(f.Staged, stamp); }
        if (change == "replacement") { File.Delete(f.Staged); File.WriteAllText(f.Staged, "replacement"); }
        if (change is "shutdown" or "close") f.End(change);
        if (change == "cancel") f.Escape(); else f.Click("Delete partial files");
        if (change is "shutdown" or "close" or "cancel") await Task.Delay(200, TestContext.Current.CancellationToken);
        // Poll the UI acknowledgement, not the journal: a recovery scan briefly opens it exclusively and
        // would inject a sharing failure into the very reconciliation write this control is observing.
        else await Wait(() => f.Vm.Notification != notification || f.Button("OK") is not null);
        if (f.Button("OK") is not null) { f.Click("OK"); await Wait(() => f.Vm.Notification != notification); }
        Emit(f, "cleanup-button", new { change, Retained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.Equal(change != "unchanged", File.Exists(f.Staged)); Assert.Equal(change is not ("shutdown" or "close" or "cancel"), f.Ended); Assert.Single(f.Services.Jobs.Jobs);
        if (change is "shutdown" or "close") Assert.Equal(notification, f.Vm.Notification);
    }

    [AvaloniaFact]
    public async Task A_closed_journal_cannot_be_run_again_using_a_stale_row()
    {
        using var f = await Fixture.Create(); JournalRecovery.Close(f.Interrupted, "already reviewed");
        var run = f.Vm.RunInterruptedAgainAsync(f.Interrupted); await Wait(() => run.IsCompleted || f.HasDialog); bool prompted = f.HasDialog; await f.Drain(run);
        Emit(f, "closed-journal", new { Started = await run, Prompted = prompted, Retained = File.Exists(f.Staged), Jobs = f.Services.Jobs.Jobs.Count });
        Assert.False(prompted); Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.Single(f.Services.Jobs.Jobs);
    }

    [AvaloniaFact]
    public async Task A_failed_journal_close_preserves_the_durable_source_manifest()
    {
        using var f = await Fixture.Create(); var manifest = JobJournal.ManifestPathOf(f.Interrupted.JournalPath); File.WriteAllText(manifest, "owned source manifest");
        string failure;
        if (OperatingSystem.IsWindows())
        {
            failure = "Windows sharing refusal";
            using var held = new FileStream(f.Interrupted.JournalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Throws<IOException>(() => { using var denied = new FileStream(f.Interrupted.JournalPath, FileMode.Open, FileAccess.Write, FileShare.Read); });
            JournalRecovery.Close(f.Interrupted, "owned locked journal");
        }
        else
        {
            // POSIX sharing is advisory and this open can permit a successful append. Exercise an actual
            // ordinary-user write refusal instead; restore the owned fixture's original mode afterwards.
            failure = "POSIX write permission refusal";
            var mode = File.GetUnixFileMode(f.Interrupted.JournalPath);
            File.SetUnixFileMode(f.Interrupted.JournalPath, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() => { using var denied = new FileStream(f.Interrupted.JournalPath, FileMode.Open, FileAccess.Write, FileShare.Read); });
                JournalRecovery.Close(f.Interrupted, "owned read-only journal");
            }
            finally { File.SetUnixFileMode(f.Interrupted.JournalPath, mode); }
        }
        bool retained = File.Exists(manifest); Emit(f, "close-manifest", new { Retained = retained, Ended = f.Ended, Failure = failure, AppendRefusalVerified = true });
        Assert.True(retained); Assert.False(f.Ended); Assert.Equal("owned source manifest", File.ReadAllText(manifest));
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("INTERRUPTED_DEMAND " + JsonSerializer.Serialize(new { control, detail, Calls = f.Provider.Calls.ToArray(), OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false }));
    private static void Workers(Fixture f) { Assert.NotEmpty(f.Provider.Calls); Assert.All(f.Provider.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); }); }
    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Interrupted demand checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window; public required string Root, Staged, SourceFolder, Destination; public required InterruptedJob Interrupted; public required Provider Provider;
        public static async Task<Fixture> Create(string? held = null, string outcome = "ordinary")
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
            var src = Directory.CreateDirectory(Path.Join(root, "source")).FullName; var dst = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            var path = Path.Join(src, "source.txt"); File.WriteAllText(path, "source bytes");
            var prior = services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(path, EntryKind.File)], Destination = Location.FileSystem(dst) });
            await Wait(() => prior.State.IsFinished()); Assert.Equal(JobState.Completed, prior.State);
            var journal = Assert.Single(Directory.GetFiles(services.Paths.JournalDirectory, "job-*.fcj"));
            File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
            var interrupted = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
            var staged = Path.Join(dst, JournalRecovery.StagedPrefix + Path.GetFileNameWithoutExtension(journal)[^8..] + "-owned.tmp"); File.WriteAllText(staged, "stage");
            interrupted = interrupted with { StagingDirectories = [dst], OpenIntents = [new PendingIntent(1, "copy", path, Path.Join(dst, "source.txt"), staged)] };
            var provider = new Provider(services.Providers.Get(Schemes.FileSystem), staged, held, outcome); services.Providers.Register(provider);
            return new Fixture { Services = services, Vm = vm, Window = window, Root = root, SourceFolder = src, Destination = dst, Staged = staged, Interrupted = interrupted, Provider = provider };
        }
        public bool Ended => !JournalRecovery.Scan(Services.Paths.JournalDirectory).Any(j => j.JournalPath == Interrupted.JournalPath);
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public bool HasDialog => Window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("backdrop"));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public void Escape() => Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        public void End(string end) { if (end == "shutdown") Services.Io.Dispose(); else Window.Close(); }
        public void Cleanup()
        {
            var item = new InterruptedJobViewModel(Interrupted); Vm.Operations.Interrupted.Add(item); Vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs();
            var view = Assert.Single(Window.GetVisualDescendants().OfType<OperationsView>());
            typeof(OperationsView).GetMethod("OnCleanupInterrupted", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [new Button { Tag = item }, new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)]);
        }
        public async Task Drain(params Task[] tasks)
        {
            var clock = Stopwatch.StartNew(); while (tasks.Any(t => !t.IsCompleted)) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10)); if (Button("OK") is not null) Click("OK"); else if (HasDialog) Escape(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            await Task.WhenAll(tasks); await Wait(() => !Services.Jobs.HasActiveWork);
        }
        public void Dispose() { Provider.Release.Set(); AccessibilityTests.Close(Services, Window, Root); Provider.Release.Dispose(); }
    }
    private sealed class Provider(ResourceProvider inner, string path, string? held, string outcome) : ResourceProvider
    {
        public readonly ManualResetEventSlim Release = new(held is null); public readonly ConcurrentQueue<Call> Calls = new(); private int _active; public int Active => Volatile.Read(ref _active); public int Disposals; public bool DisposedDuringRead;
        public override string Scheme => Schemes.FileSystem; public override string GetDeviceKey(Location l) => "owned-interrupted-demand";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l); public override Location? GetParent(Location l) => inner.GetParent(l); public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e); public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l); public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        private void Invoke(string op) { Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (op != held) return; Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(12))) throw new IOException("Owned interruption hold expired."); } finally { Interlocked.Decrement(ref _active); } }
        public override IContentSource? OpenContent(ItemRef item)
        {
            if (item.FileSystemPath != path) return inner.OpenContent(item); Invoke("open"); if (outcome == "io") throw new IOException("Owned stage failure"); if (outcome == "access") throw new UnauthorizedAccessException("Owned stage denied"); return new Source(inner.OpenContent(item)!, this, outcome);
        }
        private sealed class Source(IContentSource inner, Provider owner, string outcome) : IContentSource, IPartialContent
        {
            private bool _reading; private int _reads;
            public string DisplayName => inner.DisplayName; public long Length => outcome == "unknown" ? -1 : outcome == "oversize" ? 64L * 1024 * 1024 + 1 : inner.Length; public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath;
            public ContentRevision? GetRevision() { var r = inner.GetRevision(); return outcome == "revision" && _reads > 0 && r is { } value ? value with { ModifiedTicks = value.ModifiedTicks + 1 } : r; }
            public IReadOnlyList<(long Offset, long Length)> MissingRanges => outcome == "partial" ? [(0, 1)] : []; public string? Caveat => outcome == "caveat" ? "Owned partial stage" : null;
            public int Read(long offset, Span<byte> buffer) { _reading = true; try { owner.Invoke("read"); _reads++; return outcome == "negative" ? -1 : outcome == "invalid" ? buffer.Length + 1 : outcome == "short" ? 0 : outcome == "growth" && offset == inner.Length ? 1 : inner.Read(offset, buffer); } finally { _reading = false; } }
            public void Dispose() { owner.DisposedDuringRead |= _reading; owner.Disposals++; inner.Dispose(); }
        }
    }
}
