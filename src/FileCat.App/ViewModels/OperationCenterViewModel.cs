using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using TaskbarProgressState = FileCat.Platform.Windows.TaskbarProgressState;

namespace FileCat.App.ViewModels;

/// <summary>One job as shown in the operation center. Progress is polled at a bounded rate.</summary>
public sealed partial class JobViewModel : ObservableObject
{
    public JobViewModel(Job job, Func<Location, string>? display = null, SyncGroup? syncGroup = null)
    {
        Job = job;
        SyncGroup = syncGroup;
        Route = DescribeRoute(job.Request, display ?? (l => l.IsFileSystem ? l.Path : l.ToString()));
    }

    public Job Job { get; }
    public SyncGroup? SyncGroup { get; }

    /// <summary>
    /// What a screen reader says for the job's row in Operations: the operation and where it stands ("Copy "d.bin" to
    /// C:\…, Waiting for other operations on C:"). A row takes its name from this, and was read out as its class name.
    /// </summary>
    public override string ToString() => string.IsNullOrEmpty(StateText) ? Title : $"{Title}, {StateText}";

    public bool IsStorage => Job.Kind == JobKind.StorageTransform;
    public string CancelLabel => IsStorage ? "Stop" : SyncGroup is null ? "Cancel" : "Cancel this step";
    /// <summary>The plan's publication boundary (§11.5): no Cancel while an archive update is being saved and finished.</summary>
    public const string FinishingSafelyHelp = "Finishing this archive update safely.";
    [ObservableProperty] private bool _canCancel = true;
    [ObservableProperty] private string _cancelTooltip = DefaultCancelTooltip(null);
    /// <summary>Spoken once per archive phase change (plan §11.5); never per member.</summary>
    [ObservableProperty] private string _phaseAnnouncement = string.Empty;
    private ArchiveJobPhase? _announcedPhase;
    private static string DefaultCancelTooltip(SyncGroup? group) => group is null
        ? "Stops at the next safe step; completed steps are kept"
        : "Cancels only this step. Other synchronization steps stay visible; pending removals wait for review if a transfer stops.";
    internal static string ArchivePhaseText(ArchiveJobPhase phase) => phase switch
    {
        ArchiveJobPhase.Preparing => "Preparing",
        ArchiveJobPhase.ReadingArchive => "Reading archive",
        ArchiveJobPhase.Rebuilding => "Rebuilding",
        ArchiveJobPhase.Checking => "Checking",
        ArchiveJobPhase.Saving => "Saving",
        _ => "Finishing safely",
    };

    /// <summary>The progress shown and the time left (release issue I26), on this view's own clock.</summary>
    private readonly ProgressEstimator _progress = new();
    private readonly long _clockStart = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>Where the items come from and where they go (plan §9: source and destination stay clear).</summary>
    public string Route { get; }

