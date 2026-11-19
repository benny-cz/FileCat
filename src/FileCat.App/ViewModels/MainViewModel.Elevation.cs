using System.ComponentModel;
using System.Diagnostics;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _openingAdministratorWindow;
    private readonly HashSet<Job> _requestedAdministratorFileRetries = [];
    private readonly HashSet<Job> _startingAdministratorFileRetries = [];
    private readonly Dictionary<Job, Job> _brokerFileRetries = [];
    internal Action<ProcessStartInfo, bool> LaunchAdministratorWindow { get; set; } = AdministratorWindow.Start;
    internal Func<ProcessStartInfo, CancellationToken, Task> LaunchAdministratorFileRetry { get; set; } = AdministratorWindow.StartFileRetry;

    private async Task<bool> OpenAdministratorWindowAsync(Location? journal = null, (string Folder, string Volume)? folder = null,
        AdministratorLinkDraft? linkDraft = null, AdministratorTask? task = null, Job? retryJob = null)
    {
        if (!OperatingSystem.IsWindows() || Services.Shell.IsElevated || _openingAdministratorWindow) return false;
        _openingAdministratorWindow = true;
        try
        {
            const string title = "Open FileCat as administrator";
            string reason = journal is not null ? $"Reading the change journal on {Formatters.SafeName(journal.Path)} requires administrator rights. "
                : folder is { } denied ? $"Reopen {Formatters.SafeName(denied.Folder)} with administrator rights? " : "";
            if (linkDraft is null && task is null && !await Dialogs.ConfirmAsync(title, reason +
                "Windows will ask for approval to open a separate FileCat window with full administrator rights. Your current window stays open.",
                "Open as administrator", danger: true)) return false;

            if (retryJob is not null)
            {
                var review = await Task.Run(() => AdministratorFileRetryReview.Start(task!, Services.Paths.ElevationExchangeDirectory,
                    LaunchAdministratorFileRetry, Services.Io.StoppingToken));
                retryJob.MarkRetriedAsAdministrator();
                _ = RestoreDeclinedFileRetryAsync(retryJob, review);
                return true;
            }

            await Task.Run(() =>
            {
                using var exchange = linkDraft?.Stage(Services.Paths.ElevationExchangeDirectory) ?? task?.Stage(Services.Paths.ElevationExchangeDirectory);
                (string, string)? payload = exchange is null ? null : (exchange.VolumePlanPath, exchange.Hash);
                LaunchAdministratorWindow(AdministratorWindow.CreateStartInfo(journal, folder,
                    linkDraft is not null ? payload : null, task is not null ? payload : null), exchange is not null);
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Notify("Administrator approval was canceled.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
                                   or NotSupportedException or ArgumentException)
        {
            await Dialogs.AlertAsync("Could not open FileCat as administrator", ex.Message);
        }
        finally { _openingAdministratorWindow = false; }
        return false;
    }

    private async Task RestoreDeclinedFileRetryAsync(Job job, AdministratorFileRetryReview review)
    {
        if (await review.Declined) job.AllowAdministratorRetry();
    }

    internal Task ReopenDeniedFolderAsAdministratorAsync(TabViewModel tab) =>
        tab.Location is { IsFileSystem: true } folder && tab.DeniedFolderVolume is { } volume && !Dialogs.IsOpen
            ? OpenAdministratorWindowAsync(folder: (folder.Path, volume)) : Task.CompletedTask;

    private void OpenAdministratorFolder(string folder, string volume)
    {
        if (!OperatingSystem.IsWindows() || !Services.Shell.IsElevated || Workspace.ActivePanel is not { } panel) return;
        if (string.Equals(AdministratorWindow.LocalVolume(folder), volume, StringComparison.OrdinalIgnoreCase))
            panel.OpenTab(Location.FileSystem(folder));
        else
            Notify("Windows could not find the original local drive for this folder. Open it again from the ordinary window.", true);
    }

    /// <summary>
    /// Retries the items of a finished operation that failed for lack of permission through the per-plan
    /// administrator broker (ADR-14): one UAC approval, then the broker's own window shows exactly these steps.
    /// The existing FileCat window keeps its original rights (AI-05).
    /// </summary>
    public async Task RetryElevatedAsync(Job job)
    {
        if (!CanRequestAdministratorTask) return;
        if (AdministratorFileRetry.IsSupportedKind(job.Kind))
        {
            await RetryFileOperationAsync(job);
            return;
        }
        if (OffersAdministratorWindowRetry(job))
        {
            var request = job.Request;
            var administratorTask = request.Kind == JobKind.CreateFile
                ? new AdministratorTask(AdministratorTaskKind.CreateFile) { Paths = [request.Destination!.Path], Names = [request.NewName!] }
                : new AdministratorTask(AdministratorTaskKind.Attributes)
                {
                    Paths = job.FailedRootIndices.Select(index => request.Sources[index].FileSystemPath!).ToArray(),
                    Attributes = request.Attributes,
                };
            if (await RequestAdministratorTaskAsync(administratorTask, "Review as administrator",
                "Review the selected operation in the administrator window before applying it."))
                job.MarkRetriedAsAdministrator();
            return;
        }
        if (!ElevationPlanBuilder.OffersRetry(job)) return;
        if (ElevationBroker.Locate(Services.Paths.IsPortable, out var reason) is null)
        {
            await Dialogs.AlertAsync("Administrator retry is not available", reason!);
            return;
        }
        ElevationRetry retry;
        try { retry = await Task.Run(() => ElevationPlanBuilder.Build(job, Environment.ProcessId)); }
        catch (Exception ex) when (ElevationPlanBuilder.IsExplainable(ex))
        {
            await Dialogs.AlertAsync("Cannot retry as administrator", ex.Message);
            return;
        }
        job.MarkRetriedAsAdministrator();
        var elevated = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Elevated,
            Elevation = retry.Plan,
            Sources = retry.Sources,
            Destination = retry.Destination,
            Description = "As administrator: " + retry.Plan.Title,
        });
        if (ActiveTab is { } tab) Track(elevated, tab);
        Notify("Windows asks for administrator approval; then the FileCat administrator window shows the exact steps for you to approve.");
    }

    internal bool CanDeferAdministratorFileRetry(Job job, ErrorRequest error) => CanRequestAdministratorTask &&
        error.ErrorClass == "access" && AdministratorFileRetry.CanReview(job.Request) && AdministratorWindow.LocalVolume(error.Path) is not null;

    internal void DeferAdministratorFileRetry(Job job) => _requestedAdministratorFileRetries.Add(job);

    private void OfferDeferredAdministratorFileRetry(Job job)
    {
        if (_brokerFileRetries.Remove(job, out var original) && job.State == JobState.Canceled && job.ItemsDone == 0 && job.ChangedIssueCount == 0)
            original.AllowAdministratorRetry();
        if (!_requestedAdministratorFileRetries.Remove(job) || job.State == JobState.Canceled) return;
        if (!AdministratorFileRetry.HasDeniedWork(job))
        {
            Notify("The operation has no untouched access-denied items to retry. Review the results in Operations (Ctrl+J).", true);
            return;
        }
        if (Dialogs.IsOpen || _exitWhenIdle)
        {
            Notify("The access-denied items are ready for administrator review in Operations (Ctrl+J).");
            return;
        }
        _ = RetryFileOperationAsync(job);
    }

    private async Task RetryFileOperationAsync(Job job)
    {
        if (!AdministratorFileRetry.HasDeniedWork(job) || !_startingAdministratorFileRetries.Add(job)) return;
        try
        {
            var (draft, plan) = await Task.Run(() =>
            {
                var draft = AdministratorFileRetry.Capture(job);
                _ = AdministratorWindow.CaptureVolumes(draft.Paths);
                var plan = ElevationBroker.Locate(Services.Paths.IsPortable, out _) is not null ? draft.TryCreateBrokerPlan() : null;
                return (draft, plan);
            });
            if (Dialogs.IsOpen)
            {
                Notify("The access-denied items are ready for administrator review in Operations (Ctrl+J).");
                return;
            }
            if (plan is not null)
            {
                var sources = job.Kind is JobKind.Copy or JobKind.Move
                    ? job.AccessDeniedRoots.Select(index => job.Request.Sources[index]).ToArray() : job.Request.Sources;
                var elevated = Services.Jobs.Submit(new JobRequest { Kind = JobKind.Elevated, Elevation = plan,
                    Sources = sources, Destination = job.Request.Destination, Description = plan.Title });
                _brokerFileRetries.Add(elevated, job);
                job.MarkRetriedAsAdministrator();
                if (ActiveTab is { } tab) Track(elevated, tab);
                Notify("Windows will ask for approval, then the FileCat helper will show the remaining items for review.");
                return;
            }
            await OpenAdministratorWindowAsync(task: new(AdministratorTaskKind.FileRetry) { Paths = draft.Paths, FileRetry = draft }, retryJob: job);
        }
        catch (Exception ex) when (ElevationPlanBuilder.IsExplainable(ex))
        {
            await Dialogs.AlertAsync("Cannot retry as administrator", Formatters.SafeName(ex.Message));
        }
        finally { _startingAdministratorFileRetries.Remove(job); }
    }

    internal static bool OffersAdministratorWindowRetry(Job job) =>
        job.State.IsFinished() && !job.RetriedAsAdministrator && job.Issues.Any(issue => issue.Cause is "access" or "privilege") &&
        (job.Request.Kind == JobKind.CreateFile && job.Request.Destination is { IsFileSystem: true } &&
            job.Request.NewName is { Length: > 0 } ||
         job.Request.Kind == JobKind.Attributes && job.Request.Attributes is { ChangesPermissions: false } change &&
            (change.Recursive || change.ModifiedUtc is not null || change.CreatedUtc is not null) &&
            job.FailedRootIndices.Count > 0 && job.FailedRootIndices.All(index =>
                index < job.Request.Sources.Count && job.Request.Sources[index].FileSystemPath is not null));
}
