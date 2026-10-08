using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class EditSaveCopyTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("shutdown", false)]
    [InlineData("close", false)]
    [InlineData("shutdown", true)]
    [InlineData("close", true)]
    public async Task Ended_demand_cannot_open_or_accept_a_save_picker(string end, bool before)
    {
        using var f = await Fixture.Create(); if (before) f.End(end);
        var task = f.Start(); if (!before) await Wait(() => f.PickerCalls > 0 || task.IsCompleted);
        f.End(end); string? notification = f.Vm.Notification; f.Answer(); await task.WaitAsync(TimeSpan.FromSeconds(10));
        Emit(f, "picker-ended", new { end, before, notification, after = f.Vm.Notification });
        Assert.Equal(before ? 0 : 1, f.PickerCalls); Assert.Equal("previous destination", File.ReadAllText(f.Target)); Assert.Equal(notification, f.Vm.Notification); f.AssertPersistent();
    }

    [AvaloniaTheory]
    [InlineData("picker")]
    [InlineData("read")]
    [InlineData("copy")]
    public async Task Repeated_save_copy_has_one_owner_until_the_picker_or_native_call_returns(string phase)
    {
        using var f = await Fixture.Create(phase == "picker" ? null : phase); Task? first = null, second = null;
        try
        {
            first = f.Start(); await Wait(() => f.PickerCalls > 0 || first.IsCompleted);
            if (phase != "picker") { f.Answer(); await Wait(() => f.Gate.Active > 0 || first.IsCompleted); }
            second = f.Start(); await Wait(() => second.IsCompleted || f.PickerCalls > 1);
            Emit(f, "coalesced", new { phase, secondCompleted = second.IsCompleted }); Assert.Equal(1, f.PickerCalls); Assert.True(second.IsCompleted);
            f.Answer(); f.Gate.Release.Set(); await first.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal("base", File.ReadAllText(f.Target)); f.AssertPersistent();
        }
        finally { f.Answer(); f.Gate.Release.Set(); await f.Drain(first, second); }
    }

    [AvaloniaTheory]
    [InlineData("open", "shutdown")]
    [InlineData("read", "shutdown")]
    [InlineData("open", "close")]
    [InlineData("read", "close")]
    [InlineData("copy", "shutdown")]
    [InlineData("copy", "close")]
    [InlineData("move", "shutdown")]
    [InlineData("move", "close")]
    public async Task Active_source_and_destination_calls_keep_their_owner_through_shutdown(string held, string end)
    {
        using var f = await Fixture.Create(held); Task? task = null;
        try
        {
            task = f.Start(); f.Answer(); await Wait(() => f.Gate.Active > 0 || task.IsCompleted);
            bool reached = f.Gate.Active > 0; f.End(end); string? notification = f.Vm.Notification;
            bool pending = !task.IsCompleted; int disposed = f.Provider.Sources.Sum(s => s.Disposals); string? snapshot = f.Files.Snapshot;
            bool snapshotHeld = snapshot is not null && File.Exists(snapshot);
            f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(10));
            Emit(f, "active-ended", new { held, end, reached, pending, disposed, snapshotHeld, notification, after = f.Vm.Notification });
            Assert.True(reached); Assert.True(pending); AssertWorkers(f);
            if (held is "open" or "read") Assert.Equal(0, disposed); else Assert.True(snapshotHeld);
            Assert.Equal(held == "move" ? "base" : "previous destination", File.ReadAllText(f.Target)); Assert.Equal(notification, f.Vm.Notification);
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); }); f.AssertPersistent(); f.AssertNoTemporaryCopies();
        }
        finally { f.Gate.Release.Set(); await f.Drain(task); }
    }

    [AvaloniaTheory]
    [InlineData("source", "shutdown")]
    [InlineData("source", "close")]
    [InlineData("target", "shutdown")]
    [InlineData("target", "close")]
    public async Task Queued_device_admission_cannot_start_after_demand_ends(string phase, string end)
    {
        using var f = await Fixture.Create(); using var release = new ManualResetEventSlim(false); using var entered = new CountdownEvent(2);
        string key = phase == "source" ? "owned-save-source" : "owned-save-target";
        var blockers = Enumerable.Range(0, 2).Select(_ => f.Services.Io.Run(key, IoPriority.Normal, _ => { entered.Signal(); Assert.True(release.Wait(TimeSpan.FromSeconds(12))); return true; })).ToArray(); Task? task = null;
        try
        {
            await Wait(() => entered.CurrentCount == 0); task = f.Start(); f.Answer();
            if (phase == "target") await Wait(() => f.Provider.Sources.Any(s => s.Disposals == 1) || task.IsCompleted);
            // A same-device checkpoint is queued behind the two occupied workers, proving that admission is held.
            var checkpoint = f.Services.Io.Run(key, IoPriority.Normal, _ => true); Assert.False(checkpoint.IsCompleted);
            f.End(end); string? notification = f.Vm.Notification; release.Set(); await Task.WhenAll(blockers); await task.WaitAsync(TimeSpan.FromSeconds(10));
            try { await checkpoint; } catch (OperationCanceledException) { }
            Emit(f, "queued-ended", new { phase, end, notification, after = f.Vm.Notification });
            Assert.Equal("previous destination", File.ReadAllText(f.Target)); Assert.Equal(notification, f.Vm.Notification);
            if (phase == "source") Assert.Empty(f.Provider.Sources); Assert.Null(f.Files.Snapshot); f.AssertPersistent(); f.AssertNoTemporaryCopies();
        }
        finally { release.Set(); await Task.WhenAll(blockers); await f.Drain(task); }
    }

    [AvaloniaTheory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("null")]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("negative")]
    [InlineData("short")]
    [InlineData("unknown")]
    [InlineData("oversize")]
    [InlineData("partial")]
    [InlineData("caveat")]
    [InlineData("grew")]
    [InlineData("revision")]
    public async Task Uncertain_or_incomplete_source_preserves_existing_destination_and_session(string outcome)
    {
        using var f = await Fixture.Create(outcome: outcome); var task = f.Start(); await Wait(() => f.PickerCalls > 0);
        if (outcome == "missing") File.Delete(f.Session.WorkingPath); f.Answer(); await task.WaitAsync(TimeSpan.FromSeconds(10));
        Emit(f, "source-refused", new { outcome }); AssertWorkers(f); Assert.Equal("previous destination", File.ReadAllText(f.Target)); Assert.Contains("Cannot save", f.Vm.Notification); Assert.Single(f.Services.EditSessions.LoadAll());
        if (outcome != "missing") Assert.Equal("base", File.ReadAllText(f.Session.WorkingPath));
        Assert.All(f.Provider.Sources, s => Assert.Equal(1, s.Disposals)); f.AssertNoTemporaryCopies();
    }

    [AvaloniaTheory]
    [InlineData("copy-io")]
    [InlineData("copy-access")]
    [InlineData("copy-partial")]
    [InlineData("copy-truncated")]
    [InlineData("copy-corrupt")]
    [InlineData("copy-grown")]
    [InlineData("move-io")]
    public async Task Destination_failure_preserves_existing_bytes_and_removes_owned_staging(string outcome)
    {
        using var f = await Fixture.Create(outcome: outcome); var task = f.Start(); f.Answer(); await task.WaitAsync(TimeSpan.FromSeconds(10));
        Emit(f, "target-refused", new { outcome }); AssertWorkers(f); Assert.Equal("previous destination", File.ReadAllText(f.Target)); Assert.Contains("Cannot save", f.Vm.Notification); f.AssertPersistent(); f.AssertNoTemporaryCopies();
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(1048577)]
    public async Task Successful_copy_has_exact_bytes_and_keeps_the_edit_open(int size)
    {
        using var f = await Fixture.Create(); byte[] bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 31)).ToArray(); File.WriteAllBytes(f.Session.WorkingPath, bytes);
        var task = f.Start(); f.Answer(); await task.WaitAsync(TimeSpan.FromSeconds(10));
        Emit(f, "success", new { size, SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Target))) }); AssertWorkers(f); Assert.Equal(bytes, File.ReadAllBytes(f.Target)); Assert.Equal(bytes, File.ReadAllBytes(f.Session.WorkingPath)); Assert.Single(f.Services.EditSessions.LoadAll()); Assert.Contains("Saved a copy", f.Vm.Notification); Assert.Equal(1, f.FileProxy.Disposals); f.AssertNoTemporaryCopies();
    }

    [AvaloniaFact]
    public async Task Picker_cancellation_and_nonlocal_result_do_not_write_and_allow_a_later_copy()
    {
        using var f = await Fixture.Create(); var task = f.Start(); f.Picker.TrySetResult(null); await task;
        Assert.Equal("previous destination", File.ReadAllText(f.Target)); f.ResetPicker(new Uri("content://owned/result")); task = f.Start(); f.Answer(); await task;
        Assert.Equal("previous destination", File.ReadAllText(f.Target)); Assert.Equal(1, f.FileProxy.Disposals);
        f.ResetPicker(new Uri(f.Target)); task = f.Start(); f.Answer(); await task; Emit(f, "picker-retry", new { f.PickerCalls }); Assert.Equal("base", File.ReadAllText(f.Target)); AssertWorkers(f); f.AssertPersistent(); f.AssertNoTemporaryCopies();
    }

    [AvaloniaFact]
    public async Task Save_copy_uses_the_edit_after_the_picker_and_freezes_it_before_destination_admission()
    {
        using var f = await Fixture.Create("copy"); Task? task = null;
        try
        {
            task = f.Start(); await Wait(() => f.PickerCalls > 0); File.WriteAllText(f.Session.WorkingPath, "chosen edit"); f.Answer(); await Wait(() => f.Gate.Active > 0 || task.IsCompleted);
            bool reached = f.Gate.Active > 0; File.WriteAllText(f.Session.WorkingPath, "later editor save"); f.Gate.Release.Set(); await task;
            Emit(f, "frozen", new { reached }); Assert.True(reached); AssertWorkers(f); Assert.Equal("chosen edit", File.ReadAllText(f.Target)); Assert.Equal("later editor save", File.ReadAllText(f.Session.WorkingPath)); Assert.Single(f.Services.EditSessions.LoadAll()); f.AssertNoTemporaryCopies();
        }
        finally { f.Gate.Release.Set(); await f.Drain(task); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selecting_the_working_path_is_refused_without_overwriting_the_editor_file(bool normalized)
    {
        using var f = await Fixture.Create(); string path = normalized ? Path.Join(Path.GetDirectoryName(f.Session.WorkingPath)!, ".", Path.GetFileName(f.Session.WorkingPath)) : f.Session.WorkingPath;
        f.ResetPicker(new Uri(path)); var task = f.Start(); f.Answer(); await task;
        Emit(f, "same-path", new { normalized }); Assert.Contains("different", f.Vm.Notification); Assert.Equal("base", File.ReadAllText(f.Session.WorkingPath)); Assert.Equal("previous destination", File.ReadAllText(f.Target)); Assert.Null(f.Files.Snapshot); f.AssertPersistent(); f.AssertNoTemporaryCopies();
    }

    [AvaloniaFact]
    public async Task A_conflict_owner_can_save_a_copy_without_releasing_its_commit_owner()
    {
        using var f = await Fixture.Create(); var owners = (HashSet<string>)typeof(MainViewModel).GetField("_sessionActions", Fixture.Fields)!.GetValue(f.Vm)!; Assert.True(owners.Add(f.Session.Id));
        var task = f.Start(); f.Answer(); await task; Emit(f, "nested-owner", new { retainedOwner = owners.Contains(f.Session.Id) }); Assert.Contains(f.Session.Id, owners); Assert.Equal("base", File.ReadAllText(f.Target)); AssertWorkers(f); f.AssertPersistent(); f.AssertNoTemporaryCopies(); owners.Remove(f.Session.Id);
    }

    private void Emit(Fixture f, string control, object detail) => output.WriteLine("EDIT_SAVE_COPY " + JsonSerializer.Serialize(new { control, detail, Calls = f.Gate.Calls.ToArray(), f.PickerCalls, PickerDisposed = f.FileProxy.Disposals, NativeDesktopInteraction = false, PhysicalOrNetworkSource = false, RealOwnedWorkingAndDestinationFiles = true }));
    private static void AssertWorkers(Fixture f) { Assert.NotEmpty(f.Gate.Calls); Assert.All(f.Gate.Calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); }); }
    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned save-copy checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Gate(string? held)
    {
        public readonly ManualResetEventSlim Release = new(held is null); public readonly ConcurrentQueue<Call> Calls = new(); private int _active;
        public int Active => Volatile.Read(ref _active);
        public void Invoke(string op) { Calls.Enqueue(new(op, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (op != held) return; Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(12))) throw new IOException("Owned hold expired."); } finally { Interlocked.Decrement(ref _active); } }
    }
    // Only the production caller's observed picker API is implemented. This does not simulate a native dialog.
    public class StorageProxy : DispatchProxy
    {
        public Func<Task<IStorageFile?>>? Save; public Uri? Uri; public int Disposals;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "SaveFilePickerAsync" => Save!(), "get_Path" => Uri, "get_Name" => "owned.txt", "get_CanSave" => true,
            "Dispose" => DisposeItem(), _ => throw new NotSupportedException("Unobserved owned picker API: " + method?.Name),
        };
        private object? DisposeItem() { Disposals++; return null; }
    }
    private sealed class Fixture : IDisposable
    {
        public const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window; public required string Root; public required EditSessionRecord Session; public required Provider Provider; public required Gate Gate; public required Files Files; public required IPlatform Original;
        public required string Target; public required StorageProxy PickerProxy; public StorageProxy FileProxy = null!; private IStorageFile _file = null!;
        public TaskCompletionSource<IStorageFile?> Picker = new(TaskCreationOptions.RunContinuationsAsynchronously); public int PickerCalls;
        public static async Task<Fixture> Create(string? held = null, string outcome = "ordinary")
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(); await (Task)typeof(MainWindow).GetField("_interruptedStartup", Fields)!.GetValue(window)!;
            using var source = new MemoryContentSource("notes.txt", "base"u8.ToArray()); var session = services.EditSessions.CreateRemote("owned-unused", "owned", "/notes.txt", source, source.GetRevision()!.Value, null);
            string target = Path.Join(root, "destination.txt"); File.WriteAllText(target, "previous destination"); var gate = new Gate(held); var files = new Files(gate, outcome);
            var provider = new Provider(services.Providers.Get(Schemes.FileSystem), session.WorkingPath, gate, outcome); services.Providers.Register(provider);
            var picker = DispatchProxy.Create<IStorageProvider, StorageProxy>(); var proxy = (StorageProxy)(object)picker;
            var f = new Fixture { Services = services, Vm = vm, Window = window, Root = root, Session = session, Gate = gate, Provider = provider, Target = target, PickerProxy = proxy, Files = files, Original = services.Platform };
            proxy.Save = () => { f.PickerCalls++; return f.Picker.Task; }; f.ResetPicker(new Uri(target));
            typeof(TopLevel).GetField("_storageProvider", Fields)!.SetValue(window, picker); typeof(AppServices).GetField("<Platform>k__BackingField", Fields)!.SetValue(services, new TestPlatform(files)); return f;
        }
        public void ResetPicker(Uri uri) { Picker = new(TaskCreationOptions.RunContinuationsAsynchronously); _file = DispatchProxy.Create<IStorageFile, StorageProxy>(); FileProxy = (StorageProxy)(object)_file; FileProxy.Uri = uri; }
        public void Answer() => Picker.TrySetResult(_file);
        public Task Start() => (Task)typeof(MainViewModel).GetMethod("SaveSessionCopyAsync", Fields)!.Invoke(Vm, [Session])!;
        public void End(string end) { if (end == "shutdown") Services.Io.Dispose(); else Window.Close(); }
        public void AssertPersistent() { Assert.Single(Services.EditSessions.LoadAll()); Assert.Equal("base", File.ReadAllText(Session.WorkingPath)); Assert.Empty(Services.Jobs.Jobs); }
        public void AssertNoTemporaryCopies() { Assert.Empty(Directory.EnumerateDirectories(Services.Paths.TempDirectory, "edit-commit-*")); Assert.Empty(Directory.EnumerateFiles(Root, ".filecat-edit-copy-*.tmp")); }
        public async Task Drain(params Task?[] tasks) { foreach (var task in tasks) if (task is not null) await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        public void Dispose() { Gate.Release.Set(); Answer(); typeof(AppServices).GetField("<Platform>k__BackingField", Fields)!.SetValue(Services, Original); AccessibilityTests.Close(Services, Window, Root); Gate.Release.Dispose(); }
    }
    private sealed class TestPlatform : PortablePlatform { public TestPlatform(Files files) => FileOperations = files; }
    private sealed class Files(Gate gate, string outcome) : PortableFileOperations
    {
        public string? Snapshot;
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            Snapshot = source; gate.Invoke("copy");
            if (outcome == "copy-access") throw new UnauthorizedAccessException("Owned destination denied.");
            if (outcome == "copy-io") throw new IOException("Owned destination failed.");
            if (outcome == "copy-partial") { File.WriteAllText(destination, "partial staged bytes"); throw new IOException("Owned staged copy failed."); }
            if (outcome is "copy-truncated" or "copy-corrupt" or "copy-grown") { File.WriteAllText(destination, outcome == "copy-truncated" ? "ba" : outcome == "copy-corrupt" ? "evil" : "base-extra"); return; }
            base.CopyFile(source, destination, options, progress, ct);
        }
        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false) { gate.Invoke("move"); if (outcome == "move-io") throw new IOException("Owned publish failed."); base.Move(source, destination, replaceExisting, writeThrough); }
    }
    private sealed class Provider(ResourceProvider inner, string path, Gate gate, string outcome) : ResourceProvider
    {
        public readonly ConcurrentQueue<Source> Sources = new(); public override string Scheme => Schemes.FileSystem;
        public override string GetDeviceKey(Location l) => l.Path == path ? "owned-save-source" : "owned-save-target";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l); public override Location? GetParent(Location l) => inner.GetParent(l);
        public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e);
        public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l);
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        public override IContentSource? OpenContent(ItemRef item)
        {
            if (item.FileSystemPath != path) return inner.OpenContent(item); gate.Invoke("open");
            if (outcome == "io") throw new IOException("Owned source failed."); if (outcome == "access") throw new UnauthorizedAccessException("Owned source denied."); if (outcome == "null") return null;
            var source = new Source(inner.OpenContent(item)!, gate, outcome); Sources.Enqueue(source); return source;
        }
    }
    private sealed class Source(IContentSource inner, Gate gate, string outcome) : IContentSource, IPartialContent
    {
        public int Disposals; public bool DisposedWhileReading; private bool _reading; private int _reads;
        public string DisplayName => inner.DisplayName; public long Length => outcome == "unknown" ? -1 : outcome == "oversize" ? EditSessionStore.MaxMemberBytes + 1 : inner.Length; public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath;
        public ContentRevision? GetRevision() { var r = inner.GetRevision(); return outcome == "revision" && _reads > 0 && r is { } v ? v with { ModifiedTicks = v.ModifiedTicks + 1 } : r; }
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => outcome == "partial" ? [(0, 1)] : []; public string? Caveat => outcome == "caveat" ? "Owned uncertainty" : null;
        public int Read(long offset, Span<byte> buffer) { _reading = true; try { gate.Invoke("read"); _reads++; return outcome == "invalid" ? buffer.Length + 1 : outcome == "negative" ? -1 : outcome == "short" ? 0 : outcome == "grew" && offset == inner.Length ? 1 : inner.Read(offset, buffer); } finally { _reading = false; } }
        public void Dispose() { DisposedWhileReading |= _reading; Disposals++; inner.Dispose(); }
    }
}
