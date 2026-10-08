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
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class InterruptedSourceTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("removed", "copy")]
    [InlineData("renamed", "copy")]
    [InlineData("size", "copy")]
    [InlineData("time", "copy")]
    [InlineData("file-to-directory", "copy")]
    [InlineData("directory-to-file", "copy")]
    [InlineData("created", "copy")]
    [InlineData("identity", "copy")]
    [InlineData("final-path", "copy")]
    [InlineData("link-target", "copy")]
    [InlineData("missing-info", "copy")]
    [InlineData("info-io", "copy")]
    [InlineData("info-access", "copy")]
    [InlineData("identity-io", "copy")]
    [InlineData("final-path-access", "copy")]
    [InlineData("unchanged", "copy")]
    [InlineData("cancel", "copy")]
    [InlineData("removed", "move")]
    [InlineData("renamed", "move")]
    [InlineData("size", "move")]
    [InlineData("time", "move")]
    [InlineData("file-to-directory", "move")]
    [InlineData("directory-to-file", "move")]
    [InlineData("created", "move")]
    [InlineData("identity", "move")]
    [InlineData("final-path", "move")]
    [InlineData("link-target", "move")]
    [InlineData("missing-info", "move")]
    [InlineData("info-io", "move")]
    [InlineData("info-access", "move")]
    [InlineData("identity-io", "move")]
    [InlineData("final-path-access", "move")]
    [InlineData("unchanged", "move")]
    [InlineData("cancel", "move")]
    public async Task Changed_or_unavailable_sources_are_rejected_before_partial_cleanup(string change, string kind)
    {
        using var f = await Fixture.Create(change == "directory-to-file", change);
        var run = f.Start(kind); await Wait(() => f.HasDialog || run.IsCompleted); Assert.True(f.HasDialog);
        f.Change(change); if (change == "cancel") f.Escape(); else f.Approve(kind); await f.Drain(run);
        bool accepted = change == "unchanged"; Emit(f, "confirmation-source", new { change, kind, Started = await run, Retained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.Equal(accepted, await run); Assert.Equal(!accepted, File.Exists(f.Staged)); Assert.Equal(accepted, f.Ended); Assert.Equal(accepted ? 2 : 1, f.Services.Jobs.Jobs.Count);
        if (change != "cancel") Workers(f);
    }

    [AvaloniaTheory]
    [InlineData("removed", "copy")]
    [InlineData("identity", "copy")]
    [InlineData("final-path", "copy")]
    [InlineData("unchanged", "copy")]
    [InlineData("removed", "move")]
    [InlineData("identity", "move")]
    [InlineData("final-path", "move")]
    [InlineData("unchanged", "move")]
    public async Task Sources_are_rechecked_after_the_kept_file_alert(string change, string kind)
    {
        using var f = await Fixture.Create(outcome: change); var run = f.Start(kind); await Wait(() => f.HasDialog);
        File.WriteAllText(f.Staged, "new staged bytes"); f.Approve(kind); await Wait(() => f.Button("OK") is not null || run.IsCompleted); Assert.NotNull(f.Button("OK"));
        f.Change(change); f.Click("OK"); await f.Drain(run);
        Emit(f, "kept-alert-source", new { change, kind, Started = await run, Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.Equal(change == "unchanged", await run); Assert.Equal(change == "unchanged", f.Ended); Assert.Equal(change == "unchanged" ? 2 : 1, f.Services.Jobs.Jobs.Count); Assert.Equal("new staged bytes", File.ReadAllText(f.Staged)); Workers(f);
    }

    [AvaloniaTheory]
    [InlineData("info", "shutdown")]
    [InlineData("identity", "shutdown")]
    [InlineData("final-path", "shutdown")]
    [InlineData("info", "close")]
    [InlineData("identity", "close")]
    [InlineData("final-path", "close")]
    public async Task Active_source_revalidation_remains_owned_until_the_native_call_returns(string held, string end)
    {
        using var f = await Fixture.Create(); var run = f.Start(); await Wait(() => f.HasDialog); f.Files.Held = held; f.Files.Release.Reset(); f.Approve();
        try
        {
            await Wait(() => f.Files.Active > 0 || run.IsCompleted || f.HasDialog); Assert.Equal(1, f.Files.Active);
            f.End(end); bool pending = !run.IsCompleted; f.Files.Release.Set(); await f.Drain(run);
            Emit(f, "active-source-end", new { held, end, pending, Started = await run, Retained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
            Assert.True(pending); Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs); Workers(f);
        }
        finally { f.Files.Release.Set(); await f.Drain(run); }
    }

    [AvaloniaTheory]
    [InlineData("shutdown")]
    [InlineData("close")]
    public async Task A_source_revalidation_queued_behind_both_device_workers_never_enters_after_demand_ends(string end)
    {
        using var f = await Fixture.Create(); var run = f.Start(); await Wait(() => f.HasDialog);
        using var gate = new ManualResetEventSlim(); int occupied = 0;
        var first = f.Services.Io.Run("owned-interrupted-source", FileCat.Core.Threading.IoPriority.Normal, _ => { Interlocked.Increment(ref occupied); gate.Wait(TimeSpan.FromSeconds(12)); return 1; });
        var second = f.Services.Io.Run("owned-interrupted-source", FileCat.Core.Threading.IoPriority.Normal, _ => { Interlocked.Increment(ref occupied); gate.Wait(TimeSpan.FromSeconds(12)); return 2; });
        try
        {
            await Wait(() => occupied == 2); int calls = f.Files.Calls.Count; f.Approve();
            var checkpoint = f.Services.Io.Run("owned-interrupted-source", FileCat.Core.Threading.IoPriority.Normal, _ => true);
            Assert.False(checkpoint.IsCompleted); Assert.False(run.IsCompleted);
            f.End(end); int callsAtEnd = f.Files.Calls.Count;
            if (end == "close") Assert.False(f.Window.IsVisible); else Assert.True(f.Services.Io.IsStopped);
            gate.Set(); await Task.WhenAll(first, second); await f.Drain(run);
            try { await checkpoint; } catch (OperationCanceledException) { }
            // Closing saves placement before OnClosed ends demand. A watchdog replacement can enter during that
            // interval. Observe the accepted close, then require no source call after that actual end boundary.
            Emit(f, "queued-source-end", new { end, CallsBeforeClose = calls, Before = callsAtEnd, After = f.Files.Calls.Count, Started = await run, Retained = File.Exists(f.Staged), Ended = f.Ended });
            Assert.Equal(callsAtEnd, f.Files.Calls.Count); Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs); Workers(f);
        }
        finally { gate.Set(); await Task.WhenAll(first, second); await f.Drain(run); }
    }

    [AvaloniaFact]
    public async Task A_source_missing_before_review_is_omitted_and_the_remaining_source_can_continue()
    {
        using var f = await Fixture.Create(); var missing = Path.Join(f.Root, "missing.txt");
        var job = f.Interrupted with { Sources = [missing, f.Source], SourceCount = 2 };
        var run = f.Vm.RunInterruptedAgainAsync(job); await Wait(() => f.HasDialog); f.Approve(); await f.Drain(run);
        Emit(f, "initially-missing", new { Started = await run, Count = f.Services.Jobs.Jobs.Last().Request.Sources.Count });
        Assert.True(await run); Assert.Single(f.Services.Jobs.Jobs.Last().Request.Sources); Workers(f);
    }

    [AvaloniaTheory]
    [InlineData("removed", "copy")]
    [InlineData("identity", "copy")]
    [InlineData("final-path", "copy")]
    [InlineData("unchanged", "copy")]
    [InlineData("removed", "move")]
    [InlineData("identity", "move")]
    [InlineData("final-path", "move")]
    [InlineData("unchanged", "move")]
    public async Task A_submitted_recovery_checks_its_review_again_when_the_queued_job_starts(string change, string kind)
    {
        using var f = await Fixture.Create(outcome: change); f.Services.Jobs.MaxConcurrent = 0;
        var run = f.Start(kind); await Wait(() => f.HasDialog); f.Approve(kind); await run.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(await run);
        var queued = f.Services.Jobs.Jobs.Last(); Assert.Equal(JobState.Queued, queued.State); f.Change(change); f.Services.Jobs.MaxConcurrent = 4; f.Services.Jobs.Schedule(); await Wait(() => queued.State.IsFinished());
        Emit(f, "queued-job-source", new { change, kind, queued.State, Issues = queued.Issues.Select(i => new { i.Message, i.Outcome }).ToArray(), TargetExists = File.Exists(Path.Join(f.Destination, "item")), SourceExists = File.Exists(f.Source) });
        Assert.Equal(change == "unchanged", File.Exists(Path.Join(f.Destination, "item")));
        if (change == "unchanged") Assert.Equal(JobState.Completed, queued.State); else Assert.Contains(queued.Issues, i => i.Outcome == StepOutcome.CanceledBeforeChange);
        Workers(f);
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("INTERRUPTED_SOURCE " + JsonSerializer.Serialize(new { control, detail, Calls = f.Files.Calls.ToArray(), OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false, SyntheticIdentityAndResolvedPath = true, ContentHashOrRecursiveIdentityQualified = false }));
    private static void Workers(Fixture f) { Assert.NotEmpty(f.Files.Calls); Assert.All(f.Files.Calls.Where(c => c.Thread.StartsWith("FileCat I/O ", StringComparison.Ordinal)), c => Assert.False(c.Ui)); Assert.Contains(f.Files.Calls, c => !c.Ui && c.Thread.StartsWith("FileCat I/O ", StringComparison.Ordinal)); }
    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned interrupted-source checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window; public required string Root, Source, Staged, Destination; public required InterruptedJob Interrupted; public required Files Files; public required IFileSystemOperations Original;
        private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        public static async Task<Fixture> Create(bool directory = false, string outcome = "ordinary")
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(); await (Task)typeof(MainWindow).GetField("_interruptedStartup", Fields)!.GetValue(window)!;
            var source = Path.Join(Directory.CreateDirectory(Path.Join(root, "source")).FullName, "item"); var destination = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            if (directory) { Directory.CreateDirectory(source); File.WriteAllText(Path.Join(source, "child.txt"), "source bytes"); } else File.WriteAllText(source, "source bytes");
            var prior = services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, directory ? EntryKind.Directory : EntryKind.File)], Destination = Location.FileSystem(destination) }); await Wait(() => prior.State.IsFinished()); Assert.Equal(JobState.Completed, prior.State);
            var arrived = Path.Join(destination, "item"); if (directory) Directory.Delete(arrived, true); else File.Delete(arrived);
            var journal = Assert.Single(Directory.GetFiles(services.Paths.JournalDirectory, "job-*.fcj")); File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
            var interrupted = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
            var staged = Path.Join(destination, JournalRecovery.StagedPrefix + Path.GetFileNameWithoutExtension(journal)[^8..] + "-owned.tmp"); File.WriteAllText(staged, "stage"); interrupted = interrupted with { StagingDirectories = [destination], OpenIntents = [new PendingIntent(1, "copy", source, arrived, staged)] };
            var original = services.Jobs.FileOperations; var files = new Files(source, original, outcome); typeof(JobManager).GetField("_fs", Fields)!.SetValue(services.Jobs, files);
            services.Providers.Register(new Provider(services.Providers.Get(Schemes.FileSystem), source));
            return new Fixture { Services = services, Vm = vm, Window = window, Root = root, Source = source, Destination = destination, Staged = staged, Interrupted = interrupted, Files = files, Original = original };
        }
        public Task<bool> Start(string kind = "copy") => Vm.RunInterruptedAgainAsync(Interrupted with { Kind = kind == "move" ? nameof(JobKind.Move) : nameof(JobKind.Copy) });
        public bool Ended => !JournalRecovery.Scan(Services.Paths.JournalDirectory).Any(j => j.JournalPath == Interrupted.JournalPath);
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public bool HasDialog => Window.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("backdrop"));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public void Approve(string kind = "copy") => Click(kind == "move" ? "Move the rest" : "Copy the rest");
        public void Escape() => Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        public void End(string end) { if (end == "shutdown") Services.Io.Dispose(); else Window.Close(); }
        public void Change(string change)
        {
            Files.Changed = true;
            switch (change)
            {
                case "removed": File.Delete(Source); break;
                case "renamed": File.Move(Source, Source + "-renamed"); break;
                case "size": File.AppendAllText(Source, " newer"); break;
                case "time": File.SetLastWriteTimeUtc(Source, File.GetLastWriteTimeUtc(Source).AddMinutes(2)); break;
                case "created": File.SetCreationTimeUtc(Source, File.GetCreationTimeUtc(Source).AddMinutes(2)); break;
                case "file-to-directory": File.Delete(Source); Directory.CreateDirectory(Source); break;
                case "directory-to-file": Directory.Delete(Source, true); File.WriteAllText(Source, "replacement"); break;
            }
        }
        public async Task Drain(params Task[] tasks)
        {
            var clock = Stopwatch.StartNew(); while (tasks.Any(t => !t.IsCompleted)) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10)); if (Button("OK") is not null) Click("OK"); else if (HasDialog) Escape(); await Task.Delay(10, TestContext.Current.CancellationToken); }
            await Task.WhenAll(tasks); await Wait(() => !Services.Jobs.HasActiveWork);
        }
        public void Dispose() { Files.Release.Set(); typeof(JobManager).GetField("_fs", Fields)!.SetValue(Services.Jobs, Original); AccessibilityTests.Close(Services, Window, Root); Files.Release.Dispose(); }
    }
    private sealed class Files(string source, IFileSystemOperations inner, string outcome) : PortableFileOperations
    {
        public readonly ConcurrentQueue<Call> Calls = new(); public readonly ManualResetEventSlim Release = new(true); public bool Changed; public string? Held; private int _active; public int Active => Volatile.Read(ref _active);
        private void Invoke(string path, string op) { if (path != source) return; Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (Held != op) return; Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(12))) throw new IOException("Owned source hold expired."); } finally { Interlocked.Decrement(ref _active); } }
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            Invoke(path, "info"); if (path != source) return inner.TryGetInfo(path);
            if (Changed && outcome == "info-io") throw new IOException("Owned source unavailable."); if (Changed && outcome == "info-access") throw new UnauthorizedAccessException("Owned source denied."); if (Changed && outcome == "missing-info") return null;
            var info = inner.TryGetInfo(path); if (outcome == "link-target" && info is not null) return info with { IsLink = true, LinkTarget = Changed ? "changed-target" : "reviewed-target" };
            return info;
        }
        public override string? GetFileIdentity(string path) { Invoke(path, "identity"); if (Changed && path == source && outcome == "identity-io") throw new IOException("Owned identity unavailable."); return path == source ? outcome == "identity" && Changed ? "owned-replacement-id" : "owned-reviewed-id" : inner.GetFileIdentity(path); }
        public override string? GetFinalPath(string path) { Invoke(path, "final-path"); if (Changed && path == source && outcome == "final-path-access") throw new UnauthorizedAccessException("Owned resolution denied."); return path == source ? outcome == "final-path" && Changed ? source + "-other" : source : inner.GetFinalPath(path); }
    }
    private sealed class Provider(ResourceProvider inner, string source) : ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem; public override string GetDeviceKey(Location l) => l.Path == source ? "owned-interrupted-source" : "owned-interrupted-other";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l); public override Location? GetParent(Location l) => inner.GetParent(l); public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e); public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l); public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct); public override IContentSource? OpenContent(ItemRef item) => inner.OpenContent(item);
    }
}
