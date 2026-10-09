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
    public JobViewModel(Job job, Func<Location, string>? display = null)
    {
        Job = job;
        Route = DescribeRoute(job.Request, display ?? (l => l.IsFileSystem ? l.Path : l.ToString()));
    }

    public Job Job { get; }

    /// <summary>The progress shown and the time left (release issue I26), on this view's own clock.</summary>
    private readonly ProgressEstimator _progress = new();
    private readonly long _clockStart = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>Where the items come from and where they go (plan §9: source and destination stay clear).</summary>
    public string Route { get; }

    private static string DescribeRoute(JobRequest r, Func<Location, string> display)
    {
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
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRetryElevated;
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
    /// <summary>The pace along the job: X is how far it was (0 to 1), Y the bytes per second then.</summary>
    [ObservableProperty] private Avalonia.Point[] _speedHistory = [];

    private readonly List<Avalonia.Point> _speeds = [];
    private TimeSpan? _lastSpeedSample;
    private long _lastSpeedBytes;

    /// <summary>A large file gets a bar of its own; a small one is done before a bar would help.</summary>
    private const long OwnBarFrom = 16L << 20;

    public void Refresh()
    {
        var j = Job;
        Title = j.Title;
        IsFinished = j.State.IsFinished();
        IsActive = j.State.IsActive();
        NeedsDecision = j.State == JobState.AwaitingDecision;
        CanPause = j.State is JobState.Running or JobState.Queued && !j.IsPaused;
        CanResume = j.IsPaused || j.State is JobState.Paused or JobState.Pausing;
        CanUndo = j.CanUndo;
        CanRetryElevated = OperatingSystem.IsWindows() && Platform.Windows.Elevation.ElevationPlanBuilder.OffersRetry(j);
        int issues = j.IssueCount;
        HasIssues = issues > 0;
        Severity = j.State == JobState.Failed || j.ItemsFailed > 0 ? "error" : j.State == JobState.CompletedWithIssues ? "warning" : j.State == JobState.Completed ? "ok" : "info";

        long bt = j.BytesTotal, bd = j.BytesDone, it = j.ItemsTotal, id = j.ItemsDone + j.ItemsSkipped + j.ItemsFailed;
        IsIndeterminate = IsActive && !j.TotalsFinal && bt == 0 && it == 0;
        // All the work counts (copying, reading back to verify, what settled); 100% only once the job has ended.
        var estimate = _progress.Update(new ProgressSample(System.Diagnostics.Stopwatch.GetElapsedTime(_clockStart),
            j.WorkBytesDone, j.WorkBytesTotal, id, it, j.TotalsFinal, j.State == JobState.Running && !j.IsPaused));
        Percent = IsFinished ? 100 : 100 * estimate.Fraction;
        string state = j.State switch
        {
            JobState.Queued when j.WaitReason is not null => j.WaitReason,
            JobState.Paused => "Paused",
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
        if (IsFinished)
        {
            // Done as asked: how much, and nothing more (the title says what). Counts matter when some items were not.
            parts.Clear();
            if (bd > 0) parts.Add(Formatters.SizeWithUnit(bd));
            if (j.State != JobState.Completed || j.ItemsSkipped + j.ItemsFailed > 0) parts.Add(j.Summary);
        }
        DetailText = string.Join(" · ", parts);
        CurrentItem = j.CurrentItem is { } c ? Path.GetFileName(c.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : c : string.Empty;
        Describe(j, estimate, bd, bt, id, it);
    }

    /// <summary>The richer view of a running operation (release issue I30).</summary>
    private void Describe(Job j, ProgressEstimate estimate, long bd, long bt, long id, long it)
    {
        bool running = j.State == JobState.Running && !j.IsPaused;
        bool verifying = j.CurrentItem?.EndsWith("(verifying)", StringComparison.Ordinal) == true;
        PercentText = IsActive && !IsIndeterminate ? $"{Math.Floor(Percent):0}%" : IsFinished ? "100%" : string.Empty;
        PhaseText = j.State switch
        {
            JobState.Queued => j.WaitReason ?? "Waiting for its turn",
            JobState.Paused or JobState.Pausing => "Paused",
            JobState.AwaitingDecision => "Waiting for your decision",
            JobState.Running when j.IsPaused => "Pausing",
            JobState.Running when verifying => "Verifying",
            JobState.Running when estimate.Note == ProgressNote.Finishing => "Finishing",
            JobState.Running when !j.TotalsFinal && bd == 0 && id == 0 => "Counting what to do",
            JobState.Running => j.Kind switch
            {
                JobKind.Copy => "Copying",
                JobKind.Move => "Moving",
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
            : string.Empty;
        DataText = bt > 0 || bd > 0
            ? $"{Formatters.SizeWithUnit(bd)} of {Formatters.SizeWithUnit(bt)}{(j.TotalsFinal ? "" : "+")}" +
              (j.VerifyBytesTotal > 0 ? $" · verified {Formatters.SizeWithUnit(j.VerifyBytesDone / 2)} of {Formatters.SizeWithUnit(j.VerifyBytesTotal / 2)}" : "")
            : string.Empty;
        // The pace, once a second: now, and on average since the start.
        var now = System.Diagnostics.Stopwatch.GetElapsedTime(_clockStart);
        if (!running) _lastSpeedSample = null;
        else if (_lastSpeedSample is not { } last)
        {
            _lastSpeedSample = now;
            _lastSpeedBytes = j.WorkBytesDone;
        }
        else if (now - last >= TimeSpan.FromSeconds(1))
        {
            long work = j.WorkBytesDone;
            _speeds.Add(new Avalonia.Point(Percent / 100, Math.Max(0, (work - _lastSpeedBytes) / (now - last).TotalSeconds)));
            // The whole job stays drawn: past 600 points, every other one goes.
            if (_speeds.Count > 600) for (int i = _speeds.Count - 2; i > 0; i -= 2) _speeds.RemoveAt(i);
            SpeedHistory = [.. _speeds];
            HasSpeedHistory = true;
            _lastSpeedSample = now;
            _lastSpeedBytes = work;
        }
        double average = j.StartedUtc is { } from && (DateTime.UtcNow - from).TotalSeconds is var seconds and > 1 ? j.WorkBytesDone / seconds : 0;
        SpeedText = !IsActive ? string.Empty
            : _speeds.Count > 0 ? $"{Formatters.Size((long)_speeds[^1].Y)}/s now · {Formatters.Size((long)average)}/s on average"
            : average > 0 ? $"{Formatters.Size((long)average)}/s on average" : string.Empty;

        static string Duration(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{Math.Max(0, t.Seconds)} s";
    }
}

public sealed partial class InterruptedJobViewModel(InterruptedJob job) : ObservableObject
{
    public InterruptedJob Job { get; } = job;
    public string Title => $"{Job.Title} (interrupted {Job.CreatedUtc.ToLocalTime():g})";
    public string Details => $"{Job.SourceCount:N0} source items · " + (Job.OpenIntents.Count == 0
        ? $"{Job.CompletedSteps:N0} steps had finished. No step was in progress."
        : $"{Job.CompletedSteps:N0} steps had finished; {Job.OpenIntents.Count} step(s) were in progress and are inspected before anything is changed.");

    public string CleanupLabel => Job.OpenIntents.Any(i => i.Operation == JobJournal.RenameViaOp) ? "Finish renaming…" : "Clean up partial files";

    /// <summary>A copy or move whose sources are all known can continue with a new job.</summary>
    public bool CanRunAgain => Job.Kind is nameof(JobKind.Copy) or nameof(JobKind.Move) && Job.Destination is not null && Job.SourcesKnown;
}

/// <summary>User-facing operation history and control (plan §19.2): not a dump of technical logs.</summary>
public sealed partial class OperationCenterViewModel : ObservableObject
{
    private readonly Dictionary<Job, JobViewModel> _map = new();
    private readonly DispatcherTimer _timer;

    private readonly Func<Location, string>? _display;

    public OperationCenterViewModel(JobManager manager, Func<Location, string>? display = null)
    {
        Manager = manager;
        _display = display;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick());
        manager.JobAdded += job => Dispatcher.UIThread.Post(() => Add(job));
        manager.JobChanged += job => Dispatcher.UIThread.Post(() =>
        {
            if (_map.TryGetValue(job, out var vm)) vm.Refresh();
            UpdateSummary();
        });
    }

    public JobManager Manager { get; }
    public ObservableCollection<JobViewModel> Jobs { get; } = [];
    public ObservableCollection<InterruptedJobViewModel> Interrupted { get; } = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private JobViewModel? _selected;
    [ObservableProperty] private JobViewModel? _primary;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _hasVisibleWork;
    [ObservableProperty] private int _activeCount;
    /// <summary>Whether "Clear finished" would remove anything.</summary>
    [ObservableProperty] private bool _hasFinished;
    /// <summary>What the window's taskbar button shows (Windows).</summary>
    [ObservableProperty] private TaskbarProgressState _taskbarState;
    [ObservableProperty] private double _taskbarFraction;

    public IReadOnlyList<JobIssue> SelectedIssues => Selected?.Job.Issues ?? [];

    partial void OnSelectedChanged(JobViewModel? value) => OnPropertyChanged(nameof(SelectedIssues));

    /// <summary>Opened, the details show the operation that matters now rather than an empty half (release issue I30).</summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (value && Selected is null) Selected = Primary ?? Jobs.FirstOrDefault();
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
        if (_map.ContainsKey(job)) return;
        var vm = new JobViewModel(job, _display);
        _map[job] = vm;
        Add(vm);
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
        (TaskbarState, TaskbarFraction) = shown switch
        {
            { NeedsDecision: true } or { CanResume: true } => (TaskbarProgressState.Paused, shown.Percent / 100),
            { IsIndeterminate: true } => (TaskbarProgressState.Indeterminate, 0),
            not null => (TaskbarProgressState.Normal, shown.Percent / 100),
            null when Primary is { IsFinished: true, Severity: "error" } => (TaskbarProgressState.Error, 1),
            _ => (TaskbarProgressState.None, 0),
        };
        Summary = active.Count switch
        {
            0 => Interrupted.Count > 0 ? Formatters.Plural(Interrupted.Count, "interrupted operation needs", "interrupted operations need") + " attention" : string.Empty,
            1 => string.Empty,
            _ => $"+{active.Count - 1} more",
        };
    }

    public void LoadInterrupted(IEnumerable<InterruptedJob> jobs)
    {
        Interrupted.Clear();
        foreach (var j in jobs) Interrupted.Add(new InterruptedJobViewModel(j));
        UpdateSummary();
    }

    public void RemoveFinished()
    {
        // By the job's own state, as HasFinished counts them: a row whose refresh is still queued is finished too.
        foreach (var vm in Jobs.Where(j => j.Job.State.IsFinished()).ToList())
            RetireFinishedRow(vm);
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
        Jobs.Remove(vm);
        _map.Remove(vm.Job);
        if (ReferenceEquals(Selected, vm)) Selected = null;
        Manager.Remove(vm.Job);
    }
}
