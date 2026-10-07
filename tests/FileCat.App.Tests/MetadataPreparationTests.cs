using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
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
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class MetadataPreparationTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lexically_overlapping_folders_need_no_final_path_calls_to_disable_sync(bool nested)
    {
        using var f = new Fixture("zip", "plain", "final"); await f.Load();
        f.Right.Navigate(Location.FileSystem(nested ? Directory.CreateDirectory(Path.Join(f.Folder, "inner")).FullName : f.Folder));
        await Wait(() => f.Right.Listing.State == ListingState.Complete); var before = DirectoryDiffWindow.OpenWindows.ToHashSet();
        await (await f.StartCompare()).WaitAsync(TimeSpan.FromSeconds(10));
        var w = Assert.Single(DirectoryDiffWindow.OpenWindows, w => !before.Contains(w));
        try { Emit("lexical-overlap", f, new { nested, w.OffersSync }); Assert.False(w.OffersSync); Assert.DoesNotContain(f.Gate.Calls, c => c.Operation == "final"); f.AssertUnchanged(); }
        finally { w.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attribute_summaries_preserve_uniform_and_mixed_states(bool mixed)
    {
        using var f = new Fixture("zip", "plain"); await f.Load(); f.SelectBoth();
        var second = Path.Join(f.Folder, "second.txt");
        if (OperatingSystem.IsWindows())
        { File.SetAttributes(f.Archive, FileAttributes.Normal); File.SetAttributes(second, mixed ? FileAttributes.ReadOnly : FileAttributes.Normal); }
        else
        { File.SetUnixFileMode(f.Archive, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.SetUnixFileMode(second, UnixFileMode.UserRead | UnixFileMode.UserWrite | (mixed ? UnixFileMode.GroupExecute : 0)); }
        try
        {
            var task = f.Vm.ExecuteAsync(CommandIds.Attributes); await Wait(() => f.Button("Apply") is not null);
            var box = f.Window.GetVisualDescendants().OfType<CheckBox>().Single(b => OperatingSystem.IsWindows() ? b.Content as string == "Read-only" : Avalonia.Automation.AutomationProperties.GetName(b) == "Group execute");
            bool? state = box.IsChecked; f.Click("Cancel"); await task;
            Emit("attribute-state", f, new { mixed, state }); Assert.Equal(mixed ? null : (bool?)false, state); Assert.Empty(f.Services.Jobs.Jobs); f.AssertUnchanged();
        }
        finally { if (OperatingSystem.IsWindows()) File.SetAttributes(second, FileAttributes.Normal); }
    }

    [AvaloniaTheory]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("target")]
    public async Task An_explicitly_approved_attribute_job_keeps_the_selection_shown_in_its_dialog(string change)
    {
        using var f = new Fixture("zip", "plain"); await f.Load();
        var task = f.Vm.ExecuteAsync(CommandIds.Attributes); await Wait(() => f.Button("Apply") is not null);
        f.Window.GetVisualDescendants().OfType<TextBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Modified").Text = "2020-02-03 04:05:06";
        if (change == "selection") f.Left.Listing.FocusName("second.txt");
        else if (change == "source") f.Left.Navigate(Location.FileSystem(f.Root));
        else f.Right.Navigate(Location.FileSystem(f.Root));
        f.Click("Apply"); await task.WaitAsync(TimeSpan.FromSeconds(10));
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        Emit("approved-attributes", f, new { change, state = job.State.ToString(), sources = job.Request.Sources.Select(s => s.FileSystemPath).ToArray() });
        Assert.Equal(FileCat.Core.Jobs.JobState.Completed, job.State); Assert.Equal(f.Archive, Assert.Single(job.Request.Sources).FileSystemPath);
        var expected = DateTime.Parse("2020-02-03 04:05:06").ToUniversalTime(); Assert.InRange(Math.Abs((File.GetLastWriteTimeUtc(f.Archive) - expected).TotalSeconds), 0, 1);
        f.AssertUnchanged();
    }
    [AvaloniaTheory]
    [InlineData("zip", "plain")]
    [InlineData("tar", "plain")]
    [InlineData("zip", "short")]
    [InlineData("tar", "short")]
    [InlineData("plain", "plain")]
    [InlineData("zip", "io")]
    [InlineData("zip", "access")]
    [InlineData("zip", "unsupported")]
    [InlineData("zip", "invalid-count")]
    public async Task Signature_opening_uses_registered_content_workers_and_preserves_real_formats(string format, string outcome)
    {
        using var f = new Fixture(format, outcome); await f.Load();
        await f.Vm.ExecuteAsync(CommandIds.Enter).WaitAsync(TimeSpan.FromSeconds(10));
        bool opened = f.Left.Location!.Scheme is Schemes.Zip or Schemes.Archive;
        Emit("signature-outcome", f, new { format, outcome, opened });
        Assert.Equal(format != "plain" && outcome is "plain" or "short", opened);
        AssertWorkers(f.Gate, "open");
        Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); });
        if (opened)
        {
            await Wait(() => f.Left.Listing.State != ListingState.Loading);
            Assert.Equal(ListingState.Complete, f.Left.Listing.State);
            Assert.True(f.Left.Listing.FindStoreIndex("kept.txt") >= 0);
        }
        f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("plain")]
    [InlineData("missing-one")]
    [InlineData("missing-all")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task Attributes_require_metadata_for_every_selected_item_before_showing_states(string outcome)
    {
        using var f = new Fixture("zip", outcome); await f.Load(); f.SelectBoth();
        var task = f.Vm.ExecuteAsync(CommandIds.Attributes);
        await Wait(() => task.IsCompleted || f.Button("Apply") is not null);
        bool shown = f.Button("Apply") is not null;
        Emit("attributes-outcome", f, new { outcome, shown });
        if (shown) f.Click("Cancel");
        await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(outcome == "plain", shown);
        if (!shown) Assert.Contains("cannot be read", f.Vm.Notification, StringComparison.Ordinal);
        AssertWorkers(f.Gate, "info"); Assert.Empty(f.Services.Jobs.Jobs); f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("plain")]
    [InlineData("alias")]
    [InlineData("unknown-left")]
    [InlineData("unknown-right")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task Recursive_preview_requires_known_distinct_final_paths_before_offering_synchronization(string outcome)
    {
        using var f = new Fixture("zip", outcome); await f.Load();
        var before = DirectoryDiffWindow.OpenWindows.ToHashSet();
        await (await f.StartCompare()).WaitAsync(TimeSpan.FromSeconds(10));
        var w = Assert.Single(DirectoryDiffWindow.OpenWindows, w => !before.Contains(w));
        try
        {
            Emit("overlap-outcome", f, new { outcome, w.Title });
            var sync = w.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string)?.StartsWith("Synchronize", StringComparison.Ordinal) == true);
            Assert.Equal(outcome == "plain", sync.IsVisible);
            AssertWorkers(f.Gate, "final"); f.AssertUnchanged();
        }
        finally { w.Close(); }
    }

    public static TheoryData<string, string> EndedDemand
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var operation in new[] { "open", "read", "info", "final" })
                foreach (var action in new[] { "refresh", "navigate", "close", "shutdown" }) rows.Add(operation, action);
            return rows;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(EndedDemand))]
    public async Task Ended_preparation_retains_active_calls_until_return_and_never_publishes_late_UI(string operation, string action)
    {
        using var f = new Fixture("zip", "plain", operation); await f.Load();
        var before = DirectoryDiffWindow.OpenWindows.ToHashSet(); Task? task = null;
        // The baseline's synchronous UI call is released by a bounded owned timer, so a regression cannot deadlock the fixture.
        using var rescue = new Timer(_ => f.Gate.Release.Set(), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        try
        {
            task = operation == "final" ? await f.StartCompare() : f.Vm.ExecuteAsync(operation == "info" ? CommandIds.Attributes : CommandIds.Enter);
            await Wait(() => f.Gate.Active > 0 || task.IsCompleted || f.Button("Apply") is not null);
            AssertWorkers(f.Gate, operation);
            Assert.Equal(1, f.Gate.Active); Assert.False(task.IsCompleted);
            rescue.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            string? notification = f.Vm.Notification;
            if (action == "shutdown") f.Services.Io.Dispose();
            else if (action == "refresh") { f.Left.Refresh(); await Wait(() => !f.Left.Listing.IsRefreshing); }
            else if (action == "navigate") { f.Left.Navigate(Location.FileSystem(f.Root)); await Wait(() => f.Left.Listing.State == ListingState.Complete); }
            else { var replacement = f.Left.Panel.OpenTab(Location.FileSystem(f.Root)); await Wait(() => replacement.Listing.State == ListingState.Complete); f.Left.Panel.CloseTab(f.Left); }
            await Task.Delay(30, TestContext.Current.CancellationToken);
            bool pending = !task.IsCompleted; int active = f.Gate.Active;
            var paths = f.Left.Location;
            int disposedDuringHold = f.Provider.Sources.Sum(s => s.Disposals);
            f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(10));
            Emit("ended-demand", f, new { operation, action, pending, active, disposedDuringHold, notification });
            Assert.True(pending); Assert.Equal(1, active); Assert.Equal(0, disposedDuringHold);
            Assert.Equal(paths, f.Left.Location); Assert.Equal(notification, f.Vm.Notification);
            Assert.Null(f.Button("Apply")); Assert.Equal(before, DirectoryDiffWindow.OpenWindows.ToHashSet()); Assert.Empty(f.Services.Jobs.Jobs);
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); }); f.AssertUnchanged();
        }
        finally
        {
            f.Gate.Release.Set(); if (f.Button("Apply") is not null) f.Click("Cancel");
            foreach (var w in DirectoryDiffWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) w.Close();
            if (task is not null) await task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("read")]
    [InlineData("info")]
    [InlineData("final")]
    public async Task Eight_preparations_share_finite_device_workers_and_allow_another_device_to_progress(string operation)
    {
        using var f = new Fixture("zip", "plain", operation); await f.Load(); var tasks = new List<Task>();
        var before = DirectoryDiffWindow.OpenWindows.ToHashSet();
        using var rescue = new Timer(_ => f.Gate.Release.Set(), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        try
        {
            for (int i = 0; i < 8; i++)
            {
                tasks.Add(operation == "final" ? await f.StartCompare() : f.Vm.ExecuteAsync(operation == "info" ? CommandIds.Attributes : CommandIds.Enter));
                if (i == 0) { await Wait(() => f.Gate.Calls.Any(c => c.Operation == operation) || tasks[0].IsCompleted); AssertWorkers(f.Gate, operation); }
            }
            await Wait(() => f.Gate.Active >= 2 || f.Gate.Release.IsSet);
            AssertWorkers(f.Gate, operation); rescue.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            bool other = await f.Services.Io.Run("owned-unrelated-preparation-device", IoPriority.Interactive, _ => true).WaitAsync(TimeSpan.FromSeconds(2));
            var health = f.Services.Io.GetHealth(f.Provider.GetDeviceKey(f.Left.Location!));
            int limit = health == DeviceHealth.Responsive ? f.Services.Io.ThreadsPerDevice : f.Services.Io.MaxThreadsPerDevice;
            int active = f.Gate.Active, pending = tasks.Count(t => !t.IsCompleted);
            f.Services.Io.Dispose(); f.Gate.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
            Emit("shared-admission", f, new { operation, other, health = health.ToString(), limit, active, pending });
            Assert.True(other); Assert.InRange(active, 2, limit); Assert.Equal(8, pending); Assert.Null(f.Button("Apply"));
            Assert.Equal(before, DirectoryDiffWindow.OpenWindows.ToHashSet()); Assert.Equal(0, f.Gate.Active); Assert.Empty(f.Services.Jobs.Jobs);
            Assert.All(f.Provider.Sources, s => { Assert.Equal(1, s.Disposals); Assert.False(s.DisposedWhileReading); }); f.AssertUnchanged();
        }
        finally
        {
            f.Gate.Release.Set(); if (f.Button("Apply") is not null) f.Click("Cancel");
            foreach (var w in DirectoryDiffWindow.OpenWindows.Where(w => !before.Contains(w)).ToArray()) w.Close();
            if (tasks.Count > 0) await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private void Emit(string control, Fixture f, object details) => output.WriteLine("METADATA_PREPARATION " + JsonSerializer.Serialize(new
    { control, details, Calls = f.Gate.Calls.ToArray(), Sources = f.Provider.Sources.Select(s => new { s.Reads, s.Disposals, s.DisposedWhileReading }), f.Before, After = f.Hash(), NativeDesktopInteraction = false, PhysicalDeviceAccess = false }));
    private static void AssertWorkers(Gate g, string operation)
    {
        var calls = g.Calls.Where(c => c.Operation == operation).ToArray(); Assert.NotEmpty(calls);
        Assert.All(calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); });
    }
    private static async Task Wait(Func<bool> done)
    {
        var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned preparation checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly AppServices Services; public readonly MainViewModel Vm; public readonly MainWindow Window; public readonly string Root, Folder, Archive, Before;
        public readonly TabViewModel Left, Right; public readonly Gate Gate; public readonly Provider Provider; private readonly IPlatform _original;
        private static readonly FieldInfo PlatformField = typeof(AppServices).GetField("<Platform>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public Fixture(string format, string outcome, string? held = null)
        {
            (Services, Vm, Window, Root) = AccessibilityTests.OpenMainWindow(); _original = Services.Platform;
            Folder = Directory.CreateDirectory(Path.Join(Root, "signatures")).FullName; Archive = Path.Join(Folder, "owned.payload");
            if (format == "zip") { using var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create); using var s = zip.CreateEntry("kept.txt").Open(); s.Write("owned member bytes"u8); }
            else if (format == "tar") { using var tar = new TarWriter(File.Create(Archive), TarEntryFormat.Ustar); tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "kept.txt") { DataStream = new MemoryStream("owned member bytes"u8.ToArray()) }); }
            else File.WriteAllText(Archive, "owned ordinary text");
            File.WriteAllText(Path.Join(Folder, "second.txt"), "owned second selection"); Before = Hash();
            Left = Vm.Workspace.Panels[0].ActiveTab!; Right = Vm.Workspace.Panels[1].ActiveTab!;
            Gate = new Gate(held); Provider = new Provider(Services.Providers.Get(Schemes.FileSystem), Gate, outcome); Services.Providers.Register(Provider);
            PlatformField.SetValue(Services, new MetadataPlatform(new MetadataFiles(Gate, outcome, Folder)));
        }
        public string Hash() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Archive)));
        public void AssertUnchanged() => Assert.Equal(Before, Hash());
        public async Task Load()
        {
            Left.Navigate(Location.FileSystem(Folder)); Right.Navigate(Location.FileSystem(Path.Join(Root, "files"))); Vm.Workspace.Activate(Left.Panel);
            await Wait(() => Left.Listing.State == ListingState.Complete && Right.Listing.State == ListingState.Complete); Assert.True(Left.Listing.FocusName("owned.payload"));
        }
        public void SelectBoth() { foreach (var name in new[] { "owned.payload", "second.txt" }) Left.Listing.SetMark(Left.Listing.FindStoreIndex(name), true); }
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public async Task<Task> StartCompare()
        {
            var task = Vm.ExecuteAsync(CommandIds.CompareDirectories); await Wait(() => Button("Compare") is not null);
            Window.GetVisualDescendants().OfType<CheckBox>().Single(c => (c.Content as string)?.StartsWith("Include subfolders", StringComparison.Ordinal) == true).IsChecked = true;
            Click("Compare"); return task;
        }
        public void Dispose() { Gate.Release.Set(); PlatformField.SetValue(Services, _original); AccessibilityTests.Close(Services, Window, Root); Gate.Dispose(); }
    }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Gate(string? held) : IDisposable
    {
        public readonly ConcurrentQueue<Call> Calls = new(); public readonly ManualResetEventSlim Release = new(held is null); private int _active;
        public int Active => Volatile.Read(ref _active);
        public void Invoke(string operation)
        {
            Calls.Enqueue(new(operation, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? ""));
            if (operation != held) return;
            Interlocked.Increment(ref _active);
            try { if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Owned preparation call was not released."); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() => Release.Dispose();
    }
    private sealed class MetadataPlatform : PortablePlatform { public MetadataPlatform(MetadataFiles files) => FileOperations = files; }
    private sealed class MetadataFiles(Gate gate, string outcome, string left) : PortableFileOperations
    {
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            gate.Invoke("info"); if (outcome == "io") throw new IOException("owned metadata cannot be read");
            if (outcome == "access") throw new UnauthorizedAccessException("owned metadata cannot be read");
            return outcome == "missing-all" || outcome == "missing-one" && Path.GetFileName(path) == "second.txt" ? null : base.TryGetInfo(path);
        }
        public override VolumeInfo GetVolumeInfo(string path) => VolumeInfo.Unknown(path);
        public override string? GetFinalPath(string path)
        {
            gate.Invoke("final"); if (outcome == "io") throw new IOException("owned final path unavailable");
            if (outcome == "access") throw new UnauthorizedAccessException("owned final path unavailable");
            if (outcome == "unknown-left" && path == left || outcome == "unknown-right" && path != left) return null;
            return outcome == "alias" ? left : Path.GetFullPath(path);
        }
    }
    private sealed class Provider(ResourceProvider inner, Gate gate, string outcome) : ResourceProvider
    {
        public readonly ConcurrentQueue<Source> Sources = new(); public override string Scheme => Schemes.FileSystem;
        public override string GetDeviceKey(Location l) => "owned-metadata-preparation";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l);
        public override Location? GetParent(Location l) => inner.GetParent(l);
        public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e);
        public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l);
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        public override IContentSource? OpenContent(ItemRef item)
        {
            gate.Invoke("open"); if (outcome == "io") throw new IOException("owned content unavailable");
            if (outcome == "access") throw new UnauthorizedAccessException("owned content unavailable");
            if (outcome == "unsupported") throw new NotSupportedException("owned content unsupported");
            var s = new Source(new FileContentSource(item.FileSystemPath!), gate, outcome); Sources.Enqueue(s); return s;
        }
    }
    private sealed class Source(IContentSource inner, Gate gate, string outcome) : IContentSource
    {
        private bool _reading; public int Disposals, Reads; public bool DisposedWhileReading;
        public string DisplayName => inner.DisplayName; public long Length => inner.Length; public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer)
        {
            _reading = true; Interlocked.Increment(ref Reads);
            try { gate.Invoke("read"); if (outcome == "invalid-count") return buffer.Length + 1; return inner.Read(offset, outcome == "short" ? buffer[..Math.Min(1, buffer.Length)] : buffer); }
            finally { _reading = false; }
        }
        public void Dispose() { DisposedWhileReading |= _reading; Interlocked.Increment(ref Disposals); inner.Dispose(); }
    }
}