    private static string DescribeRoute(JobRequest r, Func<Location, string> display)
    {
        if (r.UndoPlan is { } inverse) return $"Reversing {inverse.Steps.Count:N0} recorded actions from {Formatters.SafeName(inverse.OriginalTitle)}";
        string? from = null;
        try
        {
            // A captured listing answers without enumerating; more than three folders are summarized.
            var parents = ItemSources.Parents(r.Sources, 3);
            from = parents is null ? "many folders" : parents.Count switch { 0 => null, 1 => display(parents.First()), _ => $"{parents.Count} folders" };
        }
        catch (ObjectDisposedException)
        {
            // Sources released already: the title still names the operation.
        }
        var to = r.Destination is { } d ? display(d) : null;
        return (from, to) switch
        {
            ({ } f, { } t) => $"{f} → {t}",
            ({ } f, null) => f,
            (null, { } t) => "→ " + t,
            _ => string.Empty,
        };
    }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private string _detailText = string.Empty;
    [ObservableProperty] private string _currentItem = string.Empty;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsHistoryProgress))] private bool _isActive;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsHistoryProgress))] private bool _showsOverallProgress = true;
    public bool ShowsHistoryProgress => IsActive && ShowsOverallProgress;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private bool _isQueued;
    [ObservableProperty] private bool _canMoveUpInQueue;
    [ObservableProperty] private bool _canLimitSpeed;
    [ObservableProperty] private string _moveUpReason = string.Empty;
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;
    [ObservableProperty] private string _resumeLabel = "Resume";
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canShowUndoReport;
    [ObservableProperty] private bool _canReviewStorage;
    [ObservableProperty] private string _undoStatus = string.Empty;
    [ObservableProperty] private bool _canRetryElevated;
    /// <summary>A finished check of checksums and signatures whose results are still kept (D-57): they can be shown again.</summary>
    [ObservableProperty] private bool _canShowResults;
    /// <summary>Plan §11.2 step 7: opening a newly created archive is an explicit action after it finished.</summary>
    [ObservableProperty] private bool _canOpenArchive;
    [ObservableProperty] private bool _hasIssues;
    [ObservableProperty] private bool _needsDecision;
    [ObservableProperty] private string _severity = "info";

    // What a running operation shows beyond one line (release issue I30): how far in words, what it does now, the file in
    // hand, how long it has run, and its pace over the last minute and a half.
    [ObservableProperty] private string _percentText = string.Empty;
    [ObservableProperty] private string _phaseText = string.Empty;
    [ObservableProperty] private string _currentFileText = string.Empty;
    [ObservableProperty] private double _currentFilePercent;
    [ObservableProperty] private bool _showsFileProgress;
    [ObservableProperty] private string _timeLeftText = string.Empty;
    [ObservableProperty] private string _elapsedText = string.Empty;
    /// <summary>"Time left: about 25 s · running for 10 s" while it runs, "Took 15 s" once done.</summary>
    [ObservableProperty] private string _timingText = string.Empty;
    [ObservableProperty] private bool _hasSpeedHistory;
    [ObservableProperty] private string _itemsText = string.Empty;
    [ObservableProperty] private string _dataText = string.Empty;
    [ObservableProperty] private string _speedText = string.Empty;
    /// <summary>The last 90 seconds of actual data or completed items: X is elapsed seconds, Y units per second.</summary>
    [ObservableProperty] private Avalonia.Point[] _speedHistory = [];
    [ObservableProperty] private string _graphCaption = string.Empty;
    private readonly OperationSpeedHistory _speedHistorySamples = new();
    private bool MeasuresItems => Job.Kind is JobKind.Delete or JobKind.Recycle or JobKind.CreateDirectory or
        JobKind.CreateFile or JobKind.Rename or JobKind.Attributes or JobKind.Registry or JobKind.CreateLink or JobKind.ApplyCommand or JobKind.Undo;

    /// <summary>A large file gets a bar of its own; a small one is done before a bar would help.</summary>
    private const long OwnBarFrom = 16L << 20;

    public void Refresh()
    {
        var j = Job;
        // The worker can finish during this refresh. Keep the active flag and the 100% decision
        // on the same lifecycle snapshot so a still-active view never briefly shows 100%.
        JobState lifecycleState = j.State;
        // A title and a summary name files (a verification's names its checksum file and who signed it): escaped as names are (§18.3).
        Title = Formatters.SafeName(j.Title);
        IsFinished = lifecycleState.IsFinished();
        IsQueued = lifecycleState == JobState.Queued;
        IsActive = lifecycleState.IsActive();
        CanLimitSpeed = !IsFinished && JobExecutors.MayThrottle(j.Request);
        NeedsDecision = j.State == JobState.AwaitingDecision;
        CanPause = j.State is JobState.Running or JobState.Queued && !j.IsPaused;
        CanResume = j.IsPaused || j.State is JobState.Paused or JobState.Pausing;
        ResumeLabel = j.State == JobState.Pausing ? "Keep running" : "Resume";
        CanUndo = j.CanUndo;
        CanShowUndoReport = IsFinished && j.UndoReport is not null;
        UndoStatus = j.UndoState switch
        {
            UndoAttemptState.Reviewing => "Undo is being reviewed",
            UndoAttemptState.Attempting => "Undo is running as a separate operation",
            UndoAttemptState.Consumed => "Undo attempt consumed; its result is on the inverse operation",
            _ => string.Empty,
        };
        CanRetryElevated = !IsStorage && OperatingSystem.IsWindows() && !Core.State.WindowsTokenAuthority.IsHighIntegrity &&
            (Services.AdministratorFileRetry.IsSupportedKind(j.Kind) ? Services.AdministratorFileRetry.HasDeniedWork(j) :
            MainViewModel.OffersAdministratorWindowRetry(j) || Platform.Windows.Elevation.ElevationPlanBuilder.OffersRetry(j));
        CanShowResults = IsFinished && j.Kind is (JobKind.VerifyChecksums or JobKind.VerifyBeside) && j.VerificationReport is not null;
        // State only: whether the file is still there is checked when the action runs, never on this refresh path.
        CanOpenArchive = j.State is JobState.Completed or JobState.CompletedWithIssues && j.Kind == JobKind.ArchiveUpdate &&
            j.Request.Archive is { Baseline: null, NativeBaseline: null, MoveSources: false };
        int issues = j.IssueCount;
        HasIssues = issues > 0;
        Severity = j.State == JobState.Failed || j.ItemsFailed > 0 ? "error" : j.State == JobState.CompletedWithIssues ? "warning" : j.State == JobState.Completed ? "ok" : "info";

        if (IsStorage)
        {
            RefreshStorage(j.StorageResult);
            return;
        }
        ShowsOverallProgress = true;

        long bt = j.BytesTotal, bd = j.BytesDone, it = j.ItemsTotal, id = j.ItemsDone + j.ItemsSkipped + j.ItemsFailed;
        IsIndeterminate = IsActive && !j.TotalsFinal && bt == 0 && it == 0;
        // The fraction tracks settled work, including skips, not physically copied bytes or a successful outcome.
        var estimate = _progress.Update(new ProgressSample(System.Diagnostics.Stopwatch.GetElapsedTime(_clockStart),
            j.WorkBytesDone, j.WorkBytesTotal, id, it, j.TotalsFinal, j.State == JobState.Running && !j.IsPaused));
        Percent = lifecycleState is JobState.Completed or JobState.CompletedWithIssues
            ? 100 : Math.Min(99, 100 * estimate.Fraction);
        string state = j.State switch
        {
            JobState.Queued when j.WaitReason is not null => j.WaitReason,
            JobState.Pausing => "Pausing — waiting for a safe point",
            JobState.Paused => "Paused",
            JobState.Stopping => "Stopping — waiting for a safe point",
            JobState.AwaitingDecision => "Waiting for your decision",
            _ => j.State.Describe(),
        };
        StateText = state;
        var parts = new List<string>();
        if (bt > 0 || bd > 0) parts.Add($"{Formatters.SizeWithUnit(bd)}{(bt > 0 ? " of " + Formatters.SizeWithUnit(bt) + (j.TotalsFinal ? "" : "+") : "")}");
        if (it > 0) parts.Add($"{id:N0} of {it:N0}{(j.TotalsFinal ? "" : "+")} items");
        if (IsActive)
        {
            var speed = j.BytesPerSecond;
            if (speed > 1024) parts.Add(Formatters.Size((long)speed) + "/s");
            // Only a final total supports an estimate; growing totals never fabricate an ETA (plan §9.1). The time left is
            // a range while it is uncertain, one value once the likely and the pessimistic times agree (I26).
            if (j.State == JobState.Running && !j.IsPaused)
            {
                string? left = estimate switch
                {
                    { Likely: { } likely, Pessimistic: { } pessimistic } => Formatters.TimeLeft(likely, pessimistic),
                    { Note: ProgressNote.Measuring } => "estimating the time left…",
                    { Note: ProgressNote.Stalled } => "no progress for a while",
                    { Note: ProgressNote.Finishing } => "finishing…",
                    _ => null,
                };
                if (left is not null) parts.Add(left);
            }
            if (j.RateLimit > 0) parts.Add($"limited to {Formatters.Size(j.RateLimit)}/s");
        }
        if (j.State is JobState.Canceled or JobState.Failed or JobState.Interrupted)
        {
            parts.Clear();
            parts.Add($"{j.ItemsDone:N0} done, {j.ItemsSkipped:N0} skipped, {j.ItemsFailed:N0} failed");
            if (!j.TotalsFinal) parts.Add("remaining scope not fully counted");
            else if (it > id) parts.Add($"{it - id:N0} not processed");
        }
        else if (IsFinished)
        {
            // Done as asked: how much, and nothing more (the title says what). Counts matter when some items were not.
            parts.Clear();
            if (bd > 0) parts.Add($"{Formatters.SizeWithUnit(bd)} planned bytes accounted for");
            if (j.Kind == JobKind.Undo || j.State != JobState.Completed || j.ItemsSkipped + j.ItemsFailed > 0) parts.Add(j.Summary);
        }
        DetailText = Formatters.SafeName(string.Join(" · ", parts));
        // The worker can advance from a verification label to the next file while this view refreshes.
        // Use one snapshot for both the visible name and its phase, or a shorter next name can be
        // sliced using the previous name's verification suffix.
        string? current = j.CurrentItem;
        string displayed = current is { } c
            ? Formatters.SafeName(Path.GetFileName(c.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : c)
            : string.Empty;
        CurrentItem = displayed;
        Describe(j, estimate, bd, bt, id, it, displayed);
    }

    /// <summary>The richer view of a running operation (release issue I30).</summary>
    private void Describe(Job j, ProgressEstimate estimate, long bd, long bt, long id, long it, string currentItem)
    {
        bool running = j.State == JobState.Running && !j.IsPaused;
        const string verifyingSuffix = " (verifying)";
        bool verifying = currentItem.EndsWith(verifyingSuffix, StringComparison.Ordinal);
        PercentText = IsActive && !IsIndeterminate ? $"{Math.Floor(Percent):0}%"
            : IsFinished && j.State == JobState.Completed ? "100%" : string.Empty;
        var archivePhase = j.Kind == JobKind.ArchiveUpdate ? j.ArchivePhase : null;
        bool deferred = j.CancellationDeferred && !j.State.IsFinished();
        CanCancel = !deferred;
        CancelTooltip = deferred ? FinishingSafelyHelp : DefaultCancelTooltip(SyncGroup);
        if (archivePhase != _announcedPhase && j.State == JobState.Running)
        {
            _announcedPhase = archivePhase;
            if (archivePhase is { } announced) PhaseAnnouncement = $"{Title}: {ArchivePhaseText(announced)}";
        }
        PhaseText = j.State switch
        {
            JobState.Queued => j.WaitReason ?? "Waiting for its turn",
            JobState.Pausing => "Pausing — waiting for a safe point",
            JobState.Paused => "Paused",
            JobState.Stopping => "Stopping — waiting for a safe point",
            JobState.AwaitingDecision => "Waiting for your decision",
            JobState.Running when j.IsPaused => "Pausing",
            JobState.Running when archivePhase is { } phase => ArchivePhaseText(phase),
            JobState.Running when verifying => "Verifying",
            JobState.Running when estimate.Note == ProgressNote.Finishing => "Finishing",
            JobState.Running when !j.TotalsFinal && bd == 0 && id == 0 => "Counting what to do",
            JobState.Running => j.Kind switch
            {
                JobKind.Copy => "Copying",
                JobKind.Move => "Moving",
                JobKind.Undo => "Applying recorded inverse actions",
                JobKind.Recycle => $"Moving to {RecycleText.Destination}",
                JobKind.Delete => "Deleting",
                JobKind.Extract => "Extracting",
                JobKind.Checksum => "Calculating checksums",
                JobKind.ArchiveUpdate => "Packing",
                JobKind.ArchiveTest => "Testing",
                _ => "Working",
            },
            _ => j.State.Describe(),
        };
        // The file in hand; a large one shows how far its current step is: its copying, then its reading back.
        var file = j.CurrentFileProgress;
        string name = verifying ? currentItem[..^verifyingSuffix.Length] : currentItem;
        ShowsFileProgress = running && file is { Size: >= OwnBarFrom, WorkTotal: > 0 };
        CurrentFileText = !IsActive || name.Length == 0 ? string.Empty : name;
        CurrentFilePercent = 0;
        if (ShowsFileProgress && file is { } big)
        {
            long copied = Math.Min(big.WorkDone, big.Size), read = Math.Max(0, big.WorkDone - big.Size);
            long toRead = big.WorkTotal - big.Size;
            // The 160-unit file bar needs a visible gap until the step itself ends.
            CurrentFilePercent = verifying && toRead > 0 ? Math.Min(98, 100.0 * read / toRead) : Math.Min(98, 100.0 * copied / Math.Max(1, big.Size));
            CurrentFileText = verifying
                ? $"{name} · verifying, {Math.Floor(CurrentFilePercent):0}%"
                : $"{name} · {Formatters.SizeWithUnit(copied)} of {Formatters.SizeWithUnit(big.Size)}";
        }
        TimeLeftText = !IsActive ? string.Empty : estimate switch
        {
            { Likely: { } likely, Pessimistic: { } pessimistic } => Formatters.TimeSpanRange(likely, pessimistic),
            { Note: ProgressNote.Measuring } => "estimating…",
            { Note: ProgressNote.Counting } => "known once everything is counted",
            { Note: ProgressNote.Stalled } => "no progress for a while",
            { Note: ProgressNote.Finishing } => "finishing…",
            _ => running ? string.Empty : "—",
        };
        var elapsed = j.ElapsedSinceStart;
        ElapsedText = j.StartedUtc is not null ? Duration(elapsed) : string.Empty;
        TimingText = IsFinished ? (ElapsedText.Length > 0 ? "Took " + ElapsedText : string.Empty)
            : string.Join(" · ", new[] { TimeLeftText.Length > 0 ? "Time left: " + TimeLeftText : "", ElapsedText.Length > 0 ? "running for " + ElapsedText : "" }.Where(t => t.Length > 0));
        ItemsText = it > 0 || id > 0
            ? $"{id:N0} of {it:N0}{(j.TotalsFinal ? "" : "+")} items" + (j.ItemsSkipped > 0 ? $" · {j.ItemsSkipped:N0} skipped" : "") + (j.ItemsFailed > 0 ? $" · {j.ItemsFailed:N0} failed" : "")
            : (j.State is JobState.Canceled or JobState.Failed or JobState.Interrupted) && !j.TotalsFinal
                ? "Remaining item scope was not fully counted" : string.Empty;
        DataText = bt > 0 || bd > 0
            ? $"{Formatters.SizeWithUnit(bd)} of {Formatters.SizeWithUnit(bt)}{(j.TotalsFinal ? "" : "+")} planned bytes accounted for" +
              (j.DataBytesProcessed > 0 ? $" · {Formatters.SizeWithUnit(j.DataBytesProcessed)} data bytes copied or read" : "") +
              (j.VerifyBytesTotal > 0 ? $" · verified {Formatters.SizeWithUnit(j.VerifyBytesDone / 2)} of {Formatters.SizeWithUnit(j.VerifyBytesTotal / 2)}" : "")
            : string.Empty;
        // The pace, once a second and over the last 90 seconds. Logical byte credit for a same-volume rename or a
        // skipped copy is excluded; only actual copied data, verification reads or completed items move this graph.
        var now = System.Diagnostics.Stopwatch.GetElapsedTime(_clockStart);
        long units = MeasuresItems ? j.ItemsDone : j.DataBytesProcessed;
        if (_speedHistorySamples.Sample(now, units, running))
        {
            SpeedHistory = [.. _speedHistorySamples.Samples];
            HasSpeedHistory = _speedHistorySamples.HasTrend;
            GraphCaption = MeasuresItems
                ? $"Last 90 s · peak {_speedHistorySamples.Peak:0.#} items/s"
                : $"Last 90 s · peak {Formatters.Size((long)_speedHistorySamples.Peak)}/s";
        }
        double average = elapsed.TotalSeconds > 0 ? units / elapsed.TotalSeconds : 0;
        SpeedText = IsFinished
            ? MeasuresItems && j.ItemsDone > 0 && average > 0
                ? $"Average item rate: {average:0.#} items/s"
                : Formatters.AverageDataRate(j.DataBytesProcessed, elapsed) is { } finalRate
                    ? $"Average transfer speed: {finalRate}"
                    : string.Empty
            : !running ? string.Empty
            : MeasuresItems
                ? $"{_speedHistorySamples.Current:0.#} items/s now · {average:0.#} items/s on average"
                : _speedHistorySamples.Samples.Count > 0
                    ? $"{Formatters.Size((long)_speedHistorySamples.Current)}/s now · {Formatters.Size((long)average)}/s on average"
                    : average > 0 ? $"{Formatters.Size((long)average)}/s on average" : string.Empty;

        static string Duration(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{Math.Max(0, t.Seconds)} s";
    }
}

public enum RecoveryActionPhase { Idle, Reviewing, AwaitingApproval, Applying, Submitted, Completed }

public sealed partial class InterruptedJobViewModel(InterruptedJob job, bool archiveCleanupAvailable = false) : ObservableObject
{
    internal void UpdateArchiveCleanupAvailability(bool available)
    {
        if (archiveCleanupAvailable == available) return;
        archiveCleanupAvailable = available;
        OnPropertyChanged(nameof(CanCleanup));
        OnPropertyChanged(nameof(CanReviewArchiveCleanup));
        OnPropertyChanged(nameof(Details));
    }
    internal long StorageOrder { get; init; }
    public InterruptedJob Job { get; } = job;
    public bool IsStorage => Job.IsStorageTransform;
    public string Title => IsStorage ? Formatters.SafeName(Job.Title) + " · needs review"
        : $"{Formatters.SafeName(Job.Title)} (interrupted {Job.CreatedUtc.ToLocalTime():g})";
    public string Details => IsStorage ? "Result not confirmed. Review the recorded storage changes and uncertainty before acknowledging this history. Historical paths are evidence; this action does not replay changes or delete user files."
        : Job.IsReviewedAnalysisCopy ? "Inspect the recorded output paths to confirm what was published. A new copy requires a fresh analysis review."
        : Job.ArchiveTransactions.HasRecords ? ArchiveDetails
        : Job.HasLegacyZipRecovery ? InterruptedRecoveryReport.LegacyZipReviewWarning :
        $"{Job.SourceCount:N0} source items · " + (Job.OpenIntents.Count == 0
        ? $"{Job.CompletedSteps:N0} steps had finished. No step was in progress."
        : $"{Job.CompletedSteps:N0} steps had finished; {Job.OpenIntents.Count} step(s) were in progress and are inspected before anything is changed.") +
        (Job.Kind is nameof(JobKind.Copy) or nameof(JobKind.Move) && !CanRunAgain
            ? " " + Formatters.SafeName(Job.TransferIntentProblem ?? "The complete original transfer options are unavailable.") +
              RecoveryGuidance : "") +
        (InterruptedRecoveryReport.HasUnreviewedRemoteUpload(Job)
            ? " " + InterruptedRecoveryReport.RemoteUploadReviewWarning(Job) : "");

    private string ArchiveDetails => CanReviewArchiveCleanup
        ? "Review retained files to check the current archive before choosing cleanup. The archive will be kept."
        : InterruptedRecoveryReport.ArchiveSummary(Job);

    private string RecoveryGuidance => CanReviewNewTransfer
        ? Job.TransferIntentProblem?.Contains("Review a new transfer", StringComparison.OrdinalIgnoreCase) == true
            ? "" : " Review a new transfer to choose its scope and options again."
        : " The recorded scope cannot be prefilled here. Select the sources and destination again; cleanup and the recovery report remain available.";

    public string CleanupLabel => Job.ArchiveTransactions.HasRecords ? "Review retained files…"
        : Job.OpenIntents.Any(i => i.Operation == JobJournal.RenameViaOp) ? "Finish renaming…"
        : InterruptedRecoveryReport.HasUnreviewedRemoteUpload(Job) ? "Review local cleanup…"
        : "Clean up partial files";
    public string ReportButtonText => IsStorage ? "Review storage changes" : "Review report";
    public string MarkReviewedButtonText => IsStorage ? "Acknowledge reviewed history…" : "Mark reviewed…";
    public string ReviewReportLabel => (IsStorage ? "Review storage changes for " : "Review retained recovery report for ") + Formatters.SafeName(Job.Title);
    public string MarkReviewedLabel => (IsStorage ? "Acknowledge reviewed storage history for " : "Mark interrupted recovery reviewed for ") + Formatters.SafeName(Job.Title);
    public string HideLabel => "Hide interrupted recovery notice for " + Formatters.SafeName(Job.Title);
    public bool CanCleanup => !IsStorage && !Job.IsReviewedAnalysisCopy && !Job.HasLegacyZipRecovery &&
        (!Job.ArchiveTransactions.HasRecords || archiveCleanupAvailable);
    public bool CanReviewArchiveCleanup => Job.ArchiveTransactions.HasRecords && archiveCleanupAvailable;
    public bool CanMarkReviewed => !IsStorage && !Job.ArchiveTransactions.RequiresReview;

    /// <summary>Continuation requires complete, supported original meaning as well as known source paths.</summary>
    public bool CanRunAgain => !Job.ArchiveTransactions.HasRecords && Job.CanContinueTransfer;
    public string ContinueLabel => Job.Kind == nameof(JobKind.Move) ? "Continue move…" : "Continue copy…";
    public bool CanReviewNewTransfer => !Job.ArchiveTransactions.HasRecords && !CanRunAgain && Job.Kind is nameof(JobKind.Copy) or nameof(JobKind.Move) && Job.SourcesKnown &&
        Job.Sources.All(RecoverableTransferIntent.ValidPath) && Job.SourceCount <= RecoverableTransferIntent.RootLimit &&
        RecoverableTransferIntent.LocalDestination(Job.Destination) is not null;

    private RecoveryActionPhase _actionPhase;
    private string _actionStatus = string.Empty;
    public RecoveryActionPhase ActionPhase => _actionPhase;
    public string ActionStatus => _actionStatus;
    public bool HasActionStatus => _actionStatus.Length > 0;
    public bool HasPassiveActionStatus => HasActionStatus && !CanStopReview;
    public bool IsActionBusy => _actionPhase != RecoveryActionPhase.Idle;
    public bool CanStopReview => _actionPhase is RecoveryActionPhase.Reviewing or RecoveryActionPhase.AwaitingApproval or RecoveryActionPhase.Applying;
    public string StopReviewLabel => _actionPhase == RecoveryActionPhase.Applying ? "Stop after this step" : "Stop review";

    internal void SetActionPhase(RecoveryActionPhase phase, string status)
    {
        _actionPhase = phase;
        _actionStatus = status;
        OnPropertyChanged(nameof(ActionPhase));
        OnPropertyChanged(nameof(ActionStatus));
        OnPropertyChanged(nameof(HasActionStatus));
        OnPropertyChanged(nameof(HasPassiveActionStatus));
        OnPropertyChanged(nameof(IsActionBusy));
        OnPropertyChanged(nameof(CanStopReview));
        OnPropertyChanged(nameof(StopReviewLabel));
    }
}

public sealed class SyncGroupViewModel(SyncGroup group) : ObservableObject
{
    public SyncGroup Group { get; } = group;
    public string Title => $"Synchronize {Formatters.SafeName(Group.Source)} → {Formatters.SafeName(Group.Target)}";
    public string Reviewed => $"Reviewed plan {Group.ReviewRevision.ToString("N")[..8]}: {Group.ReviewedPlan}; {Group.ChildIds.Count:N0} steps";
    public string Outcome => Group.Outcome;
    public bool CanStop => Group.CanStop;
    public bool PendingRemovalReview => Group.PendingRemovalReview;
    public void Refresh()
    {
        OnPropertyChanged(nameof(Outcome));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(PendingRemovalReview));
    }
}

