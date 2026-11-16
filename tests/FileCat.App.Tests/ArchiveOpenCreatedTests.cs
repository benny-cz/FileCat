using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Archives;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

/// <summary>Plan §11.2 step 7: "Open archive" is an explicit action after a creation finished.</summary>
public sealed class ArchiveOpenCreatedTests
{
    [AvaloniaFact]
    public async Task A_finished_creation_offers_Open_archive_which_shows_the_new_archive()
    {
        using var f = await ArchiveCreationWorkflowTests.Fixture.Open();
        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Tar);
        await Until(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack"); await f.Complete(operation);
        var job = Assert.Single(f.Services.Jobs.Jobs); await Until(() => job.State.IsFinished());
        var row = new JobViewModel(job); row.Refresh();
        Assert.True(row.CanOpenArchive);
        await f.Vm.OpenCreatedArchiveAsync(job);
        Assert.Equal(FileCat.Archives.ArchiveProvider.ForFile(job.Request.Archive!.ArchivePath), f.Vm.ActiveTab!.Location);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Only_finished_creations_offer_Open_archive(bool update, bool expected)
    {
        var plan = new ArchivePlan(Path.Combine(Path.GetTempPath(), "never-created.tar"), update ? ArchiveBaseline.Of(typeof(ArchiveOpenCreatedTests).Assembly.Location) : null, [],
            Profile: new ArchiveWriteProfile(ArchiveWriteFormat.Tar));
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "archive", "local", [], []);
        var row = new JobViewModel(job);
        job.SetState(JobState.Running); row.Refresh(); Assert.False(row.CanOpenArchive);
        job.SetState(JobState.Completed); row.Refresh(); Assert.Equal(expected, row.CanOpenArchive);
    }

    private static async Task Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(); await Task.Delay(10); }
    }
}
