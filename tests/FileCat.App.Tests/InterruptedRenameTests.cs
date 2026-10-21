using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class InterruptedRenameTests(ITestOutputHelper output)
{
    private static string ReviewText(TextBlock block) => block.Inlines is { Count: > 0 } inlines
        ? string.Concat(inlines.OfType<Run>().Select(run => run.Text))
        : block.Text ?? string.Empty;

    [AvaloniaFact]
    public async Task Hide_for_now_keeps_the_journal_and_temporary_item_for_the_next_startup()
    {
        using var f = await Fixture.Create();
        f.Vm.Operations.LoadInterrupted([f.Interrupted]);
        f.Vm.Operations.IsOpen = true;
        f.Window.UpdateLayout();
        var hide = Assert.Single(f.Window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Hide for now");
        hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Empty(f.Vm.Operations.Interrupted);
        Assert.False(f.Ended);
        Assert.True(JournalRecovery.IsInterrupted(f.Interrupted));
        Assert.Equal("reviewed", File.ReadAllText(f.Via));
        f.Vm.Operations.LoadInterrupted(JournalRecovery.Scan(f.Services.Paths.JournalDirectory));
        Assert.Single(f.Vm.Operations.Interrupted);
    }

    [AvaloniaFact]
    public async Task Mark_reviewed_requires_deliberate_consent_and_retains_full_rename_evidence()
    {
        using var f = await Fixture.Create();
        f.Vm.Operations.LoadInterrupted([f.Interrupted]);
        f.Vm.Operations.IsOpen = true;
        f.Window.UpdateLayout();
        await AssertNamedWhenSettled(f.Window);
        ClickBanner("Mark reviewed…");
        await Wait(() => f.Button("Mark reviewed and stop reminders") is not null);
        f.Window.UpdateLayout();
        var summary = Assert.Single(f.Window.GetVisualDescendants().OfType<Border>(),
            border => border.Classes.Contains("reviewHighlights"));
        var keyPoints = string.Join("\n", summary.GetVisualDescendants()
            .OfType<SelectableTextBlock>().Select(ReviewText));
        Assert.Contains("File changes: none", keyPoints);
        Assert.Contains("Interrupted rename intents to inspect: 1", keyPoints);
        Assert.Contains("interrupted rename intent", string.Join("\n", f.Window.GetVisualDescendants().OfType<TextBlock>().Select(ReviewText)));
        Assert.False(await f.Vm.MarkInterruptedReviewedAsync(f.Interrupted)); // the first review owns this journal
        f.Click("Cancel");
        await Wait(() => !f.HasDialog);
        Assert.False(f.Ended);
        Assert.Empty(RecoveryReviewStore.Load(f.Services.Paths.JournalDirectory));
        Assert.Equal("reviewed", File.ReadAllText(f.Via));

        // Closing the overlay precedes the async handler's reservation release.
        // A real pointer cannot invoke the disabled action until it becomes enabled;
        // RaiseEvent bypasses that guard unless the test waits for it explicitly.
        await Wait(() => !f.Vm.Operations.Interrupted.Single().IsActionBusy &&
            f.Window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Mark reviewed…" && b.IsEnabled));
        ClickBanner("Mark reviewed…");
        await Wait(() => f.Button("Mark reviewed and stop reminders") is not null);
        f.Click("Mark reviewed and stop reminders");
        await Wait(() => f.Ended && f.Vm.Operations.Interrupted.Count == 0);
        var record = Assert.Single(RecoveryReviewStore.Load(f.Services.Paths.JournalDirectory));
        Assert.True(record.ClosureConfirmed);
        Assert.Contains(JsonSerializer.Serialize(f.Via), record.Report);
        Assert.Contains(JsonSerializer.Serialize(f.OriginalPath), record.Report);
        Assert.Contains(JsonSerializer.Serialize(f.Target), record.Report);
        Assert.Contains("Open step 1 (rename-via", record.Report, StringComparison.OrdinalIgnoreCase);
        Assert.Single(f.Vm.Operations.ReviewedRecovery);
        f.Window.UpdateLayout();
        await AssertNamedWhenSettled(f.Window);
        Assert.Equal("reviewed", File.ReadAllText(f.Via));
        Assert.False(File.Exists(f.Target));
        Assert.False(JournalRecovery.IsInterrupted(f.Interrupted));

        Assert.True(f.Vm.Operations.HasReviewedRecovery);

        void ClickBanner(string label)
        {
            var button = f.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == label);
            Assert.True(button.IsEnabled, "The recovery action must be enabled before native input can invoke it.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    [AvaloniaFact]
    public async Task Failed_diagnostic_storage_keeps_recovery_tracking_open()
    {
        using var f = await Fixture.Create();
        File.WriteAllText(RecoveryReviewStore.DirectoryOf(f.Services.Paths.JournalDirectory), "blocks the review directory");
        var run = f.Vm.MarkInterruptedReviewedAsync(f.Interrupted);
        await Wait(() => f.Button("Mark reviewed and stop reminders") is not null);
        f.Click("Mark reviewed and stop reminders");
        Assert.False(await run);
        Assert.True(JournalRecovery.IsInterrupted(f.Interrupted));
        Assert.Equal("reviewed", File.ReadAllText(f.Via));
        Assert.Empty(Directory.GetFiles(f.Services.Paths.JournalDirectory, "review-*.json", SearchOption.TopDirectoryOnly));
    }

    [AvaloniaFact]
    public async Task Recovery_report_copies_every_retained_path_without_closing_tracking()
    {
        using var f = await Fixture.Create();
        var report = Assert.IsType<ReportWindow>(await f.Vm.ShowInterruptedReportAsync(f.Interrupted));
        try
        {
            await Wait(() => report.Reading is not null);
            await report.Reading!;
            Assert.Contains(JsonSerializer.Serialize(f.Via), report.Text);
            Assert.Contains(JsonSerializer.Serialize(f.OriginalPath), report.Text);
            Assert.Contains(JsonSerializer.Serialize(f.Target), report.Text);
            report.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Copy all")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => report.StatusText == "The whole report is on the clipboard.");
            Assert.Equal(report.Text, await ClipboardExtensions.TryGetTextAsync(report.Clipboard!));
            Assert.True(JournalRecovery.IsInterrupted(f.Interrupted));
            Assert.Equal("reviewed", File.ReadAllText(f.Via));
        }
        finally { report.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("unchanged")]
    [InlineData("cancel")]
    [InlineData("removed")]
    [InlineData("renamed")]
    [InlineData("size")]
    [InlineData("time")]
    [InlineData("same-bytes")]
    [InlineData("replaced")]
    [InlineData("file-to-directory")]
    [InlineData("directory-to-file")]
    [InlineData("directory-bytes")]
    [InlineData("directory-added")]
    [InlineData("directory-removed")]
    [InlineData("identity")]
    [InlineData("final-path")]
    [InlineData("info-io")]
    [InlineData("info-access")]
    [InlineData("missing-info")]
    [InlineData("link")]
    [InlineData("target-taken")]
    [InlineData("both-taken")]
    public async Task Cleanup_moves_only_the_temporary_item_that_was_reviewed(string change)
    {
        using var f = await Fixture.Create(change.StartsWith("directory", StringComparison.Ordinal), change);
        var run = f.Vm.CleanupInterruptedAsync(f.Interrupted); await Wait(() => f.HasDialog || run.IsCompleted); Assert.True(f.HasDialog);
        f.Change(change); if (change == "cancel") f.Escape(); else f.Click("Finish renaming"); await f.Drain(run);
        bool accepted = change is "unchanged" or "target-taken";
        Emit(f, "confirmation-rename", new { change, Completed = await run, f.Ended, Via = Exists(f.Via), Original = Exists(f.OriginalPath), Target = Exists(f.Target), ViaBytes = Bytes(f.Via), TargetBytes = Bytes(f.Target), OriginalBytes = Bytes(f.OriginalPath) });
        Assert.Equal(accepted, await run); Assert.Equal(accepted, f.Ended);
        if (accepted) Assert.Equal("reviewed", File.ReadAllText(change == "target-taken" ? f.OriginalPath : f.Target));
        else if (change is not ("removed" or "renamed")) Assert.True(Exists(f.Via));
        if (!accepted && change != "both-taken") Assert.False(Exists(f.Target));
        if (change == "target-taken") Assert.Equal("stranger", File.ReadAllText(f.Target));
        if (change == "both-taken") { Assert.Equal("stranger", File.ReadAllText(f.Target)); Assert.Equal("original occupant", File.ReadAllText(f.OriginalPath)); }
        Assert.NotEmpty(f.Files.Calls); Assert.All(f.Files.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); });
    }

    [AvaloniaTheory]
    [InlineData("info", "close")]
    [InlineData("identity", "close")]
    [InlineData("final-path", "close")]
    [InlineData("info", "shutdown")]
    [InlineData("identity", "shutdown")]
    [InlineData("final-path", "shutdown")]
    public async Task An_active_rename_review_stays_owned_until_the_native_call_returns(string held, string end)
    {
        using var f = await Fixture.Create(); var run = f.Vm.CleanupInterruptedAsync(f.Interrupted); await Wait(() => f.HasDialog);
        f.Files.Held = held; f.Files.Release.Reset(); f.Click("Finish renaming");
        try
        {
            await Wait(() => f.Files.Active > 0 || run.IsCompleted); Assert.Equal(1, f.Files.Active);
            f.End(end); bool pending = !run.IsCompleted; f.Files.Release.Set(); await f.Drain(run);
            Emit(f, "active-rename-end", new { held, end, pending, Completed = await run, f.Ended, Via = Exists(f.Via), Target = Exists(f.Target) });
            Assert.True(pending); Assert.False(await run); Assert.False(f.Ended); Assert.Equal("reviewed", File.ReadAllText(f.Via)); Assert.False(Exists(f.Target)); Assert.DoesNotContain(f.Files.Calls, c => c.Operation == "move");
        }
        finally { f.Files.Release.Set(); await f.Drain(run); }
    }

    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("shutdown")]
    public async Task A_queued_rename_revalidation_cannot_enter_after_demand_ends(string end)
    {
        using var f = await Fixture.Create(); var run = f.Vm.CleanupInterruptedAsync(f.Interrupted); await Wait(() => f.HasDialog);
        // Keep both blockers owned until the explicit release; active native calls are tested separately.
        ((Timer)typeof(FileCat.Core.Threading.DeviceIoScheduler).GetField("_watchdog", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Services.Io)!).Change(Timeout.Infinite, Timeout.Infinite);
        using var gate = new ManualResetEventSlim(); int occupied = 0;
        var first = f.Services.Io.Run("owned-interrupted-rename", FileCat.Core.Threading.IoPriority.Normal, _ => { Interlocked.Increment(ref occupied); gate.Wait(); return 1; });
        var second = f.Services.Io.Run("owned-interrupted-rename", FileCat.Core.Threading.IoPriority.Normal, _ => { Interlocked.Increment(ref occupied); gate.Wait(); return 2; });
        try
        {
            await Wait(() => occupied == 2);
            f.Click("Finish renaming"); Assert.False(run.IsCompleted); f.End(end); int atEnd = f.Files.Calls.Count;
            Assert.DoesNotContain(f.Files.Calls, c => c.Operation == "move");
            if (end == "close") Assert.False(f.Window.IsVisible); else Assert.True(f.Services.Io.IsStopped);
            // The rename remains queued until stop. No review or move may enter after this boundary.
            bool viaAtEnd = Exists(f.Via), targetAtEnd = Exists(f.Target); var bytesAtEnd = Bytes(viaAtEnd ? f.Via : f.Target);
            gate.Set(); await Task.WhenAll(first, second); await f.Drain(run);
            Emit(f, "queued-rename-end", new { end, Before = atEnd, After = f.Files.Calls.Count, Completed = await run, f.Ended, ViaAtEnd = viaAtEnd, TargetAtEnd = targetAtEnd, BytesAtEnd = bytesAtEnd, Via = Exists(f.Via), Target = Exists(f.Target), FinalBytes = Bytes(viaAtEnd ? f.Via : f.Target) });
            Assert.Equal(atEnd, f.Files.Calls.Count); Assert.False(await run); Assert.False(f.Ended); Assert.Equal(viaAtEnd, Exists(f.Via)); Assert.Equal(targetAtEnd, Exists(f.Target)); Assert.Equal("reviewed", bytesAtEnd); Assert.Equal(bytesAtEnd, Bytes(viaAtEnd ? f.Via : f.Target));
        }
        finally { gate.Set(); await Task.WhenAll(first, second); await f.Drain(run); }
    }

    [AvaloniaTheory]
    [InlineData("info", "close")]
    [InlineData("info", "shutdown")]
    [InlineData("move", "close")]
    [InlineData("move", "shutdown")]
    public async Task An_entered_native_move_has_a_different_stop_boundary_from_queued_review(string held, string end)
    {
        using var f = await Fixture.Create(); var run = f.Vm.CleanupInterruptedAsync(f.Interrupted); await Wait(() => f.HasDialog);
        f.Files.Held = held; f.Files.Release.Reset(); f.Click("Finish renaming");
        try
        {
            await Wait(() => f.Files.Active > 0 || run.IsCompleted); Assert.Equal(1, f.Files.Active);
            bool pendingBeforeStop = !run.IsCompleted; f.End(end); int atEnd = f.Files.Calls.Count;
            bool pendingAfterStop = !run.IsCompleted, viaAtEnd = Exists(f.Via), targetAtEnd = Exists(f.Target);
            int activeAtStop = f.Files.Active; string? bytesAtEnd = Bytes(f.Via);
            f.Files.Release.Set(); await f.Drain(run);
            Emit(f, "entered-native-stop", new { held, end, pendingBeforeStop, pendingAfterStop, ActiveAtStop = activeAtStop, Before = atEnd, After = f.Files.Calls.Count, Completed = await run, f.Ended, ViaAtEnd = viaAtEnd, TargetAtEnd = targetAtEnd, BytesAtEnd = bytesAtEnd, Via = Exists(f.Via), Target = Exists(f.Target), ViaBytes = Bytes(f.Via), TargetBytes = Bytes(f.Target) });
            Assert.True(pendingBeforeStop); Assert.True(pendingAfterStop); Assert.Equal(1, activeAtStop);
            Assert.True(viaAtEnd); Assert.False(targetAtEnd); Assert.Equal("reviewed", bytesAtEnd);
            Assert.Equal(atEnd, f.Files.Calls.Count); Assert.False(await run); Assert.False(f.Ended);
            // A native Move already entered before stop owns its call until return; stop cannot undo that call.
            // A held review has not entered Move and must still refuse it after stop.
            if (held == "move")
            { Assert.False(Exists(f.Via)); Assert.True(Exists(f.Target)); Assert.Equal("reviewed", File.ReadAllText(f.Target)); Assert.Single(f.Files.Calls, c => c.Operation == "move"); }
            else
            { Assert.True(Exists(f.Via)); Assert.False(Exists(f.Target)); Assert.Equal("reviewed", File.ReadAllText(f.Via)); Assert.DoesNotContain(f.Files.Calls, c => c.Operation == "move"); }
        }
        finally { f.Files.Release.Set(); await f.Drain(run); }
    }

    [AvaloniaFact]
    public async Task A_successful_rename_does_not_hide_another_unreviewed_temporary_item()
    {
        using var f = await Fixture.Create(); var other = Path.Join(Path.GetDirectoryName(f.Via)!, "other.tmp");
        var intent = new PendingIntent(2, JobJournal.RenameViaOp, other + "-original", other + "-target", null) { Via = other };
        var run = f.Vm.CleanupInterruptedAsync(f.Interrupted with { OpenIntents = [.. f.Interrupted.OpenIntents, intent] }); await Wait(() => f.HasDialog);
        var reviewText = string.Join("\n", f.Window.GetVisualDescendants().OfType<TextBlock>().Select(ReviewText));
        Assert.Contains("Recorded rename paths needing manual review: 1", reviewText);
        Assert.Contains("An item may be absent or inaccessible", reviewText);
        Assert.Contains(other, reviewText);
        Assert.DoesNotContain("temporary items could not be completely reviewed and will be kept", reviewText);
        f.Click("Finish renaming"); await f.Drain(run);
        Emit(f, "mixed-rename-review", new { Completed = await run, f.Ended, TargetBytes = File.ReadAllText(f.Target), MissingTemporary = !Exists(other) });
        Assert.False(await run); Assert.False(f.Ended); Assert.Equal("reviewed", File.ReadAllText(f.Target));
        var record = Assert.Single(RecoveryReviewStore.Load(f.Services.Paths.JournalDirectory));
        Assert.False(record.ClosureConfirmed);
        Assert.Contains(JsonSerializer.Serialize(other), record.Report);
        Assert.Contains("Recorded temporary rename path not verified or moved", record.Report);
        Assert.Contains("unresolved rename remains", record.Report);
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("INTERRUPTED_RENAME " + JsonSerializer.Serialize(new { control, detail, Calls = f.Files.Calls.ToArray(), OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false, SyntheticIdentityAndResolvedPath = true, AtomicRenameIdentityQualified = false }));
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static string? Bytes(string path) => File.Exists(path) ? File.ReadAllText(path) : Directory.Exists(path) ? string.Join(";", Directory.GetFiles(path).Order().Select(p => Path.GetFileName(p) + "=" + File.ReadAllText(p))) : null;
    private static async Task Wait(Func<bool> done)
    {
        // ARM64 CI can take longer to persist and retire the reviewed journal.
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "Owned rename checkpoint timed out.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task AssertNamedWhenSettled(Window window)
    {
        // The expanded interrupted-review buttons get their localized content from bindings at
        // Loaded priority. A full-suite render can reach this assertion before those bindings run.
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            window.UpdateLayout();
            if (AccessibilityTests.Unnamed(window).Count == 0) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Empty(AccessibilityTests.Unnamed(window));
    }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window; public required string Root, Via, OriginalPath, Target; public required InterruptedJob Interrupted; public required Files Files; public required IFileSystemOperations Original;
        private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        public static async Task<Fixture> Create(bool directory = false, string outcome = "ordinary")
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(); await (Task)typeof(MainWindow).GetField("_interruptedStartup", Fields)!.GetValue(window)!;
            var folder = Directory.CreateDirectory(Path.Join(root, "rename")).FullName; var via = Path.Join(folder, "owned.tmp"); var originalPath = Path.Join(folder, "original"); var target = Path.Join(folder, "target");
            var seed = Path.Join(root, "seed"); File.WriteAllText(seed, "seed");
            var prior = services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(seed, EntryKind.File)], Destination = Location.FileSystem(folder) }); await Wait(() => prior.State.IsFinished()); Assert.Equal(JobState.Completed, prior.State);
            var journal = Assert.Single(Directory.GetFiles(services.Paths.JournalDirectory, "job-*.fcj")); File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
            var interrupted = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory)) with { StagingDirectories = [], OpenIntents = [new PendingIntent(1, JobJournal.RenameViaOp, originalPath, target, null) { Via = via }] };
            if (directory) { Directory.CreateDirectory(via); File.WriteAllText(Path.Join(via, "child"), "reviewed"); } else File.WriteAllText(via, "reviewed");
            var original = services.Jobs.FileOperations; var files = new Files(via, original, outcome); typeof(JobManager).GetField("_fs", Fields)!.SetValue(services.Jobs, files);
            services.Providers.Register(new Provider(services.Providers.Get(Schemes.FileSystem)));
            return new Fixture { Services = services, Vm = vm, Window = window, Root = root, Via = via, OriginalPath = originalPath, Target = target, Interrupted = interrupted, Files = files, Original = original };
        }
        public bool Ended => !JournalRecovery.Scan(Services.Paths.JournalDirectory).Any(j => j.JournalPath == Interrupted.JournalPath);
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public bool HasDialog => Window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("backdrop"));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public void Escape() => Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        public void End(string end) { if (end == "shutdown") Services.Io.Dispose(); else Window.Close(); }
        public void Change(string change)
        {
            Files.Changed = true; var modified = File.GetLastWriteTimeUtc(Via); var created = File.GetCreationTimeUtc(Via);
            switch (change)
            {
                case "removed": File.Delete(Via); break;
                case "renamed": File.Move(Via, Via + "-elsewhere"); break;
                case "size": File.AppendAllText(Via, " newer"); break;
                case "time": File.SetLastWriteTimeUtc(Via, modified.AddMinutes(2)); break;
                case "same-bytes": File.WriteAllText(Via, "changed!"); File.SetLastWriteTimeUtc(Via, modified); break;
                case "replaced": File.Move(Via, Via + "-reviewed"); File.WriteAllText(Via, "changed!"); File.SetLastWriteTimeUtc(Via, modified); File.SetCreationTimeUtc(Via, created); break;
                case "file-to-directory": File.Delete(Via); Directory.CreateDirectory(Via); File.WriteAllText(Path.Join(Via, "stranger"), "replacement"); break;
                case "directory-to-file": Directory.Delete(Via, true); File.WriteAllText(Via, "replacement"); break;
                case "directory-bytes": var child = Path.Join(Via, "child"); var time = File.GetLastWriteTimeUtc(child); File.WriteAllText(child, "changed!"); File.SetLastWriteTimeUtc(child, time); break;
                case "directory-added": File.WriteAllText(Path.Join(Via, "extra"), "unreviewed"); Directory.SetLastWriteTimeUtc(Via, modified); break;
                case "directory-removed": File.Delete(Path.Join(Via, "child")); Directory.SetLastWriteTimeUtc(Via, modified); break;
                case "target-taken": File.WriteAllText(Target, "stranger"); break;
                case "both-taken": File.WriteAllText(Target, "stranger"); File.WriteAllText(OriginalPath, "original occupant"); break;
            }
        }
        public async Task Drain(params Task[] tasks)
        {
            var clock = Stopwatch.StartNew(); while (tasks.Any(t => !t.IsCompleted)) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10)); if (Button("OK") is not null) Click("OK"); else if (HasDialog) Escape(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            await Task.WhenAll(tasks); await Wait(() => !Services.Jobs.HasActiveWork);
        }
        public void Dispose() { Files.Release.Set(); typeof(JobManager).GetField("_fs", Fields)!.SetValue(Services.Jobs, Original); AccessibilityTests.Close(Services, Window, Root); Files.Release.Dispose(); }
    }
    private sealed class Files(string via, IFileSystemOperations inner, string outcome) : PortableFileOperations
    {
        public readonly ConcurrentQueue<Call> Calls = new(); public bool Changed; public string? Held; public readonly ManualResetEventSlim Release = new(true); private int _active; public int Active => Volatile.Read(ref _active);
        private void Invoke(string path, string op) { if (path != via) return; Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (op != Held) return; Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(12))) throw new IOException("Owned rename hold expired."); } finally { Interlocked.Decrement(ref _active); } }
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            Invoke(path, "info"); if (path != via) return inner.TryGetInfo(path);
            if (Changed && outcome == "info-io") throw new IOException("Owned rename unavailable."); if (Changed && outcome == "info-access") throw new UnauthorizedAccessException("Owned rename denied."); if (Changed && outcome == "missing-info") return null;
            var info = inner.TryGetInfo(path); return Changed && outcome == "link" && info is not null ? info with { IsLink = true, LinkTarget = "owned-other" } : info;
        }
        public override string? GetFileIdentity(string path) { Invoke(path, "identity"); return path == via ? outcome == "identity" && Changed ? "owned-replacement-id" : "owned-reviewed-id" : inner.GetFileIdentity(path); }
        public override string? GetFinalPath(string path) { Invoke(path, "final-path"); return path == via ? outcome == "final-path" && Changed ? via + "-other" : via : inner.GetFinalPath(path); }
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false) { Invoke(source, "move"); inner.Move(source, destination, replaceExisting, writeThrough); }
    }
    private sealed class Provider(ResourceProvider inner) : ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem; public override string GetDeviceKey(Location l) => "owned-interrupted-rename";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l); public override Location? GetParent(Location l) => inner.GetParent(l); public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e); public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l); public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct); public override IContentSource? OpenContent(ItemRef item) => inner.OpenContent(item);
    }
}
