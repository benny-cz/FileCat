using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

/// <summary>
/// Owns jobs independently of tabs and panels (plan §6.3). Jobs queue per device; overlapping jobs wait
/// and say which job they wait for; independent devices run in parallel within a global limit (§9.1).
/// </summary>
public sealed class JobManager
{
    private const int ExactScopeLimit = 4096;
    private const string GlobalScope = "<all locations>";
    private readonly object _lock = new();
    private readonly List<Job> _jobs = [];
    private readonly IFileSystemOperations _fs;
    private readonly ProviderRegistry _providers;
    private readonly string _journalDirectory;
    private int _order;

    public JobManager(IFileSystemOperations fs, ProviderRegistry providers, string journalDirectory)
    {
        _fs = fs;
        _providers = providers;
        _journalDirectory = journalDirectory;
    }

    public int MaxConcurrent { get; set; } = 4;

    /// <summary>Retention of finished jobs in the list (history survives in the journal).</summary>
    public int KeepFinished { get; set; } = 50;

    public IFileSystemOperations FileOperations => _fs;

    public event Action<Job>? JobAdded;
    public event Action<Job>? JobChanged;
    public event Action<Job>? JobFinished;
    public event Action<PendingDecision>? DecisionRequested;

    public IReadOnlyList<Job> Jobs
    {
        get
        {
            lock (_lock) return _jobs.ToList();
        }
    }

    public bool HasActiveWork
    {
        get
        {
            lock (_lock) return _jobs.Any(j => !j.State.IsFinished());
        }
    }

    public Job Submit(JobRequest request)
    {
        var (title, device, reads, writes) = Describe(request);
        var job = new Job(request, title, device, reads, writes);
        job.Changed += OnJobChanged;
        lock (_lock)
        {
            job.QueueOrder = ++_order;
            _jobs.Add(job);
            TrimFinishedLocked();
        }
        job.SetState(JobState.Queued);
        JobAdded?.Invoke(job);
        Schedule();
        return job;
    }

    public void Remove(Job job)
    {
        if (!job.State.IsFinished()) return;
        lock (_lock) _jobs.Remove(job);
        JobChanged?.Invoke(job);
    }

    public void ClearFinished()
    {
        lock (_lock) _jobs.RemoveAll(j => j.State.IsFinished());
    }

