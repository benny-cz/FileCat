using FileCat.App.ViewModels;
using Avalonia.Headless.XUnit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>Release issues I26 and I30: what the Operations view shows while a job works.</summary>
public sealed class JobProgressViewTests
{
    private static Job DisplayJob(JobKind kind = JobKind.Copy) => new(new JobRequest { Kind = kind }, kind.ToString(), "local", [], []);

    [Theory]
    [InlineData(JobState.Queued)]
    [InlineData(JobState.Running)]
    [InlineData(JobState.AwaitingDecision)]
    [InlineData(JobState.Pausing)]
    [InlineData(JobState.Paused)]
    [InlineData(JobState.Stopping)]
    [InlineData(JobState.Reconciliation)]
    public void Every_pending_state_keeps_the_operation_bar_below_100_percent(JobState pending)
    {
        var job = DisplayJob();
        job.AddTotals(1, 20L << 20);
        job.TotalsFinal = true;
        job.BeginItem(20L << 20, 0);
        job.SetCurrent("last.bin");
        job.AddBytes(20L << 20); // all measured bytes are accounted for, but the operation has not ended
        job.ItemDone();
        job.SetState(pending);

        var view = new JobViewModel(job);
        view.Refresh();
        Assert.InRange(view.Percent, 0, 99);
        Assert.NotEqual("100%", view.PercentText);
        Assert.True(OperationCenterViewModel.TaskbarFor(view, view).Fraction < 1);
    }

    [Fact]
    public void Current_file_bar_stays_below_100_until_the_file_step_ends()
    {
        var job = DisplayJob();
        job.AddTotals(1, 20L << 20);
        job.BeginItem(20L << 20, 0);
        job.SetCurrent("last.bin");
        job.AddBytes(20L << 20);
        job.TotalsFinal = true;
        job.SetState(JobState.Running);

        var view = new JobViewModel(job);
        view.Refresh();
        Assert.True(view.ShowsFileProgress);
        Assert.InRange(view.CurrentFilePercent, 0, 98);
        Assert.InRange(view.Percent, 0, 99);
        Assert.NotEqual("100%", view.PercentText);
    }

