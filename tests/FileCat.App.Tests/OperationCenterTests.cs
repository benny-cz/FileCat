using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>The operations panel's count of finished jobs and "Clear finished" agree (CI run 36931084621).</summary>
public sealed class OperationCenterTests
{
    [AvaloniaFact]
    public async Task Reopening_details_reveals_running_operation_without_stealing_open_drawer_selection()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 1000, height: 600);
        try
        {
            var center = vm.Operations;
            string target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            for (int i = 0; i < 8; i++)
            {
                var finished = services.Jobs.Submit(new JobRequest
                {
                    Kind = JobKind.Copy,
                    Sources = [ItemRef.ForFileSystemPath(Path.Combine(root, $"missing-{i}.txt"), EntryKind.File)],
                    Destination = Location.FileSystem(target),
                });
                await Until(() => finished.State.IsFinished() && center.Jobs.Any(j => j.Job == finished));
            }
            center.IsOpen = true;
            var oldest = center.Jobs[^1];
            center.Selected = oldest;
            window.UpdateLayout();
            var history = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => AutomationProperties.GetName(list) == "Operations history");
            var scroll = history.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Avalonia.Vector(0, scroll.Extent.Height);
            window.UpdateLayout();
            Assert.True(scroll.Offset.Y > 0, "The history must have a previous scroll position to reveal a new job.");
            center.IsOpen = false;

            string source = Path.Combine(root, "conflict.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(Path.Combine(target, "conflict.txt"), "old");
            var running = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(target),
            });
            await Until(() => running.State == JobState.AwaitingDecision && center.Jobs.Count == 9);
            Assert.Same(oldest, center.Selected);

            window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "Details (Ctrl+J)")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => center.IsOpen && ReferenceEquals(center.Selected?.Job, running));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Same(center.Selected, history.SelectedItem);
            Assert.Equal(0, history.SelectedIndex);
            var activeRow = history.ContainerFromIndex(0);
            var rowPosition = activeRow?.TranslatePoint(new Avalonia.Point(0, 0), history);
            Assert.True(rowPosition is { } pos && pos.Y < history.Bounds.Height &&
                        pos.Y + activeRow!.Bounds.Height > 0,
                $"Opening Details should reveal the running row (scroll {scroll.Offset.Y}, list {history.Bounds.Height}, row {rowPosition}).");

            // A later progress tick or new queued job must not override a deliberate history choice.
            center.Selected = oldest;
            center.UpdateSummary();
            Assert.Same(oldest, center.Selected);
            var later = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(target),
            });
            await Until(() => center.Jobs.Any(j => j.Job == later));
            Assert.Same(oldest, center.Selected);
            center.IsOpen = false;
            center.IsOpen = true;
            Assert.Same(running, center.Selected?.Job);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Removing_a_failed_report_requires_review_and_cancel_keeps_the_evidence()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string target = Directory.CreateDirectory(Path.Combine(root, "failed-target")).FullName;
            string missing = Path.Combine(root, "missing-source.txt");
            var center = vm.Operations;
            center.IsOpen = true;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(missing, EntryKind.File)],
                Destination = Location.FileSystem(target),
            });
            await Until(() => job.State.IsFinished() && center.Jobs.Any(j => j.Job == job));
            Assert.True(job.IssueTotals.Error > 0);
            Assert.False(job.CanUndo);
            center.Selected = center.Jobs.Single(j => j.Job == job);
            window.UpdateLayout();
            Click("Remove history row…");
            var dialogs = (FileCat.App.Views.OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen && window.GetVisualDescendants().OfType<Button>()
                .Any(b => b.Content as string == "Remove this history row"));
            window.UpdateLayout();
            string text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("1 operation with retained results or problems", text);
            Assert.Contains("Removing these rows also removes their reports", text);
            Click("Keep history");
            await Until(() => !dialogs.IsOpen);
            Assert.Contains(job, services.Jobs.Jobs);

            Click("Remove history row…");
            await Until(() => dialogs.IsOpen);
            Click("Remove this history row");
            await Until(() => !services.Jobs.Jobs.Contains(job));
            Assert.Empty(center.Jobs);
            Assert.False(File.Exists(missing));

            void Click(string label) => window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Selected_issue_list_appends_two_live_outcomes_without_reselection_or_reset()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "report-source")).FullName;
            string target = Directory.CreateDirectory(Path.Combine(root, "report-target")).FullName;
            foreach (string name in new[] { "one.txt", "two.txt" })
            {
                File.WriteAllText(Path.Combine(source, name), "new " + name);
                File.WriteAllText(Path.Combine(target, name), "old " + name);
            }
            var center = vm.Operations;
            center.IsOpen = true;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(Path.Combine(source, "one.txt"), EntryKind.File),
                    ItemRef.ForFileSystemPath(Path.Combine(source, "two.txt"), EntryKind.File)],
                Destination = Location.FileSystem(target),
            });
            await Until(() => job.State == JobState.AwaitingDecision && center.Jobs.Count == 1);
            center.Selected = center.Jobs[0];
            var selected = center.Selected;
            window.UpdateLayout();
            var issueList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, center.SelectedIssues));
            Assert.Empty(center.SelectedIssues);

            job.Decision!.Resolve(new Decision(DecisionAction.Skip));
            await Until(() => job.IssueCount == 1 && job.State == JobState.AwaitingDecision && center.SelectedIssues.Count == 1);
            var first = center.SelectedIssues[0];
            issueList.SelectedItem = first;
            var changes = new List<NotifyCollectionChangedAction>();
            center.SelectedIssues.CollectionChanged += (_, e) => changes.Add(e.Action);
            for (int i = 0; i < 20; i++) center.Refresh(job); // progress refreshes do not rebuild the report
            Assert.Empty(changes);

            job.Decision!.Resolve(new Decision(DecisionAction.Skip));
            await Until(() => job.State.IsFinished() && center.SelectedIssues.Count == 2);
            Assert.Equal(job.IssueCount, center.SelectedIssues.Count);
            Assert.Same(selected, center.Selected);
            Assert.Same(center.SelectedIssues, issueList.ItemsSource);
            Assert.Same(first, issueList.SelectedItem);
            Assert.Equal([NotifyCollectionChangedAction.Add], changes);
            center.IsOpen = false;
            center.IsOpen = true;
            Assert.Equal(2, center.SelectedIssues.Count);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Opening_a_completed_large_report_loads_bounded_batches_until_counts_match()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "many-source")).FullName;
            string target = Directory.CreateDirectory(Path.Combine(root, "many-target")).FullName;
            var items = new List<ItemRef>();
            for (int i = 0; i < 270; i++)
            {
                string name = $"item-{i:D3}.txt";
                File.WriteAllText(Path.Combine(source, name), "new");
                File.WriteAllText(Path.Combine(target, name), "old");
                items.Add(ItemRef.ForFileSystemPath(Path.Combine(source, name), EntryKind.File));
            }
            var center = vm.Operations;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = items,
                Destination = Location.FileSystem(target),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
            });
            await Until(() => job.State.IsFinished() && center.Jobs.Count == 1);
            Assert.Equal(270, job.IssueCount);
            center.IsOpen = true; // selects the row and reads only the first bounded slice synchronously
            Assert.Equal(256, center.SelectedIssues.Count);
            window.UpdateLayout();
            var issueList = window.GetVisualDescendants().OfType<ListBox>()
                .Single(list => ReferenceEquals(list.ItemsSource, center.SelectedIssues));
            var scroll = issueList.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Avalonia.Vector(0, 500);
            window.UpdateLayout();
            double scrollBefore = scroll.Offset.Y;
            Assert.True(scrollBefore > 0);
            await Until(() => center.SelectedIssues.Count == job.IssueCount);
            window.UpdateLayout();
            Assert.Equal(scrollBefore, scroll.Offset.Y, 1);
            Assert.Equal("item-000.txt", Path.GetFileName(center.SelectedIssues[0].Path));
            Assert.Equal("item-269.txt", Path.GetFileName(center.SelectedIssues[^1].Path));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>
    /// A job ends on its own thread; its row learns of it from a refresh queued on the window's thread. In between, the
    /// summary already counted the job as finished (it reads the job's state), so "Clear finished" was offered, and
    /// clearing went by the row's state, which still said running: the job stayed, and so did the offer.
    /// </summary>
    [AvaloniaFact]
    public async Task Clear_finished_removes_a_job_whose_row_has_not_caught_up_yet()
    {
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("fc-operation-center-").FullName;
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
            var center = new OperationCenterViewModel(jobs);
            string from = Directory.CreateDirectory(Path.Combine(root, "from")).FullName;
            string to = Directory.CreateDirectory(Path.Combine(root, "to")).FullName;
            File.WriteAllText(Path.Combine(from, "a.txt"), "new");
            File.WriteAllText(Path.Combine(to, "a.txt"), "old");
            // The copy stops at the name it would replace, so its row is there before it ends.
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(Path.Combine(from, "a.txt"), EntryKind.File)],
                Destination = Location.FileSystem(to),
            });
            for (int i = 0; i < 500 && !(job.State == JobState.AwaitingDecision && center.Jobs.Count == 1 && !center.Jobs[0].IsFinished); i++)
                await Task.Delay(20, ct);
            Assert.Equal(JobState.AwaitingDecision, job.State);
            Assert.Single(center.Jobs);

            // Skipped, it ends at once on its own thread, while this (the window's) thread is busy: the row's refresh waits.
            job.Decision!.Resolve(new Decision(DecisionAction.Skip));
            for (int i = 0; i < 1000 && !job.State.IsFinished(); i++) Thread.Sleep(5);
            Assert.True(job.State.IsFinished());
            center.UpdateSummary(); // as the window does when told the job finished, or a click in the panel
            Assert.True(center.HasFinished);
            center.RemoveFinished();
            Assert.Empty(center.Jobs);
            Assert.False(center.HasFinished);
            Assert.Empty(jobs.Jobs);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
