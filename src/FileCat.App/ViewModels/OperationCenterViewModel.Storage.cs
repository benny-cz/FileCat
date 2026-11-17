using FileCat.App.Services;
using FileCat.Core.Jobs;

namespace FileCat.App.ViewModels;

public sealed partial class JobViewModel
{
    // A streaming discovery has no denominator. Do not sample the transfer estimator or speed history here.
    private void RefreshStorage(StorageResultSnapshot? result)
    {
        var j = Job;
        StateText = j.State switch
        {
            JobState.Queued => j.WaitReason ?? "Queued",
            JobState.Pausing => "Pausing — waiting for the current call to finish",
            JobState.Paused => "Paused",
            JobState.Stopping => "Stopping — waiting for the current call to finish",
            JobState.AwaitingDecision => "Waiting for your decision",
            _ => j.State.Describe(),
        };
        CanUndo = CanShowUndoReport = CanRetryElevated = CanLimitSpeed = false;
        CanReviewStorage = IsFinished && result is not null && (result.ReviewPending || result.PersistenceFault || result.Counts.Uncertain > 0);
        CanCancel = !IsFinished;
        CancelTooltip = "Stops before the next call. An admitted call finishes; completed changes remain.";
        UndoStatus = string.Empty;
        IsIndeterminate = j.State == JobState.Running && !j.IsPaused;
        ShowsOverallProgress = IsIndeterminate;
        Percent = CurrentFilePercent = 0;
        PercentText = TimeLeftText = SpeedText = DataText = GraphCaption = string.Empty;
        ShowsFileProgress = HasSpeedHistory = false;
        SpeedHistory = [];
        HasIssues = result is not null && (result.HasIssues || result.PersistenceFault || result.ReviewPending ||
            result.ExecutionRefused || result.Discovery != StorageCompleteness.Complete);
        Severity = StorageOperationText.Severity(result, j.State);
        DetailText = result is null ? "Waiting for storage observations." : StorageOperationText.Details(result);
        ItemsText = result is null ? string.Empty : $"{result.Counts.Processed:N0} checked" +
            (result.Discovery == StorageCompleteness.UnknownRemainder ? "; folder contents are still being discovered" : "");
        PhaseText = IsFinished ? StateText : j.State == JobState.Running && j.Request.StorageTransform is { } request
            ? StorageOperationText.Action(request.Action) : StateText;
        string? path = result?.CurrentPath;
        CurrentFileText = path is null ? string.Empty : Formatters.SafeName(path);
        CurrentItem = path is null ? string.Empty : Formatters.SafeName(path.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? path);
        var elapsed = j.ElapsedSinceStart;
        ElapsedText = j.StartedUtc is null ? string.Empty : elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min"
            : elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s" : $"{Math.Max(0, elapsed.Seconds)} s";
        TimingText = ElapsedText.Length == 0 ? string.Empty : (IsFinished ? "Took " : "Running for ") + ElapsedText;
        PhaseAnnouncement = string.Empty;
    }
}
