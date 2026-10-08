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
public sealed class InterruptedCopySelectionUiTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int length in new[] { 4, 65537 })
            foreach (string flow in new[] { "cleanup", "run-again" })
                foreach (bool selected in new[] { true, false }) yield return [length, flow, selected];
    }
    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task The_actual_cleanup_and_run_again_confirmation_keep_unselected_partial_files(int length, string flow, bool selected)
    {
        using var f = await Fixture.Create(length); string source = f.Source, target = f.Target;
        if (!selected)
        {
            File.WriteAllBytes(f.Target, [..f.Prefix, ..f.Tail]);
            source = Path.Join(Path.GetDirectoryName(f.Source)!, "unselected.bin"); target = Path.Join(f.Destination, "unselected.bin");
            File.WriteAllBytes(source, [..f.Prefix, ..f.Tail]); File.WriteAllBytes(target, f.Prefix); File.SetCreationTimeUtc(target, DateTime.UtcNow);
        }
        var roots = Assert.IsAssignableFrom<IReadOnlyList<string>>(JournalRecovery.LoadSources(f.Job));
        Assert.Single(roots); Assert.Equal(selected, roots.Contains(source, FileCat.Core.FileSystem.PathUtil.SafetyComparer));
        Task<bool>? run = flow == "run-again" ? f.Vm.RunInterruptedAgainAsync(f.Job) : null;
        if (run is null) f.Cleanup();
        await Wait(() => f.Button(flow == "run-again" ? "Copy the rest" : "Delete partial files") is not null || flow == "cleanup" && f.Button("OK") is not null);
        string action = flow == "run-again" ? "Copy the rest" : f.Button("Delete partial files") is not null ? "Delete partial files" : "OK";
        f.Click(action); await f.Finish(run); bool exists = File.Exists(target); bool ended = f.Ended;
        output.WriteLine("COPY_SELECTION_UI " + JsonSerializer.Serialize(new { length, flow, selected, Action = action,
            ActualSourceRoots = roots, ActualSource = source, TargetExists = exists, Ended = ended,
            Started = run is not null && await run, Jobs = f.Services.Jobs.Jobs.Count,
            TargetSHA256 = Hash(target), ExpectedPartialSHA256 = Convert.ToHexString(SHA256.HashData(f.Prefix)),
            SourceSHA256 = Hash(source), OwnedRealInitialCopy = true, EndRecordRemovedToModelInterruption = true,
            NativeDesktop = false, PhysicalSource = false, AtomicAliasIdentityQualified = false }));
        Assert.Equal(!selected || flow == "run-again", exists); Assert.True(ended);
        Assert.Equal(flow == "run-again" ? 2 : 1, f.Services.Jobs.Jobs.Count);
        if (exists) Assert.Equal(selected ? File.ReadAllBytes(source) : f.Prefix, File.ReadAllBytes(target));
        if (run is not null) Assert.True(await run);
        Assert.Equal(flow == "run-again" ? "Copy the rest" : selected ? "Delete partial files" : "OK", action);
    }
    private static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    private static async Task Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Owned selection confirmation missing."); await Task.Delay(10, TestContext.Current.CancellationToken); }
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