    [AvaloniaFact]
    public void Compact_strip_prefers_pending_work_over_completed_history()
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.GetTempPath());
        var center = new OperationCenterViewModel(manager);
        var finished = DisplayJob();
        finished.SetState(JobState.Completed);
        var pending = DisplayJob();
        pending.SetState(JobState.Running);
        var finishedView = new JobViewModel(finished);
        var pendingView = new JobViewModel(pending);
        finishedView.Refresh();
        pendingView.Refresh();
        center.Jobs.Add(finishedView);
        center.Jobs.Add(pendingView);

        center.UpdateSummary();
        Assert.Same(pending, center.Primary?.Job);
        Assert.True(Assert.IsType<JobViewModel>(center.Primary).Percent < 100);
        Assert.True(center.TaskbarFraction < 1);
    }

    [Fact]
    public void Requested_pause_stays_pausing_until_the_worker_acknowledges_it()
    {
        var job = DisplayJob();
        job.SetState(JobState.Running);
        job.AddTotals(4, 400);
        var view = new JobViewModel(job);
        job.Pause();
        Assert.Equal(JobState.Pausing, job.State);
        view.Refresh();
        Assert.Equal("Pausing — waiting for a safe point", view.StateText);
        Assert.Equal("Pausing — waiting for a safe point", view.PhaseText);
        Assert.Equal("Keep running", view.ResumeLabel);
        Assert.True(view.CanResume);
        Assert.False(view.CanPause);
        Assert.Equal(Platform.Windows.TaskbarProgressState.Normal, OperationCenterViewModel.TaskbarFor(view, view).State);

        job.SetState(JobState.Paused); // worker reached its safe checkpoint
        view.Refresh();
        Assert.Equal("Paused", view.PhaseText);
        Assert.Equal("Resume", view.ResumeLabel);
        Assert.Equal(Platform.Windows.TaskbarProgressState.Paused, OperationCenterViewModel.TaskbarFor(view, view).State);
        job.Resume();
        view.Refresh();
        Assert.Equal(JobState.Running, job.State);
        Assert.True(view.CanPause);
    }

    [Theory]
    [InlineData(JobState.Canceled)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Interrupted)]
    public void Stopped_jobs_keep_the_settled_fraction_and_name_unprocessed_work(JobState final)
    {
        var job = DisplayJob();
        job.SetState(JobState.Running);
        job.AddTotals(4, 400);
        job.AddBytes(100); // logical credit is not proof that bytes were copied
        job.ItemDone();
        job.ItemFailed();
        job.TotalsFinal = final != JobState.Canceled;
        job.SetState(final);
        var view = new JobViewModel(job);
        view.Refresh();

        Assert.True(view.Percent < 100);
        Assert.Equal(string.Empty, view.PercentText);
        Assert.Contains("1 done, 0 skipped, 1 failed", view.DetailText);
        Assert.Contains(final == JobState.Canceled ? "remaining scope not fully counted" : "2 not processed", view.DetailText);
        Assert.Contains("planned bytes accounted for", view.DataText);
        Assert.DoesNotContain("copied or read", view.DataText);
        Assert.Contains(final == JobState.Canceled ? "4+ items" : "4 items", view.ItemsText);
        Assert.True(OperationCenterViewModel.TaskbarFor(null, view).Fraction < 1);
    }

    [Fact]
    public void Completed_with_issues_shows_outcome_counts_without_a_success_percentage_claim()
    {
        var job = DisplayJob();
        job.SetState(JobState.Running);
        job.AddTotals(2, 20);
        job.AddBytes(20); // logical skip credit must not imply physical transfer
        job.ItemDone();
        job.ItemSkipped();
        job.TotalsFinal = true;
        job.SetState(JobState.CompletedWithIssues);
        var view = new JobViewModel(job);
        view.Refresh();

        Assert.Equal(100, view.Percent); // all planned work was settled
        Assert.Equal(string.Empty, view.PercentText);
        Assert.Equal("Completed with issues", view.PhaseText);
        Assert.Contains("1 done, 1 skipped", view.DetailText);
        Assert.Contains("1 skipped", view.ItemsText);
        Assert.Contains("planned bytes accounted for", view.DataText);
        Assert.DoesNotContain("copied or read", view.DataText);
        Assert.False(view.HasSpeedHistory);
    }

    [Fact]
    public void Same_volume_move_credit_is_accounted_for_without_claiming_copied_data_or_speed()
    {
        var job = DisplayJob(JobKind.Move);
        job.SetState(JobState.Running);
        job.AddTotals(1, 1024);
        job.AddBytes(1024); // one rename can settle logical work without transferring data
        job.ItemDone();
        job.TotalsFinal = true;
        job.SetState(JobState.Completed);
        var view = new JobViewModel(job);
        view.Refresh();

        Assert.Equal("100%", view.PercentText);
        Assert.Contains("planned bytes accounted for", view.DetailText);
        Assert.Contains("planned bytes accounted for", view.DataText);
        Assert.DoesNotContain("copied or read", view.DataText);
        Assert.False(view.HasSpeedHistory);
    }

    [Fact]
    public async Task A_worker_changing_files_during_refresh_keeps_the_displayed_name_and_phase_together()
    {
        var job = DisplayJob();
        job.SetState(JobState.Running);
        job.SetCurrent("b.bin");
        var view = new JobViewModel(job);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var worker = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                job.SetCurrent("long-file.bin (verifying)");
                job.SetCurrent("b.bin");
            }
        }, TestContext.Current.CancellationToken);
        try
        {
            for (int i = 0; i < 20_000; i++)
            {
                view.Refresh();
                Assert.Contains(view.CurrentFileText, new[] { "long-file.bin", "b.bin" });
            }
        }
        finally
        {
            stop.Cancel();
            await worker;
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Opening_the_details_shows_the_running_operation()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string file = Path.Combine(root, "slow.bin");
            File.WriteAllBytes(file, new byte[32 << 20]);
            var job = services.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
                Destination = Location.FileSystem(Directory.CreateDirectory(Path.Combine(root, "to")).FullName),
                Options = new TransferOptions { RateLimit = 4 << 20 }, // eight seconds: still running when the details open
            });
            for (int i = 0; i < 250 && vm.Operations.Primary is null; i++) await Task.Delay(20, ct);
            Assert.Same(job, vm.Operations.Primary?.Job);
            Assert.Null(vm.Operations.Selected);
            vm.Operations.IsOpen = true;
            Assert.Same(vm.Operations.Primary, vm.Operations.Selected);
            // The taskbar button follows it: its bar while it runs, yellow while it is paused.
            // Indeterminate until the job has counted its work: as long as the other states get (CI, run 36801942257).
            for (int i = 0; i < 250 && vm.Operations.TaskbarState != Platform.Windows.TaskbarProgressState.Normal; i++) await Task.Delay(20, ct);
            Assert.Equal(Platform.Windows.TaskbarProgressState.Normal, vm.Operations.TaskbarState);
            job.Pause();
            for (int i = 0; i < 250 && vm.Operations.TaskbarState != Platform.Windows.TaskbarProgressState.Paused; i++) await Task.Delay(20, ct);
            Assert.Equal(Platform.Windows.TaskbarProgressState.Paused, vm.Operations.TaskbarState);
            Assert.InRange(vm.Operations.TaskbarFraction, 0, 0.99);
            job.Cancel();
            for (int i = 0; i < 250 && vm.Operations.TaskbarState != Platform.Windows.TaskbarProgressState.None; i++) await Task.Delay(20, ct);
            Assert.Equal(Platform.Windows.TaskbarProgressState.None, vm.Operations.TaskbarState);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [Fact]
    public async Task A_verified_copy_never_shows_100_percent_before_it_ends()
    {
        string root = Directory.CreateTempSubdirectory("fc-progress-view-").FullName;
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
            string source = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            string file = Path.Combine(source, "big.bin");
            using (var fs = File.Create(file))
            {
                var block = new byte[1 << 20];
                new Random(2).NextBytes(block);
                for (int i = 0; i < 384; i++) fs.Write(block);
            }
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
                Destination = Location.FileSystem(Directory.CreateDirectory(Path.Combine(root, "dst")).FullName),
                Options = new TransferOptions { Verify = VerifyMode.ReadBack },
            });
            var view = new JobViewModel(job);
            var seen = new List<(double Percent, bool Active, string Detail, string Phase, string File, string PercentText, string Timing)>();
            while (!job.State.IsFinished())
            {
                view.Refresh();
                seen.Add((view.Percent, view.IsActive, view.DetailText, view.PhaseText, view.CurrentFileText, view.PercentText, view.TimingText));
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            view.Refresh();
            Assert.Equal(JobState.Completed, job.State);
            Assert.All(seen.Where(s => s.Active), s => Assert.True(s.Percent < 100, $"{s.Percent}% shown while working ({s.Detail})."));
            Assert.All(seen.Where(s => s.Active), s => Assert.NotEqual("100%", s.PercentText));
            // Reading back is most of this job: when the copy's last byte is written, the bar is near a third, not full.
            Assert.Contains(seen, s => s.Active && s.Percent is > 20 and < 60);
            Assert.Contains(seen, s => s.Detail.Contains("estimating the time left", StringComparison.Ordinal));
            Assert.Equal(100, view.Percent);
            // Release issue I30: what it does now, and the large file's own step: copying it, then reading it back. (On
            // macOS the file system may clone the file at once, and the copy is over before anything samples it.)
            if (seen.Any(s => s.Active && s.Percent is > 0 and < 33))
                Assert.Contains(seen, s => s.Phase == "Copying" && s.File.StartsWith("big.bin · ", StringComparison.Ordinal) && s.File.EndsWith("of 384 MB", StringComparison.Ordinal));
            Assert.Contains(seen, s => s.Phase == "Verifying" && s.File.StartsWith("big.bin · verifying, ", StringComparison.Ordinal));
            Assert.Contains(seen, s => s.Timing.Contains("running for", StringComparison.Ordinal));
            Assert.Equal("100%", view.PercentText);
            Assert.StartsWith("Took ", view.TimingText, StringComparison.Ordinal);
            Assert.Equal("1 of 1 items", view.ItemsText);
            Assert.Contains("verified 384 MB of 384 MB", view.DataText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
