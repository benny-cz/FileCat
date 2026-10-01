using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>The operations panel's count of finished jobs and "Clear finished" agree (CI run 36931084621).</summary>
public sealed class OperationCenterTests
{
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
