using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

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
        Percent = bt > 0 ? Math.Clamp(100.0 * bd / bt, 0, 100) : it > 0 ? Math.Clamp(100.0 * id / it, 0, 100) : IsFinished ? 100 : 0;
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
            // Only a final total supports an estimate; growing totals never fabricate an ETA (plan §9.1).
            if (j.TotalsFinal && speed > 1024 && bt > bd)
            {
                var eta = TimeSpan.FromSeconds((bt - bd) / speed);
                parts.Add(eta.TotalHours >= 1 ? $"{(int)eta.TotalHours}h {eta.Minutes}m left" : eta.TotalMinutes >= 1 ? $"{(int)eta.TotalMinutes}m {eta.Seconds}s left" : $"{eta.Seconds}s left");
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

    public IReadOnlyList<JobIssue> SelectedIssues => Selected?.Job.Issues ?? [];

    partial void OnSelectedChanged(JobViewModel? value) => OnPropertyChanged(nameof(SelectedIssues));

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
        var active = Jobs.Where(j => !j.Job.State.IsFinished()).ToList();
        ActiveCount = active.Count;
        Primary = active.FirstOrDefault(j => j.NeedsDecision) ?? active.FirstOrDefault(j => j.IsActive) ?? active.FirstOrDefault()
                  ?? Jobs.FirstOrDefault(j => j.IsFinished && j.Job.FinishedUtc > DateTime.UtcNow.AddSeconds(-20));
        HasVisibleWork = Primary is not null || Interrupted.Count > 0;
        Summary = active.Count switch
        {
            0 => Interrupted.Count > 0 ? $"{Interrupted.Count} interrupted operation(s) need attention" : string.Empty,
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
        foreach (var vm in Jobs.Where(j => j.IsFinished).ToList())
        {
            Jobs.Remove(vm);
            _map.Remove(vm.Job);
            Manager.Remove(vm.Job);
        }
        UpdateSummary();
    }
}
