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

public sealed class InterruptedSourceContentTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("confirmation", "copy", "same-bytes")]
    [InlineData("confirmation", "copy", "tree-bytes")]
    [InlineData("confirmation", "copy", "tree-added")]
    [InlineData("confirmation", "copy", "tree-removed")]
    [InlineData("confirmation", "copy", "unchanged")]
    [InlineData("confirmation", "copy", "tree-unchanged")]
    [InlineData("confirmation", "move", "same-bytes")]
    [InlineData("confirmation", "move", "tree-bytes")]
    [InlineData("confirmation", "move", "tree-added")]
    [InlineData("confirmation", "move", "tree-removed")]
    [InlineData("confirmation", "move", "unchanged")]
    [InlineData("confirmation", "move", "tree-unchanged")]
    [InlineData("alert", "copy", "same-bytes")]
    [InlineData("alert", "copy", "tree-bytes")]
    [InlineData("alert", "copy", "tree-added")]
    [InlineData("alert", "copy", "tree-removed")]
    [InlineData("alert", "copy", "unchanged")]
    [InlineData("alert", "copy", "tree-unchanged")]
    [InlineData("alert", "move", "same-bytes")]
    [InlineData("alert", "move", "tree-bytes")]
    [InlineData("alert", "move", "tree-added")]
    [InlineData("alert", "move", "tree-removed")]
    [InlineData("alert", "move", "unchanged")]
    [InlineData("alert", "move", "tree-unchanged")]
    [InlineData("queued-job", "copy", "same-bytes")]
    [InlineData("queued-job", "copy", "tree-bytes")]
    [InlineData("queued-job", "copy", "tree-added")]
    [InlineData("queued-job", "copy", "tree-removed")]
    [InlineData("queued-job", "copy", "unchanged")]
    [InlineData("queued-job", "copy", "tree-unchanged")]
    [InlineData("queued-job", "move", "same-bytes")]
    [InlineData("queued-job", "move", "tree-bytes")]
    [InlineData("queued-job", "move", "tree-added")]
    [InlineData("queued-job", "move", "tree-removed")]
    [InlineData("queued-job", "move", "unchanged")]
    [InlineData("queued-job", "move", "tree-unchanged")]
    public async Task Continuation_uses_only_the_reviewed_source_bytes_and_descendants(string boundary, string kind, string change)
    {
        using var f = await Fixture.Create(change.StartsWith("tree-", StringComparison.Ordinal), change);
        if (boundary == "queued-job") f.Services.Jobs.MaxConcurrent = 0;
        var run = f.Start(kind); await Wait(() => f.HasDialog || run.IsCompleted); Assert.True(f.HasDialog);
        if (boundary == "alert")
        {
            File.WriteAllText(f.Staged, "new staged bytes"); f.Approve(kind);
            await Wait(() => f.Button("OK") is not null || run.IsCompleted); Assert.NotNull(f.Button("OK"));
            f.Change(change); f.Click("OK");
        }
        else if (boundary == "confirmation") { f.Change(change); f.Approve(kind); }
        else
        {
            f.Approve(kind); await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(await run); Assert.Equal(JobState.Queued, f.Services.Jobs.Jobs.Last().State);
            f.Change(change); f.Services.Jobs.MaxConcurrent = 4; f.Services.Jobs.Schedule();
        }
        await f.Drain(run);
        bool unchanged = change is "unchanged" or "tree-unchanged";
        var target = Path.Join(f.Destination, "item");
        bool targetExists = File.Exists(target) || Directory.Exists(target);
        string? targetBytes = File.Exists(target) ? File.ReadAllText(target) : File.Exists(Path.Join(target, "child.txt")) ? File.ReadAllText(Path.Join(target, "child.txt")) : null;
        Emit(f, "source-content-boundary", new { boundary, kind, change, Started = await run,
            StagedRetained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count,
            TargetExists = targetExists, TargetBytes = targetBytes,
            SourceExists = File.Exists(f.Source) || Directory.Exists(f.Source),
            Issues = f.Services.Jobs.Jobs.Last().Issues.Select(i => new { i.Message, i.Outcome }).ToArray() });
        if (boundary != "queued-job")
        {
            Assert.Equal(unchanged, await run); Assert.Equal(unchanged, f.Ended);
            Assert.Equal(unchanged ? 2 : 1, f.Services.Jobs.Jobs.Count);
            Assert.Equal(boundary == "alert" || !unchanged, File.Exists(f.Staged));
        }
        Assert.Equal(unchanged, targetExists);
        if (unchanged) Assert.Equal("source bytes", targetBytes);
        else Assert.True(File.Exists(f.Source) || Directory.Exists(f.Source));
        Workers(f);
    }

    [AvaloniaTheory]
    [InlineData("bytes")]
    [InlineData("items")]
    [InlineData("unavailable")]
    [InlineData("partial")]
    public async Task An_unreviewable_initial_source_keeps_its_partial_file_and_journal(string problem)
    {
        using var f = await Fixture.Create(problem == "items");
        if (problem == "bytes") { using var file = new FileStream(f.Source, FileMode.Open, FileAccess.Write); file.SetLength(JournalRecovery.StagedReviewByteLimit + 1); }
        else if (problem == "items")
            for (int i = 0; i < 999; i++) File.WriteAllText(Path.Join(f.Source, "extra-" + i), "owned");
        else if (problem == "unavailable") f.Files.RefuseContent = true;
        else f.Files.PartialContent = true;
        var run = f.Start(); await f.Drain(run);
        Emit(f, "initial-source-content-refusal", new { problem, Started = await run, StagedRetained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count });
        Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs); Workers(f);
    }

    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("shutdown")]
    public async Task An_active_source_content_read_stays_owned_after_demand_ends(string end)
    {
        using var f = await Fixture.Create(); var run = f.Start(); await Wait(() => f.HasDialog);
        f.Files.Held = "content"; f.Files.Release.Reset(); f.Approve();
        try
        {
            await Wait(() => f.Files.Active == 1 || run.IsCompleted); Assert.Equal(1, f.Files.Active);
            f.End(end); bool pending = !run.IsCompleted; int disposalsWhileHeld = f.Files.DisposalsDuringRead;
            f.Files.Release.Set(); await f.Drain(run);
            Emit(f, "active-source-content-end", new { end, pending, disposalsWhileHeld, Started = await run,
                StagedRetained = File.Exists(f.Staged), Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count, f.Files.DisposalsDuringRead });
            Assert.True(pending); Assert.Equal(0, disposalsWhileHeld); Assert.Equal(0, f.Files.DisposalsDuringRead);
            Assert.False(await run); Assert.True(File.Exists(f.Staged)); Assert.False(f.Ended); Assert.Single(f.Services.Jobs.Jobs); Workers(f);
        }
        finally { f.Files.Release.Set(); await f.Drain(run); }
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("INTERRUPTED_SOURCE_CONTENT " + JsonSerializer.Serialize(new { control, detail, Calls = f.Files.Calls.ToArray(), OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false, SyntheticIdentityAndResolvedPath = true, AtomicOrNativeRecursiveIdentityQualified = false }));
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
            services.Providers.Register(new Provider(services.Providers.Get(Schemes.FileSystem), source, files));
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
                case "same-bytes":
                {
                    var modified = File.GetLastWriteTimeUtc(Source); File.WriteAllText(Source, "other! bytes");
                    File.SetLastWriteTimeUtc(Source, modified); break;
                }
                case "tree-bytes":
                {
                    var child = Path.Join(Source, "child.txt"); var modified = File.GetLastWriteTimeUtc(child);
                    File.WriteAllText(child, "other! bytes"); File.SetLastWriteTimeUtc(child, modified); break;
                }
                case "tree-added":
                {
                    var modified = Directory.GetLastWriteTimeUtc(Source); File.WriteAllText(Path.Join(Source, "new.txt"), "new child");
                    Directory.SetLastWriteTimeUtc(Source, modified); break;
                }
                case "tree-removed":
                {
                    var modified = Directory.GetLastWriteTimeUtc(Source); File.Delete(Path.Join(Source, "child.txt"));
                    Directory.SetLastWriteTimeUtc(Source, modified); break;
                }
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
        public bool RefuseContent, PartialContent; public int DisposalsDuringRead;
        public void ReadContent(string path) => Invoke(path, "content");
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
    private sealed class Provider(ResourceProvider inner, string source, Files files) : ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem; public override string GetDeviceKey(Location l) => l.Path == source ? "owned-interrupted-source" : "owned-interrupted-other";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l); public override Location? GetParent(Location l) => inner.GetParent(l); public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e); public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l); public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        public override IContentSource? OpenContent(ItemRef item)
        {
            if (item.FileSystemPath == source && files.RefuseContent) throw new IOException("Owned source content unavailable");
            var content = inner.OpenContent(item);
            return content is null || item.FileSystemPath != source ? content : new Content(content, source, files);
        }
    }
    private sealed class Content(IContentSource inner, string source, Files files) : IContentSource, IPartialContent
    {
        public string DisplayName => inner.DisplayName; public long Length => inner.Length; public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath;
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => files.PartialContent ? [(0L, 1L)] : [];
        public string? Caveat => files.PartialContent ? "Owned unavailable source byte" : null;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) { files.ReadContent(source); return inner.Read(offset, buffer); }
        public void Dispose() { if (files.Active > 0) Interlocked.Increment(ref files.DisposalsDuringRead); inner.Dispose(); }
    }
}
