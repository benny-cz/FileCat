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
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class InterruptedCopyCleanupTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Changes()
    {
        foreach (int length in new[] { 4, 65537 })
            foreach (string flow in new[] { "cleanup", "run-again" })
                foreach (string change in new[] { "unchanged", "destination-only", "source-prefix", "both-preserved", "both-source-restamped", "source-tail" })
                    yield return [length, flow, change];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Changes))]
    public async Task The_actual_confirmation_keeps_the_reviewed_partial_bytes_even_when_the_source_prefix_also_changes(int length, string flow, string change)
    {
        using var f = await Fixture.Create(length);
        string reviewed = Hash(f.Target)!; Task<bool>? run = flow == "run-again" ? f.Vm.RunInterruptedAgainAsync(f.Job) : null;
        if (run is null) f.Cleanup(); await Wait(() => f.Button(f.Action(flow)) is not null);
        var stamp = File.GetLastWriteTimeUtc(f.Target); var sourceStamp = File.GetLastWriteTimeUtc(f.Source);
        byte[] replacement = Enumerable.Repeat((byte)'b', length).ToArray();
        if (change is "destination-only" or "both-preserved" or "both-source-restamped")
        { File.WriteAllBytes(f.Target, replacement); File.SetLastWriteTimeUtc(f.Target, stamp); }
        bool changedSource = change is "source-prefix" or "both-preserved" or "both-source-restamped" or "source-tail";
        if (change is "source-prefix" or "both-preserved" or "both-source-restamped")
        { File.WriteAllBytes(f.Source, [..replacement, ..f.Tail]); File.SetLastWriteTimeUtc(f.Source, change == "both-source-restamped" ? sourceStamp.AddSeconds(5) : sourceStamp); }
        if (change == "source-tail") File.WriteAllBytes(f.Source, [..f.Prefix, ..System.Text.Encoding.ASCII.GetBytes("changed tail")]);
        string? atApproval = Hash(f.Target); f.Click(f.Action(flow)); await f.Finish(run);
        bool expectedKept = flow == "cleanup" ? change is not ("unchanged" or "source-tail") : changedSource || change == "destination-only";
        bool ended = f.Ended; bool exists = File.Exists(f.Target); string? final = Hash(f.Target);
        output.WriteLine("COPY_CLEANUP_FLOW " + JsonSerializer.Serialize(new { length, flow, change, reviewed,
            atApproval, final, exists, ended, Started = run is not null && await run, Jobs = f.Services.Jobs.Jobs.Count,
            SourceSHA256 = Hash(f.Source), OwnedRealInitialCopy = true, ActualCleanupButton = flow == "cleanup",
            EndRecordRemovedToModelInterruption = true, NativeDesktop = false, PhysicalSource = false,
            AtomicAliasIdentityQualified = false, KeptPartialJournalClosureDeliberate = flow == "cleanup" }));
        Assert.Equal(flow == "cleanup" ? expectedKept : true, exists);
        Assert.Equal(flow == "cleanup" || !changedSource, ended);
        Assert.Equal(flow == "run-again" && !changedSource ? 2 : 1, f.Services.Jobs.Jobs.Count);
        if (expectedKept) Assert.Equal(atApproval, final);
        else if (flow == "run-again") Assert.Equal(Hash(f.Source), final);
        if (run is not null) Assert.Equal(!changedSource, await run);
    }

    [AvaloniaTheory]
    [InlineData("cleanup", "target-lock")]
    [InlineData("cleanup", "source-lock")]
    [InlineData("cleanup", "target-readonly")]
    [InlineData("run-again", "target-lock")]
    [InlineData("run-again", "source-lock")]
    [InlineData("run-again", "target-readonly")]
    public async Task Real_Windows_refusals_across_approval_keep_partial_bytes(string flow, string mode)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Actual Windows sharing/read-only approval refusal requires Windows.");
        using var f = await Fixture.Create(4); Task<bool>? run = flow == "run-again" ? f.Vm.RunInterruptedAgainAsync(f.Job) : null;
        if (run is null) f.Cleanup(); await Wait(() => f.Button(f.Action(flow)) is not null);
        var attributes = File.GetAttributes(f.Target); FileStream? held = null;
        try
        {
            if (mode == "source-lock")
            {
                held = new FileStream(f.Source, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.Throws<IOException>(() => { using var denied = File.OpenRead(f.Source); });
            }
            else if (mode == "target-lock")
            {
                held = new FileStream(f.Target, FileMode.Open, FileAccess.Read, FileShare.Read);
                Assert.Throws<IOException>(() => { using var denied = new FileStream(f.Target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
            }
            else
            {
                File.SetAttributes(f.Target, attributes | FileAttributes.ReadOnly);
                Assert.Throws<UnauthorizedAccessException>(() => { using var denied = File.OpenWrite(f.Target); });
            }
            f.Click(f.Action(flow)); await f.Finish(run); EmitRefusal(f, flow, mode, run is not null && await run);
            Assert.Equal(f.Prefix, File.ReadAllBytes(f.Target)); Assert.Equal(flow == "cleanup" || mode != "source-lock", f.Ended);
            Assert.Equal(flow == "run-again" && mode != "source-lock" ? 2 : 1, f.Services.Jobs.Jobs.Count);
        }
        finally { held?.Dispose(); if (File.Exists(f.Target)) File.SetAttributes(f.Target, attributes); }
    }

    [AvaloniaTheory]
    [InlineData("cleanup")]
    [InlineData("run-again")]
    public async Task Real_POSIX_unlink_refusal_across_approval_keeps_partial_bytes_and_restores_mode(string flow)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Actual ordinary-user POSIX unlink approval refusal requires POSIX.");
        if (Environment.UserName == "root") Assert.Skip("An ordinary account is required for POSIX unlink refusal.");
        using var f = await Fixture.Create(4); Task<bool>? run = flow == "run-again" ? f.Vm.RunInterruptedAgainAsync(f.Job) : null;
        if (run is null) f.Cleanup(); await Wait(() => f.Button(f.Action(flow)) is not null);
        var mode = File.GetUnixFileMode(f.Destination);
        try
        {
            File.SetUnixFileMode(f.Destination, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
            Assert.Throws<UnauthorizedAccessException>(() => File.Delete(f.Target));
            f.Click(f.Action(flow)); await f.Finish(run); EmitRefusal(f, flow, "posix-parent-write", run is not null && await run);
            Assert.True(f.Ended); Assert.Equal(f.Prefix, File.ReadAllBytes(f.Target)); Assert.Equal(flow == "run-again" ? 2 : 1, f.Services.Jobs.Jobs.Count);
        }
        finally { File.SetUnixFileMode(f.Destination, mode); }
    }

    private void EmitRefusal(Fixture f, string flow, string mode, bool started) => output.WriteLine("COPY_CLEANUP_REFUSAL " + JsonSerializer.Serialize(new {
        flow, mode, started, Ended = f.Ended, Jobs = f.Services.Jobs.Jobs.Count, TargetSHA256 = Hash(f.Target),
        ActualNativeRefusalVerified = true, ActualCleanupButton = flow == "cleanup", OwnedFileSystem = true,
        NativeDesktop = false, PhysicalSource = false, KeptPartialJournalClosureDeliberate = flow == "cleanup" }));
    private static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    private static async Task Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Owned cleanup checkpoint missing."); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window;
        public required string Root, Source, Target, Destination; public required InterruptedJob Job;
        public required byte[] Prefix; public byte[] Tail = System.Text.Encoding.ASCII.GetBytes("source tail");
        public static async Task<Fixture> Create(int length)
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
                var src = Directory.CreateDirectory(Path.Join(root, "source")).FullName; var dst = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
                var path = Path.Join(src, "owned.bin"); var prefix = Enumerable.Repeat((byte)'a', length).ToArray(); var tail = System.Text.Encoding.ASCII.GetBytes("source tail");
                File.WriteAllBytes(path, [..prefix, ..tail]);
                var initial = services.Jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(path, EntryKind.File)], Destination = Location.FileSystem(dst) });
                await Wait(() => initial.State.IsFinished()); Assert.Equal(JobState.Completed, initial.State);
                var target = Path.Join(dst, "owned.bin"); Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(target));
                var journal = Assert.Single(Directory.GetFiles(services.Paths.JournalDirectory, "job-*.fcj"));
                File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
                var interrupted = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
                File.WriteAllBytes(target, prefix); File.SetCreationTimeUtc(target, DateTime.UtcNow);
                return new Fixture { Services = services, Vm = vm, Window = window, Root = root, Source = path,
                    Target = target, Destination = dst, Job = interrupted, Prefix = prefix, Tail = tail };
            }
            catch { AccessibilityTests.Close(services, window, root); throw; }
        }
        public string Action(string flow) => flow == "cleanup" ? "Delete partial files" : "Copy the rest";
        public bool Ended => !JournalRecovery.Scan(Services.Paths.JournalDirectory).Any(j => j.JournalPath == Job.JournalPath);
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text && b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public void Cleanup()
        {
            var item = new InterruptedJobViewModel(Job); Vm.Operations.Interrupted.Add(item); Vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs();
            var view = Assert.Single(Window.GetVisualDescendants().OfType<OperationsView>());
            typeof(OperationsView).GetMethod("OnCleanupInterrupted", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [new Button { Tag = item }, new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)]);
        }
        public async Task Finish(Task<bool>? run)
        {
            var owners = (HashSet<string>)typeof(MainViewModel).GetField("_interruptedActions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Vm)!;
            var timer = Stopwatch.StartNew();
            // The action owner is removed after the terminal result. Polling the journal can interfere with its
            // durable close, and sampling Notification after clicking can miss an already completed action.
            while (run is not null ? !run.IsCompleted : owners.Contains(Job.JournalPath))
            {
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20), "Owned cleanup did not acknowledge completion.");
                if (Button("OK") is not null) Click("OK");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            if (run is not null) await run; await Wait(() => !Services.Jobs.HasActiveWork);
        }
        public void Dispose() => AccessibilityTests.Close(Services, Window, Root);
    }
}
