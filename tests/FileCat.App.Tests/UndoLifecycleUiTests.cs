using System.Diagnostics;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class UndoLifecycleUiTests
{
    [AvaloniaFact]
    public async Task Review_is_owned_before_await_duplicate_row_and_keyboard_requests_do_not_stack_and_cancel_releases_it()
    {
        using var f = await Fixture.Open(); var original = await f.CreateEmpty();
        var review = f.Vm.UndoJobAsync(original);
        await f.WaitButton("Run these inverse actions");
        Assert.Equal(UndoAttemptState.Reviewing, original.UndoState); Assert.False(original.CanUndo);
        var actions = f.Actions;
        Assert.Single(actions); Assert.Contains("Remove created empty file", actions[0]); Assert.Contains(f.EmptyPath, actions[0]);
        Assert.Contains($"history limit of {f.Services.Jobs.KeepFinished:N0} finished operations", f.Text);
        Assert.Contains("before clearing its operation or closing FileCat", f.Text);
        Assert.Empty(AccessibilityTests.Unnamed(f.Window));
        await f.Vm.UndoJobAsync(original);
        await f.Vm.ExecuteAsync(CommandIds.Undo);
        Assert.Single(f.DialogButtons, b => b.Content as string == "Run these inverse actions");
        Assert.Single(f.Services.Jobs.Jobs);
        f.Click("Cancel"); await review;
        Assert.True(original.CanUndo); Assert.Equal(UndoAttemptState.Available, original.UndoState);
        Assert.True(File.Exists(f.EmptyPath)); Assert.Single(original.UndoSteps);
    }

    [AvaloniaFact]
    public async Task An_approved_queued_inverse_owns_close_policy_and_survives_clearing_original_history()
    {
        using var f = await Fixture.Open(); var original = await f.CreateEmpty();
        f.Services.Jobs.MaxConcurrent = 0;
        var review = f.Vm.UndoJobAsync(original); await f.WaitButton("Run these inverse actions");
        f.Click("Run these inverse actions"); await review;
        var inverse = Assert.Single(f.Services.Jobs.Jobs, j => j.Kind == JobKind.Undo);
        Assert.Equal(JobState.Queued, inverse.State); Assert.False(f.Vm.CanCloseImmediately());
        await f.Vm.UndoJobAsync(original); await f.Vm.ExecuteAsync(CommandIds.Undo);
        Assert.Equal(2, f.Services.Jobs.Jobs.Count); Assert.False(f.Vm.Dialogs.IsOpen);
        f.Services.Jobs.Remove(original); f.Services.Jobs.ClearFinished();
        Assert.Same(inverse, Assert.Single(f.Services.Jobs.Jobs)); Assert.False(f.Vm.CanCloseImmediately());
        var close = f.Vm.ConfirmExitWithActiveWorkAsync(); await f.WaitButton("Keep FileCat open");
        Assert.Contains("Undo:", f.Text); f.Click("Keep FileCat open"); Assert.False(await close);
        Assert.Equal(JobState.Queued, inverse.State);
        f.Services.Jobs.MaxConcurrent = 4; f.Services.Jobs.Schedule(); await Fixture.Wait(() => inverse.State.IsFinished());
        Assert.False(File.Exists(f.EmptyPath)); Assert.True(f.Vm.CanCloseImmediately());
        Assert.Equal(1, inverse.UndoReport!.Count(UndoOutcomeKind.Removed));
        var row = new JobViewModel(inverse); row.Refresh(); Assert.True(row.CanShowUndoReport);
        Assert.Contains("removed", row.DetailText);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_approval_arriving_after_window_close_or_io_shutdown_cannot_admit_an_inverse(bool ioShutdown)
    {
        using var f = await Fixture.Open(); var original = await f.CreateEmpty();
        var review = f.Vm.UndoJobAsync(original); await f.WaitButton("Run these inverse actions");
        var approve = Assert.Single(f.DialogButtons, b => b.Content as string == "Run these inverse actions");
        if (ioShutdown) f.Services.Io.Dispose(); else f.Window.Close();
        approve.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await review;
        Assert.Single(f.Services.Jobs.Jobs); Assert.True(original.CanUndo); Assert.True(File.Exists(f.EmptyPath));
        await f.Vm.UndoJobAsync(original); Assert.Single(f.Services.Jobs.Jobs);
    }

    [AvaloniaFact]
    public async Task A_late_guarded_keep_is_visible_in_the_complete_report_and_the_report_reopens_and_copies()
    {
        using var f = await Fixture.Open();
        var files = Enumerable.Range(0, 70).Select(i =>
        {
            string folder = Directory.CreateDirectory(Path.Combine(f.Root, "rename", i.ToString("D2"))).FullName;
            string path = Path.Combine(folder, "same.txt"); File.WriteAllText(path, "original contents");
            return ItemRef.ForFileSystemPath(path, EntryKind.File);
        }).ToArray();
        var original = f.Services.Jobs.Submit(new JobRequest { Kind = JobKind.Rename, Sources = files, NewNames = Enumerable.Repeat("renamed.txt", 70).ToArray() });
        await Fixture.Wait(() => original.State.IsFinished()); Assert.True(original.CanUndo);
        string changed = Path.Combine(files[1].Parent.Path, "renamed.txt"); File.WriteAllText(changed, "changed after rename and kept by Undo");
        var review = f.Vm.UndoJobAsync(original); await f.WaitButton("Run these inverse actions");
        Assert.Equal(70, f.Actions.Count); Assert.Contains(f.Actions, a => a.Contains(changed, StringComparison.Ordinal));
        f.Click("Run these inverse actions"); await review;
        var inverse = f.Services.Jobs.Jobs.Last(); await Fixture.Wait(() => inverse.State.IsFinished());
        Assert.Equal(JobState.CompletedWithIssues, inverse.State);
        Assert.Equal(69, inverse.UndoReport!.Count(UndoOutcomeKind.Restored)); Assert.Equal(1, inverse.UndoReport.Count(UndoOutcomeKind.Kept));
        Assert.Equal(70, inverse.UndoReport.Outcomes.Count); Assert.False(original.CanUndo);
        var report = Assert.IsType<ReportWindow>(f.Vm.ShowUndoResults(inverse));
        string shown;
        try
        {
            await report.Reading!; shown = report.Text;
            Assert.Contains(changed, shown); Assert.Contains("69 restored", shown); Assert.Contains("1 kept", shown);
            Assert.True(shown.IndexOf(changed, StringComparison.Ordinal) < shown.IndexOf(files[^1].FileSystemPath!, StringComparison.Ordinal));
            var copy = Assert.Single(report.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Copy all");
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Fixture.Wait(() => report.StatusText == "The whole report is on the clipboard.");
            Assert.Equal(shown, await ClipboardExtensions.TryGetTextAsync(report.Clipboard!));
        }
        finally { report.Close(); }
        Assert.NotNull(inverse.UndoReport);
        var reopened = Assert.IsType<ReportWindow>(f.Vm.ShowUndoResults(inverse));
        try { await reopened.Reading!; Assert.Equal(shown, reopened.Text); }
        finally { reopened.Close(); }
        await f.Vm.ShowUndoAffectedAsync(inverse);
        var set = f.Services.ResultSets.Get(f.Vm.ActiveTab!.Location!);
        Assert.NotNull(set); Assert.Equal(70, set.Count);
        Assert.Contains(set.Snapshot(), row => row.Item.FileSystemPath == changed);
    }

    [AvaloniaFact]
    public async Task Show_affected_keeps_distinct_case_resources_in_the_inventory()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("This fixture requires a case-sensitive filesystem.");
        using var f = await Fixture.Open();
        string source = Directory.CreateDirectory(Path.Combine(f.Root, "case-source")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(f.Root, "case-destination")).FullName;
        string lower = Path.Combine(source, "item.txt"), upper = Path.Combine(source, "Item.txt");
        File.WriteAllText(lower, "lower bytes"); File.WriteAllText(upper, "upper bytes");
        Assert.Equal(2, Directory.GetFiles(source).Length);
        var original = f.Services.Jobs.Submit(new JobRequest { Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(lower, EntryKind.File), ItemRef.ForFileSystemPath(upper, EntryKind.File)], Destination = Location.FileSystem(destination) });
        await Fixture.Wait(() => original.State.IsFinished()); Assert.True(original.CanUndo);
        // Occupied original names are both kept. The result inventory has four different resources.
        File.WriteAllText(lower, "new lower occupant"); File.WriteAllText(upper, "new upper occupant");
        var review = f.Vm.UndoJobAsync(original); await f.WaitButton("Run these inverse actions"); f.Click("Run these inverse actions"); await review;
        var inverse = f.Services.Jobs.Jobs.Last(); await Fixture.Wait(() => inverse.State.IsFinished());
        Assert.Equal(2, inverse.UndoReport!.Count(UndoOutcomeKind.Kept));
        await f.Vm.ShowUndoAffectedAsync(inverse);
        var set = Assert.IsType<Core.Search.ResultSet>(f.Services.ResultSets.Get(f.Vm.ActiveTab!.Location!));
        Assert.Equal(new[] { lower, upper, Path.Combine(destination, "item.txt"), Path.Combine(destination, "Item.txt") }.Order(StringComparer.Ordinal),
            set.Snapshot().Select(row => row.Item.FileSystemPath!).Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task Replacement_move_review_names_the_partial_inverse_and_irreversible_destination_content()
    {
        using var f = await Fixture.Open();
        string source = Path.Combine(f.Root, "files", "a.txt"), destination = Directory.CreateDirectory(Path.Combine(f.Root, "replacement")).FullName;
        string target = Path.Combine(destination, "a.txt"); File.WriteAllText(target, "old destination bytes");
        var original = f.Services.Jobs.Submit(new JobRequest { Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Replace } });
        await Fixture.Wait(() => original.State.IsFinished()); Assert.True(original.CanUndo);
        var review = f.Vm.UndoJobAsync(original); await f.WaitButton("Run these inverse actions");
        Assert.Contains("overwritten destination contents cannot be restored", f.Text);
        Assert.Contains("Move back: \"" + target + "\" → \"" + source + "\"", Assert.Single(f.Actions));
        var remainder = f.Window.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "Effects that Undo cannot reverse");
        Assert.Contains(remainder.ItemsSource!.Cast<string>(), s => s.Contains(target, StringComparison.Ordinal) && s.Contains("will not restore", StringComparison.Ordinal));
        f.Click("Cancel"); await review; Assert.True(original.CanUndo);
        Assert.Equal("alpha", File.ReadAllText(target)); Assert.False(File.Exists(source));
    }

    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window; public required string Root;
        public string EmptyPath => Path.Combine(Root, "created-empty.txt");
        public static async Task<Fixture> Open()
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
            return new Fixture { Services = services, Vm = vm, Window = window, Root = root };
        }
        public async Task<Job> CreateEmpty()
        {
            var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.CreateFile, Destination = Location.FileSystem(Root), NewName = "created-empty.txt" });
            await Wait(() => job.State.IsFinished()); Assert.True(job.CanUndo); return job;
        }
        public IEnumerable<Button> DialogButtons => Window.GetVisualDescendants().OfType<Button>().Where(b => b.GetVisualAncestors().OfType<Border>().Any(p => p.Classes.Contains("backdrop")));
        public IReadOnlyList<string> Actions => Window.GetVisualDescendants().OfType<ListBox>()
            .Single(l => AutomationProperties.GetName(l) == "Recorded Undo actions, with full affected paths").ItemsSource!.Cast<string>().ToArray();
        public string Text => string.Join("\n", Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        public Task WaitButton(string text) => Wait(() => DialogButtons.Any(b => b.Content as string == text));
        public void Click(string text) => Assert.Single(DialogButtons, b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public static async Task Wait(Func<bool> ready)
        {
            var clock = Stopwatch.StartNew();
            while (!ready()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Undo UI checkpoint did not arrive."); await Task.Delay(10, TestContext.Current.CancellationToken); }
            Dispatcher.UIThread.RunJobs();
        }
        public void Dispose() => AccessibilityTests.Close(Services, Window, Root);
    }
}
