using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class OperationReportTests
{
    [AvaloniaFact]
    public async Task Finished_copy_shows_its_average_in_the_narrow_details_window_without_truncation()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 640, height: 420);
        try
        {
            string source = Path.Combine(root, "files", "transfer.bin");
            byte[] bytes = new byte[1 << 20];
            new Random(17).NextBytes(bytes);
            File.WriteAllBytes(source, bytes);
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination),
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!job.State.IsFinished() || vm.Operations.Jobs.All(row => row.Job != job))
                await Task.Delay(10, timeout.Token);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(destination, "transfer.bin")));
            vm.Operations.Refresh(job);
            vm.Operations.Selected = vm.Operations.Jobs.Single(row => row.Job == job);
            vm.Operations.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            string rate = Assert.IsType<string>(Formatters.AverageDataRate(job.DataBytesProcessed, job.ElapsedSinceStart));
            var detail = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Text == "Average transfer speed: " + rate);
            Assert.True(detail.IsEffectivelyVisible);
            Assert.Equal(TextWrapping.Wrap, detail.TextWrapping);
            Assert.Equal(detail.Text, AutomationProperties.GetName(detail));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Fact]
    public async Task Completed_copy_keeps_its_average_speed_while_rename_and_skip_claim_no_transfer()
    {
        string root = Directory.CreateTempSubdirectory("fc-average-rate-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            string moved = Directory.CreateDirectory(Path.Combine(root, "moved")).FullName;
            string copied = Directory.CreateDirectory(Path.Combine(root, "copied")).FullName;
            byte[] bytes = new byte[1 << 20];
            new Random(42).NextBytes(bytes);
            string sourcePath = Path.Combine(source, "payload.bin");
            File.WriteAllBytes(sourcePath, bytes);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var manager = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));

            var move = manager.Submit(new JobRequest
            {
                Kind = JobKind.Move,
                Sources = [ItemRef.ForFileSystemPath(sourcePath, EntryKind.File)],
                Destination = Location.FileSystem(moved),
            });
            await Finished(move);
            Assert.Equal(JobState.Completed, move.State);
            Assert.Equal(0, move.DataBytesProcessed);
            Assert.Contains("Average transfer speed: not applicable", OperationReport.ForClipboard(OperationReport.Capture(move)));
            var moveView = new JobViewModel(move);
            moveView.Refresh();
            Assert.Empty(moveView.SpeedText);

            string movedPath = Path.Combine(moved, "payload.bin");
            var copy = manager.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(movedPath, EntryKind.File)],
                Destination = Location.FileSystem(copied),
                Options = new TransferOptions { Verify = VerifyMode.ReadBack },
            });
            await Finished(copy);
            Assert.Equal(JobState.Completed, copy.State);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(copied, "payload.bin")));
            var snapshot = OperationReport.Capture(copy);
            Assert.True(snapshot.DataBytesProcessed > bytes.Length); // Includes the real verification reads.
            Assert.True(snapshot.ElapsedSinceStart > TimeSpan.Zero);
            string expectedRate = Assert.IsType<string>(Formatters.AverageDataRate(snapshot.DataBytesProcessed, snapshot.ElapsedSinceStart));
            string report = Assert.IsType<string>(OperationReport.ForClipboard(snapshot));
            Assert.Contains("Actual data processed: " + snapshot.DataBytesProcessed.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes", report);
            Assert.Contains("Average transfer speed: " + expectedRate, report);
            var copyView = new JobViewModel(copy);
            copyView.Refresh();
            Assert.Equal("Average transfer speed: " + expectedRate, copyView.SpeedText);
            await Task.Delay(30, TestContext.Current.CancellationToken);
            Assert.Equal(snapshot.ElapsedSinceStart, OperationReport.Capture(copy).ElapsedSinceStart);
            copyView.Refresh();
            Assert.Equal("Average transfer speed: " + expectedRate, copyView.SpeedText);

            manager.DecisionRequested += decision => decision.Resolve(new Decision(DecisionAction.Skip));
            var skipped = manager.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(movedPath, EntryKind.File)],
                Destination = Location.FileSystem(copied),
            });
            await Finished(skipped);
            Assert.Equal(1, skipped.ItemsSkipped);
            Assert.Equal(0, skipped.DataBytesProcessed);
            Assert.Contains("Average transfer speed: not applicable", OperationReport.ForClipboard(OperationReport.Capture(skipped)));
            var skippedView = new JobViewModel(skipped);
            skippedView.Refresh();
            Assert.Empty(skippedView.SpeedText);
        }
        finally { Directory.Delete(root, recursive: true); }

        static async Task Finished(Job job)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            while (!job.State.IsFinished()) await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public void Report_preserves_exact_paths_reasons_and_distinct_effects()
    {
        var job = new Job(new JobRequest { Kind = JobKind.Move }, "Move", "local", [], []);
        string source = "C:\\deep\\hidden\tname.txt";
        string target = "D:\\destination\\name.txt";
        string reason = "Could not remove source\r\nAccess denied";
        job.AddIssue(new JobIssue(IssueSeverity.Info, source, "Chosen conflict policy: Skip", StepOutcome.Skipped)
        {
            SourcePath = source, TargetPath = target, SourceIsLocal = false, TargetIsLocal = true, RequestedAction = "Move",
            CommittedEffect = "No transfer attempted for this item", SourceDisposition = "Original kept",
            TargetDisposition = "Existing destination kept", Certainty = "Confirmed before transfer",
        });
        job.AddIssue(new JobIssue(IssueSeverity.Error, source, "Copy failed", StepOutcome.Failed)
        {
            SourcePath = source, TargetPath = target, RequestedAction = "Move",
            CommittedEffect = "Copy failed; no published copy confirmed", SourceDisposition = "Original kept",
            TargetDisposition = "Target state not confirmed", Certainty = "Failure reported by copy",
        });
        job.AddIssue(new JobIssue(IssueSeverity.Warning, source, reason, StepOutcome.PartiallyApplied)
        {
            SourcePath = source, TargetPath = target, RequestedAction = "Move",
            CommittedEffect = "Copy published; source deletion refused", SourceDisposition = "Original kept",
            TargetDisposition = "Copy published", Certainty = "Observed at deletion check",
        });

        string report = Assert.IsType<string>(OperationReport.ForClipboard(OperationReport.Capture(job)));
        Assert.Contains("Issues: 3 total; 1 info; 1 warnings; 1 errors", report);
        Assert.Contains("Details: first 3 retained; 0 omitted", report);
        Assert.Contains("Source: " + JsonSerializer.Serialize(source), report);
        Assert.Contains("Target: " + JsonSerializer.Serialize(target), report);
        Assert.Contains("Source location: non-local provider", report);
        Assert.Contains("Target location: local filesystem", report);
        Assert.Contains("Reason: " + JsonSerializer.Serialize(reason), report);
        Assert.Contains("No transfer attempted for this item", report);
        Assert.Contains("Copy failed; no published copy confirmed", report);
        Assert.Contains("Copy published; source deletion refused", report);
        Assert.DoesNotContain(source, report); // hidden tab is escaped in the reversible report
        Assert.Null(OperationReport.ForClipboard(OperationReport.Capture(job), maximumCharacters: 50));
    }

    [Fact]
    public void Successful_command_output_is_visible_in_the_report_without_claiming_an_issue_or_full_log()
    {
        var job = new Job(new JobRequest { Kind = JobKind.ApplyCommand }, "Run a tool", "local", [], []);
        string path = "/owned/log one\t.txt";
        job.AddCommandResult(new JobCommandResult(path, 0, "hash=abc\n", "", false, false, false));
        for (int i = 1; i < 65; i++)
            job.AddCommandResult(new JobCommandResult($"/owned/file-{i}", 0, $"result-{i}", "", false, false, false));
        string report = Assert.IsType<string>(OperationReport.ForClipboard(OperationReport.Capture(job)));
        Assert.Contains("Issues: 0 total", report);
        Assert.Contains("Command results: first 64 retained; 1 omitted after the 64-result limit", report);
        Assert.Contains("at most 2,000 characters per stream and item", report);
        Assert.Contains("Path: " + JsonSerializer.Serialize(path), report);
        Assert.Contains("Standard output: " + JsonSerializer.Serialize("hash=abc\n"), report);
        Assert.Contains("result-63", report);
        Assert.DoesNotContain("result-64", report);
        Assert.DoesNotContain(path, report);
    }

    [Fact]
    public void Save_exports_every_retained_detail_and_keeps_existing_report_on_cancellation()
    {
        string root = Directory.CreateTempSubdirectory("fc-report-").FullName;
        try
        {
            string output = Path.Combine(root, "report.txt");
            File.WriteAllText(output, "previous report");
            var job = new Job(new JobRequest { Kind = JobKind.Copy }, "Copy", "local", [], []);
            for (int i = 0; i < 10_005; i++)
                job.AddIssue(new JobIssue(IssueSeverity.Warning, $"source-{i}", $"reason-{i}", StepOutcome.Skipped));
            var snapshot = OperationReport.Capture(job);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.Throws<OperationCanceledException>(() => OperationReport.SaveAtomic(output, snapshot, canceled.Token));
            Assert.Equal("previous report", File.ReadAllText(output));
            Assert.Empty(Directory.EnumerateFiles(root, ".filecat-report-*.tmp"));

            OperationReport.SaveAtomic(output, snapshot, CancellationToken.None);
            string report = File.ReadAllText(output);
            Assert.Contains("Issues: 10,005 total", report);
            Assert.Contains("Details: first 10,000 retained; 5 omitted", report);
            Assert.Contains("Reason: \"reason-9999\"", report);
            Assert.DoesNotContain("reason-10000", report);
            Assert.Empty(Directory.EnumerateFiles(root, ".filecat-report-*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Issue_detail_shows_full_path_and_copies_exact_path_reason_and_report()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string sourceDir = Directory.CreateDirectory(Path.Combine(root, "long source name")).FullName;
            string targetDir = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            string source = Path.Combine(sourceDir, "deep-file.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(Path.Combine(targetDir, "deep-file.txt"), "old");
            var center = vm.Operations;
            center.IsOpen = true;
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(targetDir),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
            });
            await Until(() => job.State.IsFinished() && center.Jobs.Count == 1);
            center.Selected = center.Jobs[0];
            await Until(() => center.SelectedIssues.Count == 1);
            var issue = center.SelectedIssues[0];
            Assert.Equal(source, issue.SourcePath);
            Assert.Equal(Path.Combine(targetDir, "deep-file.txt"), issue.TargetPath);
            Assert.Equal("No transfer attempted for this item", issue.CommittedEffect);
            Assert.Contains("first 1 of 1 issues", center.IssueSummary);
            center.SelectedIssue = issue;
            window.UpdateLayout();
            ButtonNamed("Issue details…").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialogs = (OverlayDialogService)vm.Dialogs;
            await Until(() => dialogs.IsOpen);
            window.UpdateLayout();
            var fields = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsReadOnly).ToList();
            Assert.Contains(fields, t => t.Text == source);
            Assert.Contains(fields, t => t.Text == issue.TargetPath);
            Assert.Contains(fields, t => t.Text == issue.Message);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => (t is SelectableTextBlock { Inlines: { } inlines }
                    ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : t.Text)?
                    .Contains("No transfer attempted for this item", StringComparison.Ordinal) == true);

            ButtonNamed("Copy exact source path").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilClipboard(source);
            ButtonNamed("Copy exact target path").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilClipboard(issue.TargetPath!);
            ButtonNamed("Copy exact reason").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilClipboard(issue.Message);
            ButtonNamed("Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !dialogs.IsOpen);
            ButtonNamed("Copy report").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilClipboardContains("Details: first 1 retained; 0 omitted");
            string report = (await ClipboardExtensions.TryGetTextAsync(window.Clipboard!))!;
            Assert.Contains("Source: " + JsonSerializer.Serialize(source), report);
            Assert.Contains("Existing destination kept", report);

            Button ButtonNamed(string label) => window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == label);
            async Task UntilClipboard(string expected)
            {
                for (int i = 0; i < 250; i++)
                {
                    if (await ClipboardExtensions.TryGetTextAsync(window.Clipboard!) == expected) return;
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }
                Assert.Equal(expected, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            }
            async Task UntilClipboardContains(string expected)
            {
                for (int i = 0; i < 250; i++)
                {
                    if ((await ClipboardExtensions.TryGetTextAsync(window.Clipboard!))?.Contains(expected, StringComparison.Ordinal) == true) return;
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }
                Assert.Contains(expected, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