/// <summary>User-facing operation history and control (plan §19.2): not a dump of technical logs.</summary>
public sealed partial class OperationCenterViewModel : ObservableObject
{
    private const int IssueBatchSize = 256;
    private readonly Dictionary<Job, JobViewModel> _map = new();
    private readonly Dictionary<SyncGroup, SyncGroupViewModel> _groupMap = new();
    private readonly DispatcherTimer _timer;
    private readonly object _changedLock = new();
    private readonly HashSet<Job> _pendingChanges = [];
    private Job? _selectedIssueJob;
    private bool _issueRefreshQueued;
    private int _lastAnnouncedIssueCount;
    private long _storageRowOrder;

    private readonly Func<Location, string>? _display;

    public OperationCenterViewModel(JobManager manager, Func<Location, string>? display = null)
    {
        Manager = manager;
        StorageHistoryAvailable = manager.SupportsStorageHistory;
        _display = display;
        Interrupted.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InterruptedCount));
            OnPropertyChanged(nameof(HasInterrupted));
            if (SelectedInterrupted is null || !Interrupted.Contains(SelectedInterrupted))
                SelectedInterrupted = Interrupted.FirstOrDefault();
            // ListBox clears its old SelectedItem after a removed row's collection event. Let
            // that binding settle, then restore the first remaining review item if needed.
            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedInterrupted is null && Interrupted.Count > 0)
                    SelectedInterrupted = Interrupted[0];
            }, DispatcherPriority.Loaded);
        };
        ReviewedRecovery.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasReviewedRecovery));
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        manager.JobAdded += job => Dispatcher.UIThread.Post(() => Add(job));
        manager.StorageReviewRequired += candidate => Dispatcher.UIThread.Post(() => AddInterrupted(candidate));
        manager.SyncGroupAdded += group => Dispatcher.UIThread.Post(() => Add(group));
        manager.SyncGroupRemoved += group => Dispatcher.UIThread.Post(() => Remove(group));
        manager.JobRemoved += job => Dispatcher.UIThread.Post(() =>
        {
            RetireRow(job);
            UpdateSummary();
        });
        manager.JobChanged += job =>
        {
            lock (_changedLock)
                if (!_pendingChanges.Add(job)) return;
            Dispatcher.UIThread.Post(() =>
            {
                lock (_changedLock) _pendingChanges.Remove(job);
                if (_map.TryGetValue(job, out var vm)) vm.Refresh();
                if (ReferenceEquals(Selected?.Job, job))
                {
                    QueueIssueRefresh();
                    UpdateIssueSummary();
                }
                UpdateSummary();
            });
        };
    }

    public JobManager Manager { get; }
    public ObservableCollection<JobViewModel> Jobs { get; } = [];
    public ObservableCollection<SyncGroupViewModel> SyncGroups { get; } = [];
    public ObservableCollection<InterruptedJobViewModel> Interrupted { get; } = [];
    public int InterruptedCount => Interrupted.Count;
    public bool HasInterrupted => Interrupted.Count > 0;
    [ObservableProperty] private bool _storageHistoryAvailable;
    [ObservableProperty] private bool _hasMoreStorageHistory;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsStorageHistory)), NotifyPropertyChangedFor(nameof(CanStopStorageHistory))] private bool _isStorageHistoryLoading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsStorageHistory))] private string _storageHistoryStatus = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsStorageHistory)), NotifyPropertyChangedFor(nameof(CanStopStorageHistory))] private bool _isStorageHistoryAccounting;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsStorageHistory))] private string _storageAccountingStatus = string.Empty;
    [ObservableProperty] private StorageHistoryAccounting? _storageAccounting;
    public bool ShowsStorageHistory => IsStorageHistoryLoading || IsStorageHistoryAccounting || StorageHistoryStatus.Length > 0 || StorageAccountingStatus.Length > 0;
    public bool CanStopStorageHistory => IsStorageHistoryLoading || IsStorageHistoryAccounting;
    public bool StorageRowsOmitted { get; private set; }
    internal void ResetStorageRowsOmitted() => StorageRowsOmitted = false;
    [ObservableProperty] private string _recoveryAnnouncement = "Recovery status is available when an action starts.";
    [ObservableProperty] private bool _hasRecoveryAnnouncement;

    internal void AnnounceRecovery(string status)
    {
        // Keep this announcement on the drawer, not on a selected journal row. A
        // completed recovery removes that row while macOS may still deliver its
        // queued live-region notification, which otherwise has no accessible name.
        RecoveryAnnouncement = string.IsNullOrWhiteSpace(status)
            ? "Recovery action status is unavailable. Review the operation and journal."
            : status;
        HasRecoveryAnnouncement = true;
    }
    public ObservableCollection<RecoveryReviewRecord> ReviewedRecovery { get; } = [];
    public bool HasReviewedRecovery => ReviewedRecovery.Count > 0;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private JobViewModel? _selected;
    [ObservableProperty] private InterruptedJobViewModel? _selectedInterrupted;
    [ObservableProperty] private JobIssue? _selectedIssue;
    [ObservableProperty] private string _issueSummary = string.Empty;
    [ObservableProperty] private string _issueAnnouncement = string.Empty;
    [ObservableProperty] private JobViewModel? _primary;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _hasVisibleWork;
    [ObservableProperty] private int _activeCount;
    /// <summary>Whether "Clear finished" would remove anything.</summary>
    [ObservableProperty] private bool _hasFinished;
    /// <summary>What the window's taskbar button shows (Windows).</summary>
    [ObservableProperty] private TaskbarProgressState _taskbarState;
    [ObservableProperty] private double _taskbarFraction;

    /// <summary>Stable item source: new issues append without resetting issue selection or scroll.</summary>
    public ObservableCollection<JobIssue> SelectedIssues { get; } = [];

    partial void OnSelectedChanged(JobViewModel? value)
    {
        SelectedIssue = null;
        _selectedIssueJob = value?.Job;
        _lastAnnouncedIssueCount = value?.Job.IssueCount ?? 0;
        IssueAnnouncement = string.Empty;
        SelectedIssues.Clear();
        ReconcileSelectedIssues();
    }

    /// <summary>Opening Details follows active work; selection inside an open drawer remains the user's choice.</summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (!value)
        {
            IssueAnnouncement = string.Empty;
            return;
        }
        _lastAnnouncedIssueCount = Selected?.Job.IssueCount ?? 0;
        UpdateSummary();
        var latest = Primary is { } primary && !primary.Job.State.IsFinished()
            ? primary : Jobs.FirstOrDefault();
        if (!ReferenceEquals(Selected, latest))
            Selected = latest;
        else
            ReconcileSelectedIssues();
    }

    private void QueueIssueRefresh()
    {
        if (Selected is not { } selected || _issueRefreshQueued || selected.Job.IssueCount == SelectedIssues.Count) return;
        _issueRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _issueRefreshQueued = false;
            ReconcileSelectedIssues();
        }, DispatcherPriority.Background);
    }

    private void ReconcileSelectedIssues()
    {
        var job = Selected?.Job;
        if (!ReferenceEquals(job, _selectedIssueJob))
        {
            _selectedIssueJob = job;
            SelectedIssues.Clear();
        }
        if (job is null)
        {
            UpdateIssueSummary();
            return;
        }
        var next = job.ReadIssues(SelectedIssues.Count, IssueBatchSize);
        foreach (var issue in next) SelectedIssues.Add(issue);
        UpdateIssueSummary();
        if (job.IssueCount > _lastAnnouncedIssueCount)
        {
            int added = job.IssueCount - _lastAnnouncedIssueCount;
            _lastAnnouncedIssueCount = job.IssueCount;
            if (IsOpen)
            {
                var totals = job.IssueTotals;
                string label = added == 1 ? "New issue" : $"{added:N0} new issues";
                string detail = totals.Omitted > 0
                    ? "Latest details exceed the retention limit; see the issue count."
                    : job.ReadIssues(job.IssueCount - 1, 1).FirstOrDefault()?.AccessibleSummary
                        ?? "Issue details are unavailable.";
                IssueAnnouncement = $"{label}; {totals.Total:N0} total. Latest: {detail}";
            }
        }
        if (SelectedIssues.Count < job.IssueCount) QueueIssueRefresh();
    }

    private void UpdateIssueSummary()
    {
        if (Selected?.Job is not { } job)
        {
            IssueSummary = string.Empty;
            return;
        }
        if (job.Kind == JobKind.StorageTransform)
        {
            IssueSummary = job.StorageResult is { } storage ? StorageOperationText.Details(storage)
                : "Storage observations are not yet available.";
            return;
        }
        var totals = job.IssueTotals;
        IssueSummary = totals.Total == 0 ? "No issues reported."
            : $"Showing first {SelectedIssues.Count:N0} of {totals.Total:N0} issues · {totals.Retained:N0} details retained · " +
              $"{totals.Info:N0} info, {totals.Warning:N0} warnings, {totals.Error:N0} errors" +
              (totals.Omitted > 0 ? $" · {totals.Omitted:N0} additional details omitted" : "");
    }

    /// <summary>Shows a job's row as the job is now (a finished verification let go of its results: its Results button goes).</summary>
    public void Refresh(Job job)
    {
        if (_map.TryGetValue(job, out var vm)) vm.Refresh();
        if (ReferenceEquals(Selected?.Job, job)) { QueueIssueRefresh(); UpdateIssueSummary(); }
    }

    private void Add(JobViewModel vm)
    {
        // JobAdded callbacks run outside the manager lock and can reach the UI out of
        // admission order. Use the manager's retained order, not callback arrival,
        // so the newest operation is always at the top of history.
        var previousNewest = Jobs.FirstOrDefault();
        var admitted = Manager.Jobs;
        int position = 0;
        int jobIndex = IndexOf(admitted, vm.Job);
        while (position < Jobs.Count && IndexOf(admitted, Jobs[position].Job) > jobIndex)
            position++;
        Jobs.Insert(position, vm);
        vm.Refresh();
        if (IsOpen && (Selected is null || ReferenceEquals(Selected, previousNewest)))
            Selected = Jobs.FirstOrDefault();
        if (!_timer.IsEnabled) _timer.Start();
        UpdateSummary();
    }

    private static int IndexOf(IReadOnlyList<Job> jobs, Job job)
    {
        for (int i = 0; i < jobs.Count; i++)
            if (ReferenceEquals(jobs[i], job)) return i;
        return -1;
    }

    private void Add(Job job)
    {
        // Removal can reach the dispatcher before an earlier JobAdded observer has posted this add.
        if (_map.ContainsKey(job) || job.State.IsFinished() && !Manager.Jobs.Contains(job)) return;
        var vm = new JobViewModel(job, _display, Manager.SyncGroupFor(job));
        _map[job] = vm;
        Add(vm);
    }

    private void Add(SyncGroup group)
    {
        if (_groupMap.ContainsKey(group) || !Manager.SyncGroups.Contains(group)) return;
        var vm = new SyncGroupViewModel(group);
        _groupMap.Add(group, vm);
        SyncGroups.Insert(0, vm);
        group.Changed += OnSyncGroupChanged;
        vm.Refresh();
    }

    private void OnSyncGroupChanged(SyncGroup group) =>
        Dispatcher.UIThread.Post(() => { if (_groupMap.TryGetValue(group, out var vm)) vm.Refresh(); });

    private void Remove(SyncGroup group)
    {
        group.Changed -= OnSyncGroupChanged;
        if (_groupMap.Remove(group, out var vm)) SyncGroups.Remove(vm);
    }

    private void Tick()
    {
        bool any = false;
        foreach (var vm in Jobs)
        {
            if (!vm.IsFinished || vm.Job.State.IsFinished() != vm.IsFinished)
            {
                vm.Refresh();
                any |= !vm.IsFinished;
            }
        }
        foreach (var group in SyncGroups) group.Refresh();
        if (Selected is not null) { QueueIssueRefresh(); UpdateIssueSummary(); }
        UpdateSummary();
        if (!any) _timer.Stop();
    }

    public void UpdateSummary()
    {
        // A job ends on its own thread and its row learns of it from a refresh queued here; rows that lag catch up first,
        // so what is counted below and what the rows show are the same.
        var movable = Manager.MovableQueuedJobs();
        foreach (var vm in Jobs)
        {
            if (vm.IsFinished != vm.Job.State.IsFinished()) vm.Refresh();
            vm.CanMoveUpInQueue = movable.Contains(vm.Job);
            vm.MoveUpReason = vm.Job.State != JobState.Queued ? string.Empty
                : vm.SyncGroup is not null ? "Synchronization steps keep their reviewed order."
                : vm.CanMoveUpInQueue ? "Move this operation ahead of the previous queued operation."
                : "This is the first queued operation.";
        }
        var active = Jobs.Where(j => !j.Job.State.IsFinished()).ToList();
        ActiveCount = active.Count;
        Primary = active.FirstOrDefault(j => j.NeedsDecision) ?? active.FirstOrDefault(j => j.IsActive) ?? active.FirstOrDefault()
                  ?? Jobs.FirstOrDefault(j => j.IsFinished && j.Job.FinishedUtc > DateTime.UtcNow.AddSeconds(-20));
        HasVisibleWork = Primary is not null || Interrupted.Count > 0;
        HasFinished = active.Count < Jobs.Count;
        // The taskbar button (Windows): the running operation's bar; yellow while it is paused or waits for an answer,
        // red for a moment after one failed (release issue I30).
        var shown = active.FirstOrDefault(j => j.NeedsDecision) ?? active.FirstOrDefault(j => j.IsActive);
        (TaskbarState, TaskbarFraction) = TaskbarFor(shown, Primary);
        Summary = active.Count switch
        {
            0 => Interrupted.Count > 0 ? Formatters.Plural(Interrupted.Count, "operation needs", "operations need") + " review" : string.Empty,
            1 => string.Empty,
            _ => $"+{active.Count - 1} more",
        };
    }

    internal static (TaskbarProgressState State, double Fraction) TaskbarFor(JobViewModel? shown, JobViewModel? primary) =>
        shown switch
        {
            { IsStorage: true, NeedsDecision: true } or { IsStorage: true, Job: { State: JobState.Paused or JobState.Pausing } } => (TaskbarProgressState.Paused, 0),
            { IsStorage: true, IsIndeterminate: true } => (TaskbarProgressState.Indeterminate, 0),
            { IsStorage: true } => (TaskbarProgressState.None, 0),
            null when primary is { IsStorage: true, IsFinished: true, Severity: "error" } => (TaskbarProgressState.Error, 0),
            null when primary is { IsStorage: true } => (TaskbarProgressState.None, 0),
            { NeedsDecision: true } or { Job: { State: JobState.Paused } } => (TaskbarProgressState.Paused, shown.Percent / 100),
            { IsIndeterminate: true } => (TaskbarProgressState.Indeterminate, 0),
            not null => (TaskbarProgressState.Normal, shown.Percent / 100),
            null when primary is { IsFinished: true, Severity: "error" } => (TaskbarProgressState.Error, Math.Min(0.999, primary.Percent / 100)),
            _ => (TaskbarProgressState.None, 0),
        };

    public void LoadInterrupted(IEnumerable<InterruptedJob> jobs)
    {
        // Startup discovery can finish after a current-session job retires. Never replace that pending collection.
        foreach (var j in jobs) AddInterrupted(j);
        UpdateSummary();
    }
    public void AddInterrupted(InterruptedJob job)
    {
        if (job.IsStorageTransform)
        {
            string leaf = job.StorageJournalLeaf ?? Path.GetFileName(job.JournalPath);
            job = new(leaf, nameof(JobKind.StorageTransform), ShortStorageTitle(job.Title), job.CreatedUtc, [], null, [], [], 0, Math.Clamp(job.SourceCount, 0, 64))
                { StorageJournalLeaf = leaf };
            if (StorageHistoryStatus.Length == 0) StorageHistoryStatus = "Storage history accounting/verification still in progress.";
        }
        if (Interrupted.FirstOrDefault(row => PathUtil.SafetyComparer.Equals(row.Job.JournalPath, job.JournalPath)) is { } existing)
        {
            existing.UpdateArchiveCleanupAvailability(CanReviewArchiveCleanup?.Invoke(job) == true);
            return;
        }
        if (job.IsStorageTransform) MakeStorageRowSpace();
        Interrupted.Add(new InterruptedJobViewModel(job, CanReviewArchiveCleanup?.Invoke(job) == true) { StorageOrder = ++_storageRowOrder });
        UpdateSummary();
    }
    internal Func<InterruptedJob, bool>? CanReviewArchiveCleanup { get; set; }

    public void LoadReviewedRecovery(IEnumerable<RecoveryReviewRecord> records)
    {
        // A current-session storage acknowledgment may finish while the legacy startup read is still running.
        var storage = ReviewedRecovery.Where(r => r.IsStorage).ToArray();
        ReviewedRecovery.Clear();
        foreach (var record in records.Take(RecoveryReviewStore.KeepRecords)) ReviewedRecovery.Add(record);
        foreach (var record in storage.Reverse()) AddReviewedRecovery(record);
        OnPropertyChanged(nameof(HasReviewedRecovery));
    }

    public void AddReviewedRecovery(RecoveryReviewRecord record)
    {
        if (record.IsStorage)
        {
            var ack = record.StorageAcknowledgment;
            record = record with
            {
                JournalPath = Path.GetFileName(record.JournalPath), Title = ShortStorageTitle(record.Title), Report = string.Empty, Action = "Storage history",
                StorageRetainedLeaf = record.StorageRetainedLeaf ?? ack?.FileName, StorageAcknowledgment = null,
                StorageVerified = record.StorageVerified || ack is not null,
                StorageEffectUncertain = record.StorageEffectUncertain || ack is not null && (ack.Reviewed.EffectUncertain || ack.Reviewed.Damaged || ack.Reviewed.LimitReached),
                StorageOrder = ++_storageRowOrder,
            };
        }
        var previous = ReviewedRecovery.FirstOrDefault(r => r.JournalPath == record.JournalPath ||
            record.StorageRetainedLeaf is not null && r.StorageRetainedLeaf == record.StorageRetainedLeaf);
        if (previous is not null) ReviewedRecovery.Remove(previous);
        if (record.IsStorage) MakeStorageRowSpace();
        ReviewedRecovery.Insert(0, record);
        while (ReviewedRecovery.Count > RecoveryReviewStore.KeepRecords) ReviewedRecovery.RemoveAt(ReviewedRecovery.Count - 1);
        OnPropertyChanged(nameof(HasReviewedRecovery));
    }

    public int StorageDescriptorCount => Interrupted.Count(r => r.IsStorage) + ReviewedRecovery.Count(r => r.IsStorage);
    private static string ShortStorageTitle(string title) => title.Length <= 256 ? title : title[..256] + "…";
    private void MakeStorageRowSpace()
    {
        while (StorageDescriptorCount >= 64)
        {
            var reviewed = ReviewedRecovery.Where(r => r.IsStorage).MinBy(r => r.StorageOrder);
            var pending = Interrupted.Where(r => r.IsStorage && !ReferenceEquals(r, SelectedInterrupted)).MinBy(r => r.StorageOrder);
            if (pending is not null && (reviewed is null || pending.StorageOrder < reviewed.StorageOrder)) Interrupted.Remove(pending);
            else if (reviewed is not null) ReviewedRecovery.Remove(reviewed);
            else throw new InvalidOperationException("The selected storage descriptor exhausted its bounded history allowance.");
            HasMoreStorageHistory = true;
            StorageRowsOmitted = true;
        }
    }

    public void RemoveFinished()
    {
        // By the job's own state, as HasFinished counts them: a row whose refresh is still queued is finished too.
        foreach (var vm in Jobs.Where(j => j.Job.State.IsFinished()).ToList())
            RetireFinishedRow(vm);
        foreach (var group in SyncGroups.Where(g => g.Group.IsFinished).ToList()) Manager.RemoveSyncGroup(group.Group);
        UpdateSummary();
    }

    /// <summary>Retires only the finished rows the user reviewed, not jobs that finish while a dialog is open.</summary>
    public void RemoveFinished(IReadOnlyCollection<Job> reviewed)
    {
        foreach (var job in reviewed)
            if (job.State.IsFinished() && _map.TryGetValue(job, out var row)) RetireFinishedRow(row);
        foreach (var group in SyncGroups.Where(g => g.Group.IsFinished && g.Group.Steps.All(reviewed.Contains)).ToList())
            Manager.RemoveSyncGroup(group.Group);
        UpdateSummary();
    }

    public void RemoveFinished(JobViewModel vm)
    {
        if (!vm.Job.State.IsFinished()) return;
        RetireFinishedRow(vm);
        UpdateSummary();
    }

    private void RetireFinishedRow(JobViewModel vm)
    {
        RetireRow(vm.Job);
        Manager.Remove(vm.Job);
    }

    private void RetireRow(Job job)
    {
        if (!_map.Remove(job, out var vm)) return;
        bool wasSelected = ReferenceEquals(Selected, vm);
        Jobs.Remove(vm);
        if (!wasSelected) return;
        Selected = IsOpen ? Jobs.FirstOrDefault() : null;
        // ListBox may clear its removed SelectedItem after the collection event.
        // Restore a fallback only if neither binding nor user chose another row.
        Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen && Selected is null && Jobs.Count > 0)
                Selected = Jobs[0];
        }, DispatcherPriority.Loaded);
    }
}
