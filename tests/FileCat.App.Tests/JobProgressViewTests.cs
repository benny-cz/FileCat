using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>Release issues I26 and I30: what the Operations view shows while a job works.</summary>
public sealed class JobProgressViewTests
{
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
            for (int i = 0; i < 100 && vm.Operations.TaskbarState != Platform.Windows.TaskbarProgressState.Normal; i++) await Task.Delay(20, ct);
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
            // Release issue I30: what it does now, and the large file's own step: copying it, then reading it back.
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