    public void MoveInQueue(Job job, int delta)
    {
        lock (_lock)
        {
            var queued = _jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.QueueOrder).ToList();
            int i = queued.IndexOf(job);
            int k = i + delta;
            if (i < 0 || k < 0 || k >= queued.Count) return;
            (queued[i].QueueOrder, queued[k].QueueOrder) = (queued[k].QueueOrder, queued[i].QueueOrder);
        }
        JobChanged?.Invoke(job);
        Schedule();
    }

    private void TrimFinishedLocked()
    {
        var finished = _jobs.Where(j => j.State.IsFinished()).OrderBy(j => j.FinishedUtc).ToList();
        foreach (var j in finished.Take(Math.Max(0, finished.Count - KeepFinished))) _jobs.Remove(j);
    }

    private void OnJobChanged(Job job)
    {
        JobChanged?.Invoke(job);
        if (job.State == JobState.AwaitingDecision && job.Decision is { } d) DecisionRequested?.Invoke(d);
        if (job.State == JobState.Queued) Schedule();
    }

    /// <summary>Starts queued jobs whose preconditions hold. Called after every submission and completion.</summary>
    public void Schedule()
    {
        var toStart = new List<Job>();
        var canceled = new List<Job>();
        lock (_lock)
        {
            int running = _jobs.Count(j => j.State.IsActive());
            foreach (var job in _jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.QueueOrder).ToList())
            {
                if (job.IsCancellationRequested)
                {
                    job.SetState(JobState.Canceled);
                    canceled.Add(job);
                    continue;
                }
                if (job.IsPaused)
                {
                    job.WaitReason = "Paused before start";
                    continue;
                }
                var blocker = FindBlocker(job);
                if (blocker is not null)
                {
                    job.WaitingFor = blocker;
                    job.WaitReason = $"Waits for \"{blocker.Title}\": both touch the same items";
                    continue;
                }
                if (job.Request.Mode == QueueMode.Queue)
                {
                    var devBusy = _jobs.FirstOrDefault(j => !ReferenceEquals(j, job) && j.DeviceKey == job.DeviceKey &&
                        (j.State.IsActive() || j.State == JobState.Queued && j.QueueOrder < job.QueueOrder));
                    if (devBusy is not null)
                    {
                        job.WaitingFor = devBusy;
                        job.WaitReason = $"Queued behind \"{devBusy.Title}\" on the same device";
                        continue;
                    }
                }
                if (running >= MaxConcurrent)
                {
                    job.WaitingFor = null;
                    job.WaitReason = "Waiting for a free slot";
                    continue;
                }
                job.WaitingFor = null;
                job.WaitReason = null;
                running++;
                toStart.Add(job);
            }
        }
        foreach (var job in canceled) JobFinished?.Invoke(job);
        foreach (var job in toStart) Start(job);
    }

    private Job? FindBlocker(Job job)
    {
        foreach (var other in _jobs)
        {
            if (ReferenceEquals(other, job) || other.State.IsFinished()) continue;
            bool ahead = other.State.IsActive() || other.State == JobState.Queued && other.QueueOrder < job.QueueOrder;
            if (!ahead) continue;
            if (Overlaps(job, other)) return other;
        }
        return null;
    }

    /// <summary>Two jobs overlap when one writes where the other reads or writes (subtree-aware, conservative).</summary>
    public static bool Overlaps(Job a, Job b)
    {
        foreach (var w in a.WriteSet)
        {
            foreach (var x in b.WriteSet) if (ScopesOverlap(w, x)) return true;
            foreach (var x in b.ReadSet) if (ScopesOverlap(w, x)) return true;
        }
        foreach (var r in a.ReadSet)
        {
            foreach (var x in b.WriteSet) if (ScopesOverlap(r, x)) return true;
        }
        return false;
    }

    private static bool ScopesOverlap(string a, string b) =>
        a == GlobalScope || b == GlobalScope || PathUtil.SubtreesOverlap(a, b);

    private void Start(Job job)
    {
        job.SetState(JobState.Running);
        var thread = new Thread(() => Run(job)) { IsBackground = true, Name = "FileCat job " + job.ShortId };
        thread.Start();
    }

    private void Run(Job job)
    {
        JobJournal? journal = null;
        JobState final;
        try
        {
            journal = JobJournal.Create(_journalDirectory, job);
            var executor = JobExecutors.Create(job, _fs, _providers, journal);
            executor.Execute();
            job.TotalsFinal = true;
            final = FinalState(job);
        }
        catch (OperationCanceledException)
        {
            final = JobState.Canceled;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Job {job.ShortId} failed", ex);
            job.AddIssue(new JobIssue(IssueSeverity.Error, string.Empty, ex.Message, StepOutcome.Failed));
            final = JobState.Failed;
        }
        if (job.IsCancellationRequested && final != JobState.Failed) final = JobState.Canceled;
        try { journal?.Finish(final, job.Summary); }
        catch (IOException ex) { AppLog.Warn("Journal finish failed", ex); }
        journal?.Dispose();
        job.SetState(final);
        JobFinished?.Invoke(job);
        Schedule();
    }

    private static JobState FinalState(Job job)
    {
        if (job.IsCancellationRequested) return JobState.Canceled;
        bool anyFailed = job.ItemsFailed > 0 || job.Issues.Any(i => i.Severity == IssueSeverity.Error);
        bool anyDone = job.ItemsDone > 0;
        if (anyFailed && !anyDone && job.ItemsSkipped == 0) return JobState.Failed;
        if (anyFailed || job.ItemsSkipped > 0 || job.Issues.Any(i => i.Severity == IssueSeverity.Warning)) return JobState.CompletedWithIssues;
        return JobState.Completed;
    }

    // ---- Descriptions and read/write sets ----------------------------------------------------------------

    internal (string Title, string Device, IReadOnlyList<string> Reads, IReadOnlyList<string> Writes) Describe(JobRequest r)
    {
        string What() => r.Sources.Count == 1 ? $"\"{r.Sources[0].Name}\"" : $"{r.Sources.Count:N0} items";
        string Dest() => r.Destination is null ? string.Empty : _providers.TryGet(r.Destination.Scheme, out var p) && p is not null ? p.GetDisplayPath(r.Destination) : r.Destination.Path;
        var reads = new List<string>();
        var writes = new List<string>();
        bool large = r.Sources.Count > ExactScopeLimit;
        string P(ItemRef i) => i.FileSystemPath ?? i.Parent + "/" + i.Name;
        void AddSourceScopes(List<string> destination)
        {
            if (!large) { destination.AddRange(r.Sources.Select(P)); return; }
            // Huge selections are scoped by their parent folders; a captured listing answers without enumerating.
            var parents = Listing.ItemSources.Parents(r.Sources, 128);
            if (parents is null || parents.Count == 0 || parents.Any(p => !p.IsFileSystem)) destination.Add(GlobalScope);
            else destination.AddRange(parents.Select(p => p.Path).Distinct(PathUtil.SafetyComparer));
        }
        void AddDestinationScopes()
        {
            if (large) writes.Add(r.Destination is { IsFileSystem: true } dest ? dest.Path : GlobalScope);
            else writes.AddRange(r.Sources.Select(s => D(r.Sources.Count == 1 && r.NewName is not null ? r.NewName : s.Name)));
        }
        string D(string name) => r.Destination is { IsFileSystem: true } d ? Path.Join(d.Path, name) : (r.Destination?.ToString() ?? "") + "/" + name;
        string title;
        switch (r.Kind)
        {
            case JobKind.Copy:
            case JobKind.Extract:
                title = $"{(r.Kind == JobKind.Copy ? "Copy" : "Extract")} {What()} to {Dest()}";
                AddSourceScopes(reads);
                AddDestinationScopes();
                break;
            case JobKind.Move:
                title = $"Move {What()} to {Dest()}";
                AddSourceScopes(reads);
                AddSourceScopes(writes);
                AddDestinationScopes();
                break;
            case JobKind.Recycle:
                title = $"Move {What()} to the Recycle Bin";
                AddSourceScopes(writes);
                break;
            case JobKind.Delete:
                title = $"Delete {What()} permanently";
                AddSourceScopes(writes);
                break;
            case JobKind.CreateDirectory:
                title = $"Create folder \"{r.NewName}\"";
                writes.Add(D(r.NewName ?? ""));
                break;
            case JobKind.CreateFile:
                title = $"Create file \"{r.NewName}\"";
                writes.Add(D(r.NewName ?? ""));
                break;
            case JobKind.Attributes:
                title = $"Change attributes of {What()}";
                AddSourceScopes(writes);
                break;
            case JobKind.ApplyCommand:
                title = r.Description ?? $"Run a command for {What()}";
                // The program may change the items and write next to them.
                AddSourceScopes(reads);
                writes.AddRange(r.Sources.Select(s => s.FileSystemPath is { } ap ? Path.GetDirectoryName(ap) ?? ap : GlobalScope).Distinct(PathUtil.SafetyComparer).Take(256));
                break;
            case JobKind.VerifyChecksums:
                title = r.Description ?? (r.Sources.Count == 1 ? $"Verify the checksums in {What()}" : $"Verify the checksums in {r.Sources.Count:N0} manifests");
                // The listed files lie in and below each manifest's folder.
                reads.AddRange(r.Sources.Select(s => s.FileSystemPath is { } mp ? Path.GetDirectoryName(mp) ?? mp : GlobalScope).Distinct(PathUtil.SafetyComparer));
                break;
            case JobKind.CreateLink:
            {
                string kind = r.Link is { } lo ? Operations.LinkPlanner.KindName(lo.Kind) : "link";
                title = r.Description ?? (r.Sources.Count == 1 ? $"Create a {kind} to {What()} in {Dest()}" : $"Create {kind}s to {What()} in {Dest()}");
                AddSourceScopes(reads);
                if (large) writes.Add(r.Destination is { IsFileSystem: true } ld ? ld.Path : GlobalScope);
                else
                {
                    for (int i = 0; i < r.Sources.Count; i++) writes.Add(D(r.NewNames is { } ln && i < ln.Count ? ln[i] : r.Sources[i].Name));
                }
                break;
            }
            case JobKind.Rename when r.NewNames is { } newNames:
                title = r.Description ?? $"Rename {What()}";
                AddSourceScopes(writes);
                for (int i = 0; i < Math.Min(newNames.Count, r.Sources.Count) && !large; i++)
                    if (r.Sources[i].FileSystemPath is { } bp) writes.Add(Path.Join(Path.GetDirectoryName(bp), newNames[i]));
                break;
            case JobKind.Rename:
                title = $"Rename \"{r.Sources[0].Name}\" to \"{r.NewName}\"";
                writes.Add(P(r.Sources[0]));
                if (r.Sources[0].FileSystemPath is { } rp) writes.Add(Path.Join(Path.GetDirectoryName(rp), r.NewName));
                break;
            case JobKind.ArchiveUpdate:
            {
                var archive = r.Archive ?? throw new ArgumentException("An archive plan is required.", nameof(r));
                title = r.Description ?? $"Update \"{Path.GetFileName(archive.ArchivePath)}\"";
                foreach (var source in archive.Changes.Select(c => c.SourcePath).OfType<string>()) reads.Add(source);
                // The archive is rewritten as a whole: any job reading or writing it overlaps.
                writes.Add(Path.GetFullPath(archive.ArchivePath));
                break;
            }
            case JobKind.ArchiveTest:
                title = r.Description ?? $"Test {What()}";
                AddSourceScopes(reads);
                break;
            case JobKind.Elevated:
                // Sources and destination are the original (display) locations, so overlap checks see the same
                // paths as ordinary jobs; the plan itself carries the volume-GUID paths the broker runs.
                title = r.Description ?? r.Elevation?.Title ?? "Run as administrator";
                AddSourceScopes(writes);
                if (r.Destination is not null) AddDestinationScopes();
                foreach (var step in r.Elevation?.Steps ?? [])
                    if (step.Registry is { } reg) writes.Add("reg:" + reg.KeyPath + "/" + reg.Name);
                break;
            case JobKind.Registry:
                title = r.Description ?? "Change Registry data";
                var registryChanges = r.RegistryChanges.Count > 0 ? r.RegistryChanges :
                    r.Registry is { } single ? [single] : throw new ArgumentException("Registry change is required.", nameof(r));
                foreach (var change in registryChanges)
                {
                    writes.Add(change.Key.ToString() + "/" + change.Name);
                    if (change.TargetKey is { } target) writes.Add(target.ToString() + "/" + (change.TargetName ?? change.Name));
                }
                break;
            default:
                title = r.Description ?? r.Kind.ToString();
                AddSourceScopes(reads);
                break;
        }
        var deviceLoc = r.Kind is JobKind.Copy or JobKind.Move or JobKind.Extract or JobKind.CreateDirectory or JobKind.CreateFile
            ? r.Destination
            : r.Kind == JobKind.ArchiveUpdate && r.Archive is { } plan ? Location.FileSystem(Path.GetDirectoryName(Path.GetFullPath(plan.ArchivePath)) ?? plan.ArchivePath)
            : r.Kind == JobKind.Registry ? r.Registry?.Key ?? r.RegistryChanges.FirstOrDefault()?.Key : r.Sources.FirstOrDefault()?.Parent;
        string device = deviceLoc is not null && _providers.TryGet(deviceLoc.Scheme, out var prov) && prov is not null ? prov.GetDeviceKey(deviceLoc) : "local";
        return (r.Description ?? title, device, reads, writes);
    }
}
