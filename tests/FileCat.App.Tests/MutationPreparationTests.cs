using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Automation;
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
using FileCat.Core.Archives;
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

public sealed class MutationPreparationTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task Repeated_link_edits_coalesce_behind_one_owned_preview_call()
    {
        using var f = new Fixture("volume"); await f.Load(false); var task = f.Vm.ExecuteAsync(CommandIds.CreateLink);
        try
        {
            await Wait(() => f.Button("Create") is not null && f.Gate.Active == 1);
            for (int i = 0; i < 16; i++) { f.PathBox("Link path").Text = Path.Join(f.Target, "replacement-" + i + ".txt"); await Task.Delay(225, TestContext.Current.CancellationToken); }
            int held = f.Gate.Active; int admitted = f.Gate.Calls.Count(c => c.Operation == "volume");
            Emit("coalesced-preview", f, new { edits = 16, held, admitted }); Assert.Equal(1, held); Assert.Equal(1, admitted); Assert.False(f.Button("Create")!.IsEnabled);
            f.Click("Cancel"); Assert.False(task.IsCompleted); f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Empty(f.Services.Jobs.Jobs);
        }
        finally { f.Services.Io.Dispose(); f.Gate.Release.Set(); await Drain(f, task); }
    }

    [AvaloniaTheory]
    [InlineData("unchanged")]
    [InlineData("changed")]
    [InlineData("shutdown")]
    public async Task Inline_archive_rename_keeps_the_preapproval_revision(string action)
    {
        using var f = new Fixture(); await f.Load(true); var task = f.Vm.ExecuteAsync(CommandIds.Rename);
        await Wait(() => f.Window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible && t.Text == "kept.txt"));
        var editor = f.Window.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible && t.Text == "kept.txt"); editor.Text = "renamed.txt";
        if (action == "changed") f.ReplaceArchive(); var approvedHash = f.Hash();
        if (action == "shutdown") f.Services.Io.Dispose();
        f.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); await task.WaitAsync(TimeSpan.FromSeconds(15));
        if (action == "shutdown") Assert.Empty(f.Services.Jobs.Jobs);
        else
        {
            var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
            if (action == "changed") { Assert.Equal(approvedHash, f.Hash()); Assert.NotEqual(JobState.Completed, job.State); }
            else { Assert.Equal(JobState.Completed, job.State); using var zip = ZipFile.OpenRead(f.Archive); Assert.NotNull(zip.GetEntry("renamed.txt")); Assert.Null(zip.GetEntry("kept.txt")); }
        }
        Emit("inline-rename", f, new { action, approvedHash });
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Duplicate_member_delete_keeps_its_selected_ordinal_and_refuses_changed_archives(int ordinal, bool changed)
    {
        using var f = new Fixture();
        using (var zip = new ZipArchive(File.Create(f.Archive), ZipArchiveMode.Create))
            for (int i = 0; i < 2; i++) { using var writer = new StreamWriter(zip.CreateEntry("kept.txt").Open()); writer.Write("owned duplicate " + i); }
        await f.Load(true);
        int index = Enumerable.Range(0, f.Left.Listing.VisibleCount).Single(i => { var item = f.Left.Listing.GetItemRef(f.Left.Listing.GetStoreIndex(i)); return item.Name == "kept.txt" && item.Ordinal == ordinal; });
        f.Left.Listing.SetFocus(index); var task = f.StartArchive("delete"); await Wait(() => f.Button("Delete") is not null);
        if (changed) f.ReplaceArchive(); string approvedHash = f.Hash(); f.Click("Delete"); await task;
        var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        Emit("duplicate-delete", f, new { ordinal, changed, approvedHash, state = job.State.ToString(), plannedOrdinal = Assert.Single(job.Request.Archive!.Changes).Ordinal });
        Assert.Equal(ordinal, Assert.Single(job.Request.Archive!.Changes).Ordinal);
        if (changed) { Assert.Equal(approvedHash, f.Hash()); Assert.NotEqual(JobState.Completed, job.State); }
        else { Assert.Equal(JobState.Completed, job.State); using var zip = ZipFile.OpenRead(f.Archive); using var reader = new StreamReader(Assert.Single(zip.Entries).Open()); Assert.Equal("owned duplicate " + (1 - ordinal), reader.ReadToEnd()); }
    }

    [AvaloniaTheory]
    [InlineData("delete")]
    [InlineData("folder")]
    [InlineData("add")]
    [InlineData("pack")]
    public async Task Held_archive_metadata_stays_owned_after_shutdown_without_late_prompts_or_jobs(string operation)
    {
        using var f = new Fixture("info"); await f.Load(operation is "delete" or "folder"); var task = f.StartArchive(operation);
        try
        {
            if (operation == "pack") { await Wait(() => f.Button("Pack") is not null); f.PathBox("Archive path").Text = f.Archive; f.Click("Pack"); }
            await Wait(() => f.Gate.Active > 0 || task.IsCompleted || f.Button("Cancel") is not null);
            AssertWorkers(f.Gate, "info"); Assert.Equal(1, f.Gate.Active); f.Services.Io.Dispose(); await Task.Delay(40, TestContext.Current.CancellationToken);
            Assert.False(task.IsCompleted); Assert.Empty(f.Services.Jobs.Jobs); f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Null(f.Button("Cancel")); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(f.Before, f.Hash()); Emit("archive-held-shutdown", f, new { operation });
        }
        finally { f.Services.Io.Dispose(); f.Gate.Release.Set(); if (f.Button("Cancel") is not null) f.Click("Cancel"); await task.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [AvaloniaFact]
    public async Task Eight_archive_add_preparations_share_the_device_limit_and_other_devices_progress()
    {
        using var f = new Fixture("info"); await f.Load(false); var tasks = new List<Task>();
        try
        {
            for (int i = 0; i < 8; i++) tasks.Add(f.StartArchive("add"));
            await Wait(() => f.Gate.Active >= 2 || tasks.Any(t => t.IsCompleted)); AssertWorkers(f.Gate, "info");
            bool other = await f.Services.Io.Run("owned-independent-archive-device", IoPriority.Normal, _ => true).WaitAsync(TimeSpan.FromSeconds(2));
            var health = f.Services.Io.GetHealth(f.Provider.GetDeviceKey(Location.FileSystem(f.Archive))); int limit = health == DeviceHealth.Responsive ? f.Services.Io.ThreadsPerDevice : f.Services.Io.MaxThreadsPerDevice;
            Assert.True(other); Assert.InRange(f.Gate.Active, 2, limit); Assert.All(tasks, t => Assert.False(t.IsCompleted));
            f.Services.Io.Dispose(); f.Gate.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15)); Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(f.Before, f.Hash()); Emit("archive-shared-admission", f, new { other, limit });
        }
        finally { f.Services.Io.Dispose(); f.Gate.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_approved_real_hard_link_keeps_the_captured_source_and_destination(bool navigate)
    {
        using var f = new Fixture(); await f.Load(false); var task = f.Vm.ExecuteAsync(CommandIds.CreateLink); await Wait(() => f.Button("Create")?.IsEnabled == true);
        string link = f.PathBox("Link path").Text!;
        if (navigate) { f.Right.Navigate(Location.FileSystem(f.Root)); f.Left.Navigate(Location.FileSystem(f.Root)); }
        f.Click("Create"); await task.WaitAsync(TimeSpan.FromSeconds(15)); var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
        Assert.Equal(JobState.Completed, job.State); Assert.Equal(f.Target, job.Request.Destination!.Path); Assert.Equal(Path.Join(f.Folder, "source.txt"), Assert.Single(job.Request.Sources).FileSystemPath);
        File.WriteAllText(Path.Join(f.Folder, "source.txt"), "changed through the actual hard link"); Assert.Equal("changed through the actual hard link", File.ReadAllText(link));
        Emit("actual-hard-link", f, new { navigate, link, state = job.State.ToString() });
    }

    [AvaloniaTheory]
    [InlineData("delete", "unchanged")]
    [InlineData("delete", "changed")]
    [InlineData("delete", "shutdown")]
    [InlineData("folder", "unchanged")]
    [InlineData("folder", "changed")]
    [InlineData("folder", "shutdown")]
    public async Task Archive_prompts_keep_the_preapproval_revision_and_shutdown_cannot_submit(string operation, string action)
    {
        using var f = new Fixture(); await f.Load(true);
        var task = f.StartArchive(operation); await Wait(() => f.Button(operation == "delete" ? "Delete" : "Create") is not null);
        if (operation == "folder") f.Window.GetVisualDescendants().OfType<TextBox>().Single(t => t.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop"))).Text = "new-folder";
        if (action == "changed") f.ReplaceArchive();
        var approvedHash = f.Hash(); var approvedBaseline = ArchiveBaseline.Of(f.Archive);
        if (action == "shutdown") f.Services.Io.Dispose();
        f.Click(operation == "delete" ? "Delete" : "Create"); await task.WaitAsync(TimeSpan.FromSeconds(15));
        foreach (var completed in f.Services.Jobs.Jobs) await Wait(() => completed.State.IsFinished());
        Emit("archive-approval", f, new { operation, action, approvedBaseline, approvedHash, jobs = f.Services.Jobs.Jobs.Select(j => new { state = j.State.ToString(), baseline = j.Request.Archive!.Baseline }).ToArray() });
        if (action == "shutdown") Assert.Empty(f.Services.Jobs.Jobs);
        else
        {
            var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished());
            if (action == "changed") { Assert.NotEqual(approvedBaseline, job.Request.Archive!.Baseline); Assert.Equal(approvedHash, f.Hash()); Assert.NotEqual(JobState.Completed, job.State); }
            else
            {
                Assert.Equal(JobState.Completed, job.State); using var zip = ZipFile.OpenRead(f.Archive);
                Assert.Equal(operation == "folder", zip.GetEntry("kept.txt") is not null); Assert.Equal(operation == "folder", zip.GetEntry("new-folder/") is not null);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("delete", "plain")]
    [InlineData("delete", "io")]
    [InlineData("folder", "plain")]
    [InlineData("folder", "access")]
    [InlineData("add", "plain")]
    [InlineData("add", "io")]
    [InlineData("pack", "plain")]
    [InlineData("pack", "access")]
    public async Task Archive_preparation_uses_device_metadata_and_refuses_unreadable_inputs(string operation, string outcome)
    {
        using var f = new Fixture(outcome: outcome); await f.Load(operation is "delete" or "folder");
        var task = f.StartArchive(operation);
        if (operation == "pack") { await Wait(() => f.Button("Pack") is not null); f.PathBox("Archive path").Text = f.Archive; f.Click("Pack"); }
        await Wait(() => task.IsCompleted || f.Button(operation == "delete" ? "Delete" : "Create") is not null);
        bool shown = !task.IsCompleted; if (shown) f.Click("Cancel"); await task.WaitAsync(TimeSpan.FromSeconds(15));
        foreach (var completed in f.Services.Jobs.Jobs) await Wait(() => completed.State.IsFinished());
        Emit("archive-admission", f, new { operation, outcome, shown }); AssertWorkers(f.Gate, "info");
        if (outcome != "plain") Assert.False(shown);
        if (outcome == "plain" && operation is "add" or "pack")
        {
            var job = Assert.Single(f.Services.Jobs.Jobs); await Wait(() => job.State.IsFinished()); Assert.Equal(JobState.Completed, job.State);
            using var zip = ZipFile.OpenRead(f.Archive); Assert.NotNull(zip.GetEntry("source.txt"));
        }
        else { Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(f.Before, f.Hash()); }
    }

    [AvaloniaTheory]
    [InlineData("capability", "refresh")]
    [InlineData("capability", "navigate")]
    [InlineData("capability", "close")]
    [InlineData("capability", "shutdown")]
    [InlineData("info", "shutdown")]
    public async Task Initial_link_preparation_keeps_active_calls_owned_and_never_opens_a_stale_dialog(string held, string action)
    {
        using var f = new Fixture(held); await f.Load(false); Task? task = null;
        using var rescue = new Timer(_ => f.Gate.Release.Set(), null, TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        try
        {
            task = f.Vm.ExecuteAsync(CommandIds.CreateLink); await Wait(() => f.Gate.Active > 0 || task.IsCompleted);
            AssertWorkers(f.Gate, held); rescue.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (action == "refresh") f.Left.Navigate(f.Left.Location!);
            else if (action == "navigate") f.Left.Navigate(Location.FileSystem(f.Root));
            else if (action == "close") { var replacement = f.Left.Panel.OpenTab(Location.FileSystem(f.Root)); await Wait(() => replacement.Listing.State == ListingState.Complete); f.Left.Panel.CloseTab(f.Left); } else f.Services.Io.Dispose();
            await Task.Delay(40, TestContext.Current.CancellationToken); bool pending = !task.IsCompleted;
            Emit("initial-held", f, new { held, action, pending, f.Gate.Active }); Assert.True(pending); Assert.Null(f.Button("Create"));
            f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15)); Assert.Null(f.Button("Create")); Assert.Empty(f.Services.Jobs.Jobs);
        }
        finally { f.Services.Io.Dispose(); f.Gate.Release.Set(); if (task is not null) await Drain(f, task); }
    }

    [AvaloniaTheory]
    [InlineData("target")]
    [InlineData("selection")]
    public async Task Link_defaults_capture_the_target_and_selection_before_capability_preparation(string change)
    {
        using var f = new Fixture("capability"); await f.Load(false); var task = f.Vm.ExecuteAsync(CommandIds.CreateLink);
        try
        {
            await Wait(() => f.Gate.Active == 1);
            if (change == "target") f.Right.Navigate(Location.FileSystem(f.Root)); else Assert.True(f.Left.Listing.FocusName("second.txt"));
            f.Gate.Release.Set(); await Wait(() => f.Button("Create") is not null);
            string proposed = f.PathBox("Link path").Text!; Emit("captured-default", f, new { change, proposed });
            Assert.Equal(Path.Join(f.Target, "source.txt"), proposed); f.Click("Cancel"); await task;
        }
        finally { f.Gate.Release.Set(); if (f.Button("Cancel") is not null) f.Click("Cancel"); await task.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [AvaloniaFact]
    public async Task Eight_initial_link_checks_share_the_finite_worker_limit_and_other_devices_progress()
    {
        using var f = new Fixture("capability"); await f.Load(false); var tasks = new List<Task>();
        try
        {
            for (int i = 0; i < 8; i++) tasks.Add(f.Vm.ExecuteAsync(CommandIds.CreateLink));
            await Wait(() => f.Gate.Active >= 2); AssertWorkers(f.Gate, "capability");
            bool other = await f.Services.Io.Run("owned-independent-link-device", IoPriority.Normal, _ => true).WaitAsync(TimeSpan.FromSeconds(2));
            var health = f.Services.Io.GetHealth(f.Provider.GetDeviceKey(f.Left.Location!)); int limit = health == DeviceHealth.Responsive ? f.Services.Io.ThreadsPerDevice : f.Services.Io.MaxThreadsPerDevice;
            Emit("link-shared-admission", f, new { other, limit, f.Gate.Active }); Assert.True(other); Assert.InRange(f.Gate.Active, 2, limit); Assert.All(tasks, t => Assert.False(t.IsCompleted));
            f.Services.Io.Dispose(); f.Gate.Release.Set(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15)); Assert.Null(f.Button("Create")); Assert.Empty(f.Services.Jobs.Jobs);
        }
        finally { f.Services.Io.Dispose(); f.Gate.Release.Set(); await Drain(f, Task.WhenAll(tasks)); }
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("edit")]
    [InlineData("shutdown")]
    public async Task Pending_link_preview_cannot_publish_after_its_dialog_or_generation_ends(string action)
    {
        using var f = new Fixture("volume"); await f.Load(false); var task = f.Vm.ExecuteAsync(CommandIds.CreateLink);
        try
        {
            await Wait(() => f.Button("Create") is not null && f.Gate.Active > 0); AssertWorkers(f.Gate, "volume");
            var preview = f.Window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Links to create");
            if (action == "edit")
            {
                f.PathBox("Link path").Text = ""; await Task.Delay(350, TestContext.Current.CancellationToken);
                Assert.False(f.Button("Create")!.IsEnabled); f.Gate.Release.Set(); await Wait(() => f.Gate.Active == 0); await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.False(f.Button("Create")!.IsEnabled); f.Click("Cancel");
            }
            else
            {
                if (action == "shutdown") f.Services.Io.Dispose();
                f.Click("Cancel"); f.Gate.Release.Set(); await task.WaitAsync(TimeSpan.FromSeconds(15)); await Wait(() => f.Gate.Active == 0); await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.Empty(preview.ItemsSource?.Cast<object>() ?? []);
            }
            await task.WaitAsync(TimeSpan.FromSeconds(15)); Emit("ended-preview", f, new { action }); Assert.Empty(f.Services.Jobs.Jobs);
        }
        finally { f.Gate.Release.Set(); if (f.Button("Cancel") is not null) f.Click("Cancel"); await task.WaitAsync(TimeSpan.FromSeconds(15)); await Wait(() => f.Gate.Active == 0); }
    }

    [AvaloniaTheory]
    [InlineData("free")]
    [InlineData("existing")]
    [InlineData("self")]
    [InlineData("missing-folder")]
    [InlineData("relative")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task Link_preview_preserves_validation_and_uses_device_workers(string outcome)
    {
        using var f = new Fixture(outcome: outcome is "io" or "access" ? outcome : "plain"); await f.Load(false);
        var task = f.Vm.ExecuteAsync(CommandIds.CreateLink);
        try
        {
            await Wait(() => task.IsCompleted || f.Button("Create") is not null);
            if (task.IsCompleted) { Emit("link-validation", f, new { outcome, enabled = false }); AssertWorkers(f.Gate, "info"); Assert.Contains("owned failure", f.Vm.Notification); return; }
            string link = outcome switch { "existing" => Path.Join(f.Folder, "second.txt"), "self" => Path.Join(f.Folder, "source.txt"), "missing-folder" => Path.Join(f.Root, "missing", "link.txt"), "relative" => "relative-link.txt", _ => Path.Join(f.Target, "new-link.txt") };
            f.PathBox("Link path").Text = link;
            var preview = f.Window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Links to create");
            await Wait(() => outcome is "free" or "relative"
                ? f.Button("Create")!.IsEnabled && (preview.ItemsSource?.Cast<string>().Any(r => r.StartsWith(Path.GetFileName(link), StringComparison.Ordinal)) ?? false)
                : outcome == "missing-folder"
                    ? f.Window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains("does not exist", StringComparison.Ordinal) == true)
                    : preview.ItemsSource?.Cast<string>().Any(r => r.StartsWith("⚠ " + Path.GetFileName(link), StringComparison.Ordinal)) == true);
            bool enabled = f.Button("Create")!.IsEnabled; Emit("link-validation", f, new { outcome, enabled }); Assert.Equal(outcome is "free" or "relative", enabled);
            AssertWorkers(f.Gate, "info"); if (outcome is "free" or "relative") AssertWorkers(f.Gate, "volume");
            f.Click("Cancel"); await task; Assert.Empty(f.Services.Jobs.Jobs);
        }
        finally { if (f.Button("Cancel") is not null) f.Click("Cancel"); await task.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    private void Emit(string control, Fixture f, object details) => output.WriteLine("MUTATION_PREPARATION " + JsonSerializer.Serialize(new { control, details, Calls = f.Gate.Calls.ToArray(), f.Before, After = f.Hash(), NativeDesktopInteraction = false, PhysicalDeviceAccess = false, MetadataAndVolumeControlInjected = true }));
    private static void AssertWorkers(Gate gate, string operation) { var calls = gate.Calls.Where(c => c.Operation == operation).ToArray(); Assert.NotEmpty(calls); Assert.All(calls, c => { Assert.False(c.Ui); Assert.StartsWith("FileCat I/O ", c.Thread); }); }
    private static async Task Wait(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned mutation checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); } }
    private static async Task Drain(Fixture f, Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(15)) { if (f.Button("Cancel") is not null) f.Click("Cancel"); await Task.Delay(10, TestContext.Current.CancellationToken); }
        await task.WaitAsync(TimeSpan.FromSeconds(1));
    }
    private sealed class Fixture : IDisposable
    {
        public readonly AppServices Services; public readonly MainViewModel Vm; public readonly MainWindow Window; public readonly TabViewModel Left, Right;
        public readonly string Root, Folder, Target, Archive, Before; public readonly Gate Gate; public readonly ResourceProvider Provider;
        private readonly IPlatform _original; private static readonly FieldInfo PlatformField = typeof(AppServices).GetField("<Platform>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public Fixture(string? held = null, string outcome = "plain")
        {
            (Services, Vm, Window, Root) = AccessibilityTests.OpenMainWindow(); _original = Services.Platform;
            Folder = Directory.CreateDirectory(Path.Join(Root, "sources")).FullName; Target = Directory.CreateDirectory(Path.Join(Root, "links")).FullName; Archive = Path.Join(Root, "owned.zip");
            File.WriteAllText(Path.Join(Folder, "source.txt"), "owned source bytes"); File.WriteAllText(Path.Join(Folder, "second.txt"), "owned second bytes");
            using (var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create)) { using var s = zip.CreateEntry("kept.txt").Open(); s.Write("original owned ZIP member"u8); }
            Before = Hash(); Left = Vm.Workspace.Panels[0].ActiveTab!; Right = Vm.Workspace.Panels[1].ActiveTab!; Gate = new Gate(held);
            Provider = new FilesProvider(Services.Providers.Get(Schemes.FileSystem)); Services.Providers.Register(Provider); PlatformField.SetValue(Services, new TestPlatform(new TestFiles(Gate, outcome)));
        }
        public async Task Load(bool inside)
        {
            Left.Navigate(inside ? ZipProvider.ForFile(Archive) : Location.FileSystem(Folder)); Right.Navigate(Location.FileSystem(Target)); Vm.Workspace.Activate(Left.Panel);
            await Wait(() => Left.Listing.State == ListingState.Complete && Right.Listing.State == ListingState.Complete); Assert.True(Left.Listing.FocusName(inside ? "kept.txt" : "source.txt"));
        }
        public string Hash() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Archive)));
        public void ReplaceArchive() { Services.Zip.Release(Archive); string replacement = Path.Join(Root, "external-replacement.zip"); using (var zip = new ZipArchive(File.Create(replacement), ZipArchiveMode.Create)) { using var s = zip.CreateEntry("kept.txt").Open(); s.Write("new external bytes must survive the old approval"u8); } File.SetLastWriteTimeUtc(replacement, DateTime.UtcNow.AddMinutes(1)); File.Move(replacement, Archive, true); }
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public TextBox PathBox(string name) => Window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
        public Task StartArchive(string operation) => operation switch
        {
            "delete" => Vm.ExecuteAsync(CommandIds.Delete), "folder" => Vm.ExecuteAsync(CommandIds.MakeDirectory), "pack" => Vm.ExecuteAsync(CommandIds.Pack),
            _ => (Task)typeof(MainViewModel).GetMethod("AddToArchiveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Vm, [JobKind.Copy, new List<ItemRef> { ItemRef.ForFileSystemPath(Path.Join(Folder, "source.txt"), EntryKind.File) }, ZipProvider.ForFile(Archive), new TransferOptions()])!,
        };
        public void Dispose() { Gate.Release.Set(); PlatformField.SetValue(Services, _original); AccessibilityTests.Close(Services, Window, Root); Gate.Dispose(); }
    }
    private sealed record Call(string Operation, bool Ui, string Thread);
    private sealed class Gate(string? held) : IDisposable
    {
        public readonly ConcurrentQueue<Call> Calls = new(); public readonly ManualResetEventSlim Release = new(held is null); private int _active;
        public int Active => Volatile.Read(ref _active);
        public void Invoke(string operation) { Calls.Enqueue(new(operation, Dispatcher.UIThread.CheckAccess(), Thread.CurrentThread.Name ?? "")); if (operation != held) return; Interlocked.Increment(ref _active); try { if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Owned call was not released."); } finally { Interlocked.Decrement(ref _active); } }
        public void Dispose() => Release.Dispose();
    }
    private sealed class TestPlatform : PortablePlatform { public TestPlatform(TestFiles files) => FileOperations = files; }
    private sealed class TestFiles(Gate gate, string outcome) : PortableFileOperations
    {
        public override bool? CanCreateSymbolicLinks { get { gate.Invoke("capability"); return false; } }
        public override FileSystemItemInfo? TryGetInfo(string path) { gate.Invoke("info"); if (outcome == "io") throw new IOException("owned failure"); if (outcome == "access") throw new UnauthorizedAccessException("owned failure"); return base.TryGetInfo(path); }
        public override VolumeInfo GetVolumeInfo(string path) { gate.Invoke("volume"); return VolumeInfo.Unknown(path) with { SupportsHardLinks = true, SupportsSymbolicLinks = true }; }
    }
    private sealed class FilesProvider(ResourceProvider inner) : ResourceProvider
    {
        public override string Scheme => Schemes.FileSystem; public override string GetDeviceKey(Location l) => "owned-mutation-preparation";
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l); public override Location? GetParent(Location l) => inner.GetParent(l);
        public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e); public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l);
        public override bool TryParse(string t, Location? current, out Location? l) => inner.TryParse(t, current, out l);
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
    }
}
