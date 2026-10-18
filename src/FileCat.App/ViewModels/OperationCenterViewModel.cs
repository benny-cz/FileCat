using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Jobs;
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
    public string CancelLabel => SyncGroup is null ? "Cancel" : "Cancel this step";
    public string CancelTooltip => SyncGroup is null
        ? "Stops at the next safe step; completed steps are kept"
        : "Cancels only this step. Other synchronization steps stay visible; pending removals wait for review if a transfer stops.";

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
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;
    [ObservableProperty] private string _resumeLabel = "Resume";
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canShowUndoReport;
    [ObservableProperty] private string _undoStatus = string.Empty;
    [ObservableProperty] private bool _canRetryElevated;
    /// <summary>A finished check of checksums and signatures whose results are still kept (D-57): they can be shown again.</summary>
    [ObservableProperty] private bool _canShowResults;
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
        // A title and a summary name files (a verification's names its checksum file and who signed it): escaped as names are (§18.3).
        Title = Formatters.SafeName(j.Title);
        IsFinished = j.State.IsFinished();
        IsActive = j.State.IsActive();
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
        CanRetryElevated = OperatingSystem.IsWindows() && Platform.Windows.Elevation.ElevationPlanBuilder.OffersRetry(j);
        CanShowResults = IsFinished && j.Kind is (JobKind.VerifyChecksums or JobKind.VerifyBeside) && j.VerificationReport is not null;
        int issues = j.IssueCount;
        HasIssues = issues > 0;
        Severity = j.State == JobState.Failed || j.ItemsFailed > 0 ? "error" : j.State == JobState.CompletedWithIssues ? "warning" : j.State == JobState.Completed ? "ok" : "info";

        long bt = j.BytesTotal, bd = j.BytesDone, it = j.ItemsTotal, id = j.ItemsDone + j.ItemsSkipped + j.ItemsFailed;
        IsIndeterminate = IsActive && !j.TotalsFinal && bt == 0 && it == 0;
        // The fraction tracks settled work, including skips, not physically copied bytes or a successful outcome.
        var estimate = _progress.Update(new ProgressSample(System.Diagnostics.Stopwatch.GetElapsedTime(_clockStart),
            j.WorkBytesDone, j.WorkBytesTotal, id, it, j.TotalsFinal, j.State == JobState.Running && !j.IsPaused));
        Percent = j.State is JobState.Completed or JobState.CompletedWithIssues
            ? 100 : Math.Min(99.9, 100 * estimate.Fraction);
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
        CurrentItem = j.CurrentItem is { } c ? Formatters.SafeName(Path.GetFileName(c.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : c) : string.Empty;
        Describe(j, estimate, bd, bt, id, it);
    }

    /// <summary>The richer view of a running operation (release issue I30).</summary>
    private void Describe(Job j, ProgressEstimate estimate, long bd, long bt, long id, long it)
    {
        bool running = j.State == JobState.Running && !j.IsPaused;
        bool verifying = j.CurrentItem?.EndsWith("(verifying)", StringComparison.Ordinal) == true;
        PercentText = IsActive && !IsIndeterminate ? $"{Math.Floor(Percent):0}%"
            : j.State == JobState.Completed ? "100%" : string.Empty;
        PhaseText = j.State switch
        {
            JobState.Queued => j.WaitReason ?? "Waiting for its turn",
            JobState.Pausing => "Pausing — waiting for a safe point",
            JobState.Paused => "Paused",
            JobState.Stopping => "Stopping — waiting for a safe point",
            JobState.AwaitingDecision => "Waiting for your decision",
            JobState.Running when j.IsPaused => "Pausing",
            JobState.Running when verifying => "Verifying",
            JobState.Running when estimate.Note == ProgressNote.Finishing => "Finishing",
            JobState.Running when !j.TotalsFinal && bd == 0 && id == 0 => "Counting what to do",
            JobState.Running => j.Kind switch
            {
                JobKind.Copy => "Copying",
                JobKind.Move => "Moving",
                JobKind.Undo => "Applying recorded inverse actions",
                JobKind.Recycle => "Moving to the Recycle Bin",
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
        string name = verifying ? CurrentItem[..^" (verifying)".Length] : CurrentItem;
        ShowsFileProgress = running && file is { Size: >= OwnBarFrom, WorkTotal: > 0 };
        CurrentFileText = !IsActive || name.Length == 0 ? string.Empty : name;
        CurrentFilePercent = 0;
        if (ShowsFileProgress && file is { } big)
        {
            long copied = Math.Min(big.WorkDone, big.Size), read = Math.Max(0, big.WorkDone - big.Size);
            long toRead = big.WorkTotal - big.Size;
            CurrentFilePercent = verifying && toRead > 0 ? Math.Min(99.9, 100.0 * read / toRead) : Math.Min(99.9, 100.0 * copied / Math.Max(1, big.Size));
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
        ElapsedText = j.StartedUtc is { } started ? Duration((j.FinishedUtc ?? DateTime.UtcNow) - started) : string.Empty;
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
            HasSpeedHistory = _speedHistorySamples.HasActivity;
            GraphCaption = MeasuresItems
                ? $"Last 90 s · peak {_speedHistorySamples.Peak:0.#} items/s"
                : $"Last 90 s · peak {Formatters.Size((long)_speedHistorySamples.Peak)}/s";
        }
        double average = j.StartedUtc is { } from && (DateTime.UtcNow - from).TotalSeconds is var seconds and > 1 ? units / seconds : 0;
        SpeedText = !IsActive ? string.Empty
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

public enum RecoveryActionPhase { Idle, Reviewing, AwaitingApproval, Applying, Submitted }

public sealed partial class InterruptedJobViewModel(InterruptedJob job) : ObservableObject
{
    public InterruptedJob Job { get; } = job;
    public string Title => $"{Formatters.SafeName(Job.Title)} (interrupted {Job.CreatedUtc.ToLocalTime():g})";
    public string Details => $"{Job.SourceCount:N0} source items · " + (Job.OpenIntents.Count == 0
        ? $"{Job.CompletedSteps:N0} steps had finished. No step was in progress."
        : $"{Job.CompletedSteps:N0} steps had finished; {Job.OpenIntents.Count} step(s) were in progress and are inspected before anything is changed.") +
        (Job.Kind is nameof(JobKind.Copy) or nameof(JobKind.Move) && !CanRunAgain
            ? " " + Formatters.SafeName(Job.TransferIntentProblem ?? "The complete original transfer options are unavailable. Review a new transfer to choose its scope and options again.") : "");

    public string CleanupLabel => Job.OpenIntents.Any(i => i.Operation == JobJournal.RenameViaOp) ? "Finish renaming…" : "Clean up partial files";
    public string ReviewReportLabel => "Review retained recovery report for " + Formatters.SafeName(Job.Title);
    public string MarkReviewedLabel => "Mark interrupted recovery reviewed for " + Formatters.SafeName(Job.Title);
    public string HideLabel => "Hide interrupted recovery notice for " + Formatters.SafeName(Job.Title);

    /// <summary>Continuation requires complete, supported original meaning as well as known source paths.</summary>
    public bool CanRunAgain => Job.CanContinueTransfer;
    public string ContinueLabel => Job.Kind == nameof(JobKind.Move) ? "Continue move…" : "Continue copy…";
    public bool CanReviewNewTransfer => !CanRunAgain && Job.Kind is nameof(JobKind.Copy) or nameof(JobKind.Move) && Job.SourcesKnown && Job.SourceCount <= RecoverableTransferIntent.RootLimit &&
        RecoverableTransferIntent.LocalDestination(Job.Destination) is not null;

    private RecoveryActionPhase _actionPhase;
    private string _actionStatus = string.Empty;
    public RecoveryActionPhase ActionPhase => _actionPhase;
    public string ActionStatus => _actionStatus;
    public bool HasActionStatus => _actionStatus.Length > 0;
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

    private readonly Func<Location, string>? _display;

    public OperationCenterViewModel(JobManager manager, Func<Location, string>? display = null)
    {
        Manager = manager;
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
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        manager.JobAdded += job => Dispatcher.UIThread.Post(() => Add(job));
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
    public ObservableCollection<RecoveryReviewRecord> ReviewedRecovery { get; } = [];
    public bool HasReviewedRecovery => ReviewedRecovery.Count > 0;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private JobViewModel? _selected;
    [ObservableProperty] private InterruptedJobViewModel? _selectedInterrupted;
    [ObservableProperty] private JobIssue? _selectedIssue;
    [ObservableProperty] private string _issueSummary = string.Empty;
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
        SelectedIssues.Clear();
        ReconcileSelectedIssues();
    }

    /// <summary>Opened, the details show the operation that matters now rather than an empty half (release issue I30).</summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (value && Selected is null) Selected = Primary ?? Jobs.FirstOrDefault();
        else if (value) ReconcileSelectedIssues();
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
        if (SelectedIssues.Count < job.IssueCount) QueueIssueRefresh();
    }

    private void UpdateIssueSummary()
    {
        if (Selected?.Job is not { } job)
        {
            IssueSummary = string.Empty;
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
        if (ReferenceEquals(Selected?.Job, job)) QueueIssueRefresh();
    }

    private void Add(JobViewModel vm)
    {
        Jobs.Insert(0, vm);
        vm.Refresh();
        if (!_timer.IsEnabled) _timer.Start();
        UpdateSummary();
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
        if (Selected is not null) QueueIssueRefresh();
        UpdateSummary();
        if (!any) _timer.Stop();
    }

    public void UpdateSummary()
    {
        // A job ends on its own thread and its row learns of it from a refresh queued here; rows that lag catch up first,
        // so what is counted below and what the rows show are the same.
        foreach (var vm in Jobs)
            if (vm.IsFinished != vm.Job.State.IsFinished()) vm.Refresh();
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
            0 => Interrupted.Count > 0 ? Formatters.Plural(Interrupted.Count, "interrupted operation needs", "interrupted operations need") + " attention" : string.Empty,
            1 => string.Empty,
            _ => $"+{active.Count - 1} more",
        };
    }

    internal static (TaskbarProgressState State, double Fraction) TaskbarFor(JobViewModel? shown, JobViewModel? primary) =>
        shown switch
        {
            { NeedsDecision: true } or { Job: { State: JobState.Paused } } => (TaskbarProgressState.Paused, shown.Percent / 100),
            { IsIndeterminate: true } => (TaskbarProgressState.Indeterminate, 0),
            not null => (TaskbarProgressState.Normal, shown.Percent / 100),
            null when primary is { IsFinished: true, Severity: "error" } => (TaskbarProgressState.Error, Math.Min(0.999, primary.Percent / 100)),
            _ => (TaskbarProgressState.None, 0),
        };

    public void LoadInterrupted(IEnumerable<InterruptedJob> jobs)
    {
        Interrupted.Clear();
        foreach (var j in jobs) Interrupted.Add(new InterruptedJobViewModel(j));
        UpdateSummary();
    }

    public void LoadReviewedRecovery(IEnumerable<RecoveryReviewRecord> records)
    {
        ReviewedRecovery.Clear();
        foreach (var record in records.Take(RecoveryReviewStore.KeepRecords)) ReviewedRecovery.Add(record);
        OnPropertyChanged(nameof(HasReviewedRecovery));
    }

    public void AddReviewedRecovery(RecoveryReviewRecord record)
    {
        var previous = ReviewedRecovery.FirstOrDefault(r => r.JournalPath == record.JournalPath);
        if (previous is not null) ReviewedRecovery.Remove(previous);
        ReviewedRecovery.Insert(0, record);
        while (ReviewedRecovery.Count > RecoveryReviewStore.KeepRecords) ReviewedRecovery.RemoveAt(ReviewedRecovery.Count - 1);
        OnPropertyChanged(nameof(HasReviewedRecovery));
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
        Jobs.Remove(vm);
        if (ReferenceEquals(Selected, vm)) Selected = null;
    }
}
